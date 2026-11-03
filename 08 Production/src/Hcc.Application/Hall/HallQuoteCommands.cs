using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Hall;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Hall;

// ── requests / results (F6-CONTRACT §3, §6: POST /api/v1/hall/quotations, /quotations/{id}/edit|revise|send|override|approve-discount|lost|cancel,
//    /quotations/expire, /templates) ──

public sealed record QuoteHeaderIn(string? EventType = null, IReadOnlyList<DateOnly>? Dates = null, string? Slot = null, int? VenueId = null, string? Setup = null,
    int? Pax = null, int? GuaranteedPax = null);

/// <summary>A quotation line as typed. RefId = the venue code (HALL) or menu-package code (MENU): their list rate always comes from the master.</summary>
public sealed record QuoteLineIn(string Kind, string Desc, int Qty, long RatePaise, string? RefId = null, string? Unit = null, long? ListRatePaise = null, long DiscountPaise = 0,
    string? GstClass = null);

public sealed record QuoteAdvanceIn(string Due, int? PctBps = null, long? AmountPaise = null, string? Label = null);

public sealed record CreateQuotationRequest(long EnquiryId, QuoteHeaderIn? Header = null, IReadOnlyList<QuoteLineIn>? Lines = null, string? MenuCode = null,
    IReadOnlyList<QuoteAdvanceIn>? Advance = null);

public sealed record EditQuotationRequest(long QuotationId, int? Version = null, QuoteHeaderIn? Header = null, IReadOnlyList<QuoteLineIn>? Lines = null,
    IReadOnlyList<QuoteAdvanceIn>? Advance = null);

/// <summary>Revise / send / override / approve-discount / lost / cancel on the latest version. Reason: override; ReasonId + Note: lost.</summary>
public sealed record QuotationActionRequest(long QuotationId, int? Version = null, string? Reason = null, string? ReasonId = null, string? Note = null);

public sealed record ExpireQuotationsRequest(string? Note = null);

public sealed record TncClauseIn(string Heading, string Text);

public sealed record SaveTncTemplateRequest(string EventType, IReadOnlyList<TncClauseIn> Clauses, DateOnly? EffectiveFrom = null);

public sealed record QuotationResult(long QuotationId, string Number, int Version, string Status, long TotalPaise = 0, int DiscountBps = 0, bool NeedsApproval = false,
    int Count = 0, string? TemplateCode = null);

// ── store port ──

public sealed record HallQuoteHeader(string EventType, IReadOnlyList<DateOnly> Dates, string Slot, int VenueId, string Setup, int Pax, int GuaranteedPax);

public sealed record HallQuoteLine(HallLine Line, string? RefId);

public sealed record HallQuoteVersionData(HallQuoteHeader Header, IReadOnlyList<HallQuoteLine> Lines, IReadOnlyList<HallAdvanceRow> Advance, HallTncSnapshot Tnc,
    DateOnly ValidUntil, int CreatedBy, DateTime CreatedUtc);

public sealed record HallQuoteVersionRow(long VersionId, int V, string Status, HallQuoteVersionData Data, DateTime? SentUtc, int? OverrideBy, int? ApprovalBy);

public sealed record HallQuoteState(long QuotationId, string Number, string Fy, long EnquiryId, int CurrentVersion, IReadOnlyList<HallQuoteVersionRow> Versions)
{
    public HallQuoteVersionRow Latest => Versions.First(v => v.V == CurrentVersion);
}

public sealed record HallEnquiryRow(long EnquiryId, string Status, string Name, string Phone, string EventType, IReadOnlyList<DateOnly> Dates, string Slot, int Pax, int? VenueId,
    long? CompanyId);

public sealed record HallVenueSlotsRow(int VenueId, string Code, string Name, bool Active, string GstClass, IReadOnlyDictionary<string, long> SlotRates);

public sealed record HallMenuRow(string Code, string Name, long PerPlatePaise, bool Active);

public sealed record NewHallQuotation(int PropertyId, string Number, string Fy, long EnquiryId, string CustomerName, string CustomerPhone, long? CompanyId, int By, DateTime Utc);

public interface IHallQuoteStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<IReadOnlySet<string>> RolesAsync(ICommandTransaction tx, int principalId, CancellationToken ct);

    Task<HallEnquiryRow?> LockEnquiryAsync(ICommandTransaction tx, int propertyId, long enquiryId, CancellationToken ct);

    Task<IReadOnlyList<HallVenueSlotsRow>> VenuesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<IReadOnlyList<HallMenuRow>> MenusAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    /// <summary>The property's templates with every version and clause (empty = none stored: the rules use the placeholder General template).</summary>
    Task<IReadOnlyList<HallTncTemplate>> TemplatesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<IReadOnlyList<HallLostReason>> LostReasonsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    /// <summary>The next QUO number of the FY under an exclusive app lock (gap-free within the transaction): prefix from the QUO row of the
    /// numbering-series master in force (else "QT"), frozen per FY in hall.quote_series.</summary>
    Task<(string Prefix, int Seq)> NextNumberAsync(ICommandTransaction tx, int propertyId, string fyCode, DateOnly date, DateTime utc, CancellationToken ct);

    Task<long> InsertQuotationAsync(ICommandTransaction tx, NewHallQuotation q, HallQuoteVersionData v1, CancellationToken ct);

    /// <summary>The quotation (UPDLOCK) with every version; null when not on the property.</summary>
    Task<HallQuoteState?> LockAsync(ICommandTransaction tx, int propertyId, long quotationId, CancellationToken ct);

    /// <summary>Adds version v as DRAFT and makes it current.</summary>
    Task InsertVersionAsync(ICommandTransaction tx, long quotationId, int v, HallQuoteVersionData data, CancellationToken ct);

    /// <summary>Replaces a DRAFT version's header, lines, advance rows and snapshot; clears its discount approval when <paramref name="clearApproval"/>.</summary>
    Task UpdateDraftAsync(ICommandTransaction tx, long versionId, HallQuoteVersionData data, bool clearApproval, bool clearOverride, CancellationToken ct);

    /// <summary>DRAFT → SENT: the snapshot re-taken now, sent time, new validity date. Immutable afterwards.</summary>
    Task SendAsync(ICommandTransaction tx, long versionId, HallTncSnapshot tnc, DateOnly validUntil, DateTime utc, CancellationToken ct);

    Task SetStatusAsync(ICommandTransaction tx, long versionId, string status, string? lostReasonId, string? lostNote, DateTime utc, CancellationToken ct);

    Task SetOverrideAsync(ICommandTransaction tx, long versionId, int by, string reason, DateTime utc, CancellationToken ct);

    Task SetApprovalAsync(ICommandTransaction tx, long versionId, int by, int bps, DateTime utc, CancellationToken ct);

    /// <summary>Enquiry status (OPEN → QUOTED on send; → LOST with the reason on a lost quotation, soft hold released).</summary>
    Task SetEnquiryAsync(ICommandTransaction tx, long enquiryId, string status, string? lostReasonId, string? lostNote, CancellationToken ct);

    /// <summary>Every latest SENT version of the property whose validity ended before <paramref name="today"/> → EXPIRED; returns the count.</summary>
    Task<int> ExpireAsync(ICommandTransaction tx, int propertyId, DateOnly today, DateTime utc, CancellationToken ct);

    /// <summary>Adds a template version (creating the template of the event type first; materialising the seed is the caller's job); returns v.</summary>
    Task<int> SaveTemplateVersionAsync(ICommandTransaction tx, int propertyId, string code, string eventType, DateOnly effectiveFrom, IReadOnlyList<HallTncClause> clauses,
        int by, DateTime utc, CancellationToken ct);
}

/// <summary>
/// F6 lane H2 quotation + T&amp;C commands (F6-CONTRACT §3) — the server twin of the prototype's <c>handlers/hall-quote.ts</c>; rules only through
/// <see cref="HallQuoteRules"/> / <see cref="HallBilling"/>. CreateQuotation (QUO number, default lines from the hall slot and the menu, T&amp;C
/// snapshot), EditQuotation (draft only), ReviseQuotation (v+1, the sent one SUPERSEDED), SendQuotation (discount within the sender's limit or Owner
/// approval, placeholder guard unless overridden, snapshot re-taken and locked, outbox held until messaging is on), OverrideTncPlaceholder (Owner,
/// reason, once per quotation), ApproveQuotationDiscount (Owner, never the author), LoseQuotation (listed reason), CancelQuotation (draft),
/// ExpireQuotations (night run), SaveTncTemplate (Owner only; a new version, never a change to a snapshot). AcceptQuotation is lane H1's.
/// </summary>
public sealed class HallQuoteCommands(ICommandRunner runner, IHallQuoteStore store, IGstMasterStore gstStore, TimeProvider clock)
{
    public const string OpCreate = "CreateQuotation", OpEdit = "EditQuotation", OpRevise = "ReviseQuotation", OpSend = "SendQuotation", OpOverride = "OverrideTncPlaceholder",
        OpApprove = "ApproveQuotationDiscount", OpLost = "LoseQuotation", OpCancel = "CancelQuotation", OpExpire = "ExpireQuotations", OpTemplate = "SaveTncTemplate";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private async Task<string> RoleAsync(ICommandTransaction tx, int principal, Func<string, bool> allowed, string what, CancellationToken ct, HccErrorCode code = HccErrorCode.FORBIDDEN)
    {
        var roles = await store.RolesAsync(tx, principal, ct).ConfigureAwait(false);
        return roles.FirstOrDefault(allowed) ?? throw new HccException(code, "Access denied: " + what + ".");
    }

    private static HallQuoteHeader Header(HallQuoteHeader b, QuoteHeaderIn? h, IReadOnlyList<HallVenueSlotsRow> venues, DateOnly today)
    {
        var n = new HallQuoteHeader(h?.EventType ?? b.EventType, h?.Dates ?? b.Dates, h?.Slot ?? b.Slot, h?.VenueId ?? b.VenueId, h?.Setup ?? b.Setup, h?.Pax ?? b.Pax,
            h?.GuaranteedPax ?? b.GuaranteedPax);
        if (!HallRules.EventTypes.Contains(n.EventType)) throw HccException.Validation("Choose the event type.");
        if (n.Dates.Count is < 1 or > 7 || n.Dates.Any(d => d < today)) throw HccException.Validation("Event date(s) from today on (up to 7).");
        if (!HallRules.Slots.Contains(n.Slot)) throw HccException.Validation("Choose the slot.");
        var v = venues.FirstOrDefault(x => x.VenueId == n.VenueId && x.Active) ?? throw HccException.Validation("Choose an active hall.");
        if (!v.SlotRates.ContainsKey(n.Slot)) throw HccException.Validation($"{v.Name} has no {n.Slot} slot.");
        if (!HallQuoteRules.Setups.Contains(n.Setup)) throw HccException.Validation("Choose the setup.");
        if (n.Pax is < 1 or > 5000) throw HccException.Validation("Expected guests: 1–5000.");
        if (n.GuaranteedPax < 0 || n.GuaranteedPax > n.Pax) throw HccException.Validation("Guaranteed guests: 0 up to the expected guests.");
        return n with { Dates = n.Dates.Distinct().Order().ToList() };
    }

    private static IReadOnlyList<HallQuoteLine> Lines(IReadOnlyList<QuoteLineIn> raw, string slot, IReadOnlyList<HallVenueSlotsRow> venues, IReadOnlyList<HallMenuRow> menus)
    {
        if (raw.Count is < 1 or > 60) throw HccException.Validation("Give 1–60 lines.");
        var outp = new List<HallQuoteLine>();
        for (var i = 0; i < raw.Count; i++)
        {
            var l = raw[i];
            var no = i + 1;
            if (!HallQuoteRules.LineKinds.Contains(l.Kind)) throw HccException.Validation($"Line {no}: kind Hall, Menu, Extra, Room or Other.");
            var desc = (l.Desc ?? "").Trim();
            if (desc.Length is < 2 or > 120) throw HccException.Validation($"Line {no}: description 2–120 characters.");
            if (l.Qty is < 1 or > 100_000) throw HccException.Validation($"Line {no}: quantity a whole number.");
            if (l.RatePaise is < 0 or > 10_000_000_000) throw HccException.Validation($"Line {no}: rate in whole paise.");
            long? master = l.Kind switch
            {
                "HALL" => venues.FirstOrDefault(v => v.Code == l.RefId) is { } v && v.SlotRates.TryGetValue(slot, out var r) ? r : null,
                "MENU" => menus.FirstOrDefault(m => m.Code == l.RefId && m.Active)?.PerPlatePaise,
                _ => null,
            };
            if (l.Kind is "HALL" or "MENU" && master is null) throw HccException.Validation($"Line {no}: pick the {(l.Kind == "HALL" ? "hall slot" : "menu package")} from the master.");
            var list = master ?? l.ListRatePaise ?? l.RatePaise;
            if (list < l.RatePaise) throw HccException.Validation($"Line {no}: the list rate cannot be below the rate.");
            if (l.DiscountPaise < 0 || l.DiscountPaise > l.Qty * l.RatePaise) throw HccException.Validation($"Line {no}: discount 0 up to the line value.");
            var cls = string.IsNullOrWhiteSpace(l.GstClass)
                ? l.Kind switch { "HALL" => HallBilling.ClassHallRent, "MENU" => HallBilling.ClassBanquetFood, "ROOM" => HallBilling.ClassRoom, _ => HallBilling.ClassService }
                : l.GstClass.Trim();
            if (!HallQuoteRules.GstClasses.Contains(cls)) throw HccException.Validation($"Line {no}: GST class Hall rent, Banquet food, Service or Room.");
            var unit = string.IsNullOrWhiteSpace(l.Unit) ? "each" : l.Unit.Trim();
            outp.Add(new HallQuoteLine(new HallLine("L" + N(no), l.Kind, desc, l.Qty, unit.Length > 12 ? unit[..12] : unit, new Paise(l.RatePaise), new Paise(list),
                new Paise(l.DiscountPaise), cls), string.IsNullOrWhiteSpace(l.RefId) ? null : l.RefId.Trim()));
        }

        return outp;
    }

    /// <summary>Default lines: the hall rent of the slot, then the menu per plate × plates (list = rate).</summary>
    private static List<QuoteLineIn> DefaultLines(HallQuoteHeader h, IReadOnlyList<HallVenueSlotsRow> venues, IReadOnlyList<HallMenuRow> menus, string? menuCode)
    {
        var outp = new List<QuoteLineIn>();
        var v = venues.First(x => x.VenueId == h.VenueId);
        outp.Add(new QuoteLineIn("HALL", $"{v.Name} · {h.Slot.ToLowerInvariant().Replace('_', ' ')}", 1, v.SlotRates[h.Slot], v.Code, "slot"));
        var plates = h.GuaranteedPax > 0 ? h.GuaranteedPax : h.Pax;
        if (menuCode is not null && menus.FirstOrDefault(m => m.Code == menuCode && m.Active) is { } m)
            outp.Add(new QuoteLineIn("MENU", m.Name + " per plate", plates, m.PerPlatePaise, m.Code, "plate"));
        return outp;
    }

    private static IReadOnlyList<HallAdvanceRow> Advance(IReadOnlyList<QuoteAdvanceIn>? raw, string role)
    {
        if (raw is null || raw.Count == 0) return [];
        if (!HallQuoteRules.MayEditAdvancePlan(role)) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: only the Manager or Owner changes the advance plan (Reception quotes the default).");
        if (raw.Count is < 1 or > 6) throw HccException.Validation("The advance plan has 1–6 rows.");
        var rows = new List<HallAdvanceRow>();
        var pct = 0;
        for (var i = 0; i < raw.Count; i++)
        {
            var r = raw[i];
            var last = i == raw.Count - 1;
            var due = (r.Due ?? "").Trim();
            if (due is not ("ON_ACCEPT" or "EVENT_DAY") && !DateOnly.TryParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw HccException.Validation($"Advance row {i + 1}: due on acceptance, on the event day or a date.");
            var label = string.IsNullOrWhiteSpace(r.Label) ? (last ? "Balance" : $"Advance {i + 1}") : r.Label.Trim();
            if (last) { rows.Add(new HallAdvanceRow("ADV-" + N(i + 1), label, null, Paise.Zero, due, Paise.Zero)); continue; }
            if (r.PctBps is { } p)
            {
                if (p is <= 0 or >= 10000) throw HccException.Validation($"Advance row {i + 1}: percentage between 0 and 100.");
                pct += p;
                rows.Add(new HallAdvanceRow("ADV-" + N(i + 1), label, p, Paise.Zero, due, Paise.Zero));
            }
            else if (r.AmountPaise is long a && a > 0) rows.Add(new HallAdvanceRow("ADV-" + N(i + 1), label, null, new Paise(a), due, Paise.Zero));
            else throw HccException.Validation($"Advance row {i + 1}: a percentage or an amount in whole paise.");
        }

        if (pct >= 10000) throw HccException.Validation("The percentage rows must leave a balance (under 100 % in all).");
        return rows;
    }

    private async Task<long> TotalAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<HallQuoteLine> lines, DateOnly date, CancellationToken ct)
    {
        var master = new GstMaster(await gstStore.ActiveVersionsAsync(tx, propertyId, ct).ConfigureAwait(false));
        return HallBilling.Price(master, lines.Select(l => l.Line).ToList(), date).Totals.Total.Value;
    }

    private static HallQuoteVersionRow Latest(HallQuoteState q, int? version)
    {
        if (version is { } v && v != q.CurrentVersion) throw new HccException(HccErrorCode.HCC_QUO_NOT_LATEST, $"Only the latest version (v{q.CurrentVersion}) can be changed.");
        return q.Latest;
    }

    private async Task<HallQuoteState> LockAsync(ICommandTransaction tx, int propertyId, long id, CancellationToken ct) =>
        await store.LockAsync(tx, propertyId, id, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Quotation not found.");

    public async Task<CommandResult<QuotationResult>> CreateAsync(CommandContext ctx, CreateQuotationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpCreate }, req, async (tx, c) =>
        {
            var role = await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayQuote, "quotations (PERM-HAL-002)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var e = await store.LockEnquiryAsync(tx, ctx.PropertyId, req.EnquiryId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Enquiry not found.");
            if (e.Status is not ("OPEN" or "QUOTED")) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Quote an open enquiry (reopen a lost one first).");
            var venues = await store.VenuesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var menus = await store.MenusAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var header = Header(new HallQuoteHeader(e.EventType, e.Dates, e.Slot, e.VenueId ?? 0, "ROUND", e.Pax, e.Pax), req.Header, venues, bd);
            var lines = Lines(req.Lines ?? DefaultLines(header, venues, menus, req.MenuCode), header.Slot, venues, menus);
            var adv = Advance(req.Advance, role);
            var templates = await store.TemplatesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var now = Now;
            var fy = HallQuoteRules.FyCode(bd);
            var (prefix, seq) = await store.NextNumberAsync(tx, ctx.PropertyId, fy, bd, now, c).ConfigureAwait(false);
            var number = HallQuoteRules.Format(prefix, fy, seq);
            var data = new HallQuoteVersionData(header, lines, adv, HallQuoteRules.Snapshot(templates, header.EventType, bd), HallQuoteRules.ValidUntil(bd), ctx.PrincipalId, now);
            var id = await store.InsertQuotationAsync(tx, new NewHallQuotation(ctx.PropertyId, number, fy, e.EnquiryId, e.Name, e.Phone, e.CompanyId, ctx.PrincipalId, now), data, c)
                .ConfigureAwait(false);
            var hl = lines.Select(l => l.Line).ToList();
            var needs = HallQuoteRules.NeedsApproval(hl, role);
            await tx.AuditAsync("HALL_QUO_CREATE", "hall-quotation-" + N(id), $"{number} v1 drafted · {e.Name} · {header.EventType}", c).ConfigureAwait(false);
            return new QuotationResult(id, number, 1, "DRAFT", await TotalAsync(tx, ctx.PropertyId, lines, bd, c).ConfigureAwait(false), HallBilling.DiscountBps(hl), needs,
                TemplateCode: data.Tnc.TemplateCode);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> EditAsync(CommandContext ctx, EditQuotationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpEdit }, req, async (tx, c) =>
        {
            var role = await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayQuote, "quotations (PERM-HAL-002)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var q = await LockAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false);
            var v = Latest(q, req.Version);
            if (!HallQuoteRules.CanEdit(v.Status)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a draft can be edited — revise a sent quotation into a new version.");
            var venues = await store.VenuesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var menus = await store.MenusAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var header = req.Header is null ? v.Data.Header : Header(v.Data.Header, req.Header, venues, bd);
            var lines = req.Lines is null ? v.Data.Lines : Lines(req.Lines, header.Slot, venues, menus);
            var adv = req.Advance is null ? v.Data.Advance : Advance(req.Advance, role);
            var typeChanged = header.EventType != v.Data.Header.EventType;
            var tnc = typeChanged ? HallQuoteRules.Snapshot(await store.TemplatesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), header.EventType, bd) : v.Data.Tnc;
            await store.UpdateDraftAsync(tx, v.VersionId, v.Data with { Header = header, Lines = lines, Advance = adv, Tnc = tnc }, req.Lines is not null || req.Header is not null,
                typeChanged, c).ConfigureAwait(false);
            var hl = lines.Select(l => l.Line).ToList();
            await tx.AuditAsync("HALL_QUO_EDIT", "hall-quotation-" + N(q.QuotationId), $"{q.Number} v{v.V} saved", c).ConfigureAwait(false);
            return new QuotationResult(q.QuotationId, q.Number, v.V, "DRAFT", await TotalAsync(tx, ctx.PropertyId, lines, bd, c).ConfigureAwait(false), HallBilling.DiscountBps(hl),
                HallQuoteRules.NeedsApproval(hl, role));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> ReviseAsync(CommandContext ctx, QuotationActionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpRevise }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayQuote, "quotations (PERM-HAL-002)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var q = await LockAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false);
            var v = Latest(q, req.Version);
            if (!HallQuoteRules.CanRevise(v.Status)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a sent or expired quotation is revised (edit a draft directly).");
            var now = Now;
            var templates = await store.TemplatesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var data = v.Data with { Tnc = HallQuoteRules.Snapshot(templates, v.Data.Header.EventType, bd), ValidUntil = HallQuoteRules.ValidUntil(bd), CreatedBy = ctx.PrincipalId, CreatedUtc = now };
            var after = HallQuoteRules.StatusAfterRevise(v.Status);
            if (after != v.Status) await store.SetStatusAsync(tx, v.VersionId, after, null, null, now, c).ConfigureAwait(false);
            await store.InsertVersionAsync(tx, q.QuotationId, v.V + 1, data, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_QUO_REVISE", "hall-quotation-" + N(q.QuotationId), $"{q.Number} revised → v{v.V + 1} · v{v.V} {after}", c).ConfigureAwait(false);
            return new QuotationResult(q.QuotationId, q.Number, v.V + 1, "DRAFT");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> SendAsync(CommandContext ctx, QuotationActionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpSend }, req, async (tx, c) =>
        {
            var role = await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayQuote, "quotations (PERM-HAL-002)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var q = await LockAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false);
            var v = Latest(q, req.Version);
            if (!HallQuoteRules.CanSend(v.Status)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This version is already sent (revise it for changes).");
            if (v.Data.Header.Dates[0] < bd) throw new HccException(HccErrorCode.INVALID_TRANSITION, "The event date has passed — revise the dates first.");
            var hl = v.Data.Lines.Select(l => l.Line).ToList();
            if (HallQuoteRules.NeedsApproval(hl, role) && v.ApprovalBy is null)
                throw new HccException(HccErrorCode.HCC_DISCOUNT_LIMIT,
                    $"The discount of {HallQuoteRules.Pct(HallBilling.DiscountBps(hl))} is above your limit ({HallQuoteRules.Pct(HallBilling.DiscountLimitBps(role) ?? 0)}). Ask the Owner to approve it.");
            var tnc = HallQuoteRules.Snapshot(await store.TemplatesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), v.Data.Header.EventType, bd);
            if (!HallQuoteRules.Final(tnc.HasPlaceholder, v.OverrideBy is not null))
                throw new HccException(HccErrorCode.HCC_TNC_PLACEHOLDER, "The T&C still say “OWNER TO REPLACE”. The Owner replaces the template, or overrides for this quotation with a reason.");
            var now = Now;
            var until = HallQuoteRules.ValidUntil(bd);
            await store.SendAsync(tx, v.VersionId, tnc, until, now, c).ConfigureAwait(false);
            await store.SetEnquiryAsync(tx, q.EnquiryId, "QUOTED", null, null, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_QUO_SEND", "hall-quotation-" + N(q.QuotationId), $"{q.Number} v{v.V} sent · valid until {until:yyyy-MM-dd} · T&C {tnc.TemplateCode} v{tnc.V}", c)
                .ConfigureAwait(false);
            return new QuotationResult(q.QuotationId, q.Number, v.V, "SENT", await TotalAsync(tx, ctx.PropertyId, v.Data.Lines, bd, c).ConfigureAwait(false), HallBilling.DiscountBps(hl),
                TemplateCode: tnc.TemplateCode);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> OverrideAsync(CommandContext ctx, QuotationActionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpOverride }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayOverridePlaceholder, "only the Owner overrides the T&C placeholder (PERM-HAL-005)", c, HccErrorCode.HCC_TNC_OWNER_ONLY)
                .ConfigureAwait(false);
            var q = await LockAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false);
            var v = Latest(q, req.Version);
            if (v.Status != "DRAFT") throw new HccException(HccErrorCode.INVALID_TRANSITION, "Override a draft before it is sent.");
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length < 5) throw HccException.Validation("Give the reason for sending with placeholder T&C (at least 5 characters).");
            if (q.Versions.Any(x => x.OverrideBy is not null && x.V != v.V)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This quotation already used its one override (spec §7).");
            await store.SetOverrideAsync(tx, v.VersionId, ctx.PrincipalId, reason.Length > 200 ? reason[..200] : reason, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_TNC_OVERRIDE", "hall-quotation-" + N(q.QuotationId), $"{q.Number} v{v.V} · {reason}", c).ConfigureAwait(false);
            return new QuotationResult(q.QuotationId, q.Number, v.V, v.Status);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> ApproveDiscountAsync(CommandContext ctx, QuotationActionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpApprove }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayApproveDiscount, "a discount above the limit is approved by the Owner", c).ConfigureAwait(false);
            var q = await LockAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false);
            var v = Latest(q, req.Version);
            if (v.Status != "DRAFT") throw new HccException(HccErrorCode.INVALID_TRANSITION, "Approve the discount on a draft.");
            if (v.Data.CreatedBy == ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot approve a discount on your own quotation (no self-approval).");
            var bps = HallBilling.DiscountBps(v.Data.Lines.Select(l => l.Line).ToList());
            await store.SetApprovalAsync(tx, v.VersionId, ctx.PrincipalId, bps, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_QUO_DISCOUNT_APPROVE", "hall-quotation-" + N(q.QuotationId), $"{q.Number} v{v.V} · discount {HallQuoteRules.Pct(bps)} approved", c).ConfigureAwait(false);
            return new QuotationResult(q.QuotationId, q.Number, v.V, v.Status, DiscountBps: bps);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> LoseAsync(CommandContext ctx, QuotationActionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpLost }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayQuote, "quotations (PERM-HAL-002)", c).ConfigureAwait(false);
            var q = await LockAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false);
            var v = Latest(q, req.Version);
            if (!HallQuoteRules.CanLose(v.Status)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only an open quotation can be marked lost.");
            var stored = await store.LostReasonsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var reasons = stored.Count > 0 ? stored : HallQuoteRules.DefaultLostReasons;
            var r = reasons.FirstOrDefault(x => x.Id == req.ReasonId) ?? throw HccException.Validation("Choose a lost reason from the list.");
            var note = (req.Note ?? "").Trim();
            if (r.NeedsNote && note.Length < 3) throw HccException.Validation($"“{r.Label}” needs a note (at least 3 characters).");
            var n = note.Length == 0 ? null : note.Length > 300 ? note[..300] : note;
            await store.SetStatusAsync(tx, v.VersionId, "LOST", r.Id, n, Now, c).ConfigureAwait(false);
            await store.SetEnquiryAsync(tx, q.EnquiryId, "LOST", r.Id, n, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_QUO_LOST", "hall-quotation-" + N(q.QuotationId), $"{q.Number} v{v.V} lost · {r.Label}{(n is null ? "" : " · " + n)}", c).ConfigureAwait(false);
            return new QuotationResult(q.QuotationId, q.Number, v.V, "LOST");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> CancelAsync(CommandContext ctx, QuotationActionRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpCancel }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayQuote, "quotations (PERM-HAL-002)", c).ConfigureAwait(false);
            var q = await LockAsync(tx, ctx.PropertyId, req.QuotationId, c).ConfigureAwait(false);
            var v = Latest(q, req.Version);
            if (!HallQuoteRules.CanCancel(v.Status)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a draft is cancelled (a sent quotation is marked lost).");
            await store.SetStatusAsync(tx, v.VersionId, "CANCELLED", null, null, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_QUO_CANCEL", "hall-quotation-" + N(q.QuotationId), $"{q.Number} v{v.V} cancelled (number kept)", c).ConfigureAwait(false);
            return new QuotationResult(q.QuotationId, q.Number, v.V, "CANCELLED");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> ExpireAsync(CommandContext ctx, ExpireQuotationsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpExpire }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayRunExpiry, "the quotation expiry run (Manager or Owner)", c).ConfigureAwait(false);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var n = await store.ExpireAsync(tx, ctx.PropertyId, bd, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_QUO_EXPIRE", "hall-quotations", $"{n} quotation(s) expired on {bd:yyyy-MM-dd}", c).ConfigureAwait(false);
            return new QuotationResult(0, "", 0, "EXPIRED", Count: n);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<QuotationResult>> SaveTemplateAsync(CommandContext ctx, SaveTncTemplateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        return await runner.RunAsync(ctx with { Operation = OpTemplate }, req, async (tx, c) =>
        {
            await RoleAsync(tx, ctx.PrincipalId, HallQuoteRules.MayEditTemplates, "only the Owner edits T&C templates (PERM-HAL-005)", c, HccErrorCode.HCC_TNC_OWNER_ONLY)
                .ConfigureAwait(false);
            var et = (req.EventType ?? "").Trim();
            if (et != "GENERAL" && !HallRules.EventTypes.Contains(et)) throw HccException.Validation("Choose the event type (or General).");
            if (HallQuoteRules.ClausesProblem(req.Clauses?.Select(x => (x.Heading, x.Text)).ToList()) is { } p) throw HccException.Validation(p);
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var eff = req.EffectiveFrom ?? bd;
            if (eff < bd) throw HccException.Validation("Effective from today on.");
            var now = Now;
            var templates = await store.TemplatesAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (templates.Count == 0 && et != "GENERAL")
            {
                // Materialise the placeholder General template first so other event types keep falling back to it.
                var g = HallQuoteRules.DefaultTemplate();
                await store.SaveTemplateVersionAsync(tx, ctx.PropertyId, g.Code, g.EventType, g.Versions[0].EffectiveFrom, g.Versions[0].Clauses, ctx.PrincipalId, now, c).ConfigureAwait(false);
            }

            var code = templates.FirstOrDefault(t => t.EventType == et)?.Code ?? (et == "GENERAL" ? "T-GEN" : "T-" + et);
            var clauses = req.Clauses!.Select((x, i) => new HallTncClause(i + 1, x.Heading.Trim(), x.Text.Trim())).ToList();
            var vNo = await store.SaveTemplateVersionAsync(tx, ctx.PropertyId, code, et, eff, clauses, ctx.PrincipalId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("HALL_TNC_TEMPLATE_SAVE", "hall-tnc-" + code, $"{code} v{vNo} · {clauses.Count} clauses · from {eff:yyyy-MM-dd}", c).ConfigureAwait(false);
            return new QuotationResult(0, "", vNo, "SAVED", TemplateCode: code);
        }, ct).ConfigureAwait(false);
    }
}
