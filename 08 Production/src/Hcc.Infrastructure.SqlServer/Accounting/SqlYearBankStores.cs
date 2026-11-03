using System.Data;
using System.Globalization;
using Hcc.Application.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Accounting;

// F5-A1 · ACC-4 SQL stores (02 Planning/agents/ACC4-CONTRACT.md §1, §2; migrations 4280–4289): IYearLockStore over acc.fiscal_year /
// acc.carry_forward, the books (IAccountingStore days + the report opening + approved year-close vouchers) and the checklist facts;
// IBankReconStore over mst.bank_statement_layout / acc.bank_statement_import / _row / acc.bank_match.

internal static class F5Sql
{
    public static async Task<int> ExecAsync(SqlConnectionFactory f, string sql, Action<SqlParameterCollection> ps, CancellationToken ct, SqlConnection? c = null, SqlTransaction? t = null)
    {
        if (c is not null) { await using var cmd0 = f.Command(sql, c, t); ps(cmd0.Parameters); return await cmd0.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        await using var conn = await f.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = f.Command(sql, conn);
        ps(cmd.Parameters);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public static async Task<List<T>> ReadAsync<T>(SqlConnectionFactory f, string sql, Action<SqlParameterCollection> ps, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var conn = await f.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = f.Command(sql, conn);
        ps(cmd.Parameters);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<T>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(map(rd));
        return list;
    }

    public static DateOnly D(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));
    public static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
}

public sealed class SqlYearLockStore(SqlConnectionFactory connections, IAccountingStore accounting, IReportOpeningSource? opening = null) : IYearLockStore
{
    public async Task<IReadOnlyList<FiscalYearState>> YearsAsync(int propertyId, CancellationToken ct) =>
        await F5Sql.ReadAsync(connections, "SELECT fy, status, locked_utc, locked_by, reopened_utc, reopened_by, reopen_reason, provisional_exports, carry_forward_id FROM acc.fiscal_year WHERE property_id = @p",
            ps => ps.Set("@p", SqlDbType.Int, propertyId),
            r => new FiscalYearState(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetDateTime(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetDateTime(4),
                r.IsDBNull(5) ? null : r.GetInt32(5), F5Sql.S(r, 6), r.GetBoolean(7), F5Sql.S(r, 8)), ct).ConfigureAwait(false);

    public Task SaveYearAsync(int propertyId, FiscalYearState y, int actor, string action, string? reason, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(y);
        return F5Sql.ExecAsync(connections, """
            MERGE acc.fiscal_year AS t USING (SELECT @p AS property_id, @fy AS fy) AS s ON t.property_id = s.property_id AND t.fy = s.fy
            WHEN MATCHED THEN UPDATE SET status = @st, locked_by = @lb, locked_utc = @lu, reopened_by = @rb, reopened_utc = @ru, reopen_reason = @rr, provisional_exports = @pe, carry_forward_id = @cf
            WHEN NOT MATCHED THEN INSERT (property_id, fy, status, locked_by, locked_utc, reopened_by, reopened_utc, reopen_reason, provisional_exports, carry_forward_id)
              VALUES (@p, @fy, @st, @lb, @lu, @rb, @ru, @rr, @pe, @cf);
            INSERT acc.fiscal_year_event(property_id, fy, action, actor, at_utc, reason) VALUES (@p, @fy, @a, @actor, @now, @reason);
            """, ps =>
        {
            ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@fy", SqlDbType.Char, y.Fy, 7); ps.Set("@st", SqlDbType.VarChar, y.Status, 9);
            ps.Set("@lb", SqlDbType.Int, y.LockedBy); ps.Set("@lu", SqlDbType.DateTime2, y.LockedUtc); ps.Set("@rb", SqlDbType.Int, y.ReopenedBy);
            ps.Set("@ru", SqlDbType.DateTime2, y.ReopenedUtc); ps.Set("@rr", SqlDbType.NVarChar, y.ReopenReason, 400); ps.Set("@pe", SqlDbType.Bit, y.ProvisionalExports);
            ps.Set("@cf", SqlDbType.VarChar, y.CarryForwardId, 48); ps.Set("@a", SqlDbType.VarChar, action, 14); ps.Set("@actor", SqlDbType.Int, actor);
            ps.Set("@now", SqlDbType.DateTime2, now); ps.Set("@reason", SqlDbType.NVarChar, reason, 400);
        }, ct);
    }

    public async Task<IReadOnlyList<CarryForward>> CarryForwardsAsync(int propertyId, CancellationToken ct)
    {
        var heads = await F5Sql.ReadAsync(connections, """
            SELECT carry_forward_id, from_fy, to_fy, status, profit_to_capital, drawings_to_capital, generated_utc, generated_by, approved_by, approved_utc, superseded_by
            FROM acc.carry_forward WHERE property_id = @p ORDER BY generated_utc, carry_forward_id
            """, ps => ps.Set("@p", SqlDbType.Int, propertyId),
            r => (Id: r.GetString(0), From: r.GetString(1), To: r.GetString(2), St: r.GetString(3), Pr: r.GetInt64(4), Dw: r.GetInt64(5), G: r.GetDateTime(6), Gb: r.GetInt32(7),
                Ab: r.IsDBNull(8) ? (int?)null : r.GetInt32(8), Au: r.IsDBNull(9) ? (DateTime?)null : r.GetDateTime(9), Sb: F5Sql.S(r, 10)), ct).ConfigureAwait(false);
        var lines = await F5Sql.ReadAsync(connections, """
            SELECT l.carry_forward_id, l.kind, l.ledger, l.party, l.debit, l.credit FROM acc.carry_forward_line l
            JOIN acc.carry_forward c ON c.carry_forward_id = l.carry_forward_id WHERE c.property_id = @p ORDER BY l.carry_forward_id, l.kind, l.line_no
            """, ps => ps.Set("@p", SqlDbType.Int, propertyId), r => (Id: r.GetString(0), Kind: r.GetString(1).Trim(), L: r.GetString(2), P: F5Sql.S(r, 3), Dr: r.GetInt64(4), Cr: r.GetInt64(5)), ct)
            .ConfigureAwait(false);
        return heads.Select(h => new CarryForward(h.Id, h.From, h.To, h.St,
            lines.Where(l => l.Id == h.Id && l.Kind == "carry").Select(l => new CarryForwardLine(l.L, l.P, l.Dr, l.Cr)).ToList(), h.Pr, h.Dw,
            lines.Where(l => l.Id == h.Id && l.Kind == "close").Select(l => new VoucherEntry(l.P is null ? l.L : l.L + "/" + l.P, l.Dr, l.Cr)).ToList(), h.G, h.Gb, h.Ab, h.Au, h.Sb)).ToList();
    }

    public async Task SaveCarryForwardAsync(int propertyId, CarryForward cf, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cf);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await F5Sql.ExecAsync(connections, """
            DELETE acc.carry_forward_line WHERE carry_forward_id = @id;
            MERGE acc.carry_forward AS t USING (SELECT @id AS carry_forward_id) AS s ON t.carry_forward_id = s.carry_forward_id
            WHEN MATCHED THEN UPDATE SET status = @st, approved_by = @ab, approved_utc = @au, superseded_by = @sb
            WHEN NOT MATCHED THEN INSERT (carry_forward_id, property_id, from_fy, to_fy, status, profit_to_capital, drawings_to_capital, generated_by, generated_utc, approved_by, approved_utc, superseded_by)
              VALUES (@id, @p, @f, @t, @st, @pr, @dw, @gb, @gu, @ab, @au, @sb);
            """, ps =>
        {
            ps.Set("@id", SqlDbType.VarChar, cf.Id, 48); ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@f", SqlDbType.Char, cf.FromFy, 7); ps.Set("@t", SqlDbType.Char, cf.ToFy, 7);
            ps.Set("@st", SqlDbType.VarChar, cf.Status, 10); ps.Set("@pr", SqlDbType.BigInt, cf.ProfitToCapital); ps.Set("@dw", SqlDbType.BigInt, cf.DrawingsToCapital);
            ps.Set("@gb", SqlDbType.Int, cf.GeneratedBy); ps.Set("@gu", SqlDbType.DateTime2, cf.GeneratedUtc); ps.Set("@ab", SqlDbType.Int, cf.ApprovedBy);
            ps.Set("@au", SqlDbType.DateTime2, cf.ApprovedUtc); ps.Set("@sb", SqlDbType.VarChar, cf.SupersededBy, 48);
        }, ct, conn, tx).ConfigureAwait(false);
        async Task Line(string kind, int no, string ledger, string? party, long dr, long cr) => await F5Sql.ExecAsync(connections,
            "INSERT acc.carry_forward_line(carry_forward_id, line_no, kind, ledger, party, debit, credit) VALUES (@id, @n, @k, @l, @pt, @dr, @cr)", ps =>
            {
                ps.Set("@id", SqlDbType.VarChar, cf.Id, 48); ps.Set("@n", SqlDbType.SmallInt, (short)no); ps.Set("@k", SqlDbType.Char, kind, 5); ps.Set("@l", SqlDbType.VarChar, ledger, 4);
                ps.Set("@pt", SqlDbType.VarChar, party, 36); ps.Set("@dr", SqlDbType.BigInt, dr); ps.Set("@cr", SqlDbType.BigInt, cr);
            }, ct, conn, tx).ConfigureAwait(false);
        var i = 0;
        foreach (var l in cf.Lines) await Line("carry", ++i, l.Ledger, l.Party, l.Dr, l.Cr).ConfigureAwait(false);
        i = 0;
        foreach (var e in cf.Close)
        {
            var k = e.Account.IndexOf('/', StringComparison.Ordinal);
            await Line("close", ++i, k < 0 ? e.Account : e.Account[..k], k < 0 ? null : e.Account[(k + 1)..], e.DebitPaise, e.CreditPaise).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Voucher>> VouchersUpToAsync(int propertyId, DateOnly date, CancellationToken ct)
    {
        var entry = opening is null ? null : await opening.GetAsync(propertyId, ct).ConfigureAwait(false);
        var start = entry?.Voucher is { } ov && ov.Date <= date ? ov.Date : DateOnly.MinValue;
        var all = (await AccountingDays.RangeAsync(accounting, propertyId, start, date, ct).ConfigureAwait(false)).SelectMany(d => d.Vouchers).ToList();
        if (entry?.Voucher is { } v && v.Date <= date) all.Insert(0, v);
        foreach (var cf in (await CarryForwardsAsync(propertyId, ct).ConfigureAwait(false)).Where(c => c.Status == "approved"))
            if (YearLockRules.CloseVoucher(cf) is var yc && yc.Date <= date) all.Add(yc);
        return all;
    }

    public async Task<YearCheckInput> CheckInputAsync(int propertyId, string fy, DateOnly businessDate, CancellationToken ct)
    {
        var (from, to) = YearLockRules.Range(fy);
        var bd = await accounting.BusinessDateAsync(null, propertyId, ct).ConfigureAwait(false) ?? businessDate;
        var end = bd.AddDays(-1) < to ? bd.AddDays(-1) : to;
        var days = end < from ? [] : await AccountingDays.RangeWithLocksAsync(accounting, propertyId, from, end, ct).ConfigureAwait(false);
        var active = days.Where(d => d.Vouchers.Count > 0).ToList();
        var locked = await LockedMonthsAsync(propertyId, ct).ConfigureAwait(false);
        var months = active.Select(d => d.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal).Where(m => !locked.Contains(m)).Order(StringComparer.Ordinal).ToList();
        var bal = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var v in await VouchersUpToAsync(propertyId, to, ct).ConfigureAwait(false))
            foreach (var e in v.Entries) { var c = PartyLedgers.ControlOf(e.Account); bal[c] = bal.GetValueOrDefault(c) + e.DebitPaise - e.CreditPaise; }
        var pending = await F5Sql.ReadAsync(connections, """
            SELECT 'manual voucher ' + CAST(voucher_id AS varchar(20)) FROM acc.manual_voucher WHERE property_id = @p AND status = 'pending' AND voucher_date BETWEEN @f AND @t
            UNION ALL SELECT 'expense ' + CAST(expense_id AS varchar(20)) FROM ops.expense WHERE property_id = @p AND status = 'Pending' AND business_date BETWEEN @f AND @t
            """, ps => { ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue)); ps.Set("@t", SqlDbType.Date, to.ToDateTime(TimeOnly.MinValue)); },
            r => r.GetString(0), ct).ConfigureAwait(false);
        var draft = (await F5Sql.ReadAsync(connections, "SELECT status FROM acc.opening_entry WHERE property_id = @p", ps => ps.Set("@p", SqlDbType.Int, propertyId), r => r.GetString(0), ct)
            .ConfigureAwait(false)).FirstOrDefault() == "draft";
        return new YearCheckInput(bd > to, active.Where(d => !d.Locked).Select(d => d.Date).ToList(), months, bal.GetValueOrDefault("1400"), bal.GetValueOrDefault(ChartOfAccounts.Suspense),
            bal.GetValueOrDefault("1010"), bal.GetValueOrDefault("1020"), pending, draft);
    }

    public async Task<IReadOnlyList<string>> LockedMonthsAsync(int propertyId, CancellationToken ct) =>
        await F5Sql.ReadAsync(connections, "SELECT lock_month FROM acc.month_lock WHERE property_id = @p AND unlocked_utc IS NULL", ps => ps.Set("@p", SqlDbType.Int, propertyId), r => r.GetString(0), ct)
            .ConfigureAwait(false);

    public Task MarkReexportAsync(int propertyId, DateOnly date, string reason, DateTime now, CancellationToken ct) => Task.CompletedTask; // the Tally re-export flag of the opening date: f5-int (no server table for it yet)
}

public sealed class SqlBankReconStore(SqlConnectionFactory connections) : IBankReconStore
{
    public async Task<IReadOnlyList<BankStatementLayout>> LayoutsAsync(int propertyId, CancellationToken ct) =>
        await F5Sql.ReadAsync(connections, """
            SELECT layout_id, name, bank_ledger, delimiter, header_rows, date_format, col_date, col_narration, col_ref, col_debit, col_credit, col_amount, col_drcr, col_balance,
              active, ref_pattern, settlement_pattern FROM mst.bank_statement_layout ORDER BY layout_id
            """, _ => { }, r => new BankStatementLayout(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)[0], r.GetByte(4), r.GetString(5),
                new BankLayoutColumns(r.GetString(6), r.GetString(7), F5Sql.S(r, 8), F5Sql.S(r, 9), F5Sql.S(r, 10), F5Sql.S(r, 11), F5Sql.S(r, 12), F5Sql.S(r, 13)), r.GetBoolean(14), F5Sql.S(r, 15), F5Sql.S(r, 16)), ct)
            .ConfigureAwait(false);

    public Task UpsertLayoutAsync(BankStatementLayout l, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(l);
        return F5Sql.ExecAsync(connections, """
            MERGE mst.bank_statement_layout AS t USING (SELECT @id AS layout_id) AS s ON t.layout_id = s.layout_id
            WHEN MATCHED THEN UPDATE SET name = @n, bank_ledger = @bl, delimiter = @d, header_rows = @h, date_format = @df, col_date = @c1, col_narration = @c2, col_ref = @c3, col_debit = @c4,
              col_credit = @c5, col_amount = @c6, col_drcr = @c7, col_balance = @c8, ref_pattern = @rp, settlement_pattern = @sp, active = @a
            WHEN NOT MATCHED THEN INSERT (layout_id, name, bank_ledger, delimiter, header_rows, date_format, col_date, col_narration, col_ref, col_debit, col_credit, col_amount, col_drcr, col_balance, ref_pattern, settlement_pattern, active)
              VALUES (@id, @n, @bl, @d, @h, @df, @c1, @c2, @c3, @c4, @c5, @c6, @c7, @c8, @rp, @sp, @a);
            """, ps =>
        {
            ps.Set("@id", SqlDbType.VarChar, l.Id, 40); ps.Set("@n", SqlDbType.NVarChar, l.Name, 80); ps.Set("@bl", SqlDbType.VarChar, l.BankLedger, 41); ps.Set("@d", SqlDbType.Char, l.Delimiter.ToString(), 1);
            ps.Set("@h", SqlDbType.TinyInt, (byte)l.HeaderRows); ps.Set("@df", SqlDbType.VarChar, l.DateFormat, 11); var c = l.Columns;
            ps.Set("@c1", SqlDbType.NVarChar, c.Date, 60); ps.Set("@c2", SqlDbType.NVarChar, c.Narration, 60); ps.Set("@c3", SqlDbType.NVarChar, c.Ref, 60); ps.Set("@c4", SqlDbType.NVarChar, c.Debit, 60);
            ps.Set("@c5", SqlDbType.NVarChar, c.Credit, 60); ps.Set("@c6", SqlDbType.NVarChar, c.Amount, 60); ps.Set("@c7", SqlDbType.NVarChar, c.DrCrFlag, 60); ps.Set("@c8", SqlDbType.NVarChar, c.Balance, 60);
            ps.Set("@rp", SqlDbType.NVarChar, l.RefPattern, 200); ps.Set("@sp", SqlDbType.NVarChar, l.SettlementPattern, 200); ps.Set("@a", SqlDbType.Bit, l.Active);
        }, ct);
    }

    public async Task<bool> HashExistsAsync(int propertyId, string hash, CancellationToken ct) =>
        (await F5Sql.ReadAsync(connections, "SELECT 1 FROM acc.bank_statement_import WHERE property_id = @p AND file_hash = @h", ps => { ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@h", SqlDbType.Char, hash, 64); },
            r => r.GetInt32(0), ct).ConfigureAwait(false)).Count > 0;

    public async Task<BankImport> InsertImportAsync(int propertyId, BankImport h, IReadOnlyList<BankParsedRow> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(h);
        ArgumentNullException.ThrowIfNull(rows);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        long id;
        await using (var cmd = connections.Command("""
            INSERT acc.bank_statement_import(property_id, bank_ledger, layout_id, file_name, file_hash, from_date, to_date, opening_balance, closing_balance, row_count, imported_by, imported_utc)
            OUTPUT INSERTED.import_id VALUES (@p, @bl, @l, @fn, @h, @f, @t, @ob, @cb, @n, @by, @at)
            """, conn, tx))
        {
            var ps = cmd.Parameters;
            ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@bl", SqlDbType.VarChar, h.BankLedger, 41); ps.Set("@l", SqlDbType.VarChar, h.LayoutId, 40); ps.Set("@fn", SqlDbType.NVarChar, h.FileName, 200);
            ps.Set("@h", SqlDbType.Char, h.FileHash, 64); ps.Set("@f", SqlDbType.Date, h.From.ToDateTime(TimeOnly.MinValue)); ps.Set("@t", SqlDbType.Date, h.To.ToDateTime(TimeOnly.MinValue));
            ps.Set("@ob", SqlDbType.BigInt, h.OpeningBalance); ps.Set("@cb", SqlDbType.BigInt, h.ClosingBalance); ps.Set("@n", SqlDbType.Int, h.Rows); ps.Set("@by", SqlDbType.Int, h.ImportedBy);
            ps.Set("@at", SqlDbType.DateTime2, h.ImportedUtc);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var r in rows)
            await F5Sql.ExecAsync(connections, """
                INSERT acc.bank_statement_row(import_id, property_id, bank_ledger, row_date, narration, ref, debit, credit, balance, status) VALUES (@i, @p, @bl, @d, @n, @r, @dr, @cr, @b, 'unmatched')
                """, ps =>
            {
                ps.Set("@i", SqlDbType.BigInt, id); ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@bl", SqlDbType.VarChar, h.BankLedger, 41); ps.Set("@d", SqlDbType.Date, r.Date.ToDateTime(TimeOnly.MinValue));
                ps.Set("@n", SqlDbType.NVarChar, r.Narration.Length > 400 ? r.Narration[..400] : r.Narration, 400); ps.Set("@r", SqlDbType.NVarChar, r.Ref, 80);
                ps.Set("@dr", SqlDbType.BigInt, r.Debit); ps.Set("@cr", SqlDbType.BigInt, r.Credit); ps.Set("@b", SqlDbType.BigInt, r.Balance);
            }, ct, conn, tx).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return h with { Id = F5Sql.N(id) };
    }

    public async Task<IReadOnlyList<BankImport>> ImportsAsync(int propertyId, string ledger, CancellationToken ct) =>
        await F5Sql.ReadAsync(connections, """
            SELECT import_id, bank_ledger, layout_id, file_name, file_hash, from_date, to_date, opening_balance, closing_balance, row_count, imported_by, imported_utc
            FROM acc.bank_statement_import WHERE property_id = @p AND bank_ledger = @l ORDER BY import_id
            """, ps => { ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@l", SqlDbType.VarChar, ledger, 41); },
            r => new BankImport(F5Sql.N(r.GetInt64(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), F5Sql.D(r, 5), F5Sql.D(r, 6), r.GetInt64(7), r.GetInt64(8), r.GetInt32(9), r.GetInt32(10), r.GetDateTime(11)), ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<BankStatementRow>> RowsAsync(int propertyId, string? ledger, CancellationToken ct) =>
        await F5Sql.ReadAsync(connections, """
            SELECT row_id, import_id, bank_ledger, row_date, narration, ref, debit, credit, balance, status, match_id FROM acc.bank_statement_row
            WHERE property_id = @p AND (@l IS NULL OR bank_ledger = @l) ORDER BY row_date, row_id
            """, ps => { ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@l", SqlDbType.VarChar, ledger, 41); },
            r => new BankStatementRow(F5Sql.N(r.GetInt64(0)), F5Sql.N(r.GetInt64(1)), r.GetString(2), F5Sql.D(r, 3), r.GetString(4), F5Sql.S(r, 5), r.GetInt64(6), r.GetInt64(7),
                r.IsDBNull(8) ? null : r.GetInt64(8), r.GetString(9), r.IsDBNull(10) ? null : F5Sql.N(r.GetInt64(10))), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<BankMatch>> MatchesAsync(int propertyId, string? ledger, CancellationToken ct)
    {
        void P(SqlParameterCollection ps) { ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@l", SqlDbType.VarChar, ledger, 41); }
        var heads = await F5Sql.ReadAsync(connections, "SELECT match_id, rule_kind, cleared_date, matched_by, matched_utc, undone_utc FROM acc.bank_match WHERE property_id = @p AND (@l IS NULL OR bank_ledger = @l) ORDER BY match_id",
            P, r => (Id: r.GetInt64(0), Rule: r.GetString(1), C: F5Sql.D(r, 2), By: r.GetInt32(3), At: r.GetDateTime(4), U: r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5)), ct).ConfigureAwait(false);
        var rows = await F5Sql.ReadAsync(connections, "SELECT r.match_id, r.row_id FROM acc.bank_match_row r JOIN acc.bank_match m ON m.match_id = r.match_id WHERE m.property_id = @p AND (@l IS NULL OR m.bank_ledger = @l)",
            P, r => (M: r.GetInt64(0), R: r.GetInt64(1)), ct).ConfigureAwait(false);
        var lines = await F5Sql.ReadAsync(connections, "SELECT l.match_id, l.source_key, l.entry_no FROM acc.bank_match_line l JOIN acc.bank_match m ON m.match_id = l.match_id WHERE m.property_id = @p AND (@l IS NULL OR m.bank_ledger = @l)",
            P, r => (M: r.GetInt64(0), K: r.GetString(1), N: (int)r.GetInt16(2)), ct).ConfigureAwait(false);
        return heads.Select(h => new BankMatch(F5Sql.N(h.Id), rows.Where(x => x.M == h.Id).Select(x => F5Sql.N(x.R)).ToList(),
            lines.Where(x => x.M == h.Id).Select(x => new BankLineRef(x.K, x.N)).ToList(), h.Rule, h.C, h.By, h.At, h.U)).ToList();
    }

    public async Task<BankMatch> InsertMatchAsync(int propertyId, string ledger, IReadOnlyList<string> rowIds, IReadOnlyList<BankLineRef> lines, string rule, DateOnly cleared, int by, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(lines);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        long id;
        await using (var cmd = connections.Command("INSERT acc.bank_match(property_id, bank_ledger, rule_kind, cleared_date, matched_by, matched_utc) OUTPUT INSERTED.match_id VALUES (@p, @l, @r, @c, @by, @at)", conn, tx))
        {
            cmd.Parameters.Set("@p", SqlDbType.Int, propertyId); cmd.Parameters.Set("@l", SqlDbType.VarChar, ledger, 41); cmd.Parameters.Set("@r", SqlDbType.VarChar, rule, 16);
            cmd.Parameters.Set("@c", SqlDbType.Date, cleared.ToDateTime(TimeOnly.MinValue)); cmd.Parameters.Set("@by", SqlDbType.Int, by); cmd.Parameters.Set("@at", SqlDbType.DateTime2, now);
            id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        foreach (var r in rowIds)
        {
            var n = await F5Sql.ExecAsync(connections, """
                UPDATE acc.bank_statement_row SET status = 'matched', match_id = @m WHERE row_id = @r AND property_id = @p AND status = 'unmatched';
                IF @@ROWCOUNT = 1 INSERT acc.bank_match_row(match_id, row_id) VALUES (@m, @r);
                """, ps => { ps.Set("@m", SqlDbType.BigInt, id); ps.Set("@r", SqlDbType.BigInt, long.Parse(r, CultureInfo.InvariantCulture)); ps.Set("@p", SqlDbType.Int, propertyId); }, ct, conn, tx).ConfigureAwait(false);
            if (n < 2) throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.INVALID_TRANSITION, "A statement row is already matched or ignored.");
        }

        foreach (var l in lines)
            await F5Sql.ExecAsync(connections, "INSERT acc.bank_match_line(match_id, source_key, entry_no, cleared_date) VALUES (@m, @k, @n, @c)", ps =>
            {
                ps.Set("@m", SqlDbType.BigInt, id); ps.Set("@k", SqlDbType.VarChar, l.VoucherId, 80); ps.Set("@n", SqlDbType.SmallInt, (short)l.Line); ps.Set("@c", SqlDbType.Date, cleared.ToDateTime(TimeOnly.MinValue));
            }, ct, conn, tx).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new BankMatch(F5Sql.N(id), rowIds, lines, rule, cleared, by, now);
    }

    public Task UndoMatchAsync(int propertyId, string matchId, int by, DateTime now, CancellationToken ct) => F5Sql.ExecAsync(connections, """
        UPDATE acc.bank_match SET undone_by = @by, undone_utc = @at WHERE match_id = @m AND property_id = @p AND undone_utc IS NULL;
        UPDATE acc.bank_statement_row SET status = 'unmatched', match_id = NULL WHERE match_id = @m AND property_id = @p;
        """, ps => { ps.Set("@m", SqlDbType.BigInt, long.Parse(matchId, CultureInfo.InvariantCulture)); ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@by", SqlDbType.Int, by); ps.Set("@at", SqlDbType.DateTime2, now); }, ct);

    public async Task IgnoreRowAsync(int propertyId, string rowId, string reason, CancellationToken ct)
    {
        var n = await F5Sql.ExecAsync(connections, "UPDATE acc.bank_statement_row SET status = 'ignored', ignore_reason = @r WHERE row_id = @i AND property_id = @p AND status = 'unmatched'",
            ps => { ps.Set("@i", SqlDbType.BigInt, long.Parse(rowId, CultureInfo.InvariantCulture)); ps.Set("@p", SqlDbType.Int, propertyId); ps.Set("@r", SqlDbType.NVarChar, reason, 200); }, ct).ConfigureAwait(false);
        if (n != 1) throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.INVALID_TRANSITION, "Only an unmatched row can be ignored.");
    }
}
