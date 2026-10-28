using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.MoneyMasters;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.MoneyMasters;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;
using R = Hcc.Domain.MoneyMasters.MoneyMastersRules;

namespace Hcc.Infrastructure.SqlServer.MoneyMasters;

/// <summary>Shared SQL of the money-master adapters (migration 2830: mst.payment_method, mst.bank_account, ops.expense_category,
/// ops.expense_vendor; history in mst.master_history from 2801).</summary>
internal static class MnySql
{
    public sealed record Table(string Name, string Id, string Alias, string Select, string Join);

    private const string Base = "{0}.{1}, {0}.code, {0}.name, {0}.sort_order, {0}.active, {0}.retired_on, {0}.updated_utc, {0}.row_ver";

    public static Table Of(string master) => master switch
    {
        MoneyMasterIds.PaymentMethods => new("mst.payment_method", "payment_method_id", "m",
            Fmt("m", "payment_method_id") + ", m.kind, a.code, m.needs_reference, m.usable_frontdesk, m.usable_pos, m.usable_expenses, m.surcharge_bps, m.ledger",
            " LEFT JOIN mst.bank_account a ON a.bank_account_id = m.settles_to_id"),
        MoneyMasterIds.BankAccounts => new("mst.bank_account", "bank_account_id", "m",
            Fmt("m", "bank_account_id") + ", m.kind, m.bank, m.account_last4, m.ifsc, m.opening_balance_paise, m.default_for", ""),
        MoneyMasterIds.ExpenseCategories => new("ops.expense_category", "expense_category_id", "m",
            Fmt("m", "expense_category_id") + ", m.ledger, m.needs_bill, m.approval_limit_paise, m.itc_blocked, m.rcm, m.rcm_bps, m.capital", ""), // ACC-2 (4231)
        MoneyMasterIds.ExpenseVendors => new("ops.expense_vendor", "expense_vendor_id", "m",
            Fmt("m", "expense_vendor_id") + ", m.vendor_type, m.gstin, m.pan, m.tds, m.contact, m.terms, m.gst_state, m.tds_section", ""), // ACC-2 (4231)
        _ => throw new ArgumentOutOfRangeException(nameof(master), master, "Unknown money master."),
    };

    private static string Fmt(string alias, string id) => string.Format(CultureInfo.InvariantCulture, Base, alias, id);

    public static string SelectSql(string master, bool locked)
    {
        var t = Of(master);
        return $"SELECT {t.Select} FROM {t.Name} m{(locked ? " WITH (UPDLOCK, HOLDLOCK)" : "")}{t.Join} WHERE m.property_id = @p ORDER BY m.sort_order, m.code;";
    }

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i).TrimEnd();

    private static IReadOnlyList<string> Csv(string? v) => string.IsNullOrEmpty(v) ? [] : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static MoneyRow Row(string master, SqlDataReader rd)
    {
        var f = new Dictionary<string, object?>();
        switch (master)
        {
            case MoneyMasterIds.PaymentMethods:
                f[R.Kind] = rd.GetString(8);
                f[R.SettlesTo] = S(rd, 9);
                f[R.NeedsReference] = rd.GetBoolean(10);
                f[R.UsableAt] = MoneyMasterValues.Places.Where((_, i) => rd.GetBoolean(11 + i)).ToList();
                f[R.SurchargePct] = rd.GetInt32(14);
                f[R.Ledger] = S(rd, 15);
                break;
            case MoneyMasterIds.BankAccounts:
                f[R.Kind] = rd.GetString(8);
                f[R.Bank] = S(rd, 9);
                f[R.AccountLast4] = S(rd, 10);
                f[R.Ifsc] = S(rd, 11);
                f[R.OpeningBalance] = rd.GetInt64(12);
                f[R.DefaultFor] = Csv(S(rd, 13));
                break;
            case MoneyMasterIds.ExpenseCategories:
                f[R.Ledger] = S(rd, 8);
                f[R.NeedsBill] = rd.GetBoolean(9);
                f[R.ApprovalLimit] = rd.IsDBNull(10) ? null : rd.GetInt64(10);
                f[R.ItcBlocked] = rd.GetBoolean(11);
                f[R.Rcm] = rd.GetBoolean(12);
                f[R.RcmBps] = rd.IsDBNull(13) ? null : rd.GetInt32(13);
                f[R.Capital] = rd.GetBoolean(14);
                break;
            default:
                f[R.Type] = rd.GetString(8);
                f[R.Gstin] = S(rd, 9);
                f[R.Pan] = S(rd, 10);
                f[R.Tds] = rd.GetBoolean(11);
                f[R.State] = S(rd, 14);
                f[R.TdsSection] = S(rd, 15);
                f[R.Contact] = S(rd, 12);
                f[R.Terms] = S(rd, 13);
                break;
        }

        return new MoneyRow(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4),
            rd.IsDBNull(5) ? null : DateOnly.FromDateTime(rd.GetDateTime(5)), DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), (byte[])rd[7], f);
    }

    public static async Task<IReadOnlyList<MoneyRow>> ReadRowsAsync(string master, SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<MoneyRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Row(master, rd));
        return list;
    }

    /// <summary>The master's own columns and their parameters (SET list for UPDATE, column + value lists for INSERT).</summary>
    public static (string[] Columns, string[] Values) Fields(string master) => master switch
    {
        MoneyMasterIds.PaymentMethods => (["kind", "settles_to_id", "needs_reference", "usable_frontdesk", "usable_pos", "usable_expenses", "surcharge_bps", "ledger"],
            ["@f_kind", "(SELECT bank_account_id FROM mst.bank_account WHERE property_id = @p AND code = @f_settles)", "@f_ref", "@f_fd", "@f_pos", "@f_exp", "@f_bps", "@f_ledger"]),
        MoneyMasterIds.BankAccounts => (["kind", "bank", "account_last4", "ifsc", "opening_balance_paise", "default_for"],
            ["@f_kind", "@f_bank", "@f_last4", "@f_ifsc", "@f_opening", "@f_default"]),
        MoneyMasterIds.ExpenseCategories => (["ledger", "needs_bill", "approval_limit_paise", "itc_blocked", "rcm", "rcm_bps", "capital"],
            ["@f_ledger", "@f_bill", "@f_limit", "@f_itcb", "@f_rcm", "@f_rbps", "@f_cap"]),
        _ => (["vendor_type", "gstin", "pan", "tds", "contact", "terms", "gst_state", "tds_section"],
            ["@f_type", "@f_gstin", "@f_pan", "@f_tds", "@f_contact", "@f_terms", "@f_state", "@f_tsec"]),
    };

    public static void FieldParams(string master, SqlCommand cmd, IReadOnlyDictionary<string, object?> v)
    {
        string? T(string k) => v.GetValueOrDefault(k) as string;
        bool B(string k) => v.GetValueOrDefault(k) is true;
        IReadOnlyList<string> L(string k) => v.GetValueOrDefault(k) as IReadOnlyList<string> ?? [];
        switch (master)
        {
            case MoneyMasterIds.PaymentMethods:
                cmd.P("@f_kind", SqlDbType.VarChar, T(R.Kind), 6);
                cmd.P("@f_settles", SqlDbType.VarChar, T(R.SettlesTo), 20);
                cmd.P("@f_ref", SqlDbType.Bit, B(R.NeedsReference));
                cmd.P("@f_fd", SqlDbType.Bit, L(R.UsableAt).Contains(MoneyMasterValues.FrontDesk));
                cmd.P("@f_pos", SqlDbType.Bit, L(R.UsableAt).Contains(MoneyMasterValues.Pos));
                cmd.P("@f_exp", SqlDbType.Bit, L(R.UsableAt).Contains(MoneyMasterValues.Expenses));
                cmd.P("@f_bps", SqlDbType.Int, v.GetValueOrDefault(R.SurchargePct) is int bps ? bps : 0);
                cmd.P("@f_ledger", SqlDbType.VarChar, T(R.Ledger), 4);
                break;
            case MoneyMasterIds.BankAccounts:
                cmd.P("@f_kind", SqlDbType.VarChar, T(R.Kind), 4);
                cmd.P("@f_bank", SqlDbType.NVarChar, T(R.Bank), 60);
                cmd.P("@f_last4", SqlDbType.Char, T(R.AccountLast4), 4);
                cmd.P("@f_ifsc", SqlDbType.VarChar, T(R.Ifsc), 11);
                cmd.P("@f_opening", SqlDbType.BigInt, v.GetValueOrDefault(R.OpeningBalance) is long o ? o : 0L);
                cmd.P("@f_default", SqlDbType.VarChar, string.Join(',', L(R.DefaultFor)), 40);
                break;
            case MoneyMasterIds.ExpenseCategories:
                cmd.P("@f_ledger", SqlDbType.VarChar, T(R.Ledger), 4);
                cmd.P("@f_bill", SqlDbType.Bit, B(R.NeedsBill));
                cmd.P("@f_limit", SqlDbType.BigInt, v.GetValueOrDefault(R.ApprovalLimit) as long?);
                cmd.P("@f_itcb", SqlDbType.Bit, B(R.ItcBlocked));
                cmd.P("@f_rcm", SqlDbType.Bit, B(R.Rcm));
                cmd.P("@f_rbps", SqlDbType.Int, v.GetValueOrDefault(R.RcmBps) as int?);
                cmd.P("@f_cap", SqlDbType.Bit, B(R.Capital));
                break;
            default:
                cmd.P("@f_type", SqlDbType.VarChar, T(R.Type), 8);
                cmd.P("@f_gstin", SqlDbType.VarChar, T(R.Gstin), 15);
                cmd.P("@f_pan", SqlDbType.VarChar, T(R.Pan), 10);
                cmd.P("@f_tds", SqlDbType.Bit, B(R.Tds));
                cmd.P("@f_contact", SqlDbType.NVarChar, T(R.Contact), 100);
                cmd.P("@f_terms", SqlDbType.NVarChar, T(R.Terms), 60);
                cmd.P("@f_state", SqlDbType.Char, T(R.State), 2);
                cmd.P("@f_tsec", SqlDbType.VarChar, T(R.TdsSection), 20);
                break;
        }
    }

    /// <summary>Stored record values of a master with their counts (one query per source table, property-scoped).</summary>
    public static string? ValueCountsSql(string master) => master switch
    {
        MoneyMasterIds.PaymentMethods => """
            SELECT r.method, COUNT(*) FROM pay.receipt r JOIN bill.folio f ON f.folio_id = r.folio_id JOIN inv.reservation v ON v.reservation_id = f.reservation_id
             WHERE v.property_id = @p GROUP BY r.method
            UNION ALL SELECT b.tender, COUNT(*) FROM pos.bill b WHERE b.property_id = @p AND b.tender IS NOT NULL GROUP BY b.tender
            UNION ALL SELECT e.method, COUNT(*) FROM ops.expense e WHERE e.property_id = @p GROUP BY e.method;
            """,
        MoneyMasterIds.ExpenseCategories => "SELECT e.category, COUNT(*) FROM ops.expense e WHERE e.property_id = @p GROUP BY e.category;",
        MoneyMasterIds.ExpenseVendors => "SELECT LTRIM(RTRIM(e.vendor)), COUNT(*) FROM ops.expense e WHERE e.property_id = @p GROUP BY LTRIM(RTRIM(e.vendor));",
        _ => null,
    };

    public const string RenamesSql = "SELECT h.old_value, h.row_id FROM mst.master_history h WHERE h.property_id = @p AND h.master = @m AND h.field = 'name' AND h.old_value IS NOT NULL ORDER BY h.history_id;";

    public static async Task<IReadOnlyList<(string, int)>> ReadCountsAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<(string, int)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) if (!rd.IsDBNull(0)) list.Add((rd.GetString(0), rd.GetInt32(1)));
        return list;
    }

    public static async Task<IReadOnlyDictionary<string, int>> ReadRenamesAsync(SqlCommand cmd, CancellationToken ct)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) map[rd.GetString(0)] = (int)rd.GetInt64(1); // a later rename of the same text wins
        return map;
    }
}

/// <summary>SQL adapter of <see cref="IMoneyMastersStore"/>: rows locked with UPDLOCK, HOLDLOCK on the property's set (code uniqueness, reorder).</summary>
public sealed class SqlMoneyMastersStore : IMoneyMastersStore
{
    public async Task SeedAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("EXEC mst.usp_seed_money_masters @p;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MoneyRow>> LockAllAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command(MnySql.SelectSql(master, true));
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await MnySql.ReadRowsAsync(master, cmd, ct).ConfigureAwait(false);
    }

    public async Task<int> InsertAsync(ICommandTransaction tx, int propertyId, string master, MoneyDraft draft, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var t = MnySql.Of(master);
        var (cols, vals) = MnySql.Fields(master);
        await using var cmd = MstSql.Sql(tx).Command($"""
            INSERT {t.Name}(property_id, code, name, sort_order, active, {string.Join(", ", cols)}, updated_by, updated_utc) OUTPUT INSERTED.{t.Id}
            VALUES (@p, @c, @n, @s, 1, {string.Join(", ", vals)}, @u, @utc);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, draft.Row.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, draft.Row.Name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        MnySql.FieldParams(master, cmd, draft.Fields);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateAsync(ICommandTransaction tx, int propertyId, string master, int id, byte[] expectedVersion, MoneyDraft draft, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var t = MnySql.Of(master);
        var (cols, vals) = MnySql.Fields(master);
        var set = string.Join(", ", cols.Zip(vals, (c, v) => $"{c} = {v}"));
        await using var cmd = MstSql.Sql(tx).Command($"UPDATE {t.Name} SET code = @c, name = @n, sort_order = @s, {set}, updated_by = @u, updated_utc = @utc WHERE {t.Id} = @id AND row_ver = @v;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, draft.Row.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, draft.Row.Name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        MnySql.FieldParams(master, cmd, draft.Fields);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This row");
    }

    public async Task SetActiveAsync(ICommandTransaction tx, string master, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = MnySql.Of(master);
        await using var cmd = MstSql.Sql(tx).Command($"UPDATE {t.Name} SET active = @a, retired_on = @r, updated_by = @u, updated_utc = @utc WHERE {t.Id} = @id AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@r", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This row");
    }

    public async Task SetSortAsync(ICommandTransaction tx, string master, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var t = MnySql.Of(master);
        await using var cmd = MstSql.Sql(tx).Command($"UPDATE {t.Name} SET sort_order = @s, updated_by = @u, updated_utc = @utc WHERE {t.Id} = @id;");
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ICommandTransaction tx, string master, int id, byte[] expectedVersion, CancellationToken ct = default)
    {
        var t = MnySql.Of(master);
        await using var cmd = MstSql.Sql(tx).Command($"DELETE {t.Name} WHERE {t.Id} = @id AND row_ver = @v;");
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This row");
    }

    public async Task<IReadOnlyList<(string Value, int Count)>> ValueCountsAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default)
    {
        if (MnySql.ValueCountsSql(master) is not { } sql) return [];
        await using var cmd = MstSql.Sql(tx).Command(sql);
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await MnySql.ReadCountsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, int>> RenamesAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command(MnySql.RenamesSql);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@m", SqlDbType.VarChar, master, 30);
        return await MnySql.ReadRenamesAsync(cmd, ct).ConfigureAwait(false);
    }
}

/// <summary>Read side (own connection). The first read of a property that has no money masters yet seeds them (idempotent procedure).</summary>
public sealed class SqlMoneyMastersReadStore(SqlConnectionFactory connections) : IMoneyMastersReadStore
{
    public async Task<IReadOnlyList<MoneyRow>> RowsAsync(int propertyId, string master, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using (var seed = connections.Command("EXEC mst.usp_seed_money_masters @p;", conn))
        {
            seed.P("@p", SqlDbType.Int, propertyId);
            await seed.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var cmd = connections.Command(MnySql.SelectSql(master, false), conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await MnySql.ReadRowsAsync(master, cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(string Value, int Count)>> ValueCountsAsync(int propertyId, string master, CancellationToken ct = default)
    {
        if (MnySql.ValueCountsSql(master) is not { } sql) return [];
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(sql, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await MnySql.ReadCountsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, int>> RenamesAsync(int propertyId, string master, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(MnySql.RenamesSql, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@m", SqlDbType.VarChar, master, 30);
        return await MnySql.ReadRenamesAsync(cmd, ct).ConfigureAwait(false);
    }
}
