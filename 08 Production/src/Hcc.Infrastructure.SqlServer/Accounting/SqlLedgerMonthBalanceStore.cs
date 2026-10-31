using System.Data;
using Hcc.Application.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// ACC-2 parity (contract §8; migration 4257): the month rows of the aggregated opening read. A closed month whose every day with sources has a
/// current period lock is summed once from the lock snapshots (<c>acc.locked_entry</c> of the current <c>acc.period_lock</c> rows) into
/// <c>acc.ledger_month_balance</c>, with the locks' fingerprint in <c>acc.ledger_month</c>; an unlock / re-lock changes the fingerprint and the
/// next read rebuilds that month. Building is lazy (on the first report that needs the month) and idempotent (application lock per property,
/// delete + insert in one transaction). <see cref="LedgerMonthBalances.Plan"/> decides which months are used and which are (re)built.
/// </summary>
public sealed class SqlLedgerMonthBalanceStore(SqlConnectionFactory connections) : ILedgerMonthBalanceStore
{
    private const string MonthOf = "DATEFROMPARTS(YEAR(business_date), MONTH(business_date), 1)";

    public async Task<MonthOpening> OpeningAsync(int propertyId, DateOnly from, DateOnly businessDate, DateTime nowUtc, CancellationToken ct = default)
    {
        var limit = LedgerMonthBalances.Limit(from, businessDate);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        var first = await FirstActivityAsync(conn, null, propertyId, ct).ConfigureAwait(false);
        if (first is null || first >= limit) return new MonthOpening(limit, [], 0);
        var unlocked = await UnlockedMonthsAsync(conn, null, propertyId, limit, ct).ConfigureAwait(false);
        var current = await PrintsAsync(conn, null, propertyId, limit, null, ct).ConfigureAwait(false);
        var stored = await StoredAsync(conn, propertyId, limit, ct).ConfigureAwait(false);
        var (cut, build) = LedgerMonthBalances.Plan(first, limit, unlocked, current, stored);
        foreach (var m in build)
            if (!await BuildAsync(conn, propertyId, m, nowUtc, ct).ConfigureAwait(false))
            {
                cut = m; // a day of it gained sources without a lock meanwhile: the read takes it (and what follows) day by day
                break;
            }

        var entries = new List<VoucherEntry>();
        await using (var cmd = connections.Command("""
            SELECT account, SUM(debit_paise), SUM(credit_paise) FROM acc.ledger_month_balance
            WHERE property_id = @p AND month_start < @cut GROUP BY account ORDER BY account;
            """, conn, null))
        {
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            cmd.Parameters.Add("@cut", SqlDbType.Date).Value = cut.ToDateTime(TimeOnly.MinValue);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                if (rd.GetInt64(1) != 0 || rd.GetInt64(2) != 0) entries.Add(new VoucherEntry(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2)));
        }

        var months = first.Value >= cut ? 0 : (cut.Year - first.Value.Year) * 12 + cut.Month - first.Value.Month;
        return new MonthOpening(cut, entries, months);
    }

    /// <summary>(Re)builds one month inside its own transaction: re-reads the fingerprint under the property's lock, then replaces the rows.</summary>
    private async Task<bool> BuildAsync(SqlConnection conn, int propertyId, DateOnly month, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var lk = connections.Command("EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;", conn, tx))
        {
            lk.Parameters.Add("@r", SqlDbType.NVarChar, 255).Value = "acc:month-balance:" + propertyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await lk.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var next = month.AddMonths(1);
        // a day of the month that gained sources without a lock since the plan: leave the month unstored (the read takes it day by day)
        if ((await UnlockedMonthsAsync(conn, tx, propertyId, next, ct).ConfigureAwait(false)).Contains(month))
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        var print = (await PrintsAsync(conn, tx, propertyId, next, month, ct).ConfigureAwait(false)).GetValueOrDefault(month);
        await using (var cmd = connections.Command("""
            DELETE acc.ledger_month WHERE property_id = @p AND month_start = @m;
            INSERT acc.ledger_month(property_id, month_start, lock_count, lock_sum, debit_paise, credit_paise, built_utc)
            SELECT @p, @m, @n, @s, ISNULL(SUM(e.debit_paise), 0), ISNULL(SUM(e.credit_paise), 0), @now
            FROM acc.period_lock l WITH (HOLDLOCK) JOIN acc.locked_entry e ON e.lock_id = l.lock_id
            WHERE l.property_id = @p AND l.business_date >= @m AND l.business_date < @next AND l.unlocked_utc IS NULL;
            INSERT acc.ledger_month_balance(property_id, month_start, account, debit_paise, credit_paise)
            SELECT @p, @m, e.account_code, SUM(e.debit_paise), SUM(e.credit_paise)
            FROM acc.period_lock l WITH (HOLDLOCK) JOIN acc.locked_entry e ON e.lock_id = l.lock_id
            WHERE l.property_id = @p AND l.business_date >= @m AND l.business_date < @next AND l.unlocked_utc IS NULL
            GROUP BY e.account_code;
            """, conn, tx))
        {
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            cmd.Parameters.Add("@m", SqlDbType.Date).Value = month.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@next", SqlDbType.Date).Value = next.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@n", SqlDbType.Int).Value = print.Count;
            cmd.Parameters.Add("@s", SqlDbType.BigInt).Value = print.Sum;
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = nowUtc;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<DateOnly?> FirstActivityAsync(SqlConnection conn, SqlTransaction? tx, int propertyId, CancellationToken ct)
    {
        await using var cmd = connections.Command("""
            SELECT MIN(d) FROM (SELECT MIN(business_date) AS d FROM acc.source WHERE property_id = @p
                                UNION ALL SELECT MIN(business_date) FROM acc.period_lock WHERE property_id = @p AND unlocked_utc IS NULL) x;
            """, conn, tx);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is DateTime d ? DateOnly.FromDateTime(d) : null;
    }

    /// <summary>Months (before <paramref name="before"/>) with a day that has sources but no current period lock.</summary>
    private async Task<HashSet<DateOnly>> UnlockedMonthsAsync(SqlConnection conn, SqlTransaction? tx, int propertyId, DateOnly before, CancellationToken ct)
    {
        await using var cmd = connections.Command($"""
            SELECT DISTINCT {MonthOf} FROM acc.source s
            WHERE s.property_id = @p AND s.business_date < @b
              AND NOT EXISTS (SELECT 1 FROM acc.period_lock l WHERE l.property_id = @p AND l.business_date = s.business_date AND l.unlocked_utc IS NULL);
            """, conn, tx);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@b", SqlDbType.Date).Value = before.ToDateTime(TimeOnly.MinValue);
        var set = new HashSet<DateOnly>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(DateOnly.FromDateTime(rd.GetDateTime(0)));
        return set;
    }

    /// <summary>Fingerprint of the current period locks per month (before <paramref name="before"/>; one month only when <paramref name="only"/> is set).</summary>
    private async Task<Dictionary<DateOnly, MonthLockPrint>> PrintsAsync(SqlConnection conn, SqlTransaction? tx, int propertyId, DateOnly before, DateOnly? only, CancellationToken ct)
    {
        await using var cmd = connections.Command($"""
            SELECT {MonthOf}, COUNT(*), SUM(lock_id) FROM acc.period_lock {(tx is null ? "" : "WITH (HOLDLOCK)")}
            WHERE property_id = @p AND business_date < @b AND business_date >= @a AND unlocked_utc IS NULL GROUP BY {MonthOf};
            """, conn, tx);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@b", SqlDbType.Date).Value = before.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@a", SqlDbType.Date).Value = (only ?? new DateOnly(1900, 1, 1)).ToDateTime(TimeOnly.MinValue);
        var map = new Dictionary<DateOnly, MonthLockPrint>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[DateOnly.FromDateTime(rd.GetDateTime(0))] = new MonthLockPrint(rd.GetInt32(1), rd.GetInt64(2));
        return map;
    }

    private async Task<Dictionary<DateOnly, MonthLockPrint>> StoredAsync(SqlConnection conn, int propertyId, DateOnly before, CancellationToken ct)
    {
        await using var cmd = connections.Command("SELECT month_start, lock_count, lock_sum FROM acc.ledger_month WHERE property_id = @p AND month_start < @b;", conn, null);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@b", SqlDbType.Date).Value = before.ToDateTime(TimeOnly.MinValue);
        var map = new Dictionary<DateOnly, MonthLockPrint>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[DateOnly.FromDateTime(rd.GetDateTime(0))] = new MonthLockPrint(rd.GetInt32(1), rd.GetInt64(2));
        return map;
    }
}
