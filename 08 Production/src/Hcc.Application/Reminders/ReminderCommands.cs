using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Reminders;
using Hcc.Domain.Errors;
using Hcc.Domain.Reminders;

namespace Hcc.Application.Reminders;

/// <summary>Desk reminders (LF B4): SaveReminder, MarkReminderDone, DismissReminder. PERM-RMD-001; a non-Owner / Manager may address a reminder only to everyone or to their own role.</summary>
public sealed class ReminderCommands(ICommandRunner runner, IAccessPolicy policy, IReminderStore store, TimeProvider clock)
{
    public const string OpSave = "SaveReminder";
    public const string OpDone = "MarkReminderDone";
    public const string OpDismiss = "DismissReminder";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<ReminderView>> SaveAsync(CommandContext ctx, SaveReminderRequest req, int defaultLeadDays = ReminderRules.DefaultLeadDays, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ReminderPermissions.Manage, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpSave }, req, async (tx, c) =>
        {
            var known = await store.KnownRolesAsync(tx, c).ConfigureAwait(false);
            var mine = await store.RolesOfUserAsync(tx, ctx.PrincipalId, c).ConfigureAwait(false);
            var v = ReminderRules.Validate(req.Subject, req.Date, req.Time, req.LeadDays, req.Role, req.Note, req.Repeat, defaultLeadDays, known);
            var boss = mine.Contains("OWN") || mine.Contains("MGR");
            if (!boss && v.Role != ReminderRules.RoleAll && !mine.Contains(v.Role)) throw HccException.Validation("You can address a reminder to everyone or to your own role only.");
            if (req.ReminderId is { } id)
            {
                var old = await store.LockAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reminder not found.");
                if (old.Status != ReminderRules.StatusOpen) throw new HccException(HccErrorCode.INVALID_TRANSITION, "A finished reminder cannot be edited.");
                if (!boss && old.CreatedBy != ctx.PrincipalId) throw new HccException(HccErrorCode.FORBIDDEN, "Only the author, a Manager or the Owner can edit this reminder.");
                var upd = old with { Subject = v.Subject, Date = v.Date, Time = v.Time, LeadDays = v.Lead, Role = v.Role, Note = v.Note, Repeat = v.Repeat, NextDate = null };
                await store.UpdateAsync(tx, ctx.PropertyId, upd, c).ConfigureAwait(false);
                await tx.AuditAsync("REMINDER_SAVE", "reminder-" + id.ToString(CultureInfo.InvariantCulture), $"updated · {v.Subject}", c).ConfigureAwait(false);
                return View(upd);
            }

            var rec = new ReminderRec(0, v.Subject, v.Date, v.Time, v.Lead, v.Role, v.Note, v.Repeat, ReminderRules.StatusOpen, null, ctx.PrincipalId, UtcNow, null, null);
            var nid = await store.InsertAsync(tx, ctx.PropertyId, rec, c).ConfigureAwait(false);
            await tx.AuditAsync("REMINDER_SAVE", "reminder-" + nid.ToString(CultureInfo.InvariantCulture), $"set for {v.Date:yyyy-MM-dd} · {v.Subject}", c).ConfigureAwait(false);
            return View(rec with { ReminderId = nid });
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ReminderView>> DoneAsync(CommandContext ctx, ReminderIdRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ReminderPermissions.Manage, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpDone }, req, async (tx, c) =>
        {
            var r = await Open(tx, ctx, req.ReminderId, c).ConfigureAwait(false);
            var next = r.Repeat == "none" ? (DateOnly?)null : ReminderRules.Roll(ReminderRules.DueDate(r.Date, r.NextDate), r.Repeat);
            var upd = r with { DoneBy = ctx.PrincipalId, DoneUtc = UtcNow, NextDate = next ?? r.NextDate, Status = next is null ? ReminderRules.StatusDone : ReminderRules.StatusOpen };
            await store.UpdateAsync(tx, ctx.PropertyId, upd, c).ConfigureAwait(false);
            await tx.AuditAsync("REMINDER_DONE", "reminder-" + r.ReminderId.ToString(CultureInfo.InvariantCulture), next is null ? $"done · {r.Subject}" : $"done · next {next:yyyy-MM-dd} · {r.Subject}", c).ConfigureAwait(false);
            return View(upd);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ReminderView>> DismissAsync(CommandContext ctx, DismissReminderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ReminderPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var reason = ReminderRules.CleanReason(req.Reason);
        return await runner.RunAsync(ctx with { Operation = OpDismiss }, req, async (tx, c) =>
        {
            var r = await Open(tx, ctx, req.ReminderId, c).ConfigureAwait(false);
            var note = ((r.Note is null ? string.Empty : r.Note + " · ") + "Dismissed: " + reason);
            var upd = r with { Status = ReminderRules.StatusDismissed, DoneBy = ctx.PrincipalId, DoneUtc = UtcNow, Note = note.Length > 200 ? note[..200] : note };
            await store.UpdateAsync(tx, ctx.PropertyId, upd, c).ConfigureAwait(false);
            await tx.AuditAsync("REMINDER_DISMISS", "reminder-" + r.ReminderId.ToString(CultureInfo.InvariantCulture), $"dismissed · {reason}", c).ConfigureAwait(false);
            return View(upd);
        }, ct).ConfigureAwait(false);
    }

    private async Task<ReminderRec> Open(ICommandTransaction tx, CommandContext ctx, long id, CancellationToken ct)
    {
        var r = await store.LockAsync(tx, ctx.PropertyId, id, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reminder not found.");
        if (r.Status != ReminderRules.StatusOpen) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This reminder is already finished.");
        var mine = await store.RolesOfUserAsync(tx, ctx.PrincipalId, ct).ConfigureAwait(false);
        if (!ReminderRules.MayAct(ctx.PrincipalId, r.CreatedBy, r.Role, mine)) throw new HccException(HccErrorCode.FORBIDDEN, "This reminder is not addressed to you.");
        return r;
    }

    public static ReminderView View(ReminderRec r)
    {
        var due = ReminderRules.DueDate(r.Date, r.NextDate);
        return new ReminderView(r.ReminderId, r.Subject, Day(r.Date), r.Time, r.LeadDays, r.Role, r.Note, r.Repeat, r.Status, r.NextDate is { } n ? Day(n) : null, Day(due),
            Day(ReminderRules.ShowFrom(r.Date, r.NextDate, r.LeadDays)), r.CreatedBy, Wire(r.CreatedUtc), r.DoneBy, r.DoneUtc is { } d ? Wire(d) : null);
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Wire(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Reads: GET /reminders?bucket=today|upcoming|done for the signed-in user.</summary>
public sealed class ReminderQueries(IAccessPolicy policy, IReminderStore store)
{
    public async Task<ReminderList> ListAsync(int propertyId, int principalId, string bucket, DateOnly businessDate, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, ReminderPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var b = bucket is "today" or "upcoming" or "done" ? bucket : throw HccException.Validation("bucket: today, upcoming or done.");
        var all = await store.ListAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var items = b == "done"
            ? all.Where(r => r.Status != ReminderRules.StatusOpen).OrderByDescending(r => r.DoneUtc ?? r.CreatedUtc)
            : all.Where(r => r.Status == ReminderRules.StatusOpen && ReminderRules.IsDue(r.Status, r.Date, r.NextDate, r.LeadDays, businessDate) == (b == "today"))
                .OrderBy(r => ReminderRules.DueDate(r.Date, r.NextDate)).ThenBy(r => r.Time);
        return new ReminderList(b, businessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), items.Select(ReminderCommands.View).ToList());
    }
}
