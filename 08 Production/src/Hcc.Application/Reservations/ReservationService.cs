using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;
using Hcc.Domain.Reservations;

namespace Hcc.Application.Reservations;

/// <summary>
/// CreateReservation, AmendReservation, CancelReservation / MarkNoShow, ConfirmHold and reservation details (contracts §3),
/// ported from the prototype BOOK / CHANGE_DATES / CANCEL / CONFIRM handlers (03 Prototype/src/services/mock/handlers/reservations.ts):
/// same check order and messages. Lapsed holds and past-cut-off blocks are swept in a short transaction before each command.
/// </summary>
public sealed class ReservationService(IReservationStore reservations, IInventoryStore inventory, InventoryService inventoryService,
    ICommandRunner runner, IAccessPolicy access, TimeProvider clock)
{
    public async Task<CommandResult<ReservationResult>> CreateAsync(CommandContext context, CreateReservationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.CreateReservation, ct: ct).ConfigureAwait(false);
        var quoteVersion = InventoryErrors.ParseVersion(request.QuoteVersion, "quoteVersion");
        await inventory.SweepAsync(context.PropertyId, context.PrincipalId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var property = await inventory.GetPropertyAsync(context.PropertyId, tx, c).ConfigureAwait(false);
            var guest = StayRules.GuestName(request.Guest?.Name);
            var phone = StayRules.PhoneNumber(request.Guest?.Phone);
            var source = string.IsNullOrWhiteSpace(request.Source) ? "Direct" : request.Source.Trim();
            if (source.Length > 40) throw HccException.Validation("Booking source is too long.");
            var notes = TrimNotes(request.Notes);

            var quote = await UsableQuoteAsync(tx, context.PropertyId, request.QuoteId, quoteVersion, now, c).ConfigureAwait(false);
            StayRules.Stay(StayDates.ToWire(quote.Arrival), StayDates.ToWire(quote.Departure), property.BusinessDate);
            var party = StayRules.Party(quote.Adults, quote.Children);
            var snap = await inventory.LoadSnapshotAsync(context.PropertyId, quote.Arrival, quote.Departure, now, tx, c).ConfigureAwait(false);
            var room = snap.Rooms.FirstOrDefault(r => r.RoomId == request.RoomId) ?? throw HccException.Validation("Choose a room.");
            if (room.RoomTypeId != quote.RoomTypeId || (quote.RoomId is { } qr && qr != room.RoomId))
                throw HccException.Validation("The room does not match the quote. Request a new quote for this room.");
            StayRules.Capacity(room, party);

            GroupBlockInfo? block = null;
            if (request.GroupBlockId is { } bid)
            {
                block = snap.GroupBlocks.FirstOrDefault(b => b.GroupBlockId == bid) ?? throw InventoryErrors.NotFound("Group block");
                EnsurePickup(block, room, quote.Arrival, quote.Departure, property.BusinessDate);
            }

            var holds = AvailabilityRules.GroupHolds(snap);
            var reasons = AvailabilityRules.Exclusion(snap, holds, room, quote.Arrival, quote.Departure, party, 0, block?.GroupBlockId);
            if (reasons.Count > 0) throw InventoryErrors.NotAvailable("This room is not available for those dates.", reasons);
            var overrideNote = await RestrictionsAsync(context, snap, room, quote.Arrival, quote.Departure, request.RestrictionOverride, c).ConfigureAwait(false);

            var nights = quote.Nights;
            var blockRate = block?.Types.FirstOrDefault(t => t.RoomTypeId == room.RoomTypeId)?.RatePaise;
            if (blockRate is { } br)
                nights = quote.Nights.Select(n => new PricedNight(n.Date, br, n.TaxBps, Domain.Money.MoneyMath.TaxOf(new Domain.Money.Paise(br), n.TaxBps).Value)).ToList();

            DateTime? holdUntil = request.Hold ? now + StayRules.HoldLength : null;
            var status = request.Hold ? ReservationStatuses.Held : ReservationStatuses.Confirmed;
            var saved = await reservations.InsertAsync(tx, new NewReservation(context.PropertyId, context.PrincipalId, guest, phone,
                new StayAllocation(room.RoomId, room.RoomTypeId, quote.Arrival, quote.Departure, block?.GroupBlockId), quote.Adults, quote.Children,
                status, holdUntil, block is null ? source : "Group · " + block.Code, quote.QuoteId, nights, overrideNote,
                JoinNotes(notes, block is null ? null : $"Group block {block.Code} · {block.Name}"), now), c).ConfigureAwait(false);
            await inventory.MarkQuoteUsedAsync(tx, quote.QuoteId, saved.ReservationId, c).ConfigureAwait(false);

            var holdText = holdUntil is { } hu ? " · held until " + LocalTime(hu, property.TimeZone) : "";
            await tx.AuditAsync("CreateReservation", saved.PublicNo,
                Clip($"{saved.PublicNo} · {guest} · room {room.RoomNo}{holdText}{(overrideNote is null ? "" : " · " + overrideNote)}"), c).ConfigureAwait(false);
            await tx.PublishAsync("ReservationCreated", saved.ReservationId, new
            {
                saved.ReservationId,
                saved.PublicNo,
                room.RoomId,
                Arrival = StayDates.ToWire(quote.Arrival),
                Departure = StayDates.ToWire(quote.Departure),
                Status = status,
                GroupBlockId = block?.GroupBlockId,
            }, c).ConfigureAwait(false);
            return new ReservationResult(saved.ReservationId, saved.PublicNo, InventoryErrors.VersionToWire(saved.RowVersion), status,
                holdUntil is null ? null : InventoryService.ToWireInstant(holdUntil.Value));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ReservationResult>> AmendAsync(CommandContext context, AmendReservationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.EditReservation, ct: ct).ConfigureAwait(false);
        var expected = InventoryErrors.ParseVersion(request.ExpectedVersion);
        var decision = (request.PriceDecision ?? PriceDecisions.Keep).Trim().ToLowerInvariant();
        if (decision is not (PriceDecisions.Keep or PriceDecisions.Requote)) throw HccException.Validation("priceDecision must be keep or requote.");
        var quoteVersion = decision == PriceDecisions.Requote ? InventoryErrors.ParseVersion(request.QuoteVersion, "quoteVersion") : null;
        await inventory.SweepAsync(context.PropertyId, context.PrincipalId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var property = await inventory.GetPropertyAsync(context.PropertyId, tx, c).ConfigureAwait(false);
            var x = await reservations.GetForUpdateAsync(tx, context.PropertyId, request.ReservationId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Reservation");
            ReservationStatusMap.EnsureAmendable(x.Status, x.HoldUntilUtc, now);
            if (!InventoryErrors.VersionEquals(x.RowVersion, expected)) throw InventoryErrors.Stale("This reservation");

            var (a, d) = StayRules.Stay(request.Arrival, request.Departure, property.BusinessDate);
            var snap = await inventory.LoadSnapshotAsync(context.PropertyId, a, d, now, tx, c).ConfigureAwait(false);
            var room = snap.Rooms.FirstOrDefault(r => r.RoomId == request.RoomId) ?? throw HccException.Validation("Select a room.");
            StayRules.Capacity(room, x.Adults + x.Children);
            if (x.GroupBlockId is not null && (room.RoomTypeId != x.RoomTypeId || a != x.Arrival || d != x.Departure))
                throw HccException.Validation("A group pickup can change only to another room of the same type for the same dates. Cancel it to return the room to the block.");
            var holds = AvailabilityRules.GroupHolds(snap);
            var reasons = AvailabilityRules.Exclusion(snap, holds, room, a, d, x.Adults + x.Children, x.ReservationId, x.GroupBlockId);
            if (reasons.Count > 0) throw InventoryErrors.NotAvailable("Target room is unavailable for the requested stay.", reasons);
            if (room.RoomId == x.RoomId && a == x.Arrival && d == x.Departure) throw HccException.Validation("Nothing changed. Choose a different room or dates.");
            var overrideNote = await RestrictionsAsync(context, snap, room, a, d, request.RestrictionOverride, c).ConfigureAwait(false);

            IReadOnlyList<PricedNight> nights;
            long? rate;
            long? quoteId = null;
            if (decision == PriceDecisions.Requote)
            {
                if (request.QuoteId is not { } qid) throw HccException.Validation("Requote needs quoteId and quoteVersion.");
                var quote = await UsableQuoteAsync(tx, context.PropertyId, qid, quoteVersion!, now, c).ConfigureAwait(false);
                if (quote.RoomTypeId != room.RoomTypeId || (quote.RoomId is { } qr && qr != room.RoomId) || quote.Arrival != a || quote.Departure != d
                    || quote.Adults != x.Adults || quote.Children != x.Children)
                    throw HccException.Validation("The quote does not match the new stay. Request a new quote.");
                nights = quote.Nights;
                rate = quote.Nights[0].RatePaise;
                quoteId = quote.QuoteId;
            }
            else
            {
                // Agreed nightly rate is kept (WF-BOOK-001); each night takes the tax rate of its own date.
                rate = x.NightlyRatePaise ?? 0;
                nights = await inventoryService.PriceAsync(context.PropertyId, a, d, rate.Value, c).ConfigureAwait(false);
            }

            var notes = overrideNote is null ? x.Notes : JoinNotes(x.Notes, overrideNote);
            var saved = await reservations.ChangeAsync(tx, context.PropertyId, x,
                new ReservationChange(new StayAllocation(room.RoomId, room.RoomTypeId, a, d, x.GroupBlockId), rate, quoteId, nights, notes), c).ConfigureAwait(false);
            if (quoteId is { } usedQuote) await inventory.MarkQuoteUsedAsync(tx, usedQuote, x.ReservationId, c).ConfigureAwait(false);

            var oldRoom = snap.Rooms.FirstOrDefault(r => r.RoomId == x.RoomId)?.RoomNo ?? x.RoomId.ToString(CultureInfo.InvariantCulture);
            await tx.AuditAsync("AmendReservation", x.PublicNo, Clip(
                $"{x.PublicNo} · room {oldRoom} → {room.RoomNo}; {StayDates.ToWire(x.Arrival)}–{StayDates.ToWire(x.Departure)} → {StayDates.ToWire(a)}–{StayDates.ToWire(d)}; " +
                (decision == PriceDecisions.Keep ? "agreed tariff preserved" : "requoted") + (overrideNote is null ? "" : " · " + overrideNote)), c).ConfigureAwait(false);
            await tx.PublishAsync("ReservationAmended", x.ReservationId, new
            {
                x.ReservationId,
                room.RoomId,
                Arrival = StayDates.ToWire(a),
                Departure = StayDates.ToWire(d),
            }, c).ConfigureAwait(false);
            return new ReservationResult(x.ReservationId, x.PublicNo, InventoryErrors.VersionToWire(saved.RowVersion), x.Status,
                x.HoldUntilUtc is null ? null : InventoryService.ToWireInstant(x.HoldUntilUtc.Value));
        }, ct).ConfigureAwait(false);
    }

    public Task<CommandResult<ReservationResult>> CancelAsync(CommandContext context, CancelReservationRequest request, CancellationToken ct = default)
        => CancelOrNoShowAsync(context, request, noShow: false, ct);

    public Task<CommandResult<ReservationResult>> MarkNoShowAsync(CommandContext context, CancelReservationRequest request, CancellationToken ct = default)
        => CancelOrNoShowAsync(context, request, noShow: true, ct);

    public async Task<CommandResult<ReservationResult>> ConfirmHoldAsync(CommandContext context, ConfirmHoldRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.CreateReservation, ct: ct).ConfigureAwait(false);
        var expected = InventoryErrors.ParseVersion(request.ExpectedVersion);
        await inventory.SweepAsync(context.PropertyId, context.PrincipalId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var property = await inventory.GetPropertyAsync(context.PropertyId, tx, c).ConfigureAwait(false);
            var x = await reservations.GetForUpdateAsync(tx, context.PropertyId, request.ReservationId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Reservation");
            ReservationStatusMap.EnsureConfirmable(x.Status, x.HoldUntilUtc, now, x.HoldUntilUtc is { } hu ? " at " + LocalTime(hu, property.TimeZone) : "");
            if (!InventoryErrors.VersionEquals(x.RowVersion, expected)) throw InventoryErrors.Stale("This reservation");
            var saved = await reservations.SetStatusAsync(tx, context.PropertyId, x, ReservationStatuses.Confirmed, null, now, c).ConfigureAwait(false);
            await tx.AuditAsync("ConfirmHold", x.PublicNo, $"{x.PublicNo} · hold confirmed", c).ConfigureAwait(false);
            await tx.PublishAsync("ReservationConfirmed", x.ReservationId, new { x.ReservationId }, c).ConfigureAwait(false);
            return new ReservationResult(x.ReservationId, x.PublicNo, InventoryErrors.VersionToWire(saved.RowVersion), ReservationStatuses.Confirmed, null);
        }, ct).ConfigureAwait(false);
    }

    public async Task<ReservationDetails> GetAsync(int propertyId, int principalId, long reservationId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, InventoryPermissions.ViewReservations, ct: ct).ConfigureAwait(false);
        return await reservations.GetDetailsAsync(propertyId, reservationId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false)
            ?? throw InventoryErrors.NotFound("Reservation");
    }

    private async Task<CommandResult<ReservationResult>> CancelOrNoShowAsync(CommandContext context, CancelReservationRequest request, bool noShow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.CancelReservation, ct: ct).ConfigureAwait(false);
        var expected = InventoryErrors.ParseVersion(request.ExpectedVersion);
        await inventory.SweepAsync(context.PropertyId, context.PrincipalId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var property = await inventory.GetPropertyAsync(context.PropertyId, tx, c).ConfigureAwait(false);
            var x = await reservations.GetForUpdateAsync(tx, context.PropertyId, request.ReservationId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Reservation");
            var held = ReservationStatusMap.HoldRunning(x.Status, x.HoldUntilUtc, now);
            ReservationStatusMap.EnsureCancellable(x.Status, x.HoldUntilUtc, now, noShow, x.Arrival, property.BusinessDate);
            if (!InventoryErrors.VersionEquals(x.RowVersion, expected)) throw InventoryErrors.Stale("This reservation");
            if (await reservations.HasUnsettledMoneyAsync(tx, x.ReservationId, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Refund the advance and return any deposit before cancelling.");
            var reason = (request.Reason ?? "").Trim();
            if (reason.Length > 200) reason = reason[..200];
            var status = noShow ? ReservationStatuses.NoShow : ReservationStatuses.Cancelled;
            var saved = await reservations.SetStatusAsync(tx, context.PropertyId, x, status, reason.Length == 0 ? null : reason, now, c).ConfigureAwait(false);
            await tx.AuditAsync(noShow ? "MarkNoShow" : "CancelReservation", x.PublicNo,
                Clip(x.PublicNo + (held ? " · hold released" : "") + (reason.Length > 0 ? " · " + reason : "")), c).ConfigureAwait(false);
            await tx.PublishAsync(noShow ? "ReservationNoShow" : "ReservationCancelled", x.ReservationId, new { x.ReservationId, Status = status }, c).ConfigureAwait(false);
            return new ReservationResult(x.ReservationId, x.PublicNo, InventoryErrors.VersionToWire(saved.RowVersion), status, null);
        }, ct).ConfigureAwait(false);
    }

    private async Task<QuoteRecord> UsableQuoteAsync(ICommandTransaction tx, int propertyId, long quoteId, byte[] version, DateTime now, CancellationToken ct)
    {
        var quote = await inventory.GetQuoteForUpdateAsync(tx, propertyId, quoteId, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Quote");
        if (quote.UsedByReservationId is not null) throw HccException.Validation("This quote was already used for a booking. Request a new quote.");
        if (now >= quote.ExpiresUtc) throw HccException.Validation("This quote expired. Request a new quote.");
        if (!InventoryErrors.VersionEquals(quote.RowVersion, version)) throw InventoryErrors.Stale("This quote");
        return quote;
    }

    /// <summary>Stay restrictions (A2.1, DEC-053): RESTRICTED without an override; with one, the approve permission and a reason.</summary>
    private async Task<string?> RestrictionsAsync(CommandContext context, InventorySnapshot snap, RoomInfo room, DateOnly a, DateOnly d, string? overrideReason, CancellationToken ct)
    {
        var violations = StayRestrictions.Violations(snap.Restrictions, room.RoomTypeId, room.RoomType, a, d);
        if (violations.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(overrideReason)) throw InventoryErrors.Restricted(violations);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.ApproveOverride, ct: ct).ConfigureAwait(false);
        return StayRestrictions.OverrideNote(violations, StayRules.OverrideReason(overrideReason));
    }

    private static void EnsurePickup(GroupBlockInfo block, RoomInfo room, DateOnly a, DateOnly d, DateOnly businessDate)
    {
        if (block.EffectiveStatus(businessDate) != "Open") throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Pickup is closed for group block {block.Code}.");
        if (block.Types.All(t => t.RoomTypeId != room.RoomTypeId)) throw HccException.Validation($"Group block {block.Code} holds no {room.RoomType} rooms.");
        if (a < block.Arrival || d > block.Departure)
            throw HccException.Validation($"A pickup for {block.Code} must stay within {StayDates.ToWire(block.Arrival)}–{StayDates.ToWire(block.Departure)}.");
        if (block.Remaining(businessDate, room.RoomTypeId) <= 0) throw InventoryErrors.NotAvailable($"No unpicked {room.RoomType} rooms are left in group block {block.Code}.");
    }

    private static string LocalTime(DateTime utc, string timeZone)
    {
        var tz = CalendarDate.AssertTimeZone(timeZone);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string? TrimNotes(string? notes)
    {
        var n = (notes ?? "").Trim();
        return n.Length == 0 ? null : n.Length > 200 ? n[..200] : n;
    }

    private static string? JoinNotes(string? a, string? b)
    {
        var joined = string.Join(" · ", new[] { a, b }.Where(s => !string.IsNullOrEmpty(s)));
        return joined.Length == 0 ? null : joined.Length > 400 ? joined[..400] : joined;
    }

    private static string Clip(string detail) => detail.Length > 1000 ? detail[..1000] : detail;
}
