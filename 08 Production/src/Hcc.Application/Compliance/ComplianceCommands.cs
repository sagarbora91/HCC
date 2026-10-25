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
/// Compliance calendar use cases (W2-5; prototype handlers/compliance.ts). Owner + Accountant file / mark not applicable / send the CA
/// pack (PERM-CMP-001); Owner only reopens, manages obligations and due rules, settings and seeding (PERM-CMP-002). Each command checks its
/// permission before the transaction, then runs in one <see cref="ICommandRunner"/> transaction with audit (+ outbox for filing events).
/// The CA pack is simulated: nothing is sent.
/// </summary>
public sealed partial class ComplianceCommands(ICommandRunner runner, IAccessPolicy policy, IComplianceStore store, TimeProvider clock)
{
    public const string OpMarkFiled = "MarkComplianceFiled";
    public const string OpMarkNotApplicable = "MarkComplianceNotApplicable";
    public const string OpReopen = "ReopenComplianceFiling";
    public const string OpSaveObligation = "SaveComplianceObligation";
    public const string OpToggleObligation = "ToggleComplianceObligation";
    public const string OpSaveSettings = "SaveComplianceSettings";
    public const string OpGenerateCaPack = "GenerateCaPack";
    public const string OpSeedDefaults = "SeedComplianceDefaults";

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string N(long n) => n.ToString(CultureInfo.InvariantCulture);

    // P6-GUARD: compliance filings and settings are not business-date postings (no folio, cash or room-night effect), so a closed business
    // day does not block them; no IBusinessDayGuard call is made. Revisit if the lead wants filings refused on a closed date.

    public async Task<CommandResult<FilingResult>> MarkFiledAsync(CommandContext ctx, MarkFiledRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpMarkFiled }, req, async (tx, c) =>
        {
            var (o, period, today) = await ResolveAsync(tx, ctx.PropertyId, req.ObligationId, req.Period, c).ConfigureAwait(false);
            EnsureOpen(o, await store.GetFilingAsync(tx, o.ObligationId, period, c).ConfigureAwait(false));
            var reference = ComplianceRules.CleanReference(req.Reference);
            var now = UtcNow;
            await store.InsertFilingAsync(tx, new FilingRecord(o.ObligationId, period, ComplianceStatus.Filed, ctx.PrincipalId, now, reference, null), c).ConfigureAwait(false);
            var due = ComplianceRules.DueDate(o.Schedule, period);
            var late = Math.Max(0, today.DayNumber - due.DayNumber);
            await tx.AuditAsync("COMPLIANCE_FILE", Target(o.ObligationId, period),
                $"{o.Name} · {ComplianceRules.PeriodLabel(o.Frequency, period)} filed · ref {reference}{(late > 0 ? $" · {N(late)} day(s) late" : string.Empty)}", c).ConfigureAwait(false);
            await tx.PublishAsync("ComplianceFiled", o.ObligationId, new { o.ObligationId, period, kind = o.Kind, due = D(due), daysLate = late }, c).ConfigureAwait(false);
            return new FilingResult(o.ObligationId, period, ComplianceStatus.Filed, D(due), late, ComplianceWire.Utc(now));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FilingResult>> MarkNotApplicableAsync(CommandContext ctx, MarkNotApplicableRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpMarkNotApplicable }, req, async (tx, c) =>
        {
            var (o, period, today) = await ResolveAsync(tx, ctx.PropertyId, req.ObligationId, req.Period, c).ConfigureAwait(false);
            EnsureOpen(o, await store.GetFilingAsync(tx, o.ObligationId, period, c).ConfigureAwait(false));
            var reason = ComplianceRules.CleanReason(req.Reason);
            var now = UtcNow;
            await store.InsertFilingAsync(tx, new FilingRecord(o.ObligationId, period, ComplianceStatus.NotApplicable, ctx.PrincipalId, now, null, reason), c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_NA", Target(o.ObligationId, period), $"{o.Name} · {ComplianceRules.PeriodLabel(o.Frequency, period)} not applicable · {reason}", c).ConfigureAwait(false);
            var due = ComplianceRules.DueDate(o.Schedule, period);
            return new FilingResult(o.ObligationId, period, ComplianceStatus.NotApplicable, D(due), Math.Max(0, today.DayNumber - due.DayNumber), ComplianceWire.Utc(now));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ReopenResult>> ReopenAsync(CommandContext ctx, ReopenFilingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpReopen }, req, async (tx, c) =>
        {
            var (o, period, _) = await ResolveAsync(tx, ctx.PropertyId, req.ObligationId, req.Period, c).ConfigureAwait(false);
            var filing = await store.GetFilingAsync(tx, o.ObligationId, period, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This period is not filed or marked not applicable.");
            var reason = ComplianceRules.CleanReason(req.Reason);
            await store.DeleteFilingAsync(tx, o.ObligationId, period, c).ConfigureAwait(false);
            await store.InsertReopenedAsync(tx, ctx.PropertyId, filing, ctx.PrincipalId, UtcNow, reason, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_REOPEN", Target(o.ObligationId, period),
                $"{o.Name} · {ComplianceRules.PeriodLabel(o.Frequency, period)} reopened (was {filing.Status}{(filing.Reference is null ? string.Empty : " · ref " + filing.Reference)}) · {reason}", c).ConfigureAwait(false);
            return new ReopenResult(o.ObligationId, period, filing.Status);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<SaveObligationResult>> SaveObligationAsync(CommandContext ctx, SaveObligationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(req.Rule);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var name = (req.Name ?? string.Empty).Trim();
        var form = (req.Form ?? string.Empty).Trim();
        var owner = string.IsNullOrWhiteSpace(req.Owner) ? "Accountant" : req.Owner.Trim();
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (name.Length is < 3 or > 60) throw HccException.Validation("Obligation name must be 3–60 characters.");
        if (form.Length > 60) throw HccException.Validation("Form / portal is limited to 60 characters.");
        if (owner.Length > 30) throw HccException.Validation("Owner is limited to 30 characters.");
        if (note is { Length: > 300 }) throw HccException.Validation("Note is limited to 300 characters.");
        DateOnly? effectiveReq = null;
        if (!string.IsNullOrWhiteSpace(req.EffectiveFrom))
        {
            if (!DateOnly.TryParseExact(req.EffectiveFrom.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e))
                throw HccException.Validation("Effective-from must be a valid date, today or later.");
            effectiveReq = e;
        }

        var expected = req.Id is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveObligation }, req, async (tx, c) =>
        {
            var today = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var effective = effectiveReq ?? today;
            if (effective < today) throw HccException.Validation("Effective-from must be a valid date, today or later.");
            var now = UtcNow;
            if (req.Id is { } id)
            {
                var o = await store.LockObligationAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Obligation not found.");
                VersionToken.EnsureMatches(o.Version, expected!, "This obligation");
                if (await store.NameExistsAsync(tx, ctx.PropertyId, name, o.ObligationId, c).ConfigureAwait(false)) throw HccException.Validation("An obligation with this name already exists.");
                var rule = ParseRule(o.Frequency, req.Rule);
                var changed = rule != o.Rules.OrderBy(r => r.From).Last().Rule;
                await store.UpdateObligationAsync(tx, o.ObligationId, expected!, name, form, owner, note, ctx.PrincipalId, now, c).ConfigureAwait(false);
                if (changed) await store.UpsertRuleAsync(tx, o.ObligationId, new DatedRule(effective, rule), c).ConfigureAwait(false);
                await tx.AuditAsync("COMPLIANCE_OBLIGATION", "obligation:" + N(o.ObligationId),
                    changed ? $"{name} · due rule changed from {D(effective)} (periods that began earlier keep the old rule)" : $"{name} updated", c).ConfigureAwait(false);
                var v = await store.ObligationVersionAsync(tx, o.ObligationId, c).ConfigureAwait(false);
                return new SaveObligationResult(o.ObligationId, VersionToken.ToWire(v), changed ? "RuleChanged" : "Updated", o.FromPeriod, changed);
            }

            var kind = (req.Kind ?? string.Empty).Trim();
            if (!ComplianceKinds.All.Contains(kind)) throw HccException.Validation("Choose a type: TDS, GSTR-1, GSTR-3B, GSTR-9, EPF, ESIC or PT.");
            if (await store.NameExistsAsync(tx, ctx.PropertyId, name, null, c).ConfigureAwait(false)) throw HccException.Validation("An obligation with this name already exists.");
            if (await store.CountObligationsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false) >= ComplianceRules.MaxObligations) throw HccException.Validation("The calendar is limited to 30 obligations.");
            var freq = kind == ComplianceKinds.Gstr9 ? ComplianceFrequency.Annual : ComplianceFrequency.Monthly;
            var first = ParseRule(freq, req.Rule);
            var from = ComplianceRules.DefaultTrackedFrom(freq, today);
            var newId = await store.InsertObligationAsync(tx, ctx.PropertyId, kind, name, form, freq, owner, from, note, first, ctx.PrincipalId, now, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_OBLIGATION", "obligation:" + N(newId), $"{name} added (tracked from {ComplianceRules.PeriodLabel(freq, from)})", c).ConfigureAwait(false);
            var ver = await store.ObligationVersionAsync(tx, newId, c).ConfigureAwait(false);
            return new SaveObligationResult(newId, VersionToken.ToWire(ver), "Added", from, false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ToggleObligationResult>> ToggleObligationAsync(CommandContext ctx, ToggleObligationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpToggleObligation }, req, async (tx, c) =>
        {
            var o = await store.LockObligationAsync(tx, ctx.PropertyId, req.ObligationId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Obligation not found.");
            VersionToken.EnsureMatches(o.Version, expected, "This obligation");
            var active = !o.Active;
            await store.SetActiveAsync(tx, o.ObligationId, expected, active, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_OBLIGATION", "obligation:" + N(o.ObligationId), $"{o.Name} · {(active ? "switched on" : "switched off (history kept)")}", c).ConfigureAwait(false);
            return new ToggleObligationResult(o.ObligationId, active, VersionToken.ToWire(await store.ObligationVersionAsync(tx, o.ObligationId, c).ConfigureAwait(false)));
        }, ct).ConfigureAwait(false);
    }

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

    public async Task<CommandResult<CaPackResult>> GenerateCaPackAsync(CommandContext ctx, GenerateCaPackRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpGenerateCaPack }, req, async (tx, c) =>
        {
            var today = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var snap = await store.SnapshotAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snap.Settings.CaEmail)) throw HccException.Validation("Set the CA e-mail in Manage obligations first.");
            var items = ComplianceCalendar.Build(snap, today);
            var counts = ComplianceRules.Count(items.Select(i => i.Row.Status));
            var rows = items.Select(i => new PackRow(i.Obligation.Name, i.Row.PeriodLabel, i.Row.Due, i.Row.Days, i.Row.Status,
                i.Filing is null ? null : DateOnly.FromDateTime(i.Filing.AtUtc), i.Filing?.Reference)).ToList();
            var now = UtcNow;
            var pack = new PackRecord(0, now, ctx.PrincipalId, snap.Settings.CaEmail, snap.Settings.CaName, counts.Overdue, counts.Due, counts.Filed, counts.NotApplicable, ComplianceRules.PackLines(today, rows));
            var id = await store.InsertPackAsync(tx, ctx.PropertyId, pack, c).ConfigureAwait(false);
            await tx.AuditAsync("COMPLIANCE_CA_PACK", "pack:" + N(id),
                $"CA pack to {snap.Settings.CaName} (simulated — nothing sent) · {N(counts.Overdue)} overdue, {N(counts.Open)} open", c).ConfigureAwait(false);
            return new CaPackResult(id, pack.To, pack.ToName, counts.Overdue, counts.Open, pack.Lines.Count, ComplianceWire.Utc(now));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<SeedDefaultsResult>> SeedDefaultsAsync(CommandContext ctx, SeedDefaultsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, CompliancePermissions.Manage, null, null, ct).ConfigureAwait(false);
        if (req.TrackedFromMonth is not null && !ComplianceRules.IsMonthPeriod(req.TrackedFromMonth)) throw HccException.Validation("Tracked-from month must look like 2026-04.");
        if (req.TrackedFromFy is not null && !ComplianceRules.IsFyPeriod(req.TrackedFromFy)) throw HccException.Validation("Tracked-from financial year must look like 2025-26.");
        return await runner.RunAsync(ctx with { Operation = OpSeedDefaults }, req, async (tx, c) =>
        {
            if (await store.CountObligationsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false) > 0)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "The compliance calendar already has obligations.");
            var today = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var now = UtcNow;
            foreach (var d in ComplianceDefaults.Obligations)
            {
                var from = d.Frequency == ComplianceFrequency.Annual
                    ? req.TrackedFromFy ?? ComplianceRules.DefaultTrackedFrom(d.Frequency, today)
                    : req.TrackedFromMonth ?? ComplianceRules.DefaultTrackedFrom(d.Frequency, today);
                await store.InsertObligationAsync(tx, ctx.PropertyId, d.Kind, d.Name, d.Form, d.Frequency, d.Owner, from, d.Note, d.Rule, ctx.PrincipalId, now, c).ConfigureAwait(false);
            }

            await tx.AuditAsync("COMPLIANCE_SEED", "compliance", $"{N(ComplianceDefaults.Obligations.Count)} default obligations added (illustrative due dates — CA to confirm)", c).ConfigureAwait(false);
            return new SeedDefaultsResult(ComplianceDefaults.Obligations.Count);
        }, ct).ConfigureAwait(false);
    }

    private static DueRule ParseRule(string frequency, DueRuleDto r) => ComplianceRules.ParseRule(frequency, r.Type, r.Day, r.Month, r.SpecialMonth, r.SpecialDay);

    private static string Target(long obligationId, string period) => $"obligation:{N(obligationId)}:{period}";

    private static void EnsureOpen(ObligationRecord o, FilingRecord? filing)
    {
        if (!o.Active) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This obligation is switched off.");
        if (filing is not null)
            throw new HccException(HccErrorCode.INVALID_TRANSITION,
                filing.Status == ComplianceStatus.Filed ? "Already marked filed. The Owner can reopen it." : "Already marked not applicable. The Owner can reopen it.");
    }

    /// <summary>Locks the obligation and checks the period is on its calendar (tracked, finished period).</summary>
    private async Task<(ObligationRecord O, string Period, DateOnly Today)> ResolveAsync(ICommandTransaction tx, int propertyId, long obligationId, string? period, CancellationToken ct)
    {
        var o = await store.LockObligationAsync(tx, propertyId, obligationId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "That obligation period is not on the calendar.");
        var today = await store.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false);
        var p = (period ?? string.Empty).Trim();
        if (!ComplianceRules.Periods(o.Frequency, o.FromPeriod, today).Contains(p)) throw new HccException(HccErrorCode.NOT_FOUND, "That obligation period is not on the calendar.");
        return (o, p, today);
    }
}

internal static class ComplianceWire
{
    public static string Utc(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Reads (Owner + Accountant). The badge never throws: no access = 0 (prototype <c>cmpBadge</c>).</summary>
public sealed class ComplianceQueries(IAccessPolicy policy, IComplianceReadStore reads)
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static SettingsDto Settings(SettingsRecord s) => new(s.CaName, s.CaEmail, s.DueSoonDays, VersionToken.ToWire(s.Version));

    private static CalendarItemDto Item(CalendarEntry i) => new(i.Obligation.ObligationId, i.Obligation.Kind, i.Obligation.Name, i.Obligation.Form, i.Obligation.Owner,
        i.Row.Period, i.Row.PeriodLabel, D(i.Row.Due), i.Row.Days, i.Row.Status, ComplianceRules.StatusText(i.Row.Status, i.Row.Days),
        i.Filing is null ? null : new FilingDto(i.Filing.Status, i.Filing.By, ComplianceWire.Utc(i.Filing.AtUtc), i.Filing.Reference, i.Filing.Reason));

    public async Task<CalendarView> GetCalendarAsync(int propertyId, int principalId, bool includeInactive, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var today = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var snap = await reads.SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        var items = ComplianceCalendar.Build(snap, today, includeInactive);
        var c = ComplianceRules.Count(items.Select(i => i.Row.Status));
        return new CalendarView(D(today), new CalendarCountsDto(c.Overdue, c.Due, c.Upcoming, c.Filed, c.NotApplicable, c.Open), items.Select(Item).ToList(), Settings(snap.Settings), ComplianceRules.Illustrative);
    }

    public async Task<IReadOnlyList<CalendarItemDto>> GetDueThisWeekAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var today = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        return ComplianceCalendar.DueThisWeek(await reads.SnapshotAsync(propertyId, ct).ConfigureAwait(false), today).Select(Item).ToList();
    }

    public async Task<BadgeResult> GetBadgeAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        if (!await policy.HasAsync(propertyId, principalId, CompliancePermissions.Use, null, ct).ConfigureAwait(false)) return new BadgeResult(0);
        var today = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        return new BadgeResult(ComplianceCalendar.DueThisWeek(await reads.SnapshotAsync(propertyId, ct).ConfigureAwait(false), today).Count);
    }

    public async Task<IReadOnlyList<ObligationDto>> GetObligationsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        var snap = await reads.SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        return snap.Obligations.Select(o => new ObligationDto(o.ObligationId, o.Kind, o.Name, o.Form, o.Frequency, o.Owner, o.FromPeriod, o.Active, o.Note, VersionToken.ToWire(o.Version),
            o.Rules.OrderBy(r => r.From).Select(r => new DueRuleView(D(r.From),
                r.Rule.Type switch { DueRuleType.MonthEnd => "month-end", DueRuleType.Fixed => "fixed", _ => "day" }, ComplianceRules.RuleText(r.Rule),
                r.Rule.Day == 0 ? null : r.Rule.Day, r.Rule.Month == 0 ? null : r.Rule.Month, r.Rule.Special?.Month, r.Rule.Special?.Day)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<CaPackDto>> GetCaPacksAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, CompliancePermissions.Use, null, null, ct).ConfigureAwait(false);
        return (await reads.PacksAsync(propertyId, ct).ConfigureAwait(false))
            .Select(p => new CaPackDto(p.PackId, ComplianceWire.Utc(p.AtUtc), p.By, p.To, p.ToName, p.Overdue, p.Due, p.Filed, p.NotApplicable, p.Lines)).ToList();
    }
}
