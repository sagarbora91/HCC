using System.Globalization;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Inventory.Tests.Harness;

namespace Hcc.Inventory.Tests.Sql;

/// <summary>
/// A08-03 on the BASE fixture: every availability change publishes InventoryChanged {propertyId, roomTypeId, from, to} in its own transaction —
/// hold expiry (the sweep, outside the command runner, stamped with the user whose command ran it), room and maintenance blocks (their whole
/// window from the business date, also on nights without a count row yet; open-ended = one year), group blocks and bookings. Each test uses its
/// own rooms and dates (shared class fixture).
/// </summary>
[Trait("Category", "Sql")]
public sealed class InventoryChangedTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private P2Services S => f.Services;

    private async Task<long> MarkAsync() => Convert.ToInt64(await f.Db.ScalarAsync("SELECT ISNULL(MAX(outbox_id), 0) FROM plat.outbox;", Ct), CultureInfo.InvariantCulture);

    /// <summary>InventoryChanged events after <paramref name="mark"/> for one room type: "from>to@principal" in publish order.</summary>
    private async Task<string[]> EventsAsync(long mark, string type)
        => ((await f.Db.ScalarAsync($"""
            SELECT STRING_AGG(CONCAT(JSON_VALUE(payload, '$.from'), '>', JSON_VALUE(payload, '$.to'), '@', principal_id), '|') WITHIN GROUP (ORDER BY outbox_id)
            FROM plat.outbox WHERE event_type = 'InventoryChanged' AND outbox_id > {mark} AND aggregate_id = {f.TypeIds[type]}
              AND JSON_VALUE(payload, '$.roomTypeId') = '{f.TypeIds[type]}' AND JSON_VALUE(payload, '$.propertyId') = '1' AND property_id = 1;
            """, Ct) as string) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);

    private async Task<ReservationResult> BookAsync(string room, string a, string d, bool hold = false)
    {
        var q = (await S.Inventory.QuoteStayAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "QuoteStay"), new QuoteStayRequest(a, d, 1, 0, f.RoomIds[room]), Ct)).Value;
        return (await S.Booking.CreateAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "CreateReservation"),
            new CreateReservationRequest(q.QuoteId, q.Version, new GuestInput("Test Guest", "+91 90000 12345"), f.RoomIds[room], hold), Ct)).Value;
    }

    private Task<Hcc.Application.Commands.CommandResult<Done>> InCommandAsync(string op, Func<Hcc.Application.Commands.ICommandTransaction, CancellationToken, Task> work)
        => S.Runner.RunAsync(P2Services.Ctx(FakeAccessPolicy.Manager, op), new { op, at = Guid.NewGuid() }, async (tx, ct) =>
        {
            await work(tx, ct);
            await tx.AuditAsync(op, "test", "A08-03 test", ct);
            return new Done(true);
        }, Ct);

    [Fact]
    public async Task A_booking_and_its_hold_expiry_each_publish_one_event_the_sweep_in_its_own_transaction()
    {
        var mark = await MarkAsync();
        var h = await BookAsync("108", "2026-11-12", "2026-11-14", hold: true);
        Assert.Equal(["2026-11-12>2026-11-14@3"], await EventsAsync(mark, "Classic"));

        var start = S.Clock.Now;
        try
        {
            S.Clock.Now = start.AddMinutes(121);
            mark = await MarkAsync();
            await S.Store.SweepAsync(1, FakeAccessPolicy.Manager, S.Clock.Now.UtcDateTime, Ct);
            Assert.Equal("Expired", (string)(await f.Db.ScalarAsync($"SELECT status FROM inv.reservation WHERE reservation_id = {h.ReservationId};", Ct))!);
            // The lapsed hold's nights come back, stamped with the user whose command ran the sweep (no command transaction of its own).
            Assert.Contains("2026-11-12>2026-11-14@2", await EventsAsync(mark, "Classic"));
            Assert.Single(await EventsAsync(mark, "Classic"), e => e.StartsWith("2026-11-12>", StringComparison.Ordinal));
        }
        finally
        {
            S.Clock.Now = start;
        }

        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Room_blocks_publish_their_window_from_the_business_date_even_without_count_rows()
    {
        Assert.Equal(0, await f.Db.CountAsync($"SELECT COUNT(*) FROM inv.type_night_count WHERE room_type_id = {f.TypeIds["Deluxe"]} AND night >= '2027-03-01' AND night < '2027-03-05';", Ct));
        var mark = await MarkAsync();
        long id = 0;
        await InCommandAsync("TestRoomBlock", async (tx, ct) => id = await S.RoomBlocks.PlaceAsync(tx, 1, f.RoomIds["307"], new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 5), "AC repair (fictional)", "Maintenance", 4001, ct));
        Assert.Equal(["2027-03-01>2027-03-05@2"], await EventsAsync(mark, "Deluxe"));

        mark = await MarkAsync();
        await InCommandAsync("TestRoomUnblock", (tx, ct) => S.RoomBlocks.RemoveAsync(tx, 1, id, ct));
        Assert.Equal(["2027-03-01>2027-03-05@2"], await EventsAsync(mark, "Deluxe"));

        // A block that started before today reports from today; an open-ended one reports one year.
        mark = await MarkAsync();
        await InCommandAsync("TestRoomBlock", async (tx, ct) => id = await S.RoomBlocks.PlaceAsync(tx, 1, f.RoomIds["304"], new DateOnly(2027, 5, 1), null, "Renovation (fictional)", "Inventory", null, ct));
        Assert.Equal(["2027-05-01>2028-04-30@2"], await EventsAsync(mark, "Deluxe"));
        mark = await MarkAsync();
        await InCommandAsync("TestRoomBlockEdit", (tx, ct) => S.RoomBlocks.EditAsync(tx, 1, id, new DateOnly(2026, 10, 1), new DateOnly(2027, 6, 1), "Renovation, shorter (fictional)", ct));
        Assert.Equal(["2026-10-24>2028-04-30@2"], await EventsAsync(mark, "Deluxe")); // old window released + new one placed: one event, their hull
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task Group_block_creation_and_release_publish_one_event_each()
    {
        var mark = await MarkAsync();
        var b = (await S.Inventory.CreateGroupBlockAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "CreateGroupBlock"), new CreateGroupBlockRequest("GB-A0803", "OTA test group",
            "2027-01-10", "2027-01-12", "2027-01-05", [new GroupBlockAllotment(f.TypeIds["Suite"], 1, 400000), new GroupBlockAllotment(f.TypeIds["Deluxe"], 2, 300000)]), Ct)).Value;
        Assert.Equal(["2027-01-10>2027-01-12@3"], await EventsAsync(mark, "Suite"));
        Assert.Equal(["2027-01-10>2027-01-12@3"], await EventsAsync(mark, "Deluxe"));

        mark = await MarkAsync();
        await S.Inventory.ReleaseGroupBlockAsync(P2Services.Ctx(FakeAccessPolicy.Reception, "ReleaseGroupBlock"), new ReleaseGroupBlockRequest(b.GroupBlockId, b.Version), Ct);
        Assert.Equal(["2027-01-10>2027-01-12@3"], await EventsAsync(mark, "Suite"));
        Assert.Equal(["2027-01-10>2027-01-12@3"], await EventsAsync(mark, "Deluxe"));
        Assert.Equal(0, await S.DriftAsync());
    }

    [Fact]
    public async Task A_refused_change_publishes_nothing()
    {
        await BookAsync("305", "2027-02-01", "2027-02-03");
        var mark = await MarkAsync();
        // Room 305 has bookings in the window: the block is refused and the transaction (with any event) rolls back.
        Assert.Equal("NOT_AVAILABLE", await Outcome.CodeAsync(() => InCommandAsync("TestRoomBlock",
            (tx, ct) => S.RoomBlocks.PlaceAsync(tx, 1, f.RoomIds["305"], new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10), "Leak (fictional)", "Maintenance", 4002, ct))));
        Assert.Empty(await EventsAsync(mark, "Classic"));
    }
}
