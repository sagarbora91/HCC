using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Inventory;
using Hcc.Application.Reservations;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Ota;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Ota;

namespace Hcc.Application.Ota;

/// <summary>
/// OTA reads: channels, inbox, pre-check of a message against live availability (prototype chnAcceptCheck), the 7-night rate / availability
/// push preview (chnPushPreview, built from the Inventory service: availability and the BAR tariff that yield publishes into), the push log,
/// the commission report (R9) and the reconciliation view.
/// </summary>
public sealed class OtaQueries(IOtaStore store, IInventoryStore inventoryStore, InventoryService inventory, ReservationService reservations,
    IAccessPolicy access, TimeProvider clock)
{
    private const string Restricted = AvailabilityReasons.Restricted;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<ChannelView>> ListChannelsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        var channels = await store.ListChannelsAsync(propertyId, null, ct).ConfigureAwait(false);
        var pending = (await store.ListMessagesAsync(propertyId, MessageStatuses.Pending, 1000, ct).ConfigureAwait(false)).GroupBy(m => m.ChannelId).ToDictionary(g => g.Key, g => g.Count());
        return channels.Select(c => ToView(c, pending.TryGetValue(c.ChannelId, out var n) ? n : 0)).ToList();
    }

    public static ChannelView ToView(ChannelRecord c, int pending) => new(c.ChannelId, c.Code, c.Name, c.Status, InventoryErrors.VersionToWire(c.RowVersion), c.CommissionBps,
        c.MarkupBps, c.LastSyncUtc is { } l ? Iso(l) : null, c.StatusNote, c.AriDirtyUtc is not null && c.Status == ChannelStatuses.Connected, pending,
        c.Map.Select(m => new ChannelMappingView(m.RoomTypeId, m.RoomType, m.ChannelCode, m.StopSell)).ToList());

    public async Task<IReadOnlyList<ChannelMessageView>> ListMessagesAsync(int propertyId, int principalId, string? status, int limit, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        if (status is not null && status is not (MessageStatuses.Queued or MessageStatuses.Pending or "Importing" or MessageStatuses.Accepted or MessageStatuses.Applied or MessageStatuses.Rejected))
            throw HccException.Validation("Unknown message status.");
        var channels = (await store.ListChannelsAsync(propertyId, null, ct).ConfigureAwait(false)).ToDictionary(c => c.ChannelId);
        var rows = await store.ListMessagesAsync(propertyId, status, Math.Clamp(limit, 1, 500), ct).ConfigureAwait(false);
        var contact = await GuestContactMask.CanSeeAsync(access, propertyId, principalId, ct).ConfigureAwait(false);   // F-L06
        var views = new List<ChannelMessageView>();
        foreach (var m in rows)
        {
            channels.TryGetValue(m.ChannelId, out var ch);
            var map = ch?.MapForCode(m.ChannelRoomCode);
            string? publicNo = null, room = null;
            if (m.ReservationId is { } rid && await TryDetailsAsync(propertyId, principalId, rid, ct).ConfigureAwait(false) is { } d)
            {
                publicNo = d.PublicNo;
                room = d.RoomNo;
            }

            views.Add(new ChannelMessageView(m.MessageId, InventoryErrors.VersionToWire(m.RowVersion), m.ChannelId, ch?.Name ?? m.ChannelId.ToString(CultureInfo.InvariantCulture),
                m.ExternalId, m.Kind, m.ChannelRef, m.Guest, GuestContactMask.Phone(contact, m.Phone), GuestContactMask.Email(contact, m.Email), m.ChannelRoomCode, map?.RoomTypeId, map?.RoomType, StayDates.ToWire(m.Arrival),
                StayDates.ToWire(m.Departure), m.Adults, m.Children, m.AmountPaise, m.ReceivedUtc is { } r ? Iso(r) : null, m.Status, m.ReservationId, publicNo, room, m.Reason));
        }

        return views;
    }

    private async Task<ReservationDetails?> TryDetailsAsync(int propertyId, int principalId, long reservationId, CancellationToken ct)
    {
        try
        {
            return await reservations.GetAsync(propertyId, principalId, reservationId, ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code is HccErrorCode.FORBIDDEN or HccErrorCode.NOT_FOUND)
        {
            return null;
        }
    }

    /// <summary>Pre-check shown in the inbox and enforced by Accept. Never throws for a business problem: it returns Ok = false with the reason.</summary>
    public async Task<MessageCheckView> CheckMessageAsync(int propertyId, int principalId, long messageId, int? roomId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        var m = await store.GetMessageAsync(propertyId, messageId, null, false, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Message");
        return await CheckAsync(propertyId, principalId, m, roomId, ct).ConfigureAwait(false);
    }

    internal async Task<MessageCheckView> CheckAsync(int propertyId, int principalId, MessageRecord m, int? roomId, CancellationToken ct)
    {
        if (m.Status != MessageStatuses.Pending) return new MessageCheckView(false, $"Already {m.Status.ToLowerInvariant()}.", null, null, null);
        var c = await store.GetChannelAsync(propertyId, m.ChannelId, null, false, ct).ConfigureAwait(false);
        if (c is null) return new MessageCheckView(false, "Unknown channel.", null, null, null);
        var property = await inventoryStore.GetPropertyAsync(propertyId, null, ct).ConfigureAwait(false);
        var map = c.MapForCode(m.ChannelRoomCode);
        var types = await store.ListRoomTypesAsync(propertyId, null, ct).ConfigureAwait(false);
        if (m.Kind == MessageKinds.New)
        {
            var dup = await store.FindNewAsync(propertyId, m.ChannelId, m.ChannelRef, null, false, ct).ConfigureAwait(false);
            if (dup is not null && dup.MessageId != m.MessageId && dup.Status == MessageStatuses.Accepted)
                return new MessageCheckView(false, $"Duplicate: {m.ChannelRef} is already accepted.", null, null, null);
            if (map is null) return new MessageCheckView(false, $"Room code {m.ChannelRoomCode} is not mapped on {c.Name}.", null, null, null);
            var type = types.First(t => t.Id == map.RoomTypeId);
            var problem = OtaRules.NewMessageProblem(c.Name, map.RoomType, true, m.Arrival, m.Departure, property.BusinessDate, m.Adults, m.Children, type.MaxOccupancy);
            if (problem is not null) return new MessageCheckView(false, problem, null, null, null);
            var av = await inventory.GetAvailabilityAsync(propertyId, principalId, new AvailabilityQuery(StayDates.ToWire(m.Arrival), StayDates.ToWire(m.Departure),
                m.Adults, m.Children, map.RoomTypeId), ct).ConfigureAwait(false);
            var free = av.Rooms.Where(r => r.Available || r.Reasons.All(x => x == Restricted)).ToList();
            var warning = map.StopSell ? $"{map.RoomType} is stop-sold on {c.Name}; this reservation was already sold." : null;
            if (av.Types.FirstOrDefault()?.Violations is { Count: > 0 } v)
                warning = (warning is null ? "" : warning + " ") + "Stay restriction applies; accepting needs an approver override: " + string.Join(" ", v.Select(x => x.Message));
            if (roomId is { } rid)
            {
                var chosen = av.Rooms.FirstOrDefault(r => r.RoomId == rid);
                if (chosen is null || chosen.RoomTypeId != map.RoomTypeId) return new MessageCheckView(false, $"Room {rid} is not a {map.RoomType} room.", null, null, null);
                if (free.All(r => r.RoomId != rid))
                    return new MessageCheckView(false, $"Room {chosen.RoomNo} is not free for {StayDates.ToWire(m.Arrival)} → {StayDates.ToWire(m.Departure)}.", null, null, null);
                return new MessageCheckView(true, $"Room {chosen.RoomNo}", chosen.RoomNo, chosen.RoomId, warning);
            }

            if (free.Count == 0)
                return new MessageCheckView(false, $"No {map.RoomType} room free for {StayDates.ToWire(m.Arrival)} → {StayDates.ToWire(m.Departure)} (overbooking prevented).", null, null, null);
            return new MessageCheckView(true, $"Room {free[0].RoomNo} ({free.Count} free)", free[0].RoomNo, free[0].RoomId, warning);
        }

        var o = await store.FindNewAsync(propertyId, m.ChannelId, m.ChannelRef, null, false, ct).ConfigureAwait(false);
        if (o is null || o.MessageId == m.MessageId) return new MessageCheckView(false, $"Original reservation {m.ChannelRef} not found.", null, null, null);
        if (o.Status == MessageStatuses.Rejected) return new MessageCheckView(false, $"Original reservation {m.ChannelRef} was rejected.", null, null, null);
        if (o.Status == MessageStatuses.Pending)
            return new MessageCheckView(true, m.Kind == MessageKinds.Cancel ? "Withdraws the pending request" : "Updates the pending request", null, null, null);
        if (o.Status != MessageStatuses.Accepted || o.ReservationId is not { } resId)
            return new MessageCheckView(false, $"Original reservation {m.ChannelRef} is {o.Status.ToLowerInvariant()}.", null, null, null);
        var d = await reservations.GetAsync(propertyId, principalId, resId, ct).ConfigureAwait(false);
        if (m.Kind == MessageKinds.Cancel)
        {
            if (d.Status == ReservationStatuses.Cancelled) return new MessageCheckView(true, $"{d.PublicNo} already cancelled", null, null, null);
            if (d.Status != ReservationStatuses.Confirmed) return new MessageCheckView(false, $"{d.PublicNo} is {d.Status} — handle at the front desk.", null, null, null);
            return new MessageCheckView(true, $"Cancels {d.PublicNo}", d.RoomNo, d.RoomId, null);
        }

        if (d.Status != ReservationStatuses.Confirmed) return new MessageCheckView(false, $"{d.PublicNo} is {d.Status} — handle at the front desk.", null, null, null);
        if (m.Adults != d.Adults || m.Children != d.Children) return new MessageCheckView(false, "The party size changed: the front desk must amend the reservation.", null, null, null);
        if (m.Arrival < property.BusinessDate || m.Departure <= m.Arrival) return new MessageCheckView(false, "Invalid new dates.", null, null, null);
        return new MessageCheckView(true, $"{d.PublicNo} keeps room {d.RoomNo} if free for the new dates, otherwise moves to another {d.RoomType} room", d.RoomNo, d.RoomId, null);
    }

    // ───────────── rate and availability push ─────────────
    public async Task<PushPreviewView> PushPreviewAsync(int propertyId, int principalId, int channelId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        var c = await store.GetChannelAsync(propertyId, channelId, null, false, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Channel");
        var (rows, unmapped, business) = await BuildRowsAsync(propertyId, principalId, c, ct).ConfigureAwait(false);
        return new PushPreviewView(c.ChannelId, StayDates.ToWire(business), OtaRules.PushDays,
            rows.Select(r => new PushRowView(r.RoomType, r.ChannelCode, StayDates.ToWire(r.Night), r.Avail, r.RatePaise, r.Closed, r.Why)).ToList(), unmapped, rows.Count(r => r.Closed));
    }

    /// <summary>The next 7 nights for every mapped room type: rooms sellable that night (inventory availability), BAR tariff x (1 + markup), closed when stop-sold or 0 left.</summary>
    internal async Task<(IReadOnlyList<PushRowDetail> Rows, IReadOnlyList<string> Unmapped, DateOnly Business)> BuildRowsAsync(int propertyId, int principalId, ChannelRecord c,
        CancellationToken ct)
    {
        var property = await inventoryStore.GetPropertyAsync(propertyId, null, ct).ConfigureAwait(false);
        var rows = new List<PushRowDetail>();
        var unmapped = new List<string>();
        var first = true;
        for (var i = 0; i < OtaRules.PushDays; i++)
        {
            var night = property.BusinessDate.AddDays(i);
            var av = await inventory.GetAvailabilityAsync(propertyId, principalId, new AvailabilityQuery(StayDates.ToWire(night), StayDates.ToWire(night.AddDays(1)), 1), ct).ConfigureAwait(false);
            foreach (var t in av.Types)
            {
                var map = c.MapFor(t.RoomTypeId);
                if (map is null)
                {
                    if (first) unmapped.Add(t.Code);
                    continue;
                }

                var rooms = av.Rooms.Where(r => r.RoomTypeId == t.RoomTypeId).ToList();
                var avail = rooms.Count(r => r.Available || r.Reasons.All(x => x == Restricted));
                var tariff = rooms.Count > 0 ? rooms[0].NightlyRatePaise : 0;
                var stop = map.StopSell;
                rows.Add(new PushRowDetail(t.RoomTypeId, t.Code, map.ChannelCode, night, avail, OtaRules.MarkedUpRate(tariff, c.MarkupBps), stop || avail == 0,
                    stop ? "Stop-sell" : avail == 0 ? "Sold out" : null));
            }

            first = false;
        }

        return (rows, unmapped, property.BusinessDate);
    }

    public async Task<IReadOnlyList<PushLogView>> PushLogAsync(int propertyId, int principalId, int? channelId, int limit, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, OtaPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await store.ListPushesAsync(propertyId, channelId, Math.Clamp(limit, 1, 200), ct).ConfigureAwait(false);
    }

    // ───────────── money views ─────────────
    public async Task<CommissionReportView> CommissionReportAsync(int propertyId, int principalId, string? from, string? to, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, OtaPermissions.ViewMoney, ct: ct).ConfigureAwait(false);
        var (f, t) = await RangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var rows = await store.CommissionRowsAsync(propertyId, f, t, ct).ConfigureAwait(false);
        var channels = await store.ListChannelsAsync(propertyId, null, ct).ConfigureAwait(false);
        static CommissionChannelTotal Sum(int id, string name, IEnumerable<CommissionRowView> r)
        {
            var l = r.ToList();
            return new CommissionChannelTotal(id, name, l.Count, l.Sum(x => x.Nights), l.Sum(x => x.BasePaise), l.Sum(x => x.CommissionPaise), l.Sum(x => x.NetPaise));
        }

        var by = channels.Select(c => Sum(c.ChannelId, c.Name, rows.Where(r => r.ChannelId == c.ChannelId))).ToList();
        return new CommissionReportView(StayDates.ToWire(f), StayDates.ToWire(t), rows, by, Sum(0, "All channels", rows));
    }

    public async Task<ReconciliationView> ReconciliationAsync(int propertyId, int principalId, string? from, string? to, int? channelId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, OtaPermissions.ViewMoney, ct: ct).ConfigureAwait(false);
        var (f, t) = await RangeAsync(propertyId, from, to, ct).ConfigureAwait(false);
        var rows = await store.ReconciliationRowsAsync(propertyId, f, t, channelId, ct).ConfigureAwait(false);
        return new ReconciliationView(StayDates.ToWire(f), StayDates.ToWire(t), rows.Count, rows.Count(r => r.Flags.Count == 0), rows.Sum(r => r.LedgerCommissionPaise),
            rows.Sum(r => r.ExpectedCommissionPaise), rows.Sum(r => r.CommissionVariancePaise), rows);
    }

    private async Task<(DateOnly From, DateOnly To)> RangeAsync(int propertyId, string? from, string? to, CancellationToken ct)
    {
        var property = await inventoryStore.GetPropertyAsync(propertyId, null, ct).ConfigureAwait(false);
        var f = from is null ? new DateOnly(property.BusinessDate.Year, property.BusinessDate.Month, 1) : StayDates.Parse(from, "from");
        var t = to is null ? f.AddMonths(1).AddDays(-1) : StayDates.Parse(to, "to");
        if (t < f) throw HccException.Validation("The end date must be on or after the start date.");
        if (t.DayNumber - f.DayNumber > 400) throw HccException.Validation("Choose a range of at most 400 days.");
        return (f, t);
    }
}
