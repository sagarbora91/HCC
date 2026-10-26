using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.RoomMasters;
using Hcc.Domain.Errors;
using Hcc.RoomMasters.Tests.Harness;

namespace Hcc.RoomMasters.Tests.Sql;

/// <summary>Rooms-masters commands and queries on a disposable HccTest_* database: migration 2810 (floor_id backfill), room types with the
/// rack rate as the tariff schedule, range add / edit / renumber / type change / retire / delete of rooms with the type-night counts kept
/// drift-free, amenities, occupancy charges (Owner money list), permissions (DEC-MA-1, DEC-058, DEC-061), idempotency and STALE_VERSION.</summary>
[Trait("Category", "Sql")]
public class RoomMastersSqlTests : IAsyncLifetime
{
    private RoomMastersWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await RoomMastersWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private async Task<HccException> Fails(Func<Task> act) => await Assert.ThrowsAsync<HccException>(act);

    private async Task<RoomTypeView> TypeAsync(string code) => (await _w.Queries.RoomTypesAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.First(t => t.Code == code);

    private SaveRoomTypeRequest Edit(RoomTypeView t) => new(t.RoomTypeId, t.Code, t.Name, t.MaxAdults, t.MaxChildren, t.ExtraBedAllowed, t.BedType, t.SizeSqft, t.Amenities,
        t.Description, t.SortOrder, null, t.Version);

    private async Task<RoomMasterView> RoomAsync(string no) => (await _w.Queries.RoomsAsync(_w.PropertyId, _w.Users["housekeeper"], Ct)).Items.First(r => r.RoomNo == no);

    private static SaveRoomRequest Edit(RoomMasterView r) => new(r.RoomId, r.RoomNo, r.FloorId!.Value, r.RoomTypeId, r.Features, r.Smoking, r.ConnectingRoomNo, r.Extension, r.Notes, r.Version);

    [Fact]
    public async Task Migration_links_rooms_to_floors_and_the_floors_master_counts_rooms_by_floor_id()
    {
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.room WHERE floor_id IS NULL;", Ct));
        var rooms = (await _w.Queries.RoomsAsync(_w.PropertyId, _w.Users["housekeeper"], Ct)).Items;
        Assert.Equal(24, rooms.Count);
        Assert.Equal(("F3", "Deluxe", true), (rooms.First(r => r.RoomNo == "307").FloorCode, rooms.First(r => r.RoomNo == "307").RoomTypeCode, rooms[0].Active));
        Assert.Equal(0, await _w.DriftAsync(Ct));
        // Move 101 to F2: the floors master counts 7 / 9 (floor_id), no longer the hundreds digit.
        var r101 = await RoomAsync("101");
        await _w.Commands.SaveRoomAsync(_w.Ctx("techadmin", "m1"), Edit(r101) with { FloorId = _w.FloorIds["F2"] }, Ct);
        var floors = (await _w.FloorQueries.FloorsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items;
        Assert.Equal([7, 9, 8], floors.Select(f => f.RoomCount));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'inv' AND t.name IN ('amenity','occupancy_charge','occupancy_charge_rate');", Ct));
    }

    [Fact]
    public async Task Room_type_edit_by_Tech_admin_updates_occupancy_with_audit_history_outbox_replay_and_stale_version()
    {
        var deluxe = await TypeAsync("Deluxe");
        Assert.Equal((3, 0, 9, "Deluxe"), (deluxe.MaxAdults, deluxe.MaxChildren, deluxe.RoomCount, deluxe.Code));
        var req = Edit(deluxe) with { Name = "Deluxe King", MaxAdults = 2, MaxChildren = 1, ExtraBedAllowed = true, BedType = "King", SizeSqft = 240, Description = "Spacious" };
        var first = await _w.Commands.SaveRoomTypeAsync(_w.Ctx("techadmin", "t1"), req, Ct);
        var again = await _w.Commands.SaveRoomTypeAsync(_w.Ctx("techadmin", "t1"), req, Ct);
        Assert.True(again.Replayed);
        Assert.Equal(("Deluxe King", 3, "Deluxe"), (first.Value.Name, await _w.CountAsync($"SELECT max_occupancy FROM inv.room_type WHERE room_type_id = {deluxe.RoomTypeId};", Ct), first.Value.Code));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'MST_ROOM_TYPE' AND detail LIKE N'Master Room types: name Deluxe → Deluxe King; maxAdults 3 → 2; maxChildren 0 → 1;%';", Ct));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'MasterChanged';", Ct));
        var h = await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["owner"], RoomMasterIds.RoomTypes, deluxe.RoomTypeId, ct: Ct);
        Assert.Contains(h.Items, x => x.Field == "name" && x.OldValue == "Deluxe" && x.NewValue == "Deluxe King");
        var stale = await Fails(() => _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "t2"), req with { Name = "Other" }, Ct));
        Assert.Equal(HccErrorCode.STALE_VERSION, stale.Code);
        var used = await Fails(async () => await _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "t3"), Edit(await TypeAsync("Deluxe")) with { Code = "DLX" }, Ct));
        Assert.Equal(MasterReasons.InUse, Reason(used));
        var mgr = await Fails(async () => await _w.Commands.SaveRoomTypeAsync(_w.Ctx("manager", "t4"), Edit(await TypeAsync("Suite")) with { Name = "Grand suite" }, Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, mgr.Code);
    }

    [Fact]
    public async Task Rack_rate_is_the_tariff_schedule_owner_only_and_frozen_in_the_past()
    {
        var deluxe = await TypeAsync("Deluxe");
        Assert.Equal([new DatedAmount(300000, "2000-01-01")], deluxe.RackRates);
        var rates = new List<DatedAmount> { new(300000, "2000-01-01"), new(320000, "2026-11-01") };
        var tech = await Fails(() => _w.Commands.SaveRoomTypeAsync(_w.Ctx("techadmin", "r1"), Edit(deluxe) with { RackRates = rates }, Ct));
        Assert.Equal(HccErrorCode.FORBIDDEN, tech.Code);
        var saved = (await _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "r2"), Edit(deluxe) with { RackRates = rates }, Ct)).Value;
        Assert.Equal(rates, saved.RackRates);
        Assert.Equal(320000L, Convert.ToInt64(await _w.ScalarAsync($"SELECT rate_paise FROM inv.room_type_tariff WHERE room_type_id = {deluxe.RoomTypeId} AND effective_from = '2026-11-01';", Ct), System.Globalization.CultureInfo.InvariantCulture));
        var past = await Fails(() => _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "r3"), Edit(saved) with { RackRates = [new(310000, "2000-01-01"), new(320000, "2026-11-01")] }, Ct));
        Assert.Equal(MasterReasons.Invalid, Reason(past));
        // A Tech admin edit that leaves the rates alone passes; housekeepers read no rates, Reception (PERM-RES-001) does.
        await _w.Commands.SaveRoomTypeAsync(_w.Ctx("techadmin", "r4"), Edit(saved) with { Description = "Spacious", RackRates = rates }, Ct);
        Assert.Empty((await _w.Queries.RoomTypesAsync(_w.PropertyId, _w.Users["housekeeper"], ct: Ct)).Items[0].RackRates);
        Assert.NotEmpty((await _w.Queries.RoomTypesAsync(_w.PropertyId, _w.Users["reception"], ct: Ct)).Items[0].RackRates);
    }

    [Fact]
    public async Task New_type_and_range_add_on_a_new_floor_raise_the_counts_without_drift()
    {
        var f4 = (await _w.Floors.SaveFloorAsync(_w.Ctx("owner", "a0"), new SaveFloorRequest(null, "F4", "Fourth floor"), Ct)).Value;
        var noRate = await Fails(async () =>
        {
            var t = (await _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "a1"), new SaveRoomTypeRequest(null, "exec", "Executive", 2, 1), Ct)).Value;
            await _w.Commands.AddRoomsAsync(_w.Ctx("owner", "a2"), new AddRoomsRequest(["401"], f4.FloorId, t.RoomTypeId), Ct);
        });
        Assert.Equal(MasterReasons.Invalid, Reason(noRate));
        var exec = (await _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "a3"), new SaveRoomTypeRequest(null, "EXE", "Executive suite", 2, 1, RackRates: [new(380000, "2026-10-24")]), Ct)).Value;
        Assert.Equal(50, exec.SortOrder);   // after EXEC (40), created above without a rate
        var added = (await _w.Commands.AddRoomsAsync(_w.Ctx("techadmin", "a4"), new AddRoomsRequest(["401", "402", "403"], f4.FloorId, exec.RoomTypeId), Ct)).Value;
        Assert.Equal(["401", "402", "403"], added.Rooms.Select(r => r.RoomNo));
        Assert.All(added.Rooms, r => Assert.Equal(("F4", "EXE"), (r.FloorCode, r.RoomTypeCode)));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE detail = N'Master Rooms: added 401–403 (3 rooms) on Fourth floor · Executive suite';", Ct));
        await _w.ExecAsync($"EXEC inv.usp_rebuild_type_night_count {_w.PropertyId}, '2026-10-01', '2026-12-01';", Ct);
        Assert.Equal(3, await _w.CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {exec.RoomTypeId} AND night = '2026-10-30';", Ct));
        var deluxeBefore = await _w.SellableAsync("Deluxe", "2026-11-05", Ct);
        await _w.Commands.AddRoomsAsync(_w.Ctx("owner", "a5"), new AddRoomsRequest(["309"], _w.FloorIds["F3"], _w.Types["Deluxe"]), Ct);
        Assert.Equal(deluxeBefore + 1, await _w.SellableAsync("Deluxe", "2026-11-05", Ct));
        Assert.Equal(0, await _w.DriftAsync(Ct));
        Assert.Equal(MasterReasons.CodeTaken, Reason(await Fails(() => _w.Commands.AddRoomsAsync(_w.Ctx("owner", "a6"), new AddRoomsRequest(["308", "310"], _w.FloorIds["F3"], _w.Types["Deluxe"]), Ct))));
        await _w.Floors.RetireFloorAsync(_w.Ctx("owner", "a7"), new RetireFloorRequest(f4.FloorId, (await _w.FloorQueries.FloorsAsync(_w.PropertyId, _w.Users["owner"], ct: Ct)).Items.First(f => f.Code == "F4").Version), Ct);
        Assert.Equal(MasterReasons.Retired, Reason(await Fails(() => _w.Commands.AddRoomsAsync(_w.Ctx("owner", "a8"), new AddRoomsRequest(["404"], f4.FloorId, exec.RoomTypeId), Ct))));
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Fails(() => _w.Commands.AddRoomsAsync(_w.Ctx("reception", "a9"), new AddRoomsRequest(["111"], _w.FloorIds["F1"], _w.Types["Deluxe"]), Ct))).Code);
    }

    [Fact]
    public async Task Type_change_moves_the_counts_and_is_locked_by_bookings_renumber_likewise()
    {
        var classicBefore = await _w.SellableAsync("Classic", "2026-11-05", Ct);
        var suiteBefore = await _w.SellableAsync("Suite", "2026-11-05", Ct);
        var r105 = await RoomAsync("105");
        var moved = (await _w.Commands.SaveRoomAsync(_w.Ctx("techadmin", "c1"), Edit(r105) with { RoomTypeId = _w.Types["Suite"], Smoking = true, Extension = "2105", Notes = "Garden side" }, Ct)).Value;
        Assert.Equal(("Suite", true, "2105", "Garden side"), (moved.RoomTypeCode, moved.Smoking, moved.Extension, moved.Notes));
        Assert.Equal((classicBefore - 1, suiteBefore + 1), (await _w.SellableAsync("Classic", "2026-11-05", Ct), await _w.SellableAsync("Suite", "2026-11-05", Ct)));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.v_type_night_count_drift WHERE night >= '2026-10-24';", Ct));
        var r103 = await RoomAsync("103");
        var locked = await Fails(() => _w.Commands.SaveRoomAsync(_w.Ctx("owner", "c2"), Edit(r103) with { RoomTypeId = _w.Types["Suite"] }, Ct));
        Assert.Equal((HccErrorCode.INVALID_TRANSITION, MasterReasons.LockedByBookings), (locked.Code, Reason(locked)));
        Assert.Contains("HCC-1101", locked.Message, StringComparison.Ordinal);
        Assert.Equal(MasterReasons.LockedByBookings, Reason(await Fails(() => _w.Commands.RenumberRoomAsync(_w.Ctx("owner", "c3"), new RenumberRoomRequest(r103.RoomId, "113", r103.Version), Ct))));
        var renum = (await _w.Commands.RenumberRoomAsync(_w.Ctx("owner", "c4"), new RenumberRoomRequest(moved.RoomId, "115", moved.Version), Ct)).Value;
        Assert.Equal("115", renum.RoomNo);
        Assert.Equal(MasterReasons.CodeTaken, Reason(await Fails(() => _w.Commands.RenumberRoomAsync(_w.Ctx("owner", "c5"), new RenumberRoomRequest(renum.RoomId, "106", renum.Version), Ct))));
        Assert.Equal(HccErrorCode.STALE_VERSION, (await Fails(() => _w.Commands.SaveRoomAsync(_w.Ctx("owner", "c6"), Edit(moved) with { Notes = "x" }, Ct))).Code);
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(async () => await _w.Commands.SaveRoomAsync(_w.Ctx("owner", "c7"), Edit(await RoomAsync("106")) with { ConnectingRoomNo = "999" }, Ct))));
    }

    [Fact]
    public async Task Retire_places_an_inventory_block_refused_while_booked_and_reactivate_releases_it_delete_only_unused()
    {
        var r106 = await RoomAsync("106");
        var before = await _w.SellableAsync("Suite", "2026-11-05", Ct);
        var retired = (await _w.Commands.RetireRoomAsync(_w.Ctx("owner", "x1"), new RoomIdRequest(r106.RoomId, r106.Version), Ct)).Value;
        Assert.Equal((false, "2026-10-24"), (retired.Active, retired.RetiredOn));
        Assert.Equal(before - 1, await _w.SellableAsync("Suite", "2026-11-05", Ct));
        Assert.Equal(1, await _w.CountAsync($"SELECT COUNT(*) FROM inv.room_block WHERE room_id = {r106.RoomId} AND active = 1 AND reason = N'Retired room' AND to_night IS NULL;", Ct));
        Assert.Equal(MasterReasons.Retired, Reason(await Fails(() => _w.Commands.SaveRoomAsync(_w.Ctx("owner", "x2"), Edit(retired) with { Notes = "x" }, Ct))));
        var back = (await _w.Commands.ReactivateRoomAsync(_w.Ctx("techadmin", "x3"), new RoomIdRequest(r106.RoomId, retired.Version), Ct)).Value;
        Assert.True(back.Active);
        Assert.Equal(before, await _w.SellableAsync("Suite", "2026-11-05", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.v_type_night_count_drift WHERE night >= '2026-10-24';", Ct));
        var r103 = await RoomAsync("103");
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(() => _w.Commands.RetireRoomAsync(_w.Ctx("owner", "x4"), new RoomIdRequest(r103.RoomId, r103.Version), Ct))));
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(() => _w.Commands.DeleteRoomAsync(_w.Ctx("owner", "x5"), new RoomIdRequest(r103.RoomId, r103.Version), Ct))));
        var fresh = (await _w.Commands.AddRoomsAsync(_w.Ctx("owner", "x6"), new AddRoomsRequest(["109"], _w.FloorIds["F1"], _w.Types["Classic"]), Ct)).Value.Rooms[0];
        var del = (await _w.Commands.DeleteRoomAsync(_w.Ctx("owner", "x7"), new RoomIdRequest(fresh.RoomId, fresh.Version), Ct)).Value;
        Assert.True(del.Deleted);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.room WHERE room_no = '109';", Ct));
        Assert.Equal(0, await _w.DriftAsync(Ct));
    }

    [Fact]
    public async Task Room_type_retire_needs_no_rooms_in_service_reactivate_and_delete_an_unused_type()
    {
        var suite = await TypeAsync("Suite");
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(() => _w.Commands.RetireRoomTypeAsync(_w.Ctx("owner", "y1"), new RoomTypeIdRequest(suite.RoomTypeId, suite.Version), Ct))));
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(() => _w.Commands.DeleteRoomTypeAsync(_w.Ctx("owner", "y2"), new RoomTypeIdRequest(suite.RoomTypeId, suite.Version), Ct))));
        var spare = (await _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "y3"), new SaveRoomTypeRequest(null, "SPARE", "Spare", 2, RackRates: [new(150000, "2026-11-01")]), Ct)).Value;
        var off = (await _w.Commands.RetireRoomTypeAsync(_w.Ctx("techadmin", "y4"), new RoomTypeIdRequest(spare.RoomTypeId, spare.Version), Ct)).Value;
        Assert.False(off.Active);
        Assert.Single((await _w.Queries.RoomTypesAsync(_w.PropertyId, _w.Users["owner"], status: "retired", ct: Ct)).Items);
        var on = (await _w.Commands.ReactivateRoomTypeAsync(_w.Ctx("techadmin", "y5"), new RoomTypeIdRequest(spare.RoomTypeId, await _w.TypeVersionAsync(spare.RoomTypeId, Ct)), Ct)).Value;
        Assert.True(on.Active);
        Assert.True((await _w.Commands.DeleteRoomTypeAsync(_w.Ctx("owner", "y6"), new RoomTypeIdRequest(spare.RoomTypeId, await _w.TypeVersionAsync(spare.RoomTypeId, Ct)), Ct)).Value.Deleted);
        var order = await _w.Commands.ReorderRoomTypesAsync(_w.Ctx("techadmin", "y7"), new ReorderRoomTypesRequest([_w.Types["Suite"], _w.Types["Classic"], _w.Types["Deluxe"]]), Ct);
        Assert.Equal("Suite,Classic,Deluxe", order.Value.Code);
    }

    [Fact]
    public async Task Amenities_save_use_retire_and_delete_rules()
    {
        var wifi = (await _w.Commands.SaveAmenityAsync(_w.Ctx("techadmin", "m1"), new SaveAmenityRequest(null, "wifi", "Wi-Fi", "wifi"), Ct)).Value;
        var desk = (await _w.Commands.SaveAmenityAsync(_w.Ctx("techadmin", "m2"), new SaveAmenityRequest(null, "DESK", "Work desk", "briefcase", false), Ct)).Value;
        Assert.Equal(("WIFI", 10, 20), (wifi.Code, wifi.SortOrder, desk.SortOrder));
        Assert.Equal(MasterReasons.CodeTaken, Reason(await Fails(() => _w.Commands.SaveAmenityAsync(_w.Ctx("owner", "m3"), new SaveAmenityRequest(null, "WIFI", "Wireless"), Ct))));
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(() => _w.Commands.SaveAmenityAsync(_w.Ctx("owner", "m4"), new SaveAmenityRequest(null, "POOL", "Pool", "rocket"), Ct))));
        await _w.Commands.SaveRoomTypeAsync(_w.Ctx("techadmin", "m5"), Edit(await TypeAsync("Deluxe")) with { Amenities = ["WIFI"] }, Ct);
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(async () => await _w.Commands.SaveRoomTypeAsync(_w.Ctx("techadmin", "m6"), Edit(await TypeAsync("Suite")) with { Amenities = ["SPA"] }, Ct))));
        Assert.Equal(MasterReasons.InUse, Reason(await Fails(() => _w.Commands.DeleteAmenityAsync(_w.Ctx("owner", "m7"), new AmenityIdRequest(wifi.AmenityId, wifi.Version), Ct))));
        var gone = await _w.Commands.DeleteAmenityAsync(_w.Ctx("owner", "m8"), new AmenityIdRequest(desk.AmenityId, desk.Version), Ct);
        Assert.True(gone.Value.Deleted);
        var off = await _w.Commands.RetireAmenityAsync(_w.Ctx("owner", "m9"), new AmenityIdRequest(wifi.AmenityId, wifi.Version), Ct);
        Assert.False(off.Value.Active);
        Assert.Equal(["WIFI"], (await TypeAsync("Deluxe")).Amenities);   // a retired amenity stays on the type
        Assert.Single((await _w.Queries.AmenitiesAsync(_w.PropertyId, _w.Users["housekeeper"], ct: Ct)).Items);
    }

    [Fact]
    public async Task Occupancy_charges_are_an_Owner_money_list_with_dated_amounts_and_one_row_per_type()
    {
        var deluxe = await TypeAsync("Deluxe");
        await _w.Commands.SaveRoomTypeAsync(_w.Ctx("owner", "o0"), Edit(deluxe) with { ExtraBedAllowed = true }, Ct);
        var req = new SaveOccupancyChargeRequest(null, "X-DLX", "Deluxe extras", deluxe.RoomTypeId, 6, 12, [new(80000, "2026-10-24")], [new(40000, "2026-10-24")], [new(100000, "2026-10-24")]);
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Fails(() => _w.Commands.SaveOccupancyChargeAsync(_w.Ctx("techadmin", "o1"), req, Ct))).Code);
        var saved = (await _w.Commands.SaveOccupancyChargeAsync(_w.Ctx("owner", "o2"), req, Ct)).Value;
        Assert.Equal(("Deluxe", 12), (saved.RoomTypeCode, saved.ChildAgeTo));
        Assert.Equal([new DatedAmount(100000, "2026-10-24")], saved.ExtraBed);
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(() => _w.Commands.SaveOccupancyChargeAsync(_w.Ctx("owner", "o3"), req with { Code = "X2" }, Ct))));
        var classic = await TypeAsync("Classic");
        Assert.Equal(MasterReasons.Invalid, Reason(await Fails(() => _w.Commands.SaveOccupancyChargeAsync(_w.Ctx("owner", "o4"), req with { Code = "X-CL", RoomTypeId = classic.RoomTypeId }, Ct))));
        var edited = (await _w.Commands.SaveOccupancyChargeAsync(_w.Ctx("owner", "o5"), req with { ChargeId = saved.ChargeId, ExpectedVersion = saved.Version,
            ExtraAdult = [new(80000, "2026-10-24"), new(90000, "2026-12-01")] }, Ct)).Value;
        Assert.Equal(2, edited.ExtraAdult.Count);
        var hk = (await _w.Queries.OccupancyChargesAsync(_w.PropertyId, _w.Users["housekeeper"], ct: Ct)).Items.Single();
        Assert.Equal((0, 0, 0), (hk.ExtraAdult.Count, hk.ExtraChild.Count, hk.ExtraBed.Count));   // DEC-061: no amounts
        Assert.Equal(HccErrorCode.FORBIDDEN, (await Fails(() => _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["techadmin"], RoomMasterIds.OccupancyCharges, ct: Ct))).Code);
        Assert.True((await _w.Queries.HistoryAsync(_w.PropertyId, _w.Users["owner"], RoomMasterIds.OccupancyCharges, saved.ChargeId, ct: Ct)).Total >= 8);
        var off = await _w.Commands.RetireOccupancyChargeAsync(_w.Ctx("owner", "o6"), new OccupancyChargeIdRequest(saved.ChargeId, edited.Version), Ct);
        Assert.False(off.Value.Active);
    }
}
