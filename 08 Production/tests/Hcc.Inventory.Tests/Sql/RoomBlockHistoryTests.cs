using System.Globalization;
using Hcc.Application.Inventory;
using Hcc.Contracts.Inventory;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Inventory.Tests.Sql;

/// <summary>
/// W3-8 room-block history: a released or edited block keeps its past, so "as of" availability / room status / maintained counts for
/// past business dates never change after a release or edit. Each test uses its own room; the business date is moved to 2026-11-10.
/// </summary>
[Trait("Category", "Sql")]
public sealed class RoomBlockHistoryTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private P2Services S => f.Services;

    private async Task SetBusinessDateAsync(string date)
    {
        await f.Db.ExecAsync($"UPDATE inv.property SET business_date = '{date}' WHERE property_id = 1;");
        await f.Db.ExecAsync("EXEC inv.usp_rebuild_type_night_count 1, '2026-11-01', '2026-11-30';");
    }

    private async Task<long> PlaceAsync(string room, string from, string to, string source = "Inventory", long? sourceRef = null, int principal = FakeAccessPolicy.Manager)
    {
        long id = 0;
        await S.Runner.RunAsync(P2Services.Ctx(principal, "TestBlock"), new { room, from, to, k = Guid.NewGuid() }, async (tx, ct) =>
        {
            id = await S.RoomBlocks.PlaceAsync(tx, 1, f.RoomIds[room], DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture),
                "Repair (fictional)", source, sourceRef, ct);
            await tx.AuditAsync("TestBlock", room, "place", ct);
            return new Done(true);
        }, Ct);
        return id;
    }

    private async Task ReleaseAsync(long blockId, int principal = FakeAccessPolicy.Manager)
        => await S.Runner.RunAsync(P2Services.Ctx(principal, "TestUnblock"), new { blockId, k = Guid.NewGuid() }, async (tx, ct) =>
        {
            await S.RoomBlocks.RemoveAsync(tx, 1, blockId, ct);
            await tx.AuditAsync("TestUnblock", blockId.ToString(CultureInfo.InvariantCulture), "release", ct);
            return new Done(true);
        }, Ct);

    private async Task<long?> EditAsync(long blockId, string? from, string? to, string reason)
    {
        long? id = null;
        await S.Runner.RunAsync(P2Services.Ctx(FakeAccessPolicy.Manager, "TestEdit"), new { blockId, from, to, k = Guid.NewGuid() }, async (tx, ct) =>
        {
            id = await S.RoomBlocks.EditAsync(tx, 1, blockId, from is null ? null : DateOnly.Parse(from, CultureInfo.InvariantCulture),
                to is null ? null : DateOnly.Parse(to, CultureInfo.InvariantCulture), reason, ct);
            await tx.AuditAsync("TestEdit", blockId.ToString(CultureInfo.InvariantCulture), "edit", ct);
            return new Done(true);
        }, Ct);
        return id;
    }

    private async Task<bool> BlockedAsync(string room, string a, string d, string? asOf = null)
    {
        var r = (await S.Inventory.GetAvailabilityAsync(1, FakeAccessPolicy.Reception, new AvailabilityQuery(a, d, 1, 0, null, null, null, asOf), Ct)).Rooms.Single(x => x.RoomNo == room);
        return r.Reasons.Contains(AvailabilityReasons.Blocked);
    }

    private async Task<bool> StatusBlockedAsync(string room, string asOf)
        => (await S.BlockQueries.GetRoomStatusAsync(1, FakeAccessPolicy.Reception, asOf, Ct)).Rooms.Single(x => x.RoomNo == room).Blocked;

    [Fact]
    public async Task Release_today_does_not_change_yesterdays_snapshot()
    {
        await SetBusinessDateAsync("2026-11-04");
        var id = await PlaceAsync("101", "2026-11-05", "2026-11-15");  // created on 2026-11-04
        await SetBusinessDateAsync("2026-11-10");
        Assert.True(await BlockedAsync("101", "2026-11-09", "2026-11-10", "2026-11-09"));
        Assert.True(await StatusBlockedAsync("101", "2026-11-09"));
        var classic = f.TypeIds["Classic"];
        var sellableYesterday = await f.Db.CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {classic} AND night = '2026-11-09';", Ct);

        await ReleaseAsync(id);  // released on business date 2026-11-10

        Assert.True(await BlockedAsync("101", "2026-11-09", "2026-11-10", "2026-11-09"));  // yesterday as-of: still blocked
        Assert.True(await BlockedAsync("101", "2026-11-09", "2026-11-10"));                // yesterday, current knowledge: still blocked
        Assert.True(await StatusBlockedAsync("101", "2026-11-09"));
        Assert.True(await StatusBlockedAsync("101", "2026-11-05"));
        Assert.False(await StatusBlockedAsync("101", "2026-11-10"));                       // today: released
        Assert.False(await BlockedAsync("101", "2026-11-10", "2026-11-12"));
        Assert.Equal(sellableYesterday, await f.Db.CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {classic} AND night = '2026-11-09';", Ct));
        Assert.Equal(sellableYesterday + 1, await f.Db.CountAsync($"SELECT sellable FROM inv.type_night_count WHERE room_type_id = {classic} AND night = '2026-11-11';", Ct));
        Assert.Equal(0, await S.DriftAsync());
        Assert.Equal("2026-11-10", Convert.ToDateTime(await f.Db.ScalarAsync($"SELECT released_on FROM inv.room_block WHERE room_block_id = {id};", Ct), CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        // A later release does not rewrite an earlier as-of: as of 2026-11-12 (after a later business date) the block is gone for 11-12 but kept before.
        await SetBusinessDateAsync("2026-11-12");
        Assert.True(await StatusBlockedAsync("101", "2026-11-09"));
        Assert.False(await StatusBlockedAsync("101", "2026-11-11"));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Edit_keeps_the_past_and_history_records_actor_and_time()
    {
        await SetBusinessDateAsync("2026-11-04");
        var id = await PlaceAsync("102", "2026-11-05", "2026-11-20", "Maintenance", 4242, FakeAccessPolicy.Manager);
        await SetBusinessDateAsync("2026-11-10");
        var next = await EditAsync(id, null, "2026-11-12", "Shortened (fictional)");
        Assert.NotNull(next);
        await SetBusinessDateAsync("2026-11-13");

        Assert.True(await StatusBlockedAsync("102", "2026-11-07"));   // past kept
        Assert.True(await StatusBlockedAsync("102", "2026-11-11"));   // new window
        Assert.False(await StatusBlockedAsync("102", "2026-11-12"));  // shortened away
        Assert.False(await BlockedAsync("102", "2026-11-13", "2026-11-14"));
        Assert.Equal(0, await S.DriftAsync());

        var hist = await S.BlockQueries.GetHistoryAsync(1, FakeAccessPolicy.Reception, f.RoomIds["102"], null, Ct);
        Assert.Equal(["Created", "Edited"], hist.Select(h => h.Event));
        Assert.All(hist, h => Assert.Equal(FakeAccessPolicy.Manager, h.ActorId));
        Assert.Equal("2026-11-10", hist[1].EffectiveOn);
        Assert.Equal(next, hist[1].RelatedBlockId);
        Assert.Equal("2026-11-10", hist[1].FromNight);
        Assert.Equal("2026-11-12", hist[1].ToNight);
        Assert.StartsWith("2026-10-2", hist[0].EventAt, StringComparison.Ordinal);  // TestClock (UTC) from the fixture

        // Releasing by source (the Maintenance path) releases the successor, keeps the history.
        await S.Runner.RunAsync(P2Services.Ctx(FakeAccessPolicy.Manager, "TestUnblock"), new { k = Guid.NewGuid() }, async (tx, ct) =>
        {
            await S.RoomBlocks.RemoveBySourceAsync(tx, 1, "Maintenance", 4242, ct);
            await tx.AuditAsync("TestUnblock", "102", "release", ct);
            return new Done(true);
        }, Ct);
        Assert.True(await StatusBlockedAsync("102", "2026-11-11"));  // released on 11-13: the past still shows the block
        Assert.True(await StatusBlockedAsync("102", "2026-11-07"));
        hist = await S.BlockQueries.GetHistoryAsync(1, FakeAccessPolicy.Reception, null, next, Ct);
        Assert.Equal(["Released"], hist.Select(h => h.Event));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Front_desk_room_status_carries_clean_dirty_and_the_block_window_for_today_only()
    {
        // D5: Reception has no PERM-HK-001 (DEC-063), so its room board reads GET /rooms/status (PERM-RES-001): clean/dirty + block window.
        await SetBusinessDateAsync("2026-11-10");
        await f.Db.ExecAsync("UPDATE inv.room SET hk_status = 'Dirty' WHERE room_no = '104' AND property_id = 1;", Ct);
        try
        {
            await PlaceAsync("104", "2026-11-09", "2026-11-14");
            var today = (await S.BlockQueries.GetRoomStatusAsync(1, FakeAccessPolicy.Reception, null, Ct)).Rooms.Single(x => x.RoomNo == "104");
            Assert.Equal(("Dirty", true, "2026-11-09", "2026-11-14"), (today.Hk, today.Blocked, today.BlockFrom, today.BlockTo));
            var free = (await S.BlockQueries.GetRoomStatusAsync(1, FakeAccessPolicy.Reception, null, Ct)).Rooms.Single(x => x.RoomNo == "103");
            Assert.Equal((false, (string?)null, (string?)null), (free.Blocked, free.BlockFrom, free.BlockTo));
            Assert.NotNull(free.Hk);
            var past = (await S.BlockQueries.GetRoomStatusAsync(1, FakeAccessPolicy.Reception, "2026-11-09", Ct)).Rooms.Single(x => x.RoomNo == "104");
            Assert.Null(past.Hk); // no history of clean/dirty: never reported for a past date
            Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.BlockQueries.GetRoomStatusAsync(1, FakeAccessPolicy.Housekeeper, null, Ct)));
        }
        finally
        {
            await f.Db.ExecAsync("UPDATE inv.room SET hk_status = 'Ready' WHERE room_no = '104' AND property_id = 1;", Ct);
        }
    }

    [Fact]
    public async Task A_block_created_later_is_invisible_as_of_an_earlier_date_and_permissions_are_checked()
    {
        await SetBusinessDateAsync("2026-11-10");
        await PlaceAsync("105", "2026-11-01", "2026-11-14");  // created on 2026-11-10
        Assert.False(await StatusBlockedAsync("105", "2026-11-05"));  // as of 11-05 the block did not exist yet
        Assert.True(await StatusBlockedAsync("105", "2026-11-10"));
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.BlockQueries.GetRoomStatusAsync(1, FakeAccessPolicy.Housekeeper, "2026-11-05", Ct)));
        Assert.Equal("FORBIDDEN", await Outcome.CodeAsync(() => S.BlockQueries.GetHistoryAsync(1, FakeAccessPolicy.Housekeeper, null, null, Ct)));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.BlockQueries.GetRoomStatusAsync(1, FakeAccessPolicy.Reception, "2026-11-11", Ct)));
        Assert.Equal("VALIDATION", await Outcome.CodeAsync(() => S.Inventory.GetAvailabilityAsync(1, FakeAccessPolicy.Reception,
            new AvailabilityQuery("2026-11-09", "2026-11-10", 1, 0, null, null, null, "2026-12-31"), Ct)));
    }
}
