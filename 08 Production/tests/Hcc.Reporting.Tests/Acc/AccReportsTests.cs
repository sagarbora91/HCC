// F7-R6 · batch 2 accounting on the server engine. The report bodies are driven with the query VIEWS (the stores are exercised by Hcc.Accounting.Tests): the April 2026
// rulebook golden (Goldens/rulebook-reports.json, hand-computed — profit 1,40,400; balance sheet 79,27,900 both sides; cash ratio) goes through the P&L, balance sheet and
// cash-ratio builders, small hand-made views through the rest; every result is checked screen = Excel = PDF text = print, the year-lock stamp lands in every format, and
// rule V (Owner + Accountant for P&L / balance sheet / cash ratio; PERM-ACC-001 for the others; PERM-ACC-006 for Tally) is checked through the runner.
using System.Text;
using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Errors;
using Hcc.Reporting.Reports.Acc;

namespace Hcc.Reporting.Tests.Acc;

public sealed class AccReportsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly From = new(2026, 4, 1), To = new(2026, 4, 30);
    private static readonly JsonDocument Doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Goldens", "rulebook-reports.json")));
    private static JsonElement X => Doc.RootElement.GetProperty("expected");
    private static readonly RenderContext Ctx = new("HPE", "Test Hotel", ReportFormat.Csv);
    private static readonly IReadOnlyList<ReportRow> NoStamp = [];
    private static readonly IReadOnlyList<ReportRow> Stamp = [new(ReportRowKind.Subtotal, new Dictionary<string, object?> { ["code"] = YearLockRules.ProvisionalText, ["voucher"] = YearLockRules.ProvisionalText, ["line"] = YearLockRules.ProvisionalText, ["measure"] = YearLockRules.ProvisionalText, ["party"] = YearLockRules.ProvisionalText })];

    private static ReportResult Result(string id, string title, IReadOnlyList<ReportColumn> cols, IReadOnlyList<ReportRow> rows, bool provisional = false, DateOnly? from = null, DateOnly? to = null) =>
        new(id, title, new BoundParams(new Dictionary<string, string>(), from, to), cols, rows, new ReportAbout(Rig.Now, "Test user", "basis", provisional, false, from, to));

    private static long L(JsonElement e, string p) => e.GetProperty(p).GetInt64();

    private static PlLineView[] Lines(JsonElement lines, string group) => [.. lines.EnumerateObject().Select(p => new PlLineView(p.Name, "Ledger " + p.Name, p.Value.GetInt64(), group))];

    private static PlGroupView G(string name, JsonElement e) => new(name, Lines(e.GetProperty("lines"), "indirect-expenses"), L(e, "total"));

    private static PlView GoldenPl()
    {
        var e = X.GetProperty("pl");
        return new PlView(From, To, false, new PlSectionView(Lines(e.GetProperty("revenue").GetProperty("lines"), "sales"), L(e.GetProperty("revenue"), "total")), new PlSectionView([], 0),
            new PlExpensesView(G("Cost of materials consumed", e.GetProperty("consumption")), G("Employee benefits expense", e.GetProperty("employee")), G("Finance costs", e.GetProperty("finance")),
                G("Depreciation", e.GetProperty("depreciation")), G("Other expenses", e.GetProperty("other")), L(e, "expensesTotal")), L(e, "profitBeforeTax"), [new PlTallyGroupView("sales", "Sales Accounts", 58_000_000)]);
    }

    private static BsBlockView Block(string name, JsonElement e) => new(name, [.. e.GetProperty("lines").EnumerateObject().Select(p => new BsLineView(p.Name, "Ledger " + p.Name, p.Value.GetInt64()))], L(e, "total"));

    private static BalanceSheetView GoldenBs()
    {
        var e = X.GetProperty("balanceSheet");
        return new BalanceSheetView(To, false,
            new BsLiabilitiesView(Block("Owner's funds", e.GetProperty("ownersFunds")), Block("Non-current liabilities", e.GetProperty("nonCurrentLiabilities")), Block("Current liabilities", e.GetProperty("currentLiabilities")), L(e, "liabilitiesTotal")),
            new BsAssetsView(Block("Non-current assets", e.GetProperty("nonCurrentAssets")), Block("Current assets", e.GetProperty("currentAssets")), L(e, "assetsTotal")), true, 0, 0);
    }

    [Fact]
    public void Pl_april_profit_is_1_40_400_rupees_and_three_way_equal()
    {
        var rows = PlReport.Rows(GoldenPl(), false, NoStamp);
        Assert.Equal(14_040_000L, rows[^1].Cells["amount"]);
        Assert.Equal(ReportRowKind.Total, rows[^1].Kind);
        Assert.Equal(58_000_000L, rows.Single(r => (string?)r.Cells.GetValueOrDefault("line") == "Total revenue").Cells["amount"]);
        Assert.Equal(43_960_000L, rows.Single(r => (string?)r.Cells.GetValueOrDefault("line") == "Total expenses").Cells["amount"]);
        RendererTests.ThreeWay(Result(PlReport.Id, "Profit and loss", PlReport.Columns, rows, from: From, to: To));
        var tally = PlReport.Rows(GoldenPl(), true, NoStamp);
        Assert.Equal(14_040_000L, tally[^1].Cells["amount"]);
        Assert.Equal(2, tally.Count);
        RendererTests.ThreeWay(Result(PlReport.Id, "Profit and loss", PlReport.Columns, tally, from: From, to: To));
    }

    [Fact]
    public void Balance_sheet_30_april_both_sides_79_27_900_rupees()
    {
        var rows = BalanceSheetReport.Rows(GoldenBs(), NoStamp);
        Assert.Equal(792_790_000L, rows.Single(r => (string?)r.Cells.GetValueOrDefault("line") == "Total liabilities").Cells["amount"]);
        Assert.Equal(792_790_000L, rows.Single(r => (string?)r.Cells.GetValueOrDefault("line") == "Total assets").Cells["amount"]);
        Assert.Equal("Balanced", rows[^1].Cells["block"]);
        Assert.Equal(0L, rows[^1].Cells["amount"]);
        Assert.Contains("not the statutory statement", (string)rows[0].Cells["line"]!, StringComparison.Ordinal);
        RendererTests.ThreeWay(Result(BalanceSheetReport.Id, "Balance sheet", BalanceSheetReport.Columns, rows, to: To));
    }

    [Fact]
    public void Cash_ratio_share_with_one_decimal_and_flag()
    {
        var e = X.GetProperty("cashRatio");
        var view = new CashRatioView("2026-27", L(e, "cashReceipts"), L(e, "totalReceipts"), L(e, "cashPayments"), L(e, "totalPayments"),
            (int)Math.Round(e.GetProperty("receiptsPct").GetDouble() * 100), (int)Math.Round(e.GetProperty("paymentsPct").GetDouble() * 100), true);
        var rows = CashRatioReport.Rows(view, NoStamp);
        Assert.Equal(25.4, rows[0].Cells["share"]);
        Assert.Equal(true, rows[0].Cells["flagged"]);
        Assert.Equal(3.0, rows[1].Cells["share"]);
        Assert.Equal(false, rows[1].Cells["flagged"]);
        Assert.Equal(25.4, AccShared.BpsToPct(2542));
        Assert.Equal(5.0, AccShared.BpsToPct(500));
        RendererTests.ThreeWay(Result(CashRatioReport.Id, "Cash ratio", CashRatioReport.Columns, rows));
    }

    [Fact]
    public void Trial_balance_rows_totals_and_the_pending_banner()
    {
        var rows = new[] { new TrialBalance6RowView("1000", "Cash", "cash", null, 100, 0, 50, 20, 130, 0), new TrialBalance6RowView("3100", "Capital", "capital", null, 0, 100, 0, 0, 0, 100) };
        var tb = new TrialBalance6View(From, To, false, rows, new TrialBalance6TotalsView(100, 100, 50, 20, 130, 100), false, true);
        var r = TrialBalanceReport.Rows(tb, NoStamp);
        Assert.Equal(4, r.Count); // banner, two ledgers, total
        Assert.Contains("pending", (string)r[0].Cells["ledger"]!, StringComparison.Ordinal);
        Assert.Equal("Debits and credits differ", r[^1].Cells["ledger"]);
        Assert.Equal(130L, r[^1].Cells["closeDr"]);
        RendererTests.ThreeWay(Result(TrialBalanceReport.Id, "Trial balance", TrialBalanceReport.Columns, r, true, From, To));
    }

    [Fact]
    public void Statement_opening_rows_closing()
    {
        var st = new StatementView("1000", "Cash", From, To, 500, 300, 100, 700, [new StatementRowView(new DateOnly(2026, 4, 3), "RCT/1", "Receipt", "=cash sale", "Room revenue", 300, 0, 800), new StatementRowView(new DateOnly(2026, 4, 5), "PAY/1", "Payment", "Milk", "Purchases", 0, 100, 700)]);
        var r = StatementReport.Rows(st, NoStamp);
        Assert.Equal(500L, r[0].Cells["balance"]);
        Assert.Equal(700L, r[^1].Cells["balance"]);
        Assert.Equal(300L, r[^1].Cells["debit"]);
        Assert.Equal(4, r.Count);
        var result = Result(StatementReport.Id, "Statement", StatementReport.Columns, r, from: From, to: To);
        RendererTests.ThreeWay(result);
        Assert.Contains("'=cash sale", Encoding.UTF8.GetString(new CsvRenderer().Render(result, Ctx).Bytes), StringComparison.Ordinal); // CSV neutralisation
    }

    [Fact]
    public void Day_book_total_and_voucher_count()
    {
        var db = new DayBookView(From, To, [new DayBookRowView(new DateOnly(2026, 4, 3), "RCT/1", "Receipt", "Sale", "1000", "Cash", 300, 0, "src1"), new DayBookRowView(new DateOnly(2026, 4, 3), "RCT/1", "Receipt", "Sale", "4000", "Room revenue", 0, 300, "src1")], 1, 300, 300, true);
        var r = DayBookReport.Rows(db, NoStamp);
        Assert.Equal("1000 Cash", r[0].Cells["ledger"]);
        Assert.Equal("1 voucher", r[^1].Cells["ledger"]);
        Assert.Equal(300L, r[^1].Cells["debit"]);
        RendererTests.ThreeWay(Result(DayBookReport.Id, "Day book", DayBookReport.Columns, r, from: From, to: To));
    }

    [Fact]
    public void Ageing_party_rows_bills_buckets_and_the_tie_note()
    {
        var bills = new[] { new AgeingBillView("INV/1", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), 1000, 0, 1000, 45), new AgeingBillView("INV/2", new DateOnly(2026, 4, 20), new DateOnly(2026, 4, 20), 500, 100, 400, 10) };
        var row = new AgeingRowView("1110/CMP-1", "CMP-1", "Deccan Agro Ltd", "1110", bills, 1500, 400, 1000, 0, 0, true, "Advance 100 not allocated.", 100);
        var a = new AgeingView(To, "receivable", [row], new AgeingTotalsView(1500, 400, 1000, 0, 0));
        var r = AgeingReport.Rows(a, NoStamp);
        Assert.Equal(ReportRowKind.Subtotal, r[0].Kind);
        Assert.Equal(1500L, r[0].Cells["amount"]);
        Assert.Equal(1000L, r[1].Cells["b31_60"]);
        Assert.Equal(0L, r[1].Cells["b0_30"]);
        Assert.Equal(400L, r[2].Cells["b0_30"]);
        Assert.Contains("Does not tie", (string)r[3].Cells["billRef"]!, StringComparison.Ordinal);
        r.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?> { ["party"] = "Total", ["open"] = 1500L, ["b0_30"] = 400L, ["b31_60"] = 1000L, ["b61_90"] = 0L, ["b90"] = 0L }));
        RendererTests.ThreeWay(Result(AgeingReport.Id, "Outstanding with ageing", AgeingReport.Columns, r, to: To));
    }

    [Fact]
    public void Year_lock_stamp_is_the_first_row_of_every_format()
    {
        var st = new StatementView("1000", "Cash", From, To, 0, 100, 0, 100, [new StatementRowView(new DateOnly(2026, 4, 3), "RCT/1", "Receipt", "Sale", "Room revenue", 100, 0, 100)]);
        var cases = new (string Id, IReadOnlyList<ReportColumn> Cols, List<ReportRow> Rows)[]
        {
            (StatementReport.Id, StatementReport.Columns, StatementReport.Rows(st, Stamp)),
            (PlReport.Id, PlReport.Columns, PlReport.Rows(GoldenPl(), false, Stamp)),
            (BalanceSheetReport.Id, BalanceSheetReport.Columns, BalanceSheetReport.Rows(GoldenBs(), Stamp)),
            (TrialBalanceReport.Id, TrialBalanceReport.Columns, TrialBalanceReport.Rows(new TrialBalance6View(From, To, false, [], new TrialBalance6TotalsView(0, 0, 0, 0, 0, 0), true, false), Stamp)),
        };
        foreach (var (id, cols, rows) in cases)
        {
            var r = Result(id, id, cols, rows, true, From, To);
            Assert.Equal(YearLockRules.ProvisionalText, rows[0].Cells.Values.OfType<string>().First());
            RendererTests.ThreeWay(r);
            Assert.Contains("PROVISIONAL — year reopened", Encoding.UTF8.GetString(new CsvRenderer().Render(r, Ctx).Bytes), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Definitions_follow_the_catalogue_roles_and_permissions()
    {
        var q = (AccQueries)null!;
        IReportSelector[] all = [new TrialBalanceReport(q), new StatementReport(q), new DayBookReport(q), new PlReport(q), new BalanceSheetReport(q), new CashRatioReport(q), new AgeingReport(q), new TallyXmlReport(q)];
        foreach (var s in all) Assert.Equal("acc", s.Definition.Area);
        string[] ownerAccountant = [PlReport.Id, BalanceSheetReport.Id, CashRatioReport.Id];
        foreach (var s in all)
        {
            var d = s.Definition;
            if (d.Id == TallyXmlReport.Id) { Assert.Equal(["PERM-ACC-006"], d.ViewPerms); Assert.Equal("PERM-ACC-006", d.ExportPerm); Assert.False(d.MoneyFlag); Assert.Equal([ReportFormat.Screen, ReportFormat.Xml], d.Formats); }
            else if (ownerAccountant.Contains(d.Id)) { Assert.Equal(["PERM-ACC-013"], d.ViewPerms); Assert.Equal("PERM-ACC-013", d.MoneyGate); }
            else { Assert.Equal(["PERM-ACC-001"], d.ViewPerms); Assert.Equal("PERM-ACC-001", d.MoneyGate); }
        }
    }

    [Fact]
    public async Task Rule_V_through_the_runner()
    {
        var policy = new FakePolicy();
        policy.Grants["PERM-ACC-001"] = ["OWN", "MGR", "ACC"];
        policy.Grants["PERM-ACC-013"] = ["OWN", "ACC"];
        policy.Grants["PERM-ACC-006"] = ["OWN", "ACC"];
        var q = (AccQueries)null!;
        var runner = Rig.Runner(policy, new TrialBalanceReport(q), new PlReport(q), new TallyXmlReport(q));
        var catalogue = runner.CatalogueAsync;
        foreach (var (principal, tb, pl, tally) in new[] { (1, true, true, true), (2, true, false, false), (5, true, true, true), (3, false, false, false), (4, false, false, false), (7, false, false, false) })
        {
            var entries = (await catalogue(Rig.C(principal), Ct)).Select(e => e.Definition.Id).ToHashSet();
            Assert.Equal(tb, entries.Contains(TrialBalanceReport.Id));
            Assert.Equal(pl, entries.Contains(PlReport.Id));
            Assert.Equal(tally, entries.Contains(TallyXmlReport.Id));
        }

        var ex = await Assert.ThrowsAsync<HccException>(() => runner.RunAsync(Rig.C(2), PlReport.Id, new Dictionary<string, string?>(), null, Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, ex.Code);
        var ex2 = await Assert.ThrowsAsync<HccException>(() => runner.RunAsync(Rig.C(4), TrialBalanceReport.Id, new Dictionary<string, string?>(), null, Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, ex2.Code);
    }
}
