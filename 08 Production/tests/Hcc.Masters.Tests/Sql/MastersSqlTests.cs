using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Masters.Tests.Harness;

namespace Hcc.Masters.Tests.Sql;

/// <summary>Masters commands and queries on a disposable HccTest_* database: migration, RBAC seed (DEC-MA-1), commands with idempotency,
/// STALE_VERSION, in-use rules, audit, outbox and history, and paged reads.</summary>
[Trait("Category", "Sql")]
public class MastersSqlTests : IAsyncLifetime
{
    private MastersWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await MastersWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private async Task<FloorView> RenameAsync(string user, string code, string name, string key) =>
        (await _w.Commands.SaveFloorAsync(_w.Ctx(user, key), new SaveFloorRequest(_w.Floors[code], code, name, null, await _w.VersionAsync(_w.Floors[code], Ct)), Ct)).Value;

    [Fact]
    public async Task Migration_creates_the_schema_seeds_floors_from_rooms_and_grants_PERM_MST_to_Owner_and_Tech_admin_only()
    {
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM sys.schemas WHERE name = 'mst';", Ct));
        var list = await _w.Queries.FloorsAsync(_w.PropertyId, _w.Users["housekeeper"], ct: Ct);
        Assert.Equal(["F1", "F2", "F3"], list.Items.Select(f => f.Code));
        Assert.Equal(["Floor 1", "Floor 2", "Floor 3"], list.Items.Select(f => f.Name));
        Assert.Equal([10, 20, 30], list.Items.Select(f => f.SortOrder));
        Assert.All(list.Items, f => Assert.Equal(8, f.RoomCount));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.permission WHERE permission_id LIKE 'PERM-MST-%';", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.permission WHERE permission_id = 'PERM-MST-002' AND is_money = 1;", Ct));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-MST-001' AND role_code IN ('OWN','TAD');", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-MST-001' AND role_code NOT IN ('OWN','TAD');", Ct)); // DEC-MA-1: not MGR
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.role_grant WHERE permission_id = 'PERM-MST-002' AND role_code <> 'OWN';", Ct));
        await Assert.ThrowsAnyAsync<Exception>(() => _w.ExecAsync($"INSERT mst.floor(property_id, code, name, sort_order, updated_by, updated_utc) VALUES ({_w.PropertyId}, 'f9', N'x', 1, 1, SYSUTCDATETIME());", Ct)); // CK code
    }

    [Fact]
    public async Task Save_creates_and_edits_with_audit_history_and_outbox()
    {
        var added = (await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "a1"), new SaveFloorRequest(null, " f4 ", " Annexe "), Ct)).Value;
        Assert.Equal(("F4", "Annexe", 40, true, 0), (added.Code, added.Name, added.SortOrder, added.Active, added.RoomCount));
        Assert.Equal("Master Floors: added F4 · Annexe", (string)(await _w.ScalarAsync("SELECT TOP 1 detail FROM plat.audit_event WHERE action = 'MST_FLOOR' ORDER BY audit_id DESC;", Ct))!);
        var edited = await RenameAsync("techadmin", "F1", "Ground floor", "e1");
        Assert.Equal("Ground floor", edited.Name);
        Assert.Equal(8, edited.RoomCount);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MST_FLOOR' AND detail = N'Master Floors: name Floor 1 → Ground floor';", Ct));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'MasterChanged';", Ct));
        var h = await _w.Queries.FloorHistoryAsync(_w.PropertyId, _w.Users["owner"], _w.Floors["F1"], ct: Ct);
        Assert.Single(h.Items);
        Assert.Equal(("name", "Floor 1", "Ground floor", _w.Users["techadmin"]), (h.Items[0].Field, h.Items[0].OldValue, h.Items[0].NewValue, h.Items[0].ChangedBy));
        Assert.Equal(3, (await _w.Queries.FloorHistoryAsync(_w.PropertyId, _w.Users["owner"], added.FloorId, ct: Ct)).Total);
        var nothing = await Assert.ThrowsAsync<HccException>(async () => await RenameAsync("owner", "F1", "Ground floor", "e2"));
        Assert.Equal(HccErrorCode.VALIDATION, nothing.Code);
    }

    [Fact]
    public async Task Idempotent_replay_returns_the_first_result_and_a_changed_payload_is_a_mismatch()
    {
        var req = new SaveFloorRequest(null, "F4", "Annexe");
        var first = await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "k1"), req, Ct);
        var again = await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "k1"), req, Ct);
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value.FloorId, again.Value.FloorId);
        Assert.Equal(4, await _w.CountAsync("SELECT COUNT(*) FROM mst.floor;", Ct));
        var e = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "k1"), req with { Name = "Other" }, Ct));
        Assert.Equal(HccErrorCode.IDEMPOTENCY_MISMATCH, e.Code);
    }

    [Fact]
    public async Task Stale_version_duplicate_code_and_in_use_code_change_are_refused()
    {
        var old = await _w.VersionAsync(_w.Floors["F2"], Ct);
        await RenameAsync("owner", "F2", "First floor", "s1");
        var stale = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "s2"), new SaveFloorRequest(_w.Floors["F2"], "F2", "Again", null, old), Ct));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        var taken = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "s3"), new SaveFloorRequest(null, "f3", "Top"), Ct));
        Assert.Equal((HccErrorCode.VALIDATION, MasterReasons.CodeTaken), (taken.Code, Reason(taken)));
        var dupName = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "s4"), new SaveFloorRequest(null, "F5", "floor 3"), Ct));
        Assert.Equal(MasterReasons.Invalid, Reason(dupName));
        var inUse = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "s5"), new SaveFloorRequest(_w.Floors["F3"], "TOP", "Floor 3", null, await _w.VersionAsync(_w.Floors["F3"], Ct)), Ct));
        Assert.Equal((HccErrorCode.INVALID_TRANSITION, MasterReasons.InUse), (inUse.Code, Reason(inUse)));
        var wing = (await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "s6"), new SaveFloorRequest(null, "ANNEXE", "Annexe"), Ct)).Value;
        var renamed = (await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "s7"), new SaveFloorRequest(wing.FloorId, "ANX", "Annexe", null, wing.Version), Ct)).Value;
        Assert.Equal("ANX", renamed.Code);
    }

    [Fact]
    public async Task Retire_in_use_is_allowed_delete_only_when_unused_and_reactivate_restores()
    {
        var f3 = _w.Floors["F3"];
        var retired = (await _w.Commands.RetireFloorAsync(_w.Ctx("owner", "r1"), new RetireFloorRequest(f3, await _w.VersionAsync(f3, Ct)), Ct)).Value;
        Assert.Equal((false, "2026-10-24", 8), (retired.Active, retired.RetiredOn, retired.RoomCount));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail LIKE N'Master Floors: active yes → no (F3 · Floor 3 retired; still used: 8%';", Ct));
        var edit = await Assert.ThrowsAsync<HccException>(async () => await RenameAsync("owner", "F3", "Top", "r2"));
        Assert.Equal(MasterReasons.Retired, Reason(edit));
        var twice = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.RetireFloorAsync(_w.Ctx("owner", "r3"), new RetireFloorRequest(f3, retired.Version), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, twice.Code);
        var del = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.DeleteFloorAsync(_w.Ctx("owner", "r4"), new DeleteFloorRequest(f3, retired.Version), Ct));
        Assert.Equal(MasterReasons.InUse, Reason(del));
        var back = (await _w.Commands.ReactivateFloorAsync(_w.Ctx("techadmin", "r5"), new ReactivateFloorRequest(f3, retired.Version), Ct)).Value;
        Assert.True(back.Active);
        Assert.Null(back.RetiredOn);
        var wing = (await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "r6"), new SaveFloorRequest(null, "ANNEXE", "Annexe"), Ct)).Value;
        var gone = (await _w.Commands.DeleteFloorAsync(_w.Ctx("owner", "r7"), new DeleteFloorRequest(wing.FloorId, wing.Version), Ct)).Value;
        Assert.True(gone.Deleted);
        Assert.Equal(0, await _w.CountAsync($"SELECT COUNT(*) FROM mst.floor WHERE floor_id = {wing.FloorId};", Ct));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM mst.master_history WHERE row_id = {wing.FloorId} AND field = 'deleted';", Ct));
    }

    [Fact]
    public async Task Reorder_sets_steps_of_ten_and_needs_every_floor_once()
    {
        var ids = new[] { _w.Floors["F3"], _w.Floors["F1"], _w.Floors["F2"] };
        var list = (await _w.Commands.ReorderFloorsAsync(_w.Ctx("owner", "o1"), new ReorderFloorsRequest(ids), Ct)).Value;
        Assert.Equal(["F3", "F1", "F2"], list.Items.Select(f => f.Code));
        Assert.Equal([10, 20, 30], list.Items.Select(f => f.SortOrder));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM mst.master_history WHERE field = 'sortOrder';", Ct));
        Assert.Equal(["F3", "F1", "F2"], (await _w.Queries.FloorsAsync(_w.PropertyId, _w.Users["reception"], ct: Ct)).Items.Select(f => f.Code));
        await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.ReorderFloorsAsync(_w.Ctx("owner", "o2"), new ReorderFloorsRequest([ids[0], ids[1]]), Ct));
        var same = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.ReorderFloorsAsync(_w.Ctx("owner", "o3"), new ReorderFloorsRequest(ids), Ct));
        Assert.Contains("did not change", same.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_Owner_and_Tech_admin_change_floors_until_the_Owner_grants_the_Manager_DEC_MA_1()
    {
        foreach (var u in new[] { "manager", "reception", "cashier", "supervisor", "housekeeper", "accountant", "maint" })
        {
            var e = await Assert.ThrowsAsync<HccException>(async () => await _w.Commands.SaveFloorAsync(_w.Ctx(u, "p-" + u), new SaveFloorRequest(null, "F4", "Annexe"), Ct));
            Assert.Equal(HccErrorCode.FORBIDDEN, e.Code);
            await Assert.ThrowsAsync<HccException>(async () => await _w.Queries.FloorHistoryAsync(_w.PropertyId, _w.Users[u], ct: Ct));
        }

        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.idempotency;", Ct));
        await _w.ExecAsync("INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES ('MGR', 'PERM-MST-001', 'A', 'P', NULL, NULL);", Ct);
        Assert.Equal("Ground", (await RenameAsync("manager", "F1", "Ground", "p-mgr2")).Name);
    }

    [Fact]
    public async Task Floor_list_and_history_are_paged_and_filtered()
    {
        for (var i = 4; i <= 9; i++) await _w.Commands.SaveFloorAsync(_w.Ctx("owner", "add" + i), new SaveFloorRequest(null, "F" + i, "Floor " + i), Ct);
        var p1 = await _w.Queries.FloorsAsync(_w.PropertyId, _w.Users["maint"], 1, 4, null, Ct);
        var p3 = await _w.Queries.FloorsAsync(_w.PropertyId, _w.Users["maint"], 3, 4, null, Ct);
        Assert.Equal((9, 4, 1), (p1.Total, p1.Items.Count, p3.Items.Count));
        Assert.Equal("F9", p3.Items[0].Code);
        var f9 = p3.Items[0];
        await _w.Commands.RetireFloorAsync(_w.Ctx("owner", "ret9"), new RetireFloorRequest(f9.FloorId, f9.Version), Ct);
        Assert.Equal(8, (await _w.Queries.FloorsAsync(_w.PropertyId, _w.Users["maint"], status: "active", ct: Ct)).Total);
        Assert.Equal(["F9"], (await _w.Queries.FloorsAsync(_w.PropertyId, _w.Users["maint"], status: "retired", ct: Ct)).Items.Select(f => f.Code));
        await Assert.ThrowsAsync<HccException>(async () => await _w.Queries.FloorsAsync(_w.PropertyId, _w.Users["maint"], status: "gone", ct: Ct));
        var h = await _w.Queries.FloorHistoryAsync(_w.PropertyId, _w.Users["techadmin"], null, 1, 5, Ct);
        Assert.Equal((19, 5), (h.Total, h.Items.Count));
        Assert.Equal("active", h.Items[0].Field);
        Assert.Equal("owner", h.Items[0].ChangedByName);
    }
}
