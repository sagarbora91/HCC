using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Maintenance;
using Hcc.Maintenance.Tests.Harness;

namespace Hcc.Maintenance.Tests.Kernel;

/// <summary>The prototype's maintenance rules and figures (tests/stream-maintenance.mjs, BASE fixture) reproduced on the pure domain.</summary>
public class RulesGoldenTests
{
    private static readonly DateTime Now = GoldenFixture.T(GoldenFixture.Clock);
    private static readonly DateOnly Bd = GoldenFixture.D(GoldenFixture.BusinessDate);

    private static (List<MntOrder> Orders, List<MntAsset> Assets, List<MntPlan> Plans, List<MntSpare> Spares) Fx() => GoldenFixture.Build();

    private static MntOrder Wo(List<MntOrder> orders, string code) => orders[Array.FindIndex(GoldenFixture.Orders, o => o.Code == code)];

    [Fact]
    public void Fixture_summary_matches_the_prototype()
    {
        var (orders, _, _, spares) = Fx();
        Assert.Equal(new MntSummary(1, 1, 1, 1, 4, 2), MntRules.Summary(orders, Now));   // prototype: open 1, in progress 1, on hold 1, done 1, verified 4, breached 2
        Assert.Equal(["SP-02", "SP-03", "SP-09"], MntRules.LowSpares(spares).Select(s => GoldenFixture.Spares[s.Id - 1].Code).Order().ToArray());
        var amc3 = GoldenFixture.Amcs.Single(a => a.Code == "AMC-03");
        Assert.Equal("Expiring", MntRules.AmcStatus(GoldenFixture.D(amc3.From), GoldenFixture.D(amc3.To), Bd));
        Assert.Equal("Active", MntRules.AmcStatus(GoldenFixture.D("2026-04-01"), GoldenFixture.D("2027-03-31"), Bd));
        Assert.Equal("NotStarted", MntRules.AmcStatus(GoldenFixture.D("2026-11-01"), GoldenFixture.D("2027-10-31"), Bd));
        Assert.Equal("Expired", MntRules.AmcStatus(GoldenFixture.D("2025-01-01"), GoldenFixture.D("2025-12-31"), Bd));
    }

    [Fact]
    public void Sla_mttr_pm_compliance_ages_and_downtime_derive_from_the_data()
    {
        var (orders, _, _, _) = Fx();
        Assert.Equal("Breached", MntRules.Sla(Wo(orders, "WO-0901"), Now));   // P2 created 22 Oct 19:10, still on hold
        Assert.Equal("OnTrack", MntRules.Sla(Wo(orders, "WO-0902"), Now));    // P1 created 07:20, clock 08:00
        Assert.Equal("Met", MntRules.Sla(Wo(orders, "WO-0904"), Now));
        Assert.Equal("Met", MntRules.Sla(Wo(orders, "WO-0905"), Now));
        Assert.Equal("Breached", MntRules.Sla(Wo(orders, "WO-0908"), Now));   // done two days late
        Assert.Equal(468, MntRules.Mttr(orders));                              // mean(13 h 55, 5 h, 4 h 30) = round((835 + 300 + 270) / 3)
        Assert.Equal(new MntPmCompliance(2, 1, 50), MntRules.PmCompliance(orders, Bd));
        Assert.Equal([2, 1, 0, 0], MntRules.AgeBuckets(orders, Now).Select(b => b.Count).ToArray());
        var down = MntRules.DowntimeByRoom(orders, new HashSet<int>(), Now);
        Assert.Equal([new MntDowntimeRow(203, "203", 270, 1, false)], down);
        Assert.Equal("13 h 55 min", MntRules.Duration(835));
        Assert.Equal("1 d 1 h", MntRules.Duration(1500));
        Assert.Equal("45 min", MntRules.Duration(45));
        Assert.Null(MntRules.Mttr([]));
        Assert.Null(MntRules.PmCompliance([], Bd).Pct);
    }

    [Fact]
    public void Preventive_due_list_and_plan_advance_match_the_prototype()
    {
        var (orders, _, plans, _) = Fx();
        var due = MntRules.PmDue(plans, orders, Bd).Select(d => $"PM-0{d.Plan.Id}@{d.Due:yyyy-MM-dd}").ToArray();
        Assert.Equal(["PM-01@2026-10-24", "PM-02@2026-10-24", "PM-03@2026-10-25", "PM-04@2026-10-26"], due);
        Assert.Equal(GoldenFixture.D("2026-10-31"), MntRules.AdvanceNextDue(plans[0], Bd));
        Assert.Equal(GoldenFixture.D("2026-11-24"), MntRules.AdvanceNextDue(plans[2], Bd));
        Assert.Equal("Overdue", MntRules.PlanState(plans[0] with { NextDue = GoldenFixture.D("2026-10-20") }, orders, Bd));
        Assert.Equal("Due", MntRules.PlanState(plans[0], orders, Bd));
        Assert.Equal("Scheduled", MntRules.PlanState(plans[5], orders, Bd));
        Assert.Equal("Paused", MntRules.PlanState(plans[0] with { Active = false }, orders, Bd));
    }

    [Fact]
    public void Work_order_state_machine_follows_the_prototype_handlers()
    {
        var (orders, _, _, spares) = Fx();
        var open = Wo(orders, "WO-0903");
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => MntMachine.Start(open, Now)).Code);   // unassigned
        var assigned = MntMachine.Assign(open, 9, null);
        var (started, resumed) = MntMachine.Start(assigned, Now);
        Assert.False(resumed);
        Assert.Equal(MntStatus.InProgress, started.Status);
        var held = MntMachine.Hold(started, "Basin trap on order", 7);
        Assert.Equal(MntStatus.OnHold, held.Status);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => MntMachine.Complete(held, "Fixed it", 9, Now)).Code);
        var withPart = MntMachine.AddPart(held, spares[6], 2);   // Fuse 10 A at 4,000 paise
        Assert.Equal(8_000, withPart.PartsCostPaise);
        Assert.Null(withPart.AwaitingSpareId);   // the awaited spare is cleared when booked
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, Assert.Throws<HccException>(() => MntMachine.AddPart(held, spares[2], 1)).Code);   // capacitor: none in stock
        var (back, again) = MntMachine.Start(withPart, Now);
        Assert.True(again);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => MntMachine.Complete(back, " ", 9, Now)).Code);
        var done = MntMachine.Complete(back, "Basin trap replaced; no leak after 10 min", 9, Now);
        Assert.Equal(HccErrorCode.FORBIDDEN, Assert.Throws<HccException>(() => MntMachine.Verify(done, true, null, 9, Now)).Code);   // not the completer/assignee
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => MntMachine.Verify(done, false, "", 5, Now)).Code);
        var returned = MntMachine.Verify(done, false, "Channel 5 still blank", 5, Now);
        Assert.Equal((MntStatus.InProgress, null), (returned.Status, returned.DoneUtc));
        Assert.Equal(MntStatus.Verified, MntMachine.Verify(done, true, null, 5, Now).Status);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Assert.Throws<HccException>(() => MntMachine.Cancel(started, "Duplicate")).Code);   // in progress
        Assert.Equal(MntStatus.Cancelled, MntMachine.Cancel(open, "Duplicate of guest complaint").Status);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => MntMachine.EnsureCanBlock(Wo(orders, "WO-0902"))).Code);   // the lift is not a room
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => MntMachine.PartQty(0)).Code);
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => MntMachine.Priority("P9")).Code);
    }

    [Fact]
    public void Sorting_puts_active_work_first_p1_first_then_done_then_closed_newest_first()
    {
        var (orders, _, _, _) = Fx();
        Assert.Equal(["WO-0902", "WO-0903", "WO-0901", "WO-0904", "WO-0906", "WO-0905", "WO-0907", "WO-0908"], MntRules.Sort(orders).Select(o => o.No.Replace("WO-000", "WO-090", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void Part_cost_for_the_month_and_by_kind_sum_the_snapshots()
    {
        var (orders, assets, _, _) = Fx();
        Assert.Equal(6_000, MntRules.MonthPartsCost(orders, Bd));   // only WO-0906 (done in October) carries a completed part cost; WO-0901 is not done
        Assert.Equal(("AC", 1, 2), MntRules.ByKind(orders, assets.ToDictionary(a => a.Id)).First(k => k.Kind == "AC"));
    }
}
