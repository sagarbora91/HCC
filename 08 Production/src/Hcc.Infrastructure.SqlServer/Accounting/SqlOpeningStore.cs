using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// SQL Server implementation of <see cref="IOpeningEntryStore"/> (ACC-1, migration 4220: acc.opening_entry one row per property +
/// acc.opening_line). Commands serialize on <c>acc:opening:&lt;property&gt;</c> with a transaction-owned application lock; a save rewrites
/// the lines; approve / reopen check the row version when one is given.
/// </summary>
public sealed class SqlOpeningStore(SqlConnectionFactory connections) : IOpeningEntryStore
{
    private async Task<T> WithAsync<T>(ICommandTransaction? tx, Func<SqlConnection, SqlTransaction?, Task<T>> f, CancellationToken ct)
    {
        if (tx is not null)
        {
            var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The opening balances store needs a SQL command transaction.");
            return await f(s.Connection, s.Transaction).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn, null).ConfigureAwait(false);
    }

    private SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction? t) => connections.Command(sql, c, t);

    public static string Resource(int propertyId) => "acc:opening:" + propertyId.ToString(CultureInfo.InvariantCulture);

    public async Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;", c, t);
            cmd.Parameters.Set("@res", SqlDbType.NVarChar, Resource(propertyId), 255);
            var r = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
            if (r < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "Another change to the opening balances is in progress. Try again.");
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public Task<OpeningEntry?> GetAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                SELECT as_of, status, entered_by, entered_utc, approved_by, approved_utc, note, row_ver FROM acc.opening_entry WHERE property_id = @p;
                SELECT account, debit_paise, credit_paise FROM acc.opening_line WHERE property_id = @p ORDER BY line_no;
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return (OpeningEntry?)null;
            var asOf = AccSql.Date(rd, 0);
            var status = rd.GetString(1);
            var by = rd.GetInt32(2);
            var at = DateTime.SpecifyKind(rd.GetDateTime(3), DateTimeKind.Utc);
            int? approvedBy = rd.IsDBNull(4) ? null : rd.GetInt32(4);
            DateTime? approvedUtc = rd.IsDBNull(5) ? null : DateTime.SpecifyKind(rd.GetDateTime(5), DateTimeKind.Utc);
            var note = AccSql.StrOrNull(rd, 6);
            var version = SqlValues.RowVersionToWire((byte[])rd[7]);
            var lines = new List<OpeningLine>();
            await rd.NextResultAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) lines.Add(new OpeningLine(rd.GetString(0), rd.GetInt64(1), rd.GetInt64(2)));
            return new OpeningEntry(propertyId, asOf, status, lines, by, at, approvedBy, approvedUtc, note, version);
        }, ct);

    public Task<string> SaveDraftAsync(ICommandTransaction tx, int propertyId, DateOnly asOf, IReadOnlyList<OpeningLine> lines, string? note, int actor, DateTime now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(lines);
        return WithAsync(tx, async (c, t) =>
        {
            await using (var cmd = Cmd("""
                DELETE acc.opening_line WHERE property_id = @p;
                MERGE acc.opening_entry AS e
                USING (SELECT @p AS property_id) AS s ON e.property_id = s.property_id
                WHEN MATCHED THEN UPDATE SET as_of = @d, status = 'draft', entered_by = @u, entered_utc = @now, approved_by = NULL, approved_utc = NULL, note = @n
                WHEN NOT MATCHED THEN INSERT (property_id, as_of, status, entered_by, entered_utc, note) VALUES (@p, @d, 'draft', @u, @now, @n);
                """, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                cmd.Parameters.Set("@d", SqlDbType.Date, asOf.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.Set("@u", SqlDbType.Int, actor);
                cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
                cmd.Parameters.Set("@n", SqlDbType.NVarChar, note, 200);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            for (var i = 0; i < lines.Count; i++)
            {
                await using var ins = Cmd("INSERT acc.opening_line(property_id, line_no, account, debit_paise, credit_paise) VALUES (@p, @n, @a, @d, @c);", c, t);
                ins.Parameters.Set("@p", SqlDbType.Int, propertyId);
                ins.Parameters.Set("@n", SqlDbType.SmallInt, (short)(i + 1));
                ins.Parameters.Set("@a", SqlDbType.VarChar, lines[i].Account, 41);
                ins.Parameters.Set("@d", SqlDbType.BigInt, lines[i].Debit);
                ins.Parameters.Set("@c", SqlDbType.BigInt, lines[i].Credit);
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            return await VersionAsync(c, t, propertyId, ct).ConfigureAwait(false);
        }, ct);
    }

    public Task<string> ApproveAsync(ICommandTransaction tx, int propertyId, string? expectedVersion, int actor, DateTime now, CancellationToken ct)
        => SetStatusAsync(tx, propertyId, expectedVersion, """
            UPDATE acc.opening_entry SET status = 'approved', approved_by = @u, approved_utc = @now
            WHERE property_id = @p AND status = 'draft' AND (@v IS NULL OR row_ver = @v);
            """, actor, now, null, ct);

    public Task<string> ReopenAsync(ICommandTransaction tx, int propertyId, string? expectedVersion, string reason, int actor, DateTime now, CancellationToken ct)
        => SetStatusAsync(tx, propertyId, expectedVersion, """
            UPDATE acc.opening_entry SET status = 'draft', approved_by = NULL, approved_utc = NULL, reopened_by = @u, reopened_utc = @now, reopen_reason = @r
            WHERE property_id = @p AND status = 'approved' AND (@v IS NULL OR row_ver = @v);
            """, actor, now, reason, ct);

    private Task<string> SetStatusAsync(ICommandTransaction tx, int propertyId, string? expectedVersion, string sql, int actor, DateTime now, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var version = expectedVersion is null ? null : SqlValues.RowVersionFromWire(expectedVersion);
        return WithAsync(tx, async (c, t) =>
        {
            await using (var cmd = Cmd(sql, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                cmd.Parameters.Set("@u", SqlDbType.Int, actor);
                cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
                cmd.Parameters.Set("@v", SqlDbType.Binary, version, 8);
                if (reason is not null) cmd.Parameters.Set("@r", SqlDbType.NVarChar, reason, 200);
                SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The opening balances");
            }

            return await VersionAsync(c, t, propertyId, ct).ConfigureAwait(false);
        }, ct);
    }

    private async Task<string> VersionAsync(SqlConnection c, SqlTransaction? t, int propertyId, CancellationToken ct)
    {
        await using var cmd = Cmd("SELECT row_ver FROM acc.opening_entry WHERE property_id = @p;", c, t);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        return SqlValues.RowVersionToWire((byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
    }

    public Task<DateOnly?> FirstLockOnOrAfterAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct)
        => WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT MIN(business_date) FROM acc.period_lock WHERE property_id = @p AND business_date >= @d AND unlocked_utc IS NULL;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@d", SqlDbType.Date, date.ToDateTime(TimeOnly.MinValue));
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return v is DateTime d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        }, ct);
}
