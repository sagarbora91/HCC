using Hcc.Application.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Accounting.Tests.Domain;

/// <summary>
/// ACC-2 acc2-gst · the month-end GST set-off in the s.49 / 49A / 49B / Rule 88A order (rulebook §4.12, E1, §8.6) — the SAME six hand-worked
/// cases as the prototype (03 Prototype/tests/stream-accounting-gst.mjs) — plus the journal lines, the month arithmetic (carry-forward after a
/// posted set-off), the advance-GST rules (A6 / A6a / A6b) and the tax mapping keys.
/// </summary>
public sealed class GstSetOffTests
{
    private static long R(long rupees) => rupees * 100;

    private static GstHeads H(long igst, long cgst, long sgst) => new(R(igst), R(cgst), R(sgst));

    private static (string Key, long Dr, long Cr)[] Lines(GstSetOffResult x) => GstSetOff.Entries(x).Select(e => (e.MapKey, e.DebitPaise / 100, e.CreditPaise / 100)).ToArray();

    [Fact]
    public void Case1_rulebook_8_6_output_14500_plus_14500_no_itc_tcs_100_challan_28900()
    {
        var x = GstSetOff.Compute(H(0, 14_500, 14_500), H(0, 0, 0), R(100));
        Assert.Equal(H(0, 14_500, 14_500), x.Cash);
        Assert.Equal(R(29_000), x.Payable);
        Assert.Equal(R(28_900), x.Challan);
        Assert.Equal([("tax:output-cgst", 14_500L, 0L), ("tax:output-sgst", 14_500L, 0L), ("tax:gst-cash", 0L, 29_000L)], Lines(x));
    }

    [Fact]
    public void Case2_partial_itc_each_head_against_its_own_credit_rest_in_cash()
    {
        var x = GstSetOff.Compute(H(0, 10_000, 10_000), H(0, 4_000, 3_000));
        Assert.Equal(H(0, 6_000, 7_000), x.Cash);
        Assert.Equal(GstHeads.Zero, x.CreditLeft);
        Assert.Equal(R(13_000), x.Challan);
        Assert.Equal([("tax:output-cgst", 10_000L, 0L), ("tax:output-sgst", 10_000L, 0L), ("tax:gst-input", 0L, 4_000L), ("tax:gst-input-sgst", 0L, 3_000L), ("tax:gst-cash", 0L, 13_000L)],
            Lines(x));
    }

    [Fact]
    public void Case3_excess_credit_cgst_and_sgst_never_cross()
    {
        var x = GstSetOff.Compute(H(0, 5_000, 5_000), H(0, 8_000, 6_000));
        Assert.Equal(GstHeads.Zero, x.Cash);
        Assert.Equal(H(0, 3_000, 1_000), x.CreditLeft);
        Assert.Equal(0, x.Challan);
        Assert.Equal(0, x.UseCgst.Sgst);
        Assert.Equal(0, x.UseSgst.Cgst);
        Assert.Equal([("tax:output-cgst", 5_000L, 0L), ("tax:output-sgst", 5_000L, 0L), ("tax:gst-input", 0L, 5_000L), ("tax:gst-input-sgst", 0L, 5_000L)], Lines(x));
    }

    [Fact]
    public void Case4_igst_credit_goes_where_the_own_credit_falls_short()
    {
        var x = GstSetOff.Compute(H(0, 10_000, 10_000), H(12_000, 1_000, 5_000));
        Assert.Equal(H(0, 9_000, 3_000), x.UseIgst);
        Assert.Equal(H(0, 1_000, 0), x.UseCgst);
        Assert.Equal(H(0, 0, 5_000), x.UseSgst);
        Assert.Equal(H(0, 0, 2_000), x.Cash);
        Assert.Equal(R(2_000), x.Challan);
        Assert.Equal([("tax:output-cgst", 10_000L, 0L), ("tax:output-sgst", 10_000L, 0L), ("tax:gst-input-igst", 0L, 12_000L), ("tax:gst-input", 0L, 1_000L),
            ("tax:gst-input-sgst", 0L, 5_000L), ("tax:gst-cash", 0L, 2_000L)], Lines(x));
    }

    [Fact]
    public void Case5_igst_output_paid_by_igst_then_cgst_credit_then_sgst_credit()
    {
        var x = GstSetOff.Compute(H(9_000, 2_000, 2_000), H(3_000, 6_000, 5_000));
        Assert.Equal(H(3_000, 0, 0), x.UseIgst);
        Assert.Equal(H(4_000, 2_000, 0), x.UseCgst);
        Assert.Equal(H(2_000, 0, 2_000), x.UseSgst);
        Assert.Equal(H(0, 0, 1_000), x.CreditLeft);
        Assert.Equal(0, x.Challan);
        Assert.Equal([("tax:output-igst", 9_000L, 0L), ("tax:output-cgst", 2_000L, 0L), ("tax:output-sgst", 2_000L, 0L), ("tax:gst-input-igst", 0L, 3_000L),
            ("tax:gst-input", 0L, 6_000L), ("tax:gst-input-sgst", 0L, 4_000L)], Lines(x));
    }

    [Fact]
    public void Case6_igst_credit_exhausted_first_s49A_and_surplus_cash_means_no_challan()
    {
        var x = GstSetOff.Compute(H(0, 3_000, 3_000), H(5_000, 2_000, 0), R(500));
        Assert.Equal(H(0, 2_000, 3_000), x.UseIgst);
        Assert.Equal(H(0, 1_000, 0), x.UseCgst);
        Assert.Equal(H(0, 1_000, 0), x.CreditLeft);
        Assert.Equal((0L, 0L), (x.Payable, x.Challan));
        Assert.Equal([("tax:output-cgst", 3_000L, 0L), ("tax:output-sgst", 3_000L, 0L), ("tax:gst-input-igst", 0L, 5_000L), ("tax:gst-input", 0L, 1_000L)], Lines(x));
    }

    [Fact]
    public void Negative_heads_count_as_nil_and_nothing_yields_no_lines()
    {
        var x = GstSetOff.Compute(new GstHeads(0, -50, 0), new GstHeads(0, 0, -10), 5);
        Assert.Equal((0L, 0L), (x.Payable, x.Challan));
        Assert.Empty(GstSetOff.Entries(x));
    }

    [Fact]
    public void Every_case_balances_and_the_tax_keys_resolve_to_the_rulebook_ledgers()
    {
        foreach (var x in new[] { GstSetOff.Compute(H(9_000, 2_000, 2_000), H(3_000, 6_000, 5_000)), GstSetOff.Compute(H(0, 10_000, 10_000), H(12_000, 1_000, 5_000), R(300)) })
        {
            var e = GstSetOff.Entries(x);
            Assert.Equal(e.Sum(l => l.DebitPaise), e.Sum(l => l.CreditPaise));
        }

        var m = ChartOfAccounts.DefaultMapping;
        Assert.Equal(("2110", "2120", "2130", "1330", "1340", "1350", "2140"),
            (m[GstSetOff.KeyOutputCgst], m[GstSetOff.KeyOutputSgst], m[GstSetOff.KeyOutputIgst], m[GstSetOff.KeyInputIgst], m[GstSetOff.KeyCash], m[GstSetOff.KeyAdvance], m[GstSetOff.KeyRcm]));
        Assert.All(new[] { GstSetOff.KeyOutputCgst, GstSetOff.KeyCash, GstSetOff.KeyAdvance }, k => Assert.True(ChartOfAccounts.IsValidKey(k) && ChartOfAccounts.Resolve(m, k) is not null, k));
    }

    [Fact]
    public void The_set_off_source_is_a_balanced_tax_journal_on_the_posting_date()
    {
        var d = new DateOnly(2026, 5, 1);
        var s = SourceRules.GstSetoff("gst-setoff:2026-04@2026-05-01", d, GstSetOff.Compute(H(0, 14_500, 14_500), GstHeads.Zero, R(100)), "GST set-off for 2026-04");
        var v = VoucherBuilder.Build(d, [s], ChartOfAccounts.Effective(null)).Single();
        Assert.Equal((VoucherType.Journal, "tax", "gst-setoff", "JRN/20260501/001"), (v.Type, v.SourceKind, v.Sub, v.Number));
        Assert.Equal([("2110", R(14_500), 0L), ("2120", R(14_500), 0L), ("1340", 0L, R(29_000))], v.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToArray());
        Assert.Empty(v.Unmapped);
        var back = SourceRules.GstSetoffReversal("rev", d.AddDays(1), s.Entries, "reversed");
        Assert.Equal(s.Entries.Select(e => (e.MapKey, e.CreditPaise, e.DebitPaise)), back.Entries.Select(e => (e.MapKey, e.DebitPaise, e.CreditPaise)));
    }

    [Fact]
    public void Advance_gst_cum_tax_at_the_room_rate_and_its_reversal()
    {
        Assert.Equal((R(500), R(250), R(250)), GstSetOff.CumTax(R(10_500), 500));
        Assert.Equal((R(1_800), R(900), R(900)), GstSetOff.CumTax(R(11_800), 1_800));
        Assert.Equal((5L, 3L, 2L), GstSetOff.CumTax(101, 500));
        Assert.Equal((-5L, -3L, -2L), GstSetOff.CumTax(-101, 500));
        Assert.Equal((0L, 0L, 0L), GstSetOff.CumTax(R(1_000), 0));
        var d = new DateOnly(2026, 10, 24);
        var map = ChartOfAccounts.Effective(null);
        var a = VoucherBuilder.Build(d, [SourceRules.AdvanceGst("advance-gst:folio:1/receipt:2", d, R(250), R(250), "A6")], map).Single();
        Assert.Equal((VoucherType.Journal, "tax", "advance-gst"), (a.Type, a.SourceKind, a.Sub));
        Assert.Equal([("1350", R(500), 0L), ("2110", 0L, R(250)), ("2120", 0L, R(250))], a.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToArray());
        var r = VoucherBuilder.Build(d, [SourceRules.AdvanceGstReversal("advance-gst:folio:1/receipt:3", d, R(125), R(125), "A6b")], map).Single();
        Assert.Equal([("2110", R(125), 0L), ("2120", R(125), 0L), ("1350", 0L, R(250))], r.Entries.Select(e => (e.Account, e.DebitPaise, e.CreditPaise)).ToArray());
    }

    private static Voucher V(DateOnly d, string kind, string sub, params (string Account, long Dr, long Cr)[] e)
        => new("X", d, VoucherType.Journal, "t", kind + ":" + d.ToString("O", System.Globalization.CultureInfo.InvariantCulture), kind, sub,
            e.Select(x => new VoucherEntry(x.Account, R(x.Dr), R(x.Cr))).ToList(), []);

    [Fact]
    public void The_month_reads_flows_without_set_off_journals_and_carries_credit_and_cash_after_a_posted_set_off()
    {
        var apr = new DateOnly(2026, 4, 15);
        var may = new DateOnly(2026, 5, 10);
        var vouchers = new List<Voucher>
        {
            V(apr, "line", "room", ("1100", 30_000, 0), ("4000", 0, 29_000), ("2110", 0, 500), ("2120", 0, 500)),
            V(apr, "expense", "expense", ("5020", 7_000, 0), ("1310", 1_500, 0), ("1320", 1_500, 0), ("1000", 0, 10_000)),
            V(apr, "ota", "ota-payout", ("1340", 100, 0), ("1115/goibibo", 0, 100)),
            V(may, "line", "room", ("1100", 2_100, 0), ("4000", 0, 2_000), ("2110", 0, 50), ("2120", 0, 50)),
            // April's set-off journal dated in May: ignored by the flows (the stored set-off carries its use).
            V(may, "tax", "gst-setoff", ("2110", 500, 0), ("2120", 500, 0), ("1310", 0, 500), ("1320", 0, 500)),
        };
        var april = GstMonthRules.Compute("2026-04", vouchers, []);
        Assert.Equal((H(0, 500, 500), H(0, 1_500, 1_500), R(100), R(100)), (april.Output, april.Credit, april.Tcs, april.CashBalance));
        Assert.Equal((0L, H(0, 1_000, 1_000)), (april.Setoff.Payable, april.Setoff.CreditLeft));
        var posted = new StoredGstSetoff(1, "2026-04", new DateOnly(2026, 5, 1), "gst-setoff:2026-04@2026-05-01", april.Setoff, 1, DateTime.UnixEpoch);
        var mayFig = GstMonthRules.Compute("2026-05", vouchers, [posted]);
        Assert.Equal((H(0, 50, 50), H(0, 1_000, 1_000), H(0, 1_000, 1_000)), (mayFig.Output, mayFig.BroughtForward, mayFig.Credit));
        Assert.Equal((R(100), 0L, H(0, 950, 950)), (mayFig.CashBalance, mayFig.Setoff.Payable, mayFig.Setoff.CreditLeft));
        // Without the April set-off posted, May still sees the April credit (nothing used).
        Assert.Equal(H(0, 1_500, 1_500), GstMonthRules.Compute("2026-05", vouchers, []).BroughtForward);
        Assert.Equal(("2026-04", new DateOnly(2026, 4, 30)), (GstMonthRules.MonthOf(apr), GstMonthRules.Range("2026-04").To));
        Assert.False(GstMonthRules.IsMonth("2026-13"));
        Assert.Equal("gst-setoff:2026-04@2026-05-01", GstMonthRules.SourceKey("2026-04", new DateOnly(2026, 5, 1)));
    }
}
