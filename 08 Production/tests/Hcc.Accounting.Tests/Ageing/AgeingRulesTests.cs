using Hcc.Application.Accounting;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Ageing;

/// <summary>
/// ACC-3 acc3-ageing (contract ACC3 §3; rulebook §7.6, §7.7, §8): outstanding with ageing (bill-wise, oldest-first, ACC-2's explicit
/// allocations win, opening = oldest item, ties to the ledger or flagged) and the GST summary on the rulebook April 2026 month (fictional).
/// Same cases as 03 Prototype/tests/stream-accounting-ageing.mjs.
/// </summary>
public sealed class AgeingRulesTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static long R(long rupees) => rupees * 100;

    private static Voucher V(string key, DateOnly date, string kind, string sub, params (string A, long D, long C)[] e)
        => new("V/" + key, date, VoucherType.Journal, "Fictional " + key, key, kind, sub, e.Select(x => new VoucherEntry(x.A, x.D, x.C)).ToList(), []);

    /// <summary>The rulebook month's party postings (§8.1, §8.2 T4, T5b, T7a, T7b, T8, T9, T11, T19) and the GST lines of T1–T4.</summary>
    private static List<Voucher> April() =>
    [
        V("opening-entry", D(4, 1), "opening", "opening-entry", ("1110/CMP-0007", R(40000), 0), ("2200/EV-MORNING", 0, R(25000)), ("1000", R(20000), 0), ("3100", 0, R(35000))),
        V("expense:8", D(4, 5), "expense", "expense", ("5000", R(59000), 0), ("2200/EV-MORNING", 0, R(59000))),
        V("receipt:7a", D(4, 10), "receipt", "company-payment", ("1030/BA-HDFC", R(40000), 0), ("1110/CMP-0007", 0, R(40000))),
        V("expense:9", D(4, 12), "expense", "expense", ("5010", R(42000), 0), ("2200/EV-KAVERI", 0, R(42000))),
        V("vendor-payment:11", D(4, 18), "vendor", "vendor-payment", ("2200/EV-MORNING", R(60000), 0), ("1030/BA-HDFC", 0, R(60000))),
        V("line:t1", D(4, 20), "line", "room", ("1100", R(420000), 0), ("4900", R(10000), 0), ("4000", 0, R(410000)), ("2110", 0, R(10000)), ("2120", 0, R(10000))),
        V("line:t2", D(4, 20), "line", "food", ("1100", R(105000), 0), ("4200", 0, R(100000)), ("2110", 0, R(2500)), ("2120", 0, R(2500))),
        V("pos:t3", D(4, 20), "pos", "sale", ("1000", R(63000), 0), ("4200", 0, R(60000)), ("2110", 0, R(1500)), ("2120", 0, R(1500))),
        V("line:t4", D(4, 21), "line", "room", ("1115/CHN-GOIBIBO", R(21000), 0), ("4000", 0, R(20000)), ("2110", 0, R(500)), ("2120", 0, R(500))),
        V("receipt:t5", D(4, 28), "receipt", "settle", ("1110/CMP-0007", R(63000), 0), ("2000", R(10000), 0), ("1000", R(452000), 0), ("1100", 0, R(525000))),
        V("ota:t19", D(4, 29), "ota", "payout", ("1030/BA-HDFC", R(17360), 0), ("5070", R(3540), 0), ("1340", R(100), 0), ("1115/CHN-GOIBIBO", 0, R(21000))),
        V("receipt:7b", D(4, 30), "receipt", "company-payment", ("1030/BA-HDFC", R(57000), 0), ("1360", R(6000), 0), ("1110/CMP-0007", 0, R(63000))),
    ];

    [Fact]
    public void Golden_receivable_Deccan_Agro_63000_in_0_30_on_29_April_and_nothing_open_on_30_April()
    {
        var a = AgeingRules.Build(D(4, 29), AgeingRules.Receivable, April());
        var row = Assert.Single(a.Rows);
        Assert.Equal(("1110/CMP-0007", "CMP-0007", "1110"), (row.Account, row.Party, row.Control));
        Assert.Equal((R(63000), R(63000), 0L, 0L, 0L, false), (row.Open, row.B0_30, row.B31_60, row.B61_90, row.B90, row.Flagged));
        var bill = Assert.Single(row.Bills);
        Assert.Equal(("receipt:t5", D(4, 28), 1), (bill.Ref, bill.Date, bill.Days)); // the opening 40,000 was paid by T7a (oldest first)
        Assert.Equal(R(63000), a.Totals.B0_30);
        Assert.Empty(AgeingRules.Build(D(4, 30), AgeingRules.Receivable, April()).Rows);
    }

    [Fact]
    public void Golden_payables_Morning_24000_Kaveri_42000_in_0_30_oldest_first_opening_paid_first()
    {
        var a = AgeingRules.Build(D(4, 30), AgeingRules.Payable, April());
        Assert.Equal(["2200/EV-KAVERI", "2200/EV-MORNING"], a.Rows.Select(r => r.Account));
        var k = a.Rows[0];
        var m = a.Rows[1];
        Assert.Equal((R(42000), R(42000)), (k.Open, k.B0_30));
        Assert.Equal((R(24000), R(24000), false), (m.Open, m.B0_30, m.Flagged));
        var b = Assert.Single(m.Bills);
        Assert.Equal(("expense:8", R(59000), R(35000), R(24000), 25), (b.Ref, b.Amount, b.Paid, b.Open, b.Days));
        Assert.Equal((R(66000), R(66000)), (a.Totals.Open, a.Totals.B0_30));
    }

    [Fact]
    public void Explicit_ACC2_allocations_win_over_oldest_first()
    {
        var alloc = new Dictionary<string, IReadOnlyList<(string Ref, long Paise)>> { ["vendor-payment:11"] = [("expense:8", R(59000))] };
        var m = AgeingRules.Build(D(4, 30), AgeingRules.Payable, April(), null, alloc).Rows.Single(r => r.Account == "2200/EV-MORNING");
        var b = Assert.Single(m.Bills);
        Assert.Equal((AgeingRules.OpeningRef, R(24000), 29), (b.Ref, b.Open, b.Days)); // the rest 1,000 oldest-first to the opening
        Assert.Equal(R(24000), m.Open);
    }

    [Fact]
    public void A_later_void_cancels_its_payment_and_an_advance_is_flagged_with_a_note()
    {
        var vs = April();
        vs.Add(V("vendor-payment:11/void", D(4, 30), "vendor", "vendor-payment-void", ("2200/EV-MORNING", -R(60000), 0), ("1030/BA-HDFC", 0, -R(60000))));
        var m = AgeingRules.Build(D(4, 30), AgeingRules.Payable, vs).Rows.Single(r => r.Account == "2200/EV-MORNING");
        Assert.Equal((R(84000), R(84000), 2), (m.Open, m.B0_30, m.Bills.Count));
        vs = April();
        vs.Add(V("vendor-payment:12", D(4, 30), "vendor", "vendor-advance", ("2200/EV-KAVERI", R(50000), 0), ("1030/BA-HDFC", 0, R(50000))));
        var k = AgeingRules.Build(D(4, 30), AgeingRules.Payable, vs).Rows.Single(r => r.Account == "2200/EV-KAVERI");
        Assert.True(k.Flagged);
        Assert.Equal((-R(8000), 0L, R(8000)), (k.Open, k.B0_30, k.Advance));
        Assert.Contains("Advance paid", k.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "0-30")]
    [InlineData(30, "0-30")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "61-90")]
    [InlineData(90, "61-90")]
    [InlineData(91, "90+")]
    public void Buckets_by_days_from_the_bill_date(int days, string bucket) => Assert.Equal(bucket, AgeingRules.Bucket(days));

    [Fact]
    public void Old_bills_fall_into_later_buckets()
    {
        var a = AgeingRules.Build(D(6, 15), AgeingRules.Payable, April());
        var m = a.Rows.Single(r => r.Account == "2200/EV-MORNING");
        Assert.Equal((0L, 0L, R(24000), 0L), (m.B0_30, m.B31_60, m.B61_90, m.B90)); // 5 Apr → 15 Jun = 71 days
    }

    private static Gstr1View Gstr1(long b2bTaxable, long b2csTaxable) => new(D(4, 1), D(4, 30),
    [
        new Gstr1RateView("B2B", 500, b2bTaxable, b2bTaxable / 40, b2bTaxable / 40, 0, 1),
        new Gstr1RateView("B2CS", 500, b2csTaxable, b2csTaxable / 40, b2csTaxable / 40, 0, 9),
    ], [], [], "Fictional");

    [Fact]
    public void Golden_GST_summary_output_29000_TCS_100_challan_28900_ties_to_GSTR1_and_the_GST_month()
    {
        var g = GstSummaryRules.Build("2026-04", April(), [], Gstr1(R(60000), R(520000)));
        Assert.Equal((R(14500), R(14500), 0L), (g.BookOutput.CgstPaise, g.BookOutput.SgstPaise, g.BookOutput.IgstPaise));
        Assert.Equal((R(29000), R(3000), R(26000), 0L), (g.Output.Rate5, g.Output.B2b, g.Output.B2cs, g.Output.Rate18));
        Assert.Equal((R(60000), R(520000)), (g.OutputTaxable.B2b, g.OutputTaxable.B2cs));
        Assert.Equal((R(100), R(100), R(28900), R(29000)), (g.TcsCredit, g.CashLedgerBalance, g.Setoff.Challan, g.Setoff.Payable));
        Assert.Equal((0L, 0L, 0L), (g.Itc.CgstPaise, g.Itc.SgstPaise, g.Itc.IgstPaise));
        Assert.True(g.Ties.Gstr1);
        Assert.True(g.Ties.Gstr3b);
        Assert.Empty(g.Ties.Differences);
    }

    [Fact]
    public void GST_summary_lists_a_GSTR1_difference_in_words()
    {
        var g = GstSummaryRules.Build("2026-04", April(), [], Gstr1(R(60000), R(500000)));
        Assert.False(g.Ties.Gstr1);
        Assert.True(g.Ties.Gstr3b);
        Assert.Contains("differ by ₹1000.00", Assert.Single(g.Ties.Differences), StringComparison.Ordinal);
    }
}
