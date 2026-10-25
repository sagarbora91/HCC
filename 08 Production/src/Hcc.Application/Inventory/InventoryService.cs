using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;

namespace Hcc.Application.Inventory;

/// <summary>
/// GetAvailability, QuoteStay and group blocks (contracts §3). Authorization first (<see cref="IAccessPolicy"/>), every command
/// through <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox in one transaction). Times from the injected clock.
/// </summary>
public sealed class InventoryService(IInventoryStore store, ICommandRunner runner, IAccessPolicy access, ITaxRateSource taxes, TimeProvider clock)
{
    /// <summary>Quote lifetime. Wave 1 default; a configuration setting later (owner decision listed in the P2 report).</summary>
    public static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(30);

    public const string DefaultRatePlan = "BAR";

    public async Task<AvailabilityResult> GetAvailabilityAsync(int propertyId, int principalId, AvailabilityQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await access.RequireAsync(propertyId, principalId, InventoryPermissions.ViewReservations, ct: ct).ConfigureAwait(false);
        var a = StayDates.Parse(query.Arrival, "arrival");
        var d = StayDates.Parse(query.Departure, "departure");
        var nights = StayDates.Nights(a, d);
        if (nights > StayRules.MaxNights) throw HccException.Validation($"Departure must follow arrival (maximum {StayRules.MaxNights} nights).");
        var party = StayRules.Party(query.Adults, query.Children);
        RatePlan(query.RatePlanId);

        var now = clock.GetUtcNow().UtcDateTime;
        var property = await store.GetPropertyAsync(propertyId, null, ct).ConfigureAwait(false);
        var snap = await store.LoadSnapshotAsync(propertyId, a, d, now, null, ct).ConfigureAwait(false);
        if (query.GroupBlockId is { } bid && snap.GroupBlocks.All(b => b.GroupBlockId != bid)) throw InventoryErrors.NotFound("Group block");
        var holds = AvailabilityRules.GroupHolds(snap);

        var types = snap.RoomTypes.Where(t => query.RoomTypeId is null || t.RoomTypeId == query.RoomTypeId).OrderBy(t => t.SortOrder).ThenBy(t => t.RoomTypeId).ToList();
        var violations = types.ToDictionary(t => t.RoomTypeId, t => StayRestrictions.Violations(snap.Restrictions, t.RoomTypeId, t.Code, a, d));
        var rooms = snap.Rooms.Where(r => violations.ContainsKey(r.RoomTypeId)).OrderBy(r => r, RoomNumberComparer.Instance).Select(r =>
        {
            var reasons = AvailabilityRules.Exclusion(snap, holds, r, a, d, party, 0, query.GroupBlockId).ToList();
            if (violations[r.RoomTypeId].Count > 0) reasons.Add(AvailabilityReasons.Restricted);
            var hold = AvailabilityRules.RoomHold(holds, r.RoomId, a, d, query.GroupBlockId);
            return new RoomAvailability(r.RoomId, r.RoomNo, r.RoomTypeId, r.RoomType, r.Capacity, reasons.Count == 0, reasons, hold?.BlockCode);
        }).ToList();
        var typeRows = types.Select(t => new RoomTypeAvailability(t.RoomTypeId, t.Code, t.MaxOccupancy,
            rooms.Count(r => r.RoomTypeId == t.RoomTypeId && r.Available), violations[t.RoomTypeId])).ToList();
        return new AvailabilityResult(StayDates.ToWire(property.BusinessDate), StayDates.ToWire(a), StayDates.ToWire(d), nights, party, typeRows, rooms);
    }

    public async Task<CommandResult<QuoteResult>> QuoteStayAsync(CommandContext context, QuoteStayRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.CreateReservation, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var property = await store.GetPropertyAsync(context.PropertyId, tx, c).ConfigureAwait(false);
            var (a, d) = StayRules.Stay(request.Arrival, request.Departure, property.BusinessDate);
            var party = StayRules.Party(request.Adults, request.Children);
            var plan = RatePlan(request.RatePlanId);
            var snap = await store.LoadSnapshotAsync(context.PropertyId, a, d, now, tx, c).ConfigureAwait(false);

            RoomTypeInfo type;
            int? roomId = null;
            if (request.RoomId is { } rid)
            {
                var room = snap.Rooms.FirstOrDefault(r => r.RoomId == rid) ?? throw HccException.Validation("Choose a room.");
                if (request.RoomTypeId is { } tid && tid != room.RoomTypeId) throw HccException.Validation("The room is not of the chosen room type.");
                StayRules.Capacity(room, party);
                type = snap.RoomTypes.First(t => t.RoomTypeId == room.RoomTypeId);
                roomId = rid;
            }
            else
            {
                type = snap.RoomTypes.FirstOrDefault(t => t.RoomTypeId == request.RoomTypeId) ?? throw HccException.Validation("Choose a room or a room type.");
                if (party > type.MaxOccupancy) throw HccException.Validation($"Room type {type.Code} accommodates at most {type.MaxOccupancy} guests including children.");
            }

            var tariffs = await store.GetTariffsAsync(type.RoomTypeId, tx, c).ConfigureAwait(false);
            var rate = StayPricing.RateOn(tariffs, type.RackRatePaise, a);
            var nights = await PriceAsync(context.PropertyId, a, d, rate, c).ConfigureAwait(false);
            var quote = await store.InsertQuoteAsync(tx, new QuoteDraft(context.PropertyId, context.PrincipalId, type.RoomTypeId, roomId, a, d,
                request.Adults, request.Children, plan, nights, now, now + QuoteLifetime), c).ConfigureAwait(false);
            var violations = StayRestrictions.Violations(snap.Restrictions, type.RoomTypeId, type.Code, a, d);
            await tx.AuditAsync("QuoteStay", $"quote:{quote.QuoteId}",
                $"{type.Code}{(roomId is null ? "" : " room " + snap.Rooms.First(r => r.RoomId == roomId).RoomNo)} · {StayDates.ToWire(a)}–{StayDates.ToWire(d)} · {quote.TotalPaise} paise" +
                (violations.Count > 0 ? " · violations " + string.Join(",", violations.Select(v => v.Code)) : ""), c).ConfigureAwait(false);
            return ToResult(quote, violations);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<GroupBlockResult>> CreateGroupBlockAsync(CommandContext context, CreateGroupBlockRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.CreateReservation, ct: ct).ConfigureAwait(false);
        await store.SweepAsync(context.PropertyId, context.PrincipalId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var property = await store.GetPropertyAsync(context.PropertyId, tx, c).ConfigureAwait(false);
            var code = (request.Code ?? "").Trim();
            if (code.Length is < 1 or > 20) throw HccException.Validation("Enter a block code of 1–20 characters.");
            var name = (request.Name ?? "").Trim();
            if (name.Length is < 2 or > 200) throw HccException.Validation("Enter the group name.");
            var (a, d) = StayRules.Stay(request.Arrival, request.Departure, property.BusinessDate);
            var cutoff = StayDates.Parse(request.Cutoff, "cutoff");
            if (cutoff < property.BusinessDate || cutoff > a) throw HccException.Validation("The cut-off must be between today's business date and the arrival date.");
            var allot = (request.Allotments ?? []).Where(x => x.Rooms != 0).ToList();
            if (allot.Count == 0) throw HccException.Validation("Hold at least one room.");
            if (allot.Any(x => x.Rooms is < 1 or > 100)) throw HccException.Validation("Rooms per type must be 1–100.");
            if (allot.Select(x => x.RoomTypeId).Distinct().Count() != allot.Count) throw HccException.Validation("Each room type may appear once.");
            if (allot.Any(x => x.RatePaise is <= 0 or > ApiConventions.MaxPaisePerField)) throw HccException.Validation("Enter a block rate between ₹0.01 and ₹10 crore.");
            var result = await store.CreateGroupBlockAsync(tx, new GroupBlockDraft(context.PropertyId, code, name, a, d, cutoff, allot), c).ConfigureAwait(false);
            await tx.AuditAsync("CreateGroupBlock", code,
                $"{name} · {StayDates.ToWire(a)}–{StayDates.ToWire(d)} · cut-off {StayDates.ToWire(cutoff)} · " +
                string.Join(", ", allot.Select(x => $"type {x.RoomTypeId}×{x.Rooms}")), c).ConfigureAwait(false);
            await tx.PublishAsync("GroupBlockCreated", result.GroupBlockId, new { result.GroupBlockId, result.Code }, c).ConfigureAwait(false);
            return result;
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<GroupBlockResult>> ReleaseGroupBlockAsync(CommandContext context, ReleaseGroupBlockRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        await access.RequireAsync(context.PropertyId, context.PrincipalId, InventoryPermissions.CreateReservation, ct: ct).ConfigureAwait(false);
        var expected = InventoryErrors.ParseVersion(request.ExpectedVersion);
        await store.SweepAsync(context.PropertyId, context.PrincipalId, clock.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return await runner.RunAsync(context, request, async (tx, c) =>
        {
            var result = await store.ReleaseGroupBlockAsync(tx, context.PropertyId, request.GroupBlockId, expected, c).ConfigureAwait(false);
            await tx.AuditAsync("ReleaseGroupBlock", result.Code, $"{result.Code} · all unpicked rooms released; pickup closed", c).ConfigureAwait(false);
            await tx.PublishAsync("GroupBlockReleased", result.GroupBlockId, new { result.GroupBlockId, result.Code }, c).ConfigureAwait(false);
            return result;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Nightly snapshot at a fixed agreed rate with each night's tax rate.</summary>
    public async Task<IReadOnlyList<PricedNight>> PriceAsync(int propertyId, DateOnly a, DateOnly d, long ratePaise, CancellationToken ct)
    {
        var bps = new Dictionary<DateOnly, int>();
        foreach (var n in StayDates.EachNight(a, d)) bps[n] = await taxes.TaxBpsOnAsync(propertyId, n, ct).ConfigureAwait(false);
        return StayPricing.Nights(a, d, ratePaise, n => bps[n]);
    }

    public static QuoteResult ToResult(QuoteRecord q, IReadOnlyList<StayViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(q);
        return new QuoteResult(q.QuoteId, InventoryErrors.VersionToWire(q.RowVersion), q.RoomTypeId, q.RoomId, StayDates.ToWire(q.Arrival),
            StayDates.ToWire(q.Departure), q.Adults, q.Children, q.RatePlan,
            q.Nights.Select(n => new QuoteNight(StayDates.ToWire(n.Date), n.RatePaise, n.TaxBps, n.TaxPaise)).ToList(),
            q.NetPaise, q.TaxPaise, q.TotalPaise, ToWireInstant(q.ExpiresUtc), violations);
    }

    public static string ToWireInstant(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Wave 1 sells BAR only: the prototype's other rate plans are quote-level yield rules (module 13) not yet in production.</summary>
    private static string RatePlan(string? ratePlanId)
    {
        var plan = string.IsNullOrWhiteSpace(ratePlanId) ? DefaultRatePlan : ratePlanId.Trim().ToUpperInvariant();
        if (plan != DefaultRatePlan) throw HccException.Validation($"Rate plan {plan} is not available yet; use BAR.");
        return plan;
    }
}
