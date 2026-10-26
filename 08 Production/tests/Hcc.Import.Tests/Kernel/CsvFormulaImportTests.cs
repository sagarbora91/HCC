using System.Text;
using Hcc.Domain.Import;
using Hcc.Domain.Text;

namespace Hcc.Import.Tests.Kernel;

/// <summary>
/// A06-08 (OWASP CSV injection) on the way in: text a spreadsheet would run as a formula (= + - @ at the start) is kept as text with a leading
/// apostrophe in free-text columns and refused by the rule of every other column; numbers keep their minus sign.
/// </summary>
public class CsvFormulaImportTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    /// <summary>What the shipped samples refer to: room types STD/DLX/STE, the two POS outlets, the roles.</summary>
    private static ImportSnapshot Snapshot() => new(Today, false,
        [new SnapRoomType(1, "STD", "Standard Room", 2, 280000, 1), new SnapRoomType(2, "DLX", "Deluxe Room", 3, 420000, 2), new SnapRoomType(3, "STE", "Suite", 4, 750000, 3)],
        [], [], [],
        [new SnapRole("OWN", "Owner", "PASSWORD"), new SnapRole("REC", "Reception", "PIN"), new SnapRole("CSH", "Cashier", "PIN"), new SnapRole("ACC", "Accountant", "PASSWORD")],
        [], [], null, [], [], new HashSet<string>(),
        PosOutlets: [new SnapOutlet(1, "REST", "Restaurant", "table", ["T1", "T2", "T3", "T4", "T5", "T6"]), new SnapOutlet(2, "RS", "Room service", "room", [])], PosMenu: []);

    private static CsvTable Csv(string text) => CsvParser.TryParse(text, out var e) ?? throw new InvalidOperationException(e);

    private static string Quote(string cell) => "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>The shipped sample's header and first data row, with <paramref name="column"/> replaced by <paramref name="value"/> (every cell quoted).</summary>
    private static CsvTable FirstSampleRow(ImportMasterSpec spec, string? column = null, string? value = null)
    {
        var sample = Csv(Harness.Templates.Sample(spec.Wire));
        var cells = sample.Rows[0].Cells.ToArray();
        if (column is not null) cells[sample.Headers.ToList().IndexOf(column)] = value!;
        var sb = new StringBuilder(string.Join(',', sample.Headers)).Append('\n').Append(string.Join(',', cells.Select(Quote))).Append('\n');
        return Csv(sb.ToString());
    }

    private static ImportPlan PlanFor(ImportMasterSpec spec, CsvTable t) =>
        ImportPlanner.Plan(spec.Master, t, Snapshot(), spec.Master == ImportMaster.OpeningBalances ? Today.AddDays(-1) : null);

    public static TheoryData<string, string, string> EveryColumnAndPayload()
    {
        string[] payloads = ["=1+2", "@SUM(1;2)", "-2+3", "+cmd|' /C calc'!A0", "=HYPERLINK(\"http://x.test\",\"y\")", "=T1", "=x@y.zz"];
        var data = new TheoryData<string, string, string>();
        foreach (var spec in ImportMasters.All)
            foreach (var c in spec.Columns)
                foreach (var p in payloads)
                    data.Add(spec.Wire, c.Name, p);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryColumnAndPayload))]
    public void A06_08_formula_text_is_refused_by_a_checked_column_and_kept_as_text_in_a_free_text_column(string wire, string column, string payload)
    {
        var spec = ImportMasters.Find(wire)!;
        var col = spec.Columns.Single(c => c.Name == column);
        var baseline = PlanFor(spec, FirstSampleRow(spec));
        Assert.DoesNotContain(baseline.Errors, e => e.Row == 2 && e.Column == column);   // the sample row is fine in this column
        Assert.DoesNotContain(baseline.Warnings, w => w.Row == 2 && w.Column == column);

        var plan = PlanFor(spec, FirstSampleRow(spec, column, payload));
        if (col.Text == ImportCellText.Checked)
        {
            // A code, number, date, phone, yes/no or lookup: the column's own rule refuses the formula text.
            Assert.Contains(plan.Errors, e => e.Row == 2 && e.Column == column);
            return;
        }

        var warning = Assert.Single(plan.Warnings, w => w.Row == 2 && w.Column == column);
        Assert.Contains("would run as a formula", warning.Message, StringComparison.Ordinal);
        Assert.Contains("leading apostrophe", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(payload, warning.Message, StringComparison.Ordinal);   // the preview does not echo the formula itself
    }

    [Fact]
    public void A06_08_guest_and_company_text_is_stored_with_an_apostrophe_and_phones_and_numbers_are_untouched()
    {
        var guests = ImportPlanner.Plan(ImportMaster.Guests, Csv(
            "name,phone,email,address,nationality,company,gstin,notes\n"
            + "\"=HYPERLINK(\"\"http://x.test\"\",\"\"y\"\")\",+91 98220 33445,=x@y.zz,\"+cmd|' /C calc'!A0\",Indian,-2+3,,@SUM(1)\n"
            + "Asha Rao,9820011111,asha@example.com,\"12 MG Road, Pune\",Indian,,,- prefers a quiet room\n"), Snapshot());
        Assert.True(guests.Valid, string.Join(" | ", guests.Errors.Select(e => e.Message)));
        var g = ((GuestData)guests.Items[0].Data!).Draft;
        Assert.Equal("'=HYPERLINK(\"http://x.test\",\"y\")", g.Name);
        Assert.Equal("'=x@y.zz", g.Email);
        Assert.Equal("'+cmd|' /C calc'!A0", g.Address);
        Assert.Equal("'-2+3", g.Company);
        Assert.Equal("'@SUM(1)", g.Notes);
        Assert.Equal("+91 98220 33445", g.Phone);   // a phone keeps its + (the phone rule allows digits, spaces, + - ( ) only)
        Assert.Equal("'- prefers a quiet room", ((GuestData)guests.Items[1].Data!).Draft.Notes);
        Assert.Equal(6, guests.Warnings.Count);
        Assert.Equal(["address", "company", "email", "name", "notes"], guests.Warnings.Where(w => w.Row == 2).Select(w => w.Column).Order(StringComparer.Ordinal));

        var companies = ImportPlanner.Plan(ImportMaster.Companies, Csv(
            "name,gstin,contact,phone,email,city,creditLimit,negotiatedRates\n@Acme (fictional),,=Arjun,+91 98900 11223,,-Mumbai,\"50,000\",DLX=3800\n"), Snapshot());
        Assert.True(companies.Valid, string.Join(" | ", companies.Errors.Select(e => e.Message)));
        var c = (CompanyData)companies.Items[0].Data!;
        Assert.Equal(("'@Acme (fictional)", "'=Arjun", "'-Mumbai", "+91 98900 11223", 5_000_000L), (c.Name, c.Contact, c.City, c.Phone, c.CreditLimitPaise));
        Assert.Equal(380_000L, c.Rates["DLX"]);
    }

    [Fact]
    public void A06_08_numbers_keep_their_minus_sign_and_a_negative_amount_gets_the_amount_error_not_a_formula_warning()
    {
        var seasons = ImportPlanner.Plan(ImportMaster.Seasons, Csv("name,from,to,adjustmentPercent,roomTypes,priority\nMonsoon,2026-11-01,2026-11-10,-10,,1\nPeak,2026-12-01,2026-12-10,+15,,2\n"), Snapshot());
        Assert.True(seasons.Valid, string.Join(" | ", seasons.Errors.Select(e => e.Message)));
        Assert.Equal([-1000, 1500], seasons.Items.Select(i => ((SeasonData)i.Data!).AdjBps));
        Assert.Empty(seasons.Warnings);

        var rooms = ImportPlanner.Plan(ImportMaster.RoomTypes, Csv("code,name,maxOccupancy,rackRate,sortOrder\nECO,Economy,2,-500,1\n"), Snapshot());
        var rack = Assert.Single(rooms.Errors);
        Assert.Equal((2, "rackRate"), (rack.Row, rack.Column));
        Assert.DoesNotContain("formula", rack.Message, StringComparison.Ordinal);
        Assert.Empty(rooms.Warnings);

        var opening = ImportPlanner.Plan(ImportMaster.OpeningBalances, Csv("ledgerCode,debit,credit\n1000,-500,\n2200,,-500\n"), Snapshot(), Today.AddDays(-1));
        Assert.Contains(opening.Errors, e => e is { Row: 2, Column: "debit" });
        Assert.Contains(opening.Errors, e => e is { Row: 3, Column: "credit" });
        Assert.Empty(opening.Warnings);

        // A plain number in a free-text column is not a formula either.
        var units = ImportPlanner.Plan(ImportMaster.StockItems, Csv("code,name,kind,unit,unitCost,minQty,reorderQty,usePerRoomNight,laundryRate,supplier\nAMN-SP,Soap,amenity,-1,10,1,1,,,\n"), Snapshot());
        Assert.Equal("-1", ((StockItemData)Assert.Single(units.Items).Data!).Unit);
        Assert.Empty(units.Warnings);
    }

    [Fact]
    public void A06_08_each_table_name_of_a_list_is_neutralised_and_a_re_import_of_an_exported_value_is_unchanged()
    {
        var p = ImportPlanner.Plan(ImportMaster.PosOutlets, Csv("outletCode,outletName,kind,tables\nBAR,Bar,table,T1; =T2 ;@T3|T4\n"), Snapshot());
        Assert.True(p.Valid, string.Join(" | ", p.Errors.Select(e => e.Message)));
        Assert.Equal(["T1", "'=T2", "'@T3", "T4"], ((OutletData)Assert.Single(p.Items).Data!).Tables);
        var w = Assert.Single(p.Warnings);
        Assert.Equal((2, "tables"), (w.Row, w.Column));
        Assert.Contains("starts with '='", w.Message, StringComparison.Ordinal);

        // HCC's own CSV exports write such text as '=…; reading that back keeps it as it is, without a second apostrophe or a warning.
        var again = ImportPlanner.Plan(ImportMaster.Guests, Csv("name,phone\n'=HYPERLINK(1),9820011111\n"), Snapshot());
        Assert.Equal("'=HYPERLINK(1)", ((GuestData)Assert.Single(again.Items).Data!).Draft.Name);
        Assert.Empty(again.Warnings);
    }

    [Theory]
    [InlineData("=1+2", true)]
    [InlineData("+cmd", true)]
    [InlineData("-2+3", true)]
    [InlineData("@SUM(1)", true)]
    [InlineData("\t=1", true)]
    [InlineData("\r=1", true)]
    [InlineData("-500", false)]
    [InlineData("-1,250.50", false)]
    [InlineData("+15", false)]
    [InlineData("'=1+2", false)]
    [InlineData("Rahul", false)]
    [InlineData("", false)]
    public void A06_08_csv_formula_guard_flags_formula_text_but_not_plain_numbers(string text, bool risky)
    {
        Assert.Equal(risky, CsvFormula.IsRisky(text));
        Assert.Equal(risky ? "'" + text : text, CsvFormula.Neutralise(text));
    }
}
