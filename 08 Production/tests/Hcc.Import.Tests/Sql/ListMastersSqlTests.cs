using Hcc.Contracts;
using Hcc.Contracts.Import;
using Hcc.Domain.Errors;
using Hcc.Domain.Import;
using Hcc.Import.Tests.Harness;

namespace Hcc.Import.Tests.Sql;

/// <summary>
/// MA-8: the list masters of the masters wave import through the generic list writer into the owning modules' tables, re-import and
/// export/import round trips plan every row as "Unchanged", dated amounts start from the business date, files never retire rows, seeds
/// the modules store on their first change are kept, and money lists need the Owner (DEC-058).
/// </summary>
[Trait("Category", "Sql")]
public class ListMastersSqlTests : IAsyncLifetime
{
    private ImportWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await ImportWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    /// <summary>Dependency order of the masters wave files (README "The order to import", steps 16 on) after the files they need.</summary>
    private static readonly string[] Base = ["room-types", "rooms", "staff", "departments", "pos-outlets", "pos-menu"];

    private static readonly string[] Lists =
    [
        "floors", "amenities", "occupancy-charges", "meal-plans", "extras", "discount-reasons", "bank-accounts", "payment-methods", "expense-categories", "expense-vendors",
        "menu-categories", "pos-tables", "booking-sources", "policies", "id-types", "number-series", "designations", "shift-patterns", "leave-types", "holidays",
        "hk-task-types", "hk-zones", "complaint-categories", "lost-found-categories",
    ];

    private async Task ImportAllAsync()
    {
        foreach (var m in Base) await _w.ImportSampleAsync(m);
        foreach (var m in Lists) await _w.ImportSampleAsync(m);
    }

    private Task<long> Count(string sql) => _w.CountAsync(sql);

    [Fact]
    public async Task Every_list_sample_commits_into_its_tables_with_history_and_dated_amounts()
    {
        await ImportAllAsync();

        Assert.Equal(3, await Count("SELECT COUNT(*) FROM mst.floor"));
        Assert.Equal("Ground floor", (string?)await _w.ScalarAsync("SELECT name FROM mst.floor WHERE code = 'F1'"));
        Assert.Equal(4, await Count("SELECT COUNT(*) FROM inv.amenity"));
        Assert.Equal(0, await Count("SELECT CAST(show_on_website AS int) FROM inv.amenity WHERE code = 'TEAMAKER'"));
        Assert.Equal("coffee", (string?)await _w.ScalarAsync("SELECT icon FROM inv.amenity WHERE code = 'TEAMAKER'"));

        // dated amounts: one row per kind from the business date (26 Oct 2026)
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM inv.occupancy_charge"));
        Assert.Equal(6, await Count("SELECT COUNT(*) FROM inv.occupancy_charge_rate"));
        Assert.Equal(80000, await Count("SELECT r.amount_paise FROM inv.occupancy_charge_rate r JOIN inv.occupancy_charge c ON c.charge_id = r.charge_id WHERE c.code = 'OC-DLX' AND r.kind = 'Adult' AND r.effective_from = '2026-10-26'"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM inv.occupancy_charge c JOIN inv.room_type t ON t.room_type_id = c.room_type_id WHERE c.code = 'OC-STE' AND t.code = 'STE' AND c.child_age_from = 6 AND c.child_age_to = 12"));

        Assert.Equal(3, await Count("SELECT COUNT(*) FROM bill.meal_plan"));
        Assert.Equal("BD", (string?)await _w.ScalarAsync("SELECT meals FROM bill.meal_plan WHERE code = 'MAP'"));
        Assert.Equal("DLX,STE", (string?)await _w.ScalarAsync("SELECT room_types FROM bill.meal_plan WHERE code = 'MAP'"));
        Assert.Equal("bundled", (string?)await _w.ScalarAsync("SELECT gst_treatment FROM bill.meal_plan WHERE code = 'CP'"));
        Assert.Equal(75000, await Count("SELECT p.amount_paise FROM bill.meal_plan_price p JOIN bill.meal_plan m ON m.meal_plan_id = p.meal_plan_id WHERE m.code = 'MAP'"));
        Assert.Equal(4, await Count("SELECT COUNT(*) FROM bill.extra_item_price"));
        Assert.Equal(25000, await Count("SELECT p.amount_paise FROM bill.extra_item_price p JOIN bill.extra_item e ON e.extra_item_id = p.extra_item_id WHERE e.code = 'MINIBAR'"));
        Assert.Equal("OWN,MGR", (string?)await _w.ScalarAsync("SELECT roles FROM bill.discount_reason WHERE code = 'MGMT'"));
        Assert.Equal(2500, await Count("SELECT max_bps FROM bill.discount_reason WHERE code = 'SERVICE'"));
        Assert.Equal(1000, await Count("SELECT approval_above_bps FROM bill.discount_reason WHERE code = 'SERVICE'"));

        // money masters: the module's seed (till + bank account, six methods, five categories) is kept, the file adds to it
        Assert.Equal(4, await Count("SELECT COUNT(*) FROM mst.bank_account"));
        Assert.Equal(25000000, await Count("SELECT opening_balance_paise FROM mst.bank_account WHERE code = 'HDFC'"));
        Assert.Equal("wallet", (string?)await _w.ScalarAsync("SELECT default_for FROM mst.bank_account WHERE code = 'HDFC'"));
        Assert.Equal(9, await Count("SELECT COUNT(*) FROM mst.payment_method"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM mst.payment_method m JOIN mst.bank_account a ON a.bank_account_id = m.settles_to_id WHERE m.code = 'WALLET' AND a.code = 'HDFC' AND m.usable_frontdesk = 1 AND m.usable_pos = 1 AND m.usable_expenses = 0"));
        Assert.Equal(8, await Count("SELECT COUNT(*) FROM ops.expense_category"));
        Assert.Equal(500000, await Count("SELECT approval_limit_paise FROM ops.expense_category WHERE code = 'GARDEN'"));
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM ops.expense_category WHERE code = 'SECURITY' AND approval_limit_paise IS NOT NULL"));
        Assert.Equal(3, await Count("SELECT COUNT(*) FROM ops.expense_vendor"));
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM ops.expense_vendor WHERE pan IS NOT NULL"));   // a PAN never comes from a file

        // POS: categories of the menu import plus the list's, tables of the outlet import plus the pos-tables file; MA-4 outlet / item columns
        Assert.Equal(6, await Count("SELECT COUNT(*) FROM pos.menu_category"));
        Assert.Equal(8, await Count("SELECT COUNT(*) FROM pos.outlet_table WHERE active = 1"));
        Assert.Equal(4, await Count("SELECT seats FROM pos.outlet_table WHERE code = 'REST-T1'"));
        Assert.Equal(1, await Count("SELECT is_counter FROM pos.outlet_table WHERE table_no = 'BAR1'"));
        Assert.Equal("room-service", (string?)await _w.ScalarAsync("SELECT outlet_type FROM pos.outlet WHERE code = 'RS'"));
        Assert.Equal("07:00", ((string?)await _w.ScalarAsync("SELECT RTRIM(opens_at) FROM pos.outlet WHERE code = 'REST'")));
        Assert.Equal("Kitchen", (string?)await _w.ScalarAsync("SELECT kot_station FROM pos.outlet WHERE code = 'REST'"));
        Assert.Equal("PBM", (string?)await _w.ScalarAsync("SELECT short_code FROM pos.menu_item WHERE code = 'PNR'"));
        Assert.Equal("Tandoor", (string?)await _w.ScalarAsync("SELECT station FROM pos.menu_item WHERE code = 'NAN'"));

        // front desk (defaults stored first, then the file's rows)
        Assert.Equal(11, await Count("SELECT COUNT(*) FROM mst.booking_source"));
        Assert.Equal(1800, await Count("SELECT commission_bps FROM mst.booking_source WHERE code = 'OTA-MMT'"));
        Assert.Equal(9, await Count("SELECT COUNT(*) FROM mst.id_type"));
        Assert.Equal(0, await Count("SELECT CAST(accepted AS int) FROM mst.id_type WHERE code = 'STUDENT'"));
        Assert.Equal(9, await Count("SELECT COUNT(*) FROM bill.number_series")); // was 8: MA-11 seeds the RST restaurant series as a default row (DEC-MA-11d)
        Assert.Equal("FY", (string?)await _w.ScalarAsync("SELECT reset_rule FROM bill.number_series WHERE code = 'INV-FY28'"));
        Assert.Equal(3, await Count("SELECT COUNT(*) FROM inv.policy"));
        Assert.Equal("[\"OTA-BKG\"]", (string?)await _w.ScalarAsync("SELECT sources FROM inv.policy WHERE code = 'NONREF'"));

        // people lists (seeded shift patterns M E N G and the six leave types are kept)
        Assert.Equal(3, await Count("SELECT COUNT(*) FROM hr.designation"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM hr.designation WHERE code = 'ATTENDANT' AND dept_code = 'HK' AND default_pay_rule = 'daily'"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM hr.shift_pattern WHERE code = 'S' AND CONVERT(VARCHAR(5), start_time, 108) = '10:30' AND break_minutes = 30"));
        Assert.Equal(8, await Count("SELECT COUNT(*) FROM hr.leave_type"));
        Assert.Equal(3, await Count("SELECT yearly_quota FROM hr.leave_type WHERE code = 'BL'"));
        Assert.Equal(3, await Count("SELECT COUNT(*) FROM hr.holiday"));
        Assert.Equal("restricted", (string?)await _w.ScalarAsync("SELECT kind FROM hr.holiday WHERE code = '2026-12-25'"));

        // operations lists (seeded task types and categories are kept; the file changes DEP)
        Assert.Equal(4, await Count("SELECT COUNT(*) FROM ops.hk_task_type"));
        Assert.Equal(45, await Count("SELECT standard_minutes FROM ops.hk_task_type WHERE code = 'DEP'"));
        Assert.Equal(4, await Count("SELECT COUNT(*) FROM OPENJSON((SELECT checklist_json FROM ops.hk_task_type WHERE code = 'DEP'))"));
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM ops.hk_zone"));
        Assert.Equal("F1,F2", (string?)await _w.ScalarAsync("SELECT floor_codes FROM ops.hk_zone WHERE code = 'ZONE-A'"));
        Assert.Equal("sunil.hk", (string?)await _w.ScalarAsync("SELECT u.login_name FROM ops.hk_zone z JOIN plat.app_user u ON u.user_id = z.default_attendant_id WHERE z.code = 'ZONE-A'"));
        Assert.Equal(10, await Count("SELECT COUNT(*) FROM gsv.complaint_category"));
        Assert.Equal(9, await Count("SELECT COUNT(*) FROM gsv.lostfound_category"));

        // the change history of the masters framework is written like the modules' own commands
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM mst.master_history WHERE master = 'floors' AND code = 'F1' AND field = 'name' AND new_value = N'Ground floor'"));
        Assert.True(await Count("SELECT COUNT(*) FROM mst.master_history WHERE master IN ('mealPlans', 'extras', 'paymentMethods', 'numberSeries', 'hkZones')") > 20);
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM mst.master_history WHERE master = 'hkTaskTypes' AND code = 'DEP' AND field = 'standardMinutes' AND old_value = N'40' AND new_value = N'45'"));
    }

    [Fact]
    public async Task Export_then_import_changes_nothing_for_every_list_and_the_pos_masters()
    {
        await ImportAllAsync();
        foreach (var wire in Lists.Concat(["pos-outlets", "pos-menu"]))
        {
            var export = await _w.Service.ExportAsync(_w.PropertyId, _w.Users["owner"], wire, Ct);
            Assert.True(export.Rows > 0, wire);
            Assert.Equal(Templates.Template(wire).Trim(), export.CsvText.Split("\r\n")[0]);
            var up = await _w.UploadAsync("owner", wire, export.CsvText);
            Assert.True(up.ErrorCount == 0, wire + ": " + string.Join(" | ", up.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}")));
            Assert.Equal(new ImportCounts(0, 0, export.Rows, 0), up.Counts);
        }
    }

    [Fact]
    public async Task Changed_values_update_their_rows_dated_amounts_start_today_and_history_keeps_the_old_value()
    {
        await _w.ImportSampleAsync("room-types");
        await _w.ImportSampleAsync("floors");
        await _w.ImportSampleAsync("extras");
        var csv = Templates.Sample("floors").Replace("F1,Ground floor,10", "F1,Lobby level,10", StringComparison.Ordinal);
        var up = await _w.UploadAsync("owner", "floors", csv);
        Assert.Equal(new ImportCounts(0, 1, 2, 0), up.Counts);
        Assert.Contains("name Ground floor → Lobby level", up.Plan.Single(p => p.Action == "Update").Detail, StringComparison.Ordinal);
        await _w.CommitAsync("owner", up.BatchId);
        Assert.Equal("Lobby level", (string?)await _w.ScalarAsync("SELECT name FROM mst.floor WHERE code = 'F1'"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM mst.master_history WHERE master = 'floors' AND code = 'F1' AND field = 'name' AND old_value = N'Ground floor' AND new_value = N'Lobby level'"));

        // an amount in force is never rewritten: a new price starts from the business date, the old row stays
        await _w.ExecAsync("UPDATE bill.extra_item_price SET effective_from = '2025-04-01'");
        var repriced = Templates.Sample("extras").Replace("LAUNDRY,Laundry,laundry,500.00", "LAUNDRY,Laundry,laundry,550.00", StringComparison.Ordinal);
        var r = await _w.UploadAsync("owner", "extras", repriced);
        Assert.Equal(new ImportCounts(0, 1, 3, 0), r.Counts);
        await _w.CommitAsync("owner", r.BatchId);
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM bill.extra_item_price p JOIN bill.extra_item e ON e.extra_item_id = p.extra_item_id WHERE e.code = 'LAUNDRY'"));
        Assert.Equal(50000, await Count("SELECT p.amount_paise FROM bill.extra_item_price p JOIN bill.extra_item e ON e.extra_item_id = p.extra_item_id WHERE e.code = 'LAUNDRY' AND p.effective_from = '2025-04-01'"));
        Assert.Equal(55000, await Count("SELECT p.amount_paise FROM bill.extra_item_price p JOIN bill.extra_item e ON e.extra_item_id = p.extra_item_id WHERE e.code = 'LAUNDRY' AND p.effective_from = '2026-10-26'"));

        // a blank price keeps the amount; a column left out of the file leaves it alone
        var blank = await _w.UploadAsync("owner", "extras", "code,name,category,price,gst,unit,sortOrder\nLAUNDRY,Laundry,laundry,,laundry,bag,10\n");
        Assert.True(blank.ErrorCount == 0, string.Join("|", blank.Errors.Select(e => e.Message)));
        Assert.Equal(new ImportCounts(0, 0, 1, 0), blank.Counts);
    }

    [Fact]
    public async Task A_file_never_retires_an_active_row_but_may_reactivate_a_retired_one()
    {
        await _w.ImportSampleAsync("floors");
        var retire = await _w.UploadAsync("owner", "floors", Templates.Sample("floors").Replace("F3,Second floor,30,yes", "F3,Second floor,30,no", StringComparison.Ordinal));
        Assert.False(retire.CommitAllowed);
        Assert.Contains(retire.Errors, e => e is { Row: 4, Column: "active" } && e.Message.Contains("retire it on its list", StringComparison.Ordinal));

        await _w.ExecAsync("UPDATE mst.floor SET active = 0, retired_on = '2026-10-26' WHERE code = 'F3'");
        var again = await _w.UploadAsync("owner", "floors", Templates.Sample("floors"));
        Assert.Equal(new ImportCounts(0, 1, 2, 0), again.Counts);
        await _w.CommitAsync("owner", again.BatchId);
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM mst.floor WHERE code = 'F3' AND active = 1 AND retired_on IS NULL"));

        // a new row may arrive retired (a second property copied from an export)
        var gone = await _w.UploadAsync("owner", "floors", "code,name,sortOrder,active\nF9,Old annexe,90,no\n");
        Assert.Equal(new ImportCounts(1, 0, 0, 0), gone.Counts);
        await _w.CommitAsync("owner", gone.BatchId);
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM mst.floor WHERE code = 'F9' AND active = 0 AND retired_on = '2026-10-26'"));
    }

    [Fact]
    public async Task Rules_of_the_owning_modules_refuse_bad_rows_row_by_row()
    {
        await _w.ImportSampleAsync("room-types");
        await _w.ImportSampleAsync("bank-accounts");
        async Task<IReadOnlyList<ImportRowError>> Errors(string master, string csv) => (await _w.UploadAsync("owner", master, csv)).Errors;

        var methods = await Errors("payment-methods",
            "code,name,kind,settlesTo,usableAt,sortOrder\nA1,Cash to bank,cash,HDFC,frontdesk,\nA2,Credit at desk,credit,,frontdesk,\nA3,UPI to nowhere,upi,,frontdesk,\nA4,Bad ledger,upi,HDFC,frontdesk,\n");
        Assert.Contains(methods, e => e is { Row: 2, Column: "settlesTo" } && e.Message.Contains("settles to a till", StringComparison.Ordinal));
        Assert.Contains(methods, e => e is { Row: 3, Column: "usableAt" } && e.Message.Contains("Credit is not a guest payment", StringComparison.Ordinal));
        Assert.Contains(methods, e => e is { Row: 4, Column: "settlesTo" } && e.Message.Contains("bank account", StringComparison.Ordinal));

        var accounts = await Errors("bank-accounts",
            "code,name,kind,bank,accountLast4,ifsc,defaultFor\nB1,Till with bank,till,SBI,,,\nB2,No bank,bank,,,,\nB3,Bad last4,bank,SBI,12AB,,\nB4,Bad ifsc,bank,SBI,1234,XYZ,\n");
        Assert.Contains(accounts, e => e is { Row: 2, Column: "bank" });
        Assert.Contains(accounts, e => e is { Row: 3, Column: "bank" } && e.Message.Contains("bank name", StringComparison.Ordinal));
        Assert.Contains(accounts, e => e is { Row: 4, Column: "accountLast4" });
        Assert.Contains(accounts, e => e is { Row: 5, Column: "ifsc" });
        var taken = await Errors("bank-accounts", "code,name,kind,bank,defaultFor\nB5,Takes the default,bank,SBI,wallet\n");
        Assert.Contains(taken, e => e is { Row: 2, Column: "defaultFor" } && e.Message.Contains("only one account", StringComparison.Ordinal));

        // the opening balance of a stored account is setup-only
        var opening = await Errors("bank-accounts", Templates.Sample("bank-accounts").Replace("250000.00", "300000.00", StringComparison.Ordinal));
        Assert.Contains(opening, e => e.Column == "openingBalance" && e.Message.Contains("setup-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Vendors_never_carry_a_pan_and_tds_needs_one_in_hcc()
    {
        var forbidden = await _w.UploadAsync("owner", "expense-vendors", "code,name,type,pan\nV1,Vendor,supplier,ABCDE1234F\n");
        Assert.Contains(forbidden.Errors, e => e.Column == "pan" && e.Message.Contains("not allowed", StringComparison.Ordinal));
        var tds = await _w.UploadAsync("owner", "expense-vendors", "code,name,type,tds\nV1,Vendor,supplier,yes\n");
        Assert.Contains(tds.Errors, e => e is { Row: 2, Column: "tds" } && e.Message.Contains("PAN", StringComparison.Ordinal));

        await _w.ImportSampleAsync("expense-vendors");
        await _w.ExecAsync("UPDATE ops.expense_vendor SET pan = 'ABCDE1234F' WHERE code = 'VND-LINEN'");
        var ok = await _w.UploadAsync("owner", "expense-vendors", Templates.Sample("expense-vendors").Replace("27ABCDE1234F1Z5,no", "27ABCDE1234F1Z5,yes", StringComparison.Ordinal));
        Assert.True(ok.ErrorCount == 0, string.Join("|", ok.Errors.Select(e => e.Message)));
        Assert.Equal(new ImportCounts(0, 1, 2, 0), ok.Counts);
        await _w.CommitAsync("owner", ok.BatchId);
        Assert.Equal(1, await Count("SELECT CAST(tds AS int) FROM ops.expense_vendor WHERE code = 'VND-LINEN'"));
        // the export of a vendor has no PAN column
        var export = await _w.Service.ExportAsync(_w.PropertyId, _w.Users["owner"], "expense-vendors", Ct);
        Assert.DoesNotContain("pan", export.CsvText.Split("\r\n")[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Numbering_series_are_never_changed_by_a_file_and_a_new_one_starts_today_or_later()
    {
        await _w.ImportSampleAsync("number-series");
        var changed = await _w.UploadAsync("owner", "number-series", Templates.Sample("number-series").Replace("INV-FY28,Tax invoice from FY 2027-28,INV,HCC,1,4", "INV-FY28,Tax invoice from FY 2027-28,INV,HCC,1,5", StringComparison.Ordinal));
        Assert.Contains(changed.Errors, e => e is { Row: 2 } && e.Message.Contains("sequential, no gaps", StringComparison.Ordinal));

        var past = await _w.UploadAsync("owner", "number-series", "code,name,document,prefix,start,padding,reset,activeFrom\nOLD,Old,INV,OLD,1,4,FY,2026-04-01\n");
        Assert.Contains(past.Errors, e => e.Column == "activeFrom" && e.Message.Contains("today", StringComparison.Ordinal));
        var long16 = await _w.UploadAsync("owner", "number-series", "code,name,document,prefix,start,padding,reset,activeFrom\nLONG,Long,INV,ABCDEF,1,8,FY,2028-04-01\n");
        Assert.Contains(long16.Errors, e => e.Message.Contains("16", StringComparison.Ordinal));
        var never = await _w.UploadAsync("owner", "number-series", "code,name,document,prefix,start,padding,reset,activeFrom\nNEV,Never,CRN,CN,1,4,NEVER,2028-04-01\n");
        Assert.Contains(never.Errors, e => e.Column == "reset" || e.Message.Contains("financial year", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Policies_default_and_sources_rules_hold_across_the_file_and_the_stored_rows()
    {
        await _w.ImportSampleAsync("booking-sources");
        await _w.ImportSampleAsync("policies");
        var dup = await _w.UploadAsync("owner", "policies", "code,name,freeCancelHours,sources\nTWO,Second default,24,\nTHREE,Third,24,OTA-BKG\n");
        Assert.Contains(dup.Errors, e => e is { Row: 2, Column: "sources" } && e.Message.Contains("default", StringComparison.Ordinal));
        Assert.Contains(dup.Errors, e => e is { Row: 3, Column: "sources" } && e.Message.Contains("more than one", StringComparison.Ordinal));
        var unknown = await _w.UploadAsync("owner", "policies", "code,name,freeCancelHours,sources\nX,Unknown source,24,NOPE\n");
        Assert.Contains(unknown.Errors, e => e is { Column: "sources" } && e.Message.Contains("not in HCC yet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Money_lists_commit_only_as_the_owner_and_export_follows_the_same_right()
    {
        await _w.ImportSampleAsync("floors");
        var money = await _w.UploadAsync("techadmin", "bank-accounts", Templates.Sample("bank-accounts"));   // preparing is allowed
        Assert.True(money.CommitAllowed);
        await Assert.ThrowsAsync<HccException>(() => _w.CommitAsync("techadmin", money.BatchId));
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM mst.bank_account WHERE code = 'HDFC'"));
        await Assert.ThrowsAsync<HccException>(() => _w.Service.ExportAsync(_w.PropertyId, _w.Users["techadmin"], "bank-accounts", Ct));
        var floors = await _w.Service.ExportAsync(_w.PropertyId, _w.Users["techadmin"], "floors", Ct);
        Assert.Equal(3, floors.Rows);
        await Assert.ThrowsAsync<HccException>(() => _w.Service.ExportAsync(_w.PropertyId, _w.Users["reception"], "floors", Ct));
        await Assert.ThrowsAsync<HccException>(() => _w.Service.ExportAsync(_w.PropertyId, _w.Users["owner"], "ledgers", Ct));   // not an exportable master
    }

    [Fact]
    public async Task Dry_run_keeps_nothing_and_module_seeds_are_not_hidden_by_a_partial_file()
    {
        var dry = await _w.UploadAsync("owner", "leave-types", Templates.Sample("leave-types"));
        var result = await _w.CommitAsync("owner", dry.BatchId, dryRun: true);
        Assert.Equal(new ImportCounts(2, 0, 0, 0), result.Counts);
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM hr.leave_type"));   // rolled back, seed included
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM hr.people_list_seed"));

        var real = await _w.UploadAsync("owner", "leave-types", Templates.Sample("leave-types"));
        await _w.CommitAsync("owner", real.BatchId);
        Assert.Equal(8, await Count("SELECT COUNT(*) FROM hr.leave_type"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM hr.leave_type WHERE code = 'CL' AND yearly_quota = 12"));

        await _w.ImportSampleAsync("booking-sources");
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM mst.booking_source WHERE code = 'WALKIN'"));   // the defaults were stored first
        Assert.Equal(11, await Count("SELECT COUNT(*) FROM mst.booking_source"));
    }

    [Fact]
    public async Task Legacy_pos_files_with_the_old_headers_still_import_and_keep_columns_they_do_not_name()
    {
        var outlets = await _w.UploadAsync("owner", "pos-outlets", "outletCode,outletName,kind,tables\nREST,Restaurant,table,T1;T2\nRS,Room service,room,\n");
        Assert.True(outlets.ErrorCount == 0, string.Join("|", outlets.Errors.Select(e => e.Message)));
        await _w.CommitAsync("owner", outlets.BatchId);
        Assert.Equal("restaurant", (string?)await _w.ScalarAsync("SELECT outlet_type FROM pos.outlet WHERE code = 'REST'"));
        // set the MA-4 columns, then re-import the old file: a column the file does not name is left alone
        await _w.ExecAsync("UPDATE pos.outlet SET gst_profile = 'specified', opens_at = '07:30', kot_station = N'Tandoor' WHERE code = 'REST'");
        var again = await _w.UploadAsync("owner", "pos-outlets", "outletCode,outletName,kind,tables\nREST,Restaurant,table,T1;T2\nRS,Room service,room,\n");
        Assert.Equal(new ImportCounts(0, 0, 2, 0), again.Counts);
        var tables = await _w.UploadAsync("owner", "pos-outlets", "code,name,type,tables\nREST,Restaurant,restaurant,T1;T2;T9\n");
        Assert.Contains(tables.Errors, e => e.Column == "code" && e.Message.Contains("T9", StringComparison.Ordinal));
        var rename = await _w.UploadAsync("owner", "pos-outlets", "code,name,type,gstProfile\nREST,Restaurant and bar,bar,hotel\n");
        Assert.Equal(new ImportCounts(0, 1, 0, 0), rename.Counts);
        await _w.CommitAsync("owner", rename.BatchId);
        Assert.Equal("Restaurant and bar", (string?)await _w.ScalarAsync("SELECT name FROM pos.outlet WHERE code = 'REST'"));
        Assert.Equal("bar", (string?)await _w.ScalarAsync("SELECT outlet_type FROM pos.outlet WHERE code = 'REST'"));
        Assert.Equal("hotel", (string?)await _w.ScalarAsync("SELECT gst_profile FROM pos.outlet WHERE code = 'REST'"));
        Assert.Equal("Tandoor", (string?)await _w.ScalarAsync("SELECT kot_station FROM pos.outlet WHERE code = 'REST'"));
    }
}
