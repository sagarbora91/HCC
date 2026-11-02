using Hcc.Domain.Money;
using Hcc.Domain.Stays;

namespace Hcc.Stays.Tests;

/// <summary>LF A1 / B1 kernel rules (LF-CONTRACT §2, §3) — the same cases as the prototype's tests/stream-lf-rate.mjs.</summary>
public class StayRateTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly Paise Tariff = new(200_000);

    private static StayRateChangeInput In(long rate, int people = 2, int children = 0, string scope = StayRateScopes.TodayNext, string reason = "Long stay") =>
        new(new Paise(rate), people, children, scope, reason);

    [Fact]
    public void Refusals_match_the_prototype()
    {
        var dep = Today.AddDays(3);
        Assert.Equal("A rate or guest-count change applies to an in-house stay only.", StayRateRules.Refusal(false, Today, dep, Tariff, 2, 0, In(150_000), 4));
        Assert.StartsWith("Nothing changes", StayRateRules.Refusal(true, Today, dep, Tariff, 2, 0, In(200_000), 4));
        Assert.StartsWith("Choose when", StayRateRules.Refusal(true, Today, dep, Tariff, 2, 0, In(1, scope: "later"), 4));
        Assert.Equal("A reason is required.", StayRateRules.Refusal(true, Today, dep, Tariff, 2, 0, In(1, reason: " "), 4));
        Assert.StartsWith("Enter the new rate", StayRateRules.Refusal(true, Today, dep, Tariff, 2, 0, In(-1), 4));
        Assert.StartsWith("The room accommodates at most 2", StayRateRules.Refusal(true, Today, dep, Tariff, 2, 0, In(200_000, 2, 1), 2));
        Assert.StartsWith("The stay ends tomorrow", StayRateRules.Refusal(true, Today, Today.AddDays(1), Tariff, 2, 0, In(1, scope: StayRateScopes.Next), 4));
        Assert.Null(StayRateRules.Refusal(true, Today, dep, Tariff, 2, 0, In(150_000), 4));
    }

    [Fact]
    public void Owner_direct_manager_within_15_percent_reception_never()
    {
        Assert.Equal(new Paise(170_000), StayRateRules.ManagerFloor(Tariff, 1500));
        Assert.True(StayRateRules.CanApplyDirectly(true, false, Tariff, 1500, new Paise(1), false, true));
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, new Paise(170_000), false, false));
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, new Paise(169_999), false, false));
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, new Paise(250_000), false, false));
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, new Paise(250_000), true, true), "a raise of guests with an extra-person charge needs approval");
        Assert.False(StayRateRules.CanApplyDirectly(false, false, Tariff, 1500, new Paise(260_000), false, false), "Reception always requests");
        Assert.True(StayRateRules.CanApprove(false, true, Tariff, 1500, new Paise(180_000)));
        Assert.False(StayRateRules.CanApprove(false, true, Tariff, 1500, new Paise(150_000)));
        Assert.True(StayRateRules.CanApprove(true, false, Tariff, 1500, new Paise(1)));
    }

    [Fact]
    public void Scope_picks_the_nights_never_a_closed_one()
    {
        var arrival = Today.AddDays(-1);
        var dep = Today.AddDays(2);
        bool Closed(DateOnly d) => d < Today;
        var nights = new[] { new StayRoomNight(1, arrival, Tariff, false), new StayRoomNight(2, Today, Tariff, false), new StayRoomNight(3, Today.AddDays(1), Tariff, true) };
        Assert.Equal(Today.AddDays(1), StayRateRules.EffectiveFrom(StayRateScopes.Next, Today, arrival, dep, Closed));
        Assert.Equal(Today, StayRateRules.EffectiveFrom(StayRateScopes.All, Today, arrival, dep, Closed));
        Assert.Equal([2L], StayRateRules.RepostPlan(nights, StayRateScopes.All, Today, arrival, dep, Closed).Select(n => n.ChargeId));
        Assert.Empty(StayRateRules.RepostPlan(nights, StayRateScopes.Next, Today, arrival, dep, Closed));
    }

    [Fact]
    public void Agreed_rate_valid_on_arrival_and_money_roles_only()
    {
        GuestAgreedRate[] rates = [new(7, 1, new Paise(170_000), null, Today, null)];
        Assert.NotNull(GuestAgreedRateRules.ValidOn(rates, 1, Today));
        Assert.Null(GuestAgreedRateRules.ValidOn(rates, 1, Today.AddDays(1)));
        Assert.Null(GuestAgreedRateRules.ValidOn(rates, 2, Today));
        Assert.Contains("REC", GuestAgreedRateRules.MoneyRoles);
        Assert.DoesNotContain("HKP", GuestAgreedRateRules.MoneyRoles);
    }
}
