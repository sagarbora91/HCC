using System.Globalization;
using Hcc.Contracts.Stays;
using Hcc.Infrastructure.SqlServer.Ota;
using Hcc.Stays.Tests.Harness;

namespace Hcc.Stays.Tests.Sql;

/// <summary>
/// A08-03: extend, shorten, early departure and room move change the type-night counts through the allocator, and each publishes exactly one
/// InventoryChanged per room type it touched (the release and re-add runs of one command widen the same event), in the command's transaction,
/// stamped with its principal. The OTA projector turns it into an automatic availability push for those nights (from the business date) to
/// every Connected channel mapping the type, delivered by the existing ChannelPushQueued handling. BASE (24 Oct 2026): 101 Classic and
/// 104 Deluxe in house 23 → 26 Oct.
/// </summary>
[Trait("Category", "Sql")]
public class InventoryChangedSqlTests : IAsyncLifetime
{
    private StayChangeWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await StayChangeWorld.CreateAsync(ct: Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task<string> VerAsync(string booking) => await _w.Seed.StayVersionAsync(_w.Stay(booking), Ct);

    private async Task<long> MarkAsync() => Convert.ToInt64(await _w.Db.ScalarAsync("SELECT ISNULL(MAX(outbox_id), 0) FROM plat.outbox;", Ct), CultureInfo.InvariantCulture);

    /// <summary>InventoryChanged events after <paramref name="mark"/>: "type from>to" in publish order.</summary>
    private async Task<string[]> EventsAfterAsync(long mark)
        => ((await _w.Db.StringAsync($"""
            SELECT STRING_AGG(CONCAT(t.code, ' ', JSON_VALUE(o.payload, '$.from'), '>', JSON_VALUE(o.payload, '$.to')), '|') WITHIN GROUP (ORDER BY o.outbox_id)
            FROM plat.outbox o JOIN inv.room_type t ON t.room_type_id = o.aggregate_id
            WHERE o.event_type = 'InventoryChanged' AND o.outbox_id > {mark};
            """, Ct)) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);

    private async Task ExtendAsync(string booking, string to, string key)
        => await _w.Changes.ExtendStayAsync(_w.Ctx(_w.Reception, key), new ExtendStayRequest(_w.Stay(booking), await VerAsync(booking), to), Ct);

    [Fact]
    public async Task Extend_publishes_one_event_for_the_added_nights_stamped_with_the_command_principal()
    {
        var mark = await MarkAsync();
        await ExtendAsync("HCC-1041", "2026-10-28", "ext");
        Assert.Equal(["Classic 2026-10-26>2026-10-28"], await EventsAfterAsync(mark));
        Assert.Equal(1, await _w.Db.CountAsync($"""
            SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'InventoryChanged' AND outbox_id > {mark} AND property_id = {_w.W.PropertyId}
              AND principal_id = {_w.Reception} AND JSON_VALUE(payload, '$.propertyId') = '{_w.W.PropertyId}';
            """, Ct));
        // Same transaction as the stay change: the StayExtended event and the InventoryChanged event commit together.
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'StayExtended' AND outbox_id > {mark};", Ct));
    }

    [Fact]
    public async Task Shorten_and_early_departure_publish_one_event_each_covering_the_released_run()
    {
        await ExtendAsync("HCC-1043", "2026-10-27", "pre");
        var mark = await MarkAsync();
        await _w.Changes.ShortenStayAsync(_w.Ctx(_w.Reception, "sh"), new ShortenStayRequest(_w.Stay("HCC-1043"), await VerAsync("HCC-1043"), "2026-10-26", "Back to the booked date"), Ct);
        // The allocator releases every current run and re-adds the wanted one: both passes widen one event (nights 23 → 27 touched).
        Assert.Equal(["Deluxe 2026-10-23>2026-10-27"], await EventsAfterAsync(mark));

        mark = await MarkAsync();
        await _w.Changes.EarlyDepartureAsync(_w.Ctx(_w.Reception, "ed"), new EarlyDepartureRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "Family emergency"), Ct);
        Assert.Equal(["Classic 2026-10-23>2026-10-26"], await EventsAfterAsync(mark));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task Room_move_publishes_one_event_per_room_type_it_touches()
    {
        var mark = await MarkAsync();
        await _w.Changes.MoveRoomAsync(_w.Ctx(_w.Reception, "mv-same"), new MoveRoomRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), _w.Room("105"), "Quieter room", null, null), Ct);
        Assert.Equal(["Classic 2026-10-23>2026-10-26"], await EventsAfterAsync(mark)); // same type: one event

        mark = await MarkAsync();
        await _w.Changes.MoveRoomAsync(_w.Ctx(_w.Manager, "mv-suite"), new MoveRoomRequest(_w.Stay("HCC-1043"), await VerAsync("HCC-1043"), _w.Room("106"), "Upgrade", null, null), Ct);
        // Deluxe → Suite: the Deluxe nights are released (and night 23 re-added in 104), the Suite nights from today are taken.
        Assert.Equal(["Deluxe 2026-10-23>2026-10-26", "Suite 2026-10-24>2026-10-26"], await EventsAfterAsync(mark));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task A_refused_move_rolls_its_event_back_with_everything_else()
    {
        // The allocator releases the current nights (the event is already written) and then finds 105 taken on 25 Oct: all rolls back.
        await _w.Seed.ReservationAsync("105", "2026-10-25", "2026-10-26", "Arrival Tomorrow", ct: Ct);
        var mark = await MarkAsync();
        Assert.Equal("NOT_AVAILABLE", await Expect.CodeAsync(async () => await _w.Changes.MoveRoomAsync(_w.Ctx(_w.Reception, "held"),
            new MoveRoomRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), _w.Room("105"), "Quieter room", null, null), Ct)));
        Assert.Empty(await EventsAfterAsync(mark));
        // A move to a free room in a new transaction publishes its one event (nothing carried over from the failed one).
        await _w.Changes.MoveRoomAsync(_w.Ctx(_w.Reception, "free"), new MoveRoomRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), _w.Room("205"), "Quieter room", null, null), Ct);
        Assert.Equal(["Classic 2026-10-23>2026-10-26"], await EventsAfterAsync(mark));
        Assert.Equal(0, await _w.CountDriftAsync(Ct));
    }

    [Fact]
    public async Task The_OTA_projector_queues_one_availability_push_per_channel_and_delivers_it()
    {
        await _w.Db.ExecAsync($"""
            INSERT ota.channel(property_id, code, name, status, commission_bps, markup_bps) VALUES ({_w.W.PropertyId}, 'BOOKNEST', N'BookNest', 'Connected', 1800, 1000);
            INSERT ota.channel(property_id, code, name, status, commission_bps, markup_bps) VALUES ({_w.W.PropertyId}, 'PAUSED', N'Paused channel', 'Paused', 1000, 0);
            INSERT ota.channel_room_map(channel_id, room_type_id, channel_code) SELECT c.channel_id, t.room_type_id, 'BN-' + UPPER(LEFT(t.code, 3)) FROM ota.channel c CROSS JOIN inv.room_type t
              WHERE c.code IN ('BOOKNEST', 'PAUSED') AND t.code IN ('Classic', 'Deluxe');
            UPDATE inv.room_type SET rack_rate_paise = 200000 WHERE code = 'Classic' AND property_id = {_w.W.PropertyId};
            """, Ct);
        var projector = new OtaChannelManagerProjector(_w.Db.Platform.Outbox, _w.Db.Platform.Connections, _w.Clock);
        while (await projector.RunOnceAsync(ct: Ct) > 0) { } // whatever the seed published before this test
        await _w.Db.ExecAsync("UPDATE ota.push SET status = 'Delivered' WHERE status = 'Queued';", Ct);
        var before = await _w.Db.CountAsync("SELECT COUNT(*) FROM ota.push;", Ct);

        await ExtendAsync("HCC-1041", "2026-10-28", "ext-1041");            // Classic 26, 27
        await ExtendAsync("HCC-1043", "2026-10-27", "ext-1043");            // Deluxe 26
        await projector.RunOnceAsync(ct: Ct);

        // One automatic push for the Connected channel (none for the paused one), both changes merged into it.
        Assert.Equal(before + 1, await _w.Db.CountAsync("SELECT COUNT(*) FROM ota.push;", Ct));
        var push = Convert.ToInt64(await _w.Db.ScalarAsync("SELECT push_id FROM ota.push p JOIN ota.channel c ON c.channel_id = p.channel_id WHERE c.code = 'BOOKNEST' AND p.origin = 'Inventory' AND p.status = 'Queued';", Ct), CultureInfo.InvariantCulture);
        Assert.Equal("BN-CLA 2026-10-26 220000|BN-CLA 2026-10-27 220000|BN-DEL 2026-10-26 0", await _w.Db.StringAsync($"""
            SELECT STRING_AGG(CONCAT(channel_code, ' ', CONVERT(CHAR(10), night, 23), ' ', rate_paise), '|') WITHIN GROUP (ORDER BY channel_code, night)
            FROM ota.push_row WHERE push_id = {push};
            """, Ct));
        // Availability = the type-night count (sellable − sold − held) after the change.
        Assert.Equal(0, await _w.Db.CountAsync($"""
            SELECT COUNT(*) FROM ota.push_row r JOIN inv.type_night_count c ON c.room_type_id = r.room_type_id AND c.night = r.night
            WHERE r.push_id = {push} AND r.avail <> c.sellable - c.sold - c.held;
            """, Ct));
        Assert.Equal((3, "Availability changed (automatic) · Classic 2026-10-26 → 2026-10-28, Deluxe 2026-10-26 → 2026-10-27 · 3 rows · 0 closed"),
            (await _w.Db.CountAsync($"SELECT row_count FROM ota.push WHERE push_id = {push};", Ct),
             await _w.Db.StringAsync($"SELECT summary FROM ota.push WHERE push_id = {push};", Ct)));
        Assert.Equal(_w.Reception, await _w.Db.CountAsync($"SELECT requested_by FROM ota.push WHERE push_id = {push};", Ct));
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'ChannelPushQueued' AND aggregate_id = {push};", Ct));

        await projector.RunOnceAsync(ct: Ct);
        Assert.Equal("Delivered|SIM-" + push.ToString(CultureInfo.InvariantCulture), await _w.Db.StringAsync($"SELECT CONCAT(status, '|', ack_ref) FROM ota.push WHERE push_id = {push};", Ct));
        Assert.Equal(0, await projector.RunOnceAsync(ct: Ct)); // exactly once

        // The next change after delivery queues a new push.
        await _w.Changes.ShortenStayAsync(_w.Ctx(_w.Reception, "sh-1041"), new ShortenStayRequest(_w.Stay("HCC-1041"), await VerAsync("HCC-1041"), "2026-10-27", "One night less"), Ct);
        await projector.RunOnceAsync(ct: Ct);
        Assert.Equal(before + 2, await _w.Db.CountAsync("SELECT COUNT(*) FROM ota.push;", Ct));
        Assert.Equal(1, await _w.Db.CountAsync("SELECT COUNT(*) FROM ota.push WHERE origin = 'Inventory' AND status = 'Queued';", Ct));
    }
}
