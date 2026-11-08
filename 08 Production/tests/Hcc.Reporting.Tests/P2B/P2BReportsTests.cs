// F8-RPT-P2-B · server twins of the batch-B money reports: the pure builders are driven with in-memory facts (the SQL source is the only database read), each test
// checks a tie-out the prototype suite (tests/stream-rpt-p2-b.mjs) checks on its side; plus the definition / catalogue facts (money flags, permissions, columns).
using Hcc.Application.Groups;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Compliance;
using Hcc.Domain.Money;
using Hcc.Reporting.Reports.Money;
using Hcc.Reporting.Reports.P2B;
using Hcc.Reporting.Sql;

namespace Hcc.Reporting.Tests.P2B;

public sealed class P2BReportsTests
{
    private static readonly DateOnly D1 = new(2026, 10, 1), D2 = new(2026, 10, 2);
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string> { [1] = "Sagar", [2] = "Manager" };

    private static long? Cell(ReportRow r, string k) => r.Cells.TryGetValue(k, out var v) && v is long l ? l : null;

    [Fact]
    public void Definitions_money_flags_permissions_and_columns()
    {
        var money = new[] { CashCountSheetReport.Id, CounterBillsReport.Id, RefundsReport.Id, DiscountLogReport.Id, BankBookReport.Id, CompanyCreditReport.Id };
        var defs = new[]
        {
            new CashCountSheetReport(null!, null!, null!).Definition, new CounterBillsReport(null!).Definition, new RefundsReport(null!, null!).Definition,
            new DiscountLogReport(null!, null!).Definition, new CompanyCreditReport(null!, null!, null!).Definition, new LicenceExpiryReport(null!).Definition,
        };
        foreach (var d in defs) Assert.Equal("built", d.Status);
        Assert.All(defs.Where(d => money.Contains(d.Id)), d => Assert.True(d.MoneyFlag));
        var lic = defs.Single(d => d.Id == LicenceExpiryReport.Id);
        Assert.False(lic.MoneyFlag);
        Assert.DoesNotContain(lic.Columns, c => c.Money);
        Assert.Equal("PERM-GRP-004", defs.Single(d => d.Id == CompanyCreditReport.Id).MoneyGate);
        Assert.True(defs.Single(d => d.Id == CashCountSheetReport.Id).OwnShift);
        Assert.Equal(ReportDateClass.Day, defs.Single(d => d.Id == CashCountSheetReport.Id).DateClass);
    }

    [Fact]
    public void Counter_bills_rows_tie_to_the_bill_net_and_total()
    {
        var bills = new List<CounterBillFact>
        {
            new(D1, "HPE/2627/0001", "Walk-in", "Cash", 9000, 450, 1000, "Regular", [new("Laundry", 2, 5000, 10000)]),
            new(D2, "HPE/2627/0002", "Asha", "Udhaar (credit)", 4000, 200, 0, null, [new("Tea", 4, 1000, 4000)]),
        };
        var rows = CounterBillsReport.Rows(bills);
        var lines = rows.Where(r => r.Kind == ReportRowKind.Row).ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal(-1000, Cell(lines[1], "amount"));
        var subs = rows.Where(r => r.Kind == ReportRowKind.Subtotal).ToList();
        Assert.Equal(9000, Cell(subs[0], "amount") ?? 0);
        Assert.Equal(lines.Take(2).Sum(r => Cell(r, "amount") ?? 0), Cell(subs[0], "amount"));
        var total = rows.Single(r => r.Kind == ReportRowKind.Total);
        Assert.Equal(13000, Cell(total, "amount"));
        Assert.Equal(650, Cell(total, "tax"));
        Assert.Empty(CounterBillsReport.Rows([]));
    }

    [Fact]
    public void Counter_tender_text_matches_the_prototype()
    {
        Assert.Equal("Cash", SqlP2BReportSource.Tender("paid", "Cash", "Issued", string.Empty));
        Assert.Equal("Udhaar (credit)", SqlP2BReportSource.Tender("udhaar", string.Empty, "Issued", string.Empty));
        Assert.Equal("Company credit · credited CN-1", SqlP2BReportSource.Tender("company", string.Empty, "Credited", "CN-1"));
    }

    [Fact]
    public void Refunds_sorted_with_total_and_approver_names()
    {
        var facts = new List<RefundFact> { new(D2, "RC-000002", "B", "INV2", 3000, "Cash", "Wrong", 2), new(D1, "RC-000001", "A", "INV1", 2000, "UPI", "Guest left", 1) };
        var rows = RefundsReport.Rows(facts, Names);
        Assert.Equal("RC-000001", rows[0].Cells["refundNo"]);
        Assert.Equal("Sagar", rows[0].Cells["approvedBy"]);
        Assert.Equal(5000, Cell(rows[^1], "amount"));
        Assert.Equal(ReportRowKind.Total, rows[^1].Kind);
    }

    [Fact]
    public void Discount_log_groups_by_reason_with_subtotals_and_filter()
    {
        var facts = new List<DiscountFact> { new(D1, "B1", "A", "Regular guest", 500, 1), new(D2, "B2", "B", "Regular guest", 700, 0), new(D2, "B3", "C", "Staff", 300, 2) };
        var rows = DiscountLogReport.Rows(facts, Names, string.Empty);
        var subs = rows.Where(r => r.Kind == ReportRowKind.Subtotal).ToList();
        Assert.Equal(2, subs.Count);
        Assert.Equal(1200, Cell(subs[0], "amount") ?? Cell(subs[1], "amount"));
        Assert.Equal(1500, Cell(rows[^1], "amount"));
        Assert.Equal(1500, rows.Where(r => r.Kind == ReportRowKind.Row).Sum(r => Cell(r, "amount") ?? 0));
        Assert.Equal(300, Cell(DiscountLogReport.Rows(facts, Names, "staff")[^1], "amount"));
        Assert.Empty(DiscountLogReport.Rows([], Names, string.Empty));
    }

    [Fact]
    public void Company_credit_level_rates_and_statement_running_balance()
    {
        Assert.Equal("No credit", CompanyCreditReport.Level(0, 0));
        Assert.Equal("Over limit", CompanyCreditReport.Level(0, 5));
        Assert.Equal("Near limit (≥ 80%)", CompanyCreditReport.Level(1000, 800));
        Assert.Equal("Within limit", CompanyCreditReport.Level(1000, 799));
        Assert.Equal("Over limit", CompanyCreditReport.Level(1000, 1001));
        Assert.Equal("Deluxe 3000.00; Suite 5000.50", CompanyCreditReport.RatesText(new Dictionary<string, long> { ["Suite"] = 500050, ["Deluxe"] = 300000 }));
        var facts = new CompanyCreditFacts(D2, [new(1, 10, "R-1", D1, new Paise(100000))],
            [new(7, D2, "NEFT", "UTR1", null, new Paise(60000), 1, DateTime.UtcNow, new Paise(10000))], [], []);
        var rows = CompanyCreditReport.Statement(facts);
        Assert.Equal(0, Cell(rows[0], "balance"));
        Assert.Equal(100000, Cell(rows[1], "balance"));
        Assert.Equal(30000, Cell(rows[2], "balance"));
        Assert.Equal(30000, Cell(rows[^1], "balance"));
        Assert.Equal(70000, Cell(rows[^1], "payment"));
    }

    [Fact]
    public void Licence_rows_sorted_by_expiry_within_days_and_expired_kept()
    {
        var today = new DateOnly(2026, 11, 8);
        var p = new CmpProfile("H", "", "", "proprietorship", "regular", true, false, "", "FS-1", new DateOnly(2027, 1, 1), "LG-9", new DateOnly(2026, 11, 1));
        var rows = LicenceExpiryReport.Rows(p, today, 90);
        Assert.Equal(2, rows.Count);
        Assert.Equal(-7, Cell(rows[0], "daysLeft"));
        Assert.Equal(54, Cell(rows[1], "daysLeft"));
        Assert.Single(LicenceExpiryReport.Rows(p, today, 30));
        Assert.Empty(LicenceExpiryReport.Rows(null, today, 90));
        Assert.Equal(1, Cell(rows[0], "card"));
    }

    [Fact]
    public void Cash_count_sheet_rows_with_and_without_a_grid()
    {
        var line = new ShiftLine(D1, "Desk (Z)", 4, "Priya", 500000, 300000, 100000, 700000, 690000, -10000, string.Empty, string.Empty);
        var grid = new CountGridFact(1, new Dictionary<int, int> { [500] = 1, [200] = 2 }, 5000);
        var with = CashCountSheetReport.SheetRows(line, grid, null).ToList();
        Assert.Equal(10, with.Count);
        Assert.Equal(50000, Cell(with[0], "value"));
        Assert.Equal(40000, Cell(with[1], "value"));
        Assert.Equal(5000, Cell(with[6], "value"));
        var total = with[7];
        Assert.Equal(ReportRowKind.Total, total.Kind);
        Assert.Equal(690000, Cell(total, "value"));
        Assert.Equal(700000, Cell(total, "expected"));
        Assert.Equal(-10000, Cell(total, "difference"));
        var without = CashCountSheetReport.SheetRows(line, new CountGridFact(1, null, null), null).ToList();
        Assert.Null(without[0].Cells["count"]);
        Assert.Equal("Total counted (grid not kept)", without[7].Cells["denomination"]);
    }

    [Fact]
    public void Count_grid_json_parses_notes_and_coins()
    {
        var g = SqlP2BReportSource.ParseGrid(3, "{\"notes\":{\"500\":2,\"10\":4},\"coinsPaise\":1250}");
        Assert.Equal(2, g.Notes![500]);
        Assert.Equal(1250, g.CoinsPaise);
        Assert.Null(SqlP2BReportSource.ParseGrid(3, null).Notes);
    }

    [Fact]
    public void Definitions_equal_the_prototype_catalogue_json_entries()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalogue.json")));
        var json = doc.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);
        IReportSelector[] mine =
        [
            new CashCountSheetReport(null!, null!, null!), new CounterBillsReport(null!), new RefundsReport(null!, null!), new DiscountLogReport(null!, null!),
            new BankBookReport(null!, null!), new CompanyCreditReport(null!, null!, null!), new LicenceExpiryReport(null!),
        ];
        Assert.False(json.ContainsKey("rpt.money.payment-corrections"));
        foreach (var d in mine.Select(m => m.Definition))
        {
            var e = json[d.Id];
            Assert.Equal(e.GetProperty("title").GetString(), d.Title);
            Assert.Equal(e.GetProperty("status").GetString(), d.Status);
            Assert.Equal(e.GetProperty("moneyFlag").GetBoolean(), d.MoneyFlag);
            Assert.Equal(e.GetProperty("moneyGate").GetString(), d.MoneyGate);
            Assert.Equal(e.GetProperty("viewPerms").EnumerateArray().Select(x => x.GetString()), d.ViewPerms);
            Assert.Equal(e.GetProperty("params").EnumerateArray().Select(x => x.GetString()), d.Params.Select(p => p.Key));
            Assert.Equal(e.GetProperty("formats").EnumerateArray().Select(x => x.GetString()), d.Formats.Select(f => f.ToString().ToLowerInvariant()));
            Assert.Equal(e.GetProperty("phone").GetString(), d.Phone.ToString().ToLowerInvariant());
            Assert.Equal(e.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()), d.Columns.Select(c => c.Key));
            Assert.Equal(e.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("money").GetBoolean()), d.Columns.Select(c => c.Money));
        }
    }
}
