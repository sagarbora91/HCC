using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Application.Reservations;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Contracts.WebBooking;
using Hcc.Domain.Billing;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;
using Hcc.Domain.Money;
using Hcc.Domain.WebBooking;

namespace Hcc.Application.WebBooking;

/// <summary>
/// Booking website (module 22, prototype CHN_WEB_*). Public side: quote (read only) and request (idempotent, no inventory hold, no
/// payment). Staff side: queue, confirm (creates the reservation through <see cref="ReservationService"/>, so every inventory,
/// restriction and day-closed rule is the manual-booking rule), decline, simulated deposit link and its simulated "paid" callback
/// (posts a deposit receipt through the billing store). Guest identity-document numbers are never collected (DEC-068).
/// </summary>
public sealed class WebBookingService(IWebBookingStore store, IInventoryStore inventory, InventoryService inventoryService, ReservationService reservations,
    IBillingStore billing, IGstRateResolver gst, ICommandRunner runner, IAccessPolicy access, WebBookingOptions options, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Priced(RoomTypeInfo Type, int RoomsLeft, bool Bookable, string? Reason, long Rate, IReadOnlyList<PricedNight> Nights, long Net, long Tax)
    {
        public bool SoldOut { get; init; }
    }

    private sealed record Claim(long RequestId, string RequestNo);

    // ── Public: quote ──

    public async Task<PublicQuoteResult> QuoteAsync(string? arrival, string? departure, int adults, int children, string? roomType, CancellationToken ct = default)
    {
        var (propertyId, hotel) = await PublicPropertyAsync(ct).ConfigureAwait(false);
        var a = StayDates.Parse(arrival, "arrival");
        var d = StayDates.Parse(departure, "departure");
        var priced = await PriceAsync(null, propertyId, a, d, adults, children, roomType, ct).ConfigureAwait(false);
        if (roomType is { Length: > 0 } && priced.Count == 0) throw HccException.Validation("Choose a room type from the list.");
        return new PublicQuoteResult(hotel, StayDates.ToWire(a), StayDates.ToWire(d), StayDates.Nights(a, d), adults, children,
            priced.Select(ToPublic).ToList(), "Pay at hotel unless the hotel sends you a deposit link. Nothing is held until the hotel confirms.");
    }

    // ── Public: request ──

    public async Task<CommandResult<WebRequestReceipt>> SubmitAsync(string idempotencyKey, SubmitWebRequest body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        WebBookingRules.EnsureHuman(body.Website);
        var guest = WebBookingRules.Guest(body.Guest);
        var phone = WebBookingRules.Phone(body.Phone);
        var email = WebBookingRules.Email(body.Email);
        var notes = WebBookingRules.Notes(body.Notes);
        var a = StayDates.Parse(body.Arrival, "arrival");
        var d = StayDates.Parse(body.Departure, "departure");
        if (string.IsNullOrWhiteSpace(body.RoomType)) throw HccException.Validation("Choose a room type.");
        var (propertyId, _) = await PublicPropertyAsync(ct).ConfigureAwait(false);
        var ctx = new CommandContext(propertyId, WebBookingActors.PublicWeb, "WebRequest", idempotencyKey);
        var normalized = body with { Guest = guest, Phone = phone, Email = email, Notes = notes, Website = null };
        return await runner.RunAsync(ctx, normalized, async (tx, c) =>
        {
            if (await store.PendingCountAsync(propertyId, c).ConfigureAwait(false) >= options.MaxPendingRequests)
                throw new HccException(HccErrorCode.NOT_AVAILABLE, "The hotel is not taking more online requests right now. Please call the hotel.");
            var now = clock.GetUtcNow().UtcDateTime;
            var priced = (await PriceAsync(tx, propertyId, a, d, body.Adults, body.Children, body.RoomType.Trim(), c).ConfigureAwait(false)).FirstOrDefault()
                ?? throw HccException.Validation("Choose a room type from the list.");
            if (!priced.Bookable) throw new HccException(priced.SoldOut ? HccErrorCode.NOT_AVAILABLE : HccErrorCode.VALIDATION, $"{priced.Type.Code}: {priced.Reason}.");
            if (await store.PendingForPhoneAsync(tx, propertyId, phone, c).ConfigureAwait(false) >= WebBookingRules.MaxPendingPerPhone)
                throw HccException.Validation("There are already open requests for this phone number. The hotel will contact you.");
            var row = await store.InsertRequestAsync(tx, new NewWebRequest(propertyId, priced.Type.RoomTypeId, a, d, body.Adults, body.Children, guest, phone, email, notes,
                priced.Net, priced.Tax, JsonSerializer.Serialize(priced.Nights.Select(n => new PublicQuoteNight(StayDates.ToWire(n.Date), n.RatePaise, n.TaxBps, n.TaxPaise)), Json), now), c).ConfigureAwait(false);
            // No guest name, phone or e-mail in the audit text or the event payload.
            await tx.AuditAsync("WebRequestCreated", "web:" + row.PublicNo, $"{row.PublicNo} · {priced.Type.Code} · {StayDates.ToWire(a)}–{StayDates.ToWire(d)} · {row.TotalPaise} paise", c).ConfigureAwait(false);
            await tx.PublishAsync("WebRequestCreated", row.RequestId, new { row.RequestId, row.PublicNo, RoomTypeId = priced.Type.RoomTypeId, Arrival = StayDates.ToWire(a), Departure = StayDates.ToWire(d) }, c).ConfigureAwait(false);
            return new WebRequestReceipt(row.PublicNo, WebRequestStatuses.Pending, priced.Type.Code, StayDates.ToWire(a), StayDates.ToWire(d), body.Adults, body.Children,
                row.NetPaise, row.TaxPaise, row.TotalPaise, "Pending confirmation by the hotel. No room is held and no payment has been taken.");
        }, ct).ConfigureAwait(false);
    }

    // ── Staff: queue ──

    public async Task<WebRequestList> ListAsync(int propertyId, int principalId, string? status, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, WebBookingPermissions.Review, ct: ct).ConfigureAwait(false);
        if (status is not null and not (WebRequestStatuses.Pending or WebRequestStatuses.Confirming or WebRequestStatuses.Confirmed or WebRequestStatuses.Declined))
            throw HccException.Validation("Status must be Pending, Confirming, Confirmed or Declined.");
        var rows = await store.ListAsync(propertyId, status, 200, ct).ConfigureAwait(false);
        return new WebRequestList(rows.Select(r => new WebRequestSummary(r.RequestId, r.PublicNo, r.Status, r.Guest, WebBookingRules.MaskPhone(r.Phone), r.RoomType,
            StayDates.ToWire(r.Arrival), StayDates.ToWire(r.Departure), r.Adults, r.Children, r.TotalPaise, Wire(r.CreatedUtc), r.ReservationNo, r.Reason,
            InventoryErrors.VersionToWire(r.RowVersion))).ToList(), await store.PendingCountAsync(propertyId, ct).ConfigureAwait(false));
    }

    public async Task<WebRequestDetail> GetAsync(int propertyId, int principalId, long requestId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, WebBookingPermissions.Review, ct: ct).ConfigureAwait(false);
        var r = await store.GetAsync(propertyId, requestId, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Web request");
        var now = clock.GetUtcNow().UtcDateTime;
        var nights = JsonSerializer.Deserialize<List<PublicQuoteNight>>(r.QuoteNightsJson, Json) ?? [];
        var links = (await store.LinksAsync(r.RequestId, ct).ConfigureAwait(false)).Select(l => ToView(l, now)).ToList();
        var contact = await GuestContactMask.CanSeeAsync(access, propertyId, principalId, ct).ConfigureAwait(false);   // F-L06
        return new WebRequestDetail(r.RequestId, r.PublicNo, r.Status, r.Guest, GuestContactMask.Phone(contact, r.Phone)!, GuestContactMask.Email(contact, r.Email), r.Notes, r.RoomTypeId, r.RoomType, StayDates.ToWire(r.Arrival),
            StayDates.ToWire(r.Departure), r.Adults, r.Children, r.NetPaise, r.TaxPaise, r.TotalPaise, nights, Wire(r.CreatedUtc), r.ReservationId, r.ReservationNo,
            r.Reason, r.DecidedBy, InventoryErrors.VersionToWire(r.RowVersion), links);
    }

    // ── Staff: confirm (claim → quote → reservation → complete; each step idempotent under a derived key) ──

    public async Task<CommandResult<ConfirmWebRequestResult>> ConfirmAsync(CommandContext context, ConfirmWebRequestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, WebBookingPermissions.Review, ct: ct).ConfigureAwait(false);
        var key = context.IdempotencyKey;
        var claim = await runner.RunAsync(context with { Operation = "WebClaim", IdempotencyKey = WebBookingRules.StepKey(key, "c") }, request, async (tx, c) =>
        {
            var r = await store.GetForUpdateAsync(tx, context.PropertyId, request.RequestId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Web request");
            if (r.Status == WebRequestStatuses.Confirmed || r.Status == WebRequestStatuses.Declined)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This request is already {r.Status.ToLowerInvariant()}.");
            if (r.Status == WebRequestStatuses.Confirming && r.ClaimedBy != context.PrincipalId)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "Someone else is confirming this request right now.");
            await store.SetStateAsync(tx, r.RequestId, WebRequestStatuses.Confirming, null, context.PrincipalId, null, null, null, c).ConfigureAwait(false);
            await tx.AuditAsync("WebRequestClaimed", "web:" + r.PublicNo, r.PublicNo + " · confirmation started", c).ConfigureAwait(false);
            return new Claim(r.RequestId, r.PublicNo);
        }, ct).ConfigureAwait(false);

        var row = await store.GetAsync(context.PropertyId, request.RequestId, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Web request");
        var now = clock.GetUtcNow().UtcDateTime;
        // Re-entry (replay, or a retry after a crash between the reservation and the final step): adopt the reservation already made for this request.
        ReservationDetails booked;
        long bookedTotal;
        if (await store.FindReservationIdAsync(context.PropertyId, row.PublicNo, ct).ConfigureAwait(false) is { } existing)
        {
            booked = await reservations.GetAsync(context.PropertyId, context.PrincipalId, existing, ct).ConfigureAwait(false);
            bookedTotal = booked.TotalPaise;
        }
        else
        {
            var snap = await inventory.LoadSnapshotAsync(context.PropertyId, row.Arrival, row.Departure, now, null, ct).ConfigureAwait(false);
            var holds = AvailabilityRules.GroupHolds(snap);
            var party = row.Adults + row.Children;
            var room = snap.Rooms.Where(r => r.RoomTypeId == row.RoomTypeId).OrderBy(r => r, RoomNumberComparer.Instance)
                .FirstOrDefault(r => AvailabilityRules.Exclusion(snap, holds, r, row.Arrival, row.Departure, party, 0, null).Count == 0)
                ?? throw InventoryErrors.NotAvailable($"{row.RoomType} is sold out for these dates. Decline the request or offer other dates.");
            var quote = (await inventoryService.QuoteStayAsync(context with { Operation = "QuoteStay", IdempotencyKey = WebBookingRules.StepKey(key, "q") },
                new QuoteStayRequest(StayDates.ToWire(row.Arrival), StayDates.ToWire(row.Departure), row.Adults, row.Children, room.RoomId), ct).ConfigureAwait(false)).Value;
            var made = (await reservations.CreateAsync(context with { Operation = "CreateReservation", IdempotencyKey = WebBookingRules.StepKey(key, "r") },
                new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput(row.Guest, row.Phone), room.RoomId, false, null, null, "Website",
                    $"Web request {row.PublicNo}" + (row.Notes is null ? "" : " · " + row.Notes)), ct).ConfigureAwait(false)).Value;
            booked = await reservations.GetAsync(context.PropertyId, context.PrincipalId, made.ReservationId, ct).ConfigureAwait(false);
            bookedTotal = quote.TotalPaise;
        }

        return await runner.RunAsync(context with { Operation = "ConfirmWebRequest", IdempotencyKey = WebBookingRules.StepKey(key, "f") }, request, async (tx, c) =>
        {
            var r = await store.GetForUpdateAsync(tx, context.PropertyId, request.RequestId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Web request");
            if (r.Status != WebRequestStatuses.Confirming || r.ClaimedBy != context.PrincipalId)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is not being confirmed by you.");
            await store.SetStateAsync(tx, r.RequestId, WebRequestStatuses.Confirmed, null, null, context.PrincipalId, clock.GetUtcNow().UtcDateTime, booked.ReservationId, c).ConfigureAwait(false);
            await tx.AuditAsync("WebRequestConfirmed", "web:" + r.PublicNo, $"{r.PublicNo} → {booked.PublicNo} · room {booked.RoomNo}", c).ConfigureAwait(false);
            await tx.PublishAsync("WebRequestConfirmed", r.RequestId, new { r.RequestId, r.PublicNo, booked.ReservationId, ReservationNo = booked.PublicNo }, c).ConfigureAwait(false);
            return new ConfirmWebRequestResult(r.RequestId, r.PublicNo, WebRequestStatuses.Confirmed, booked.ReservationId, booked.PublicNo, booked.Version, r.TotalPaise,
                bookedTotal, bookedTotal != r.TotalPaise);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WebRequestDecision>> DeclineAsync(CommandContext context, DeclineWebRequestRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, WebBookingPermissions.Review, ct: ct).ConfigureAwait(false);
        var reason = WebBookingRules.Reason(request.Reason);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var r = await store.GetForUpdateAsync(tx, context.PropertyId, request.RequestId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Web request");
            if (r.Status is not (WebRequestStatuses.Pending or WebRequestStatuses.Confirming))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"This request is already {r.Status.ToLowerInvariant()}.");
            await store.SetStateAsync(tx, r.RequestId, WebRequestStatuses.Declined, reason, null, context.PrincipalId, clock.GetUtcNow().UtcDateTime, null, c).ConfigureAwait(false);
            await tx.AuditAsync("WebRequestDeclined", "web:" + r.PublicNo, $"{r.PublicNo} declined · {reason}", c).ConfigureAwait(false);
            await tx.PublishAsync("WebRequestDeclined", r.RequestId, new { r.RequestId, r.PublicNo }, c).ConfigureAwait(false);
            var after = await store.GetForUpdateAsync(tx, context.PropertyId, r.RequestId, c).ConfigureAwait(false);
            return new WebRequestDecision(r.RequestId, r.PublicNo, WebRequestStatuses.Declined, InventoryErrors.VersionToWire(after!.RowVersion));
        }, ct).ConfigureAwait(false);
    }

    // ── Staff: simulated deposit link ──

    public async Task<CommandResult<WebDepositLinkResult>> CreateDepositLinkAsync(CommandContext context, CreateWebDepositLinkRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, WebBookingPermissions.Review, ct: ct).ConfigureAwait(false);
        // F-L03: the plaintext token is made outside the command and never enters its stored (idempotency) result; only its hash is kept.
        var token = WebBookingRules.NewToken();
        var made = await runner.RunAsync(context, request, async (tx, c) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var r = await store.GetForUpdateAsync(tx, context.PropertyId, request.RequestId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Web request");
            if (r.Status != WebRequestStatuses.Confirmed) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A deposit link can be made only for a confirmed request.");
            var amount = WebBookingRules.DepositAmount(request.AmountPaise, new Paise(r.TotalPaise), options.DepositBps);
            if ((await store.LinksAsync(r.RequestId, c).ConfigureAwait(false)).Any(l => l.Status == DepositLinkStatuses.Paid))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "The deposit was already paid.");
            if (await store.OpenLinkForUpdateAsync(tx, r.RequestId, c).ConfigureAwait(false) is { } open)
            {
                if (open.ExpiresUtc > now) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request already has an open deposit link.");
                await store.SetLinkStateAsync(tx, open.LinkId, DepositLinkStatuses.Cancelled, null, null, null, null, c).ConfigureAwait(false);
            }

            var link = await store.InsertLinkAsync(tx, r.RequestId, WebBookingRules.TokenHash(token), amount.Value, now + options.DepositLife, context.PrincipalId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("WebDepositLinkCreated", "web:" + r.PublicNo, $"{r.PublicNo} · deposit link {amount} · expires {Wire(link.ExpiresUtc)}", c).ConfigureAwait(false);
            await tx.PublishAsync("WebDepositLinkCreated", r.RequestId, new { r.RequestId, link.LinkId, AmountPaise = amount.Value }, c).ConfigureAwait(false);
            return new WebDepositLinkResult(link.LinkId, r.RequestId, null, null, amount.Value, Wire(link.ExpiresUtc), DepositLinkStatuses.Open);
        }, ct).ConfigureAwait(false);
        return made.Replayed ? made : new CommandResult<WebDepositLinkResult>(made.Value with { Token = token, Url = "/pay/" + token }, false);
    }

    public async Task<PublicDepositView> DepositViewAsync(string token, CancellationToken ct = default)
    {
        var (propertyId, hotel) = await PublicPropertyAsync(ct).ConfigureAwait(false);
        var found = await store.FindLinkAsync(propertyId, WebBookingRules.TokenHash(token), ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Payment link");
        var r = await store.GetAsync(propertyId, found.RequestId, ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Payment link");
        var view = ToView(found.Link, clock.GetUtcNow().UtcDateTime);
        return new PublicDepositView(hotel, r.PublicNo, view.Status, found.Link.AmountPaise, view.ExpiresAt, StayDates.ToWire(r.Arrival), StayDates.ToWire(r.Departure), r.RoomType);
    }

    /// <summary>The simulated gateway callback (entered by staff): posts the deposit receipt on the reservation's folio and closes the link, in one transaction.</summary>
    public async Task<CommandResult<WebDepositPaid>> MarkDepositPaidAsync(CommandContext context, MarkWebDepositPaidRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, WebBookingPermissions.Review, ct: ct).ConfigureAwait(false);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, BillingPermissions.DepositRecord, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        var hash = WebBookingRules.TokenHash(request.Token);
        var gatewayRef = WebBookingRules.Clean(request.GatewayRef, 60);
        if (gatewayRef.Length < 4) throw HccException.Validation("Enter the gateway reference (at least 4 characters).");
        return await runner.RunAsync(context, new { hash = Convert.ToHexString(hash), gatewayRef }, async (tx, c) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var found = await store.FindLinkForUpdateAsync(tx, context.PropertyId, hash, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Payment link");
            var link = found.Link;
            if (link.Status == DepositLinkStatuses.Paid) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This link was already paid.");
            if (link.Status != DepositLinkStatuses.Open) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This payment link is no longer valid.");
            if (link.ExpiresUtc <= now) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This payment link has expired. Make a new one.");
            var r = await store.GetForUpdateAsync(tx, context.PropertyId, found.RequestId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Web request");
            if (r.ReservationId is not { } reservationId) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The request has no reservation.");
            var folioId = await billing.EnsureFolioAsync(tx, reservationId, c).ConfigureAwait(false);
            var f = await billing.LockFolioAsync(tx, folioId, c).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Folio");
            if (!f.IsOpen) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This booking is no longer open.");
            var amount = Paise.FromWire(link.AmountPaise).EnsureReceiptAmount();
            var reference = "SIM-" + gatewayRef;
            var receiptId = await billing.InsertReceiptAsync(tx, new NewReceipt(f.FolioId, ReceiptKind.Deposit, Tenders.Upi, amount, null, f.BusinessDate, null, null,
                reference, null, context.PrincipalId, now), c).ConfigureAwait(false);
            await billing.TouchFolioAsync(tx, f.FolioId, c).ConfigureAwait(false);
            await tx.AuditAsync("RECEIPT_RECORDED", "folio:" + f.FolioId.ToString(CultureInfo.InvariantCulture),
                $"deposit · {Tenders.Upi} · {amount} · receipt {receiptId} · web deposit link (simulated)", c).ConfigureAwait(false);
            await tx.PublishAsync("ReceiptRecorded", f.FolioId, new { folioId = f.FolioId, receiptId, kind = FolioTotals.KindCode(ReceiptKind.Deposit), tender = Tenders.Upi,
                amountPaise = amount.Value, businessDate = f.BusinessDate }, c).ConfigureAwait(false);
            await store.SetLinkStateAsync(tx, link.LinkId, DepositLinkStatuses.Paid, now, context.PrincipalId, reference, receiptId, c).ConfigureAwait(false);
            await tx.AuditAsync("WebDepositPaid", "web:" + r.PublicNo, $"{r.PublicNo} · deposit {amount} paid (simulated callback {reference})", c).ConfigureAwait(false);
            await tx.PublishAsync("WebDepositPaid", r.RequestId, new { r.RequestId, link.LinkId, receiptId, AmountPaise = amount.Value }, c).ConfigureAwait(false);
            return new WebDepositPaid(link.LinkId, r.RequestId, receiptId, f.FolioId, amount.Value, DepositLinkStatuses.Paid);
        }, ct).ConfigureAwait(false);
    }

    // ── Shared ──

    private async Task<(int PropertyId, string Name)> PublicPropertyAsync(CancellationToken ct)
        => await store.PublicPropertyAsync(ct).ConfigureAwait(false) ?? throw InventoryErrors.NotFound("Hotel");

    private async Task<IReadOnlyList<Priced>> PriceAsync(ICommandTransaction? tx, int propertyId, DateOnly a, DateOnly d, int adults, int children, string? typeCode,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var property = await inventory.GetPropertyAsync(propertyId, tx, ct).ConfigureAwait(false);
        StayRules.Stay(StayDates.ToWire(a), StayDates.ToWire(d), property.BusinessDate);
        var party = StayRules.Party(adults, children);
        var snap = await inventory.LoadSnapshotAsync(propertyId, a, d, now, tx, ct).ConfigureAwait(false);
        var holds = AvailabilityRules.GroupHolds(snap);
        var types = snap.RoomTypes.Where(t => string.IsNullOrWhiteSpace(typeCode) || string.Equals(t.Code, typeCode.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.RoomTypeId).ToList();
        var list = new List<Priced>();
        foreach (var t in types)
        {
            var left = snap.Rooms.Where(r => r.RoomTypeId == t.RoomTypeId)
                .Count(r => AvailabilityRules.Exclusion(snap, holds, r, a, d, party, 0, null).Count == 0);
            var violations = StayRestrictions.Violations(snap.Restrictions, t.RoomTypeId, t.Code, a, d);
            string? reason = party > t.MaxOccupancy ? $"Sleeps at most {t.MaxOccupancy} guests including children"
                : violations.Count > 0 ? string.Join(" ", violations.Select(v => v.Message).Select(m => m.TrimEnd('.')))
                : left == 0 ? "Sold out for these dates" : null;
            var rate = StayPricing.RateOn(await inventory.GetTariffsAsync(t.RoomTypeId, tx, ct).ConfigureAwait(false), t.RackRatePaise, a);
            var bps = new Dictionary<DateOnly, int>();
            foreach (var (n, g) in await gst.RoomNightsAsync(tx, propertyId, a, d, new Paise(rate), ct).ConfigureAwait(false)) bps[n] = g.Bps;
            var nights = StayPricing.Nights(a, d, rate, n => bps[n]);
            list.Add(new Priced(t, left, reason is null, reason, rate, nights, nights.Sum(n => n.RatePaise), nights.Sum(n => n.TaxPaise))
            {
                SoldOut = reason is not null && party <= t.MaxOccupancy && violations.Count == 0,
            });
        }

        return list;
    }

    private static PublicRoomTypeQuote ToPublic(Priced p) => new(p.Type.RoomTypeId, p.Type.Code, p.Type.MaxOccupancy, WebBookingRules.AvailabilityBand(p.RoomsLeft), p.Bookable, p.Reason, p.Rate,
        p.Nights.Count, p.Net, p.Tax, p.Net + p.Tax, p.Nights.Select(n => new PublicQuoteNight(StayDates.ToWire(n.Date), n.RatePaise, n.TaxBps, n.TaxPaise)).ToList());

    private static DepositLinkView ToView(DepositLinkRow l, DateTime now)
        => new(l.LinkId, l.Status == DepositLinkStatuses.Open && l.ExpiresUtc <= now ? DepositLinkStatuses.Expired : l.Status, l.AmountPaise, Wire(l.ExpiresUtc),
            l.PaidUtc is { } p ? Wire(p) : null, l.PaidRef, l.ReceiptId);

    private static string Wire(DateTime utc) => InventoryService.ToWireInstant(utc);
}
