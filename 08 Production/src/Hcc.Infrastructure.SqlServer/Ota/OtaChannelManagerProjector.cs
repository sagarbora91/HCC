using System.Data;
using System.Globalization;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Ota;
using Hcc.Domain.Dates;
using Hcc.Domain.Inventory;
using Hcc.Domain.Ota;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Ota;

/// <summary>
/// The SIMULATED channel manager, driven by the outbox (frozen <see cref="OutboxConsumer"/>: at-least-once delivery, exactly-once effect per consumer):
/// ChannelPushQueued → the push is acknowledged (status Delivered, ack SIM-n; a real channel manager would be called here);
/// reservation created / amended / cancelled / no-show and published yield tariffs → every Connected channel of the property is marked "push due"
/// (ari_dirty_utc) until the next push;
/// A08-03 InventoryChanged (any change of a room type's availability: bookings, holds and their expiry, stay extend / shorten / early departure /
/// room move, group and room blocks) → an automatic availability push of those nights (from the business date, at most
/// <see cref="MaxAutomaticNights"/>) to every Connected channel that maps the room type: an <c>ota.push</c> with origin 'Inventory', queued with its
/// own ChannelPushQueued and delivered by the handling above. While that push is still Queued, later changes are merged into it (coalesced).
/// Nothing leaves the server.
/// </summary>
public sealed class OtaChannelManagerProjector(OutboxConsumer outbox, SqlConnectionFactory connections, TimeProvider clock, Hcc.Application.Configuration.ISettingsReader? settings = null)
{
    public const string ConsumerName = "ota-channel-manager";

    /// <summary>Nights one automatic push covers at most, from the business date.</summary>
    public const int MaxAutomaticNights = 400;

    /// <summary><c>ota.push.origin</c> of an automatic availability push (staff pushes are 'Staff').</summary>
    public const string InventoryOrigin = "Inventory";

    public static readonly IReadOnlyCollection<string> EventTypes =
    [
        OtaEvents.PushQueued, OtaEvents.ReservationCreated, OtaEvents.ReservationAmended, OtaEvents.ReservationCancelled, OtaEvents.ReservationNoShow,
        OtaEvents.YieldTariffsPublished, InventoryEvents.InventoryChanged,
    ];

    public Task<int> RunOnceAsync(int batchSize = 100, CancellationToken ct = default) => outbox.ConsumeAsync(ConsumerName, EventTypes, ApplyAsync, batchSize, ct);

    public async Task ApplyAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var now = clock.GetUtcNow().UtcDateTime;
        if (evt.EventType == InventoryEvents.InventoryChanged)
        {
            await QueueAvailabilityPushAsync(evt, connection, transaction, now, ct).ConfigureAwait(false);
            return;
        }

        string sql = evt.EventType switch
        {
            OtaEvents.PushQueued => "UPDATE ota.push SET status = 'Delivered', delivered_utc = @now, ack_ref = 'SIM-' + CAST(push_id AS VARCHAR(20)) WHERE push_id = @agg AND status = 'Queued';",
            OtaEvents.YieldTariffsPublished => "UPDATE ota.channel SET ari_dirty_utc = ISNULL(ari_dirty_utc, @now) WHERE status = 'Connected' AND property_id = (SELECT property_id FROM yld.publish WHERE publish_id = @agg);",
            _ => "UPDATE ota.channel SET ari_dirty_utc = ISNULL(ari_dirty_utc, @now) WHERE status = 'Connected' AND property_id = (SELECT property_id FROM inv.reservation WHERE reservation_id = @agg);",
        };
        await using var cmd = connections.Command(sql, connection, transaction);
        cmd.Parameters.Add("@agg", SqlDbType.BigInt).Value = evt.AggregateId;
        var p = cmd.Parameters.Add("@now", SqlDbType.DateTime2);
        p.Scale = 3;
        p.Value = now;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record ChannelTarget(int ChannelId, int MarkupBps, string ChannelCode, bool StopSell);

    private sealed record TypeFacts(DateOnly BusinessDate, long RackRatePaise, int? PrincipalId);

    /// <summary>
    /// A08-03: the nights of the event (clipped to the business date and <see cref="MaxAutomaticNights"/>) for every Connected channel mapping the room
    /// type. Availability per night = sellable − sold − held of the type-night count (computed as the count writer would for a night without a row),
    /// rate = BAR tariff of the night × (1 + markup), closed when stop-sold or sold out, exactly as a staff push row.
    /// </summary>
    private async Task QueueAvailabilityPushAsync(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, DateTime now, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<InventoryChangedEvent>(evt.PayloadJson, CanonicalJson.SerializerOptions)
            ?? throw new InvalidOperationException($"InventoryChanged event {evt.OutboxId} has no payload.");
        SqlCommand Cmd(string sql) => connections.Command(sql, connection, transaction);

        var facts = await FactsAsync(Cmd, evt.OutboxId, e.PropertyId, e.RoomTypeId, ct).ConfigureAwait(false);
        if (facts is null) return; // the room type is gone (deleted in Room masters after the event): nothing to push
        var from = StayDates.Parse(e.From, "from");
        var to = StayDates.Parse(e.To, "to");
        if (from < facts.BusinessDate) from = facts.BusinessDate;
        var last = facts.BusinessDate.AddDays(MaxAutomaticNights);
        if (to > last) to = last;
        if (to <= from) return; // only past nights changed

        var channels = new List<ChannelTarget>();
        await using (var q = Cmd("""
            SELECT c.channel_id, c.markup_bps, m.channel_code, m.stop_sell
            FROM ota.channel c WITH (UPDLOCK, ROWLOCK) JOIN ota.channel_room_map m ON m.channel_id = c.channel_id
            WHERE c.property_id = @p AND c.status = 'Connected' AND m.room_type_id = @t ORDER BY c.channel_id;
            """))
        {
            q.Parameters.Add("@p", SqlDbType.Int).Value = e.PropertyId;
            q.Parameters.Add("@t", SqlDbType.Int).Value = e.RoomTypeId;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) channels.Add(new ChannelTarget(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.GetBoolean(3)));
        }

        if (channels.Count == 0) return;
        if (facts.PrincipalId is null)
        {
            // A push names who caused it (ota.push.requested_by); an event without a principal only marks the channels "push due".
            await using var dirty = Cmd("UPDATE ota.channel SET ari_dirty_utc = ISNULL(ari_dirty_utc, @now) WHERE channel_id IN (SELECT value FROM OPENJSON(@ids));");
            dirty.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
            dirty.Parameters.Add("@ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(channels.Select(c => c.ChannelId));
            await dirty.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        var avail = await AvailabilityAsync(Cmd, e.PropertyId, e.RoomTypeId, from, to, ct).ConfigureAwait(false);
        // F8-QW-NS (QWQ-2): the Owner's yld.overbook.<type code> cap (default 0 = no change) is added to the pushed availability, nowhere else.
        var cap = await OverbookCapAsync(Cmd, e.PropertyId, e.RoomTypeId, ct).ConfigureAwait(false);
        if (cap > 0) avail = avail.Select(a => (a.Night, Avail: OtaRules.PushAvail(a.Avail, cap))).ToList();
        // F8-QW-NS end
        // F4-int: day-set aware (weekday / weekend card rows, TariffCardRules.Resolve = inv.fn_tariff_on)
        var tariffs = new List<Hcc.Domain.Tariff.TrcTariffRow>();
        var weekend = new Dictionary<long, IReadOnlyCollection<int>>();
        await using (var q = Cmd("SELECT x.effective_from, x.rate_paise, x.day_set, x.card_version_id, v.weekend_days FROM inv.room_type_tariff x LEFT JOIN mst.tariff_card_version v ON v.version_id = x.card_version_id WHERE x.room_type_id = @t ORDER BY x.effective_from;"))
        {
            q.Parameters.Add("@t", SqlDbType.Int).Value = e.RoomTypeId;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                long? vid = rd.IsDBNull(3) ? null : rd.GetInt64(3);
                tariffs.Add(new Hcc.Domain.Tariff.TrcTariffRow("", DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetInt64(1), rd.GetString(2)[0], vid));
                if (vid is { } v) weekend[v] = Hcc.Domain.Tariff.TariffCardRules.ParseWeekend(rd.IsDBNull(4) ? null : rd.GetString(4)).ToList();
            }
        }

        var defaultWeekend = Hcc.Domain.Tariff.TariffCardRules.ParseWeekend(null).ToList();
        long RateOnNight(DateOnly n) => Hcc.Domain.Tariff.TariffCardRules.Resolve(tariffs, n, id => id is { } k && weekend.TryGetValue(k, out var w) ? w : defaultWeekend)?.RatePaise ?? facts.RackRatePaise;

        foreach (var ch in channels)
        {
            var rows = avail.Select(a =>
            {
                var closed = ch.StopSell || a.Avail == 0;
                return (a.Night, a.Avail, Rate: OtaRules.MarkedUpRate(RateOnNight(a.Night), ch.MarkupBps), Closed: closed,
                    Why: ch.StopSell ? "Stop-sell" : a.Avail == 0 ? "Sold out" : null);
            }).ToList();

            long? queued;
            await using (var q = Cmd("SELECT TOP (1) push_id FROM ota.push WITH (UPDLOCK, ROWLOCK) WHERE channel_id = @c AND origin = @o AND status = 'Queued' ORDER BY push_id DESC;"))
            {
                q.Parameters.Add("@c", SqlDbType.Int).Value = ch.ChannelId;
                q.Parameters.Add("@o", SqlDbType.VarChar, 10).Value = InventoryOrigin;
                queued = await q.ExecuteScalarAsync(ct).ConfigureAwait(false) as long?;
            }

            var pushId = queued ?? await InsertPushAsync(Cmd, e.PropertyId, ch.ChannelId, facts.PrincipalId.Value, now, ct).ConfigureAwait(false);
            await using (var del = Cmd("DELETE ota.push_row WHERE push_id = @id AND room_type_id = @t AND night >= @from AND night < @to;"))
            {
                del.Parameters.Add("@id", SqlDbType.BigInt).Value = pushId;
                del.Parameters.Add("@t", SqlDbType.Int).Value = e.RoomTypeId;
                del.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
                del.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            foreach (var r in rows)
            {
                await using var ins = Cmd("INSERT ota.push_row(push_id, room_type_id, night, channel_code, avail, rate_paise, closed, why) VALUES (@id, @t, @n, @code, @av, @rate, @cl, @why);");
                ins.Parameters.Add("@id", SqlDbType.BigInt).Value = pushId;
                ins.Parameters.Add("@t", SqlDbType.Int).Value = e.RoomTypeId;
                ins.Parameters.Add("@n", SqlDbType.Date).Value = r.Night.ToDateTime(TimeOnly.MinValue);
                ins.Parameters.Add("@code", SqlDbType.VarChar, 20).Value = ch.ChannelCode;
                ins.Parameters.Add("@av", SqlDbType.Int).Value = r.Avail;
                ins.Parameters.Add("@rate", SqlDbType.BigInt).Value = r.Rate;
                ins.Parameters.Add("@cl", SqlDbType.Bit).Value = r.Closed;
                ins.Parameters.Add("@why", SqlDbType.VarChar, 12).Value = (object?)r.Why ?? DBNull.Value;
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            var (count, closedCount) = await SummariseAsync(Cmd, pushId, ct).ConfigureAwait(false);
            if (queued is null)
                await OutboxWriter.InsertAsync(Cmd, OtaEvents.PushQueued, pushId, CanonicalJson.Serialize(new { pushId, channelId = ch.ChannelId, rows = count, closed = closedCount }),
                    e.PropertyId, facts.PrincipalId, ct).ConfigureAwait(false);
        }
    }

    // F8-QW-NS
    private async Task<int> OverbookCapAsync(Func<string, SqlCommand> cmd, int propertyId, int roomTypeId, CancellationToken ct)
    {
        if (settings is null) return 0;
        await using var q = cmd("SELECT code FROM inv.room_type WHERE room_type_id = @t AND property_id = @p;");
        q.Parameters.Add("@t", SqlDbType.Int).Value = roomTypeId;
        q.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        var code = await q.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (string.IsNullOrEmpty(code)) return 0;
        try
        {
            var v = await settings.GetAsync(propertyId, Hcc.Application.Configuration.MeasurementCatalogue.OverbookKey(code), null, ct).ConfigureAwait(false);
            return v.Kind == Hcc.Domain.Configuration.ConfigValueKind.Number ? (int)Math.Clamp(v.Number, 0, Hcc.Application.Configuration.MeasurementCatalogue.OverbookMax) : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return 0; }
    }
    // F8-QW-NS end

    private static async Task<TypeFacts?> FactsAsync(Func<string, SqlCommand> cmd, long outboxId, int propertyId, int roomTypeId, CancellationToken ct)
    {
        await using var q = cmd("""
            SELECT p.business_date, t.rack_rate_paise, (SELECT principal_id FROM plat.outbox WHERE outbox_id = @o)
            FROM inv.property p JOIN inv.room_type t ON t.property_id = p.property_id
            WHERE p.property_id = @p AND t.room_type_id = @t;
            """);
        q.Parameters.Add("@o", SqlDbType.BigInt).Value = outboxId;
        q.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        q.Parameters.Add("@t", SqlDbType.Int).Value = roomTypeId;
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new TypeFacts(DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetInt64(1), rd.IsDBNull(2) ? null : rd.GetInt32(2));
    }

    /// <summary>Rooms of the type free to sell per night: the type-night count, or for a night without a count row what the count writer would create.</summary>
    private static async Task<List<(DateOnly Night, int Avail)>> AvailabilityAsync(Func<string, SqlCommand> cmd, int propertyId, int roomTypeId, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        await using var q = cmd("""
            WITH nights AS (
              SELECT TOP (@count) DATEADD(day, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, @from) AS night FROM sys.all_objects)
            SELECT n.night,
              ISNULL(c.sellable, (SELECT COUNT(*) FROM inv.room r WHERE r.room_type_id = @t AND r.property_id = @p
                 AND NOT EXISTS (SELECT 1 FROM inv.v_room_block_effective b WHERE b.room_id = r.room_id
                                   AND (b.from_night IS NULL OR b.from_night <= n.night) AND (b.to_night IS NULL OR b.to_night > n.night))))
              - ISNULL(c.sold, (SELECT COUNT(*) FROM inv.room_night rn JOIN inv.room r ON r.room_id = rn.room_id WHERE r.room_type_id = @t AND rn.night = n.night))
              - ISNULL(c.held, (SELECT ISNULL(SUM(gt.allotted - gt.picked), 0) FROM inv.group_block_type gt JOIN inv.group_block g ON g.group_block_id = gt.group_block_id
                 WHERE gt.room_type_id = @t AND g.status = 'Open' AND g.arrival <= n.night AND g.departure > n.night))
            FROM nights n LEFT JOIN inv.type_night_count c ON c.room_type_id = @t AND c.night = n.night
            ORDER BY n.night;
            """);
        q.Parameters.Add("@count", SqlDbType.Int).Value = to.DayNumber - from.DayNumber;
        q.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        q.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        q.Parameters.Add("@t", SqlDbType.Int).Value = roomTypeId;
        var list = new List<(DateOnly, int)>();
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((DateOnly.FromDateTime(rd.GetDateTime(0)), Math.Max(0, rd.GetInt32(1))));
        return list;
    }

    private static async Task<long> InsertPushAsync(Func<string, SqlCommand> cmd, int propertyId, int channelId, int principalId, DateTime now, CancellationToken ct)
    {
        await using var q = cmd("""
            INSERT ota.push(property_id, channel_id, requested_utc, requested_by, row_count, closed_count, summary, status, origin)
            OUTPUT INSERTED.push_id VALUES (@p, @c, @now, @u, 0, 0, N'', 'Queued', @o);
            """);
        q.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        q.Parameters.Add("@c", SqlDbType.Int).Value = channelId;
        var n = q.Parameters.Add("@now", SqlDbType.DateTime2);
        n.Scale = 3;
        n.Value = now;
        q.Parameters.Add("@u", SqlDbType.Int).Value = principalId;
        q.Parameters.Add("@o", SqlDbType.VarChar, 10).Value = InventoryOrigin;
        return (long)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    /// <summary>Recounts the push's rows and rewrites its summary: "Availability changed (automatic) · Deluxe 2026-10-26 → 2026-10-27 · 1 rows · 0 closed".</summary>
    private static async Task<(int Rows, int Closed)> SummariseAsync(Func<string, SqlCommand> cmd, long pushId, CancellationToken ct)
    {
        var parts = new List<string>();
        int rows = 0, closed = 0;
        await using (var q = cmd("""
            SELECT t.code, MIN(r.night), MAX(r.night), COUNT(*), SUM(CAST(r.closed AS INT))
            FROM ota.push_row r JOIN inv.room_type t ON t.room_type_id = r.room_type_id
            WHERE r.push_id = @id GROUP BY t.code, t.sort_order, t.room_type_id ORDER BY t.sort_order, t.room_type_id;
            """))
        {
            q.Parameters.Add("@id", SqlDbType.BigInt).Value = pushId;
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var first = DateOnly.FromDateTime(rd.GetDateTime(1));
                var lastNight = DateOnly.FromDateTime(rd.GetDateTime(2));
                parts.Add($"{rd.GetString(0)} {StayDates.ToWire(first)} → {StayDates.ToWire(lastNight.AddDays(1))}");
                rows += rd.GetInt32(3);
                closed += rd.GetInt32(4);
            }
        }

        var summary = $"Availability changed (automatic) · {string.Join(", ", parts)} · {rows.ToString(CultureInfo.InvariantCulture)} rows · {closed.ToString(CultureInfo.InvariantCulture)} closed";
        if (summary.Length > 300) summary = summary[..297] + "...";
        await using var up = cmd("UPDATE ota.push SET row_count = @rows, closed_count = @closed, summary = @sum WHERE push_id = @id;");
        up.Parameters.Add("@rows", SqlDbType.Int).Value = rows;
        up.Parameters.Add("@closed", SqlDbType.Int).Value = closed;
        up.Parameters.Add("@sum", SqlDbType.NVarChar, 300).Value = summary;
        up.Parameters.Add("@id", SqlDbType.BigInt).Value = pushId;
        await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return (rows, closed);
    }
}
