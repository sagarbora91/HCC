using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Compliance;
using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;

namespace Hcc.Application.Compliance;

/// <summary>
/// Compliance calendar = SCC's tax module in full (DEC-116; prototype handlers/compliance.ts). SCC rules: done with optional filed-on / notes /
/// evidence / reviewer (L2402–2425), not applicable without a reason (L2179–2195), FY switch (L234–239), firm profile (L1187–1216), archive
/// key / category (L1465–1486), custom obligations (L1376–1462), evidence files (L2281–2316). HCC platform safeguards kept: Owner + Accountant
/// use (PERM-CMP-001); the Owner manages and is the only one who can undo a done / NA record, which keeps what it removed (PERM-CMP-002);
/// audit per command (+ outbox on done). Each command checks its permission before the transaction, then runs in one ICommandRunner
/// transaction that first locks the property's compliance settings row. The CA pack is simulated: nothing is sent.
/// </summary>
public sealed partial class ComplianceCommands(ICommandRunner runner, IAccessPolicy policy, IComplianceStore store, TimeProvider clock)
{
    public const string OpMarkDone = "MarkComplianceDone";
    public const string OpMarkNotApplicable = "MarkComplianceNotApplicable";
    public const string OpReopen = "ReopenComplianceItem";
    public const string OpSetFy = "SetComplianceFy";
    public const string OpSaveProfile = "SaveComplianceProfile";
    public const string OpToggleArchive = "ToggleComplianceArchive";
    public const string OpToggleCategory = "ToggleComplianceCategoryArchive";
    public const string OpSaveCustom = "SaveCustomObligation";
    public const string OpDeleteCustom = "DeleteCustomObligation";
    public const string OpAddEvidence = "AddComplianceEvidence";
    public const string OpDeleteEvidence = "DeleteComplianceEvidence";
    public const string OpSaveSettings = "SaveComplianceSettings";
    public const string OpGenerateCaPack = "GenerateCaPack";

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string N(long n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Txt(string? v) => (v ?? string.Empty).Trim();

    private static string Limit(string v, int max, string what) => v.Length <= max ? v : throw HccException.Validation($"{what} is limited to {N(max)} characters.");

    private static int Fy(int fy) => fy is >= SccTaxRules.FyMin and <= SccTaxRules.FyMax
        ? fy : throw HccException.Validation($"Choose a financial year from {SccTaxRules.FyLabel(SccTaxRules.FyMin)} to {SccTaxRules.FyLabel(SccTaxRules.FyMax)}.");

    private static string Label(SccCard c, int fy) => SccTaxRules.StripHtml(SccTaxRules.RenderFyText(c.Name, fy));

    private static string Target(int fy, string itemId) => $"compliance:{N(fy)}:{itemId}";

    /// <summary>SCC: a firm must be selected to record (L2163, L2181, L2404) — here the property's profile, set by the Owner.</summary>
    private static void RequireProfile(ComplianceSnapshot s)
    {
        if (s.Profile is null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Set the compliance firm profile first (Owner: Compliance calendar › Firm profile).");
    }

    /// <summary>SCC findItemMonth: the card must exist (built-in, or a custom obligation's month instance). DEC-133: a licence card only in the FY
    /// of its expiry date.</summary>
    private static SccCard Card(SccCalendar cal, string? itemId, int fy) =>
        cal.FindCard(Txt(itemId), fy) ?? throw new HccException(HccErrorCode.NOT_FOUND, "That compliance item is not in the calendar.");

    private async Task<(ComplianceSnapshot Snap, SccCalendar Cal, DateOnly Today)> LoadAsync(ICommandTransaction tx, int propertyId, CancellationToken ct)
    {
        await store.LockSettingsAsync(tx, propertyId, ct).ConfigureAwait(false);
        var snap = await store.SnapshotAsync(tx, propertyId, ct).ConfigureAwait(false);
        return (snap, snap.Calendar(), await store.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false));
    }

    // P6-GUARD: compliance records are not business-date postings (no folio, cash or room-night effect), so a closed business day does not block
    // them; no IBusinessDayGuard call is made (as W2-5).

    public async Task<CommandResult<MarkDoneResult>> MarkDoneAsync(CommandContext ctx, MarkDoneRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var fy = Fy(req.FyStart);
        var notes = Limit(Txt(req.Notes), ComplianceRules.MaxNotes, "Notes");
        var evidence = Limit(Txt(req.EvidenceRef), ComplianceRules.MaxEvidenceRef, "Evidence");
        var reviewer = Limit(Txt(req.ReviewerNote), ComplianceRules.MaxReviewer, "Reviewer");
        DateOnly? filedReq = null;
        if (!string.IsNullOrWhiteSpace(req.FiledOn))
        {
            if (!DateOnly.TryParseExact(req.FiledOn.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) throw HccException.Validation("Filed on must be a valid date.");
            filedReq = f;
        }

        return await runner.RunAsync(ctx with { Operation = OpMarkDone }, req, async (tx, c) =>
        {
            var (snap, cal, today) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            RequireProfile(snap);
            var card = Card(cal, req.ItemId, fy);
            var cur = cal.Record(fy, card.Id);
            if (cur?.Na == true)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "This item is marked Not Applicable (NA) and is read-only. Remove NA first to record a filing (Owner: Reopen).");
            if (cur?.Done == true) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Already marked filed. The Owner can reopen it.");
            var filedOn = filedReq ?? today;
            var due = SccTaxRules.ParseDue(card.Due, fy) ?? cur?.DueDate;
            var now = UtcNow;
            await store.UpsertRecordAsync(tx, ctx.PropertyId, new CmpRecord(fy, card.Id, true, false, filedOn, notes, evidence, reviewer, due, ctx.PrincipalId, now, null, null, null), c)
                .ConfigureAwait(false);
            var late = due is { } d ? Math.Max(0, filedOn.DayNumber - d.DayNumber) : 0;
            await tx.AuditAsync("COMPLIANCE_DONE", Target(fy, card.Id),
                $"{Label(card, fy)} · {SccTaxRules.FyLabel(fy)} · filed on {D(filedOn)}{(evidence.Length > 0 ? " · evidence " + evidence : string.Empty)}{(late > 0 ? $" · {N(late)} day(s) after the due date" : string.Empty)}", c)
                .ConfigureAwait(false);
            await tx.PublishAsync("ComplianceFiled", ctx.PropertyId, new { fyStart = fy, itemId = card.Id, filedOn = D(filedOn), due = due is { } dd ? D(dd) : null, daysLate = late }, c)
                .ConfigureAwait(false);
            return new MarkDoneResult(fy, card.Id, D(filedOn), due is { } x ? D(x) : null, late);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ItemResult>> MarkNotApplicableAsync(CommandContext ctx, MarkNotApplicableRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var fy = Fy(req.FyStart);
        return await runner.RunAsync(ctx with { Operation = OpMarkNotApplicable }, req, async (tx, c) =>
        {
            var (snap, cal, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            RequireProfile(snap);
            var card = Card(cal, req.ItemId, fy);
            var cur = cal.Record(fy, card.Id);
            if (cur?.Na == true) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Already marked not applicable. The Owner can reopen it.");
            var rec = (cur ?? new CmpRecord(fy, card.Id, false, false, null, null, null, null, null, null, null, null, null, null)) with { Na = true, NaBy = ctx.PrincipalId, NaUtc = UtcNow };
            await store.UpsertRecordAsync(tx, ctx.PropertyId, rec, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_NA", Target(fy, card.Id), $"{Label(card, fy)} · {SccTaxRules.FyLabel(fy)} · not applicable", c).ConfigureAwait(false);
            return new ItemResult(fy, card.Id, "na");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC un-NA (removes the flag only) and undo done (deletes the record) — Owner only, with a reason, the removed record kept.</summary>
    public async Task<CommandResult<ItemResult>> ReopenAsync(CommandContext ctx, ReopenItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var fy = Fy(req.FyStart);
        var reason = ComplianceRules.CleanReason(req.Reason);
        return await runner.RunAsync(ctx with { Operation = OpReopen }, req, async (tx, c) =>
        {
            var (_, cal, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var id = Txt(req.ItemId);
            var cur = cal.Record(fy, id);
            if (cur is null || (!cur.Na && !cur.Done)) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This item is not filed or marked not applicable.");
            var part = cur.Na ? "na" : "done";
            if (part == "na" && cur.Done) await store.UpsertRecordAsync(tx, ctx.PropertyId, cur with { Na = false, NaBy = null, NaUtc = null }, c).ConfigureAwait(false);
            else await store.DeleteRecordAsync(tx, ctx.PropertyId, fy, id, c).ConfigureAwait(false);
            var now = UtcNow;
            await store.InsertReopenedAsync(tx, ctx.PropertyId, new ReopenedRecord(fy, id, part, cur, ctx.PrincipalId, now, reason), c).ConfigureAwait(false);
            var name = cal.FindCard(id) is { } card ? Label(card, fy) : id;
            await tx.AuditAsync("COMPLIANCE_REOPEN", Target(fy, id),
                $"{name} · {SccTaxRules.FyLabel(fy)} · reopened (was {(part == "na" ? "not applicable" : "filed" + (cur.FiledOn is { } f ? " on " + D(f) : string.Empty))}{(string.IsNullOrEmpty(cur.EvidenceRef) ? string.Empty : " · evidence " + cur.EvidenceRef)}) · {reason}", c)
                .ConfigureAwait(false);
            return new ItemResult(fy, id, part);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC switchFY (L234–239). DEC-125: the pick holds until the next 1 April of the business date (stored with the business date's FY);
    /// after that every read shows the business date's FY again (<see cref="SccTaxRules.EffectiveFy"/>).</summary>
    public async Task<CommandResult<FyResult>> SetFyAsync(CommandContext ctx, SetFyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var fy = Fy(req.FyStart);
        return await runner.RunAsync(ctx with { Operation = OpSetFy }, req, async (tx, c) =>
        {
            await store.LockSettingsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var today = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await store.SetFyAsync(tx, ctx.PropertyId, fy, SccTaxRules.FyOfDate(today), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_FY", "compliance", $"Compliance calendar · {SccTaxRules.FyLabel(fy)}", c).ConfigureAwait(false);
            return new FyResult(fy);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC saveFirm (L1187–1216): name required; PAN / GSTIN upper-cased, no format check; QRMP only for regular / both.</summary>
    public async Task<CommandResult<VersionResult>> SaveProfileAsync(CommandContext ctx, SaveProfileRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var name = Limit(Txt(req.Name), ComplianceRules.MaxFirmName, "Firm name");
        if (name.Length == 0) throw HccException.Validation("Please enter a firm name.");
        var gstType = string.IsNullOrWhiteSpace(req.GstType) ? "regular" : req.GstType.Trim();
        if (!SccTaxRules.GstTypes.Contains(gstType)) throw HccException.Validation("Choose the GST registration type.");
        var entity = string.IsNullOrWhiteSpace(req.Entity) ? "proprietorship" : req.Entity.Trim();
        if (!SccTaxRules.Entities.Contains(entity)) throw HccException.Validation("Choose the entity type.");
        var draft = new CmpProfile(name, Limit(Txt(req.Pan).ToUpperInvariant(), ComplianceRules.MaxPan, "PAN"), Limit(Txt(req.Gstin).ToUpperInvariant(), ComplianceRules.MaxGstin, "GSTIN"),
            entity, gstType, req.HasEmployees, gstType is "regular" or "both" && req.Qrmp, Limit(Txt(req.Notes), ComplianceRules.MaxNotes, "Notes"));
        // DEC-133: licence numbers / expiry dates — null keeps the stored value, "" clears it.
        string? No(string? v, string what) => v is null ? null : Limit(v.Trim(), ComplianceRules.MaxLicenceNo, what);
        DateOnly? Exp(string? v, string what) => string.IsNullOrWhiteSpace(v) ? null
            : DateOnly.TryParseExact(v.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation($"{what} must be a valid date.");
        var fssaiNo = No(req.FssaiNo, "FSSAI licence number");
        var fssaiExp = Exp(req.FssaiExpiry, "FSSAI licence expiry");
        var lodgingNo = No(req.LodgingNo, "Lodging licence number");
        var lodgingExp = Exp(req.LodgingExpiry, "Lodging licence expiry");
        var expected = string.IsNullOrWhiteSpace(req.ExpectedVersion) ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveProfile }, req, async (tx, c) =>
        {
            var (snap, _, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (snap.Profile is { } cur)
            {
                if (expected is null) throw HccException.Validation("expectedVersion is required to change the firm profile.");
                VersionToken.EnsureMatches(cur.Version, expected, "The compliance firm profile");
            }

            var was = snap.Profile?.Profile;
            static string? Keep(string? v, string? old) => v is null ? (string.IsNullOrEmpty(old) ? null : old) : v.Length == 0 ? null : v;
            var profile = draft with
            {
                FssaiNo = Keep(fssaiNo, was?.FssaiNo), FssaiExpiry = req.FssaiExpiry is null ? was?.FssaiExpiry : fssaiExp,
                LodgingNo = Keep(lodgingNo, was?.LodgingNo), LodgingExpiry = req.LodgingExpiry is null ? was?.LodgingExpiry : lodgingExp,
            };

            var v = await store.SaveProfileAsync(tx, ctx.PropertyId, snap.Profile is null ? null : expected, profile, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_PROFILE", "compliance",
                $"Compliance firm profile · {profile.Name} · {SccTaxRules.TypeLabel(profile.GstType)}{(profile.Qrmp ? " · QRMP" : string.Empty)}{(profile.HasEmployees ? string.Empty : " · no employees")}{ComplianceRules.LicenceAudit(profile)}", c)
                .ConfigureAwait(false);
            return new VersionResult(VersionToken.ToWire(v));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC toggleArchive (L1465–1473): a built-in obligation key or a custom obligation id.</summary>
    public async Task<CommandResult<ArchiveResult>> ToggleArchiveAsync(CommandContext ctx, ToggleArchiveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var key = Txt(req.Key);
        return await runner.RunAsync(ctx with { Operation = OpToggleArchive }, req, async (tx, c) =>
        {
            var (snap, _, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var custom = snap.Custom.FirstOrDefault(x => x.Custom.Id == key);
            if (key.Length == 0 || (custom is null && !SccTaxCatalogue.BuiltInObligations().Any(o => o.Key == key) && !LicenceCards.Obligations().Any(o => o.Key == key)))
                throw new HccException(HccErrorCode.NOT_FOUND, "Unknown obligation.");
            var archived = await store.ToggleArchiveAsync(tx, ctx.PropertyId, key, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_ARCHIVE", "compliance:archive", $"{custom?.Custom.Name ?? key} · {(archived ? "archived" : "restored")}", c).ConfigureAwait(false);
            return new ArchiveResult(key, archived);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC toggleCatArchive (L1477–1486).</summary>
    public async Task<CommandResult<ArchiveResult>> ToggleCategoryAsync(CommandContext ctx, ToggleCategoryArchiveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var cat = Txt(req.Cat);
        if (!SccTaxRules.Cats.Contains(cat)) throw new HccException(HccErrorCode.NOT_FOUND, "Unknown category.");
        return await runner.RunAsync(ctx with { Operation = OpToggleCategory }, req, async (tx, c) =>
        {
            await store.LockSettingsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var archived = await store.ToggleArchiveCategoryAsync(tx, ctx.PropertyId, cat, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_ARCHIVE", "compliance:archive", $"{SccTaxRules.CatLabel(cat)} · category {(archived ? "archived" : "restored")}", c).ConfigureAwait(false);
            return new ArchiveResult(cat, archived);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC saveCustomObligation (L1376–1416): name and ≥ 1 month; due day 0–31 in the ticked month; an edit applies to every month.</summary>
    public async Task<CommandResult<CustomObligationResult>> SaveCustomAsync(CommandContext ctx, SaveCustomObligationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var name = Limit(Txt(req.Name), ComplianceRules.MaxName, "Name");
        if (name.Length == 0) throw HccException.Validation("Please enter an obligation name.");
        var months = (req.Months ?? []).Select(m => (m ?? string.Empty).Trim()).ToList();
        if (months.Count == 0) throw HccException.Validation("Please select at least one applicable month.");
        if (months.Any(m => !SccTaxCatalogue.Months.Contains(m))) throw HccException.Validation("Choose months from April to March.");
        if (req.DueDay is < 0 or > 31) throw HccException.Validation("Due day must be “Last day” or 1–31.");
        var cat = string.IsNullOrWhiteSpace(req.Cat) ? "gst" : req.Cat.Trim();
        if (!SccTaxRules.Cats.Contains(cat)) throw HccException.Validation("Choose a category.");
        var freq = string.IsNullOrWhiteSpace(req.Freq) ? "Monthly" : req.Freq.Trim();
        if (!SccTaxRules.Freqs.Contains(freq)) throw HccException.Validation("Choose a frequency.");
        var risk = string.IsNullOrWhiteSpace(req.Risk) ? null : req.Risk.Trim();
        if (risk is not (null or "High" or "Medium" or "Low")) throw HccException.Validation("Risk must be High, Medium, Low or automatic.");
        IReadOnlyList<string> List(IReadOnlyList<string>? v, string what)
        {
            var l = (v ?? []).Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0).ToList();
            if (l.Count > ComplianceRules.MaxListLines) throw HccException.Validation($"{what}: at most {N(ComplianceRules.MaxListLines)} lines.");
            foreach (var s in l) Limit(s, ComplianceRules.MaxShort, what);
            return l;
        }

        string T(string? v, string what, int max = ComplianceRules.MaxText) => Limit(Txt(v), max, what);
        var ordered = SccTaxCatalogue.Months.Where(months.Contains).ToList();
        var buffer = req.Buffer is { } b ? Math.Clamp(b, 0, 30) : (int?)null;
        var draft = new CmpCustom(req.CustomId ?? 0, cat, name, freq, req.DueDay, ordered, T(req.Form, "Form / portal", ComplianceRules.MaxShort), T(req.Who, "Who"), T(req.What, "What"),
            T(req.Why, "Why"), T(req.Penalty, "Penalty"), T(req.Responsible, "Responsible", ComplianceRules.MaxShort), T(req.Reviewer, "Reviewer", ComplianceRules.MaxShort), risk, buffer,
            List(req.Checklist, "Checklist"), List(req.Workflow, "Workflow"), List(req.Evidence, "Evidence"));
        var expected = req.CustomId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveCustom }, req, async (tx, c) =>
        {
            var (snap, _, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var now = UtcNow;
            long id;
            byte[] v;
            if (req.CustomId is { } existing)
            {
                var cur = snap.Custom.FirstOrDefault(x => x.Custom.CustomId == existing) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Custom obligation not found.");
                VersionToken.EnsureMatches(cur.Version, expected!, "This custom obligation");
                v = await store.UpdateCustomAsync(tx, ctx.PropertyId, draft, expected!, ctx.PrincipalId, now, c).ConfigureAwait(false);
                id = existing;
            }
            else (id, v) = await store.InsertCustomAsync(tx, ctx.PropertyId, draft, ctx.PrincipalId, now, c).ConfigureAwait(false);

            var shorter = req.DueDay >= 29 && ordered.Any(m => DateTime.DaysInMonth(2026, Array.IndexOf(CultureInfo.InvariantCulture.DateTimeFormat.MonthNames, m) + 1) < req.DueDay);
            var cid = "cust-" + N(id);
            await tx.AuditAsync("COMPLIANCE_CUSTOM", "compliance:" + cid,
                $"{cid} · {name} {(req.CustomId is null ? "added" : "updated")} · {(req.DueDay == 0 ? "last day" : "day " + N(req.DueDay))} of {string.Join(", ", ordered.Select(m => m[..3]))}{(shorter ? " · rolls over into the next month in the shorter months (as SCC)" : string.Empty)}", c)
                .ConfigureAwait(false);
            return new CustomObligationResult(id, cid, VersionToken.ToWire(v));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC deleteCustomObligation (L1454–1462): removed from the calendar, its evidence files deleted; records stay.</summary>
    public async Task<CommandResult<DeleteCustomResult>> DeleteCustomAsync(CommandContext ctx, DeleteCustomObligationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpDeleteCustom }, req, async (tx, c) =>
        {
            var (snap, _, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var cur = snap.Custom.FirstOrDefault(x => x.Custom.CustomId == req.CustomId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Custom obligation not found.");
            var gone = await store.DeleteCustomAsync(tx, ctx.PropertyId, req.CustomId, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_CUSTOM", "compliance:" + cur.Custom.Id,
                $"{cur.Custom.Id} · {cur.Custom.Name} deleted{(gone > 0 ? $" · {N(gone)} evidence file(s) removed" : string.Empty)}", c).ConfigureAwait(false);
            return new DeleteCustomResult(req.CustomId, gone);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC evAttachHere (L2294–2299): a file per FY and card (firm required). HCC limit 5 MB.</summary>
    public async Task<CommandResult<EvidenceResult>> AddEvidenceAsync(CommandContext ctx, AddEvidenceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var fy = Fy(req.FyStart);
        var fileName = Limit(string.IsNullOrWhiteSpace(req.FileName) ? "evidence" : req.FileName.Trim(), ComplianceRules.MaxFileName, "File name");
        var type = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType.Trim();
        if (type.Length > 100) type = "application/octet-stream";
        if (string.IsNullOrEmpty(req.Base64) || req.Base64.Length > (ComplianceRules.MaxFileBytes / 3 + 2) * 4) throw HccException.Validation("An evidence file can be at most 5 MB.");
        byte[] content;
        try
        {
            content = Convert.FromBase64String(req.Base64);
        }
        catch (FormatException)
        {
            throw HccException.Validation("Choose a file to attach.");
        }

        if (content.Length > ComplianceRules.MaxFileBytes) throw HccException.Validation("An evidence file can be at most 5 MB.");
        return await runner.RunAsync(ctx with { Operation = OpAddEvidence }, req, async (tx, c) =>
        {
            var (snap, cal, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            RequireProfile(snap);
            var card = Card(cal, req.ItemId, fy);
            var id = await store.InsertEvidenceAsync(tx, ctx.PropertyId, fy, card.Id, fileName, type, content, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_EVIDENCE", Target(fy, card.Id), $"{Label(card, fy)} · {SccTaxRules.FyLabel(fy)} · evidence attached · {fileName} ({N(content.Length)} bytes)", c)
                .ConfigureAwait(false);
            return new EvidenceResult(id, content.Length);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC evDelete (L2288–2293).</summary>
    public async Task<CommandResult<EvidenceResult>> DeleteEvidenceAsync(CommandContext ctx, DeleteEvidenceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpDeleteEvidence }, req, async (tx, c) =>
        {
            var (_, cal, _) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var gone = await store.DeleteEvidenceAsync(tx, ctx.PropertyId, req.EvidenceId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Evidence file not found.");
            var name = cal.FindCard(gone.ItemId) is { } card ? Label(card, gone.FyStart) : gone.ItemId;
            await tx.AuditAsync("COMPLIANCE_EVIDENCE", Target(gone.FyStart, gone.ItemId), $"{name} · {SccTaxRules.FyLabel(gone.FyStart)} · evidence removed · {gone.FileName}", c).ConfigureAwait(false);
            return new EvidenceResult(gone.EvidenceId, gone.SizeBytes);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>HCC extras kept: CA name / e-mail for the simulated pack and the "Due this week" window.</summary>
    public async Task<CommandResult<SettingsDto>> SaveSettingsAsync(CommandContext ctx, SaveSettingsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var name = req.CaName?.Trim();
        var email = req.CaEmail?.Trim();
        if (name is not null && (name.Length < 3 || name.Length > 60)) throw HccException.Validation("CA name must be 3–60 characters.");
        if (email is not null && (!EmailPattern().IsMatch(email) || email.Length > 80)) throw HccException.Validation("Enter a valid e-mail address.");
        if (req.DueSoonDays is { } dd && (dd < 1 || dd > 30)) throw HccException.Validation("“Due this week” window must be 1–30 days.");
        return await runner.RunAsync(ctx with { Operation = OpSaveSettings }, req, async (tx, c) =>
        {
            var cur = await store.LockSettingsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (cur.IsDefault && expected.All(b => b == 0)) expected = cur.Version; // never saved: the read returned the all-zero token
            else VersionToken.EnsureMatches(cur.Version, expected, "The compliance settings");
            var next = cur with { CaName = name ?? cur.CaName, CaEmail = email ?? cur.CaEmail, DueSoonDays = req.DueSoonDays ?? cur.DueSoonDays };
            await store.UpdateSettingsAsync(tx, ctx.PropertyId, expected, next.CaName, next.CaEmail, next.DueSoonDays, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_SETTINGS", "compliance", $"Compliance settings saved · CA {next.CaName} · window {N(next.DueSoonDays)} days", c).ConfigureAwait(false);
            var s = await store.LockSettingsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            return new SettingsDto(s.CaName, s.CaEmail, s.DueSoonDays, VersionToken.ToWire(s.Version));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The simulated CA pack (SCC sends its shell's WhatsApp "ca_pack" template, not supplied): nothing is sent.</summary>
    public async Task<CommandResult<CaPackResult>> GenerateCaPackAsync(CommandContext ctx, GenerateCaPackRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpGenerateCaPack }, req, async (tx, c) =>
        {
            var (snap, cal, today) = await LoadAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snap.Settings.CaEmail)) throw HccException.Validation("Set the CA e-mail in Manage compliance first.");
            var p = ComplianceRules.PackLines(cal, snap.Settings.Effective(today), today);
            var now = UtcNow;
            var pack = new PackRecord(0, now, ctx.PrincipalId, snap.Settings.CaEmail, snap.Settings.CaName, p.Overdue, p.DueSoon, p.Done, p.NotApplicable, p.Lines);
            var id = await store.InsertPackAsync(tx, ctx.PropertyId, pack, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_CA_PACK", "pack:" + N(id), $"CA pack to {snap.Settings.CaName} (simulated — nothing sent) · {N(p.Overdue)} overdue, {N(p.Open)} pending", c)
                .ConfigureAwait(false);
            return new CaPackResult(id, pack.To, pack.ToName, p.Overdue, p.Open, p.Lines.Count, ComplianceWire.Utc(now));
        }, ct).ConfigureAwait(false);
    }
}

internal static class ComplianceWire
{
    public static string Utc(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string? Utc(DateTime? utc) => utc is { } u ? Utc(u) : null;

    public static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
