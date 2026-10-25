using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Stays;

namespace Hcc.Stays.Tests.Kernel;

/// <summary>Stay-change rules (ported from the prototype MOVE handler) and the allocator night plans; no database.</summary>
public class StayChangeRulesTests
{
    private static readonly DateOnly Bd = new(2026, 10, 24);

    private static HccException Fail(Action a) => Assert.Throws<HccException>(a);

    [Fact]
    public void Extend_needs_a_later_departure_within_thirty_nights_of_the_business_date()
    {
        StayChangeRules.AssertExtend(new(2026, 10, 26), new(2026, 10, 27), Bd);
        StayChangeRules.AssertExtend(new(2026, 10, 26), Bd.AddDays(30), Bd);
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.AssertExtend(new(2026, 10, 26), new(2026, 10, 26), Bd)).Code);
        var e = Fail(() => StayChangeRules.AssertExtend(new(2026, 10, 26), Bd.AddDays(31), Bd));
        Assert.Equal("Departure must follow the active arrival date (maximum 30 nights).", e.Message); // prototype message
    }

    [Fact]
    public void Shorten_keeps_tonight_and_never_releases_posted_nights()
    {
        var arrival = new DateOnly(2026, 10, 23);
        StayChangeRules.AssertShorten(arrival, new(2026, 10, 27), new(2026, 10, 25), Bd, new DateOnly(2026, 10, 23));
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.AssertShorten(arrival, new(2026, 10, 27), Bd, Bd, null)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.AssertShorten(arrival, new(2026, 10, 27), new(2026, 10, 28), Bd, null)).Code);
        var posted = Fail(() => StayChangeRules.AssertShorten(arrival, new(2026, 10, 27), new(2026, 10, 25), Bd, new DateOnly(2026, 10, 25)));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, posted.Code);
        Assert.StartsWith("Room nights are already posted on or after the new departure date.", posted.Message); // prototype message
    }

    [Fact]
    public void Early_departure_rules()
    {
        StayChangeRules.AssertEarlyDeparture(new(2026, 10, 23), new(2026, 10, 26), Bd, new DateOnly(2026, 10, 23));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Fail(() => StayChangeRules.AssertEarlyDeparture(new(2026, 10, 23), Bd, Bd, null)).Code); // nothing to release
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.AssertEarlyDeparture(Bd, new(2026, 10, 26), Bd, null)).Code);           // arrived today
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Fail(() => StayChangeRules.AssertEarlyDeparture(new(2026, 10, 23), new(2026, 10, 26), Bd, Bd)).Code); // tonight posted
    }

    [Fact]
    public void Move_target_must_be_vacant_ready_unblocked_and_fit_the_party()
    {
        static void Check(int to = 2, string hk = "Ready", bool occupied = false, bool blocked = false, long? block = null, int toType = 1, int guests = 2, int max = 3)
            => StayChangeRules.AssertMoveTarget(1, to, 1, toType, block, guests, 0, max, hk, occupied, blocked);
        Check();
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => Check(to: 1)).Code);
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, Fail(() => Check(hk: "Dirty")).Code);
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, Fail(() => Check(occupied: true)).Code);
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, Fail(() => Check(blocked: true)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => Check(guests: 4)).Code);
        Check(block: 9, toType: 1);                                                  // group pickup: same type is fine
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => Check(block: 9, toType: 2)).Code);
    }

    [Fact]
    public void Rate_choice_and_downgrade_loss()
    {
        Assert.Equal(new RateChoice(false, null), StayChangeRules.Rate(null, null));
        Assert.Equal(new RateChoice(true, 395_000), StayChangeRules.Rate("Change", 395_000));
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.Rate("keep", 1)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.Rate("change", null)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.Rate("change", 0)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.Rate("discount", 5)).Code);
        Assert.Equal(10_000, StayChangeRules.DowngradeLoss(400_000, 395_000, 2));
        Assert.Equal(0, StayChangeRules.DowngradeLoss(400_000, 450_000, 5)); // a raise gives nothing up
        Assert.Equal(0, StayChangeRules.DowngradeLoss(400_000, 400_000, 5));
    }

    [Fact]
    public void Reason_is_trimmed_and_required_when_asked()
    {
        Assert.Equal("Noise", StayChangeRules.Reason("  Noise ", true));
        Assert.Equal(string.Empty, StayChangeRules.Reason(null, false));
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.Reason(" a ", true)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Fail(() => StayChangeRules.Reason(new string('x', 201), false)).Code);
    }

    [Fact]
    public void Night_plans_split_by_room_and_decide_add_only_versus_full_replan()
    {
        static RoomNightKey N(int room, int day) => new(room, 1, new DateOnly(2026, 10, day));
        var current = new[] { N(101, 23), N(105, 24), N(105, 25) };
        var runs = NightPlan.Runs(current);
        Assert.Equal([new NightRun(101, 1, new(2026, 10, 23), new(2026, 10, 24)), new NightRun(105, 1, new(2026, 10, 24), new(2026, 10, 26))], runs);
        Assert.Equal(current, NightPlan.Nights(runs));

        var extended = current.Append(N(105, 26)).ToList();
        Assert.Empty(NightPlan.Removed(current, extended));
        Assert.Equal([N(105, 26)], NightPlan.Added(current, extended));

        var shortened = current.Where(n => n.Night < new DateOnly(2026, 10, 25)).ToList();
        Assert.Equal([N(105, 25)], NightPlan.Removed(current, shortened));
        Assert.Empty(NightPlan.Added(current, shortened));

        var moved = current.Where(n => n.Night < new DateOnly(2026, 10, 25)).Concat([N(107, 25)]).ToList();
        Assert.Equal([N(105, 25)], NightPlan.Removed(current, moved));
        Assert.Equal([N(107, 25)], NightPlan.Added(current, moved));
    }
}
