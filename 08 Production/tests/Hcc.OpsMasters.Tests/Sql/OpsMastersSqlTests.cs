using Hcc.Application.OpsMasters;
using Hcc.Contracts;
using Hcc.Contracts.GuestServices;
using Hcc.Contracts.Housekeeping;
using Hcc.Contracts.OpsMasters;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.OpsMasters;
using Hcc.OpsMasters.Tests.Harness;

namespace Hcc.OpsMasters.Tests.Sql;

/// <summary>The four operations masters on a disposable HccTest_* database: migration and seed, commands with idempotency / STALE_VERSION /
/// in-use rules / audit / outbox / history, RBAC (PERM-MST-001 = Owner + Tech admin), and their effect on the housekeeping task machine,
/// stay-over assignment and guest-services logging.</summary>
[Trait("Category", "Sql")]
public class OpsMastersSqlTests : IAsyncLifetime
{
    private OpsWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await OpsWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private static string[] Items(params string[] x) => x;

    private Task<HkTaskTypeView> TypeAsync(string code) => TypesAsync().ContinueWith(t => t.Result.Single(x => x.Code == code), Ct);

    private async Task<IReadOnlyList<HkTaskTypeView>> TypesAsync() => (await _w.Queries.TaskTypesAsync(_w.PropertyId, null, null, null, Ct)).Items;

    private async Task<HkTaskTypeView> SaveTypeAsync(string user, string key, HkTaskTypeView? old, string code, string name, string[] kinds, int minutes, bool inspect, string[] checklist, string? version = null) =>
        (await _w.Commands.SaveHkTaskTypeAsync(_w.Ctx(user, key), new SaveHkTaskTypeRequest(old?.Id, code, name, null, kinds, minutes, inspect, checklist, version ?? old?.Version), Ct)).Value;

    private async Task<HkZoneView> SaveZoneAsync(string user, string key, string code, string name, string[]? floors = null, string[]? rooms = null, string? attendant = null, HkZoneView? old = null) =>
        (await _w.Commands.SaveHkZoneAsync(_w.Ctx(user, key), new SaveHkZoneRequest(old?.Id, code, name, null, floors, rooms, attendant is null ? null : _w.Users[attendant], old?.Version), Ct)).Value;

    // ───────────── Migration, seed, permissions ─────────────
    [Fact]
    public async Task Migration_creates_the_tables_and_seeds_the_prototype_values_for_the_property()
    {
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM sys.schemas WHERE name = 'gsv';", Ct));
        var types = await TypesAsync();
        Assert.Equal(["DEP", "STAY", "DEEP", "INSP"], types.Select(t => t.Code));
        Assert.Equal([40, 20, 90, 10], types.Select(t => t.StandardMinutes));
        Assert.Equal([true, false, true, false], types.Select(t => t.NeedsInspection));
        Assert.Equal(["checkout", "move"], types[0].AppliesTo);
        Assert.Equal(["Linen & bathroom", "Surfaces & amenities", "Final room check"], types[0].Checklist);
        Assert.Equal(3, types[3].Checklist.Count);
        var cc = (await _w.Queries.ComplaintCategoriesAsync(_w.PropertyId, null, null, null, Ct)).Items;
        Assert.Equal(["Room cleanliness", "Maintenance", "Noise", "Staff behaviour", "Food & beverage", "Billing", "Amenities", "Other"], cc.Select(c => c.Name));
        Assert.Equal(("Front desk", 2), (cc[2].Department, cc[2].SlaHours));
        var lf = (await _w.Queries.LostFoundCategoriesAsync(_w.PropertyId, null, null, null, Ct)).Items;
        Assert.Equal(["JEWEL", "ELEC", "CASH", "DOCS"], lf.Where(x => x.Valuable).Select(x => x.Code));
        Assert.Equal(7, lf.Count);
        Assert.Empty((await _w.Queries.ZonesAsync(_w.PropertyId, null, null, null, Ct)).Items); // zones are not seeded
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.permission WHERE permission_id LIKE 'PERM-MST-%' AND permission_id NOT IN ('PERM-MST-001','PERM-MST-002');", Ct)); // no new PERM ids
    }

    [Fact]
    public async Task First_time_setup_seed_equals_the_migration_seed_and_running_either_twice_adds_nothing()
    {
        var second = Convert.ToInt32(await _w.ScalarAsync("INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('NEW', N'New hotel', 'Asia/Kolkata', '2026-10-24');", Ct));
        await _w.ExecAsync("DECLARE @p INT = " + second + ";\n" + OpsMastersSeed.Sql, Ct);
        foreach (var (table, cols) in new[] { ("ops.hk_task_type", "code, name, sort_order, applies_to, standard_minutes, needs_inspection, checklist_json"), ("gsv.complaint_category", "code, name, sort_order, department, sla_hours"),
                     ("gsv.lostfound_category", "code, name, sort_order, department, sla_hours, valuable") })
        {
            Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM (SELECT {cols} FROM {table} WHERE property_id = {_w.PropertyId} EXCEPT SELECT {cols} FROM {table} WHERE property_id = {second}) x;", Ct));
            Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM (SELECT {cols} FROM {table} WHERE property_id = {second} EXCEPT SELECT {cols} FROM {table} WHERE property_id = {_w.PropertyId}) x;", Ct));
            Assert.True(await _w.CountAsync($"SELECT COUNT(*) FROM {table} WHERE property_id = {second};", Ct) > 0);
        }

        var before = await _w.CountAsync("SELECT (SELECT COUNT(*) FROM ops.hk_task_type) + (SELECT COUNT(*) FROM gsv.complaint_category) + (SELECT COUNT(*) FROM gsv.lostfound_category);", Ct);
        await _w.ExecAsync(OpsWorld.OpsSeedMigrationSql(), Ct);
        await _w.ExecAsync("DECLARE @p INT = " + second + ";\n" + OpsMastersSeed.Sql, Ct);
        Assert.Equal(before, await _w.CountAsync("SELECT (SELECT COUNT(*) FROM ops.hk_task_type) + (SELECT COUNT(*) FROM gsv.complaint_category) + (SELECT COUNT(*) FROM gsv.lostfound_category);", Ct));
    }

    [Fact]
    public async Task Records_logged_before_the_masters_get_their_category_code_by_name_when_the_seed_runs()
    {
        await _w.ExecAsync($"""
            INSERT ops.guest_complaint(property_id, guest_name, category, severity, description, owner_id, status, created_utc, created_by)
            VALUES ({_w.PropertyId}, N'Old guest', N'Noise', 'Low', N'Music at night', {_w.Users["owner"]}, 'Open', '2026-10-20T10:00:00', {_w.Users["owner"]});
            INSERT ops.lost_item(property_id, description, category, valuable, found_where, found_utc, found_date, found_by, storage, status)
            VALUES ({_w.PropertyId}, N'Scarf', N'Clothing', 0, N'Lobby', '2026-10-20T10:00:00', '2026-10-20', {_w.Users["owner"]}, 'Housekeeping store', 'Stored');
            """, Ct);
        await _w.ExecAsync(OpsWorld.OpsSeedMigrationSql(), Ct);
        Assert.Equal("NOISE", (string)(await _w.ScalarAsync("SELECT category_code FROM ops.guest_complaint;", Ct))!);
        Assert.Equal("CLOTH", (string)(await _w.ScalarAsync("SELECT category_code FROM ops.lost_item;", Ct))!);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM gsv.complaint_category WHERE code = 'NOISE';", Ct)); // idempotent
    }

    [Fact]
    public async Task Only_Owner_and_Tech_admin_change_the_lists_everyone_may_read_them()
    {
        foreach (var user in new[] { "manager", "reception", "cashier", "supervisor", "housekeeper", "accountant", "maint" })
        {
            var e = await Assert.ThrowsAsync<HccException>(async () => await SaveTypeAsync(user, "d-" + user, null, "X", "X", Items("deep"), 30, false, Items("A")));
            Assert.Equal(HccErrorCode.FORBIDDEN, e.Code);
            await Assert.ThrowsAsync<HccException>(async () => await SaveZoneAsync(user, "z-" + user, "X", "X", ["F1"]));
            await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveComplaintCategoryAsync(_w.Ctx(user, "c-" + user), new SaveComplaintCategoryRequest(null, "X", "X", null, "Front desk", 4), Ct));
            await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveLostFoundCategoryAsync(_w.Ctx(user, "l-" + user), new SaveLostFoundCategoryRequest(null, "X", "X", null, "Front desk", 4, false), Ct));
            await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RetireAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx(user, "r-" + user), new OpsRowRequest(1, "AAAAAAAAAAA="), Ct));
            await Assert.ThrowsAsync<HccException>(async () => await _w.Queries.HistoryAsync(OpsMasterDefs.HkTaskTypes, _w.PropertyId, _w.Users[user], null, null, null, Ct));
        }

        foreach (var user in new[] { "owner", "techadmin" })
        {
            var c = (await _w.Commands.SaveComplaintCategoryAsync(_w.Ctx(user, "ok-" + user), new SaveComplaintCategoryRequest(null, "T-" + user[..3].ToUpperInvariant(), "Test " + user, null, "Front desk", 4), Ct)).Value;
            Assert.True(c.Active);
            Assert.NotNull(await _w.Queries.HistoryAsync(OpsMasterDefs.ComplaintCategories, _w.PropertyId, _w.Users[user], c.Id, null, null, Ct));
        }

        Assert.Equal(4, (await _w.Queries.TaskTypesAsync(_w.PropertyId, null, null, null, Ct)).Total); // reads need only a session
    }

    // ───────────── Task types ─────────────
    [Fact]
    public async Task Task_type_save_edit_with_audit_history_outbox_idempotency_and_stale_version()
    {
        var dep = await TypeAsync("DEP");
        var edited = await SaveTypeAsync("techadmin", "e1", dep, "DEP", "Departure clean", Items("checkout", "move"), 45, true, Items("Linen", "Bathroom", "Surfaces", "Final check"));
        Assert.Equal((45, 4), (edited.StandardMinutes, edited.Checklist.Count));
        Assert.Equal("Master Housekeeping task types: standardMinutes 40 → 45; checklist Linen & bathroom / Surfaces & amenities / Final room check → Linen / Bathroom / Surfaces / Final check",
            (string)(await _w.ScalarAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'MST_HK_TASK_TYPE' ORDER BY audit_id DESC;", Ct))!);
        var h = await _w.Queries.HistoryAsync(OpsMasterDefs.HkTaskTypes, _w.PropertyId, _w.Users["owner"], dep.Id, null, null, Ct);
        Assert.Equal(["checklist", "standardMinutes"], h.Items.Select(x => x.Field).Order());
        Assert.Equal(("40", "45"), (h.Items.Single(x => x.Field == "standardMinutes").OldValue!, h.Items.Single(x => x.Field == "standardMinutes").NewValue!));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'MasterChanged';", Ct));
        var stale = await Assert.ThrowsAsync<HccException>(async () => await SaveTypeAsync("owner", "e2", dep, "DEP", "Departure clean", Items("checkout", "move"), 50, true, Items("A")));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        var nothing = await Assert.ThrowsAsync<HccException>(async () => await SaveTypeAsync("owner", "e3", edited, "DEP", "Departure clean", Items("checkout", "move"), 45, true, Items("Linen", "Bathroom", "Surfaces", "Final check")));
        Assert.Equal(HccErrorCode.VALIDATION, nothing.Code);
        var req = new SaveHkTaskTypeRequest(null, "FAST", "Fast clean", null, ["inspection"], 8, false, ["One"]);
        // "inspection" is already covered by INSP: refused; retire INSP first, then the same key saves once and replays
        var clash = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveHkTaskTypeAsync(_w.Ctx("owner", "n1"), req, Ct));
        Assert.Equal("MASTER_INVALID", Reason(clash));
        var insp = await TypeAsync("INSP");
        await _w.Commands.RetireAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "r1"), new OpsRowRequest(insp.Id, insp.Version), Ct);
        var first = await _w.Commands.SaveHkTaskTypeAsync(_w.Ctx("owner", "n2"), req, Ct);
        var again = await _w.Commands.SaveHkTaskTypeAsync(_w.Ctx("owner", "n2"), req, Ct);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.Id, again.Value.Id);
        Assert.Equal(5, (await TypesAsync()).Count);
        var mismatch = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveHkTaskTypeAsync(_w.Ctx("owner", "n2"), req with { StandardMinutes = 9 }, Ct));
        Assert.Equal(HccErrorCode.IDEMPOTENCY_MISMATCH, mismatch.Code);
    }

    [Fact]
    public async Task Task_type_validation_errors_carry_the_master_reason_and_field()
    {
        async Task<HccException> Bad(string code, string name, string[]? kinds, int minutes, bool inspect, string[] list) =>
            await Assert.ThrowsAsync<HccException>(async () => await SaveTypeAsync("owner", Guid.NewGuid().ToString("N"), null, code, name, kinds!, minutes, inspect, list));
        Assert.Equal("MASTER_CODE_TAKEN", Reason(await Bad("dep", "Other name", Items("deep"), 30, false, Items("A"))));
        Assert.Equal("name", (await Bad("N1", "departure CLEAN", Items("deep"), 30, false, Items("A"))).Details!["field"].GetString());
        Assert.Equal("code", (await Bad("bad code", "X", Items("deep"), 30, false, Items("A"))).Details!["field"].GetString());
        Assert.Equal("standardMinutes", (await Bad("N2", "X", Items("deep"), 3, false, Items("A"))).Details!["field"].GetString());
        Assert.Equal("checklist", (await Bad("N3", "X", Items("deep"), 30, false, Items())).Details!["field"].GetString());
        Assert.Equal("needsInspection", (await Bad("N4", "X", Items("stayover"), 30, true, Items("A"))).Details!["field"].GetString());
        Assert.Equal("appliesTo", (await Bad("N5", "X", Items("deep"), 30, false, Items("A"))).Details!["field"].GetString()); // deep is already DEEP's
    }

    [Fact]
    public async Task Task_type_in_use_cannot_be_deleted_or_change_its_code_unused_rows_can_and_retire_reactivate_reorder_work()
    {
        await _w.TaskAsync("101", "checkout", "ToDo", "housekeeper", Ct);
        var types = await TypesAsync();
        var dep = types.Single(t => t.Code == "DEP");
        Assert.Equal(1, dep.Used);
        var del = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.DeleteAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "d1"), new OpsRowRequest(dep.Id, dep.Version), Ct));
        Assert.Equal("MASTER_IN_USE", Reason(del));
        Assert.Contains("retire it instead", del.Message, StringComparison.Ordinal);
        var code = await Assert.ThrowsAsync<HccException>(async () => await SaveTypeAsync("owner", "c1", dep, "DEP2", "Departure clean", Items("checkout", "move"), 40, true, Items("Linen & bathroom", "Surfaces & amenities", "Final room check")));
        Assert.Equal("MASTER_IN_USE", Reason(code));
        var retired = (await _w.Commands.RetireAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("techadmin", "r1"), new OpsRowRequest(dep.Id, dep.Version), Ct)).Value;
        Assert.False(retired.Active);
        Assert.NotNull(retired.RetiredOn);
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, (await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RetireAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "r2"), new OpsRowRequest(dep.Id, retired.Version), Ct))).Code);
        var asRetired = await Assert.ThrowsAsync<HccException>(async () => await SaveTypeAsync("owner", "c2", await TypeAsync("DEP"), "DEP", "Departure clean", Items("checkout"), 41, true, Items("A")));
        Assert.Equal("MASTER_RETIRED", Reason(asRetired));
        var back = (await _w.Commands.ReactivateAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "r3"), new OpsRowRequest(dep.Id, retired.Version), Ct)).Value;
        Assert.True(back.Active);
        var deep = types.Single(t => t.Code == "DEEP");
        Assert.Equal(0, deep.Used);
        Assert.True((await _w.Commands.DeleteAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "d2"), new OpsRowRequest(deep.Id, deep.Version), Ct)).Value.Deleted);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM mst.master_history WHERE master = 'hkTaskTypes' AND field = 'deleted';", Ct));
        var now = await TypesAsync();
        await _w.Commands.ReorderAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "o1"), new OpsReorderRequest(now.Select(t => t.Id).Reverse().ToList()), Ct);
        Assert.Equal(["INSP", "STAY", "DEP"], (await TypesAsync()).Select(t => t.Code));
        Assert.Equal([10, 20, 30], (await TypesAsync()).Select(t => t.SortOrder));
        await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.ReorderAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "o2"), new OpsReorderRequest(now.Select(t => t.Id).Take(1).ToList()), Ct));
    }

    [Fact]
    public async Task The_checklist_and_inspection_rule_of_the_task_type_drive_the_task_machine_and_the_task_view()
    {
        var t1 = await _w.TaskAsync("101", "checkout", "ToDo", "housekeeper", Ct);
        var open = (await _w.HkReads.OpenTasksAsync(_w.PropertyId, null, Ct)).Single(t => t.TaskId == t1);
        Assert.Equal(3, open.Checklist.Count); // seeded type: three items
        var dep = await TypeAsync("DEP");
        await SaveTypeAsync("owner", "e1", dep, "DEP", "Departure clean", Items("checkout", "move"), 40, true, Items("Linen", "Bathroom", "Surfaces", "Final check"));
        Assert.Equal(4, (await _w.HkReads.OpenTasksAsync(_w.PropertyId, null, Ct)).Single(t => t.TaskId == t1).Checklist.Count);
        var view = await Step(t1, "start", "s1");
        foreach (var i in new[] { 0, 1, 2 }) view = await Step(t1, "check", "c" + i, i);
        var early = await Assert.ThrowsAsync<HccException>(async () => await Step(t1, "complete", "x1"));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, early.Code);
        await Assert.ThrowsAsync<HccException>(async () => await Step(t1, "check", "c4", 4));
        view = await Step(t1, "check", "c3", 3);
        Assert.Equal(4, view.Checklist.Count);
        Assert.All(view.Checklist, Assert.True);
        view = await Step(t1, "complete", "done1");
        Assert.Equal("Inspection", view.Status);
        Assert.Equal("Inspection", (string)(await _w.ScalarAsync($"SELECT hk_status FROM inv.room WHERE room_id = {_w.Rooms["101"]};", Ct))!);

        // inspection off + two items: complete goes straight to Done and the room is Ready
        dep = await TypeAsync("DEP");
        await SaveTypeAsync("owner", "e2", dep, "DEP", "Departure clean", Items("checkout", "move"), 40, false, Items("Linen", "Final check"));
        var t2 = await _w.TaskAsync("102", "checkout", "ToDo", "housekeeper", Ct);
        await Step(t2, "start", "t2s");
        await Step(t2, "check", "t2c0", 0);
        await Step(t2, "check", "t2c1", 1);
        view = await Step(t2, "complete", "t2d");
        Assert.Equal("Done", view.Status);
        Assert.Equal("Ready", (string)(await _w.ScalarAsync($"SELECT hk_status FROM inv.room WHERE room_id = {_w.Rooms["102"]};", Ct))!);

        // a kind without an active type falls back to the built-in rule (3 items, inspected)
        await _w.Commands.RetireAsync(OpsMasterDefs.HkTaskTypes, _w.Ctx("owner", "ret"), new OpsRowRequest(dep.Id, await _w.VersionAsync("ops.hk_task_type", "task_type_id", dep.Id, Ct)), Ct);
        var t3 = await _w.TaskAsync("103", "checkout", "ToDo", "housekeeper", Ct);
        Assert.Equal(2, (await _w.HkReads.OpenTasksAsync(_w.PropertyId, null, Ct)).Single(t => t.TaskId == t3).Checklist.Count); // a retired type still serves until another covers the kind
        var rep = await _w.TaskAsync("104", "repair", "ToDo", "maint", Ct);
        Assert.Equal(3, (await _w.HkReads.OpenTasksAsync(_w.PropertyId, null, Ct)).Single(t => t.TaskId == rep).Checklist.Count);
        await _w.ExecAsync("DELETE ops.hk_task_type WHERE code = 'DEP';", Ct);
        Assert.Equal(3, (await _w.HkReads.OpenTasksAsync(_w.PropertyId, null, Ct)).Single(t => t.TaskId == t3).Checklist.Count);
    }

    private async Task<HousekeepingTaskView> Step(long taskId, string action, string key, int? index = null)
    {
        var r = await _w.Housekeeping.TransitionAsync(_w.Ctx("owner", key, HousekeepingCommands_Op), new TransitionHousekeepingTaskRequest(taskId, await _w.TaskVersionAsync(taskId, Ct), action, null, index), Ct);
        return r.Value.Task;
    }

    private const string HousekeepingCommands_Op = "TransitionHousekeepingTask";

    // ───────────── Zones ─────────────
    [Fact]
    public async Task Zone_save_validation_overlap_attendant_and_in_use_rules()
    {
        var z = await SaveZoneAsync("techadmin", "z1", "Z1", "Ground section", ["F1"], null, "housekeeper");
        Assert.Equal(["F1"], z.Floors);
        Assert.Empty(z.Rooms);
        Assert.Equal(_w.Users["housekeeper"], z.DefaultAttendantId);
        Assert.Equal(0, z.Used);
        async Task<HccException> Bad(string key, string code, string[]? floors, string[]? rooms, string? att = null) =>
            await Assert.ThrowsAsync<HccException>(async () => await SaveZoneAsync("owner", key, code, "Zone " + code, floors, rooms, att));
        Assert.Contains("at least one floor or room", (await Bad("b1", "E", null, null)).Message, StringComparison.Ordinal);
        Assert.Equal("floors", (await Bad("b2", "F9", ["F9"], null)).Details!["field"].GetString());
        Assert.Equal("rooms", (await Bad("b3", "R9", null, ["999"])).Details!["field"].GetString());
        Assert.Equal("floors", (await Bad("b4", "DUP", ["F1"], null)).Details!["field"].GetString());
        var second = await SaveZoneAsync("owner", "z2", "VIP", "VIP rooms", null, ["101", "102"], "housekeeper2");
        Assert.Equal("rooms", (await Bad("b5", "DUP2", null, ["102"])).Details!["field"].GetString());
        Assert.Equal("defaultAttendant", (await Bad("b7", "MNT", ["F2"], null, "maint")).Details!["field"].GetString());
        // open tasks in the zone's rooms = "used" (101 and 102 are in Z1's floor and VIP)
        await _w.TaskAsync("101", "checkout", "ToDo", "housekeeper", Ct);
        await _w.TaskAsync("201", "checkout", "ToDo", "housekeeper", Ct);
        await _w.TaskAsync("105", "repair", "ToDo", "maint", Ct);
        var list = (await _w.Queries.ZonesAsync(_w.PropertyId, null, null, null, Ct)).Items;
        Assert.Equal(1, list.Single(x => x.Code == "Z1").Used);
        Assert.Equal(1, list.Single(x => x.Code == "VIP").Used);
        var del = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.DeleteAsync(OpsMasterDefs.HkZones, _w.Ctx("owner", "dz"), new OpsRowRequest(second.Id, second.Version), Ct));
        Assert.Equal("MASTER_IN_USE", Reason(del));
        var edited = await SaveZoneAsync("owner", "z3", "Z1", "Ground section", ["F1", "F2"], null, "housekeeper", z);
        Assert.Equal(["F1", "F2"], edited.Floors);
        Assert.Equal(7, await _w.CountAsync("SELECT COUNT(*) FROM mst.master_history WHERE master = 'hkZones' AND row_id = " + z.Id + ";", Ct));
    }

    [Fact]
    public async Task Stayover_tasks_go_to_the_default_attendant_of_the_rooms_zone_a_listed_room_beats_its_floor_and_a_disabled_attendant_leaves_it_unassigned()
    {
        foreach (var room in new[] { "101", "201", "202", "301" }) await _w.InHouseAsync(room, "Guest " + room, Ct);
        await SaveZoneAsync("owner", "za", "Z1", "Floor 1 section", ["F1"], null, "housekeeper");
        await SaveZoneAsync("owner", "zb", "Z2", "Floor 2 section", ["F2"], null, "housekeeper");
        await SaveZoneAsync("owner", "zc", "VIP", "VIP", null, ["201"], "housekeeper2");
        var run = (await _w.Stayover.GenerateAsync(_w.Ctx("owner", "g1", "GenerateStayoverTasks"), new GenerateStayoverTasksRequest(), Ct)).Value;
        Assert.Equal(4, run.Created);
        async Task<int?> Assignee(string room) => await _w.ScalarAsync($"SELECT assignee_id FROM ops.hk_task WHERE kind = 'stayover' AND room_id = {_w.Rooms[room]};", Ct) is int a ? a : null;
        Assert.Equal(_w.Users["housekeeper"], await Assignee("101"));
        Assert.Equal(_w.Users["housekeeper2"], await Assignee("201")); // the room itself wins
        Assert.Equal(_w.Users["housekeeper"], await Assignee("202")); // its floor's zone
        Assert.Null(await Assignee("301"));                              // no zone: unassigned as before
        // next day: the VIP attendant is disabled → the room stays unassigned (the zone is still the one that applies)
        await _w.ExecAsync($"UPDATE plat.app_user SET enabled = 0 WHERE user_id = {_w.Users["housekeeper2"]}; UPDATE inv.property SET business_date = '2026-10-25' WHERE property_id = {_w.PropertyId};", Ct);
        run = (await _w.Stayover.GenerateAsync(_w.Ctx("owner", "g2", "GenerateStayoverTasks"), new GenerateStayoverTasksRequest(), Ct)).Value;
        Assert.Equal(4, run.Created);
        Assert.Equal(DBNull.Value, await _w.ScalarAsync($"SELECT assignee_id FROM ops.hk_task WHERE kind = 'stayover' AND room_id = {_w.Rooms["201"]} AND business_date = '2026-10-25';", Ct));
        Assert.Equal(_w.Users["housekeeper"], Convert.ToInt32(await _w.ScalarAsync($"SELECT assignee_id FROM ops.hk_task WHERE kind = 'stayover' AND room_id = {_w.Rooms["101"]} AND business_date = '2026-10-25';", Ct)));
    }

    // ───────────── Complaint and lost-and-found categories ─────────────
    private async Task<ComplaintResult> LogComplaintAsync(string key, string category, string severity = "Low") =>
        (await _w.GuestServices.LogComplaintAsync(_w.Ctx("owner", key, "LogComplaint"), new LogComplaintRequest(null, "Test Guest", category, severity, "Loud music at night", _w.Users["owner"]), Ct)).Value;

    [Fact]
    public async Task Complaint_categories_drive_logging_codes_names_report_and_in_use_rules()
    {
        var c1 = await LogComplaintAsync("l1", "NOISE");
        var c2 = await LogComplaintAsync("l2", "room cleanliness"); // a name still resolves (older callers)
        Assert.Equal(("Noise", "NOISE"), (await Cat(c1.ComplaintId)));
        Assert.Equal(("Room cleanliness", "CLEAN"), (await Cat(c2.ComplaintId)));
        var view = (await _w.GuestQueries.ComplaintsAsync(_w.PropertyId, _w.Users["owner"], 1, 50, false, Ct)).Items.Single(x => x.ComplaintId == c1.ComplaintId);
        Assert.Equal("NOISE", view.CategoryCode);
        var bad = await Assert.ThrowsAsync<HccException>(async () => await LogComplaintAsync("l3", "Nope"));
        Assert.Equal(HccErrorCode.VALIDATION, bad.Code);
        // new category → can be logged and is reported; retired → refused for new, still reported
        var park = (await _w.Commands.SaveComplaintCategoryAsync(_w.Ctx("techadmin", "n1"), new SaveComplaintCategoryRequest(null, "PARK", "Parking", null, "Front desk", 6), Ct)).Value;
        Assert.Equal(90, park.SortOrder);
        var c3 = await LogComplaintAsync("l4", "PARK");
        Assert.Equal(("Parking", "PARK"), await Cat(c3.ComplaintId));
        var rep = await _w.GuestQueries.ComplaintReportAsync(_w.PropertyId, _w.Users["owner"], Ct);
        Assert.Equal(["Room cleanliness", "Noise", "Parking"], rep.Select(r => r.Category));
        var used = (await _w.Queries.ComplaintCategoriesAsync(_w.PropertyId, null, null, null, Ct)).Items;
        Assert.Equal(1, used.Single(x => x.Code == "PARK").Used);
        var del = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.DeleteAsync(OpsMasterDefs.ComplaintCategories, _w.Ctx("owner", "d1"), new OpsRowRequest(park.Id, park.Version), Ct));
        Assert.Equal("MASTER_IN_USE", Reason(del));
        var retired = (await _w.Commands.RetireAsync(OpsMasterDefs.ComplaintCategories, _w.Ctx("owner", "r1"), new OpsRowRequest(park.Id, park.Version), Ct)).Value;
        await Assert.ThrowsAsync<HccException>(async () => await LogComplaintAsync("l5", "PARK"));
        Assert.Contains(await _w.GuestQueries.ComplaintReportAsync(_w.PropertyId, _w.Users["owner"], Ct), r => r.Category == "Parking");
        // rename keeps the logged name (the snapshot) and the code; an unused category deletes cleanly
        var noise = used.Single(x => x.Code == "NOISE");
        var renamed = (await _w.Commands.SaveComplaintCategoryAsync(_w.Ctx("owner", "n2"), new SaveComplaintCategoryRequest(noise.Id, "NOISE", "Noise and disturbance", null, "Front desk", 3, noise.Version), Ct)).Value;
        Assert.Equal(3, renamed.SlaHours);
        Assert.Equal(("Noise", "NOISE"), await Cat(c1.ComplaintId));
        var billing = used.Single(x => x.Code == "BILLING");
        Assert.True((await _w.Commands.DeleteAsync(OpsMasterDefs.ComplaintCategories, _w.Ctx("owner", "d2"), new OpsRowRequest(billing.Id, billing.Version), Ct)).Value.Deleted);
        var dept = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveComplaintCategoryAsync(_w.Ctx("owner", "n3"), new SaveComplaintCategoryRequest(null, "X1", "X one", null, "Kitchen", 3), Ct));
        Assert.Equal("department", dept.Details!["field"].GetString());
        Assert.False(retired.Active);
    }

    private async Task<(string, string?)> Cat(long complaintId) =>
        ((string)(await _w.ScalarAsync($"SELECT category FROM ops.guest_complaint WHERE complaint_id = {complaintId};", Ct))!,
         (await _w.ScalarAsync($"SELECT category_code FROM ops.guest_complaint WHERE complaint_id = {complaintId};", Ct)) as string);

    [Fact]
    public async Task Lost_and_found_categories_decide_valuable_and_safe_storage_and_keep_their_code_on_the_item()
    {
        async Task<LostItemResult> Log(string key, string category, string storage) =>
            (await _w.GuestServices.LogItemAsync(_w.Ctx("owner", key, "LogLostItem"), new LogLostItemRequest("Test item", category, "Lobby", storage, null, null), Ct)).Value;
        var scarf = await Log("i1", "CLOTH", "Housekeeping store");
        Assert.False(scarf.Valuable);
        var ring = await Assert.ThrowsAsync<HccException>(async () => await Log("i2", "Jewellery", "Housekeeping store"));
        Assert.Contains("Valuables (Jewellery)", ring.Message, StringComparison.Ordinal);
        Assert.True((await Log("i3", "JEWEL", "Front office safe")).Valuable);
        Assert.Equal("JEWEL", (string)(await _w.ScalarAsync("SELECT TOP 1 category_code FROM ops.lost_item WHERE category = N'Jewellery';", Ct))!);
        var art = (await _w.Commands.SaveLostFoundCategoryAsync(_w.Ctx("owner", "a1"), new SaveLostFoundCategoryRequest(null, "ART", "Artwork", null, "Front desk", 6, true), Ct)).Value;
        Assert.True(art.Valuable);
        await Assert.ThrowsAsync<HccException>(async () => await Log("i4", "ART", "Back office cupboard"));
        Assert.True((await Log("i5", "ART", "Front office safe")).Valuable);
        var flipped = (await _w.Commands.SaveLostFoundCategoryAsync(_w.Ctx("owner", "a2"), new SaveLostFoundCategoryRequest(art.Id, "ART", "Artwork", null, "Front desk", 6, false, art.Version), Ct)).Value;
        Assert.False(flipped.Valuable);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM ops.lost_item WHERE category_code = 'ART' AND valuable = 1;", Ct)); // logged items are not rewritten
        Assert.Equal(1, (await _w.Queries.LostFoundCategoriesAsync(_w.PropertyId, null, null, null, Ct)).Items.Single(x => x.Code == "ART").Used);
        await Assert.ThrowsAsync<HccException>(async () => await Log("i6", "Nope", "Front office safe"));
        var histories = await _w.Queries.HistoryAsync(OpsMasterDefs.LostFoundCategories, _w.PropertyId, _w.Users["owner"], art.Id, null, null, Ct);
        Assert.Equal(("valuable", "yes", "no"), (histories.Items[0].Field, histories.Items[0].OldValue!, histories.Items[0].NewValue!));
    }
}
