using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>
/// SQL Server implementation of <see cref="IManualVoucherStore"/> (ACC-2, migrations 4239 / 4240: acc.manual_voucher + acc.manual_line,
/// acc.loan). Commands serialize on <c>acc:manual:&lt;property&gt;</c> with a transaction-owned application lock; status changes check the
/// row version when one is given. Parties come from the masters exactly as <see cref="SqlReportPartyNames"/> reads them, plus the loans.
/// </summary>
public sealed class SqlManualVoucherStore(SqlConnectionFactory connections) : IManualVoucherStore
{
    private const string Columns = """
        voucher_id, voucher_date, voucher_type, narration, reference, template, status, entered_by, entered_utc, decided_by, decided_utc, reason,
        void_of, voided_by, voided_utc, booked_on, row_ver
        """;

    private const string PartiesSql = """
        SELECT CASE kind WHEN 'till' THEN '1000' ELSE '1030' END, code, CAST(NULL AS NVARCHAR(200)), CAST(NULL AS VARCHAR(20)) FROM mst.bank_account WHERE property_id = @p
        UNION ALL SELECT '1110', CONCAT('CMP-', company_id), NULL, NULL FROM grp.company_account WHERE property_id = @p
        UNION ALL SELECT '1115', code, NULL, NULL FROM ota.channel WHERE property_id = @p
        UNION ALL SELECT '1120', NULL, customer_name, mobile FROM ops.udhaar_receivable WHERE property_id = @p
        UNION ALL SELECT '1200', CONCAT('STF-', staff_id), NULL, NULL FROM hr.staff WHERE property_id = @p
        UNION ALL SELECT '2200', code, NULL, NULL FROM ops.expense_vendor WHERE property_id = @p
        UNION ALL SELECT '2400', party_id, NULL, NULL FROM acc.loan WHERE property_id = @p AND active = 1;
        """;

    private async Task<T> WithAsync<T>(ICommandTransaction? tx, Func<SqlConnection, SqlTransaction?, Task<T>> f, CancellationToken ct)
    {
        if (tx is not null)
        {
            var s = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The manual voucher store needs a SQL command transaction.");
            return await f(s.Connection, s.Transaction).ConfigureAwait(false);
        }

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn, null).ConfigureAwait(false);
    }

    private SqlCommand Cmd(string sql, SqlConnection c, SqlTransaction? t) => connections.Command(sql, c, t);

    public static string Resource(int propertyId) => "acc:manual:" + propertyId.ToString(CultureInfo.InvariantCulture);

    private static DateTime? Utc(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateTime.SpecifyKind(rd.GetDateTime(i), DateTimeKind.Utc);

    public async Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        await WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;", c, t);
            cmd.Parameters.Set("@res", SqlDbType.NVarChar, Resource(propertyId), 255);
            var r = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -999);
            if (r < 0) throw new HccException(HccErrorCode.UNAVAILABLE, "Another change to the manual vouchers is in progress. Try again.");
            return 0;
        }, ct).ConfigureAwait(false);
    }

    public Task<ManualVoucher?> GetAsync(ICommandTransaction? tx, int propertyId, long voucherId, CancellationToken ct)
        => WithAsync(tx, async (c, t) => (await ReadAsync(c, t, propertyId, voucherId, ct).ConfigureAwait(false)).FirstOrDefault(), ct);

    public Task<IReadOnlyList<ManualVoucher>> ListAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyList<ManualVoucher>>(tx, async (c, t) => await ReadAsync(c, t, propertyId, null, ct).ConfigureAwait(false), ct);

    private async Task<List<ManualVoucher>> ReadAsync(SqlConnection c, SqlTransaction? t, int propertyId, long? voucherId, CancellationToken ct)
    {
        await using var cmd = Cmd($"""
            SELECT {Columns} FROM acc.manual_voucher WHERE property_id = @p AND (@id IS NULL OR voucher_id = @id) ORDER BY voucher_id DESC;
            SELECT l.voucher_id, l.account, l.debit_paise, l.credit_paise, l.note FROM acc.manual_line l JOIN acc.manual_voucher v ON v.voucher_id = l.voucher_id
            WHERE v.property_id = @p AND (@id IS NULL OR v.voucher_id = @id) ORDER BY l.voucher_id, l.line_no;
            """, c, t);
        cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = voucherId is { } id ? id : DBNull.Value;
        var heads = new List<(long Id, Func<IReadOnlyList<ManualLine>, ManualVoucher> Make)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt64(0);
            var date = AccSql.Date(rd, 1);
            var type = rd.GetString(2);
            var narration = rd.GetString(3);
            var reference = AccSql.StrOrNull(rd, 4);
            var template = AccSql.StrOrNull(rd, 5);
            var status = rd.GetString(6);
            var by = rd.GetInt32(7);
            var at = DateTime.SpecifyKind(rd.GetDateTime(8), DateTimeKind.Utc);
            int? decidedBy = rd.IsDBNull(9) ? null : rd.GetInt32(9);
            var decidedUtc = Utc(rd, 10);
            var reason = AccSql.StrOrNull(rd, 11);
            long? voidOf = rd.IsDBNull(12) ? null : rd.GetInt64(12);
            int? voidedBy = rd.IsDBNull(13) ? null : rd.GetInt32(13);
            var voidedUtc = Utc(rd, 14);
            DateOnly? bookedOn = rd.IsDBNull(15) ? null : AccSql.Date(rd, 15);
            var version = SqlValues.RowVersionToWire((byte[])rd[16]);
            heads.Add((id, lines => new ManualVoucher(id, propertyId, date, type, narration, reference, template, status, lines, by, at, decidedBy, decidedUtc, reason,
                voidOf, voidedBy, voidedUtc, bookedOn, version)));
        }

        var lines = new Dictionary<long, List<ManualLine>>();
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt64(0);
            if (!lines.TryGetValue(id, out var list)) lines[id] = list = [];
            list.Add(new ManualLine(rd.GetString(1), rd.GetInt64(2), rd.GetInt64(3), AccSql.StrOrNull(rd, 4)));
        }

        return heads.Select(h => h.Make(lines.TryGetValue(h.Id, out var l) ? l : [])).ToList();
    }

    private static void Head(SqlCommand cmd, ManualVoucherDraft d)
    {
        cmd.Parameters.Set("@d", SqlDbType.Date, d.Date.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.Set("@type", SqlDbType.VarChar, d.Type, 8);
        cmd.Parameters.Set("@narr", SqlDbType.NVarChar, d.Narration, 400);
        cmd.Parameters.Set("@ref", SqlDbType.NVarChar, d.Reference, 60);
        cmd.Parameters.Set("@tpl", SqlDbType.VarChar, d.Template, 24);
        cmd.Parameters.Set("@status", SqlDbType.VarChar, d.Status, 8);
        cmd.Parameters.Set("@by", SqlDbType.Int, d.EnteredBy);
        cmd.Parameters.Set("@at", SqlDbType.DateTime2, d.EnteredUtc);
        cmd.Parameters.Add("@dby", SqlDbType.Int).Value = d.DecidedBy is { } x ? x : DBNull.Value;
        cmd.Parameters.Add("@dat", SqlDbType.DateTime2).Value = d.DecidedUtc is { } y ? y : DBNull.Value;
        cmd.Parameters.Set("@reason", SqlDbType.NVarChar, d.Reason, 200);
        cmd.Parameters.Add("@of", SqlDbType.BigInt).Value = d.VoidOf is { } z ? z : DBNull.Value;
        cmd.Parameters.Add("@booked", SqlDbType.Date).Value = d.BookedOn is { } b ? b.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
    }

    private async Task LinesAsync(SqlConnection c, SqlTransaction? t, long voucherId, IReadOnlyList<ManualLine> lines, CancellationToken ct)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            await using var ins = Cmd("INSERT acc.manual_line(voucher_id, line_no, account, debit_paise, credit_paise, note) VALUES (@v, @n, @a, @d, @c, @note);", c, t);
            ins.Parameters.Set("@v", SqlDbType.BigInt, voucherId);
            ins.Parameters.Set("@n", SqlDbType.SmallInt, (short)(i + 1));
            ins.Parameters.Set("@a", SqlDbType.VarChar, lines[i].Account, 45);
            ins.Parameters.Set("@d", SqlDbType.BigInt, lines[i].Debit);
            ins.Parameters.Set("@c", SqlDbType.BigInt, lines[i].Credit);
            ins.Parameters.Set("@note", SqlDbType.NVarChar, lines[i].Note, 120);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<string> VersionAsync(SqlConnection c, SqlTransaction? t, long voucherId, CancellationToken ct)
    {
        await using var cmd = Cmd("SELECT row_ver FROM acc.manual_voucher WHERE voucher_id = @v;", c, t);
        cmd.Parameters.Set("@v", SqlDbType.BigInt, voucherId);
        return SqlValues.RowVersionToWire((byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!);
    }

    public Task<(long Id, string Version)> InsertAsync(ICommandTransaction tx, int propertyId, ManualVoucherDraft draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(draft);
        return WithAsync(tx, async (c, t) =>
        {
            long id;
            await using (var cmd = Cmd("""
                INSERT acc.manual_voucher(property_id, voucher_date, voucher_type, narration, reference, template, status, entered_by, entered_utc, decided_by, decided_utc,
                                          reason, void_of, booked_on)
                OUTPUT INSERTED.voucher_id VALUES (@p, @d, @type, @narr, @ref, @tpl, @status, @by, @at, @dby, @dat, @reason, @of, @booked);
                """, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                Head(cmd, draft);
                id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }

            await LinesAsync(c, t, id, draft.Lines, ct).ConfigureAwait(false);
            return (id, await VersionAsync(c, t, id, ct).ConfigureAwait(false));
        }, ct);
    }

    public Task<string> ReplaceAsync(ICommandTransaction tx, int propertyId, long voucherId, string? expectedVersion, ManualVoucherDraft draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(draft);
        var version = expectedVersion is null ? null : SqlValues.RowVersionFromWire(expectedVersion);
        return WithAsync(tx, async (c, t) =>
        {
            await using (var cmd = Cmd("""
                UPDATE acc.manual_voucher SET voucher_date = @d, voucher_type = @type, narration = @narr, reference = @ref, template = @tpl, status = @status,
                  entered_by = @by, entered_utc = @at, decided_by = @dby, decided_utc = @dat, reason = @reason, void_of = @of, booked_on = @booked
                WHERE property_id = @p AND voucher_id = @v AND status IN ('pending', 'rejected') AND (@ver IS NULL OR row_ver = @ver);
                """, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                cmd.Parameters.Set("@v", SqlDbType.BigInt, voucherId);
                cmd.Parameters.Set("@ver", SqlDbType.Binary, version, 8);
                Head(cmd, draft);
                SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The manual voucher");
            }

            await using (var del = Cmd("DELETE acc.manual_line WHERE voucher_id = @v;", c, t))
            {
                del.Parameters.Set("@v", SqlDbType.BigInt, voucherId);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await LinesAsync(c, t, voucherId, draft.Lines, ct).ConfigureAwait(false);
            return await VersionAsync(c, t, voucherId, ct).ConfigureAwait(false);
        }, ct);
    }

    public Task<string> DecideAsync(ICommandTransaction tx, int propertyId, long voucherId, string? expectedVersion, bool approve, string? reason, int actor, DateTime now,
        CancellationToken ct)
        => SetAsync(tx, propertyId, voucherId, expectedVersion, approve
            ? "UPDATE acc.manual_voucher SET status = 'approved', decided_by = @u, decided_utc = @now, reason = NULL, booked_on = voucher_date WHERE property_id = @p AND voucher_id = @v AND status = 'pending' AND (@ver IS NULL OR row_ver = @ver);"
            : "UPDATE acc.manual_voucher SET status = 'rejected', decided_by = @u, decided_utc = @now, reason = @r WHERE property_id = @p AND voucher_id = @v AND status = 'pending' AND (@ver IS NULL OR row_ver = @ver);",
            reason, actor, now, ct);

    public Task<string> VoidAsync(ICommandTransaction tx, int propertyId, long voucherId, string? expectedVersion, string reason, int actor, DateTime now, CancellationToken ct)
        => SetAsync(tx, propertyId, voucherId, expectedVersion,
            "UPDATE acc.manual_voucher SET status = 'void', voided_by = @u, voided_utc = @now, reason = @r WHERE property_id = @p AND voucher_id = @v AND status = 'approved' AND (@ver IS NULL OR row_ver = @ver);",
            reason, actor, now, ct);

    private Task<string> SetAsync(ICommandTransaction tx, int propertyId, long voucherId, string? expectedVersion, string sql, string? reason, int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var version = expectedVersion is null ? null : SqlValues.RowVersionFromWire(expectedVersion);
        return WithAsync(tx, async (c, t) =>
        {
            await using (var cmd = Cmd(sql, c, t))
            {
                cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
                cmd.Parameters.Set("@v", SqlDbType.BigInt, voucherId);
                cmd.Parameters.Set("@u", SqlDbType.Int, actor);
                cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
                cmd.Parameters.Set("@ver", SqlDbType.Binary, version, 8);
                cmd.Parameters.Set("@r", SqlDbType.NVarChar, reason, 200);
                SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "The manual voucher");
            }

            return await VersionAsync(c, t, voucherId, ct).ConfigureAwait(false);
        }, ct);
    }

    public Task<IReadOnlySet<string>> PartyIdsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlySet<string>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd(PartiesSql, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var control = rd.GetString(0);
                var party = control == "1120" ? PartyLedgers.Udhaar(rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3))
                    : Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture)!;
                ids.Add(PartyLedgers.Compose(control, PartyLedgers.Clip(party)));
            }

            return ids;
        }, ct);

    public Task<IReadOnlyList<LoanRow>> LoansAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => WithAsync<IReadOnlyList<LoanRow>>(tx, async (c, t) =>
        {
            await using var cmd = Cmd("SELECT party_id, name, lender, account_last4, note, active FROM acc.loan WHERE property_id = @p ORDER BY party_id;", c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            var list = new List<LoanRow>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                list.Add(new LoanRow(rd.GetString(0), rd.GetString(1), rd.GetString(2), AccSql.StrOrNull(rd, 3), AccSql.StrOrNull(rd, 4), rd.GetBoolean(5)));
            return list;
        }, ct);

    public Task InsertLoanAsync(ICommandTransaction tx, int propertyId, LoanRow loan, int actor, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(loan);
        return WithAsync(tx, async (c, t) =>
        {
            await using var cmd = Cmd("""
                INSERT acc.loan(property_id, party_id, name, lender, account_last4, note, active, created_by, created_utc) VALUES (@p, @id, @n, @l, @a4, @note, 1, @u, @now);
                """, c, t);
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId);
            cmd.Parameters.Set("@id", SqlDbType.VarChar, loan.PartyId, 40);
            cmd.Parameters.Set("@n", SqlDbType.NVarChar, loan.Name, 60);
            cmd.Parameters.Set("@l", SqlDbType.NVarChar, loan.Lender, 60);
            cmd.Parameters.Set("@a4", SqlDbType.Char, loan.AccountLast4, 4);
            cmd.Parameters.Set("@note", SqlDbType.NVarChar, loan.Note, 200);
            cmd.Parameters.Set("@u", SqlDbType.Int, actor);
            cmd.Parameters.Set("@now", SqlDbType.DateTime2, now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return 0;
        }, ct);
    }
}
