using Hcc.Domain.Import;

namespace Hcc.Import.Tests.Kernel;

/// <summary>CSV reading, rupee parsing/rounding, planner rules and the shipped templates (no database).</summary>
public class ImportKernelTests
{
    private static ImportSnapshot Empty(bool closed = false) => new(new DateOnly(2026, 10, 26), closed, [new SnapRoomType(1, "DLX", "Deluxe", 3, 420000, 1)], [], [], [],
        [new SnapRole("OWN", "Owner", "PASSWORD"), new SnapRole("REC", "Reception", "PIN"), new SnapRole("ACC", "Accountant", "PASSWORD")], [], [], null, [], [], new HashSet<string>());

    private static CsvTable Csv(string text) => CsvParser.TryParse(text, out var e) ?? throw new InvalidOperationException(e);

    [Theory]
    [InlineData("4200", 420000, false)]
    [InlineData("4,200.50", 420050, false)]
    [InlineData("₹ 1,25,000", 12500000, false)]
    [InlineData("Rs. 99.5", 9950, false)]
    [InlineData("99.555", 9956, true)]   // half away from zero: 9955.5 → 9956
    [InlineData("99.545", 9955, true)]   // 9954.5 → 9955 (not banker's 9954)
    [InlineData("0.005", 1, true)]
    [InlineData("0.004", 0, true)]
    [InlineData("100000000", 10000000000, false)]
    public void Rupees_parse_to_paise_half_away_from_zero(string text, long paise, bool rounded)
    {
        Assert.True(ImportValues.TryRupees(text, out var p, out var r, out var err), err);
        Assert.Equal(paise, p);
        Assert.Equal(rounded, r);
    }

    [Theory]
    [InlineData("-100")]
    [InlineData("(100)")]
    [InlineData("42OO")]
    [InlineData("1.2.3")]
    [InlineData("")]
    [InlineData("100000000.01")]
    public void Bad_or_negative_amounts_are_refused(string text) => Assert.False(ImportValues.TryRupees(text, out _, out _, out _));

    [Fact]
    public void Csv_reads_bom_quotes_line_breaks_crlf_and_semicolons()
    {
        var t = Csv("﻿name,notes\r\n\"Rao, Asha\",\"line1\nline2 \"\"quoted\"\"\"\r\n\r\n,\r\nB,x");
        Assert.Equal(["name", "notes"], t.Headers);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("Rao, Asha", t.Rows[0].Cells[0]);
        Assert.Equal("line1\nline2 \"quoted\"", t.Rows[0].Cells[1]);
        Assert.Equal(2, t.Rows[0].Row);
        Assert.Equal(6, t.Rows[1].Row);   // the multi-line cell and the blank/empty records are skipped in numbering by source line
        var semi = Csv("roomNo;roomType\n101;DLX\n");
        Assert.Equal(';', semi.Delimiter);
        Assert.Equal("DLX", semi.Rows[0].Cells[1]);
        Assert.Null(CsvParser.TryParse("a,b\n\"open", out var err));
        Assert.Contains("never closed", err, StringComparison.Ordinal);
        Assert.Null(CsvParser.TryParse("   ", out _));
    }

    [Fact]
    public void Headers_match_loosely_and_forbidden_columns_are_refused()
    {
        var p = ImportPlanner.Plan(ImportMaster.Rooms, Csv("Room No,ROOM_TYPE,Floor\n101,dlx\n"), Empty());
        Assert.True(p.Valid);
        Assert.Single(p.Warnings);   // Floor ignored
        Assert.Equal(ImportAction.Create, p.Items[0].Action);
        var s = ImportPlanner.Plan(ImportMaster.Staff, Csv("loginName,displayName,roles,PIN\nasha,Asha,REC,123456\n"), Empty());
        Assert.Contains(s.Errors, e => e.Row == 1 && e.Column == "PIN" && e.Message.Contains("not allowed", StringComparison.Ordinal));
        var g = ImportPlanner.Plan(ImportMaster.Guests, Csv("name,phone,Aadhaar Number\nAsha Rao,9820011111,1234\n"), Empty());
        Assert.False(g.Valid);
        Assert.True(ImportMasters.IsForbiddenHeader("password"));
        Assert.True(ImportMasters.IsForbiddenHeader("pan"));
        Assert.False(ImportMasters.IsForbiddenHeader("openingcash"));   // contains "pin" but is not a PIN
        var missing = ImportPlanner.Plan(ImportMaster.Tariffs, Csv("roomType,rate\nDLX,10\n"), Empty());
        Assert.Contains(missing.Errors, e => e.Column == "effectiveFrom" && e.Row == 1);
    }

    [Fact]
    public void Row_level_errors_name_row_column_and_message()
    {
        var p = ImportPlanner.Plan(ImportMaster.Tariffs, Csv("roomType,effectiveFrom,rate\nDLX,2026-11-01,4500\nSTD,2026-11-01,3000\nDLX,2026-10-01,10\nDLX,01-11-2026,4OOO\nDLX,2026-11-01,4500\n"), Empty());
        Assert.Contains(p.Errors, e => e is { Row: 3, Column: "roomType" } && e.Message.Contains("STD", StringComparison.Ordinal));
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "effectiveFrom" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "rate" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "effectiveFrom" } && e.Message == "Duplicate of row 2.");
        Assert.Single(p.Items);
    }

    [Fact]
    public void Rounded_amount_is_a_warning_and_stored_in_paise()
    {
        var p = ImportPlanner.Plan(ImportMaster.RoomTypes, Csv("code,name,maxOccupancy,rackRate\nSTE,Suite,4,7500.005\n"), Empty());
        Assert.True(p.Valid);
        Assert.Equal(750001, ((RoomTypeData)p.Items[0].Data!).RackPaise);
        Assert.Contains(p.Warnings, w => w.Column == "rackRate" && w.Message.Contains("₹7,500.01", StringComparison.Ordinal));
    }

    [Fact]
    public void Opening_trial_balance_must_balance_and_needs_setup_state()
    {
        var ok = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,100.50,\n2200,,100.50\n"), Empty(), new DateOnly(2026, 10, 25));
        Assert.True(ok.Valid);
        Assert.Equal(10050, ok.DebitPaise);
        Assert.Equal(10050, ok.CreditPaise);
        var bad = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,100.50,\n2200,,100\n"), Empty(), new DateOnly(2026, 10, 25));
        var e = Assert.Single(bad.Errors);
        Assert.Equal(0, e.Row);
        Assert.Contains("total debits ₹100.50, total credits ₹100, difference ₹0.50", e.Message, StringComparison.Ordinal);
        var both = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,10,10\n9999,,5\nNOPE,1,\n"), Empty(), new DateOnly(2026, 10, 25));
        Assert.Contains(both.Errors, x => x is { Row: 2, Column: "credit" });
        Assert.Contains(both.Errors, x => x is { Row: 3, Column: "ledgerCode" });   // suspense is never a ledger
        Assert.Contains(both.Errors, x => x is { Row: 4, Column: "ledgerCode" });
        var noDate = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,1,\n2200,,1\n"), Empty(), null);
        Assert.Contains(noDate.Errors, x => x.Column == "asOfDate");
        var closed = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,1,\n2200,,1\n"), Empty(closed: true), new DateOnly(2026, 10, 25));
        Assert.Contains(closed.Errors, x => x.Row == 0 && x.Message.Contains("first business day is already closed", StringComparison.Ordinal));
        var till = ImportPlanner.Plan(ImportMaster.TillCash, Csv("tillCode,tillName,openingCash\nFD-1,Desk,100\n"), Empty(closed: true));
        Assert.False(till.Valid);
    }

    [Fact]
    public void Staff_roles_map_codes_or_names_never_owner_and_pick_the_credential_kind()
    {
        var p = ImportPlanner.Plan(ImportMaster.Staff, Csv("loginName,displayName,roles\nAsha.R,Asha,reception\nmeena,Meena,REC;Accountant\nboss,Boss,OWN\nx,Short,REC\n"), Empty());
        Assert.Equal("asha.r", ((StaffData)p.Items[0].Data!).Login);
        Assert.Equal("PIN", ((StaffData)p.Items[0].Data!).CredentialKind);
        Assert.Equal("PASSWORD", ((StaffData)p.Items[1].Data!).CredentialKind);
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "roles" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "loginName" });
    }

    [Fact]
    public void Ledgers_refuse_hcc_codes_and_group_mismatches()
    {
        var p = ImportPlanner.Plan(ImportMaster.Ledgers, Csv("code,name,group,hccAccount\n1000,Cash,Asset,\nCAP,Capital,Capital,\nBANK,Bank,Asset,1030\nX,Wrong,Liability,1030\nY,Eq,Equity,1000\n"), Empty());
        Assert.Contains(p.Errors, e => e is { Row: 2, Column: "code" });
        Assert.Equal("Equity", ((LedgerData)p.Items.Single(i => i.Key == "CAP").Data!).Group);
        Assert.Equal("1030", ((LedgerData)p.Items.Single(i => i.Key == "BANK").Data!).HccAccount);
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "hccAccount" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "hccAccount" });
    }

    private static ImportSnapshot WithPos(IReadOnlyList<SnapOutlet>? outlets = null, IReadOnlyList<SnapMenuItem>? menu = null)
        => Empty() with { PosOutlets = outlets ?? [], PosMenu = menu ?? [] };

    [Fact]
    public void Pos_outlets_validate_kind_and_tables_and_plan_create_unchanged_or_refuse_a_difference()
    {
        var p = ImportPlanner.Plan(ImportMaster.PosOutlets, Csv("outletCode,outletName,kind,tables\nrest,Restaurant,table,T1;T2\nRS,Room service,room,\nBAR,Bar,table,\nX1,Room,room,T1\nY1,Y,lounge,\nREST,Dup,table,T1\nZ,Tables,table,T1;t1\n"), WithPos());
        Assert.Equal(2, p.Items.Count(i => i.Action == ImportAction.Create));
        Assert.Equal("REST", ((OutletData)p.Items[0].Data!).Code);
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "tables" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "tables" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "kind" });
        Assert.Contains(p.Errors, e => e is { Row: 7, Column: "outletCode" } && e.Message == "Duplicate of row 2.");
        Assert.Contains(p.Errors, e => e is { Row: 8, Column: "tables" });

        var have = WithPos([new SnapOutlet(7, "REST", "Restaurant", "table", ["T2", "T1"])]);
        var same = ImportPlanner.Plan(ImportMaster.PosOutlets, Csv("outletCode,outletName,kind,tables\nREST,Restaurant,table,T1;T2\n"), have);
        Assert.Equal(ImportAction.Unchanged, Assert.Single(same.Items).Action);
        var diff = ImportPlanner.Plan(ImportMaster.PosOutlets, Csv("outletCode,outletName,kind,tables\nREST,Restaurant,table,T1;T2;T3\n"), have);
        Assert.Contains(diff.Errors, e => e is { Row: 2, Column: "outletCode" });
    }

    [Fact]
    public void Pos_menu_needs_known_outlets_valid_price_and_veg_and_plans_updates()
    {
        var outlets = new[] { new SnapOutlet(1, "REST", "Restaurant", "table", ["T1"]), new SnapOutlet(2, "RS", "Room service", "room", []) };
        var none = ImportPlanner.Plan(ImportMaster.PosMenu, Csv("itemCode,itemName,category,price,veg,outlets\nDAL,Dal,Main,180,yes,\n"), WithPos());
        Assert.Contains(none.Errors, e => e is { Row: 2, Column: "outlets" });

        var p = ImportPlanner.Plan(ImportMaster.PosMenu, Csv("itemCode,itemName,category,price,veg,outlets\ndal,Dal Tadka,Main course,180.00,yes,\nTEA,Tea,Beverages,0,no,rs\nBAD,Bad,Main,abc,maybe,NOPE\nneg,Neg,Main,-5,yes,REST\n"), WithPos(outlets));
        var dal = (MenuItemData)p.Items[0].Data!;
        Assert.Equal("DAL", dal.Code);
        Assert.Equal(18000, dal.PricePaise);
        Assert.Equal([1, 2], dal.OutletIds);   // blank = every outlet
        Assert.Equal([2], ((MenuItemData)p.Items[1].Data!).OutletIds);
        Assert.False(((MenuItemData)p.Items[1].Data!).Veg);
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "price" });
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "veg" });
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "outlets" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "price" });

        var have = WithPos(outlets, [new SnapMenuItem(9, "DAL", "Dal Tadka", "Main course", 18000, true, [1, 2])]);
        var same = ImportPlanner.Plan(ImportMaster.PosMenu, Csv("itemCode,itemName,category,price,veg,outlets\nDAL,Dal Tadka,Main course,180,yes,REST;RS\n"), have);
        Assert.Equal(ImportAction.Unchanged, Assert.Single(same.Items).Action);
        var changed = ImportPlanner.Plan(ImportMaster.PosMenu, Csv("itemCode,itemName,category,price,veg,outlets\nDAL,Dal Tadka,Main course,200,yes,REST;RS\n"), have);
        var item = Assert.Single(changed.Items);
        Assert.Equal(ImportAction.Update, item.Action);
        Assert.Equal(9, ((MenuItemData)item.Data!).ExistingId);
        Assert.Contains("price ₹180 → ₹200", item.Detail, StringComparison.Ordinal);
    }

    // ── A09-09: departments, companies, suppliers, stock items ──

    [Fact]
    public void Departments_validate_code_name_and_leave_limit_and_plan_create_update_or_unchanged()
    {
        var p = ImportPlanner.Plan(ImportMaster.Departments, Csv("code,name,leaveCap\nfo,Front office,2\nHK,Housekeeping,\nX,Bad code,\nF&B,Food,\nMN,M,30\nFO,Duplicate,\n"), Empty());
        var fo = (DepartmentData)p.Items[0].Data!;
        Assert.Equal(("FO", 2), (fo.Code, fo.LeaveCap));
        Assert.Null(((DepartmentData)p.Items[1].Data!).LeaveCap);
        Assert.Equal(2, p.Items.Count(i => i.Action == ImportAction.Create));
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "code" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "code" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "name" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "leaveCap" });
        Assert.Contains(p.Errors, e => e is { Row: 7, Column: "code" } && e.Message == "Duplicate of row 2.");

        var have = Empty() with { Departments = [new SnapDepartment("FO", "Front office", 2)] };
        Assert.Equal(ImportAction.Unchanged, Assert.Single(ImportPlanner.Plan(ImportMaster.Departments, Csv("code,name,leaveCap\nFO,Front office,2\n"), have).Items).Action);
        var changed = Assert.Single(ImportPlanner.Plan(ImportMaster.Departments, Csv("code,name,leaveCap\nFO,Front office,\n"), have).Items);
        Assert.Equal(ImportAction.Update, changed.Action);
        Assert.Equal("leave limit 2 → default", changed.Detail);
        Assert.True(((DepartmentData)changed.Data!).Exists);
    }

    [Fact]
    public void Companies_check_gstin_phone_email_limit_and_rates_and_plan_a_limit_change()
    {
        const string head = "name,gstin,contact,phone,email,city,creditLimit,negotiatedRates\n";
        var p = ImportPlanner.Plan(ImportMaster.Companies, Csv(head
            + "Mehta Traders,27abcde1234f1z5,Arjun,9890011223,a@example.com,Mumbai,\"50,000\",dlx=3800.50\n"
            + "Plain Co,,,,,,,\n"
            + "Bad Co,27XX,,12,not-mail,,-5,STE=100;DLX=abc;DLX=0\n"
            + ",,No name,,,,,\n"
            + "MEHTA TRADERS,,,,,,,\n"), Empty());
        var m = (CompanyData)p.Items[0].Data!;
        Assert.Equal("27ABCDE1234F1Z5", m.Gstin);
        Assert.Equal(5000000, m.CreditLimitPaise);
        Assert.True(m.LimitChanged);
        Assert.Equal(380050, m.Rates["DLX"]);
        var plain = (CompanyData)p.Items[1].Data!;
        Assert.Equal((0L, false, 0), (plain.CreditLimitPaise, plain.LimitChanged, plain.Rates.Count));
        foreach (var col in new[] { "gstin", "phone", "email", "creditLimit", "negotiatedRates" }) Assert.Contains(p.Errors, e => e.Row == 4 && e.Column == col);
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "negotiatedRates" } && e.Message.Contains("STE is not in HCC yet", StringComparison.Ordinal));
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "name" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "name" } && e.Message == "Duplicate of row 2.");

        var have = Empty() with
        {
            Companies = [new SnapCompany(5, "Mehta Traders", "27ABCDE1234F1Z5", "Arjun", "9890011223", "a@example.com", "Mumbai", new Dictionary<string, long> { ["DLX"] = 380050 }, 5000000)],
        };
        var same = ImportPlanner.Plan(ImportMaster.Companies, Csv(head + "mehta traders,27ABCDE1234F1Z5,Arjun,9890011223,a@example.com,Mumbai,50000,DLX=3800.50\n"), have);
        Assert.Equal(ImportAction.Update, Assert.Single(same.Items).Action);   // the name's capitals differ
        var unchanged = ImportPlanner.Plan(ImportMaster.Companies, Csv(head + "Mehta Traders,27ABCDE1234F1Z5,Arjun,9890011223,a@example.com,Mumbai,50000,DLX=3800.50\n"), have);
        Assert.Equal(ImportAction.Unchanged, Assert.Single(unchanged.Items).Action);
        var raised = Assert.Single(ImportPlanner.Plan(ImportMaster.Companies, Csv(head + "Mehta Traders,27ABCDE1234F1Z5,Arjun,9890011223,a@example.com,Mumbai,75000,\n"), have).Items);
        Assert.Equal(ImportAction.Update, raised.Action);
        Assert.Contains("credit limit ₹50,000 → ₹75,000", raised.Detail, StringComparison.Ordinal);
        Assert.Contains("rates DLX ₹3,800.50 → no negotiated rates", raised.Detail, StringComparison.Ordinal);
        var data = (CompanyData)raised.Data!;
        Assert.Equal((5L, true, 7500000L), (data.ExistingId!.Value, data.LimitChanged, data.CreditLimitPaise));
    }

    [Fact]
    public void Suppliers_need_a_name_and_phone_and_refuse_an_ambiguous_existing_name()
    {
        var p = ImportPlanner.Plan(ImportMaster.Suppliers, Csv("name,phone,trade\nShree Laundry,98221 00200,Laundry\nOm Electricals,+919850012345,\nAB,abc,\nNo Phone,,\nshree laundry,9822100200,\n"), Empty());
        Assert.Equal(2, p.Items.Count(i => i.Action == ImportAction.Create));
        Assert.Equal("General", ((SupplierData)p.Items[1].Data!).Trade);
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "name" });
        Assert.Contains(p.Errors, e => e is { Row: 4, Column: "phone" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "phone" });
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "name" } && e.Message == "Duplicate of row 2.");

        var have = Empty() with { Suppliers = [new SnapSupplier(3, "Shree Laundry", "Laundry", "98221 00200"), new SnapSupplier(4, "Twin", "General", "9800000000"), new SnapSupplier(5, "twin", "General", "9800000001")] };
        var r = ImportPlanner.Plan(ImportMaster.Suppliers, Csv("name,phone,trade\nShree Laundry,98221 00200,Laundry\nTwin,9800000000,\n"), have);
        Assert.Equal(ImportAction.Unchanged, Assert.Single(r.Items).Action);
        Assert.Contains(r.Errors, e => e is { Row: 3, Column: "name" } && e.Message.Contains("2 suppliers named Twin", StringComparison.Ordinal));
        var phone = Assert.Single(ImportPlanner.Plan(ImportMaster.Suppliers, Csv("name,phone,trade\nShree Laundry,9822100299,Laundry\n"), have).Items);
        Assert.Equal((ImportAction.Update, 3), (phone.Action, ((SupplierData)phone.Data!).ExistingId!.Value));
    }

    [Fact]
    public void Stock_items_validate_code_kind_money_and_levels_and_only_levels_change_on_reimport()
    {
        const string head = "code,name,kind,unit,unitCost,minQty,reorderQty,usePerRoomNight,laundryRate,supplier\n";
        var p = ImportPlanner.Plan(ImportMaster.StockItems, Csv(head
            + "lin-bs,Bed sheet,Linen,pcs,450,40,60,1.5,12,Linen House\n"
            + "AMN-SP,Soap,amenity,pcs,,,,,,\n"
            + "BAD,X,food,,abc,-1,20000,1.555,5,\n"
            + "CLN-FL,Floor cleaner,cleaning,can,200000,1,1,,3,\n"
            + "LIN-BS,Again,linen,pcs,,,,,,\n"), Empty());
        var bs = (StockItemData)p.Items[0].Data!;
        Assert.Equal(("LIN-BS", "linen", 45000L, 150, 1200L), (bs.Code, bs.Kind, bs.CostPaise, bs.StdX100!.Value, bs.LaundryPaise!.Value));
        var soap = (StockItemData)p.Items[1].Data!;
        Assert.Equal((0L, 0, 0, (int?)null, (long?)null), (soap.CostPaise, soap.MinQty, soap.ReorderQty, soap.StdX100, soap.LaundryPaise));
        foreach (var col in new[] { "code", "name", "kind", "unit", "unitCost", "minQty", "reorderQty", "usePerRoomNight", "laundryRate" })
            Assert.Contains(p.Errors, e => e.Row == 4 && e.Column == col);
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "unitCost" });
        Assert.Contains(p.Errors, e => e is { Row: 5, Column: "laundryRate" } && e.Message.Contains("linen only", StringComparison.Ordinal));
        Assert.Contains(p.Errors, e => e is { Row: 6, Column: "code" } && e.Message == "Duplicate of row 2.");

        var have = Empty() with { StockItems = [new SnapStockItem(3, "LIN-BS", "Bed sheet", "linen", "pcs", 47000, 40, 60, 150, 1200, "Linen House")] };
        var same = ImportPlanner.Plan(ImportMaster.StockItems, Csv(head + "LIN-BS,Bed sheet,linen,pcs,450,40,60,1.5,12,Linen House\n"), have);
        Assert.Equal(ImportAction.Unchanged, Assert.Single(same.Items).Action);
        Assert.Contains(same.Warnings, w => w.Column == "unitCost" && w.Message.Contains("keeps its unit cost ₹470", StringComparison.Ordinal));
        var levels = Assert.Single(ImportPlanner.Plan(ImportMaster.StockItems, Csv(head + "LIN-BS,Bed sheet,linen,pcs,,50,60,2,,\n"), have).Items);
        Assert.Equal(ImportAction.Update, levels.Action);
        Assert.Equal("minimum 40 → 50 · use per room-night 1.5 → 2", levels.Detail);
        Assert.Equal(3, ((StockItemData)levels.Data!).ExistingId);
        var renamed = ImportPlanner.Plan(ImportMaster.StockItems, Csv(head + "LIN-BS,Bed sheet king,linen,pcs,,40,60,1.5,,\n"), have);
        Assert.Contains(renamed.Errors, e => e is { Row: 2, Column: "code" } && e.Message.Contains("Stock screen", StringComparison.Ordinal));
    }

    [Fact]
    public void Shipped_templates_match_the_column_specs_and_every_sample_parses()
    {
        var readme = File.ReadAllText(Path.Combine(Harness.Templates.Folder, "README.md"));
        foreach (var spec in ImportMasters.All)
        {
            var header = Csv(Harness.Templates.Template(spec.Wire) + "\n");
            Assert.Equal(spec.Columns.Select(c => c.Name), header.Headers);
            var sample = Csv(Harness.Templates.Sample(spec.Wire));
            Assert.Equal(spec.Columns.Select(c => c.Name), sample.Headers);
            Assert.NotEmpty(sample.Rows);
            Assert.Contains($"### {spec.Wire}.csv", readme, StringComparison.Ordinal);
            foreach (var c in spec.Columns) Assert.Contains($"| {c.Name} |", readme, StringComparison.Ordinal);
        }
    }
}
