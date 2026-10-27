using Hcc.Domain.Import;

namespace Hcc.Import.Tests.Kernel;

/// <summary>MA-8: the list masters' definitions, cell parsing, row and file rules, CSV export and the export → import round trip (no database).</summary>
public class ImportListsKernelTests
{
    private static ListSnapRow R(string code, string name, int sort = 10, bool active = true, params (string Key, string Value)[] values) =>
        new(1, code, name, sort, active, values.ToDictionary(v => v.Key, v => v.Value));

    private static ImportSnapshot Snap(Dictionary<string, IReadOnlyList<ListSnapRow>>? lists = null) => new(new DateOnly(2026, 10, 26), false,
        [new SnapRoomType(1, "DLX", "Deluxe", 3, 420000, 1), new SnapRoomType(2, "STE", "Suite", 4, 750000, 3)], [new SnapRoom("301", 2)], [], [],
        [new SnapRole("OWN", "Owner", "PASSWORD")], [new SnapUser("sunil.hk", "Sunil Shinde", ["HKP"])], [], null, [], [], new HashSet<string>(),
        [new SnapOutlet(1, "REST", "Restaurant", "table", ["T1"], "restaurant"), new SnapOutlet(2, "RS", "Room service", "room", [], "room-service")], [],
        [new SnapDepartment("FO", "Front office", null), new SnapDepartment("HK", "Housekeeping", null), new SnapDepartment("FB", "Food", null)], null, null, null,
        lists ?? new Dictionary<string, IReadOnlyList<ListSnapRow>>());

    private static ImportPlan Plan(string wire, string csv, ImportSnapshot? snap = null)
    {
        var spec = ImportMasters.Find(wire)!;
        var table = CsvParser.TryParse(csv, out var e) ?? throw new InvalidOperationException(e);
        return ImportPlanner.Plan(spec.Master, table, snap ?? Snap());
    }

    [Fact]
    public void Definitions_are_consistent_and_every_list_has_a_spec_with_the_template_columns()
    {
        Assert.Equal(24, ImportLists.All.Count);
        Assert.Equal(ImportLists.All.Count, ImportLists.All.Select(d => d.Wire).Distinct().Count());
        Assert.Equal(ImportLists.All.Count, ImportLists.All.Select(d => d.Proto).Distinct().Count());
        foreach (var d in ImportLists.All)
        {
            var spec = ImportMasters.Spec(d.Master);
            Assert.Equal(d.Wire, spec.Wire);
            Assert.Equal(d.OwnerOnlyCommit, spec.OwnerOnlyCommit);
            Assert.Equal(["code", "name", .. d.Fields.Select(f => f.Key), "sortOrder", "active"], spec.Columns.Select(c => c.Name));
            Assert.Equal(d.Columns.Count, d.Columns.Distinct().Count());
            Assert.All(d.Fields, f =>
            {
                Assert.False(ImportMasters.IsForbiddenHeader(CsvParser.Key(f.Key)), $"{d.Wire}.{f.Key} is a forbidden header");
                if (f.Kind is ListKind.Pick) Assert.NotEmpty(f.Options!);
                if (f.Ref is { } r) Assert.True(ImportLists.Find(r) is not null || r is ImportLists.UserRef or ImportLists.RoomRef or ImportLists.RoomTypeRef or ImportLists.DepartmentRef or ImportLists.OutletRef or "pos-outlets", $"{d.Wire}.{f.Key} refers to {r}");
            });
        }

        // money masters (DEC-058): every list with a money field or a money list is committed by the Owner only
        foreach (var d in ImportLists.All.Where(d => d.Fields.Any(f => f.Money) || d.Wire is "extras" or "meal-plans" or "payment-methods" or "bank-accounts" or "holidays"))
            Assert.True(d.OwnerOnlyCommit, d.Wire);
    }

    [Fact]
    public void Floors_plan_create_update_unchanged_next_sort_order_and_duplicates()
    {
        var have = Snap(new Dictionary<string, IReadOnlyList<ListSnapRow>> { ["floors"] = [R("F1", "Ground floor", 10), R("F2", "First floor", 20)] });
        var p = Plan("floors", "code,name,sortOrder,active\nf1,Ground floor,10,\nF2,Level one,,\nf3,Second floor,,\nF3,Again,,\nBAD CODE,Bad,,\nF4,,,\nF5,Fifth,-1,\n", have);
        Assert.Equal(ImportAction.Unchanged, p.Items[0].Action);
        Assert.Equal(ImportAction.Update, p.Items[1].Action);
        Assert.Equal("name First floor → Level one", p.Items[1].Detail);
        var created = (ListRowData)p.Items[2].Data!;
        Assert.Equal(("F3", 30), (created.Code, created.SortOrder));
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "code" } && e.Message == "Duplicate of row 4.");
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "code" });
        Assert.Contains(p.Errors, e => e is { Row: 7, Column: "name" });
        Assert.Contains(p.Errors, e => e is { Row: 8, Column: "sortOrder" });
    }

    [Fact]
    public void A_file_cannot_retire_an_active_row_and_an_active_name_is_used_once()
    {
        var have = Snap(new Dictionary<string, IReadOnlyList<ListSnapRow>> { ["floors"] = [R("F1", "Ground floor", 10), R("F2", "First floor", 20, false)] });
        var retire = Plan("floors", "code,name,active\nF1,Ground floor,no\n", have);
        Assert.Contains(retire.Errors, e => e is { Row: 2, Column: "active" });
        var reactivate = Plan("floors", "code,name,active\nF2,First floor,yes\n", have);
        Assert.Equal(ImportAction.Update, Assert.Single(reactivate.Items).Action);
        var same = Plan("floors", "code,name\nF9,ground FLOOR\n", have);
        Assert.Contains(same.Errors, e => e.Column == "name" && e.Message.Contains("already called", StringComparison.Ordinal));
        var born = Plan("floors", "code,name,active\nF9,Old annexe,no\n", have);
        Assert.Equal(ImportAction.Create, Assert.Single(born.Items).Action);
    }

    [Fact]
    public void Cells_parse_to_canonical_text_and_report_row_errors()
    {
        var p = Plan("extras", "code,name,category,price,gst,unit,department\nLND,Laundry,LAUNDRY,\"₹ 1,250.505\",Laundry,bag,laundry\nX,Bad,boat,abc,food,each,\nY,Bad2,misc,10,food,lorry,\n");
        var d = (ListRowData)p.Items[0].Data!;
        Assert.Equal("laundry", d.Values["category"]);
        Assert.Equal("1250.51", d.Values["price"]);
        Assert.Contains(p.Warnings, w => w is { Row: 2, Column: "price" } && w.Message.Contains("rounded", StringComparison.Ordinal));
        Assert.Contains(p.Errors, e => e is { Row: 3, Column: "category" });
        Assert.Contains(p.Errors, e => e is { Row: 3, Column: "price" });
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "unit" });

        var t = Plan("shift-patterns", "code,name,start,end,breakMinutes,night\nm,Morning,7:00,15.00,30,no\nN,Night,22:00,06:00,0,yes\nB,Bad,25:00,06:00,,\nC,Over,07:00,05:00,0,no\n");
        Assert.Equal("07:00", ((ListRowData)t.Items[0].Data!).Values["start"]);
        Assert.Equal("15:00", ((ListRowData)t.Items[0].Data!).Values["end"]);
        Assert.Contains(t.Errors, e => e is { Row: 4, Column: "start" });
        Assert.Contains(t.Errors, e => e is { Row: 5 } && e.Message.Contains("night", StringComparison.Ordinal));
    }

    [Fact]
    public void Percent_cells_accept_a_percent_sign_and_respect_the_field_limits()
    {
        var p = Plan("discount-reasons", "code,name,maxPct,approvalAbovePct,roles\nS,Service,25%,10,Owner;manager\nB,Bad,0,,\nC,Cap,101,,\nD,Order,10,10,\nE,Role,10,,Cashier\n");
        var s = (ListRowData)p.Items[0].Data!;
        Assert.Equal(("25", "10", "Owner;Manager"), (s.Values["maxPct"], s.Values["approvalAbovePct"], s.Values["roles"]));
        Assert.Contains(p.Errors, e => e is { Row: 3, Column: "maxPct" });
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "maxPct" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "approvalAbovePct" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "roles" });
    }

    [Fact]
    public void References_must_exist_and_a_column_left_out_keeps_the_stored_value()
    {
        var p = Plan("occupancy-charges", "code,name,roomType,extraAdult\nOC1,Deluxe,dlx,800\nOC2,Nowhere,XXL,800\n");
        Assert.Equal("DLX", ((ListRowData)p.Items[0].Data!).Values["roomType"]);
        Assert.Contains(p.Errors, e => e is { Row: 3, Column: "roomType" } && e.Message.Contains("not in HCC yet", StringComparison.Ordinal));

        var have = Snap(new Dictionary<string, IReadOnlyList<ListSnapRow>>
        {
            ["occupancy-charges"] = [R("OC1", "Deluxe", 10, true, ("roomType", "DLX"), ("extraAdult", "800.00"), ("extraChild", "400.00"), ("extraBed", ""), ("childAgeFrom", "6"), ("childAgeTo", "12"))],
        });
        // only the columns named change: the other stored values (even dated amounts and ages) stay
        var keep = Plan("occupancy-charges", "code,name,roomType,extraAdult\nOC1,Deluxe,DLX,800\n", have);
        Assert.Equal(ImportAction.Unchanged, Assert.Single(keep.Items).Action);
        var raise = Plan("occupancy-charges", "code,name,roomType,extraAdult\nOC1,Deluxe,DLX,900\n", have);
        Assert.Equal("extra adult ₹800 → ₹900", Assert.Single(raise.Items).Detail);
        // a blank dated amount keeps it; a blank age clears it
        var blank = Plan("occupancy-charges", "code,name,roomType,extraAdult,childAgeFrom\nOC1,Deluxe,DLX,,\n", have);
        Assert.Equal("child age from 6 → —", Assert.Single(blank.Items).Detail);
    }

    [Fact]
    public void Row_rules_of_the_owning_modules_apply_to_leave_types_shifts_zones_and_task_types()
    {
        // SCC leave port (DEC-116): SCC's fixed categories; a quota (halves, up to 2 decimals) only on CL / SL / EL; only LOP unpaid.
        var leave = Plan("leave-types", "code,name,paid,yearlyQuota\nCL,Casual,yes,12.5\nML,Quota on an untracked category,yes,5\nLOP,Paid without pay,yes,\n"
            + "BL,Not an SCC category,yes,\nSL,Three decimals,yes,7.125\nTOOLONGX,Long code,yes,1\n");
        Assert.Equal(ImportAction.Create, leave.Items[0].Action);
        Assert.Equal("12.5", ((ListRowData)leave.Items[0].Data!).Values["yearlyQuota"]);
        Assert.Contains(leave.Errors, e => e is { Row: 3, Column: "yearlyQuota" } && e.Message.Contains("quota", StringComparison.Ordinal));
        Assert.Contains(leave.Errors, e => e is { Row: 4, Column: "paid" } && e.Message.Contains("unpaid", StringComparison.Ordinal));
        Assert.Contains(leave.Errors, e => e is { Row: 5, Column: "code" } && e.Message.Contains("fixed", StringComparison.Ordinal));
        Assert.Contains(leave.Errors, e => e is { Row: 6, Column: "yearlyQuota" } && e.Message.Contains("2 decimals", StringComparison.Ordinal));
        Assert.Contains(leave.Errors, e => e is { Row: 7, Column: "code" });

        var zones = Plan("hk-zones", "code,name,floors,rooms,defaultAttendant\nA,Zone A,F1;F2,301,Sunil Shinde\nB,No members,,,\nC,Unknown floor,F9,,\nD,Unknown user,F1,,nobody\n",
            Snap(new Dictionary<string, IReadOnlyList<ListSnapRow>> { ["floors"] = [R("F1", "G"), R("F2", "First")] }));
        Assert.Equal("sunil.hk", ((ListRowData)zones.Items[0].Data!).Values["defaultAttendant"]);
        Assert.Contains(zones.Errors, e => e is { Row: 3, Column: "floors" } && e.Message.Contains("at least one floor", StringComparison.Ordinal));
        Assert.Contains(zones.Errors, e => e is { Row: 4, Column: "floors" });
        Assert.Contains(zones.Errors, e => e is { Row: 5, Column: "defaultAttendant" });

        var overlap = Plan("hk-zones", "code,name,floors\nA,Zone A,F1;F2\nB,Zone B,F2\n", Snap(new Dictionary<string, IReadOnlyList<ListSnapRow>> { ["floors"] = [R("F1", "G"), R("F2", "First")] }));
        Assert.Contains(overlap.Errors, e => e.Column == "floors" && e.Message.Contains("more than one active zone", StringComparison.Ordinal));

        var tasks = Plan("hk-task-types", "code,name,appliesTo,standardMinutes,needsInspection,checklist\nDEP,Departure,checkout;move,40,yes,Linen;Bathroom\nSTAY,Stayover,stayover,20,yes,Linen\nX,Dup kind,checkout,10,no,Linen\nY,Many,deep,10,no,1;2;3;4;5;6;7;8;9\n");
        Assert.Contains(tasks.Errors, e => e is { Row: 3 } && e.Message.Contains("never inspected", StringComparison.Ordinal));
        Assert.Contains(tasks.Errors, e => e is { Row: 5, Column: "checklist" });
    }

    [Fact]
    public void Existing_numbering_series_are_frozen_and_payment_methods_follow_the_account_kind()
    {
        var have = Snap(new Dictionary<string, IReadOnlyList<ListSnapRow>>
        {
            ["number-series"] = [R("INV", "Tax invoice", 10, true, ("document", "INV"), ("prefix", "INV"), ("start", "1"), ("padding", "4"), ("reset", "FY"), ("activeFrom", "2000-04-01"))],
            ["bank-accounts"] = [R("TILL", "Drawer", 10, true, ("kind", "till")), R("HDFC", "HDFC", 20, true, ("kind", "bank"))],
        });
        var series = Plan("number-series", "code,name,document,prefix,start,padding,reset,activeFrom\nINV,Tax invoice,INV,INV,1,4,FY,2000-04-01\nINV,Dup,INV,INV,1,4,FY,2000-04-01\n", have);
        Assert.Contains(series.Errors, e => e.Message == "Duplicate of row 2.");
        var edit = Plan("number-series", "code,name,document,prefix,start,padding,reset,activeFrom\nINV,Tax invoice,INV,XYZ,1,4,FY,2000-04-01\n", have);
        Assert.Contains(edit.Errors, e => e is { Row: 2, Column: "prefix" } && e.Message.Contains("sequential, no gaps", StringComparison.Ordinal));
        var rename = Plan("number-series", "code,name,document,prefix,start,padding,reset,activeFrom\nINV,Sales invoice,INV,INV,1,4,FY,2000-04-01\n", have);
        Assert.Equal(ImportAction.Update, Assert.Single(rename.Items).Action);

        var methods = Plan("payment-methods", "code,name,kind,settlesTo,usableAt\nCASH,Cash,cash,TILL,frontdesk\nUPI,UPI,upi,HDFC,pos\nBAD,Bad cash,cash,HDFC,\nCR,Credit,credit,HDFC,\nCRD,Credit desk,credit,,frontdesk\nNONE,No account,upi,,\nGH,Ghost,upi,GHOST,\n", have);
        Assert.Equal(2, methods.Items.Count(i => i.Action == ImportAction.Create));
        Assert.Contains(methods.Errors, e => e is { Row: 4, Column: "settlesTo" });
        Assert.Contains(methods.Errors, e => e is { Row: 5, Column: "settlesTo" });
        Assert.Contains(methods.Errors, e => e is { Row: 6, Column: "usableAt" });
        Assert.Contains(methods.Errors, e => e is { Row: 7, Column: "settlesTo" });
        Assert.Contains(methods.Errors, e => e is { Row: 8, Column: "settlesTo" } && e.Message.Contains("not in HCC yet", StringComparison.Ordinal));
    }

    [Fact]
    public void Older_pos_headers_work_and_pos_columns_not_named_stay_as_they_are()
    {
        var snap = Snap() with
        {
            PosOutlets = [new SnapOutlet(1, "REST", "Restaurant", "table", ["T1", "T2"], "restaurant", "07:30", "23:00", "specified", "Tandoor", 10, true)],
        };
        var legacy = Plan("pos-outlets", "outletCode,outletName,kind,tables\nREST,Restaurant,table,T1;T2\nRS,Room service,room,\n", snap);
        Assert.Equal(ImportAction.Unchanged, legacy.Items[0].Action);
        Assert.Equal(ImportAction.Create, legacy.Items[1].Action);
        Assert.Equal("room-service", ((OutletData)legacy.Items[1].Data!).Type);
        var changed = Plan("pos-outlets", "code,name,type,opens,gstProfile,sortOrder\nREST,Restaurant,restaurant,08:00,hotel,20\n", snap);
        Assert.Equal("hours 07:30–23:00 → 08:00–— · GST profile specified → hotel · sort order 10 → 20", Assert.Single(changed.Items).Detail);
        var kind = Plan("pos-outlets", "code,name,type\nREST,Restaurant,room-service\n", snap);
        Assert.Contains(kind.Errors, e => e.Column == "type");
        var retire = Plan("pos-outlets", "code,name,type,active\nREST,Restaurant,restaurant,no\n", snap);
        Assert.Contains(retire.Errors, e => e.Column == "active");

        var menu = Plan("pos-menu", "itemCode,itemName,category,price,veg\nDAL,Dal,Main,180,yes\n", snap with { PosMenu = [new SnapMenuItem(9, "DAL", "Dal", "Main", 18000, true, [1], "DT", "food", "Kitchen", 10, true)] });
        Assert.Equal(ImportAction.Unchanged, Assert.Single(menu.Items).Action);
    }

    [Fact]
    public void Export_csv_quotes_cells_and_a_planned_file_round_trips_unchanged_for_every_list()
    {
        Assert.Equal("a", ImportLists.CsvCell("a"));
        Assert.Equal("\"a,b\"", ImportLists.CsvCell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", ImportLists.CsvCell("say \"hi\""));

        var refs = new Dictionary<string, IReadOnlyList<ListSnapRow>>
        {
            ["floors"] = [R("F1", "Ground"), R("F2", "First"), R("F3", "Second")],
            ["bank-accounts"] = [R("HDFC", "HDFC", 20, true, ("kind", "bank")), R("POS-TILL", "Till", 30, true, ("kind", "till"))],
            ["booking-sources"] = [R("OTA-BKG", "Booking.com"), R("OTA-MMT", "MakeMyTrip")],
        };
        var snap = Snap(refs);
        foreach (var d in ImportLists.All)
        {
            var sample = File.ReadAllText(Path.Combine(Harness.Templates.Folder, "samples", d.Wire + ".csv"));
            var first = Plan(d.Wire, sample, Snap(refs.Where(x => x.Key != d.Wire).ToDictionary(x => x.Key, x => x.Value)));
            Assert.True(first.Errors.Count == 0, d.Wire + ": " + string.Join(" | ", first.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}")));
            var rows = first.Items.Select((i, n) => { var x = (ListRowData)i.Data!; return new ListSnapRow(n + 1, x.Code, x.Name, x.SortOrder, x.Active ?? true, x.Values); }).ToList();
            var csv = ImportLists.ToCsv(d, rows);
            Assert.Equal(string.Join(',', d.Columns), csv.Split("\r\n")[0]);
            var withRows = new Dictionary<string, IReadOnlyList<ListSnapRow>>(refs) { [d.Wire] = rows };
            var again = Plan(d.Wire, csv, Snap(withRows));
            Assert.True(again.Errors.Count == 0, d.Wire + " export: " + string.Join(" | ", again.Errors.Select(e => $"{e.Row}/{e.Column}: {e.Message}")));
            Assert.All(again.Items, i => Assert.Equal(ImportAction.Unchanged, i.Action));
            Assert.Equal(rows.Count, again.Items.Count);
        }
    }
}
