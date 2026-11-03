using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Domain.Hall;

namespace Hcc.Application.Hall;

/// <summary>
/// F6 lane H3 non-money commands (F6-CONTRACT §3) — the server twin of the prototype's handlers/hall.ts enquiry and venue handlers:
/// AddHallEnquiry / UpdateHallEnquiry / LoseHallEnquiry / HoldHallEnquiry (soft hold, Manager, 1–3 days, warns but never blocks) / UpsertHallVenue (Owner) / LogHallPrint.
/// PERM-HAL-001 / 005 / 006 first, then the role rules of <see cref="HallRules"/> (the same sets as the prototype). Nothing is deleted: lose, release, retire.
/// </summary>
public sealed class HallCommands(ICommandRunner runner, IAccessPolicy policy, IHallBillingStore billing, IHallStore store, TimeProvider clock)
{
    public const string OpAddEnquiry = "AddHallEnquiry", OpUpdateEnquiry = "UpdateHallEnquiry", OpLoseEnquiry = "LoseHallEnquiry", OpHoldEnquiry = "HoldHallEnquiry",
        OpUpsertVenue = "UpsertHallVenue", OpLogPrint = "LogHallPrint";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string? s, string what) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation(what + ": use a date like 2026-12-05.");

    private async Task RoleAsync(ICommandTransaction tx, int principal, Func<string, bool> allowed, string what, CancellationToken ct)
    {
        var roles = await billing.RolesAsync(tx, principal, ct).ConfigureAwait(false);
        if (!roles.Any(allowed)) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: " + what + ".");
    }

    private static IReadOnlyList<DateOnly> ParseDates(IReadOnlyList<string>? raw, DateOnly today)
    {
        var dates = (raw ?? []).Select(d => ParseDay(d, "Event date")).Distinct().OrderBy(d => d).ToList();
        if (dates.Count is < 1 or > 7 || dates.Any(d => d < today)) throw HccException.Validation("Event date(s) from today on (up to 7).");
        return dates;
    }

    public async Task<CommandResult<HallCommandResult>> AddEnquiryAsync(CommandContext ctx, AddHallEnquiryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Enquiries, null, null, ct).ConfigureAwait(false);
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 80) throw HccException.Validation("Enter the customer's name.");
        var phone = new string((req.Phone ?? "").Where(ch => ch is not (' ' or '-')).ToArray());
        if (phone.Length is < 10 or > 14 || !phone.TrimStart('+').All(char.IsAsciiDigit)) throw HccException.Validation("Enter a 10-digit phone number.");
        if (!HallRules.EventTypes.Contains(req.EventType)) throw HccException.Validation("Choose the event type.");
        if (!HallRules.Slots.Contains(req.Slot)) throw HccException.Validation("Choose the slot.");
        if (req.Pax is < 1 or > 5000) throw HccException.Validation("Expected guests: 1–5000.");
        if (req.Source is not ("WALKIN" or "PHONE" or "REFERENCE" or "WEBSITE" or "REPEAT")) throw HccException.Validation("Choose the source.");
        return await runner.RunAsync(ctx with { Operation = OpAddEnquiry }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallRules.MayEnquire, "enquiries (PERM-HAL-001)", c).ConfigureAwait(false);
            var bd = await billing.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var dates = ParseDates(req.Dates, bd);
            var follow = ParseDay(req.FollowUp, "Follow-up date");
            if (follow < bd) throw HccException.Validation("Set a follow-up date from today on.");
            if (req.VenueId is { } v && !await store.VenueActiveAsync(tx, ctx.PropertyId, v, c).ConfigureAwait(false)) throw HccException.Validation("Choose an active hall.");
            var id = await store.InsertEnquiryAsync(tx, new NewHallEnquiry(ctx.PropertyId, name, phone, req.EventType, dates, req.Slot, req.Pax, req.VenueId, req.Source, follow,
                string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim()[..Math.Min(500, req.Note.Trim().Length)], req.GuestId, req.CompanyId, ctx.PrincipalId, Now), c).ConfigureAwait(false);
            var msg = $"Enquiry ENQ-{N(id)} · {name} · {req.EventType} · {string.Join(", ", dates.Select(Wire))} {req.Slot} · {req.Pax} pax";
            await tx.AuditAsync("HALL_ENQUIRY_ADD", "hall-enquiry-" + N(id), msg, c).ConfigureAwait(false);
            return new HallCommandResult(id, "OPEN", msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallCommandResult>> UpdateEnquiryAsync(CommandContext ctx, UpdateHallEnquiryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Enquiries, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpUpdateEnquiry }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallRules.MayEnquire, "enquiries (PERM-HAL-001)", c).ConfigureAwait(false);
            var bd = await billing.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var q = await store.LockEnquiryAsync(tx, ctx.PropertyId, req.EnquiryId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Enquiry not found.");
            if (req.ExpectedVersion is { } ev && ev != q.Version) throw new HccException(HccErrorCode.STALE_VERSION, "This enquiry was changed by someone else. Reload and try again.");
            var reopen = q.Status == "LOST" && req.Status == "OPEN";
            if (!(q.Status is "OPEN" or "QUOTED") && !reopen) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A won or lost enquiry is read-only (reopen a lost one first).");
            var n = q;
            if (req.FollowUp is not null)
            {
                var f = ParseDay(req.FollowUp, "Follow-up date");
                if (f < bd) throw HccException.Validation("Set a follow-up date from today on.");
                n = n with { FollowUp = f };
            }

            if (req.Note is not null) n = n with { Note = req.Note.Trim().Length == 0 ? null : req.Note.Trim()[..Math.Min(500, req.Note.Trim().Length)] };
            if (req.Pax is { } p)
            {
                if (p is < 1 or > 5000) throw HccException.Validation("Expected guests: 1–5000.");
                n = n with { Pax = p };
            }

            if (req.VenueId is { } vid)
            {
                if (vid != 0 && !await store.VenueActiveAsync(tx, ctx.PropertyId, vid, c).ConfigureAwait(false)) throw HccException.Validation("Choose an active hall.");
                n = n with { VenueId = vid == 0 ? null : vid };
            }

            if (req.Dates is not null) n = n with { Dates = ParseDates(req.Dates, bd) };
            if (req.Slot is not null)
            {
                if (!HallRules.Slots.Contains(req.Slot)) throw HccException.Validation("Choose the slot.");
                n = n with { Slot = req.Slot };
            }

            if (req.Status is not null)
            {
                if (req.Status is not ("OPEN" or "QUOTED")) throw HccException.Validation("Use Mark lost (or accept the quotation) to close an enquiry.");
                if (!HallRules.EnquiryTransition(q.Status, req.Status) && q.Status != req.Status) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"An enquiry cannot go from {q.Status} to {req.Status}.");
                n = n with { Status = req.Status, LostReasonId = req.Status == "OPEN" ? null : n.LostReasonId, LostNote = req.Status == "OPEN" ? null : n.LostNote };
            }

            await store.UpdateEnquiryAsync(tx, n, ctx.PrincipalId, c).ConfigureAwait(false);
            var msg = $"Enquiry ENQ-{N(q.EnquiryId)} updated · {n.Status} · follow-up {Wire(n.FollowUp)}";
            await tx.AuditAsync("HALL_ENQUIRY_UPDATE", "hall-enquiry-" + N(q.EnquiryId), msg, c).ConfigureAwait(false);
            return new HallCommandResult(q.EnquiryId, n.Status, msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallCommandResult>> LoseEnquiryAsync(CommandContext ctx, LoseHallEnquiryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Enquiries, null, null, ct).ConfigureAwait(false);
        var reason = HallLostReasons.All.FirstOrDefault(r => r.Id == req.ReasonId) ?? throw HccException.Validation("Choose a lost reason from the list.");
        var note = (req.Note ?? "").Trim();
        if (reason.NeedsNote && note.Length < 3) throw HccException.Validation($"“{reason.Label}” needs a note (at least 3 characters).");
        return await runner.RunAsync(ctx with { Operation = OpLoseEnquiry }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallRules.MayEnquire, "enquiries (PERM-HAL-001)", c).ConfigureAwait(false);
            var q = await store.LockEnquiryAsync(tx, ctx.PropertyId, req.EnquiryId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Enquiry not found.");
            if (q.Status is not ("OPEN" or "QUOTED")) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only an open or quoted enquiry can be marked lost.");
            await store.UpdateEnquiryAsync(tx, q with { Status = "LOST", LostReasonId = reason.Id, LostNote = note.Length == 0 ? null : note[..Math.Min(300, note.Length)], SoftVenueId = null, SoftUntil = null }, ctx.PrincipalId, c).ConfigureAwait(false);
            var msg = $"Enquiry ENQ-{N(q.EnquiryId)} lost · {reason.Label}{(note.Length > 0 ? " · " + note : "")}";
            await tx.AuditAsync("HALL_ENQUIRY_LOST", "hall-enquiry-" + N(q.EnquiryId), msg, c).ConfigureAwait(false);
            return new HallCommandResult(q.EnquiryId, "LOST", msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallCommandResult>> HoldEnquiryAsync(CommandContext ctx, HoldHallEnquiryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Enquiries, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpHoldEnquiry }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallRules.MaySoftHold, "a soft hold needs the Manager or Owner", c).ConfigureAwait(false);
            var bd = await billing.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var q = await store.LockEnquiryAsync(tx, ctx.PropertyId, req.EnquiryId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Enquiry not found.");
            if (q.Status is not ("OPEN" or "QUOTED")) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only an open or quoted enquiry can hold a hall.");
            if (req.Release)
            {
                await store.UpdateEnquiryAsync(tx, q with { SoftVenueId = null, SoftUntil = null }, ctx.PrincipalId, c).ConfigureAwait(false);
                return new HallCommandResult(q.EnquiryId, q.Status, $"Soft hold released · ENQ-{N(q.EnquiryId)}");
            }

            var venueId = req.VenueId ?? q.VenueId ?? throw HccException.Validation("Choose an active hall.");
            if (!await store.VenueActiveAsync(tx, ctx.PropertyId, venueId, c).ConfigureAwait(false)) throw HccException.Validation("Choose an active hall.");
            if (req.Days < 1 || req.Days > HallRules.SoftHoldMaxDays) throw HccException.Validation($"A soft hold lasts 1–{HallRules.SoftHoldMaxDays} days.");
            var venues = await billing.VenuesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var refs = venues.Select(x => new HallVenueRef(N(x.VenueId), x.CombinedOf?.Select(i => N(i)).ToArray())).ToList();
            var holds = await billing.HoldsAsync(tx, ctx.PropertyId, q.Dates, c).ConfigureAwait(false);
            if (HallRules.Clash(refs, holds, [N(venueId)], q.Dates, q.Slot) is { } hit)
                throw new HccException(HccErrorCode.HCC_HALL_CLASH, $"{venues.First(x => x.VenueId == venueId).Name} is already booked on {Wire(hit.Dates.First(q.Dates.Contains))} ({hit.Slot}, event {hit.EventId}).");
            var until = bd.AddDays(req.Days); // held through that business day
            var others = await store.SoftHoldsAsync(tx, ctx.PropertyId, q.Dates, bd, q.EnquiryId, c).ConfigureAwait(false);
            var foot = refs.Count == 0 ? [N(venueId)] : HallRules.Footprint(refs, N(venueId));
            var warn = others.Where(o => HallRules.SlotsOverlap(o.Slot, q.Slot) && HallRules.Footprint(refs, N(o.VenueId)).Any(foot.Contains) && o.Dates.Any(q.Dates.Contains)).Select(o => "ENQ-" + N(o.EnquiryId)).ToList();
            await store.UpdateEnquiryAsync(tx, q with { SoftVenueId = venueId, SoftUntil = until }, ctx.PrincipalId, c).ConfigureAwait(false);
            var msg = $"Soft hold · {venues.First(x => x.VenueId == venueId).Name} · {string.Join(", ", q.Dates.Select(Wire))} {q.Slot} until {Wire(bd.AddDays(req.Days))}{(warn.Count > 0 ? " · warning: also soft-held by " + string.Join(", ", warn) : "")}";
            await tx.AuditAsync("HALL_ENQUIRY_HOLD", "hall-enquiry-" + N(q.EnquiryId), msg, c).ConfigureAwait(false);
            return new HallCommandResult(q.EnquiryId, q.Status, msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallCommandResult>> UpsertVenueAsync(CommandContext ctx, UpsertHallVenueRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HallPermissions.Masters, null, null, ct).ConfigureAwait(false);
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 60) throw HccException.Validation("Hall name: 2–60 characters.");
        var code = (req.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 20 || !code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-')) throw HccException.Validation("Hall code: 2–20 letters, digits or hyphens.");
        if (req.Kind is not ("HALL" or "LAWN" or "TERRACE")) throw HccException.Validation("Kind: Hall, Lawn or Terrace.");
        if (req.Slots is null || req.Slots.Count < 1) throw HccException.Validation("Give at least one slot with its time and rate.");
        var seen = new HashSet<string>();
        foreach (var s in req.Slots)
        {
            if (!HallRules.Slots.Contains(s.Slot) || !seen.Add(s.Slot)) throw HccException.Validation("Each slot once: Morning, Afternoon, Evening or Full day.");
            if (!TimeOnly.TryParseExact(s.From, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) || !TimeOnly.TryParseExact(s.To, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) || f >= t)
                throw HccException.Validation("Slot times: HH:mm, from before to.");
            if (s.RatePaise is < 0 or > 10_000_000_000) throw HccException.Validation("Slot rate: whole paise.");
        }

        foreach (var (k, n) in req.Capacity ?? new Dictionary<string, int>())
            if (k is not ("THEATRE" or "ROUND" or "USHAPE" or "FLOATING" or "CLASSROOM") || n is < 1 or > 10000) throw HccException.Validation("Capacity: a whole number per set-up.");
        return await runner.RunAsync(ctx with { Operation = OpUpsertVenue }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallRules.MayEditVenues, "only the Owner edits the halls master (PERM-HAL-006)", c).ConfigureAwait(false);
            var bd = await billing.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var combinedCodes = new List<string>();
            if (req.CombinedOf is { Count: > 0 } parts)
            {
                if (parts.Count < 2) throw HccException.Validation("A combined hall joins two or more existing single halls.");
                foreach (var pid in parts)
                {
                    var pc = await store.VenueCodeAsync(tx, ctx.PropertyId, pid, c).ConfigureAwait(false);
                    if (pc is null || pid == req.VenueId) throw HccException.Validation("A combined hall joins two or more existing single halls.");
                    combinedCodes.Add(pc);
                }
            }

            if (req.VenueId is { } vid && !req.Active && await store.ConfirmedAheadAsync(tx, ctx.PropertyId, vid, bd, c).ConfigureAwait(false) > 0)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "This hall has confirmed events ahead; it stays active until they are billed or cancelled.");
            var (id, version) = await store.UpsertVenueAsync(tx, ctx.PropertyId, req with { Code = code, Name = name }, combinedCodes, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            var msg = $"{(req.VenueId is null ? "Hall added" : "Hall updated")} · {name} v{version}";
            await tx.AuditAsync("HALL_VENUE_UPSERT", "hall-venue-" + N(id), msg, c).ConfigureAwait(false);
            return new HallCommandResult(id, req.Active ? "ACTIVE" : "RETIRED", msg);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HallCommandResult>> LogPrintAsync(CommandContext ctx, LogHallPrintRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var kind = (req.DocKind ?? "").Trim().ToUpperInvariant();
        if (kind is not ("QUOTATION" or "BEO" or "INVOICE" or "RECEIPT")) throw HccException.Validation("Document: quotation, function sheet, invoice or receipt.");
        // The function sheet may be printed by anyone who sees the calendar (no prices); the money documents need the view of the money permissions.
        if (kind == "QUOTATION") await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, AccessEvaluator.View(HallPermissions.Quotations), null, null, ct).ConfigureAwait(false);
        else if (kind is "INVOICE" or "RECEIPT") await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, AccessEvaluator.View(HallPermissions.Bill), null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpLogPrint }, req, async (tx, c) =>
        {
            var prices = kind != "BEO" || req.WithPrices;
            await store.InsertPrintLogAsync(tx, ctx.PropertyId, kind, req.RefId, req.Version, prices, ctx.PrincipalId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_PRINT", "hall-" + kind.ToLowerInvariant() + "-" + N(req.RefId), $"{kind} printed{(req.Version is { } v ? " v" + N(v) : "")}{(prices ? "" : " (no prices)")}", c).ConfigureAwait(false);
            return new HallCommandResult(req.RefId, kind, $"{kind} {N(req.RefId)} printed");
        }, ct).ConfigureAwait(false);
    }
}

/// <summary>Reads of the hall module. Prices (every <c>*Paise</c> field) are sent only to a caller who may view PERM-HAL-002 (housekeeping / maintenance / supervisor never, DEC-061).</summary>
public sealed class HallQueries(IAccessPolicy policy, IHallStore store)
{
    private async Task<bool> MoneyAsync(int propertyId, int principalId, CancellationToken ct) =>
        await policy.HasAsync(propertyId, principalId, AccessEvaluator.View(HallPermissions.Quotations), null, ct).ConfigureAwait(false);

    private Task RequireAsync(int propertyId, int principalId, string perm, CancellationToken ct) => policy.RequireAsync(propertyId, principalId, AccessEvaluator.View(perm), null, null, ct);

    public async Task<IReadOnlyList<HallVenueView>> VenuesAsync(int propertyId, int principalId, CancellationToken ct = default) =>
        await store.VenuesAsync(propertyId, await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    public IReadOnlyList<HallLostReasonView> LostReasons() => HallLostReasons.All;

    public async Task<IReadOnlyList<HallCalendarRowView>> CalendarAsync(int propertyId, int principalId, DateOnly from, DateOnly to, int? venueId, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 400) throw HccException.Validation("Calendar range: up to 400 days.");
        return await store.CalendarAsync(propertyId, from, to, venueId, ct).ConfigureAwait(false); // no money in a calendar row: every staff role
    }

    public async Task<IReadOnlyList<HallEnquiryView>> EnquiriesAsync(int propertyId, int principalId, string? status, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, HallPermissions.Enquiries, ct).ConfigureAwait(false);
        return await store.EnquiriesAsync(propertyId, status, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HallQuotationView>> QuotationsAsync(int propertyId, int principalId, string? status, long? id, CancellationToken ct = default)
    {
        await RequireAsync(propertyId, principalId, HallPermissions.Quotations, ct).ConfigureAwait(false);
        return await store.QuotationsAsync(propertyId, status, id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HallEventView>> EventsAsync(int propertyId, int principalId, string? status, long? id, CancellationToken ct = default) =>
        await store.EventsAsync(propertyId, status, id, await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>Everything the prototype screens read in one call (the production sync loader): enquiries and quotations only for a caller who holds those views.</summary>
    public async Task<HallSnapshotView> SnapshotAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var enq = await policy.HasAsync(propertyId, principalId, AccessEvaluator.View(HallPermissions.Enquiries), null, ct).ConfigureAwait(false);
        return new HallSnapshotView(money, await store.VenuesAsync(propertyId, money, ct).ConfigureAwait(false),
            enq ? await store.EnquiriesAsync(propertyId, null, ct).ConfigureAwait(false) : [],
            money ? await store.QuotationsAsync(propertyId, null, null, ct).ConfigureAwait(false) : [],
            await store.EventsAsync(propertyId, null, null, money, ct).ConfigureAwait(false), HallLostReasons.All);
    }
}
