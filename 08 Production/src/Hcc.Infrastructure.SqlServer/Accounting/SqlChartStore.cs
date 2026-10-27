using System.Data;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// SQL Server implementation of <see cref="IChartStore"/> (ACC-1, migrations 4200–4201): the property's Owner rows of <c>acc.ledger</c>
/// (overrides of built-ins and Owner-added ledgers), its custom sub-groups in <c>acc.ledger_group</c> and the history <c>acc.ledger_change</c>.
/// The built-in catalogue rows (property_id NULL) are not read: the built-ins come from <see cref="ChartOfAccounts"/> (a test pins them equal).
/// Commands serialize per property with a transaction-owned application lock.
/// </summary>
public sealed class SqlChartStore(SqlConnectionFactory connections) : IChartStore
{
    private async Task<T> WithAsync<T>(ICommandTransaction? tx, Func<SqlConnection, SqlTransaction?, Task<T>> f, CancellationToken ct)
    {
        if (tx is not null)
        {
            var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The chart store needs a SQL command transaction.");
            return await f(s.Connection, s.Transaction).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn, null).ConfigureAwait(false);
    }

    private SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction? t) => connections.Command(sql, c, t);

    private static string Gst(LedgerGst g) => g switch { LedgerGst.Input => "input", LedgerGst.Output => "output", _ => "none" };

    public async Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;", c, t);
            cmd.Parameters.Set("@res", SqlDbType.NVarChar, "acc:chart:" + propertyId.ToString(System.Globalization.CultureInfo.InvariantCulture), 255);
            var r = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
            if (r < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "Another chart of accounts change is in progress. Try again.");
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<StoredLedger>> LedgersAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyList<StoredLedger>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT code, name, group_id, gst, tally_name, note, active, is_system FROM acc.ledger WHERE property_id = @p ORDER BY code;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<StoredLedger>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var gst = ChartRules.ParseGst(rd.GetString(3)) ?? LedgerGst.None;
                var note = AccSql.StrOrNull(rd, 5);
                list.Add(new StoredLedger(rd.GetString(0), rd.GetString(1), rd.GetString(2), gst, AccSql.StrOrNull(rd, 4), rd.GetBoolean(7) ? note ?? "" : note, rd.GetBoolean(6)));
            }

            return list;
        }, ct);

    public Task<IReadOnlyList<LedgerGroup>> CustomGroupsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyList<LedgerGroup>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT group_id, name, parent_id, nature, side FROM acc.ledger_group WHERE property_id = @p AND is_custom = 1 ORDER BY ledger_group_id;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<LedgerGroup>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new LedgerGroup(rd.GetString(0), rd.GetString(1), AccSql.StrOrNull(rd, 2), rd.GetString(3), rd.GetString(4), true));
            return list;
        }, ct);

    public async Task WriteLedgerAsync(ICommandTransaction tx, int propertyId, string code, StoredLedger? row, int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(code);
        var builtin = ChartOfAccounts.All.FirstOrDefault(l => l.Code == code);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                IF @del = 1
                  DELETE acc.ledger WHERE property_id = @p AND code = @c;
                ELSE IF EXISTS (SELECT 1 FROM acc.ledger WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND code = @c)
                  UPDATE acc.ledger SET name = @n, group_id = @g, gst = @gst, tally_name = @tn, note = @note, active = @a, changed_by = @u, changed_utc = @now
                  WHERE property_id = @p AND code = @c;
                ELSE
                  INSERT acc.ledger(property_id, code, name, group_id, is_system, is_control, gst, tally_name, active, note, changed_by, changed_utc)
                  VALUES (@p, @c, @n, @g, @sys, @ctl, @gst, @tn, @a, @note, @u, @now);
                """, c, t);
            cmd.Parameters.Set("@del", SqlDbType.Bit, row is null);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@c", SqlDbType.VarChar, code, 4);
            cmd.Parameters.Set("@n", SqlDbType.NVarChar, row?.Name ?? "", 60);
            cmd.Parameters.Set("@g", SqlDbType.VarChar, row?.GroupId ?? "", 48);
            cmd.Parameters.Set("@sys", SqlDbType.Bit, builtin is not null);
            cmd.Parameters.Set("@ctl", SqlDbType.Bit, builtin?.Control ?? false);
            cmd.Parameters.Set("@gst", SqlDbType.VarChar, Gst(row?.Gst ?? LedgerGst.None), 6);
            cmd.Parameters.Set("@tn", SqlDbType.NVarChar, string.IsNullOrWhiteSpace(row?.TallyName) ? null : row.TallyName, 60);
            cmd.Parameters.Set("@note", SqlDbType.NVarChar, row?.Note, 200);
            cmd.Parameters.Set("@a", SqlDbType.Bit, row?.Active ?? true);
            cmd.Parameters.Set("@u", SqlDbType.Int, actor);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public async Task WriteGroupAsync(ICommandTransaction tx, int propertyId, LedgerGroup group, int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(group);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                IF EXISTS (SELECT 1 FROM acc.ledger_group WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND group_id = @g)
                  UPDATE acc.ledger_group SET name = @n, parent_id = @parent, nature = @na, side = @si, changed_by = @u, changed_utc = @now
                  WHERE property_id = @p AND group_id = @g;
                ELSE
                  INSERT acc.ledger_group(property_id, group_id, name, parent_id, nature, side, is_custom, changed_by, changed_utc)
                  VALUES (@p, @g, @n, @parent, @na, @si, 1, @u, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@g", SqlDbType.VarChar, group.Id, 48);
            cmd.Parameters.Set("@n", SqlDbType.NVarChar, group.Name, 60);
            cmd.Parameters.Set("@parent", SqlDbType.VarChar, group.Parent, 48);
            cmd.Parameters.Set("@na", SqlDbType.Char, group.Nature, 2);
            cmd.Parameters.Set("@si", SqlDbType.Char, group.Side, 2);
            cmd.Parameters.Set("@u", SqlDbType.Int, actor);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChartChange>> AppendChangesAsync(ICommandTransaction tx, int propertyId, string kind, string code, IReadOnlyList<ChartFieldChange> changes,
        int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(changes);
        return await WithAsync<IReadOnlyList<ChartChange>>(tx, async (c, t) =>
        {
            var list = new List<ChartChange>();
            foreach (var ch in changes)
            {
                await using var cmd = Cmd("""
                    INSERT acc.ledger_change(property_id, kind, code, field, before_value, after_value, actor, at_utc)
                    OUTPUT INSERTED.change_id VALUES (@p, @k, @c, @f, @b, @a, @u, @now);
                    """, c, t);
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                cmd.Parameters.Set("@k", SqlDbType.VarChar, kind, 6);
                cmd.Parameters.Set("@c", SqlDbType.VarChar, code, 48);
                cmd.Parameters.Set("@f", SqlDbType.VarChar, ch.Field, 20);
                cmd.Parameters.Set("@b", SqlDbType.NVarChar, Clip(ch.Before), 300);
                cmd.Parameters.Set("@a", SqlDbType.NVarChar, Clip(ch.After), 300);
                cmd.Parameters.Set("@u", SqlDbType.Int, actor);
                cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
                var id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
                list.Add(new ChartChange(id, now, actor, kind, code, ch.Field, Clip(ch.Before), Clip(ch.After)));
            }

            return list;
        }, ct).ConfigureAwait(false);
    }

    private static string Clip(string s) => s.Length <= 300 ? s : s[..300];

    public async Task<IReadOnlyList<ChartChange>> HistoryAsync(int propertyId, CancellationToken ct)
        => await WithAsync<IReadOnlyList<ChartChange>>(null, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT change_id, at_utc, actor, kind, code, field, before_value, after_value FROM acc.ledger_change WHERE property_id = @p ORDER BY change_id;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<ChartChange>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new ChartChange(rd.GetInt64(0), DateTime.SpecifyKind(rd.GetDateTime(1), DateTimeKind.Utc), rd.GetInt32(2), rd.GetString(3), rd.GetString(4),
                    rd.GetString(5), rd.GetString(6), rd.GetString(7)));
            return list;
        }, ct).ConfigureAwait(false);
}
