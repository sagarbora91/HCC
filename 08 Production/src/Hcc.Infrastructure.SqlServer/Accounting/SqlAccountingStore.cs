using System.Data;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

internal static class AccSql
{
    public static void Set(this SqlParameterCollection ps, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? ps.Add(name, type, size) : ps.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    public static DateOnly Date(SqlDataReader rd, int i) => DateOnly.FromDateTime(rd.GetDateTime(i));

    public static string? StrOrNull(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    public static VoucherType Type(string s) => Enum.Parse<VoucherType>(s);
}

/// <summary>
/// SQL Server implementation of <see cref="IAccountingStore"/> (migration 1000). Reads of billing/payments tables are read-only
/// (reconciliation figures, pending sweep). Commands serialize per period / mapping with a transaction-owned application lock.
/// </summary>
public sealed class SqlAccountingStore(SqlConnectionFactory connections) : IAccountingStore
{
    private async Task<T> WithAsync<T>(ICommandTransaction? tx, Func<SqlConnection, SqlTransaction?, Task<T>> f, CancellationToken ct)
    {
        if (tx is not null)
        {
            var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The accounting store needs a SQL command transaction.");
            return await f(s.Connection, s.Transaction).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn, null).ConfigureAwait(false);
    }

    private SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction? t) => connections.Command(sql, c, t);

    public async Task SerializeAsync(ICommandTransaction tx, string resource, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;", c, t);
            cmd.Parameters.Set("@res", SqlDbType.NVarChar, resource, 255);
            var r = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
            if (r < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "Another accounting change is in progress. Try again.");
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AccountingSource>> SourcesAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
        => WithAsync<IReadOnlyList<AccountingSource>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT s.source_id, s.source_key, s.source_kind, s.sub, s.voucher_type, s.business_date, s.narration, e.map_key, e.debit_paise, e.credit_paise, e.party_id
                FROM acc.source s JOIN acc.source_entry e ON e.source_id = s.source_id
                WHERE s.property_id = @p AND s.business_date BETWEEN @f AND @t
                ORDER BY s.business_date, s.source_id, e.line_no;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.Set("@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
            var list = new List<AccountingSource>();
            long current = -1;
            List<SourceEntry>? entries = null;
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = rd.GetInt64(0);
                if (id != current)
                {
                    current = id;
                    entries = [];
                    list.Add(new AccountingSource(rd.GetString(1), rd.GetString(2), rd.GetString(3), AccSql.Date(rd, 5), AccSql.Type(rd.GetString(4)), rd.GetString(6), entries));
                }

                entries!.Add(new SourceEntry(rd.GetString(7), rd.GetInt64(8), rd.GetInt64(9), rd.IsDBNull(10) ? null : rd.GetString(10))); // ACC-1: party_id (migration 4210)
            }

            return list;
        }, ct);

    public Task<IReadOnlyDictionary<string, string>> MappingOverridesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyDictionary<string, string>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT map_key, account_code FROM acc.mapping WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) d[rd.GetString(0)] = rd.GetString(1);
            return d;
        }, ct);

    public Task<IReadOnlyDictionary<string, string>> MasterLedgersAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyDictionary<string, string>>(tx, async (c, t) =>
        {
            // MA-11: the ledger column of the payment methods (2830; a method's money lands in it, and an expense funded by it is credited
            // to it) and of the expense categories. Earlier names keep the mapping they had (defaults / overrides).
            await using var cmd = Cmd("""
                SELECT CONCAT('method:', name), ledger FROM mst.payment_method WHERE property_id = @p AND ledger IS NOT NULL
                UNION ALL SELECT CONCAT('funding:', name), ledger FROM mst.payment_method WHERE property_id = @p AND ledger IS NOT NULL
                UNION ALL SELECT CONCAT('expense:', name), ledger FROM ops.expense_category WHERE property_id = @p AND ledger IS NOT NULL;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) d[rd.GetString(0)] = rd.GetString(1).TrimEnd();
            return d;
        }, ct);

    public Task<IReadOnlyDictionary<string, int>> KeyUsesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyDictionary<string, int>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT e.map_key, COUNT(DISTINCT e.source_id) FROM acc.source_entry e JOIN acc.source s ON s.source_id = e.source_id
                WHERE s.property_id = @p GROUP BY e.map_key;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) d[rd.GetString(0)] = rd.GetInt32(1);
            return d;
        }, ct);

    public async Task WriteMappingAsync(ICommandTransaction tx, int propertyId, string key, string? account, string? before, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                IF @a IS NULL
                  DELETE acc.mapping WHERE property_id = @p AND map_key = @k;
                ELSE IF EXISTS (SELECT 1 FROM acc.mapping WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND map_key = @k)
                  UPDATE acc.mapping SET account_code = @a, changed_by = @u, changed_utc = @now WHERE property_id = @p AND map_key = @k;
                ELSE
                  INSERT acc.mapping(property_id, map_key, account_code, changed_by, changed_utc) VALUES (@p, @k, @a, @u, @now);
                INSERT acc.mapping_change(property_id, map_key, before_code, after_code, actor, at_utc)
                VALUES (@p, @k, @b, ISNULL(@a, @def), @u, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@k", SqlDbType.VarChar, key, 80);
            cmd.Parameters.Set("@a", SqlDbType.VarChar, account, 4);
            cmd.Parameters.Set("@b", SqlDbType.VarChar, before, 4);
            cmd.Parameters.Set("@def", SqlDbType.VarChar, ChartOfAccounts.DefaultMapping.GetValueOrDefault(key), 4);
            cmd.Parameters.Set("@u", SqlDbType.Int, tx.Context.PrincipalId);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MappingChange>> MappingHistoryAsync(int propertyId, CancellationToken ct)
        => await WithAsync<IReadOnlyList<MappingChange>>(null, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT change_id, at_utc, actor, map_key, before_code, after_code FROM acc.mapping_change WHERE property_id = @p ORDER BY change_id;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<MappingChange>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new MappingChange(rd.GetInt64(0), DateTime.SpecifyKind(rd.GetDateTime(1), DateTimeKind.Utc), rd.GetInt32(2), rd.GetString(3), AccSql.StrOrNull(rd, 4), AccSql.StrOrNull(rd, 5)));
            return list;
        }, ct).ConfigureAwait(false);

    public async Task<PeriodLock?> CurrentLockAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct)
        => (await LocksAsync(tx, propertyId, date, date, ct).ConfigureAwait(false)).FirstOrDefault();

    public Task<IReadOnlyList<PeriodLock>> LocksAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
        => WithAsync<IReadOnlyList<PeriodLock>>(tx, async (c, t) =>
        {
            var heads = new List<(long Id, DateOnly Date, int By, DateTime At, int Count, long D, long C, string Ver)>();
            await using (var cmd = Cmd($"""
                SELECT lock_id, business_date, locked_by, locked_utc, voucher_count, debit_paise, credit_paise, row_ver
                FROM acc.period_lock {(t is null ? "" : "WITH (UPDLOCK, HOLDLOCK)")}
                WHERE property_id = @p AND business_date BETWEEN @f AND @t AND unlocked_utc IS NULL ORDER BY business_date;
                """, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                cmd.Parameters.Set("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.Set("@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    heads.Add((rd.GetInt64(0), AccSql.Date(rd, 1), rd.GetInt32(2), DateTime.SpecifyKind(rd.GetDateTime(3), DateTimeKind.Utc), rd.GetInt32(4),
                        rd.GetInt64(5), rd.GetInt64(6), SqlValues.RowVersionToWire((byte[])rd[7])));
            }

            var result = new List<PeriodLock>();
            foreach (var h in heads)
            {
                var vouchers = new List<Voucher>();
                await using var cmd = Cmd("""
                    SELECT v.seq, v.voucher_no, v.voucher_type, v.narration, v.source_key, v.source_kind, v.sub, e.account_code, e.debit_paise, e.credit_paise
                    FROM acc.locked_voucher v JOIN acc.locked_entry e ON e.lock_id = v.lock_id AND e.seq = v.seq
                    WHERE v.lock_id = @l ORDER BY v.seq, e.line_no;
                    """, c, t);
                cmd.Parameters.Set("@l", SqlDbType.BigInt, h.Id);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                var seq = -1;
                List<VoucherEntry>? entries = null;
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (rd.GetInt32(0) != seq)
                    {
                        seq = rd.GetInt32(0);
                        entries = [];
                        vouchers.Add(new Voucher(rd.GetString(1), h.Date, AccSql.Type(rd.GetString(2)), rd.GetString(3), rd.GetString(4), rd.GetString(5), rd.GetString(6), entries, []));
                    }

                    entries!.Add(new VoucherEntry(rd.GetString(7), rd.GetInt64(8), rd.GetInt64(9)));
                }

                result.Add(new PeriodLock(h.Id, propertyId, h.Date, h.By, h.At, h.Count, h.D, h.C, h.Ver, vouchers));
            }

            return result;
        }, ct);

    public async Task<PeriodLock> InsertLockAsync(ICommandTransaction tx, int propertyId, DateOnly date, IReadOnlyList<Voucher> vouchers, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(vouchers);
        var (debit, credit) = VoucherBuilder.Totals(vouchers);
        return await WithAsync(tx, async (c, t) =>
        {
            long id;
            string version;
            await using (var cmd = Cmd("""
                INSERT acc.period_lock(property_id, business_date, voucher_count, debit_paise, credit_paise, locked_by, locked_utc)
                OUTPUT INSERTED.lock_id, INSERTED.row_ver VALUES (@p, @d, @n, @dr, @cr, @u, @now);
                """, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.Set("@n", SqlDbType.Int, vouchers.Count);
                cmd.Parameters.Set("@dr", SqlDbType.BigInt, debit);
                cmd.Parameters.Set("@cr", SqlDbType.BigInt, credit);
                cmd.Parameters.Set("@u", SqlDbType.Int, tx.Context.PrincipalId);
                cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await rd.ReadAsync(ct).ConfigureAwait(false);
                id = rd.GetInt64(0);
                version = SqlValues.RowVersionToWire((byte[])rd[1]);
            }

            for (var i = 0; i < vouchers.Count; i++)
            {
                var v = vouchers[i];
                await using (var cmd = Cmd("""
                    INSERT acc.locked_voucher(lock_id, seq, voucher_no, voucher_type, narration, source_key, source_kind, sub) VALUES (@l, @s, @no, @ty, @na, @k, @kind, @sub);
                    """, c, t))
                {
                    cmd.Parameters.Set("@l", SqlDbType.BigInt, id);
                    cmd.Parameters.Set("@s", SqlDbType.Int, i);
                    cmd.Parameters.Set("@no", SqlDbType.VarChar, v.Number, 20);
                    cmd.Parameters.Set("@ty", SqlDbType.VarChar, v.Type.ToString(), 8);
                    cmd.Parameters.Set("@na", SqlDbType.NVarChar, v.Narration, 400);
                    cmd.Parameters.Set("@k", SqlDbType.VarChar, v.SourceKey, 80);
                    cmd.Parameters.Set("@kind", SqlDbType.VarChar, v.SourceKind, 12);
                    cmd.Parameters.Set("@sub", SqlDbType.VarChar, v.Sub, 30);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                for (var j = 0; j < v.Entries.Count; j++)
                {
                    var e = v.Entries[j];
                    await using var cmd = Cmd("INSERT acc.locked_entry(lock_id, seq, line_no, account_code, debit_paise, credit_paise) VALUES (@l, @s, @j, @a, @dr, @cr);", c, t);
                    cmd.Parameters.Set("@l", SqlDbType.BigInt, id);
                    cmd.Parameters.Set("@s", SqlDbType.Int, i);
                    cmd.Parameters.Set("@j", SqlDbType.TinyInt, (byte)j);
                    cmd.Parameters.Set("@a", SqlDbType.VarChar, e.Account, PartyLedgers.MaxAccountLength); // ACC-1: party ledger ids (migration 4211)
                    cmd.Parameters.Set("@dr", SqlDbType.BigInt, e.DebitPaise);
                    cmd.Parameters.Set("@cr", SqlDbType.BigInt, e.CreditPaise);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            return new PeriodLock(id, propertyId, date, tx.Context.PrincipalId, now, vouchers.Count, debit, credit, version, vouchers);
        }, ct).ConfigureAwait(false);
    }

    public async Task UnlockAsync(ICommandTransaction tx, long lockId, string expectedVersion, string reason, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var version = SqlValues.RowVersionFromWire(expectedVersion);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                UPDATE acc.period_lock SET unlocked_by = @u, unlocked_utc = @now, unlock_reason = @r
                WHERE lock_id = @l AND row_ver = @v AND unlocked_utc IS NULL;
                """, c, t);
            cmd.Parameters.Set("@u", SqlDbType.Int, tx.Context.PrincipalId);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            cmd.Parameters.Set("@r", SqlDbType.NVarChar, reason, 200);
            cmd.Parameters.Set("@l", SqlDbType.BigInt, lockId);
            cmd.Parameters.Set("@v", SqlDbType.Binary, version, 8);
            SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The period lock");
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LockEvent>> LockHistoryAsync(int propertyId, CancellationToken ct)
        => await WithAsync<IReadOnlyList<LockEvent>>(null, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT lock_id, business_date, 'lock', locked_utc, locked_by, NULL FROM acc.period_lock WHERE property_id = @p
                UNION ALL
                SELECT lock_id, business_date, 'unlock', unlocked_utc, unlocked_by, unlock_reason FROM acc.period_lock WHERE property_id = @p AND unlocked_utc IS NOT NULL
                ORDER BY 4, 1, 3;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<LockEvent>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new LockEvent(rd.GetInt64(0), AccSql.Date(rd, 1), rd.GetString(2), DateTime.SpecifyKind(rd.GetDateTime(3), DateTimeKind.Utc), rd.GetInt32(4), AccSql.StrOrNull(rd, 5)));
            return list;
        }, ct).ConfigureAwait(false);

    public Task<DateOnly?> BusinessDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<DateOnly?>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT business_date FROM inv.property WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : null;
        }, ct);

    public Task<PendingEvents> PendingEventsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd(AccountingVoucherProjector.PendingSql, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@c", SqlDbType.VarChar, AccountingVoucherProjector.ConsumerName, 40);
            cmd.Parameters.Set("@types", SqlDbType.NVarChar, System.Text.Json.JsonSerializer.Serialize(AccountingEvents.Consumed));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            return new PendingEvents(rd.GetInt32(0), rd.GetInt32(1));
        }, ct);

    public Task<OperationalFigures> OperationalFiguresAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT ISNULL(SUM(CASE WHEN c.kind = 'room' THEN c.amount_paise END), 0), ISNULL(SUM(CASE WHEN c.kind = 'service' THEN c.amount_paise END), 0),
                       ISNULL(SUM(CASE WHEN c.kind = 'discount' THEN c.amount_paise END), 0), ISNULL(SUM(CASE WHEN c.kind = 'correction' THEN c.amount_paise END), 0),
                       ISNULL(SUM(c.tax_paise), 0)
                FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
                WHERE r.property_id = @p AND c.service_date = @d;
                SELECT ISNULL(SUM(CASE x.kind WHEN 'payment' THEN x.amount_paise WHEN 'refund' THEN -x.amount_paise END), 0),
                       ISNULL(SUM(CASE x.kind WHEN 'deposit' THEN x.amount_paise WHEN 'deposit-return' THEN -x.amount_paise END), 0),
                       ISNULL(SUM(CASE x.kind WHEN 'company-credit' THEN x.amount_paise END), 0)
                FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
                WHERE r.property_id = @p AND x.business_date = @d;
                -- DEC-074 (wave B3): group master folios — attrition net and GST, receipts in − out (posted on that business date).
                SELECT ISNULL(SUM(CASE WHEN e.kind = 'attrition' THEN e.amount_paise END), 0), ISNULL(SUM(CASE WHEN e.kind = 'attrition' THEN e.tax_paise END), 0),
                       ISNULL(SUM(CASE e.kind WHEN 'payment' THEN e.amount_paise WHEN 'deposit' THEN e.amount_paise WHEN 'refund' THEN -e.amount_paise END), 0)
                FROM grp.master_entry e JOIN grp.group_account g ON g.group_id = e.group_id
                WHERE g.property_id = @p AND e.business_date = @d;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            var (room, service, discount, adjustment, tax) = (rd.GetInt64(0), rd.GetInt64(1), rd.GetInt64(2), rd.GetInt64(3), rd.GetInt64(4));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            var (receipts, deposits, credit) = (rd.GetInt64(0), rd.GetInt64(1), rd.GetInt64(2));
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            return new OperationalFigures(room, service, discount, adjustment, tax, receipts, deposits, credit, rd.GetInt64(0), rd.GetInt64(1), rd.GetInt64(2));
        }, ct);

    public Task<IReadOnlyList<DateOnly>> ActivityDatesAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
        => WithAsync<IReadOnlyList<DateOnly>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT business_date FROM acc.source WHERE property_id = @p AND business_date BETWEEN @f AND @t
                UNION SELECT business_date FROM acc.period_lock WHERE property_id = @p AND business_date BETWEEN @f AND @t AND unlocked_utc IS NULL
                ORDER BY 1;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.Set("@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue));
            var list = new List<DateOnly>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(AccSql.Date(rd, 0));
            return list;
        }, ct);

    // ── DEC-077: reopened business day (cls.day_reopen, read only) and the Tally export log (migration 3601) ──

    public Task<DateOnly?> ReopenedDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<DateOnly?>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT business_date FROM cls.day_reopen WHERE property_id = @p AND reclosed_utc IS NULL;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : null;
        }, ct);

    public Task<bool> TallyExportedAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT CASE WHEN EXISTS (SELECT 1 FROM acc.tally_export_day WHERE property_id = @p AND business_date = @d) THEN 1 ELSE 0 END;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }, ct);

    public Task<bool> FlagTallyReexportAsync(ICommandTransaction tx, int propertyId, DateOnly date, long? reopenId, string reason, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                DECLARE @exported BIT = CASE WHEN EXISTS (SELECT 1 FROM acc.tally_export_day WHERE property_id = @p AND business_date = @d) THEN 1 ELSE 0 END;
                IF @exported = 1 AND NOT EXISTS (SELECT 1 FROM acc.tally_reexport WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND business_date = @d AND cleared_export_id IS NULL)
                  INSERT acc.tally_reexport(property_id, business_date, reopen_id, reason, marked_utc) VALUES (@p, @d, @r, @why, @now);
                SELECT @exported;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.Set("@r", SqlDbType.BigInt, reopenId);
            cmd.Parameters.Set("@why", SqlDbType.NVarChar, reason, 200);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
        }, ct);
    }

    public Task<IReadOnlyList<TallyReexportFlag>> TallyReexportsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyList<TallyReexportFlag>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT reexport_id, business_date, reopen_id, reason, marked_utc FROM acc.tally_reexport
                WHERE property_id = @p AND cleared_export_id IS NULL ORDER BY business_date;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<TallyReexportFlag>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new TallyReexportFlag(rd.GetInt64(0), AccSql.Date(rd, 1), rd.IsDBNull(2) ? null : rd.GetInt64(2), rd.GetString(3), DateTime.SpecifyKind(rd.GetDateTime(4), DateTimeKind.Utc)));
            return list;
        }, ct);

    public async Task<long> RecordTallyExportAsync(ICommandTransaction tx, NewTallyExport export, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(export);
        return await WithAsync(tx, async (c, t) =>
        {
            long id;
            await using (var cmd = Cmd("""
                INSERT acc.tally_export(property_id, from_date, to_date, voucher_count, debit_paise, credit_paise, exported_by, exported_utc, remote_ids)
                OUTPUT INSERTED.export_id VALUES (@p, @f, @t, @n, @dr, @cr, @u, @now, 1);
                """, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, export.PropertyId);
                cmd.Parameters.Set("@f", SqlDbType.Date, export.From.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.Set("@t", SqlDbType.Date, export.To.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.Set("@n", SqlDbType.Int, export.VoucherCount);
                cmd.Parameters.Set("@dr", SqlDbType.BigInt, export.Debit);
                cmd.Parameters.Set("@cr", SqlDbType.BigInt, export.Credit);
                cmd.Parameters.Set("@u", SqlDbType.Int, export.ExportedBy);
                cmd.Parameters.Set("@now", SqlDbType.DateTime2, export.ExportedUtc);
                id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }

            foreach (var (date, vouchers, reexport, provisional) in export.Days)
            {
                await using var cmd = Cmd("""
                    INSERT acc.tally_export_day(export_id, property_id, business_date, voucher_count, reexport, provisional) VALUES (@e, @p, @d, @n, @re, @pr);
                    """, c, t);
                cmd.Parameters.Set("@e", SqlDbType.BigInt, id);
                cmd.Parameters.Set("@p", SqlDbType.Int, export.PropertyId);
                cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.Set("@n", SqlDbType.Int, vouchers);
                cmd.Parameters.Set("@re", SqlDbType.Bit, reexport);
                cmd.Parameters.Set("@pr", SqlDbType.Bit, provisional);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            foreach (var flag in export.ClearFlags)
            {
                await using var cmd = Cmd("UPDATE acc.tally_reexport SET cleared_export_id = @e, cleared_utc = @now WHERE reexport_id = @x AND cleared_export_id IS NULL;", c, t);
                cmd.Parameters.Set("@e", SqlDbType.BigInt, id);
                cmd.Parameters.Set("@now", SqlDbType.DateTime2, export.ExportedUtc);
                cmd.Parameters.Set("@x", SqlDbType.BigInt, flag);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            return id;
        }, ct).ConfigureAwait(false);
    }

    // ── A03b-05: stable Tally voucher ids (migration 3950) ──

    public Task<string> TallyIdentityAsync(ICommandTransaction? tx, int propertyId, DateTime now, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            // The insert is one statement (atomic on its own connection too): the identity is created once, then read. Never updated (trigger).
            await using var cmd = Cmd("""
                INSERT acc.tally_identity(property_id, created_utc)
                SELECT @p, @now WHERE NOT EXISTS (SELECT 1 FROM acc.tally_identity WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p);
                SELECT LOWER(REPLACE(CONVERT(CHAR(36), tally_guid), '-', '')) FROM acc.tally_identity WHERE property_id = @p;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        }, ct);

    public Task<IReadOnlyList<DateOnly>> TallyExportedWithoutIdsAsync(ICommandTransaction? tx, int propertyId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dates);
        return WithAsync<IReadOnlyList<DateOnly>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT DISTINCT d.business_date FROM acc.tally_export_day d JOIN acc.tally_export e ON e.export_id = d.export_id
                WHERE d.property_id = @p AND e.remote_ids = 0 AND d.business_date IN (SELECT CAST(value AS DATE) FROM OPENJSON(@dates))
                ORDER BY d.business_date;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@dates", SqlDbType.NVarChar, System.Text.Json.JsonSerializer.Serialize(dates.Select(AccountingVoucherProjector.Iso)));
            var list = new List<DateOnly>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(AccSql.Date(rd, 0));
            return list;
        }, ct);
    }

    public async Task<string> PropertyNameAsync(int propertyId, CancellationToken ct)
        => await WithAsync<string>(null, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT name FROM inv.property WHERE property_id = @p;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        }, ct).ConfigureAwait(false);
}
