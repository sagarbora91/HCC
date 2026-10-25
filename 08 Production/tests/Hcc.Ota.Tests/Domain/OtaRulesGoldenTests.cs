using Hcc.Domain.Errors;
using Hcc.Domain.Ota;

namespace Hcc.Ota.Tests.Domain;

/// <summary>Pure rules with the prototype's figures (tests/stream-channels.mjs: 18 % of 600000 = 108000, 15 % of 200000 = 30000, Suite 450000 + 10 % = 495000).</summary>
public sealed class OtaRulesGoldenTests
{
    [Theory]
    [InlineData(600000, 1800, 108000)]
    [InlineData(200000, 1500, 30000)]
    [InlineData(900000, 1800, 162000)]
    [InlineData(5, 1000, 1)]          // 0.5 rounds away from zero
    [InlineData(4, 1000, 0)]
    [InlineData(100005, 1800, 18001)] // 18000.9
    [InlineData(-5, 1000, -1)]        // symmetric for a reversal
    [InlineData(-600000, 1800, -108000)]
    [InlineData(0, 3000, 0)]
    public void Commission_is_base_times_bps_half_away_from_zero(long basePaise, int bps, long expected)
        => Assert.Equal(expected, OtaRules.Commission(basePaise, bps));

    [Theory]
    [InlineData(450000, 1000, 495000)]
    [InlineData(300000, 800, 324000)]
    [InlineData(200000, 500, 210000)]
    [InlineData(200000, 0, 200000)]
    [InlineData(333, 1000, 366)]      // 366.3
    [InlineData(335, 1000, 369)]      // 368.5 -> 369
    public void Marked_up_rate_is_tariff_times_one_plus_markup(long rate, int markup, long expected)
        => Assert.Equal(expected, OtaRules.MarkedUpRate(rate, markup));

    [Fact]
    public void Adjustment_keeps_the_running_ledger_equal_to_the_commission_of_the_final_total()
    {
        // 2 nights then extended to 3 nights at the same agreed rate: 108000 + 54000 = 162000 = 18 % of 900000.
        var accrual = OtaRules.Commission(600000, 1800);
        var delta = OtaRules.AdjustmentDelta(600000, 900000, 1800);
        Assert.Equal((108000L, 54000L), (accrual, delta));
        Assert.Equal(OtaRules.Commission(900000, 1800), accrual + delta);
        // rounding never drifts: sum of deltas always equals the commission of the last base
        long sum = OtaRules.Commission(100005, 1800), last = 100005;
        foreach (var next in new long[] { 200011, 150003, 99999, 0 })
        {
            sum += OtaRules.AdjustmentDelta(last, next, 1800);
            last = next;
        }

        Assert.Equal(0, sum);
    }

    [Fact]
    public void Terms_codes_and_statuses_follow_the_prototype_bounds()
    {
        OtaRules.Terms(3000, 2500);
        Assert.Throws<HccException>(() => OtaRules.Terms(3001, 0));
        Assert.Throws<HccException>(() => OtaRules.Terms(0, 2501));
        Assert.Throws<HccException>(() => OtaRules.Terms(-1, 0));
        Assert.Equal("RR-STD", OtaRules.ChannelCode(" rr-std ", "Classic"));
        Assert.Null(OtaRules.ChannelCode("  ", "Deluxe"));
        Assert.Throws<HccException>(() => OtaRules.ChannelCode("bad code!", "Classic"));
        Assert.Throws<HccException>(() => OtaRules.ChannelCode("X", "Classic"));
        Assert.Equal("18%", OtaRules.Pct(1800));
        Assert.Equal("12.5%", OtaRules.Pct(1250));
        Assert.True(OtaRules.EarnsCommission("Confirmed") && OtaRules.EarnsCommission("InHouse") && OtaRules.EarnsCommission("Departed"));
        Assert.False(OtaRules.EarnsCommission("Cancelled") || OtaRules.EarnsCommission("Held") || OtaRules.EarnsCommission("NoShow"));
    }

    [Fact]
    public void New_message_checks_run_in_the_prototype_order()
    {
        var bd = new DateOnly(2026, 10, 24);
        string? P(bool mapped, string a, string d, int adults, int children, int cap) =>
            OtaRules.NewMessageProblem("StayHub", "Suite", mapped, DateOnly.Parse(a), DateOnly.Parse(d), bd, adults, children, cap);
        Assert.Equal("Suite is not mapped on StayHub.", P(false, "2026-10-25", "2026-10-26", 2, 0, 4));
        Assert.Equal("Arrival is before the business date.", P(true, "2026-10-20", "2026-10-21", 2, 0, 4));
        Assert.Equal("Departure must follow arrival.", P(true, "2026-10-25", "2026-10-25", 2, 0, 4));
        Assert.Equal("Party of 5 exceeds Suite capacity (4).", P(true, "2026-10-25", "2026-10-26", 4, 1, 4));
        Assert.Equal("Party of 3 exceeds Suite capacity (2).", P(true, "2026-10-25", "2026-10-26", 3, 0, 2));
        Assert.Null(P(true, "2026-10-25", "2026-10-27", 2, 1, 4));
    }
}
