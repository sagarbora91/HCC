using System.Data;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.FrontDeskMasters;
using Hcc.Application.Import;
using Hcc.Domain.Errors;
using Hcc.Domain.FrontDeskMasters;
using Hcc.Domain.Import;
using Hcc.Infrastructure.SqlServer.OpsMasters;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.PeopleLists;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Import;

/// <summary>How a column's value travels between the database and the canonical cell text of <see cref="ImportLists"/>.</summary>
internal enum Cv
{
    Text,
    Int,
    Bool,
    Bps,
    Paise,
    Time,
    Date,
    Csv,
    Json,
    Meals,
    Roles,

    /// <summary>DECIMAL days (SCC leave quota): stored 12.50 ↔ cell 12.5.</summary>
    Dec,
}

/// <summary>
/// One field of a list table. <paramref name="Read"/> = SQL over alias t giving the stored value; <paramref name="Column"/> / <paramref name="Expr"/> = the column written and the SQL
/// that computes it from the parameter @v_&lt;key&gt; (default: the parameter itself); <paramref name="NotNull"/> = an empty cell writes the type's empty value instead of NULL;
/// <paramref name="Writes"/> = several columns from one cell (usable-at flags).
/// </summary>
internal sealed record Col(string Key, string Read, Cv Cv, string? Column = null, string? Expr = null, bool NotNull = false, (string Column, string Expr)[]? Writes = null, bool Hidden = false);

/// <summary>A dated amount of a row kept in a child table (price, rate): in force = latest effective date up to the business date.</summary>
internal sealed record Dated(string Key, string Table, string Fk, string DateCol, string AmountCol, string? KindCol = null, string? KindVal = null);

internal sealed record ListMap(string Wire, string From, string IdCol, string Insert, string Where, Col[] Cols, Dated[]? DatedCols = null, bool HasProperty = true)
{
    public string Table => From.Split(' ')[0];
}

internal static class ImportListMaps
{
    private static Col T(string key, string column, bool notNull = false) => new(key, "t." + column, Cv.Text, column, null, notNull);

    private static Col N(string key, string column, bool notNull = false) => new(key, "t." + column, Cv.Int, column, null, notNull);

    private static Col Dec(string key, string column) => new(key, "t." + column, Cv.Dec, column, null, false);

    private static Col B(string key, string column) => new(key, "t." + column, Cv.Bool, column, null, true);

    private static Col Look(string key, string column, string table, string idCol, string codeCol, string propertyFilter = "x.property_id = @p") =>
        new(key, $"(SELECT x.{codeCol} FROM {table} x WHERE x.{idCol} = t.{column})", Cv.Text, column, $"(SELECT x.{idCol} FROM {table} x WHERE {propertyFilter} AND x.{codeCol} = @v_{key})");

    private static ListMap M(string wire, string table, string idCol, params Col[] cols) =>
        new(wire, table + " t", idCol, table, "t.property_id = @p", cols);

    public static readonly IReadOnlyDictionary<string, ListMap> All = new ListMap[]
    {
        M("floors", "mst.floor", "floor_id"),
        M("amenities", "inv.amenity", "amenity_id", T("icon", "icon"), B("showOnWebsite", "show_on_website")),
        M("occupancy-charges", "inv.occupancy_charge", "charge_id", Look("roomType", "room_type_id", "inv.room_type", "room_type_id", "code"), N("childAgeFrom", "child_age_from"), N("childAgeTo", "child_age_to"))
            with
        {
            DatedCols =
            [
                new("extraAdult", "inv.occupancy_charge_rate", "charge_id", "effective_from", "amount_paise", "kind", "Adult"),
                new("extraChild", "inv.occupancy_charge_rate", "charge_id", "effective_from", "amount_paise", "kind", "Child"),
                new("extraBed", "inv.occupancy_charge_rate", "charge_id", "effective_from", "amount_paise", "kind", "Bed"),
            ],
        },
        M("meal-plans", "bill.meal_plan", "meal_plan_id", new Col("meals", "t.meals", Cv.Meals, "meals", null, true), T("gst", "gst_treatment"), new Col("roomTypes", "t.room_types", Cv.Csv, "room_types", null, true))
            with { DatedCols = [new("addOn", "bill.meal_plan_price", "meal_plan_id", "effective_from", "amount_paise")] },
        M("extras", "bill.extra_item", "extra_item_id", T("category", "category"), T("gst", "gst_category"), T("unit", "unit"), T("department", "department"))
            with { DatedCols = [new("price", "bill.extra_item_price", "extra_item_id", "effective_from", "amount_paise")] },
        M("discount-reasons", "bill.discount_reason", "discount_reason_id", new Col("maxPct", "t.max_bps", Cv.Bps, "max_bps", null, true), new Col("approvalAbovePct", "t.approval_above_bps", Cv.Bps, "approval_above_bps"),
            new Col("roles", "t.roles", Cv.Roles, "roles", null, true)),
        M("payment-methods", "mst.payment_method", "payment_method_id", T("kind", "kind"), Look("settlesTo", "settles_to_id", "mst.bank_account", "bank_account_id", "code"), B("needsReference", "needs_reference"),
            new Col("usableAt", "CONCAT_WS(';', IIF(t.usable_frontdesk = 1, 'frontdesk', NULL), IIF(t.usable_pos = 1, 'pos', NULL), IIF(t.usable_expenses = 1, 'expenses', NULL))", Cv.Csv, null, null, true,
                [("usable_frontdesk", "IIF(CHARINDEX('frontdesk', @v_usableAt) > 0, 1, 0)"), ("usable_pos", "IIF(CHARINDEX('pos', @v_usableAt) > 0, 1, 0)"), ("usable_expenses", "IIF(CHARINDEX('expenses', @v_usableAt) > 0, 1, 0)")]),
            new Col("surchargePct", "t.surcharge_bps", Cv.Bps, "surcharge_bps", null, true), T("ledger", "ledger")),
        M("bank-accounts", "mst.bank_account", "bank_account_id", T("kind", "kind"), T("bank", "bank"), new Col("accountLast4", "RTRIM(t.account_last4)", Cv.Text, "account_last4"), T("ifsc", "ifsc"),
            new Col("openingBalance", "t.opening_balance_paise", Cv.Paise, "opening_balance_paise", null, true), new Col("defaultFor", "t.default_for", Cv.Csv, "default_for", null, true)),
        M("expense-categories", "ops.expense_category", "expense_category_id", T("ledger", "ledger", true), B("needsBill", "needs_bill"), new Col("approvalLimit", "t.approval_limit_paise", Cv.Paise, "approval_limit_paise")),
        M("expense-vendors", "ops.expense_vendor", "expense_vendor_id", T("type", "vendor_type", true), T("gstin", "gstin"), B("tds", "tds"), T("contact", "contact"), T("terms", "terms"), new Col("pan", "t.pan", Cv.Text, null, null, false, null, true)),
        M("menu-categories", "pos.menu_category", "category_id"),
        new("pos-tables", "pos.outlet_table t JOIN pos.outlet o ON o.outlet_id = t.outlet_id", "table_id", "pos.outlet_table", "o.property_id = @p",
            [Look("outlet", "outlet_id", "pos.outlet", "outlet_id", "code"), T("tableNo", "table_no", true), N("seats", "seats"), T("section", "section"), B("counter", "is_counter")], null, false),
        M("booking-sources", "mst.booking_source", "row_id", T("segment", "segment", true), T("channel", "channel"), new Col("commissionBps", "t.commission_bps", Cv.Bps, "commission_bps", null, true)),
        M("id-types", "mst.id_type", "row_id", B("accepted", "accepted"), B("needsPhoto", "needs_photo"), B("foreignerOnly", "foreigner_only")),
        M("number-series", "bill.number_series", "row_id", T("document", "document", true), T("prefix", "prefix", true), N("start", "start_no", true), N("padding", "padding", true), T("reset", "reset_rule", true),
            new Col("activeFrom", "CONVERT(VARCHAR(10), t.active_from, 23)", Cv.Date, "active_from", null, true)),
        M("policies", "inv.policy", "row_id", N("freeCancelHours", "free_cancel_hours", true), new Col("cancelChargeBps", "t.cancel_charge_bps", Cv.Bps, "cancel_charge_bps", null, true),
            new Col("noShowChargeBps", "t.no_show_charge_bps", Cv.Bps, "no_show_charge_bps", null, true), new Col("depositBps", "t.deposit_bps", Cv.Bps, "deposit_bps", null, true),
            new Col("sources", "t.sources", Cv.Json, "sources", null, true)),
        M("designations", "hr.designation", "designation_id", T("dept", "dept_code"), T("grade", "grade"), T("defaultPayRule", "default_pay_rule")),
        M("shift-patterns", "hr.shift_pattern", "shift_pattern_id", new Col("start", "CONVERT(VARCHAR(5), t.start_time, 108)", Cv.Time, "start_time", "CAST(@v_start AS TIME(0))", true),
            new Col("end", "CONVERT(VARCHAR(5), t.end_time, 108)", Cv.Time, "end_time", "CAST(@v_end AS TIME(0))", true), N("breakMinutes", "break_minutes", true), B("night", "night"), T("weeklyOffRule", "weekly_off_rule", true)),
        M("leave-types", "hr.leave_type", "leave_type_id", B("paid", "paid"), Dec("yearlyQuota", "yearly_quota")),
        M("holidays", "hr.holiday", "holiday_id", T("kind", "kind", true)),
        M("hk-task-types", "ops.hk_task_type", "task_type_id", new Col("appliesTo", "t.applies_to", Cv.Csv, "applies_to", null, true), N("standardMinutes", "standard_minutes", true), B("needsInspection", "needs_inspection"),
            new Col("checklist", "t.checklist_json", Cv.Json, "checklist_json", null, true)),
        M("hk-zones", "ops.hk_zone", "zone_id", new Col("floors", "t.floor_codes", Cv.Csv, "floor_codes", null, true), new Col("rooms", "t.room_nos", Cv.Csv, "room_nos", null, true),
            Look("defaultAttendant", "default_attendant_id", "plat.app_user", "user_id", "login_name", "1 = 1")),
        M("complaint-categories", "gsv.complaint_category", "category_id", T("department", "department", true), N("slaHours", "sla_hours", true)),
        M("lost-found-categories", "gsv.lostfound_category", "category_id", T("department", "department", true), N("slaHours", "sla_hours", true), B("valuable", "valuable")),
    }.ToDictionary(m => m.Wire, StringComparer.Ordinal);
}

internal static class ImportListSql
{
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly (string Name, string Code)[] RoleCodes = [("Owner", "OWN"), ("Manager", "MGR"), ("Reception", "REC")];

    /// <summary>The batch of SELECTs of every list (one result set per list, in <see cref="ImportLists.All"/> order); @p property.</summary>
    public static string SnapshotSql()
    {
        var sb = new System.Text.StringBuilder("DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);\n");
        foreach (var d in ImportLists.All)
        {
            var m = ImportListMaps.All[d.Wire];
            var cols = string.Concat(m.Cols.Select(c => $", CAST({c.Read} AS NVARCHAR(4000))"));
            var dated = string.Concat((m.DatedCols ?? []).Select(x => $", CAST({DatedRead(x)} AS NVARCHAR(40))"));
            sb.Append($"SELECT t.{m.IdCol}, t.code, t.name, t.sort_order, t.active{cols}{dated} FROM {m.From} WHERE {m.Where} ORDER BY t.sort_order, t.code;\n");
        }

        return sb.ToString();
    }

    private static string DatedRead(Dated x) =>
        $"(SELECT TOP 1 c.{x.AmountCol} FROM {x.Table} c WHERE c.{x.Fk} = t.{x.Fk} {(x.KindCol is null ? "" : $"AND c.{x.KindCol} = '{x.KindVal}' ")}AND c.{x.DateCol} <= @bd ORDER BY c.{x.DateCol} DESC)";

    /// <summary>Reads every list's result set (in definition order) into the snapshot dictionary.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<ListSnapRow>>> ReadAsync(SqlDataReader rd, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<ListSnapRow>>(StringComparer.Ordinal);
        foreach (var d in ImportLists.All)
        {
            var m = ImportListMaps.All[d.Wire];
            var rows = new List<ListSnapRow>();
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                var i = 5;
                foreach (var c in m.Cols)
                {
                    var raw = rd.IsDBNull(i) ? null : rd.GetString(i);
                    i++;
                    if (c.Hidden) values[c.Key] = raw ?? string.Empty;
                    else values[c.Key] = Canon(d.Field(c.Key)!, c.Cv, raw);
                }

                foreach (var x in m.DatedCols ?? [])
                {
                    var raw = rd.IsDBNull(i) ? null : rd.GetString(i);
                    i++;
                    values[x.Key] = raw is null ? string.Empty : ImportLists.RupeesText(long.Parse(raw, CultureInfo.InvariantCulture));
                }

                rows.Add(new ListSnapRow(Convert.ToInt64(rd.GetValue(0), CultureInfo.InvariantCulture), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetBoolean(4), values));
            }

            result[d.Wire] = rows;
            await rd.NextResultAsync(ct).ConfigureAwait(false);
        }

        return result;
    }

    // ── stored value → canonical cell text ──

    public static string Canon(Hcc.Domain.Import.ListField f, Cv cv, string? raw)
    {
        switch (cv)
        {
            case Cv.Int:
            case Cv.Time:
            case Cv.Date:
                return raw ?? string.Empty;
            case Cv.Bool:
                return raw is "1" or "True" or "true" ? "yes" : "no";
            case Cv.Dec:
                return raw is null ? string.Empty : ImportLists.DaysText(decimal.Parse(raw, CultureInfo.InvariantCulture));
            case Cv.Bps:
                return raw is null ? string.Empty : ImportLists.PctText(int.Parse(raw, CultureInfo.InvariantCulture));
            case Cv.Paise:
                return raw is null ? string.Empty : ImportLists.RupeesText(long.Parse(raw, CultureInfo.InvariantCulture));
            case Cv.Csv:
                return ImportLists.CanonItems(f, (raw ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            case Cv.Meals:
                return ImportLists.CanonItems(f, (raw ?? string.Empty).Select(ch => ch.ToString()));
            case Cv.Roles:
                return ImportLists.CanonItems(f, (raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(code => RoleCodes.FirstOrDefault(r => r.Code == code).Name ?? code));
            case Cv.Json:
                try
                {
                    return ImportLists.CanonItems(f, JsonSerializer.Deserialize<List<string>>(raw ?? "[]") ?? []);
                }
                catch (JsonException)
                {
                    return string.Empty;
                }

            default:
                if (f.Kind == ListKind.Pick && raw is not null) return f.Options!.FirstOrDefault(o => string.Equals(o, raw, StringComparison.OrdinalIgnoreCase)) ?? raw;
                return raw ?? string.Empty;
        }
    }

    // ── canonical cell text → parameter ──

    public static void Bind(SqlCommand cmd, Col c, string canonical)
    {
        var name = "@v_" + c.Key;
        var empty = canonical.Length == 0;
        object? value;
        SqlDbType type;
        switch (c.Cv)
        {
            case Cv.Int:
                type = SqlDbType.Int;
                value = empty ? (c.NotNull ? 0 : null) : int.Parse(canonical, CultureInfo.InvariantCulture);
                break;
            case Cv.Bool:
                type = SqlDbType.Bit;
                value = canonical == "yes";
                break;
            case Cv.Dec:
                type = SqlDbType.Decimal;
                value = empty ? null : decimal.Parse(canonical, CultureInfo.InvariantCulture);
                break;
            case Cv.Bps:
                type = SqlDbType.Int;
                value = empty ? (c.NotNull ? 0 : null) : ImportLists.BpsOf(canonical);
                break;
            case Cv.Paise:
                type = SqlDbType.BigInt;
                value = empty ? (c.NotNull ? 0L : null) : ImportLists.PaiseOf(canonical);
                break;
            case Cv.Date:
                type = SqlDbType.Date;
                value = empty ? null : DateOnly.ParseExact(canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
                break;
            case Cv.Csv:
                type = SqlDbType.NVarChar;
                value = string.Join(',', ImportLists.Items(canonical));
                break;
            case Cv.Json:
                type = SqlDbType.NVarChar;
                value = JsonSerializer.Serialize(ImportLists.Items(canonical), Relaxed);
                break;
            case Cv.Meals:
                type = SqlDbType.NVarChar;
                value = string.Concat(ImportLists.Items(canonical));
                break;
            case Cv.Roles:
                type = SqlDbType.NVarChar;
                value = string.Join(',', ImportLists.Items(canonical).Select(r => RoleCodes.First(x => x.Name == r).Code));
                break;
            default:
                type = SqlDbType.NVarChar;
                value = empty ? (c.NotNull ? string.Empty : null) : canonical;
                break;
        }

        var p = type == SqlDbType.NVarChar ? cmd.Parameters.Add(name, type, 4000) : cmd.Parameters.Add(name, type);
        if (type == SqlDbType.Decimal)
        {
            p.Precision = 9;
            p.Scale = 2;
        }

        p.Value = value ?? DBNull.Value;
    }

    public static string WriteExpr(Col c) => c.Expr ?? "@v_" + c.Key;
}

/// <summary>
/// Writer of the list masters for the import commit (<see cref="IImportListWriter"/>): one generic upsert over the table maps of
/// <see cref="ImportListMaps"/>, the change history of the masters framework, dated amounts from the business date, and the seeds some
/// modules store on their first change.
/// </summary>
public sealed class SqlImportListWriter(IFrontDeskMastersStore frontDesk, ISettingsReader? settings = null) : IImportListWriter
{
    private static readonly HashSet<string> FrontDesk = ["booking-sources", "id-types", "number-series", "policies"];
    private static readonly HashSet<string> People = ["designations", "shift-patterns", "leave-types", "holidays"];
    private static readonly HashSet<string> Money = ["payment-methods", "bank-accounts", "expense-categories", "expense-vendors"];
    private static readonly HashSet<string> Ops = ["hk-task-types", "hk-zones", "complaint-categories", "lost-found-categories"];

    private static string Short(string? v) => v is null ? string.Empty : v.Length <= 200 ? v : v[..199] + "…";

    public async Task EnsureSeededAsync(ICommandTransaction tx, int propertyId, string wire, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var t = ImportSql.Sql(tx);
        if (People.Contains(wire))
        {
            await PlSql.SeedAsync(t.Command, propertyId, ct).ConfigureAwait(false);
        }
        else if (Money.Contains(wire))
        {
            await using var cmd = t.Command("EXEC mst.usp_seed_money_masters @p;");
            cmd.P("@p", SqlDbType.Int, propertyId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        else if (Ops.Contains(wire))
        {
            await using var cmd = t.Command(OpsMastersSeed.Sql);
            cmd.P("@p", SqlDbType.Int, propertyId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        else if (FrontDesk.Contains(wire))
        {
            var master = wire switch
            {
                "booking-sources" => FrontDeskMasterIds.BookingSources,
                "id-types" => FrontDeskMasterIds.IdTypes,
                "number-series" => FrontDeskMasterIds.NumberSeries,
                _ => FrontDeskMasterIds.Policies,
            };
            var rows = await frontDesk.LockAllAsync(tx, propertyId, master, ct).ConfigureAwait(false);
            if (rows.Count > 0) return;
            var prefix = master == FrontDeskMasterIds.NumberSeries && settings is not null ? (await settings.GetAsync(tx, propertyId, "invoicePrefix", null, ct).ConfigureAwait(false)).Text : null;
            var defaults = FrontDeskMastersRules.Defaults(master, prefix);
            var sort = 0;
            foreach (var d in defaults)
                await frontDesk.InsertAsync(tx, propertyId, master, d.Code, d.Name, sort += 10, d.Fields, tx.Context.PrincipalId, DateTime.UtcNow, ct).ConfigureAwait(false);
            await tx.AuditAsync("MST_FDM", "mst-" + master + ":defaults", $"Master {FrontDeskMastersRules.Label(master)}: {defaults.Count} default rows stored", ct).ConfigureAwait(false);
        }
    }

    public async Task<long> UpsertAsync(ICommandTransaction tx, int propertyId, ListDef def, ListRowData row, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(row);
        var m = ImportListMaps.All[def.Wire];
        var t = ImportSql.Sql(tx);
        var writes = m.Cols.Where(c => !c.Hidden).ToList();
        var created = row.ExistingId is null;
        if (created && def.Wire == "number-series") await EnsureSeriesMayStartAsync(tx, propertyId, row, businessDate, ct).ConfigureAwait(false);
        long id;
        var active = row.Active ?? true;
        if (created)
        {
            var cols = new List<string> { "code", "name", "sort_order", "active", "retired_on", "updated_by", "updated_utc" };
            var vals = new List<string> { "@code", "@name", "@sort", "@active", "CASE WHEN @active = 1 THEN NULL ELSE @bd END", "@u", "@utc" };
            if (m.HasProperty)
            {
                cols.Insert(0, "property_id");
                vals.Insert(0, "@p");
            }

            foreach (var c in writes)
            {
                if (c.Writes is { } many)
                {
                    foreach (var (col, expr) in many)
                    {
                        cols.Add(col);
                        vals.Add(expr);
                    }
                }
                else if (c.Column is not null)
                {
                    cols.Add(c.Column);
                    vals.Add(ImportListSql.WriteExpr(c));
                }
            }

            await using var cmd = t.Command($"INSERT {m.Insert}({string.Join(", ", cols)}) OUTPUT INSERTED.{m.IdCol} VALUES ({string.Join(", ", vals)});");
            Common(cmd, propertyId, row, active, businessDate, principalId, utc);
            foreach (var c in writes.Where(c => c.Column is not null || c.Writes is not null)) ImportListSql.Bind(cmd, c, row.Values.GetValueOrDefault(c.Key, string.Empty));
            id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        else
        {
            id = row.ExistingId!.Value;
            var sets = new List<string> { "name = @name", "sort_order = @sort", "updated_by = @u", "updated_utc = @utc" };
            if (row.Active is not null) sets.Add("active = @active, retired_on = CASE WHEN @active = 1 THEN NULL ELSE ISNULL(retired_on, @bd) END");
            var changed = row.Changes.Select(c => c.Field).ToHashSet(StringComparer.Ordinal);
            foreach (var c in writes.Where(c => changed.Contains(c.Key)))
            {
                if (c.Writes is { } many) sets.AddRange(many.Select(w => $"{w.Column} = {w.Expr}"));
                else if (c.Column is not null) sets.Add($"{c.Column} = {ImportListSql.WriteExpr(c)}");
            }

            await using var cmd = t.Command($"UPDATE t SET {string.Join(", ", sets)} FROM {m.Insert} t WHERE t.{m.IdCol} = @id;");
            Common(cmd, propertyId, row, active, businessDate, principalId, utc);
            cmd.P("@id", SqlDbType.BigInt, id);
            foreach (var c in writes.Where(c => changed.Contains(c.Key) && (c.Column is not null || c.Writes is not null))) ImportListSql.Bind(cmd, c, row.Values.GetValueOrDefault(c.Key, string.Empty));
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var x in m.DatedCols ?? []) await WriteDatedAsync(t, x, id, row.Values.GetValueOrDefault(x.Key, string.Empty), businessDate, ct).ConfigureAwait(false);
        await HistoryAsync(t, propertyId, def, row, id, created, principalId, utc, ct).ConfigureAwait(false);
        return id;
    }

    private static void Common(SqlCommand cmd, int propertyId, ListRowData row, bool active, DateOnly bd, int principalId, DateTime utc)
    {
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@code", SqlDbType.VarChar, row.Code, 20);
        cmd.P("@name", SqlDbType.NVarChar, row.Name, 60);
        cmd.P("@sort", SqlDbType.Int, row.SortOrder);
        cmd.P("@active", SqlDbType.Bit, active);
        cmd.P("@bd", SqlDbType.Date, bd.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
    }

    private async Task EnsureSeriesMayStartAsync(ICommandTransaction tx, int propertyId, ListRowData row, DateOnly businessDate, CancellationToken ct)
    {
        var doc = row.Values["document"];
        var activity = await frontDesk.DocumentsAsync(tx, propertyId, doc, null, null, ct).ConfigureAwait(false);
        FrontDeskMastersRules.EnsureStartFree(doc, row.Values["reset"], DateOnly.ParseExact(row.Values["activeFrom"], "yyyy-MM-dd", CultureInfo.InvariantCulture), businessDate, activity.LastDocument, activity.FyCodes);
    }

    /// <summary>A dated amount: when it differs from the amount in force on the business date, the amount starts from the business date (an amount already in effect is never rewritten).</summary>
    private static async Task WriteDatedAsync(SqlCommandTransaction t, Dated x, long id, string canonical, DateOnly bd, CancellationToken ct)
    {
        if (canonical.Length == 0) return;
        var paise = ImportLists.PaiseOf(canonical)!.Value;
        var kind = x.KindCol is null ? string.Empty : $" AND {x.KindCol} = '{x.KindVal}'";
        await using var cmd = t.Command($"""
            DECLARE @cur BIGINT = (SELECT TOP 1 {x.AmountCol} FROM {x.Table} WHERE {x.Fk} = @id{kind} AND {x.DateCol} <= @bd ORDER BY {x.DateCol} DESC);
            IF @cur IS NULL OR @cur <> @a
            BEGIN
              DELETE {x.Table} WHERE {x.Fk} = @id{kind} AND {x.DateCol} = @bd;
              INSERT {x.Table}({x.Fk}{(x.KindCol is null ? "" : ", " + x.KindCol)}, {x.DateCol}, {x.AmountCol}) VALUES (@id{(x.KindCol is null ? "" : ", '" + x.KindVal + "'")}, @bd, @a);
            END
            """);
        cmd.P("@id", SqlDbType.BigInt, id);
        cmd.P("@bd", SqlDbType.Date, bd.ToDateTime(TimeOnly.MinValue));
        cmd.P("@a", SqlDbType.BigInt, paise);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task HistoryAsync(SqlCommandTransaction t, int propertyId, ListDef def, ListRowData row, long id, bool created, int principalId, DateTime utc, CancellationToken ct)
    {
        var lines = new List<(string Field, string? Old, string? New)>();
        if (created)
        {
            lines.Add(("code", null, row.Code));
            lines.Add(("name", null, row.Name));
            lines.Add(("sortOrder", null, row.SortOrder.ToString(CultureInfo.InvariantCulture)));
            foreach (var f in def.Fields)
                if (row.Values.GetValueOrDefault(f.Key, string.Empty) is { Length: > 0 } v) lines.Add((f.Key, null, v));
        }
        else
        {
            lines.AddRange(row.Changes);
        }

        foreach (var (field, o, n) in lines)
        {
            await using var cmd = t.Command("""
                INSERT mst.master_history(property_id, master, row_id, code, field, old_value, new_value, changed_by, changed_utc)
                VALUES (@p, @m, @r, @c, @f, @o, @n, @u, @utc);
                """);
            cmd.P("@p", SqlDbType.Int, propertyId);
            cmd.P("@m", SqlDbType.VarChar, def.Proto, 30);
            cmd.P("@r", SqlDbType.BigInt, id);
            cmd.P("@c", SqlDbType.VarChar, row.Code.Length <= 20 ? row.Code : row.Code[..20], 20);
            cmd.P("@f", SqlDbType.VarChar, field, 40);
            cmd.P("@o", SqlDbType.NVarChar, o is null ? null : Short(o), 200);
            cmd.P("@n", SqlDbType.NVarChar, n is null ? null : Short(n), 200);
            cmd.P("@u", SqlDbType.Int, principalId);
            cmd.P("@utc", SqlDbType.DateTime2, utc);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task ApplyOutletAsync(ICommandTransaction tx, int propertyId, int outletId, OutletData row, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = ImportSql.Sql(tx).Command("""
            UPDATE pos.outlet SET name = @name, outlet_type = @type,
              opens_at = CASE WHEN @tg = 1 THEN @opens ELSE opens_at END, closes_at = CASE WHEN @tg = 1 THEN @closes ELSE closes_at END,
              gst_profile = ISNULL(@profile, gst_profile), kot_station = CASE WHEN @sg = 1 THEN @station ELSE kot_station END,
              sort_order = ISNULL(@sort, sort_order),
              active = ISNULL(@active, active), retired_on = CASE WHEN ISNULL(@active, active) = 1 THEN NULL ELSE ISNULL(retired_on, @bd) END,
              updated_by = @u, updated_utc = @utc
            WHERE outlet_id = @id AND property_id = @p;
            """);
        cmd.P("@id", SqlDbType.Int, outletId);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@name", SqlDbType.NVarChar, row.Name, 100);
        cmd.P("@type", SqlDbType.VarChar, row.Type.Length == 0 ? (row.Kind == "room" ? "room-service" : "restaurant") : row.Type, 12);
        cmd.P("@tg", SqlDbType.Bit, row.TimesGiven);
        cmd.P("@opens", SqlDbType.Char, row.Opens, 5);
        cmd.P("@closes", SqlDbType.Char, row.Closes, 5);
        cmd.P("@profile", SqlDbType.VarChar, row.GstProfile, 9);
        cmd.P("@sg", SqlDbType.Bit, row.StationGiven);
        cmd.P("@station", SqlDbType.NVarChar, row.KotStation, 20);
        cmd.P("@sort", SqlDbType.Int, row.SortOrder);
        cmd.P("@active", SqlDbType.Bit, row.Active);
        cmd.P("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task ApplyMenuItemAsync(ICommandTransaction tx, int propertyId, int itemId, MenuItemData row, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = ImportSql.Sql(tx).Command("""
            UPDATE pos.menu_item SET
              short_code = CASE WHEN @scg = 1 THEN @sc ELSE short_code END, gst_class = ISNULL(@gst, gst_class),
              station = CASE WHEN @sg = 1 THEN @station ELSE station END, sort_order = ISNULL(@sort, sort_order),
              active = ISNULL(@active, active), retired_on = CASE WHEN ISNULL(@active, active) = 1 THEN NULL ELSE ISNULL(retired_on, @bd) END,
              updated_by = @u, updated_utc = @utc
            WHERE item_id = @id AND property_id = @p;
            """);
        cmd.P("@id", SqlDbType.Int, itemId);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@scg", SqlDbType.Bit, row.ShortCodeGiven);
        cmd.P("@sc", SqlDbType.VarChar, row.ShortCode, 10);
        cmd.P("@gst", SqlDbType.VarChar, row.GstClass, 7);
        cmd.P("@sg", SqlDbType.Bit, row.StationGiven);
        cmd.P("@station", SqlDbType.NVarChar, row.Station, 20);
        cmd.P("@sort", SqlDbType.Int, row.SortOrder);
        cmd.P("@active", SqlDbType.Bit, row.Active);
        cmd.P("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
