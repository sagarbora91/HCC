using Hcc.Contracts.Maintenance;
using Hcc.Maintenance.Tests.Harness;

namespace Hcc.Maintenance.Tests.Sql;

/// <summary>Maintenance commands and queries against SQL Server with the REAL access policy (RBAC seed roles) and the real room-block port.</summary>
[Trait("Category", "Sql")]
public class MaintenanceSqlTests : IAsyncLifetime
{
    private MntWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await MntWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<WorkOrderResult> Do(Func<Task<Hcc.Application.Commands.CommandResult<WorkOrderResult>>> call) => (await call()).Value;

    private async Task<WorkOrderResult> Start(string who, string code, string key) =>
        (await _w.Commands.StartAsync(_w.Ctx(who, key), new StartWorkOrderRequest(_w.Orders[code], await _w.VersionAsync(code, Ct)), Ct)).Value;

    private async Task<WorkOrderResult> Complete(string who, string code, string key, string note = "Work done and checked") =>
        (await _w.Commands.CompleteAsync(_w.Ctx(who, key), new CompleteWorkOrderRequest(_w.Orders[code], await _w.VersionAsync(code, Ct), note), Ct)).Value;

    private async Task<WorkOrderResult> Verify(string who, string code, string key, bool approve = true, string? reason = null, bool unblock = false) =>
        (await _w.Commands.VerifyAsync(_w.Ctx(who, key), new VerifyWorkOrderRequest(_w.Orders[code], await _w.VersionAsync(code, Ct), approve, reason, unblock), Ct)).Value;

    private async Task<WorkOrderResult> Assign(string who, string code, string key, int? tech = null, int? vendor = null) =>
        (await _w.Commands.AssignAsync(_w.Ctx(who, key), new AssignWorkOrderRequest(_w.Orders[code], await _w.VersionAsync(code, Ct), tech, vendor), Ct)).Value;

    [Fact]
    public async Task Report_reproduces_the_prototype_fixture_figures()
    {
        var r = await _w.Queries.ReportAsync(_w.PropertyId, _w.Users["owner"], Ct);
        Assert.Equal(new MaintenanceSummaryView(1, 1, 1, 1, 4, 2), r.Summary);
        Assert.Equal(468, r.MttrMinutes);
        Assert.Equal("7 h 48 min", r.MttrDuration);
        Assert.Equal((2, 1, 50), (r.PmDue, r.PmOnTime, r.PmCompliancePct));
        Assert.Equal([2, 1, 0, 0], r.AgeBuckets.Select(b => b.Count).ToArray());
        var down = Assert.Single(r.DowntimeByRoom);
        Assert.Equal(("203", 270, false), (down.RoomNo, down.Minutes, down.Running));
        Assert.Equal(1, r.PendingRepairs);   // the repair task for room 308 has no work order yet
        Assert.Equal([_w.Spares["SP-02"], _w.Spares["SP-03"], _w.Spares["SP-09"]], r.LowSpareIds.Order().ToArray());
        Assert.Equal(6_000, r.MonthPartsCostPaise);   // Owner holds finance rights
        var amcs = await _w.Queries.AmcsAsync(_w.PropertyId, _w.Users["owner"], Ct);
        Assert.Equal("Expiring", amcs.Amcs.Single(a => a.AmcId == _w.Amcs["AMC-03"]).Status);
        Assert.Equal(9_600_000, amcs.Amcs.Single(a => a.AmcId == _w.Amcs["AMC-03"]).ValuePaise);
    }

    [Fact]
    public async Task Money_is_hidden_from_maintenance_supervisor_reception_and_cashier_but_not_the_owner_or_manager()
    {
        var p = _w.PropertyId;
        // AMC contract values: Owner/Manager only (G12). Supervisor sees the contracts without values; Reception, Cashier, Maintenance are refused outright.
        Assert.NotNull((await _w.Queries.AmcsAsync(p, _w.Users["manager"], Ct)).Amcs[0].ValuePaise);
        var sup = await _w.Queries.AmcsAsync(p, _w.Users["supervisor"], Ct);
        Assert.True(sup.ValuesHidden);
        Assert.All(sup.Amcs, a => Assert.Null(a.ValuePaise));
        foreach (var who in new[] { "reception", "cashier", "maint", "housekeeper" })
            Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.AmcsAsync(p, _w.Users[who], Ct)));

        // Part prices on work orders: WO-0901 carries 1 × ₹1,800 gas. Only finance readers receive the unit cost.
        var ownerPage = await _w.Queries.ListWorkOrdersAsync(p, _w.Users["owner"], 1, 50, ct: Ct);
        Assert.Equal(180_000, ownerPage.Rows.Single(w => w.WorkOrderId == _w.Orders["WO-0901"]).Parts.Single().UnitCostPaise);
        Assert.False(ownerPage.MoneyHidden);
        var supPage = await _w.Queries.ListWorkOrdersAsync(p, _w.Users["supervisor"], 1, 50, ct: Ct);
        Assert.Equal(8, supPage.Total);
        Assert.True(supPage.MoneyHidden);
        Assert.All(supPage.Rows.SelectMany(w => w.Parts), part => Assert.Null(part.UnitCostPaise));
        Assert.All(supPage.Rows, w => Assert.Null(w.PartsCostPaise));
        var techPage = await _w.Queries.ListWorkOrdersAsync(p, _w.Users["maint"], 1, 50, ct: Ct);
        Assert.True(techPage.OwnOnly);
        Assert.All(techPage.Rows.SelectMany(w => w.Parts), part => Assert.Null(part.UnitCostPaise));
        var detail = await _w.Queries.GetWorkOrderAsync(p, _w.Users["maint"], _w.Orders["WO-0901"], Ct);
        Assert.Null(detail.Order.PartsCostPaise);
        var registers = await _w.Queries.RegistersAsync(p, _w.Users["maint"], Ct);
        Assert.True(registers.MoneyHidden);
        Assert.All(registers.Spares, s => Assert.Null(s.CostPaise));
        Assert.Empty(registers.Vendors);   // vendor phone book and PM plans are supervisor-level
        Assert.NotNull((await _w.Queries.RegistersAsync(p, _w.Users["owner"], Ct)).Spares[0].CostPaise);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.ReportAsync(p, _w.Users["maint"], Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.ListWorkOrdersAsync(p, _w.Users["reception"], 1, 50, ct: Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Queries.ListWorkOrdersAsync(p, _w.Users["cashier"], 1, 50, ct: Ct)));
    }

    [Fact]
    public async Task Visibility_follows_the_prototype_technician_own_housekeeper_active_orders_on_their_rooms()
    {
        var p = _w.PropertyId;
        var tech = await _w.Queries.ListWorkOrdersAsync(p, _w.Users["maint"], 1, 50, ct: Ct);
        Assert.All(tech.Rows, w => Assert.Equal(_w.Users["maint"], w.AssigneeId));
        Assert.DoesNotContain(tech.Rows, w => w.WorkOrderId == _w.Orders["WO-0903"]);   // unassigned
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Queries.GetWorkOrderAsync(p, _w.Users["maint"], _w.Orders["WO-0903"], Ct)));
        var hk = await _w.Queries.ListWorkOrdersAsync(p, _w.Users["housekeeper"], 1, 50, ct: Ct);
        Assert.Equal([_w.Orders["WO-0903"]], hk.Rows.Select(w => w.WorkOrderId).ToArray());   // open work on room 106 where the housekeeper has a task
        var other = await _w.Queries.ListWorkOrdersAsync(p, _w.Users["maint2"], 1, 50, ct: Ct);
        Assert.Empty(other.Rows);
        var openOnly = await _w.Queries.ListWorkOrdersAsync(p, _w.Users["manager"], 1, 50, "Open", "P3", Ct);
        Assert.Equal([_w.Orders["WO-0903"]], openOnly.Rows.Select(w => w.WorkOrderId).ToArray());
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Queries.ListWorkOrdersAsync(p, _w.Users["manager"], 1, 50, "Nope", null, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Queries.ListWorkOrdersAsync(p, _w.Users["manager"], 0, 50, ct: Ct)));
    }

    [Fact]
    public async Task Repair_request_to_work_order_through_hold_part_complete_and_verify_with_unblock()
    {
        var supervisor = _w.Users["supervisor"];
        var made = (await _w.Commands.ConvertRepairAsync(_w.Ctx("supervisor", "c1"), new ConvertRepairRequest(_w.RepairTaskId, "P2"), Ct)).Value;
        var id = made.WorkOrderId;
        Assert.Equal("Open", made.Status);
        Assert.Equal(_w.Users["maint"], Convert.ToInt32(await _w.ScalarAsync($"SELECT assignee_id FROM mnt.work_order WHERE wo_id = {id};", Ct)));
        Assert.NotNull(await _w.ScalarAsync($"SELECT downtime_from_utc FROM mnt.work_order WHERE wo_id = {id} AND downtime_from_utc IS NOT NULL;", Ct));   // 308 is blocked → downtime runs
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => _w.Commands.ConvertRepairAsync(_w.Ctx("supervisor", "c2"), new ConvertRepairRequest(_w.RepairTaskId), Ct)));
        Assert.Equal(0, (await _w.Queries.ReportAsync(_w.PropertyId, supervisor, Ct)).PendingRepairs);

        var staleToken = await _w.VersionAsync(id, Ct);
        var started = (await _w.Commands.StartAsync(_w.Ctx("maint", "s1"), new StartWorkOrderRequest(id, staleToken), Ct)).Value;
        Assert.Equal("InProgress", started.Status);
        Assert.Equal("InProgress", await _w.StringAsync($"SELECT status FROM ops.hk_task WHERE task_id = {_w.RepairTaskId};", Ct));   // the repair task follows
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.HoldAsync(_w.Ctx("maint", "h0"), new HoldWorkOrderRequest(id, staleToken, "Basin trap on order"), Ct)));

        await _w.Commands.HoldAsync(_w.Ctx("maint", "h1"), new HoldWorkOrderRequest(id, await _w.VersionAsync(id, Ct), "Basin trap on order", _w.Spares["SP-07"]), Ct);
        var stock = Convert.ToInt32(await _w.ScalarAsync($"SELECT stock FROM mnt.spare WHERE spare_id = {_w.Spares["SP-07"]};", Ct));
        await _w.Commands.AddPartAsync(_w.Ctx("maint", "p1"), new AddWorkOrderPartRequest(id, await _w.VersionAsync(id, Ct), _w.Spares["SP-07"], 2), Ct);
        Assert.Equal(stock - 2, Convert.ToInt32(await _w.ScalarAsync($"SELECT stock FROM mnt.spare WHERE spare_id = {_w.Spares["SP-07"]};", Ct)));
        Assert.Equal(8_000, Convert.ToInt64(await _w.ScalarAsync($"SELECT SUM(qty * unit_cost_paise) FROM mnt.work_order_part WHERE wo_id = {id};", Ct)));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM mnt.work_order WHERE wo_id = {id} AND awaiting_spare_id IS NOT NULL;", Ct));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(() => Complete("maint", "WO-0901", "x")));   // sanity: a different order, on hold
        await _w.Commands.StartAsync(_w.Ctx("maint", "s2"), new StartWorkOrderRequest(id, await _w.VersionAsync(id, Ct)), Ct);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(async () => await _w.Commands.CompleteAsync(_w.Ctx("maint", "d0"), new CompleteWorkOrderRequest(id, await _w.VersionAsync(id, Ct), " "), Ct)));
        await _w.Commands.CompleteAsync(_w.Ctx("maint", "d1"), new CompleteWorkOrderRequest(id, await _w.VersionAsync(id, Ct), "Basin trap replaced; no leak after 10 min"), Ct);
        Assert.Equal("Done", await _w.StringAsync($"SELECT status FROM ops.hk_task WHERE task_id = {_w.RepairTaskId};", Ct));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM ops.room_block WHERE room_id = {_w.Rooms["308"]} AND released_utc IS NULL;", Ct));   // completion never unblocks

        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Verify("maint", "WO-0901", "v0")));   // technicians cannot verify
        var verifyId = id;
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.VerifyAsync(_w.Ctx("maint", "v1"), new VerifyWorkOrderRequest(verifyId, await _w.VersionAsync(verifyId, Ct), true), Ct)));
        var verified = (await _w.Commands.VerifyAsync(_w.Ctx("supervisor", "v2"), new VerifyWorkOrderRequest(id, await _w.VersionAsync(id, Ct), true, null, true), Ct)).Value;
        Assert.Equal("Verified", verified.Status);
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM ops.room_block WHERE room_id = {_w.Rooms["308"]} AND released_utc IS NULL;", Ct));
        Assert.NotNull(await _w.ScalarAsync($"SELECT downtime_to_utc FROM mnt.work_order WHERE wo_id = {id} AND downtime_to_utc IS NOT NULL;", Ct));
        Assert.True(await _w.CountAsync($"SELECT COUNT(*) FROM mnt.work_order_history WHERE wo_id = {id};", Ct) >= 7);
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(async () => await _w.Commands.VerifyAsync(_w.Ctx("owner", "v3"), new VerifyWorkOrderRequest(id, await _w.VersionAsync(id, Ct), true), Ct)));
    }

    [Fact]
    public async Task Verification_returns_with_a_reason_and_the_completer_never_verifies_their_own_work()
    {
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => Verify("supervisor", "WO-0904", "r0", approve: false)));
        var back = await Verify("supervisor", "WO-0904", "r1", approve: false, reason: "Channel 5 still blank");
        Assert.Equal("InProgress", back.Status);
        await Complete("manager", "WO-0904", "r2", "Retuned channels");   // a manager steps in and completes
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Verify("manager", "WO-0904", "r3")));
        Assert.Equal("Verified", (await Verify("owner", "WO-0904", "r4")).Status);
        // The faulty asset returns to Working once no other active order is open on it.
        Assert.Equal("Working", await _w.StringAsync($"SELECT status FROM mnt.asset WHERE asset_id = {_w.Assets["TV-103"]};", Ct));
    }

    [Fact]
    public async Task Technician_scope_cannot_assign_verify_block_cancel_generate_or_act_on_others_work()
    {
        var u = _w.Users;
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Start("maint", "WO-0903", "t1")));   // unassigned: not theirs
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Start("maint2", "WO-0902", "t2")));  // another technician's
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Assign("maint", "WO-0903", "t3", tech: u["maint"])));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Start("reception", "WO-0902", "t4")));   // Reception holds ROOM-001 property-wide but no module access
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => Start("housekeeper", "WO-0902", "t5")));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.BlockRoomAsync(_w.Ctx("maint", "t6"), new BlockWorkOrderRoomRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct)), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.CancelAsync(_w.Ctx("maint", "t7"), new CancelWorkOrderRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), "Not needed"), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.GeneratePmAsync(_w.Ctx("maint", "t8"), new GeneratePmWorkOrdersRequest(), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.SaveAssetAsync(_w.Ctx("maint", "t9"), new SaveAssetRequest(null, null, "X1", "AC", null, "Roof", null, null, null, null), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.CreateAsync(_w.Ctx("housekeeper", "t10"), new CreateWorkOrderRequest("Tap leak", "P3", _w.Rooms["105"]), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.CreateAsync(_w.Ctx("reception", "t11"), new CreateWorkOrderRequest("Tap leak", "P3", _w.Rooms["105"]), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(async () => await _w.Commands.CreateAsync(_w.Ctx("cashier", "t12"), new CreateWorkOrderRequest("Tap leak", "P3", _w.Rooms["105"]), Ct)));
        // A technician reports a fault: always self-assigned, even when another assignee is named.
        var created = (await _w.Commands.CreateAsync(_w.Ctx("maint", "t13"), new CreateWorkOrderRequest("Corridor light flickering", "P4", null, null, "2nd floor corridor", null, u["maint2"]), Ct)).Value;
        Assert.Equal(u["maint"], Convert.ToInt32(await _w.ScalarAsync($"SELECT assignee_id FROM mnt.work_order WHERE wo_id = {created.WorkOrderId};", Ct)));
        Assert.Equal("Open", created.Status);
        // The supervisor chooses; only an enabled maintenance user is a valid technician.
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => Assign("supervisor", "WO-0903", "t14", tech: u["housekeeper"])));
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => Assign("supervisor", "WO-0903", "t15", vendor: 9999)));
        await Assign("supervisor", "WO-0903", "t16", vendor: _w.Vendors["VEN-04"]);
        await Assign("supervisor", "WO-0903", "t17", tech: u["maint"]);
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM mnt.work_order WHERE wo_id = {_w.Orders["WO-0903"]} AND vendor_id IS NOT NULL;", Ct));
        Assert.Equal("InProgress", (await Start("maint", "WO-0903", "t18")).Status);
        // Asset marks: creating a fault on a working asset turns it Faulty.
        var onAc = (await _w.Commands.CreateAsync(_w.Ctx("supervisor", "t19"), new CreateWorkOrderRequest("AC remote dead", "P3", null, _w.Assets["AC-101"]), Ct)).Value;
        Assert.Equal("Faulty", await _w.StringAsync($"SELECT status FROM mnt.asset WHERE asset_id = {_w.Assets["AC-101"]};", Ct));
        Assert.Equal(_w.Rooms["101"], Convert.ToInt32(await _w.ScalarAsync($"SELECT room_id FROM mnt.work_order WHERE wo_id = {onAc.WorkOrderId};", Ct)));   // the asset defaults the room
    }

    [Fact]
    public async Task Room_downtime_uses_the_room_block_port_refuses_overlaps_links_existing_blocks_and_unblocks_on_verify()
    {
        // Room 103 has a reservation tonight: the port refuses and nothing is written.
        var busy = (await _w.Commands.CreateAsync(_w.Ctx("supervisor", "b0"), new CreateWorkOrderRequest("Ceiling seepage", "P2", _w.Rooms["103"]), Ct)).Value;
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(async () => await _w.Commands.BlockRoomAsync(_w.Ctx("supervisor", "b1"), new BlockWorkOrderRoomRequest(busy.WorkOrderId, await _w.VersionAsync(busy.WorkOrderId, Ct)), Ct)));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM ops.room_block WHERE room_id = {_w.Rooms["103"]};", Ct));

        // Room 106 (WO-0903): block until 26 Oct → ops.room_block (source maintenance) + inv.room_block (source Maintenance, ref = block id).
        var lift = Expect.CodeAsync(async () => await _w.Commands.BlockRoomAsync(_w.Ctx("supervisor", "b2"), new BlockWorkOrderRoomRequest(_w.Orders["WO-0902"], await _w.VersionAsync("WO-0902", Ct)), Ct));
        Assert.Equal("VALIDATION", await lift);   // the lift is not a guest room
        Assert.Equal("VALIDATION", await Expect.CodeAsync(async () => await _w.Commands.BlockRoomAsync(_w.Ctx("supervisor", "b3"), new BlockWorkOrderRoomRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), "2026-10-24"), Ct)));   // must end after today
        var blocked = (await _w.Commands.BlockRoomAsync(_w.Ctx("supervisor", "b4"), new BlockWorkOrderRoomRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), "2026-10-26"), Ct)).Value;
        Assert.False(blocked.LinkedExisting);
        Assert.Equal(("maintenance", "2026-10-24", "2026-10-26"), (await _w.StringAsync($"SELECT source FROM ops.room_block WHERE block_id = {blocked.BlockId};", Ct)!,
            Convert.ToDateTime(await _w.ScalarAsync($"SELECT from_date FROM ops.room_block WHERE block_id = {blocked.BlockId};", Ct)).ToString("yyyy-MM-dd"),
            Convert.ToDateTime(await _w.ScalarAsync($"SELECT to_date FROM ops.room_block WHERE block_id = {blocked.BlockId};", Ct)).ToString("yyyy-MM-dd")));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM inv.room_block WHERE source = 'Maintenance' AND source_ref = {blocked.BlockId} AND active = 1;", Ct));
        Assert.Contains("WO-", await _w.StringAsync($"SELECT reason FROM ops.room_block WHERE block_id = {blocked.BlockId};", Ct), StringComparison.Ordinal);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'MaintenanceRoomBlocked';", Ct));
        Assert.True((await _w.Queries.ReportAsync(_w.PropertyId, _w.Users["manager"], Ct)).DowntimeByRoom.Single(d => d.RoomNo == "106").Running);

        // Work it through: assign → start → complete by the technician, verify with unblock by the supervisor.
        await Assign("supervisor", "WO-0903", "b5", tech: _w.Users["maint"]);
        await Start("maint", "WO-0903", "b6");
        await Complete("maint", "WO-0903", "b7", "Thermostat replaced");
        Assert.Equal("Faulty", await _w.StringAsync($"SELECT status FROM mnt.asset WHERE asset_id = {_w.Assets["GY-106"]};", Ct));   // still faulty until verified
        await Verify("supervisor", "WO-0903", "b8", unblock: true);
        Assert.Equal("Working", await _w.StringAsync($"SELECT status FROM mnt.asset WHERE asset_id = {_w.Assets["GY-106"]};", Ct));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM inv.room_block WHERE source_ref = {blocked.BlockId} AND active = 1;", Ct));
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM ops.room_block WHERE room_id = {_w.Rooms["106"]} AND released_utc IS NULL;", Ct));

        // Room 308 is already blocked by housekeeping: the work order links the existing block instead of stacking another.
        var repair = (await _w.Commands.ConvertRepairAsync(_w.Ctx("supervisor", "b9"), new ConvertRepairRequest(_w.RepairTaskId), Ct)).Value;
        var linked = (await _w.Commands.BlockRoomAsync(_w.Ctx("supervisor", "b10"), new BlockWorkOrderRoomRequest(repair.WorkOrderId, await _w.VersionAsync(repair.WorkOrderId, Ct)), Ct)).Value;
        Assert.True(linked.LinkedExisting);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM ops.room_block WHERE room_id = {_w.Rooms["308"]};", Ct));
    }

    [Fact]
    public async Task Preventive_generation_follows_the_prototype_and_is_repeat_safe()
    {
        var made = (await _w.Commands.GeneratePmAsync(_w.Ctx("supervisor", "g1"), new GeneratePmWorkOrdersRequest(), Ct)).Value;
        Assert.Equal((4, "2026-10-27"), (made.Generated, made.HorizonDate));
        var rows = new List<(string Title, string Prio, string Status, string? Vendor, string? Assignee, string Due)>();
        await using (var conn = await _w.Platform.Connections.OpenAsync(Ct))
        await using (var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT p.plan_id, w.priority, w.status, w.vendor_id, w.assignee_id, CONVERT(varchar(10), w.due_date, 23) FROM mnt.work_order w JOIN mnt.pm_plan p ON p.plan_id = w.plan_id WHERE w.wo_id IN ({string.Join(',', made.WorkOrderIds)}) ORDER BY p.plan_id;", conn))
        await using (var rd = await cmd.ExecuteReaderAsync(Ct))
            while (await rd.ReadAsync(Ct))
                rows.Add(("PM-" + rd.GetInt32(0).ToString("D2", System.Globalization.CultureInfo.InvariantCulture), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetInt32(3).ToString(System.Globalization.CultureInfo.InvariantCulture), rd.IsDBNull(4) ? null : rd.GetInt32(4).ToString(System.Globalization.CultureInfo.InvariantCulture), rd.GetString(5)));
        Assert.Equal(["PM-01@2026-10-24", "PM-02@2026-10-24", "PM-03@2026-10-25", "PM-04@2026-10-26"], rows.Select(r => r.Title + "@" + r.Due).ToArray());
        Assert.All(rows, r => Assert.Equal(("P3", "Open"), (r.Prio, r.Status)));
        Assert.Equal(_w.Vendors["VEN-03"].ToString(System.Globalization.CultureInfo.InvariantCulture), rows[0].Vendor);   // generator → its vendor
        Assert.Equal(_w.Users["maint"].ToString(System.Globalization.CultureInfo.InvariantCulture), rows[2].Assignee);    // AC → first enabled technician
        Assert.Equal("2026-10-31", Convert.ToDateTime(await _w.ScalarAsync($"SELECT next_due FROM mnt.pm_plan WHERE plan_id = {_w.Plans["PM-01"]};", Ct)).ToString("yyyy-MM-dd"));
        var again = (await _w.Commands.GeneratePmAsync(_w.Ctx("supervisor", "g2"), new GeneratePmWorkOrdersRequest(), Ct)).Value;
        Assert.Equal(0, again.Generated);
        var report = await _w.Queries.ReportAsync(_w.PropertyId, _w.Users["manager"], Ct);
        Assert.Equal((4, 1, 25), (report.PmDue, report.PmOnTime, report.PmCompliancePct));   // prototype: {due 4, onTime 1, pct 25}

        // Complete the fire-pump occurrence on time and verify: compliance 2 of 4, plan last-done follows.
        var fp = made.WorkOrderIds[1];
        await _w.Commands.StartAsync(_w.Ctx("maint", "g3"), new StartWorkOrderRequest(fp, await _w.VersionAsync(fp, Ct)), Ct);
        await _w.Commands.CompleteAsync(_w.Ctx("maint", "g4"), new CompleteWorkOrderRequest(fp, await _w.VersionAsync(fp, Ct), "Pump ran 10 min; pressure ok"), Ct);
        Assert.Equal(2, (await _w.Queries.ReportAsync(_w.PropertyId, _w.Users["manager"], Ct)).PmOnTime);
        await _w.Commands.VerifyAsync(_w.Ctx("supervisor", "g5"), new VerifyWorkOrderRequest(fp, await _w.VersionAsync(fp, Ct), true), Ct);
        Assert.Equal("2026-10-24", Convert.ToDateTime(await _w.ScalarAsync($"SELECT last_done FROM mnt.pm_plan WHERE plan_id = {_w.Plans["PM-02"]};", Ct)).ToString("yyyy-MM-dd"));
        // The unique (plan, due date) index is the backstop under a race.
        await Assert.ThrowsAnyAsync<Exception>(() => _w.ExecAsync($"INSERT mnt.work_order(property_id, kind, title, priority, status, area, created_utc, created_by, plan_id, due_date) VALUES ({_w.PropertyId}, 'Preventive', N'dup', 'P3', 'Open', N'x', SYSUTCDATETIME(), 1, {_w.Plans["PM-01"]}, '2026-10-24');", Ct));
    }

    [Fact]
    public async Task Parts_stock_rules_cancel_and_registers()
    {
        // Out of stock → NOT_AVAILABLE; quantity bounds → VALIDATION; booking needs an open work order.
        await Assign("supervisor", "WO-0903", "r1", tech: _w.Users["maint"]);
        await Start("maint", "WO-0903", "r2");
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(async () => await _w.Commands.AddPartAsync(_w.Ctx("maint", "r3"), new AddWorkOrderPartRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), _w.Spares["SP-03"], 1), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(async () => await _w.Commands.AddPartAsync(_w.Ctx("maint", "r4"), new AddWorkOrderPartRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), _w.Spares["SP-04"], 0), Ct)));
        await _w.Commands.AddPartAsync(_w.Ctx("maint", "r5"), new AddWorkOrderPartRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), _w.Spares["SP-04"], 1), Ct);
        await _w.Commands.AddPartAsync(_w.Ctx("maint", "r6"), new AddWorkOrderPartRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), _w.Spares["SP-04"], 1), Ct);
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM mnt.work_order_part WHERE wo_id = {_w.Orders["WO-0903"]};", Ct));   // same spare and cost → one line
        Assert.Equal(1, Convert.ToInt32(await _w.ScalarAsync($"SELECT stock FROM mnt.spare WHERE spare_id = {_w.Spares["SP-04"]};", Ct)));
        Assert.Equal("INVALID_TRANSITION", await Expect.CodeAsync(async () => await _w.Commands.CancelAsync(_w.Ctx("supervisor", "r7"), new CancelWorkOrderRequest(_w.Orders["WO-0903"], await _w.VersionAsync("WO-0903", Ct), "Duplicate of guest complaint"), Ct)));   // in progress
        Assert.Equal("Cancelled", (await _w.Commands.CancelAsync(_w.Ctx("supervisor", "r8"), new CancelWorkOrderRequest(_w.Orders["WO-0901"], await _w.VersionAsync("WO-0901", Ct), "Replaced by new unit"), Ct)).Value.Status);

        // Receive spare: version check; quantity bounds.
        var v = await _w.SpareVersionAsync("SP-03", Ct);
        var got = (await _w.Commands.ReceiveSpareAsync(_w.Ctx("supervisor", "r9"), new ReceiveSpareRequest(_w.Spares["SP-03"], v, 4), Ct)).Value;
        Assert.Equal(4, got.Stock);
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.ReceiveSpareAsync(_w.Ctx("supervisor", "r10"), new ReceiveSpareRequest(_w.Spares["SP-03"], v, 1), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.ReceiveSpareAsync(_w.Ctx("supervisor", "r11"), new ReceiveSpareRequest(_w.Spares["SP-03"], got.Version, 1000), Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.ReceiveSpareAsync(_w.Ctx("maint", "r12"), new ReceiveSpareRequest(_w.Spares["SP-03"], got.Version, 1), Ct)));

        // Registers: asset, plan, vendor with validation.
        var asset = (await _w.Commands.SaveAssetAsync(_w.Ctx("supervisor", "a1"), new SaveAssetRequest(null, null, "Split AC 1.5 T · 107", "AC", _w.Rooms["107"], null, "Himsheetal", "HS-18", "2026-10-01", _w.Vendors["VEN-01"]), Ct)).Value;
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SaveAssetAsync(_w.Ctx("supervisor", "a2"), new SaveAssetRequest(null, null, "X1", "Rocket", null, "Roof", null, null, null, null), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SaveAssetAsync(_w.Ctx("supervisor", "a3"), new SaveAssetRequest(null, null, "X1", "AC", null, null, null, null, null, null), Ct)));
        Assert.Equal("STALE_VERSION", await Expect.CodeAsync(() => _w.Commands.SaveAssetAsync(_w.Ctx("supervisor", "a4"), new SaveAssetRequest(asset.Id, "AAAAAAAAAAA=", "Split AC 107", "AC", _w.Rooms["107"], null, null, null, null, null), Ct)));
        var plan = (await _w.Commands.SavePlanAsync(_w.Ctx("supervisor", "a5"), new SavePmPlanRequest(null, null, asset.Id, "AC filter cleaning", 30, "2026-11-01"), Ct)).Value;
        Assert.True(plan.Id > 0);
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SavePlanAsync(_w.Ctx("supervisor", "a6"), new SavePmPlanRequest(null, null, asset.Id, "AC filter", 0, "2026-11-01"), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SaveVendorAsync(_w.Ctx("supervisor", "a7"), new SaveVendorRequest(null, null, "Prakash Electricals", "Electrical", "abc"), Ct)));
        var vendor = (await _w.Commands.SaveVendorAsync(_w.Ctx("supervisor", "a8"), new SaveVendorRequest(null, null, "Prakash Electricals", "Electrical", "+91 90000 11299"), Ct)).Value;

        // AMC: Owner/Manager only; the contract value never appears in the audit trail.
        var amcReq = new SaveAmcRequest(null, null, vendor.Id, "Wiring check", "2026-11-01", "2027-10-31", 2, 1_500_000);
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SaveAmcAsync(_w.Ctx("supervisor", "m1"), amcReq, Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SaveAmcAsync(_w.Ctx("reception", "m2"), amcReq, Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SaveAmcAsync(_w.Ctx("cashier", "m3"), amcReq, Ct)));
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SaveAmcAsync(_w.Ctx("maint", "m4"), amcReq, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.SaveAmcAsync(_w.Ctx("manager", "m5"), amcReq with { To = "2026-10-31" }, Ct)));
        var amc = (await _w.Commands.SaveAmcAsync(_w.Ctx("manager", "m6"), amcReq, Ct)).Value;
        Assert.True(amc.Id > 0);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE '%1500000%' OR detail LIKE '%15,000%';", Ct));
        Assert.Equal("NotStarted", (await _w.Queries.AmcsAsync(_w.PropertyId, _w.Users["owner"], Ct)).Amcs.Single(a => a.AmcId == amc.Id).Status);
    }

    [Fact]
    public async Task Idempotency_key_replays_the_stored_result_and_a_different_payload_is_a_mismatch()
    {
        var req = new CreateWorkOrderRequest("Door closer loose", "P4", _w.Rooms["105"]);
        var first = await _w.Commands.CreateAsync(_w.Ctx("supervisor", "idem-1"), req, Ct);
        var second = await _w.Commands.CreateAsync(_w.Ctx("supervisor", "idem-1"), req, Ct);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Value.WorkOrderId, second.Value.WorkOrderId);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM mnt.work_order WHERE title = N'Door closer loose';", Ct));
        Assert.Equal("IDEMPOTENCY_MISMATCH", await Expect.CodeAsync(() => _w.Commands.CreateAsync(_w.Ctx("supervisor", "idem-1"), req with { Title = "Another fault" }, Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.CreateAsync(_w.Ctx("supervisor", "idem-2"), new CreateWorkOrderRequest("ab", "P3", _w.Rooms["105"]), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.CreateAsync(_w.Ctx("supervisor", "idem-3"), new CreateWorkOrderRequest("Tap leak", "P9", _w.Rooms["105"]), Ct)));
        Assert.Equal("VALIDATION", await Expect.CodeAsync(() => _w.Commands.CreateAsync(_w.Ctx("supervisor", "idem-4"), new CreateWorkOrderRequest("Tap leak", "P3"), Ct)));   // no room, area or asset
        Assert.Equal("NOT_FOUND", await Expect.CodeAsync(() => _w.Commands.CreateAsync(_w.Ctx("supervisor", "idem-5"), new CreateWorkOrderRequest("Tap leak", "P3", 987654), Ct)));
        // Every command wrote an audit row; the outbox carries the creation event without money.
        Assert.True(await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MNT_WO';", Ct) >= 1);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'MaintenanceWorkOrderCreated';", Ct));
    }
}

[Trait("Category", "Sql")]
public class PmLeadDaysSettingTests : IAsyncLifetime
{
    private MntWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await MntWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    [Fact]
    public async Task Generation_horizon_follows_mnt_pmLeadDays_from_its_effective_date()
    {
        // Business date 2026-10-24: the default 3 days reaches 2026-10-27 (see the prototype test above). Set 10 days from today.
        await _w.ExecAsync("INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) VALUES (" + _w.PropertyId + ", 'mnt.pmLeadDays', '2026-01-01', '2026-01-01'); " +
            "INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, source, changed_by, changed_utc) " +
            "VALUES (" + _w.PropertyId + ", 'mnt.pmLeadDays', '2026-10-25', '10', '3', 'b', 'a', 'change', " + _w.Users["owner"] + ", '2026-01-01');", Ct);
        var today = (await _w.Commands.GeneratePmAsync(_w.Ctx("supervisor", "l1"), new GeneratePmWorkOrdersRequest(), Ct)).Value;
        Assert.Equal("2026-10-27", today.HorizonDate);                                         // not effective yet
        await _w.ExecAsync("UPDATE inv.property SET business_date = '2026-10-25' WHERE property_id = " + _w.PropertyId + ";", Ct);
        var later = (await _w.Commands.GeneratePmAsync(_w.Ctx("supervisor", "l2"), new GeneratePmWorkOrdersRequest(), Ct)).Value;
        Assert.Equal("2026-11-04", later.HorizonDate);                                         // 25 Oct + 10 days
    }
}
