using Hcc.Contracts;
using Hcc.Contracts.Housekeeping;
using Hcc.Domain.Dates;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>Room board, assigned scope, room blocks (out of order) and repair requests through SQL.</summary>
[Trait("Category", "Sql")]
public class HousekeepingSqlTests : IAsyncLifetime
{
    private static readonly StaysGolden G = StaysGolden.Load();
    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await P3World.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private int Room(string no) => _w.Seed.Rooms[no];

    private async Task<TransitionHousekeepingTaskResult> T(int who, long taskId, string action, string key, int? assignee = null, int? index = null, string? reason = null) =>
        (await _w.Housekeeping.TransitionAsync(_w.Ctx(who, key), new TransitionHousekeepingTaskRequest(taskId, await _w.Seed.TaskVersionAsync(taskId, Ct), action, assignee, index, reason), Ct)).Value;

    [Fact]
    public async Task Board_matches_the_prototype_room_board_for_the_BASE_fixture()
    {
        var page1 = await _w.Board.BoardAsync(_w.PropertyId, Users.Supervisor, 1, 20, Ct);
        var page2 = await _w.Board.BoardAsync(_w.PropertyId, Users.Supervisor, 2, 20, Ct);
        Assert.Equal((24, 20, 4, false), (page1.Total, page1.Rows.Count, page2.Rows.Count, page1.AssignedOnly));
        var statuses = page1.Rows.Concat(page2.Rows).ToDictionary(r => r.RoomNo, r => r.Status);
        Assert.Equal(G.Base.Expected.RoomStatus.OrderBy(k => k.Key), statuses.OrderBy(k => k.Key));
        Assert.Equal((G.Base.Expected.Occupied, G.Base.Expected.Available, G.Base.Expected.Sellable), (page1.Counts.Occupied, page1.Counts.Available, page1.Counts.Sellable));
        var r101 = page1.Rows.Single(r => r.RoomNo == "101");
        Assert.Equal(G.Base.Expected.CurrentGuest101, r101.Current!.Name);
        Assert.Equal("Ishaan Bose", page1.Rows.Single(r => r.RoomNo == "103").Next!.Name);
        Assert.Equal("Bathroom leak — plumber booked (fictional)", page2.Rows.Single(r => r.RoomNo == "308").Block!.Reason);
        Assert.Equal(G.Base.Tasks.Count, page1.Rows.Concat(page2.Rows).Sum(r => r.OpenTasks.Count));
        await Expect.ErrorAsync(HccErrorCode.VALIDATION, () => _w.Board.BoardAsync(_w.PropertyId, Users.Supervisor, 1, 101, Ct));
        await Expect.ErrorAsync(HccErrorCode.FORBIDDEN, () => _w.Board.BoardAsync(_w.PropertyId, Users.Cashier, 1, 50, Ct));
    }

    [Fact]
    public async Task Housekeeper_and_maintenance_see_only_their_assigned_rooms_and_tasks()
    {
        var mine = G.Base.Tasks.Where(t => t.Assignee == "housekeeper").Select(t => t.Room).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var hk = await _w.Board.BoardAsync(_w.PropertyId, Users.Housekeeper, 1, 50, Ct);
        Assert.True(hk.AssignedOnly);
        Assert.Equal(mine, hk.Rows.Select(r => r.RoomNo).ToList());
        Assert.All(hk.Rows.SelectMany(r => r.OpenTasks), t => Assert.Equal(Users.Housekeeper, t.AssigneeId));
        var maint = await _w.Board.OpenTasksAsync(_w.PropertyId, Users.Maint, 1, 50, Ct);
        Assert.Equal(["308"], maint.Rows.Select(t => t.RoomNo).ToList());
        Assert.Equal("repair", maint.Rows[0].Kind);

        // Task scope: housekeeper2's task cannot be started by housekeeper; maintenance cannot start a cleaning task.
        var other = _w.Seed.Tasks[G.Base.Tasks.First(t => t.Assignee == "housekeeper2" && t.Status == "To do").Id];
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => T(Users.Housekeeper, other, "start", "x1")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => T(Users.Maint, _w.Seed.Tasks["task-0"], "start", "x2")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => T(Users.Housekeeper, other, "assign", "x3", assignee: Users.Housekeeper)));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Housekeeping.TransitionAsync(_w.Ctx(Users.Supervisor, "x4"), new TransitionHousekeepingTaskRequest(987654, "AAAAAAAAAAA=", "start"), Ct)));
        var started = await T(Users.Housekeeper, _w.Seed.Tasks["task-0"], "start", "x5");
        Assert.Equal(("InProgress", "Cleaning"), (started.Task.Status, started.RoomHk));
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Housekeeping.TransitionAsync(_w.Ctx(Users.Housekeeper, "x6"), new TransitionHousekeepingTaskRequest(_w.Seed.Tasks["task-0"], "AAAAAAAAAAA=", "check", Index: 0), Ct)));
        Assert.Contains(_w.Policy.Calls, c => c.Perm == "PERM-HK-003" && c.Scope?.Kind == "HkTask");
    }

    [Fact]
    public async Task Assignment_needs_an_eligible_assignee_cleans_to_housekeepers_repairs_to_maintenance()
    {
        var repair = _w.Seed.Tasks["repair-308"];
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => T(Users.Supervisor, repair, "assign", "a1", assignee: Users.Housekeeper)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => T(Users.Supervisor, _w.Seed.Tasks["task-0"], "assign", "a2", assignee: Users.Maint)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => T(Users.Supervisor, _w.Seed.Tasks["task-0"], "assign", "a3", assignee: Users.Cashier)));
        var ok = await T(Users.Supervisor, _w.Seed.Tasks["task-0"], "assign", "a4", assignee: Users.Housekeeper2);
        Assert.Equal(Users.Housekeeper2, ok.Task.AssigneeId);
        _w.Policy.Disable(Users.Housekeeper);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => T(Users.Supervisor, _w.Seed.Tasks["task-1"], "assign", "a5", assignee: Users.Housekeeper)));
    }

    [Fact]
    public async Task Room_blocks_follow_the_prototype_cases_and_block_check_in_and_allocation()
    {
        var c = G.Cases;
        Assert.Equal(c.BlockOverlappingInHouse.Code, await Expect.CodeAsync(() => _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b1"), new BlockRoomRequest(Room("101"), "Leak", "2026-10-25", "2026-10-27"), Ct)));
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(() => _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b2"), new BlockRoomRequest(Room("103"), "Leak"), Ct)));
        var after = await _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b3"), new BlockRoomRequest(Room("101"), "Repaint after checkout", "2026-10-26", "2026-10-28"), Ct);
        Assert.Equal("NO_ERROR", c.BlockAfterDeparture.Code);
        Assert.Equal(("2026-10-26", "2026-10-28"), (after.Value.From, after.Value.To));
        Assert.Equal(c.BlockInPast.Code, await Expect.CodeAsync(() => _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b4"), new BlockRoomRequest(Room("105"), "Leak", "2026-10-20"), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b5"), new BlockRoomRequest(Room("105"), ""), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b6"), new BlockRoomRequest(Room("105"), "Leak", "2026-10-26", "2026-10-26"), Ct)));
        Assert.Equal(c.HousekeeperBlock.Code, await Expect.CodeAsync(() => _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Housekeeper, "b7"), new BlockRoomRequest(Room("105"), "Leak"), Ct)));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b8"), new BlockRoomRequest(99999, "Leak"), Ct)));

        // Dated block on 105 [26, 28): bookings outside pass, overlapping nights blocked (P2 port), back-to-back free.
        await _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b9"), new BlockRoomRequest(Room("105"), "AC compressor replacement", "2026-10-26", "2026-10-28"), Ct);
        Assert.False(await _w.Blocks.IsBlockedDuringAsync(Room("105"), StayDates.Parse("2026-10-24"), StayDates.Parse("2026-10-26"), Ct));
        Assert.False(await _w.Blocks.IsBlockedDuringAsync(Room("105"), StayDates.Parse("2026-10-28"), StayDates.Parse("2026-10-30"), Ct));
        Assert.True(await _w.Blocks.IsBlockedDuringAsync(Room("105"), StayDates.Parse("2026-10-25"), StayDates.Parse("2026-10-27"), Ct));
        Assert.Equal([Room("101"), Room("105"), Room("308")], await _w.Blocks.BlockedRoomsAsync(_w.PropertyId, StayDates.Parse("2026-10-26"), StayDates.Parse("2026-10-27"), Ct));
        Assert.Equal("Ready", (await _w.Board.BoardAsync(_w.PropertyId, Users.Supervisor, 1, 50, Ct)).Rows.Single(r => r.RoomNo == "105").Status); // not blocked tonight

        // Unblock: housekeeper forbidden; supervisor ok; second unblock INVALID_TRANSITION.
        var ver = await _w.Seed.BlockVersionAsync(Room("105"), Ct);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Housekeeping.UnblockRoomAsync(_w.Ctx(Users.Housekeeper, "u1"), new UnblockRoomRequest(Room("105"), ver), Ct)));
        await _w.Housekeeping.UnblockRoomAsync(_w.Ctx(Users.Supervisor, "u2"), new UnblockRoomRequest(Room("105"), ver), Ct);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Housekeeping.UnblockRoomAsync(_w.Ctx(Users.Supervisor, "u3"), new UnblockRoomRequest(Room("105"), ver), Ct)));
        Assert.False(await _w.Blocks.IsBlockedDuringAsync(Room("105"), StayDates.Parse("2026-10-26"), StayDates.Parse("2026-10-27"), Ct));
    }

    [Fact]
    public async Task Inspection_pass_makes_the_room_Ready_but_a_block_stays_AC_010()
    {
        var c = G.Cases.InspectionKeepsBlock;
        await _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "b"), new BlockRoomRequest(Room(c.Room), "Window seal", "2026-10-24", c.BlockTo), Ct);
        var taskId = _w.Seed.Tasks[G.Base.Tasks.Single(t => t.Status == "Inspection").Id];
        var r = await T(Users.Supervisor, taskId, "inspect", "i");
        Assert.Equal(c.HkAfter, r.RoomHk);
        var row = (await _w.Board.BoardAsync(_w.PropertyId, Users.Supervisor, 1, 50, Ct)).Rows.Single(x => x.RoomNo == c.Room);
        Assert.Equal((c.StatusAfter, c.BlockTo, c.BlockedAfter), (row.Status, row.Block?.To, row.Block is not null));
    }

    [Fact]
    public async Task Repair_request_goes_to_maintenance_and_completion_keeps_the_block()
    {
        var hkRoom = G.Base.Tasks.First(t => t.Assignee == "housekeeper" && t.Status == "To do").Room;
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Housekeeping.RequestRepairAsync(_w.Ctx(Users.Accountant, "r1"), new RequestRepairRequest(Room(hkRoom), "Tap leaking"), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Housekeeping.RequestRepairAsync(_w.Ctx(Users.Housekeeper, "r2"), new RequestRepairRequest(Room(hkRoom), ""), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Housekeeping.RequestRepairAsync(_w.Ctx(Users.Housekeeper, "r3"), new RequestRepairRequest(Room("101"), "Tap leaking"), Ct)));
        var rep = await _w.Housekeeping.RequestRepairAsync(_w.Ctx(Users.Housekeeper, "r4"), new RequestRepairRequest(Room(hkRoom), "Tap leaking"), Ct);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Housekeeping.RequestRepairAsync(_w.Ctx(Users.Reception, "r5"), new RequestRepairRequest(Room(hkRoom), "Again"), Ct)));
        Assert.Equal("Tap leaking", await _w.Db.StringAsync($"SELECT note FROM ops.hk_task WHERE task_id = {rep.Value.TaskId};", Ct));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => T(Users.Housekeeper, rep.Value.TaskId, "start", "r6")));
        await T(Users.Supervisor, rep.Value.TaskId, "assign", "r7", assignee: Users.Maint);
        await _w.Housekeeping.BlockRoomAsync(_w.Ctx(Users.Supervisor, "r8"), new BlockRoomRequest(Room(hkRoom), "Tap leaking"), Ct);
        await T(Users.Maint, rep.Value.TaskId, "start", "r9");
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => T(Users.Maint, rep.Value.TaskId, "check", "r10", index: 0)));
        var done = await T(Users.Maint, rep.Value.TaskId, "complete", "r11");
        Assert.Equal("Done", done.Task.Status);
        Assert.Equal("Dirty", done.RoomHk); // repair never changes readiness
        Assert.True(await _w.Blocks.IsBlockedDuringAsync(Room(hkRoom), StayDates.Parse("2026-10-24"), StayDates.Parse("2026-10-25"), Ct), "repair completion does not unblock");
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Housekeeping.UnblockRoomAsync(_w.Ctx(Users.Maint, "r12"), new UnblockRoomRequest(Room(hkRoom), await _w.Seed.BlockVersionAsync(Room(hkRoom), Ct)), Ct)));
    }
}
