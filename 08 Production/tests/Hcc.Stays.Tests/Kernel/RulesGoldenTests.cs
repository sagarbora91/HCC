using System.Reflection;
using Hcc.Contracts;
using Hcc.Contracts.Housekeeping;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Housekeeping;
using Hcc.Domain.Stays;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Kernel;

/// <summary>Pure rules vs the prototype's figures and messages (no database).</summary>
public class RulesGoldenTests
{
    private static readonly StaysGolden G = StaysGolden.Load();

    private static HccErrorCode Code(Action a)
    {
        try
        {
            a();
        }
        catch (HccException e)
        {
            return e.Code;
        }

        throw new Xunit.Sdk.XunitException("Expected an HccException.");
    }

    [Fact]
    public void Room_board_status_of_every_BASE_room_matches_the_prototype()
    {
        var bd = StayDates.Parse(G.Base.BusinessDate);
        var occupied = G.Base.Bookings.Where(b => b.Status == "In house").Select(b => b.Room).ToHashSet();
        var statuses = G.Base.Rooms.ToDictionary(r => r.RoomNo, r =>
        {
            var span = r.Blocked ? new RoomBlockSpan(r.BlockFrom is null ? null : StayDates.Parse(r.BlockFrom), r.BlockTo is null ? null : StayDates.Parse(r.BlockTo)) : null;
            return RoomBoardRules.RoomStatus(span, bd, occupied.Contains(r.RoomNo), r.Hk);
        });
        Assert.Equal(G.Base.Expected.RoomStatus.OrderBy(k => k.Key), statuses.OrderBy(k => k.Key));
        Assert.Equal(G.Base.Expected.Occupied, statuses.Values.Count(s => s == RoomBoardRules.Occupied));
        Assert.Equal(G.Base.Expected.Available, statuses.Values.Count(s => s == RoomHk.Ready));
        Assert.Equal(G.Base.Expected.Sellable, statuses.Values.Count(s => s != RoomBoardRules.Blocked));
    }

    [Theory]
    [InlineData("2026-10-24", "2026-10-25", false)] // back-to-back before
    [InlineData("2026-10-27", "2026-10-29", false)] // back-to-back after
    [InlineData("2026-10-24", "2026-10-26", true)]
    [InlineData("2026-10-26", "2026-10-28", true)]
    [InlineData("2026-10-20", "2026-10-30", true)]
    public void BlockedDuring_is_half_open_like_the_prototype(string a, string d, bool expected)
    {
        var block = new RoomBlockSpan(StayDates.Parse("2026-10-25"), StayDates.Parse("2026-10-27"));
        Assert.Equal(expected, RoomBoardRules.BlockedDuring(block, StayDates.Parse(a), StayDates.Parse(d)));
        Assert.True(RoomBoardRules.BlockedDuring(new RoomBlockSpan(null, null), StayDates.Parse("2030-01-01"), StayDates.Parse("2030-01-02")), "undated block blocks everything (base 308)");
        Assert.True(RoomBoardRules.BlockedDuring(new RoomBlockSpan(StayDates.Parse("2026-10-25"), null), StayDates.Parse("2027-03-01"), StayDates.Parse("2027-03-02")), "open-ended");
        Assert.False(RoomBoardRules.BlockedDuring(null, StayDates.Parse(a), StayDates.Parse(d)));
    }

    [Fact]
    public void Task_machine_reproduces_the_prototype_journey_from_checkout_to_ready()
    {
        var steps = G.Journey.Steps.ToDictionary(s => s.Step);
        var t = new HkTaskState(HkTaskKind.Checkout, HkTaskStatus.ToDo, 0, 6, null);
        Assert.Equal("To do", steps["checkout"].TaskStatus);

        var r = HkTaskMachine.Apply(t, HkAction.Start, true);
        Assert.Equal((steps["start"].RoomHk, "In progress"), (r.RoomHk, Proto(r.Task.Status)));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Code(() => HkTaskMachine.Apply(r.Task, HkAction.Complete, true)));
        Assert.Equal(G.Journey.Expected.CompleteEarly, Code(() => HkTaskMachine.Apply(r.Task, HkAction.Complete, true)).ToString());
        t = r.Task;
        for (var i = 0; i < 3; i++) t = HkTaskMachine.Apply(t, HkAction.Check, true, index: i).Task;
        r = HkTaskMachine.Apply(t, HkAction.Complete, true);
        Assert.Equal((steps["complete"].RoomHk, steps["complete"].TaskStatus), (r.RoomHk, Proto(r.Task.Status)));
        Assert.Equal(G.Journey.Expected.RejectNoReason, Code(() => HkTaskMachine.Apply(r.Task, HkAction.Reject, true, reason: "  ")).ToString());
        r = HkTaskMachine.Apply(r.Task, HkAction.Reject, true, reason: "Mirror streaks in bathroom");
        Assert.Equal((steps["reject"].RoomHk, steps["reject"].TaskStatus), (r.RoomHk, Proto(r.Task.Status)));
        Assert.Equal(0, r.Task.ChecklistMask);
        Assert.Equal("Mirror streaks in bathroom", r.Task.RejectReason);
        t = r.Task;
        for (var i = 0; i < 3; i++) t = HkTaskMachine.Apply(t, HkAction.Check, true, index: i).Task;
        r = HkTaskMachine.Apply(t, HkAction.Complete, true);
        Assert.Equal((steps["complete-again"].RoomHk, steps["complete-again"].TaskStatus), (r.RoomHk, Proto(r.Task.Status)));
        r = HkTaskMachine.Apply(r.Task, HkAction.Inspect, true);
        Assert.Equal((steps["inspect"].RoomHk, steps["inspect"].TaskStatus), (r.RoomHk, Proto(r.Task.Status)));
    }

    [Fact]
    public void Inspection_off_completes_to_Ready_stayover_leaves_readiness_and_repair_has_no_checklist()
    {
        var full = new HkTaskState(HkTaskKind.Checkout, HkTaskStatus.InProgress, HkTaskMachine.FullChecklist, 6, null);
        var off = HkTaskMachine.Apply(full, HkAction.Complete, inspectionRequired: false);
        Assert.Equal((RoomHk.Ready, HkTaskStatus.Done), (off.RoomHk, off.Task.Status));
        var stay = HkTaskMachine.Apply(full with { Kind = HkTaskKind.Stayover }, HkAction.Complete, true);
        Assert.Equal((null, HkTaskStatus.Done), (stay.RoomHk, stay.Task.Status));
        Assert.Null(HkTaskMachine.Apply(new HkTaskState(HkTaskKind.Stayover, HkTaskStatus.ToDo, 0, 6, null), HkAction.Start, true).RoomHk);
        var repair = new HkTaskState(HkTaskKind.Repair, HkTaskStatus.InProgress, 0, 9, null);
        Assert.Equal(HccErrorCode.VALIDATION, Code(() => HkTaskMachine.Apply(repair, HkAction.Check, true, index: 0)));
        var done = HkTaskMachine.Apply(repair, HkAction.Complete, true);
        Assert.Equal((null, HkTaskStatus.Done), (done.RoomHk, done.Task.Status));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Code(() => HkTaskMachine.Apply(full, HkAction.Inspect, true)));
        Assert.Equal(HccErrorCode.VALIDATION, Code(() => HkTaskMachine.Apply(full, "dance", true)));
    }

    [Fact]
    public void Guest_validation_and_masks_follow_the_prototype()
    {
        var full = new GuestDraft("Sonal Kulkarni", "+91 98200 05555", "sonal@example.test", "5 Demo Lane, Latur (fictional)", "Indian", "Passport (demo)", "K555",
            Party: [new GuestPartyMember("Om Kulkarni", "Child", "Son")]);
        Assert.Empty(GuestRules.Validate(full, true));
        Assert.Contains("phone", GuestRules.Validate(full with { Phone = "12" }, false).Keys);
        Assert.Contains("GSTIN", GuestRules.Summary(GuestRules.Validate(full with { Gstin = "BAD", Company = "X" }, false)), StringComparison.Ordinal);
        Assert.Contains("last 4", GuestRules.Summary(GuestRules.Validate(full with { IdLast4 = "123456789012" }, false)), StringComparison.Ordinal);
        Assert.Contains("address", GuestRules.Summary(GuestRules.Validate(full with { Address = "" }, true)), StringComparison.Ordinal);
        Assert.Equal("Enter the ID number.", GuestRules.Validate(full with { IdLast4 = "" }, true)["idNumber"]); // DEC-068: number or (older profiles) last 4
        Assert.Equal("Enter the guest’s name (at least 2 characters). (1 more: Phone)", GuestRules.Summary(GuestRules.Validate(full with { Name = "X", Phone = "" }, false)));
        Assert.Equal("••••••1041", GuestRules.MaskPhone("+91 98200 01041"));
        Assert.Equal("g•••@example.test", GuestRules.MaskEmail("guest1@example.test"));
        Assert.Equal("K555", GuestRules.Clean(full with { IdLast4 = " k555 " }).IdLast4);
    }

    [Fact]
    public void Check_in_preconditions_give_the_prototype_codes()
    {
        var ok = new CheckInFacts("Confirmed", StayDates.Parse("2026-10-24"), StayDates.Parse("2026-10-26"), StayDates.Parse("2026-10-24"), true, "Ready", false, false, true);
        StayRules.AssertCanCheckIn(ok);
        Assert.Equal(G.Cases.DirtyRoomCheckin.Code, Code(() => StayRules.AssertCanCheckIn(ok with { RoomHk = G.Cases.DirtyRoomCheckin.RoomHk })).ToString());
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, Code(() => StayRules.AssertCanCheckIn(ok with { RoomBlockedDuringStay = true })));
        Assert.Equal(HccErrorCode.NOT_AVAILABLE, Code(() => StayRules.AssertCanCheckIn(ok with { RoomOccupied = true })));
        Assert.Equal(HccErrorCode.VALIDATION, Code(() => StayRules.AssertCanCheckIn(ok with { Registered = false })));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Code(() => StayRules.AssertCanCheckIn(ok with { ReservationStatus = "Held" })));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Code(() => StayRules.AssertCanCheckIn(ok with { BusinessDate = StayDates.Parse("2026-10-26") })));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, Code(() => StayRules.AssertCanCheckOut("CheckedOut")));
    }

    [Fact]
    public void Housekeeping_contracts_carry_no_money_fields_DEC_061()
    {
        string[] banned = ["paise", "rate", "amount", "balance", "folio", "price", "tariff", "receipt", "charge"];
        var types = typeof(RoomBoardRow).Assembly.GetTypes().Where(t => t.Namespace == "Hcc.Contracts.Housekeeping");
        Assert.NotEmpty(types);
        foreach (var t in types)
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.DoesNotContain(banned, b => p.Name.Contains(b, StringComparison.OrdinalIgnoreCase));
    }

    private static string Proto(string status) => status switch { HkTaskStatus.ToDo => "To do", HkTaskStatus.InProgress => "In progress", _ => status };
}
