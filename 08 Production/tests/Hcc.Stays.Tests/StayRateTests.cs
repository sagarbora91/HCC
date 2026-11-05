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

    // ── CTL-01: the agreed rate as well as the tariff; the stricter wins (same cases as tests/stream-af2-ctl01.mjs) ──

    [Fact]
    public void Ctl01_at_the_threshold_passes_one_paisa_below_needs_the_owner()
    {
        var agreed = new Paise(250_000);
        Assert.Equal(new Paise(212_500), StayRateRules.AgreedFloor(agreed, 1500));
        Assert.False(StayRateRules.OverThreshold(agreed, 1500, new Paise(212_500)));
        Assert.True(StayRateRules.OverThreshold(agreed, 1500, new Paise(212_499)));
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, new Paise(212_500), false, false));
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, new Paise(212_499), false, false));
        Assert.True(StayRateRules.CanApprove(false, true, Tariff, 1500, agreed, 1500, new Paise(212_500)));
        Assert.False(StayRateRules.CanApprove(false, true, Tariff, 1500, agreed, 1500, new Paise(212_499)));
        Assert.True(StayRateRules.CanApprove(true, false, Tariff, 1500, agreed, 1500, new Paise(1)), "the Owner decides anything");
    }

    [Fact]
    public void Ctl01_the_audit_case_2500_to_1900_against_a_2000_tariff_needs_the_owner()
    {
        // Within 15 % of the tariff (₹1,700 floor) but 24 % below the agreed ₹2,500.
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, new Paise(190_000), false, false), "tariff-only rule (before CTL-01)");
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, new Paise(250_000), 1500, new Paise(190_000), false, false));
        Assert.Equal(2400, StayRateRules.ReductionBps(new Paise(250_000), new Paise(190_000)));
        Assert.Equal(0, StayRateRules.ReductionBps(new Paise(250_000), new Paise(260_000)));
        // Raising the threshold to 25 % lets the Manager do it.
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, new Paise(250_000), 2500, new Paise(190_000), false, false));
    }

    [Fact]
    public void Ctl01_agreed_rate_below_the_tariff_keeps_the_tariff_limit()
    {
        var agreed = new Paise(180_000); // a regular guest's agreed rate under the ₹2,000 master tariff
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, new Paise(170_000), false, false));
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, new Paise(169_999), false, false), "the tariff floor is the stricter one here");
        Assert.False(StayRateRules.OverThreshold(agreed, 1500, new Paise(169_999)));
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, new Paise(200_000), false, false));
    }

    [Fact]
    public void Ctl01_agreed_base_survives_manager_steps_and_moves_with_the_owner()
    {
        Assert.Equal(new Paise(220_000), StayRateRules.AgreedBase(null, null, 220_000));
        Assert.Equal(new Paise(250_000), StayRateRules.AgreedBase(null, 250_000, 220_000));
        Assert.Equal(new Paise(200_000), StayRateRules.AgreedBase(200_000, 250_000, 200_000));
        // Mid-stay: 2,500 → 2,200 by the Manager; the next step to 2,100 is 16 % below the agreed 2,500 although only 4.5 % below 2,200.
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, StayRateRules.AgreedBase(250_000, 250_000, 220_000), 1500, new Paise(210_000), false, false));
        Assert.Equal(new Paise(0), StayRateRules.AgreedFloor(new Paise(250_000), 10_000));
    }

    [Fact]
    public void Ctl01_a_change_that_does_not_move_the_rate_is_not_blocked_by_the_agreed_threshold()
    {
        // af2-fix finding 2 (same case as tests/stream-af2-ctl01.mjs): the stay is already at ₹2,000, below the agreed floor ₹2,125 of ₹2,500.
        var agreed = new Paise(250_000);
        var current = new Paise(200_000);
        Assert.False(StayRateRules.NeedsOwner(agreed, 1500, current, current), "guest-count / scope-only change");
        Assert.True(StayRateRules.NeedsOwner(agreed, 1500, current, new Paise(199_999)), "a rate that moves is still judged");
        Assert.True(StayRateRules.NeedsOwner(agreed, 1500, null, current), "no current rate given = treated as moving");
        Assert.True(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, current, false, false, current));
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, current, false, false));
        Assert.True(StayRateRules.CanApprove(false, true, Tariff, 1500, agreed, 1500, current, current));
        Assert.False(StayRateRules.CanApprove(false, true, Tariff, 1500, agreed, 1500, new Paise(205_000), current), "a raise still below the agreed floor moves the rate");
        // The tariff limit (DEC-063 / PERM-RATE-001) is unchanged: below the tariff floor a guest-only change still needs an approval.
        Assert.False(StayRateRules.CanApplyDirectly(false, true, Tariff, 1500, agreed, 1500, new Paise(160_000), false, false, new Paise(160_000)));
        Assert.Equal("This rate is more than 15% below the agreed rate " + agreed + ": the Owner decides it (CTL-01).", StayRateRules.OwnerDecidesText(agreed, 1500));
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
