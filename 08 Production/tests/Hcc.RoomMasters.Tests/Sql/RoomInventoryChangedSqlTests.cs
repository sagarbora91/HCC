using System.Globalization;
using Hcc.Contracts;
using Hcc.Contracts.RoomMasters;
using Hcc.Domain.Errors;
using Hcc.RoomMasters.Tests.Harness;

namespace Hcc.RoomMasters.Tests.Sql;

/// <summary>
/// A08-03 follow-up (Wave D): adding, deleting or re-typing a room in Master configuration changes the room type's availability, so it
/// publishes InventoryChanged {propertyId, roomTypeId, from, to} in its own transaction — from the business date (2026-10-24) for one year,
/// like an open-ended room block, because nights without a count row change too. One event per room type per command (a range add of
/// several rooms is one event); a refused command publishes nothing. Compiles with Wave D4's InventoryChangePublisher.
/// </summary>
[Trait("Category", "Sql")]
public class RoomInventoryChangedSqlTests : IAsyncLifetime
{
    private RoomMastersWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await RoomMastersWorld.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<long> MarkAsync() => Convert.ToInt64(await _w.ScalarAsync("SELECT ISNULL(MAX(outbox_id), 0) FROM plat.outbox;", Ct), CultureInfo.InvariantCulture);

    /// <summary>InventoryChanged events after <paramref name="mark"/> for one room type: "from>to@principal" in publish order.</summary>
    private async Task<string[]> EventsAsync(long mark, string type)
        => ((await _w.ScalarAsync($"""
            SELECT STRING_AGG(CONCAT(JSON_VALUE(payload, '$.from'), '>', JSON_VALUE(payload, '$.to'), '@', principal_id), '|') WITHIN GROUP (ORDER BY outbox_id)
            FROM plat.outbox WHERE event_type = 'InventoryChanged' AND outbox_id > {mark} AND aggregate_id = {_w.Types[type]}
              AND JSON_VALUE(payload, '$.roomTypeId') = '{_w.Types[type]}' AND JSON_VALUE(payload, '$.propertyId') = '{_w.PropertyId}' AND property_id = {_w.PropertyId};
            """, Ct) as string) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);

    private async Task<RoomMasterView> RoomAsync(string no) => (await _w.Queries.RoomsAsync(_w.PropertyId, _w.Users["owner"], Ct)).Items.First(r => r.RoomNo == no);

    private static SaveRoomRequest Edit(RoomMasterView r) => new(r.RoomId, r.RoomNo, r.FloorId!.Value, r.RoomTypeId, r.Features, r.Smoking, r.ConnectingRoomNo, r.Extension, r.Notes, r.Version);

    private string Year(string who) => "2026-10-24>2027-10-24@" + _w.Users[who].ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task Adding_rooms_publishes_one_event_for_the_type_from_the_business_date_for_a_year_and_deleting_one_publishes_another()
    {
        var mark = await MarkAsync();
        var added = (await _w.Commands.AddRoomsAsync(_w.Ctx("owner", "e1"), new AddRoomsRequest(["309", "310"], _w.FloorIds["F3"], _w.Types["Deluxe"]), Ct)).Value;
        Assert.Equal([Year("owner")], await EventsAsync(mark, "Deluxe"));   // two rooms, one transaction: one event
        Assert.Empty(await EventsAsync(mark, "Classic"));

        mark = await MarkAsync();
        var r310 = added.Rooms.Single(r => r.RoomNo == "310");
        Assert.True((await _w.Commands.DeleteRoomAsync(_w.Ctx("techadmin", "e2"), new RoomIdRequest(r310.RoomId, r310.Version), Ct)).Value.Deleted);
        Assert.Equal([Year("techadmin")], await EventsAsync(mark, "Deluxe"));
        Assert.Equal(0, await _w.DriftAsync(Ct));
    }

    [Fact]
    public async Task A_type_change_publishes_one_event_for_each_of_the_two_types()
    {
        var mark = await MarkAsync();
        await _w.Commands.SaveRoomAsync(_w.Ctx("techadmin", "e3"), Edit(await RoomAsync("105")) with { RoomTypeId = _w.Types["Suite"] }, Ct);
        Assert.Equal([Year("techadmin")], await EventsAsync(mark, "Classic"));
        Assert.Equal([Year("techadmin")], await EventsAsync(mark, "Suite"));
        Assert.Empty(await EventsAsync(mark, "Deluxe"));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.v_type_night_count_drift WHERE night >= '2026-10-24';", Ct));   // the move counts from today
    }

    [Fact]
    public async Task A_refused_delete_or_add_publishes_nothing()
    {
        var r101 = await RoomAsync("101");
        var mark = await MarkAsync();
        await Assert.ThrowsAsync<HccException>(() => _w.Commands.DeleteRoomAsync(_w.Ctx("owner", "e4"), new RoomIdRequest(r101.RoomId, Convert.ToBase64String(new byte[8])), Ct));   // stale version
        await Assert.ThrowsAsync<HccException>(() => _w.Commands.AddRoomsAsync(_w.Ctx("owner", "e5"), new AddRoomsRequest(["311", "101"], _w.FloorIds["F3"], _w.Types["Classic"]), Ct)); // 101 taken
        Assert.Empty(await EventsAsync(mark, "Classic"));
    }
}
