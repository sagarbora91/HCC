// F8-QW-NS · overbooking cap at the channel push (QWQ-2) and the due no-show rules (QWQ-7). Pure; no database.
using Hcc.Application.Reservations;
using Hcc.Domain.Ota;
using Xunit;

namespace Hcc.Ota.Tests.Domain;

public sealed class NoShowOverbookTests
{
    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(2, 2, 4)]
    [InlineData(-1, 0, 0)]
    [InlineData(2, -5, 2)]
    public void F8_QW_NS_PushAvail_cap_zero_changes_nothing_and_cap_adds_rooms(int sellable, int cap, int expected) =>
        Assert.Equal(expected, OtaRules.PushAvail(sellable, cap));

    [Fact]
    public void F8_QW_NS_PastCutoff_boundaries()
    {
        var biz = new DateOnly(2026, 11, 8);
        var cut = NoShowRules.Cutoff("23:00");
        Assert.False(NoShowRules.PastCutoff(biz, new DateTime(2026, 11, 8, 22, 59, 0), cut));
        Assert.True(NoShowRules.PastCutoff(biz, new DateTime(2026, 11, 8, 23, 0, 0), cut));
        Assert.True(NoShowRules.PastCutoff(biz, new DateTime(2026, 11, 9, 0, 5, 0), cut));
        Assert.False(NoShowRules.PastCutoff(biz, new DateTime(2026, 11, 7, 23, 30, 0), cut));
    }

    [Theory]
    [InlineData("21:30", 21, 30)]
    [InlineData("bad", 23, 0)]
    [InlineData(null, 23, 0)]
    public void F8_QW_NS_Cutoff_parses_or_defaults(string? raw, int h, int m) => Assert.Equal(new TimeOnly(h, m), NoShowRules.Cutoff(raw));

    [Fact]
    public void F8_QW_NS_Charge_and_text_are_shown_not_posted()
    {
        Assert.Equal(100000, NoShowRules.Charge(200000, 5000));
        Assert.Equal(0, NoShowRules.Charge(200000, 0));
        Assert.Equal("Policy Half night: no-show charge ₹1,000 — not posted", NoShowRules.ChargeText("Half night", 100000));
        Assert.Contains("hidden", NoShowRules.ChargeText("Default", null));
    }
}
