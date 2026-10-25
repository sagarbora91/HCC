using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Handover;
using Hcc.Domain.Errors;
using Hcc.Domain.Handover;

namespace Hcc.Application.Handover;

/// <summary>
/// Shift handover use cases (prototype HOV_* handlers, rules R1–R10): SaveHandoverTemplate / ToggleHandoverTemplate (approve),
/// TickChecklistItem, SubmitChecklistRun, ReopenChecklistRun (approve), SubmitHandoverNote, AcknowledgeHandoverNote, CompleteCarryItem.
/// The demo clock command of the prototype (HOV_CLOCK) is not ported: production uses the real property clock (IST).
/// </summary>
public sealed class HandoverCommands(ICommandRunner runner, IAccessPolicy policy, IHandoverStore store, IHandoverPrincipals principals, TimeProvider clock)
{
    public const string OpSaveTemplate = "SaveHandoverTemplate";
    public const string OpToggleTemplate = "ToggleHandoverTemplate";
    public const string OpTick = "TickChecklistItem";
    public const string OpSubmitRun = "SubmitChecklistRun";
    public const string OpReopenRun = "ReopenChecklistRun";
    public const string OpSubmitNote = "SubmitHandoverNote";
    public const string OpAckNote = "AcknowledgeHandoverNote";
    public const string OpCarryDone = "CompleteCarryItem";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private DateTime NowLocal => HandoverRules.ToLocal(UtcNow);

    /// <summary>The caller's access: module access (any of the three coarse permissions) or FORBIDDEN; manager = approve.</summary>
    internal async Task<Caller> RequireAccessAsync(CommandContext ctx, CancellationToken ct)
    {
        var has = false;
        foreach (var perm in HandoverPermissions.AnyAccess)
            if (await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, perm, AccessScopes.Any, ct).ConfigureAwait(false)) { has = true; break; }
        if (!has) throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: checklists need the housekeeping, payments or approve permission.");
        var manager = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, HandoverPermissions.Approve, AccessScopes.Any, ct).ConfigureAwait(false);
        var roles = await principals.RoleCodesAsync(ctx.PropertyId, ctx.PrincipalId, UtcNow, ct).ConfigureAwait(false);
        var cash = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, HandoverPermissions.Payments, AccessScopes.Any, ct).ConfigureAwait(false)
            || await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, HandoverPermissions.FinanceReport, AccessScopes.Any, ct).ConfigureAwait(false);
        return new Caller(roles, manager, cash);
    }

    internal sealed record Caller(IReadOnlyList<string> Roles, bool Manager, bool SeesCash);

    // ── Templates ──
    public async Task<CommandResult<HandoverTemplateView>> SaveTemplateAsync(CommandContext ctx, SaveHandoverTemplateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HandoverPermissions.Approve, null, null, ct).ConfigureAwait(false);
        var clean = HandoverRules.ValidateTemplate(req.Name, req.Desk, req.Shift, req.Roles,
            req.Items?.Select(i => ((string?)i?.Text, i?.Mandatory ?? false, i?.Due, i?.Photo ?? false, i?.Link)).ToList());
        byte[]? expected = req.TemplateId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveTemplate }, req, async (tx, c) =>
        {
            HandoverTemplateView view;
            if (req.TemplateId is { } id)
            {
                var t = await store.LockTemplateAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Checklist template not found.");
                VersionToken.EnsureMatches(t.RowVer, expected!, "This checklist template");
                if (await store.TemplateNameTakenAsync(tx, ctx.PropertyId, clean.Name, id, c).ConfigureAwait(false)) throw HccException.Validation("A template with this name already exists.");
                await store.UpdateTemplateAsync(tx, id, expected!, clean, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await tx.AuditAsync("HOV_TEMPLATE", Target("hov-template", id), $"{clean.Name} · v{t.Version + 1} · existing runs keep their snapshot", c).ConfigureAwait(false);
                view = HandoverViews.Template((await store.LockTemplateAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false))!);
            }
            else
            {
                if (await store.TemplateNameTakenAsync(tx, ctx.PropertyId, clean.Name, null, c).ConfigureAwait(false)) throw HccException.Validation("A template with this name already exists.");
                var id2 = await store.InsertTemplateAsync(tx, ctx.PropertyId, clean, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await tx.AuditAsync("HOV_TEMPLATE", Target("hov-template", id2), $"{clean.Name} created ({clean.Items.Count} items)", c).ConfigureAwait(false);
                view = HandoverViews.Template((await store.LockTemplateAsync(tx, ctx.PropertyId, id2, c).ConfigureAwait(false))!);
            }

            return view;
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HandoverTemplateView>> ToggleTemplateAsync(CommandContext ctx, ToggleHandoverTemplateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HandoverPermissions.Approve, null, null, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpToggleTemplate }, req, async (tx, c) =>
        {
            var t = await store.LockTemplateAsync(tx, ctx.PropertyId, req.TemplateId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Checklist template not found.");
            VersionToken.EnsureMatches(t.RowVer, expected, "This checklist template");
            await store.SetTemplateActiveAsync(tx, t.TemplateId, expected, !t.Active, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("HOV_TEMPLATE", Target("hov-template", t.TemplateId), $"{t.Name} · {(t.Active ? "deactivated" : "activated")}", c).ConfigureAwait(false);
            return HandoverViews.Template((await store.LockTemplateAsync(tx, ctx.PropertyId, t.TemplateId, c).ConfigureAwait(false))!);
        }, ct).ConfigureAwait(false);
    }

    // ── Runs ──
    public async Task<CommandResult<ChecklistRunView>> TickAsync(CommandContext ctx, TickChecklistItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var who = await RequireAccessAsync(ctx, ct).ConfigureAwait(false);
        var note = req.Note is null ? null : req.Note.Trim();
        if (note is not null && note.Length > HandoverRules.NoteMax) throw HccException.Validation($"Notes are limited to {HandoverRules.NoteMax} characters.");
        var photo = req.PhotoName is null ? null : req.PhotoName.Trim();
        if (!string.IsNullOrEmpty(photo) && !HandoverRules.IsValidPhotoName(photo))
            throw HccException.Validation("Photo name must be a file name ending .jpg, .png or .heic (simulated — no upload).");
        return await runner.RunAsync(ctx with { Operation = OpTick }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var t = await store.LockTemplateAsync(tx, ctx.PropertyId, req.TemplateId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Checklist template not found.");
            var run = await store.LockRunAsync(tx, ctx.PropertyId, t.TemplateId, date, c).ConfigureAwait(false);
            if (!HandoverRules.CanUseTemplate(who.Roles, who.Manager, run?.Roles ?? t.Roles)) throw new HccException(HccErrorCode.FORBIDDEN, "This checklist belongs to another role.");
            if (run is null)
            {
                if (!t.Active) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This checklist template is inactive.");
                run = await store.CreateRunAsync(tx, ctx.PropertyId, t, date, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            }

            if (!run.Open) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This checklist was submitted. A manager can reopen it.");
            var item = run.Items.FirstOrDefault(i => i.Key == req.Item) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Checklist item not found.");
            var updated = item;
            if (note is not null) updated = updated with { Note = note.Length == 0 ? null : note };
            if (photo is not null) updated = updated with { PhotoName = photo.Length == 0 ? null : photo };
            if (req.Done is { } done)
            {
                if (done && updated.Def.Photo && updated.PhotoName is null) throw HccException.Validation("Add a photo file name for this item first (simulated photo).");
                updated = done ? updated with { Done = true, DoneBy = ctx.PrincipalId, DoneUtc = UtcNow } : updated with { Done = false, DoneBy = null, DoneUtc = null };
            }

            await store.UpdateItemAsync(tx, run.RunId!.Value, updated, c).ConfigureAwait(false);
            await tx.AuditAsync("HOV_ITEM", Target("hov-run", run.RunId.Value), $"{run.Name} · {HandoverViews.Date(run.Date)} · {item.Def.Text}{(req.Done is null ? " · note/photo updated" : updated.Done ? " · done" : " · unticked")}", c).ConfigureAwait(false);
            var fresh = (await store.LockRunAsync(tx, ctx.PropertyId, t.TemplateId, date, c).ConfigureAwait(false))!;
            return HandoverViews.Run(fresh, NowLocal);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ChecklistRunView>> SubmitRunAsync(CommandContext ctx, SubmitChecklistRunRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var who = await RequireAccessAsync(ctx, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpSubmitRun }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var t = await store.LockTemplateAsync(tx, ctx.PropertyId, req.TemplateId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Checklist template not found.");
            var run = await store.LockRunAsync(tx, ctx.PropertyId, t.TemplateId, date, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "Tick at least the mandatory items before submitting.");
            if (!HandoverRules.CanUseTemplate(who.Roles, who.Manager, run.Roles)) throw new HccException(HccErrorCode.FORBIDDEN, "This checklist belongs to another role.");
            if (!run.Open) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This checklist is already submitted.");
            var missing = run.Items.Where(i => i.Def.Mandatory && !i.Done).ToList();
            if (missing.Count > 0)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{missing.Count} mandatory item{(missing.Count == 1 ? " is" : "s are")} not done: {string.Join("; ", missing.Take(3).Select(i => i.Def.Text))}.");
            var incoming = await IncomingFor(tx, ctx, who, run.Desk, c).ConfigureAwait(false);
            if (incoming.Count > 0)
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"Acknowledge the incoming {run.Desk} handover note first ({HandoverRules.NoteRef(incoming[0].Desk, incoming[0].Date, incoming[0].Shift)}).");
            var late = HandoverRules.ShiftEnd(run.Date, run.Shift) < NowLocal;
            await store.SubmitRunAsync(tx, run.RunId!.Value, ctx.PrincipalId, UtcNow, late, c).ConfigureAwait(false);
            var done = run.Items.Count(i => i.Done);
            await tx.AuditAsync("HOV_RUN", Target("hov-run", run.RunId.Value), $"{run.Name} · {HandoverViews.Date(run.Date)} submitted · {done}/{run.Items.Count} items{(late ? " · late" : string.Empty)}", c).ConfigureAwait(false);
            await tx.PublishAsync("ChecklistRunSubmitted", run.RunId.Value, new { runId = run.RunId.Value, templateId = t.TemplateId, desk = run.Desk, shift = run.Shift, date = HandoverViews.Date(run.Date), late }, c).ConfigureAwait(false);
            return HandoverViews.Run((await store.LockRunAsync(tx, ctx.PropertyId, t.TemplateId, date, c).ConfigureAwait(false))!, NowLocal);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ChecklistRunView>> ReopenRunAsync(CommandContext ctx, ReopenChecklistRunRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HandoverPermissions.Approve, null, null, ct).ConfigureAwait(false);
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 3) throw HccException.Validation("A reason is required to reopen a checklist.");
        if (reason.Length > 200) throw HccException.Validation("Reason: maximum 200 characters.");
        return await runner.RunAsync(ctx with { Operation = OpReopenRun }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var today = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var date = req.Date is null ? today : DateOnly.TryParseExact(req.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation("Date must be yyyy-MM-dd.");
            var run = await store.LockRunAsync(tx, ctx.PropertyId, req.TemplateId, date, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Checklist run not found.");
            if (run.Open) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a submitted checklist can be reopened.");
            await store.ReopenRunAsync(tx, run.RunId!.Value, reason, c).ConfigureAwait(false);
            await tx.AuditAsync("HOV_RUN", Target("hov-run", run.RunId.Value), $"{run.Name} · {HandoverViews.Date(run.Date)} reopened · {reason}", c).ConfigureAwait(false);
            return HandoverViews.Run((await store.LockRunAsync(tx, ctx.PropertyId, req.TemplateId, date, c).ConfigureAwait(false))!, NowLocal);
        }, ct).ConfigureAwait(false);
    }

    // ── Notes ──
    public async Task<CommandResult<HandoverNoteView>> SubmitNoteAsync(CommandContext ctx, SubmitHandoverNoteRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var who = await RequireAccessAsync(ctx, ct).ConfigureAwait(false);
        if (req.Desk is null || !HandoverRules.Desks.Contains(req.Desk)) throw HccException.Validation("Choose a desk.");
        if (!HandoverRules.InDesk(who.Roles, who.Manager, req.Desk)) throw new HccException(HccErrorCode.FORBIDDEN, $"Only {req.Desk} staff can write this handover note.");
        var shift = req.Shift ?? HandoverRules.ShiftAt(NowLocal);
        if (!HandoverRules.Shifts.Contains(shift)) throw HccException.Validation("Choose a shift.");
        string issues = (req.Issues ?? string.Empty).Trim(), keys = (req.Keys ?? string.Empty).Trim(), guests = (req.Guests ?? string.Empty).Trim();
        if (!req.Nothing && issues.Length < 3) throw HccException.Validation("Describe the open issues, or tick “Nothing to report”.");
        foreach (var (label, v) in new[] { ("Open issues", issues), ("Keys", keys), ("Guest notes", guests) })
            if (v.Length > HandoverRules.TextMax) throw HccException.Validation($"{label}: maximum {HandoverRules.TextMax} characters.");
        long? cash = null;
        string? cashNote = null;
        if (req.Cash is { } cin)
        {
            if (req.Desk != HandoverRules.FrontDesk) throw HccException.Validation("Cash lines belong only on the Front desk handover.");
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HandoverPermissions.Payments, AccessScopes.Any, null, ct).ConfigureAwait(false);
            if (cin.AmountPaise is < 0 or > HandoverRules.MaxCashPaise) throw HccException.Validation("Enter the cash float handed over (₹0 – ₹10,00,000).");
            cash = cin.AmountPaise;
            cashNote = (cin.Note ?? string.Empty).Trim();
            if (cashNote.Length > HandoverRules.NoteMax) cashNote = cashNote[..HandoverRules.NoteMax];
        }

        var carry = (req.Carry ?? []).Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0).ToList();
        if (carry.Count > HandoverRules.MaxCarry) throw HccException.Validation($"Up to {HandoverRules.MaxCarry} carry-forward items.");
        if (carry.Any(x => x.Length is < 3 or > HandoverRules.ItemTextMax)) throw HccException.Validation("Each carry-forward item must be 3–120 characters.");

        return await runner.RunAsync(ctx with { Operation = OpSubmitNote }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var write = new HovNoteWrite(req.Desk, date, shift, HandoverRules.NextShift(shift), issues, req.Nothing, keys, guests, cash, cashNote, carry);
            var old = await store.LockNoteByKeyAsync(tx, ctx.PropertyId, req.Desk, date, shift, c).ConfigureAwait(false);
            long id;
            if (old is not null)
            {
                if (old.Status != "Submitted") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This handover note was already acknowledged by the next shift.");
                if (old.AuthorId != ctx.PrincipalId && !who.Manager) throw new HccException(HccErrorCode.FORBIDDEN, "Only the author or a manager can replace this note.");
                await store.ReplaceNoteAsync(tx, old.NoteId, write, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                id = old.NoteId;
            }
            else
            {
                id = await store.InsertNoteAsync(tx, ctx.PropertyId, write, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            }

            // The audit detail never carries the cash amount.
            await tx.AuditAsync("HOV_NOTE", Target("hov-note", id), $"{HandoverRules.NoteRef(req.Desk, date, shift)} · {req.Desk} {shift} → {write.ToShift}{(cash is null ? string.Empty : " · float recorded")} · {carry.Count} carry-forward{(old is null ? string.Empty : " · replaced")}", c).ConfigureAwait(false);
            await tx.PublishAsync("HandoverNoteSubmitted", id, new { noteId = id, desk = req.Desk, date = HandoverViews.Date(date), shift, toShift = write.ToShift }, c).ConfigureAwait(false);
            return HandoverViews.Note((await store.LockNoteAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false))!, who.SeesCash);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HandoverNoteView>> AcknowledgeNoteAsync(CommandContext ctx, AcknowledgeHandoverNoteRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var who = await RequireAccessAsync(ctx, ct).ConfigureAwait(false);
        var remark = (req.Remark ?? string.Empty).Trim();
        if (remark.Length > HandoverRules.NoteMax) throw HccException.Validation($"Remark: maximum {HandoverRules.NoteMax} characters.");
        return await runner.RunAsync(ctx with { Operation = OpAckNote }, req, async (tx, c) =>
        {
            var n = await store.LockNoteAsync(tx, ctx.PropertyId, req.NoteId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Handover note not found.");
            if (!HandoverRules.InDesk(who.Roles, who.Manager, n.Desk)) throw new HccException(HccErrorCode.FORBIDDEN, $"Only {n.Desk} staff can acknowledge this note.");
            if (n.Status != "Submitted") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This note is already acknowledged.");
            if (n.AuthorId == ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot acknowledge your own handover note. The next shift must acknowledge it.");
            await store.AcknowledgeNoteAsync(tx, n.NoteId, ctx.PrincipalId, UtcNow, remark.Length == 0 ? null : remark, c).ConfigureAwait(false);
            await tx.AuditAsync("HOV_NOTE", Target("hov-note", n.NoteId), $"{HandoverRules.NoteRef(n.Desk, n.Date, n.Shift)} · acknowledged · {n.Carry.Count(x => !x.Done)} carry-forward open", c).ConfigureAwait(false);
            await tx.PublishAsync("HandoverNoteAcknowledged", n.NoteId, new { noteId = n.NoteId, desk = n.Desk }, c).ConfigureAwait(false);
            return HandoverViews.Note((await store.LockNoteAsync(tx, ctx.PropertyId, n.NoteId, c).ConfigureAwait(false))!, who.SeesCash);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HandoverNoteView>> CompleteCarryAsync(CommandContext ctx, CompleteCarryItemRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var who = await RequireAccessAsync(ctx, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpCarryDone }, req, async (tx, c) =>
        {
            var n = await store.LockNoteAsync(tx, ctx.PropertyId, req.NoteId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Handover note not found.");
            if (!HandoverRules.InDesk(who.Roles, who.Manager, n.Desk)) throw new HccException(HccErrorCode.FORBIDDEN, $"Only {n.Desk} staff can close this item.");
            var item = n.Carry.FirstOrDefault(x => x.CarryId == req.CarryId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Carry-forward item not found.");
            if (item.Done) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This item is already done.");
            await store.CompleteCarryAsync(tx, item.CarryId, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("HOV_CARRY", Target("hov-note", n.NoteId), $"{n.Desk} carry-forward done · {item.Text}", c).ConfigureAwait(false);
            return HandoverViews.Note((await store.LockNoteAsync(tx, ctx.PropertyId, n.NoteId, c).ConfigureAwait(false))!, who.SeesCash);
        }, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<HovNoteRec>> IncomingFor(ICommandTransaction tx, CommandContext ctx, Caller who, string desk, CancellationToken ct)
    {
        if (!HandoverRules.InDesk(who.Roles, who.Manager, desk)) return [];
        return await store.IncomingAsync(tx, ctx.PropertyId, ctx.PrincipalId, desk, ct).ConfigureAwait(false);
    }

    private static string Target(string kind, long id) => kind + ":" + id.ToString(CultureInfo.InvariantCulture);
}
