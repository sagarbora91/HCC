using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.GuestIds;
using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Housekeeping;
using Hcc.Domain.Stays;

namespace Hcc.Application.Stays;

/// <summary>
/// Stays use cases: RegisterGuest, CheckIn, CheckoutStay (contracts §3; WF-STAY-001, WF-CHECKOUT-001). Each command checks
/// its PERM id through <see cref="IAccessPolicy"/> before the transaction (so a replayed key is re-authorized, as in the
/// prototype), then runs in one <see cref="ICommandRunner"/> transaction with audit (+ outbox for checkout).
/// Guest ID details are never written to audit details or logs. DEC-068: a full ID number in the draft is sealed by
/// <paramref name="guestIds"/> (AES-GCM) in the same transaction and replaced by a keyed fingerprint in the idempotency hash;
/// without the service a number is refused (never stored in clear).
/// </summary>
public sealed class StaysCommands(ICommandRunner runner, IAccessPolicy policy, IStaysStore store, IStayFolioPort folios, TimeProvider clock,
    GuestIdService? guestIds = null)
{
    public const string OpRegisterGuest = "RegisterGuest";
    public const string OpCheckIn = "CheckIn";
    public const string OpCheckoutStay = "CheckoutStay";

    private static readonly string[] Registrable = [ReservationStatus.Held, ReservationStatus.Confirmed, ReservationStatus.InHouse];

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<RegisterGuestResult>> RegisterGuestAsync(CommandContext ctx, RegisterGuestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.ReservationId <= 0) throw HccException.Validation("Select a reservation.");
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var guestExpected = req.GuestExpectedVersion is null ? null : VersionToken.Parse(req.GuestExpectedVersion, "guestExpectedVersion");
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StaysPermissions.GuestRegister, null, null, ct).ConfigureAwait(false);
        if (guestIds is null && GuestIdService.HasNumber(req.Guest))
            throw new HccException(HccErrorCode.UNAVAILABLE, "Full ID numbers cannot be stored: guest ID encryption is not configured on this server.");
        var hashed = guestIds?.HashablePayload(req) ?? req;

        return await runner.RunAsync(ctx with { Operation = OpRegisterGuest }, hashed, async (tx, c) =>
        {
            var res = await store.LockReservationAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
            VersionToken.EnsureMatches(res.Version, expected, "This reservation");
            if (!Registrable.Contains(res.Status)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This reservation can no longer be changed.");

            var linked = await store.RegisteredGuestAsync(tx, res.ReservationId, c).ConfigureAwait(false);
            long guestId;
            if (req.Guest is not null)
            {
                var draft = ToDraft(req.Guest);
                var errors = GuestRules.Validate(draft, registration: true);
                if (errors.Count > 0) throw HccException.Validation(GuestRules.Summary(errors));
                var clean = GuestRules.Clean(draft);
                var target = req.GuestId ?? linked;
                if (target is { } existingId)
                {
                    var g = await store.LockGuestAsync(tx, ctx.PropertyId, existingId, c).ConfigureAwait(false)
                        ?? throw new HccException(HccErrorCode.NOT_FOUND, "Guest profile not found.");
                    if (guestExpected is null) throw HccException.Validation("guestExpectedVersion is required to change an existing guest profile.");
                    VersionToken.EnsureMatches(g.Version, guestExpected, "This guest profile");
                    if (guestIds is not null) clean = await guestIds.ApplyAsync(tx, ctx.PropertyId, g.GuestId, clean, g.Draft, c).ConfigureAwait(false);
                    await store.UpdateGuestAsync(tx, g.GuestId, guestExpected, clean, verified: true, c).ConfigureAwait(false);
                    guestId = g.GuestId;
                }
                else
                {
                    guestId = await store.InsertGuestAsync(tx, ctx.PropertyId, clean, verified: true, "manual", UtcNow, c).ConfigureAwait(false);
                    if (guestIds is not null) await guestIds.ApplyAsync(tx, ctx.PropertyId, guestId, clean, null, c).ConfigureAwait(false);
                }
            }
            else
            {
                // Production has no auto-created demo profile (prototype REGISTER {id}): the linked profile must already be complete.
                if (linked is null) throw HccException.Validation("Enter the guest’s registration details.");
                var g = await store.LockGuestAsync(tx, ctx.PropertyId, linked.Value, c).ConfigureAwait(false)
                    ?? throw new HccException(HccErrorCode.NOT_FOUND, "Guest profile not found.");
                var errors = GuestRules.Validate(g.Draft, registration: true);
                if (errors.Count > 0) throw HccException.Validation(GuestRules.Summary(errors));
                if (!g.Verified) await store.UpdateGuestAsync(tx, g.GuestId, g.Version, g.Draft, verified: true, c).ConfigureAwait(false);
                guestId = g.GuestId;
            }

            await store.UpsertRegistrationAsync(tx, res.ReservationId, guestId, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            // Audit carries ids only — never name, phone or ID details.
            await tx.AuditAsync("REGISTER", Target("reservation", res.ReservationId), $"guest {guestId} · registration verified", c).ConfigureAwait(false);
            var version = await store.GuestVersionAsync(tx, guestId, c).ConfigureAwait(false);
            return new RegisterGuestResult(res.ReservationId, guestId, VersionToken.ToWire(version), true);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CheckInResult>> CheckInAsync(CommandContext ctx, CheckInRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.ReservationId <= 0) throw HccException.Validation("Select a reservation.");
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StaysPermissions.CheckIn, null, null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpCheckIn }, req, async (tx, c) =>
        {
            var res = await store.LockReservationAsync(tx, ctx.PropertyId, req.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
            VersionToken.EnsureMatches(res.Version, expected, "This reservation");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var guestId = await store.RegisteredGuestAsync(tx, res.ReservationId, c).ConfigureAwait(false);
            RoomRecord? room = null;
            var blocked = false;
            if (res.RoomId is { } roomId)
            {
                room = await store.LockRoomAsync(tx, ctx.PropertyId, roomId, c).ConfigureAwait(false);
                blocked = await store.RoomBlockedDuringAsync(tx, roomId, res.Arrival, res.Departure, c).ConfigureAwait(false);
            }

            StayRules.AssertCanCheckIn(new CheckInFacts(res.Status, res.Arrival, res.Departure, bd, guestId is not null,
                room?.Hk, blocked, room?.OccupyingStayId is not null, res.AllocationIntact));

            var folioId = await folios.EnsureFolioAsync(tx, res.ReservationId, c).ConfigureAwait(false);
            await store.SetReservationStatusAsync(tx, res.ReservationId, res.Version, ReservationStatus.InHouse, c).ConfigureAwait(false);
            var (stayId, stayVersion) = await store.InsertStayAsync(tx, res, guestId!.Value, folioId, room!.RoomId, bd, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("CHECKIN", Target("stay", stayId),
                $"reservation {res.ReservationId} → room {room.RoomNo} · folio {folioId} · business date {StayDates.ToWire(bd)}", c).ConfigureAwait(false);
            return new CheckInResult(stayId, res.ReservationId, folioId, room.RoomId, room.RoomNo, StayDates.ToWire(bd), VersionToken.ToWire(stayVersion));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<CheckoutStayResult>> CheckoutStayAsync(CommandContext ctx, CheckoutStayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (req.StayId <= 0) throw HccException.Validation("Select a stay.");
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, StaysPermissions.CheckOut, null, null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpCheckoutStay }, req, async (tx, c) =>
        {
            var stay = await store.LockStayAsync(tx, ctx.PropertyId, req.StayId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
            StayRules.AssertCanCheckOut(stay.Status); // a second checkout (other key) → INVALID_TRANSITION
            VersionToken.EnsureMatches(stay.Version, expected, "This stay");
            var res = await store.LockReservationAsync(tx, ctx.PropertyId, stay.ReservationId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
            var room = await store.LockRoomAsync(tx, ctx.PropertyId, stay.RoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);

            await folios.AssertCheckoutSettledAsync(tx, new CheckoutFolioCheck(stay.FolioId, stay.ReservationId, stay.Arrival, stay.Departure, req.CompanyCreditApprovalId), c).ConfigureAwait(false);

            var version = await store.CheckOutStayAsync(tx, stay.StayId, expected, bd, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await store.SetReservationStatusAsync(tx, res.ReservationId, res.Version, ReservationStatus.Departed, c).ConfigureAwait(false);
            await store.SetRoomHkAsync(tx, room.RoomId, RoomHk.Dirty, c).ConfigureAwait(false);
            await tx.AuditAsync("CHECKOUT", Target("stay", stay.StayId),
                $"reservation {res.ReservationId} · departed; room {room.RoomNo} dirty · business date {StayDates.ToWire(bd)}", c).ConfigureAwait(false);
            var outboxId = await tx.PublishAsync(StaysEvents.StayDeparted, stay.StayId,
                new StayDepartedEvent(stay.StayId, res.ReservationId, ctx.PropertyId, room.RoomId, StayDates.ToWire(bd)), c).ConfigureAwait(false);
            return new CheckoutStayResult(stay.StayId, res.ReservationId, room.RoomId, room.RoomNo, RoomHk.Dirty, StayDates.ToWire(bd), outboxId, VersionToken.ToWire(version));
        }, ct).ConfigureAwait(false);
    }

    internal static GuestDraft ToDraft(GuestDraftDto d) => new(d.Name, d.Phone, d.Email, d.Address, d.Nationality, d.IdType, d.IdLast4,
        d.Company, d.Gstin, d.Preferences, d.Notes, d.Party?.Select(p => new GuestPartyMember(p.Name, p.AgeBand, p.Relation)).ToList(), d.IdNumber);

    private static string Target(string kind, long id) => kind + "-" + id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Guest reads with server-side field masks (RBAC §9; prototype <c>guestView</c>).</summary>
public sealed class StaysQueries(IAccessPolicy policy, IStaysReadStore reads)
{
    public async Task<GuestView> GetGuestAsync(int propertyId, int principalId, long guestId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, StaysPermissions.GuestView, null, null, ct).ConfigureAwait(false);
        var g = await reads.GetGuestAsync(propertyId, guestId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Guest profile not found.");
        var idDetails = await policy.HasAsync(propertyId, principalId, StaysPermissions.GuestIdDetails, null, ct).ConfigureAwait(false);
        // Prototype guestAccess(): contact details follow the registration permission (cashier sees masked phone/email).
        var contact = await policy.HasAsync(propertyId, principalId, StaysPermissions.GuestRegister, null, ct).ConfigureAwait(false);
        return Mask(g, idDetails, contact);
    }

    // DEC-068: the view never carries the full number. ID-view holders see XXXX-XXXX-1234 and may call RevealGuestId (audited).

    public static GuestView Mask(GuestRecord g, bool idDetails, bool contact)
    {
        ArgumentNullException.ThrowIfNull(g);
        var d = g.Draft;
        return new GuestView(g.GuestId, d.Name ?? string.Empty,
            contact ? d.Phone ?? string.Empty : GuestRules.MaskPhone(d.Phone),
            contact ? d.Email : GuestRules.MaskEmail(d.Email),
            d.Address,
            d.Nationality ?? "Indian",
            idDetails ? d.IdType : GuestRules.Restricted,
            idDetails ? d.IdLast4 : GuestRules.Restricted,
            d.Company, d.Gstin,
            (d.Party ?? []).Select(p => new GuestPartyDto(p.Name, p.AgeBand, p.Relation)).ToList(),
            g.Verified, !idDetails, VersionToken.ToWire(g.Version),
            idDetails ? Domain.GuestIds.GuestIdRules.Mask(d.IdLast4) : GuestRules.Restricted,
            g.IdOnFile, idDetails && g.IdOnFile);
    }
}
