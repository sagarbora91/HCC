using System.Globalization;
using Hcc.Contracts.Ota;
using Hcc.Contracts.Reservations;
using Hcc.Infrastructure.SqlServer.Ota;

namespace Hcc.Ota.Tests.Sql;

/// <summary>
/// A08-03: InventoryChanged → the OTA projector queues an automatic availability push of the changed nights for every Connected channel that
/// maps the room type, with the same availability, rate and closed rule as a staff push (PushChannelRates preview), coalesces further changes into
/// the channel's queued push, and leaves it to the existing ChannelPushQueued handling to deliver. BASE fixture: BookNest (Connected, every type,
/// 10 % markup), StayHub (Connected, Classic + Deluxe stop-sold, 8 %), TripKart (Error), RoomRaahi (Disconnected).
/// </summary>
[Trait("Category", "Sql")]
public sealed class OtaInventoryPushSqlTests : OtaSqlTest
{
    private async Task DrainAsync()
    {
        while (await F.Projector.RunOnceAsync(ct: Ct) > 0) { }
    }

    private Task<int> AutoPushesAsync(string channel, string? status = null)
        => CountAsync($"SELECT COUNT(*) FROM ota.push WHERE origin = '{OtaChannelManagerProjector.InventoryOrigin}' AND channel_id = {F.ChannelIds[channel]}" + (status is null ? ";" : $" AND status = '{status}';"));

    private async Task<long> AutoPushAsync(string channel)
        => Convert.ToInt64(await F.Db.ScalarAsync($"SELECT MAX(push_id) FROM ota.push WHERE origin = 'Inventory' AND channel_id = {F.ChannelIds[channel]};", Ct), CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_booking_queues_an_automatic_push_with_the_staff_push_figures_on_each_connected_channel_mapping_the_type()
    {
        await DrainAsync();
        var before = await CountAsync("SELECT COUNT(*) FROM ota.push;");
        await F.AcceptAsync("F1");                                  // BookNest Deluxe 27 → 29 Oct (through the Reservations service)
        await F.Projector.RunOnceAsync(ct: Ct);

        Assert.Equal(before + 2, await CountAsync("SELECT COUNT(*) FROM ota.push;"));
        Assert.Equal((1, 1, 0, 0), (await AutoPushesAsync("BOOKNEST", "Queued"), await AutoPushesAsync("STAYHUB", "Queued"), await AutoPushesAsync("TRIPKART"), await AutoPushesAsync("ROOMRAAHI")));

        // Same availability and rate as the staff push preview for those nights; StayHub has Deluxe stop-sold, so its rows are closed.
        foreach (var channel in new[] { "BOOKNEST", "STAYHUB" })
        {
            var preview = (await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds[channel], Ct)).Rows
                .Where(r => r.RoomType == "Deluxe" && r.Night is "2026-10-27" or "2026-10-28").OrderBy(r => r.Night, StringComparer.Ordinal)
                .Select(r => $"{r.ChannelCode} {r.Night} {r.Avail} {r.RatePaise} {(r.Closed ? 1 : 0)} {r.Why}").ToArray();
            var push = await AutoPushAsync(channel);
            var rows = (await F.Db.ScalarAsync($"""
                SELECT STRING_AGG(CONCAT(channel_code, ' ', CONVERT(CHAR(10), night, 23), ' ', avail, ' ', rate_paise, ' ', CAST(closed AS INT), ' ', why), '|') WITHIN GROUP (ORDER BY night)
                FROM ota.push_row WHERE push_id = {push};
                """, Ct) as string)!.Split('|');
            Assert.Equal(2, preview.Length);
            Assert.Equal(preview, rows);
        }

        Assert.Equal(2, await CountAsync($"SELECT COUNT(*) FROM ota.push_row WHERE push_id = {await AutoPushAsync("STAYHUB")} AND closed = 1 AND why = 'Stop-sell' AND channel_code = 'SH-DLX';"));

        // The existing ChannelPushQueued handling delivers both; the staff push log lists them with their summary.
        await F.Projector.RunOnceAsync(ct: Ct);
        Assert.Equal((1, 1), (await AutoPushesAsync("BOOKNEST", "Delivered"), await AutoPushesAsync("STAYHUB", "Delivered")));
        var bookNest = await AutoPushAsync("BOOKNEST");
        var log = (await F.Queries.PushLogAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], 10, Ct)).Single(p => p.PushId == bookNest);
        Assert.Equal(("Delivered", 2, 0, "Availability changed (automatic) · Deluxe 2026-10-27 → 2026-10-29 · 2 rows · 0 closed"), (log.Status, log.Rows, log.Closed, log.Summary));
    }

    [Fact]
    public async Task Changes_before_the_push_is_delivered_are_merged_into_the_queued_push()
    {
        await DrainAsync();
        await F.AcceptAsync("F1");                                  // BookNest Deluxe 27 → 29 Oct
        var f2 = await F.AcceptAsync("F2");                         // StayHub Classic 25 → 26 Oct
        await F.Booking.CancelAsync(F.Ctx(F.Manager, "CancelReservation"), new CancelReservationRequest(f2.ReservationId!.Value,
            (await F.ReservationAsync(f2.ReservationId.Value)).Version, "Guest cancelled by phone"), Ct);
        await F.Projector.RunOnceAsync(ct: Ct);

        // Three events (Deluxe; Classic booked; Classic cancelled) → one queued automatic push per channel, the Classic night once (its rows
        // replaced, not duplicated), with the availability as it is now: the cancelled room is back on sale, as in the staff push preview.
        Assert.Equal((1, 1), (await AutoPushesAsync("BOOKNEST"), await AutoPushesAsync("STAYHUB")));
        var push = await AutoPushAsync("BOOKNEST");
        Assert.Equal("BN-CLS 2026-10-25|BN-DLX 2026-10-27|BN-DLX 2026-10-28", await F.Db.ScalarAsync(
            $"SELECT STRING_AGG(CONCAT(channel_code, ' ', CONVERT(CHAR(10), night, 23)), '|') WITHIN GROUP (ORDER BY channel_code, night) FROM ota.push_row WHERE push_id = {push};", Ct));
        Assert.Equal(3, await CountAsync($"SELECT row_count FROM ota.push WHERE push_id = {push};"));
        var classic25 = (await F.Queries.PushPreviewAsync(1, F.Reception, F.ChannelIds["BOOKNEST"], Ct)).Rows.Single(r => r.RoomType == "Classic" && r.Night == "2026-10-25");
        Assert.Equal(classic25.Avail, await CountAsync($"SELECT avail FROM ota.push_row WHERE push_id = {push} AND channel_code = 'BN-CLS' AND night = '2026-10-25';"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE event_type = '{OtaEvents.PushQueued}' AND aggregate_id = {push};"));

        await F.Projector.RunOnceAsync(ct: Ct);
        Assert.Equal("Delivered", await F.Db.ScalarAsync($"SELECT status FROM ota.push WHERE push_id = {push};", Ct));

        // After delivery a new change queues a new push; the delivered one is never changed.
        var f3 = await F.AcceptAsync("F3");                         // BookNest Suite 25 → 27 Oct
        Assert.NotNull(f3.ReservationId);
        await F.Projector.RunOnceAsync(ct: Ct);
        var next = await AutoPushAsync("BOOKNEST");
        Assert.NotEqual(push, next);
        Assert.Equal(2, await CountAsync($"SELECT COUNT(*) FROM ota.push_row WHERE push_id = {next} AND channel_code = 'BN-STE';"));
        Assert.Equal(3, await CountAsync($"SELECT COUNT(*) FROM ota.push_row WHERE push_id = {push};"));
    }

    [Fact]
    public async Task A_paused_or_unmapped_channel_gets_no_automatic_push_and_past_nights_are_not_pushed()
    {
        await DrainAsync();
        var c = await ChannelAsync("STAYHUB");
        await F.Service.SetStatusAsync(F.Ctx(F.TechAdmin, "SetChannelStatus"), new SetChannelStatusRequest(c.ChannelId, c.Version, ChannelActions.Pause), Ct);
        await F.AcceptAsync("F3");                                  // BookNest Suite 25 → 27 Oct; StayHub does not map Suite and is paused anyway
        await F.Projector.RunOnceAsync(ct: Ct);
        Assert.Equal((1, 0), (await AutoPushesAsync("BOOKNEST"), await AutoPushesAsync("STAYHUB")));
        Assert.Equal("2026-10-25|2026-10-26", await F.Db.ScalarAsync($"SELECT STRING_AGG(CONVERT(CHAR(10), night, 23), '|') WITHIN GROUP (ORDER BY night) FROM ota.push_row WHERE push_id = {await AutoPushAsync("BOOKNEST")};", Ct));
        await DrainAsync();

        // An event whose nights are all before the business date pushes nothing (only the dedup row is written).
        var pushes = await CountAsync("SELECT COUNT(*) FROM ota.push;");
        await ExecAsync($"INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id) VALUES ('InventoryChanged', {F.TypeIds["Suite"]}, N'{{\"from\":\"2026-10-20\",\"propertyId\":1,\"roomTypeId\":{F.TypeIds["Suite"]},\"to\":\"2026-10-24\"}}', 1, {F.Owner});");
        Assert.Equal(1, await F.Projector.RunOnceAsync(ct: Ct));
        Assert.Equal(pushes, await CountAsync("SELECT COUNT(*) FROM ota.push;"));
    }
}
