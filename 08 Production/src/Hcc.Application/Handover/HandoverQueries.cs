using System.Globalization;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Handover;
using Hcc.Domain.Errors;
using Hcc.Domain.Handover;

namespace Hcc.Application.Handover;

/// <summary>Read side: my checklists (current shift first), notes by desk with the incoming list, open carry-forward, manager dashboard and template list.</summary>
public sealed class HandoverQueries(IAccessPolicy policy, IHandoverReadStore reads, IHandoverPrincipals principals, TimeProvider clock)
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private async Task<(IReadOnlyList<string> Roles, bool Manager, bool SeesCash)> CallerAsync(int propertyId, int principalId, CancellationToken ct)
    {
        var has = false;
        foreach (var perm in HandoverPermissions.AnyAccess)
            if (await policy.HasAsync(propertyId, principalId, perm, AccessScopes.Any, ct).ConfigureAwait(false)) { has = true; break; }
        if (!has) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: checklists need the housekeeping, payments or approve permission.");
        var manager = await policy.HasAsync(propertyId, principalId, HandoverPermissions.Approve, AccessScopes.Any, ct).ConfigureAwait(false);
        var cash = await policy.HasAsync(propertyId, principalId, HandoverPermissions.Payments, AccessScopes.Any, ct).ConfigureAwait(false)
            || await policy.HasAsync(propertyId, principalId, HandoverPermissions.FinanceReport, AccessScopes.Any, ct).ConfigureAwait(false);
        return (await principals.RoleCodesAsync(propertyId, principalId, UtcNow, ct).ConfigureAwait(false), manager, cash);
    }

    private async Task<(IReadOnlyList<string> Roles, bool Manager, bool SeesCash)> ManagerAsync(int propertyId, int principalId, CancellationToken ct)
    {
        await policy.RequireAsync(propertyId, principalId, HandoverPermissions.Approve, null, 0L, ct).ConfigureAwait(false);
        return await CallerAsync(propertyId, principalId, ct).ConfigureAwait(false);
    }

    /// <summary>Saved runs of the date plus every active template without a run (as an unstarted card).</summary>
    private async Task<List<HovRun>> AllRunsAsync(int propertyId, DateOnly date, CancellationToken ct)
    {
        var templates = await reads.TemplatesAsync(propertyId, ct).ConfigureAwait(false);
        var runs = await reads.RunsAsync(propertyId, date, ct).ConfigureAwait(false);
        var list = new List<HovRun>();
        foreach (var t in templates)
        {
            var r = runs.FirstOrDefault(x => x.TemplateId == t.TemplateId);
            if (r is not null) list.Add(r);
            else if (t.Active) list.Add(new HovRun(null, t.TemplateId, t.Version, t.Name, t.Desk, t.Shift, t.Roles, date, true, false, null, null, null, null, null,
                t.Items.Select(i => new HovItemState(i, false, null, null, null, null)).ToList()));
        }

        return list;
    }

    public async Task<ChecklistList> MyChecklistsAsync(int propertyId, int principalId, string? date, CancellationToken ct = default)
    {
        var who = await CallerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var d = await DateAsync(propertyId, date, ct).ConfigureAwait(false);
        var nowLocal = HandoverRules.ToLocal(UtcNow);
        var mine = (await AllRunsAsync(propertyId, d, ct).ConfigureAwait(false)).Where(r => HandoverRules.CanUseTemplate(who.Roles, who.Manager, r.Roles)).ToList();
        var cur = HandoverRules.ShiftAt(nowLocal);
        return new ChecklistList(HandoverViews.Date(d), cur, HandoverRules.Order(mine, cur).Select(r => HandoverViews.Run(r, nowLocal)).ToList());
    }

    public async Task<HandoverNoteList> NotesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var who = await CallerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var notes = (await reads.NotesAsync(propertyId, bd, ct).ConfigureAwait(false)).Where(n => HandoverRules.InDesk(who.Roles, who.Manager, n.Desk)).ToList();
        var incoming = notes.Where(n => n.Status == "Submitted" && n.AuthorId != principalId).Select(n => n.NoteId).ToList();
        return new HandoverNoteList(notes.Select(n => HandoverViews.Note(n, who.SeesCash)).ToList(), incoming);
    }

    public async Task<OpenCarryList> OpenCarryAsync(int propertyId, int principalId, string? desk, CancellationToken ct = default)
    {
        var who = await CallerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (desk is not null && !HandoverRules.Desks.Contains(desk)) throw HccException.Validation("Choose a desk.");
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var items = (await reads.NotesAsync(propertyId, bd, ct).ConfigureAwait(false))
            .Where(n => (desk is null || n.Desk == desk) && HandoverRules.InDesk(who.Roles, who.Manager, n.Desk))
            .SelectMany(n => n.Carry.Where(c => !c.Done).Select(c => new OpenCarryView(c.CarryId, n.NoteId, c.Text, n.Desk, n.AuthorName, HandoverViews.Date(n.Date)))).ToList();
        return new OpenCarryList(items);
    }

    public async Task<HandoverTemplateList> TemplatesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await ManagerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        return new HandoverTemplateList((await reads.TemplatesAsync(propertyId, ct).ConfigureAwait(false)).Select(HandoverViews.Template).ToList());
    }

    public async Task<HandoverDashboardView> DashboardAsync(int propertyId, int principalId, string? date, string? by, CancellationToken ct = default)
    {
        await ManagerAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var mode = by ?? "desk";
        if (mode is not ("role" or "desk")) throw HccException.Validation("by must be role or desk.");
        var d = await DateAsync(propertyId, date, ct).ConfigureAwait(false);
        var nowLocal = HandoverRules.ToLocal(UtcNow);
        var runs = await AllRunsAsync(propertyId, d, ct).ConfigureAwait(false);
        var rows = HandoverRules.Dashboard(runs, mode, nowLocal).Select(r => new HandoverDashboardRow(r.Key, r.Key, r.Runs, r.Submitted, r.Items, r.Done, r.Pct, r.Overdue, r.Late, r.NotStarted)).ToList();
        var overdue = runs.Where(r => r.Started).SelectMany(r => r.Items.Where(i => HandoverRules.ItemOverdue(r, i, nowLocal))
            .Select(i => new HandoverOverdueRow(r.RunId, r.TemplateId, r.Name, r.Desk, r.Shift, i.Key, i.Def.Text, i.Def.Due))).ToList();
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var unack = (await reads.NotesAsync(propertyId, bd, ct).ConfigureAwait(false)).Count(n => n.Status == "Submitted");
        return new HandoverDashboardView(HandoverViews.Date(d), mode, rows, overdue, unack);
    }

    private async Task<DateOnly> DateAsync(int propertyId, string? date, CancellationToken ct)
    {
        if (date is null) return await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation("Date must be yyyy-MM-dd.");
    }
}
