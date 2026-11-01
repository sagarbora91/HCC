// ACC-3 (lane acc3-tests): April 2026 through the ACC-3 report builders on the server. Expectations: rulebook-reports.json, hand-computed from rulebook 8.1-8.6
// (mirrors ACC_RB_REPORTS in the prototype fixture). The month is the production engine's full month (RulebookMonthTests.FullMonthVouchers, pay run dated 30 April).
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Hcc.Application.Accounting;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Golden;

public sealed class RulebookReportsTests
{
    private static readonly JsonDocument Doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "rulebook-reports.json")));
    private static JsonElement X => Doc.RootElement.GetProperty("expected");
    private static readonly DateOnly From = new(2026, 4, 1), To = new(2026, 4, 30);
    private static long L(JsonElement e, string p) => e.GetProperty(p).GetInt64();
    private static IReadOnlyList<Voucher> Month => RulebookMonthTests.FullMonthVouchers();
    private static long ByCode(IEnumerable<PlLineView> lines, string code) => lines.Where(l => l.Code == code).Sum(l => l.Paise);
    private static long ByCode(IEnumerable<BsLineView> lines, string code) => lines.Where(l => l.Code.Split('/')[0] == code).Sum(l => l.Paise);

    private static PlView Pl(DateOnly from, DateOnly to)
    {
        var (_, period) = AccountingReports.Split(Month, from, to);
        return PlReports.Pl(from, to, period);
    }

    [Fact]
    public void Expectations_tie_to_the_rulebook()
    {
        var pl = X.GetProperty("pl");
        Assert.Equal(14_040_000, L(pl, "profitBeforeTax"));
        Assert.Equal(L(pl, "expensesTotal"), new[] { "consumption", "employee", "finance", "depreciation", "other" }.Sum(k => L(pl.GetProperty(k), "total")));
        var bs = X.GetProperty("balanceSheet");
        Assert.Equal(792_790_000, L(bs, "liabilitiesTotal"));
        Assert.Equal(L(bs, "liabilitiesTotal"), L(bs, "assetsTotal"));
        Assert.Equal(2_890_000, L(X.GetProperty("gst"), "challan"));
    }

    [Fact]
    public void Pl_april_every_line_and_profit()
    {
        var pl = Pl(From, To);
        var e = X.GetProperty("pl");
        Assert.Equal(L(e, "profitBeforeTax"), pl.ProfitBeforeTax);
        Assert.Equal(L(e.GetProperty("revenue"), "total"), pl.Revenue.Total);
        Assert.Equal(0, pl.OtherIncome.Total);
        foreach (var p in e.GetProperty("revenue").GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), ByCode(pl.Revenue.Lines, p.Name));
        foreach (var (name, group) in new[] { ("consumption", pl.Expenses.Consumption), ("employee", pl.Expenses.Employee), ("finance", pl.Expenses.Finance), ("depreciation", pl.Expenses.Depreciation), ("other", pl.Expenses.Other) })
        {
            Assert.Equal(L(e.GetProperty(name), "total"), group.Total);
            foreach (var p in e.GetProperty(name).GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), ByCode(group.Lines, p.Name));
        }

        Assert.Equal(L(e, "expensesTotal"), pl.Expenses.Total);
        Assert.NotEmpty(pl.ByTallyGroup);
    }

    [Fact]
    public void Balance_sheet_30_april_both_sides_every_block()
    {
        var b = PlReports.BalanceSheet(To, Month);
        var e = X.GetProperty("balanceSheet");
        Assert.True(b.Balanced);
        Assert.Equal((792_790_000L, 792_790_000L), (b.Liabilities.Total, b.Assets.Total));
        Assert.Equal((0L, 0L), (b.Suspense, b.OpeningDifference));
        Assert.Equal(L(e.GetProperty("ownersFunds"), "total"), b.Liabilities.OwnersFunds.Total);
        Assert.Equal(L(e.GetProperty("nonCurrentLiabilities"), "total"), b.Liabilities.NonCurrent.Total);
        Assert.Equal(L(e.GetProperty("currentLiabilities"), "total"), b.Liabilities.Current.Total);
        Assert.Equal(L(e.GetProperty("nonCurrentAssets"), "total"), b.Assets.NonCurrent.Total);
        Assert.Equal(L(e.GetProperty("currentAssets"), "total"), b.Assets.Current.Total);
        foreach (var p in e.GetProperty("currentAssets").GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), ByCode(b.Assets.Current.Lines, p.Name));
        foreach (var p in e.GetProperty("nonCurrentAssets").GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), ByCode(b.Assets.NonCurrent.Lines, p.Name));
        foreach (var p in e.GetProperty("currentLiabilities").GetProperty("lines").EnumerateObject()) Assert.Equal(p.Value.GetInt64(), ByCode(b.Liabilities.Current.Lines, p.Name));
        Assert.Equal(L(e.GetProperty("nonCurrentLiabilities").GetProperty("lines"), "2400"), ByCode(b.Liabilities.NonCurrent.Lines, "2400"));
        Assert.Equal(L(e.GetProperty("ownersFunds").GetProperty("lines"), "3100"), ByCode(b.Liabilities.OwnersFunds.Lines, "3100"));
    }

    [Fact]
    public void Cash_ratio_in_basis_points_and_flag()
    {
        var (_, period) = AccountingReports.Split(Month, From, To);
        var c = PlReports.CashRatio("2026-27", period);
        var e = X.GetProperty("cashRatio");
        Assert.Equal((L(e, "cashReceipts"), L(e, "totalReceipts"), L(e, "cashPayments"), L(e, "totalPayments")), (c.CashReceipts, c.TotalReceipts, c.CashPayments, c.TotalPayments));
        Assert.Equal((int)Math.Round(e.GetProperty("receiptsPct").GetDouble() * 100), c.ReceiptsPct);
        Assert.Equal((int)Math.Round(e.GetProperty("paymentsPct").GetDouble() * 100), c.PaymentsPct);
        Assert.True(c.Flagged);
    }

    [Fact]
    public void Ageing_payables_30_april_oldest_first()
    {
        var a = AgeingRules.Build(To, "payable", Month);
        var e = X.GetProperty("ageing").GetProperty("payable30Apr");
        Assert.Equal((L(e, "open"), L(e, "b0_30"), 0L, 0L, 0L), (a.Totals.Open, a.Totals.B0_30, a.Totals.B31_60, a.Totals.B61_90, a.Totals.B90));
        foreach (var party in new[] { "EV-MORNING", "EV-KAVERI" })
        {
            var row = a.Rows.Single(r => r.Party == party);
            Assert.Equal(L(e.GetProperty("rows").GetProperty(party), "open"), row.Open);
            Assert.Equal(row.Open, row.B0_30);
            Assert.Equal(e.GetProperty("rows").GetProperty(party).GetProperty("days").GetInt32(), row.Bills.Single(x => x.Open > 0).Days);
        }

        var morning = a.Rows.Single(r => r.Party == "EV-MORNING").Bills.Single(x => x.Open > 0);
        Assert.Equal((5_900_000L, 3_500_000L, 2_400_000L), (morning.Amount, morning.Paid, morning.Open));   // the opening 25,000 is cleared first
    }

    [Fact]
    public void Ageing_buckets_move_with_the_as_of_date()
    {
        foreach (var w in X.GetProperty("ageing").GetProperty("payableLater").EnumerateArray())
        {
            var a = AgeingRules.Build(DateOnly.Parse(w.GetProperty("asOf").GetString()!, CultureInfo.InvariantCulture), "payable", Month);
            var bucket = w.GetProperty("morning").GetProperty("bucket").GetString();
            var got = bucket switch { "b31_60" => a.Totals.B31_60, "b61_90" => a.Totals.B61_90, _ => a.Totals.B90 };
            Assert.Equal(6_600_000, got);
            Assert.Equal(6_600_000, a.Totals.Open);
        }
    }

    [Fact]
    public void Ageing_receivables_nil_on_30_april_and_deccan_plus_goibibo_on_28_april()
    {
        Assert.Equal(0, AgeingRules.Build(To, "receivable", Month).Totals.Open);   // Deccan Agro paid in full on 30 April (T7b)
        var a = AgeingRules.Build(new DateOnly(2026, 4, 28), "receivable", Month);
        var e = X.GetProperty("ageing").GetProperty("receivable28Apr");
        Assert.Equal((L(e, "open"), L(e, "b0_30")), (a.Totals.Open, a.Totals.B0_30));
        Assert.Equal(L(e.GetProperty("rows").GetProperty("1110"), "open"), a.Rows.Where(r => r.Control == "1110").Sum(r => r.Open));
        Assert.Equal(L(e.GetProperty("rows").GetProperty("1115"), "open"), a.Rows.Where(r => r.Control == "1115").Sum(r => r.Open));
    }

    [Fact]
    public void Gst_summary_april()
    {
        var e = X.GetProperty("gst");
        var (b2b, b2cs) = (L(e, "b2bTaxable"), L(e, "b2csTaxable"));
        Gstr1View gstr1 = new(From, To, [new("B2B", 500, b2b, b2b / 40, b2b / 40, 0, 1), new("B2CS", 500, b2cs, b2cs / 40, b2cs / 40, 0, 9)], [], [], "Fictional");
        var g = GstSummaryRules.Build("2026-04", Month, [], gstr1);
        Assert.Equal((L(e.GetProperty("output"), "cgst"), L(e.GetProperty("output"), "sgst"), 0L), (g.BookOutput.CgstPaise, g.BookOutput.SgstPaise, g.BookOutput.IgstPaise));
        Assert.Equal((L(e, "rate5Taxable"), 0L, b2b, b2cs), (g.OutputTaxable.Rate5, g.OutputTaxable.Rate18, g.OutputTaxable.B2b, g.OutputTaxable.B2cs));
        Assert.Equal((0L, 0L, 0L), (g.OutputTaxable.Advances11A, g.OutputTaxable.Advances11B, g.OutputTaxable.CreditNotes));
        Assert.Equal((L(e, "tcsCredit"), L(e, "cashLedgerBalance"), L(e, "challan"), L(e, "payable")), (g.TcsCredit, g.CashLedgerBalance, g.Setoff.Challan, g.Setoff.Payable));
        Assert.Equal((0L, 0L, 0L), (g.Itc.CgstPaise, g.Itc.SgstPaise, g.Itc.IgstPaise));
        Assert.Equal(0, g.Rcm);
        Assert.True(g.Ties.Gstr1);
        Assert.True(g.Ties.Gstr3b);
        Assert.Empty(g.Ties.Differences);
    }

    // ───────── Tally golden assertions (acc3-tally): counts, Σ Dr = Σ Cr, REMOTEID stability ─────────
    private static string GoldenPath(string name) => Path.Combine(AppContext.BaseDirectory, "Golden", name);

    [Fact]
    public void Tally_golden_vouchers_counts_balance_and_remoteid()
    {
        var input = JsonDocument.Parse(File.ReadAllText(GoldenPath("tally-april-2026.input.json"))).RootElement;
        var text = new UTF8Encoding(false).GetString(File.ReadAllBytes(GoldenPath("tally-april-2026.vouchers.xml")));
        var vouchers = XDocument.Parse(text).Descendants("VOUCHER").ToList();            // well-formed
        Assert.Equal(input.GetProperty("emit").GetArrayLength(), vouchers.Count);        // one Tally voucher per emitted HCC voucher
        Assert.Equal(239, vouchers.Count);                                               // the April month: 239 vouchers
        foreach (var v in vouchers)
        {
            var amounts = v.Elements("ALLLEDGERENTRIES.LIST").Select(x => (Dr: x.Element("ISDEEMEDPOSITIVE")!.Value == "Yes", A: Math.Abs(decimal.Parse(x.Element("AMOUNT")!.Value, CultureInfo.InvariantCulture)))).ToList();
            Assert.Equal(amounts.Where(a => a.Dr).Sum(a => a.A), amounts.Where(a => !a.Dr).Sum(a => a.A));   // sum Dr = sum Cr per voucher
            Assert.Equal(v.Attribute("REMOTEID")!.Value, v.Element("GUID")!.Value);                          // GUID = REMOTEID
        }

        var ids = vouchers.Select(v => v.Attribute("REMOTEID")!.Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("\r\n", text, StringComparison.Ordinal);                         // Windows line ends
    }
}
