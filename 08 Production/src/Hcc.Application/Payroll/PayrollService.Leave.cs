using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

// SCC LeaveDesk in full (owner rule DEC-116; DEC-119 keeps HCC's safeguards) — prototype 03 Prototype/src/services/mock/handlers/staff.ts
// (HR_LEAVE_* / HR_AGENDA_*) with the same texts. One leave record per DATE (migration 4110); SCC getCapacity / canAddLeave under the property's
// payroll anchor lock (3 records a date for the whole property, 4 a shift); balance and blackout warnings refuse only until confirmed (SCC confirm);
// a hard blackout refuses. Kept from HCC: own leave or PERM-HR-002 for anyone, PERM-HR-003 decides, nobody decides their own leave or request,
// removing = cancel with an audit record, settings / import / clear Owner-only (PERM-HR-009, DEC-058), no leave before the business date (DEC-075).
public sealed partial class PayrollService
{
    public const string OpSetLeaveEntOverride = "SetLeaveEntOverride";
    public const string OpAddLeaveBlackout = "AddLeaveBlackout";
    public const string OpRemoveLeaveBlackout = "RemoveLeaveBlackout";
    public const string OpAddLeaveAgenda = "AddLeaveAgenda";
    public const string OpRemoveLeaveAgenda = "RemoveLeaveAgenda";
    public const string OpImportLeave = "ImportLeave";
    public const string OpClearLeave = "ClearLeave";

    internal static LeaveResult ToResult(LeaveRow l, string? version = null) => new(l.Span.LeaveId, l.Span.StaffId, l.Span.Type.ToString(), l.Span.From, l.Span.To,
        l.Span.Half?.ToString(), l.Span.Days, l.Span.Status.ToString(), version ?? l.Version);

    private static LeaveHalf? ParseHalf(string? half) => half switch
    {
        null or "" => null,
        "AM" => LeaveHalf.AM,
        "PM" => LeaveHalf.PM,
        _ => throw HccException.Validation("Invalid leave type"),
    };

    private static LeaveType ParseCategory(string? code)
    {
        var t = LeaveType.IsValid(code) ? new LeaveType(code!) : default;
        return LeaveType.Fixed.Contains(t) ? t : throw HccException.Validation("Choose a leave category.");
    }

    // ── RequestLeave (SCC submitLeave) ──

    public async Task<CommandResult<LeaveRequestResult>> RequestLeaveAsync(CommandContext ctx, RequestLeaveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var manage = await Has(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        if (!manage) await Require(ctx, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        if (req.ApproverId is not { } approverId) throw HccException.Validation("Approver name is required");
        var half = ParseHalf(req.Half);
        var to = req.To ?? req.From;
        if (req.From > to) throw HccException.Validation("Leave From cannot be after Leave To");
        var type = ParseCategory(req.LeaveType);
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length > 500) throw HccException.Validation("Reason / notes: up to 500 characters.");

        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);   // SCC capacity is property-wide: one request at a time
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (!IsOwn(ctx, st) && !manage) throw Forbidden("You can request leave only for yourself.");
            if (!st.Profile.Active) throw Invalid("This staff member is inactive.");
            var approvers = await db.ApproversAsync(ctx.PropertyId, Now, c2).ConfigureAwait(false);
            var approver = approvers.FirstOrDefault(a => a.UserId == approverId) ?? throw HccException.Validation("Choose the approver from the list (the Owner or a Manager).");
            if (st.UserId == approverId) throw HccException.Validation("The approver cannot be the person on leave — nobody approves their own leave (DEC-119).");
            if (req.From < clockInfo.BusinessDate)
                throw HccException.Validation("Leave cannot start before the business date. A past day of the open pay month is corrected on Attendance › Past days (DEC-075).");

            var policy = await db.LeavePolicyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var mine = (await db.DaysAsync(ctx.PropertyId, [st.StaffId], req.From, to, c2).ConfigureAwait(false)).GetValueOrDefault(st.StaffId) ?? new StaffDays();
            var over = SccLeave.OverBalanceText(policy, mine.Leaves, st.StaffId, st.Profile.Name, type, req.From, to, half);
            if (over is not null && !req.ConfirmOverBalance) throw Invalid(over);
            var bk = SccLeave.BlackoutHits(policy, req.From, to, st.Profile.Dept);
            if (bk.Hard) throw new HccException(HccErrorCode.NOT_AVAILABLE, SccLeave.HardBlackoutText(bk.Labels));
            if (bk.Hits.Count > 0 && !req.ConfirmBlackout) throw Invalid(SccLeave.SoftBlackoutText(bk.Labels, type));

            var all = await db.LeavesBetweenAsync(ctx.PropertyId, req.From, to, c2).ConfigureAwait(false);
            var plan = SccLeave.Plan(policy.WeekOffMask, all.Select(l => l.Span).ToList(), st.StaffId, req.From, to, half);
            if (plan.Added == 0) throw new HccException(HccErrorCode.NOT_AVAILABLE, plan.Toast);
            var added = new List<LeaveResult>();
            foreach (var (d, r) in plan.Dates)
            {
                if (r != LeaveDateResult.Added) continue;
                var id = await db.InsertLeaveDayAsync(new NewLeaveDay(ctx.PropertyId, st.StaffId, type, d, half, reason.Length > 0 ? reason : null, approver.UserId, null,
                    req.From, to, LeaveStatus.Pending, ctx.PrincipalId, Now), c2).ConfigureAwait(false);
                var row = (await db.LockLeaveAsync(ctx.PropertyId, id, c2).ConfigureAwait(false))!;
                added.Add(ToResult(row));
            }

            var text = $"{st.Profile.Name} · {DayEntry.LeaveLabel(type)} · {SccLeave.KindLabel(half)} · {Day(req.From)}{(to != req.From ? " – " + Day(to) : "")} · {plan.Toast}"
                + $" · approver {approver.Name}{(over is not null ? " · over the quota (confirmed)" : "")}{(bk.Hits.Count > 0 ? " · blackout " + bk.Labels + " (confirmed)" : "")}";
            await tx.AuditAsync("HR_LEAVE_REQUESTED", Target("hr.leave", added[0].LeaveId), text, c2).ConfigureAwait(false);
            return new LeaveRequestResult(added, plan.Blocked, plan.Duplicates, plan.WeekOff, plan.Toast);
        }, ct).ConfigureAwait(false);
    }

    // ── DecideLeave (SCC decideLeave: one date record) ──

    public async Task<CommandResult<LeaveResult>> DecideLeaveAsync(CommandContext ctx, DecideLeaveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.LeaveDecide, ct: ct).ConfigureAwait(false);
        var note = (req.Note ?? string.Empty).Trim();
        if (note.Length > 200) throw HccException.Validation("Note: up to 200 characters.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var l = await db.LockLeaveAsync(ctx.PropertyId, req.LeaveId, c2).ConfigureAwait(false) ?? throw NotFound("Leave record not found.");
            if (l.Span.Status != LeaveStatus.Pending) throw Invalid("This leave is already " + l.Span.Status.ToString().ToLowerInvariant());
            var st = await RequireStaffAsync(db, ctx, l.Span.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (l.RequestedBy == ctx.PrincipalId || IsOwn(ctx, st)) throw Forbidden("You cannot approve your own request. Another authorised user decides it.");
            if (req.Approve && await db.AnyAttendanceAsync(st.StaffId, l.Span.Date, l.Span.Date, exceptHalfDayMarks: l.Span.Half is not null, c2).ConfigureAwait(false))
                throw Invalid("Attendance is already recorded on this date.");

            var status = req.Approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
            var version = await db.UpdateLeaveStatusAsync(l.Span.LeaveId, req.ExpectedVersion, status, ctx.PrincipalId, Now, note.Length > 0 ? note : null, c2).ConfigureAwait(false);
            await tx.AuditAsync(req.Approve ? "HR_LEAVE_APPROVED" : "HR_LEAVE_REJECTED", Target("hr.leave", l.Span.LeaveId),
                $"{st.Profile.Name} · {Day(l.Span.Date)} · {DayEntry.LeaveLabel(l.Span.Type)} {SccLeave.KindLabel(l.Span.Half)} · "
                + (req.Approve ? "Leave approved — now live for floor gate & payroll" : "Leave rejected — kept for audit") + (note.Length > 0 ? " · " + Clip(note, 200) : ""), c2)
                .ConfigureAwait(false);
            await tx.PublishAsync("LeaveDecided", l.Span.LeaveId, new { leaveId = l.Span.LeaveId, staffId = st.StaffId, status = status.ToString(), from = l.Span.From, to = l.Span.To },
                c2).ConfigureAwait(false);
            return ToResult(l with { Span = l.Span with { Status = status } }, version);
        }, ct).ConfigureAwait(false);
    }

    // ── CancelLeave (SCC removeLeave = cancel with an audit record, DEC-119) ──

    public async Task<CommandResult<LeaveResult>> CancelLeaveAsync(CommandContext ctx, CancelLeaveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var manage = await Has(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        if (!manage) await Require(ctx, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var l = await db.LockLeaveAsync(ctx.PropertyId, req.LeaveId, c2).ConfigureAwait(false) ?? throw NotFound("Leave record not found.");
            if (l.Span.Status == LeaveStatus.Cancelled) throw Invalid("This leave record is already removed (kept for audit).");
            var st = await RequireStaffAsync(db, ctx, l.Span.StaffId, lockRow: false, c2).ConfigureAwait(false);
            var own = l.RequestedBy == ctx.PrincipalId || IsOwn(ctx, st);
            if (l.Span.Status == LeaveStatus.Pending)
            {
                if (!own && !manage) throw Forbidden("Only the requester or a manager can cancel this request.");
            }
            else
            {
                if (!manage) throw Forbidden("Access denied: a manager removes approved or rejected leave.");
                if (l.Span.Status == LeaveStatus.Approved && l.Span.Date < clockInfo.BusinessDate)
                    throw Invalid("An approved leave day before the business date is changed with an attendance correction (Attendance › Past days, DEC-075).");
            }

            var was = l.Span.Status;
            var version = await db.CancelLeaveAsync(l.Span.LeaveId, req.ExpectedVersion, ctx.PrincipalId, Now, null, c2).ConfigureAwait(false);
            await tx.AuditAsync("HR_LEAVE_CANCELLED", Target("hr.leave", l.Span.LeaveId),
                $"{st.Profile.Name} · {Day(l.Span.Date)} · {was} leave removed (cancelled — kept for audit)", c2).ConfigureAwait(false);
            return ToResult(l with { Span = l.Span with { Status = LeaveStatus.Cancelled } }, version);
        }, ct).ConfigureAwait(false);
    }

    // ── Settings (SCC Staff Master; Owner, DEC-058) ──

    private static decimal Days(decimal v, string what)
    {
        if (v < 0 || v > 9_999_999) throw HccException.Validation($"{what}: a number of days, 0 or more.");
        return Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>SCC entitlement defaults (any number ≥ 0, halves allowed, no maximum; a blank field saves 0 — the client sends 0) and the week-off set.</summary>
    public async Task<CommandResult<LeavePolicyResult>> SetLeavePolicyAsync(CommandContext ctx, SetLeavePolicyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        var cl = Days(req.Cl, "Entitlement for CL");
        var sl = Days(req.Sl, "Entitlement for SL");
        var el = Days(req.El, "Entitlement for EL");
        if (req.WeekOffMask is < 0 or > 127) throw HccException.Validation("Week-off days: weekdays Sunday to Saturday.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var old = await db.LeavePolicyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var p = old with { Cl = cl, Sl = sl, El = el, WeekOffMask = req.WeekOffMask ?? old.WeekOffMask };
            await db.SetLeavePolicyAsync(ctx.PropertyId, p, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var staff = (await db.AllStaffAsync(ctx.PropertyId, c2).ConfigureAwait(false)).Where(s => s.Profile.Active).ToList();
            var fy = FinancialYear.Containing(clockInfo.BusinessDate);
            var leaves = await db.DaysAsync(ctx.PropertyId, staff.Select(s => s.StaffId).ToList(), fy.From, fy.From, c2).ConfigureAwait(false);
            var over = staff.Count(s => p.TrackedTypes().Any(t => LeaveRules.Used((leaves.GetValueOrDefault(s.StaffId) ?? new StaffDays()).Leaves, clockInfo.BusinessDate, t)
                > p.EntFor(s.StaffId).Of(t)));
            string Wo(int m) => SccLeave.Days(m).Count == 0 ? "none" : string.Join(" + ", SccLeave.Days(m).Select(d => ((DayOfWeek)d).ToString()[..3]));
            var text = $"Leave settings · CL {SccLeave.Num(p.Cl)} · SL {SccLeave.Num(p.Sl)} · EL {SccLeave.Num(p.El)} · week-off {Wo(p.WeekOffMask)} · new leave only"
                + (over > 0 ? $" · {over} staff already over the new entitlement" : "");
            await tx.AuditAsync("HR_LEAVE_POLICY_SET", "hr.leave-policy:" + ctx.PropertyId.ToString(CultureInfo.InvariantCulture),
                $"{text} (was CL {SccLeave.Num(old.Cl)} · SL {SccLeave.Num(old.Sl)} · EL {SccLeave.Num(old.El)} · week-off {Wo(old.WeekOffMask)})", c2).ConfigureAwait(false);
            return new LeavePolicyResult(p.Cl, p.Sl, p.El, p.WeekOffMask, over, text);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>SCC saveEntOverride: a blank (null) category uses the default; all blank removes the override.</summary>
    public async Task<CommandResult<LeaveEntOverrideResult>> SetLeaveEntOverrideAsync(CommandContext ctx, SetLeaveEntOverrideRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        var cl = req.Cl is { } a ? Days(a, "CL") : (decimal?)null;
        var sl = req.Sl is { } b ? Days(b, "SL") : (decimal?)null;
        var el = req.El is { } c ? Days(c, "EL") : (decimal?)null;
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            var any = cl is not null || sl is not null || el is not null;
            await db.SetLeaveOverrideAsync(ctx.PropertyId, st.StaffId, any ? new LeaveEntOverride(cl, sl, el) : null, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var parts = new[] { ("CL", cl), ("SL", sl), ("EL", el) }.Where(x => x.Item2 is not null).Select(x => $"{x.Item1} {SccLeave.Num(x.Item2!.Value)}");
            var text = any ? $"Entitlement override saved for {st.Profile.Name} · {string.Join(" · ", parts)}" : $"{st.Profile.Name} · Override cleared — using defaults";
            await tx.AuditAsync("HR_LEAVE_ENT_OVERRIDE", Target("hr.staff", st.StaffId), text, c2).ConfigureAwait(false);
            return new LeaveEntOverrideResult(st.StaffId, cl, sl, el, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LeaveBlackoutResult>> AddLeaveBlackoutAsync(CommandContext ctx, AddLeaveBlackoutRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        var (from, to) = req.From <= req.To ? (req.From, req.To) : (req.To, req.From);   // SCC: a swapped range is normalised
        var kind = req.Kind == "holiday" ? "holiday" : "blackout";
        var hard = kind != "holiday" && req.Hard;
        var label = (req.Label ?? string.Empty).Trim();
        label = label.Length > 80 ? label[..80] : label;
        var dept = (req.Dept ?? string.Empty).Trim();
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            if (dept.Length > 0 && !(await db.DepartmentsAsync(ctx.PropertyId, c2).ConfigureAwait(false)).Any(d => d.Code == dept))
                throw HccException.Validation("Unknown department.");
            var b = new LeaveBlackout(0, from, to, dept, kind, hard, label);
            var id = await db.InsertBlackoutAsync(ctx.PropertyId, b, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var text = $"{b.Text} · {Day(from)}{(to != from ? " – " + Day(to) : "")} · {(dept.Length > 0 ? dept : "all departments")} · "
                + (kind == "holiday" ? "Store holiday" : hard ? "Blackout (hard block)" : "Blackout (warn)") + " · Blackout saved";
            await tx.AuditAsync("HR_LEAVE_BLACKOUT_ADDED", Target("hr.leave-blackout", id), text, c2).ConfigureAwait(false);
            return new LeaveBlackoutResult(id, from, to, dept, kind, hard, label, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LeaveBlackoutResult>> RemoveLeaveBlackoutAsync(CommandContext ctx, RemoveLeaveBlackoutRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var b = await db.DeleteBlackoutAsync(ctx.PropertyId, req.BlackoutId, c2).ConfigureAwait(false) ?? throw NotFound("Blackout not found.");
            var text = $"{b.Text} {Day(b.From)}{(b.To != b.From ? " – " + Day(b.To) : "")} · Blackout removed";
            await tx.AuditAsync("HR_LEAVE_BLACKOUT_REMOVED", Target("hr.leave-blackout", b.BlackoutId), text, c2).ConfigureAwait(false);
            return new LeaveBlackoutResult(b.BlackoutId, b.From, b.To, b.Dept, b.Kind, b.Hard, b.Label, text);
        }, ct).ConfigureAwait(false);
    }

    [GeneratedRegex("^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    private static partial Regex AgendaTime();

    /// <summary>SCC submitAgenda: date and title required (≤ 200), time HH:MM or blank, type meeting / agenda / reminder, description ≤ 1000. A manager.</summary>
    public async Task<CommandResult<LeaveAgendaResult>> AddLeaveAgendaAsync(CommandContext ctx, AddLeaveAgendaRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        var title = (req.Title ?? string.Empty).Trim();
        if (title.Length == 0) throw HccException.Validation("Date and title are required");
        if (title.Length > 200) throw HccException.Validation("Title: up to 200 characters.");
        var type = req.Type ?? "meeting";
        if (type is not ("meeting" or "agenda" or "reminder")) throw HccException.Validation("Invalid type");
        var time = (req.Time ?? string.Empty).Trim();
        if (time.Length > 0 && !AgendaTime().IsMatch(time)) throw HccException.Validation("Time: HH:MM (24-hour) or blank.");
        var description = (req.Description ?? string.Empty).Trim();
        if (description.Length > 1000) throw HccException.Validation("Description: up to 1000 characters.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var id = await db.InsertAgendaAsync(ctx.PropertyId, new LeaveAgendaRow(0, req.Date, title, time, type, description, null, ctx.PrincipalId, Now), c2).ConfigureAwait(false);
            var text = $"{Day(req.Date)}{(time.Length > 0 ? " " + time : "")} · {type} · {title} · Agenda item added";
            await tx.AuditAsync("HR_LEAVE_AGENDA_ADDED", Target("hr.leave-agenda", id), text, c2).ConfigureAwait(false);
            return new LeaveAgendaResult(id, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LeaveAgendaResult>> RemoveLeaveAgendaAsync(CommandContext ctx, RemoveLeaveAgendaRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var a = await db.DeleteAgendaAsync(ctx.PropertyId, req.AgendaId, c2).ConfigureAwait(false) ?? throw NotFound("Agenda item not found.");
            var text = $"{Day(a.Date)} · {a.Title} · Agenda removed";
            await tx.AuditAsync("HR_LEAVE_AGENDA_REMOVED", Target("hr.leave-agenda", a.AgendaId), text, c2).ConfigureAwait(false);
            return new LeaveAgendaResult(a.AgendaId, text);
        }, ct).ConfigureAwait(false);
    }

    // ── Import / clear (SCC importJSON merge / executeClearData; Owner) ──

    /// <summary>SCC importJSON merge: a record whose id is already on that date is ignored; the same staff member with a record not rejected on that
    /// date → skipped (any half — stricter than the screen, SCC); a full date → skipped; HCC: a week-off date or a locked pay month → skipped.
    /// Entitlements merged; the week-off set replaced when given.</summary>
    public async Task<CommandResult<ImportLeaveResult>> ImportLeaveAsync(CommandContext ctx, ImportLeaveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        if (req.Leaves is null || req.Agendas is null) throw HccException.Validation("Import failed: Missing \"leaves\" object");
        if (req.Leaves.Count > 20_000 || req.Agendas.Count > 20_000) throw HccException.Validation("Import failed: the file is too large.");
        if (req.Skipped < 0) throw HccException.Validation("Import failed: invalid skipped count.");
        if (req.WeekOffMask is < 0 or > 127) throw HccException.Validation("Import failed: invalid week-off days.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var policy = await db.LeavePolicyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var staff = (await db.AllStaffAsync(ctx.PropertyId, c2).ConfigureAwait(false)).ToDictionary(s => s.StaffId);
            var approvers = (await db.ApproversAsync(ctx.PropertyId, Now, c2).ConfigureAwait(false)).Select(a => a.UserId).ToHashSet();
            var locked = (await db.RunsAsync(ctx.PropertyId, c2).ConfigureAwait(false)).Where(r => r.Locked).Select(r => r.Month).ToHashSet();
            var lo = req.Leaves.Count > 0 ? req.Leaves.Min(l => l.Date) : DateOnly.MinValue;
            var hi = req.Leaves.Count > 0 ? req.Leaves.Max(l => l.Date) : DateOnly.MinValue;
            var existing = req.Leaves.Count > 0 ? (await db.LeavesBetweenAsync(ctx.PropertyId, lo, hi, c2).ConfigureAwait(false)).ToList() : [];
            int added = 0, skipped = 0, agendas = 0;
            foreach (var r in req.Leaves)
            {
                var type = ParseCategory(r.LeaveType);
                var half = ParseHalf(r.Half);
                var status = r.Status switch
                {
                    "Pending" => LeaveStatus.Pending,
                    "Approved" => LeaveStatus.Approved,
                    "Rejected" => LeaveStatus.Rejected,
                    _ => throw HccException.Validation("Import failed: invalid status."),
                };
                if (!staff.ContainsKey(r.StaffId)) throw NotFound("Staff member not found.");
                if (r.ApproverId is { } ap && !approvers.Contains(ap)) throw HccException.Validation("Import failed: the approver is not the Owner or a Manager.");
                var sccId = Opt(r.SccId, 30);
                var day = existing.Where(l => l.Span.Date == r.Date).ToList();
                if (sccId is not null && day.Any(l => l.Span.Status != LeaveStatus.Cancelled && (l.SccId == sccId || l.Span.LeaveId.ToString(CultureInfo.InvariantCulture) == sccId))) continue;
                if (policy.IsWeekOff(r.Date) || locked.Contains(PayMonth.Of(r.Date))) { skipped++; continue; }
                if (day.Any(l => l.Span.Active && l.Span.StaffId == r.StaffId)) { skipped++; continue; }
                if (!SccLeave.Capacity(day.Select(l => l.Span)).CanAdd(half)) { skipped++; continue; }
                var from = r.From <= r.To ? r.From : r.To;
                var to = r.From <= r.To ? r.To : r.From;
                var id = await db.InsertLeaveDayAsync(new NewLeaveDay(ctx.PropertyId, r.StaffId, type, r.Date, half, Opt(r.Reason, 500), r.ApproverId,
                    r.ApproverId is null ? Opt(r.ApproverName, 100) ?? "—" : null, from, to, status, ctx.PrincipalId, Now, r.Legacy, sccId, Opt(r.DecidedByName, 100),
                    Opt(r.DecidedAt, 30)), c2).ConfigureAwait(false);
                existing.Add((await db.LockLeaveAsync(ctx.PropertyId, id, c2).ConfigureAwait(false))!);
                added++;
            }

            var current = (await db.AgendasAsync(ctx.PropertyId, c2).ConfigureAwait(false)).ToList();
            foreach (var a in req.Agendas)
            {
                var sccId = Opt(a.SccId, 30);
                if (sccId is not null && current.Any(x => x.Date == a.Date && (x.SccId == sccId || x.AgendaId.ToString(CultureInfo.InvariantCulture) == sccId))) continue;
                var title = Opt(a.Title, 200) ?? throw HccException.Validation("Import failed: an agenda item has no title.");
                var kind = a.Type is "meeting" or "agenda" or "reminder" ? a.Type : throw HccException.Validation("Import failed: invalid agenda type.");
                var time = (a.Time ?? string.Empty).Trim();
                var row = new LeaveAgendaRow(0, a.Date, title, AgendaTime().IsMatch(time) ? time : "", kind, Opt(a.Description, 1000) ?? "", sccId, ctx.PrincipalId, Now);
                await db.InsertAgendaAsync(ctx.PropertyId, row, c2).ConfigureAwait(false);
                current.Add(row);
                agendas++;
            }

            if (req.Defaults is not null || req.Overrides is not null || req.WeekOffMask is not null)
            {
                var d = req.Defaults;
                var p = policy with
                {
                    Cl = d?.Cl is { } cl ? Days(cl, "CL") : policy.Cl,
                    Sl = d?.Sl is { } sl ? Days(sl, "SL") : policy.Sl,
                    El = d?.El is { } el ? Days(el, "EL") : policy.El,
                    WeekOffMask = req.WeekOffMask ?? policy.WeekOffMask,
                };
                await db.SetLeavePolicyAsync(ctx.PropertyId, p, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
                foreach (var o in req.Overrides ?? [])
                {
                    if (!staff.ContainsKey(o.StaffId)) throw NotFound("Staff member not found.");
                    var old = policy.Overrides is not null && policy.Overrides.TryGetValue(o.StaffId, out var x) ? x : null;
                    var merged = new LeaveEntOverride(o.Cl is { } a ? Days(a, "CL") : old?.Cl, o.Sl is { } b ? Days(b, "SL") : old?.Sl, o.El is { } c ? Days(c, "EL") : old?.El);
                    await db.SetLeaveOverrideAsync(ctx.PropertyId, o.StaffId, merged.Cl is null && merged.Sl is null && merged.El is null ? null : merged, ctx.PrincipalId, Now, c2)
                        .ConfigureAwait(false);
                }
            }

            var text = $"Imported: {added} leaves, {agendas} agendas. Skipped: {req.Skipped + skipped}";
            await tx.AuditAsync("HR_LEAVE_IMPORTED", "hr.leave-import:" + ctx.PropertyId.ToString(CultureInfo.InvariantCulture), text, c2).ConfigureAwait(false);
            return new ImportLeaveResult(added, agendas, req.Skipped + skipped, text);
        }, ct).ConfigureAwait(false);
    }

    private static string? Opt(string? v, int max)
    {
        var s = (v ?? string.Empty).Trim();
        return s.Length == 0 ? null : s.Length > max ? s[..max] : s;
    }

    /// <summary>SCC Clear All: type DELETE. Leave is cancelled and kept for audit (a locked pay month keeps its records); agenda items deleted;
    /// entitlement defaults, overrides and the week-off set back to the defaults; department caps and blackouts survive (SCC).</summary>
    public async Task<CommandResult<ClearLeaveResult>> ClearLeaveAsync(CommandContext ctx, ClearLeaveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        if ((req.Confirm ?? string.Empty).Trim() != "DELETE") throw HccException.Validation("Type DELETE (uppercase) to confirm");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var (n, kept, ag) = await db.ClearLeaveAsync(ctx.PropertyId, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var policy = await db.LeavePolicyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var d = LeavePolicy.Defaults();
            await db.SetLeavePolicyAsync(ctx.PropertyId, policy with { Cl = d.Cl, Sl = d.Sl, El = d.El, WeekOffMask = 0 }, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            foreach (var staffId in (policy.Overrides ?? new Dictionary<long, LeaveEntOverride>()).Keys)
                await db.SetLeaveOverrideAsync(ctx.PropertyId, staffId, null, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var text = $"All data cleared · {n} leave record(s) cancelled (kept for audit){(kept > 0 ? $" · {kept} in locked pay months kept" : "")} · {ag} agenda item(s) removed"
                + " · entitlements and week-off back to the defaults";
            await tx.AuditAsync("HR_LEAVE_CLEARED", "hr.leave-clear:" + ctx.PropertyId.ToString(CultureInfo.InvariantCulture), text, c2).ConfigureAwait(false);
            return new ClearLeaveResult(n, kept, ag, text);
        }, ct).ConfigureAwait(false);
    }
}
