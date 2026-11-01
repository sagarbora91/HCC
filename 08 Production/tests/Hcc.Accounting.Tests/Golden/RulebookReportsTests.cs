// ACC-3 (lane acc3-tests): April 2026 through the ACC-3 reports on the server, expectations in rulebook-reports.json (hand-computed from rulebook 8.1-8.6).
// STATUS AT WRITING: acc3-pl / acc3-ageing / acc3-tally had not landed, so this file is behind the symbol ACC3_REPORTS (add <DefineConstants>ACC3_REPORTS</DefineConstants>
// to the test project once the lanes are merged and the ADAPTER methods at the bottom are pointed at the real query classes). Without the symbol the file compiles to nothing
// and cannot break the build. Contract names: PlView, BalanceSheetView, CashRatioView (AccountingReportContracts.cs), PlQueries, AgeingQueries, GstSummaryQueries.
#if ACC3_REPORTS
using System.Text.Json;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Golden;

public sealed class RulebookReportsTests
{
    private static readonly JsonDocument Doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-reports.json")));
    private static JsonElement X => Doc.RootElement.GetProperty("expected");
    private static readonly DateOnly From = new(2026, 4, 1), To = new(2026, 4, 30);
    private static long L(JsonElement e, string p) => e.GetProperty(p).GetInt64();
    private static IReadOnlyList<Voucher> Month => RulebookMonthTests.FullMonthVouchers();
    private static long Sum(JsonElement lines) => lines.EnumerateObject().Sum(p => p.Value.GetInt64());

    [Fact]
    public void Expectations_tie_to_the_rulebook()
    {
        var pl = X.GetProperty("pl");
        Assert.Equal(14_040_000, L(pl, "profitBeforeTax"));
        Assert.Equal(L(pl, "expensesTotal"), L(pl.GetProperty("consumption"), "total") + L(pl.GetProperty("employee"), "total") + L(pl.GetProperty("finance"), "total") + L(pl.GetProperty("other"), "total"));
        var bs = X.GetProperty("balanceSheet");
        Assert.Equal(792_790_000, L(bs, "liabilitiesTotal"));
        Assert.Equal(L(bs, "liabilitiesTotal"), L(bs, "assetsTotal"));
        Assert.Equal(2_890_000, L(X.GetProperty("gst"), "challan"));
    }

    [Fact]
    public void Pl_april_every_line_and_profit()
    {
        var pl = Adapter.Pl(Month, From, To);
        var e = X.GetProperty("pl");
        Assert.Equal(L(e, "profitBeforeTax"), pl.ProfitBeforeTax);
        Assert.Equal(L(e.GetProperty("revenue"), "total"), pl.Revenue.Total);
        foreach (var (name, group) in new[] { ("consumption", pl.Expenses.Consumption), ("employee", pl.Expenses.Employee), ("finance", pl.Expenses.Finance), ("depreciation", pl.Expenses.Depreciation), ("other", pl.Expenses.Other) })
        {
            Assert.Equal(L(e.GetProperty(name), "total"), group.Total);
            foreach (var p in e.GetProperty(name).GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), group.Lines.Where(l => l.Code == p.Name).Sum(l => l.Paise));
        }
        foreach (var p in e.GetProperty("revenue").GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), pl.Revenue.Lines.Where(l => l.Code == p.Name).Sum(l => l.Paise));
        Assert.Equal(L(e, "expensesTotal"), pl.Expenses.Total);
    }

    [Fact]
    public void Balance_sheet_30_april_both_sides_every_block()
    {
        var b = Adapter.BalanceSheet(Month, To);
        var e = X.GetProperty("balanceSheet");
        Assert.True(b.Balanced);
        Assert.Equal((792_790_000L, 792_790_000L), (b.Liabilities.Total, b.Assets.Total));
        Assert.Equal((0L, 0L), (b.Suspense, b.OpeningDifference));
        Assert.Equal(L(e.GetProperty("ownersFunds"), "total"), b.Liabilities.OwnersFunds.Total);
        Assert.Equal(L(e.GetProperty("nonCurrentLiabilities"), "total"), b.Liabilities.NonCurrent.Total);
        Assert.Equal(L(e.GetProperty("currentLiabilities"), "total"), b.Liabilities.Current.Total);
        Assert.Equal(L(e.GetProperty("nonCurrentAssets"), "total"), b.Assets.NonCurrent.Total);
        Assert.Equal(L(e.GetProperty("currentAssets"), "total"), b.Assets.Current.Total);
    }

    [Fact]
    public void Cash_ratio_and_flag()
    {
        var c = Adapter.CashRatio(Month, "2026-27");
        var e = X.GetProperty("cashRatio");
        Assert.Equal((L(e, "cashReceipts"), L(e, "totalReceipts"), L(e, "cashPayments"), L(e, "totalPayments")), (c.CashReceipts, c.TotalReceipts, c.CashPayments, c.TotalPayments));
        Assert.True(c.Flagged);
    }

    [Fact]
    public void Ageing_payables_30_april_oldest_first()
    {
        var a = Adapter.Ageing(Month, To, "payable");
        var e = X.GetProperty("ageing").GetProperty("payable30Apr");
        Assert.Equal(L(e, "open"), a.Totals.Open);
        Assert.Equal(L(e, "b0_30"), a.Totals.B0To30);
        Assert.Equal(L(e.GetProperty("rows").GetProperty("EV-MORNING"), "open"), a.Rows.Single(r => r.Party == "EV-MORNING").Open);
        Assert.Equal(L(e.GetProperty("rows").GetProperty("EV-KAVERI"), "open"), a.Rows.Single(r => r.Party == "EV-KAVERI").Open);
    }

    [Fact]
    public void Ageing_receivables_28_april_deccan_and_goibibo()
    {
        var a = Adapter.Ageing(Month, new DateOnly(2026, 4, 28), "receivable");
        Assert.Equal(L(X.GetProperty("ageing").GetProperty("receivable28Apr"), "open"), a.Totals.Open);
        Assert.Equal(0, Adapter.Ageing(Month, To, "receivable").Totals.Open);   // Deccan Agro paid in full on 30 April
    }

    [Fact]
    public void Gst_summary_april()
    {
        var g = Adapter.GstSummary(Month, "2026-04");
        var e = X.GetProperty("gst");
        Assert.Equal((L(e, "tcsCredit"), L(e, "challan")), (g.TcsCredit, g.Setoff.Challan));
        Assert.Equal((0L, 0L, 0L), (g.Itc.Cgst, g.Itc.Sgst, g.Itc.Igst));
        Assert.Empty(g.Ties.Differences);
    }

    /// <summary>ADAPTER: the only place that names the lanes' query classes; point these at the real signatures after the merge.</summary>
    private static class Adapter
    {
        public static PlView Pl(IReadOnlyList<Voucher> all, DateOnly from, DateOnly to) { var (o, p) = AccountingReports.Split(all, from, to); return PlQueries.Pl(from, to, o, p); }
        public static BalanceSheetView BalanceSheet(IReadOnlyList<Voucher> all, DateOnly asOf) => PlQueries.BalanceSheet(asOf, all);
        public static CashRatioView CashRatio(IReadOnlyList<Voucher> all, string fy) => PlQueries.CashRatio(fy, all);
        public static AgeingView Ageing(IReadOnlyList<Voucher> all, DateOnly asOf, string side) => AgeingQueries.Build(asOf, side, all);
        public static GstSummaryView GstSummary(IReadOnlyList<Voucher> all, string month) => GstSummaryQueries.Build(month, all);
    }
}
#endif
