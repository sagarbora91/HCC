using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

public sealed partial class PayrollService
{
    internal static LeaveResult ToResult(LeaveRow l, string? version = null) => new(l.Span.LeaveId, l.Span.StaffId, l.Span.Type.ToString(), l.Span.From, l.Span.To,
        l.Span.Half?.ToString(), l.Span.Days, l.Span.Status.ToString(), version ?? l.Version);

    // ── RequestLeave (SCC submitLeave: weekly offs skipped, duplicate / half-day overlap, capacity per department per day) ──

    public async Task<CommandResult<LeaveResult>> RequestLeaveAsync(CommandContext ctx, RequestLeaveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var type = ParseEnum<LeaveType>(req.LeaveType, "Choose a leave type.");
        LeaveHalf? half = req.Half is null ? null : ParseEnum<LeaveHalf>(req.Half, "Choose the first or second half.");
        var to = half is not null ? req.From : req.To ?? req.From;
        if (to < req.From) throw HccException.Validation("The end date must be on or after the start date.");
        if (req.From.AddDays(LeaveRules.MaxSpanDays - 1) < to) throw HccException.Validation($"Maximum {LeaveRules.MaxSpanDays} days per request.");
        if (FinancialYear.Containing(req.From).To < to) throw HccException.Validation("A request cannot cross 31 March — split it per financial year.");
        var reason = Required(req.Reason, 3, 200, "Give a short reason.");
        var manage = await Has(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        if (!manage) await Require(ctx, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var peek = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: false, c2).ConfigureAwait(false);
            if (!IsOwn(ctx, peek) && !manage) throw Forbidden("You can request leave only for yourself.");
            // Lock order: department (capacity serializes per department) → staff.
            var dept = await db.LockDepartmentAsync(ctx.PropertyId, peek.Profile.Dept, c2).ConfigureAwait(false);
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (!st.Profile.Active) throw Invalid("This staff member is inactive.");
            if (st.Profile.PayType == PayType.Daily && type != LeaveType.LOP) throw HccException.Validation("Daily-wage staff can take leave without pay only (illustrative policy).");
            if (req.From < clockInfo.BusinessDate) throw HccException.Validation("Leave cannot start before the business date.");

            var days = (await db.DaysAsync(ctx.PropertyId, [st.StaffId], req.From, to, c2).ConfigureAwait(false)).GetValueOrDefault(st.StaffId) ?? new StaffDays();
            // SCC duplicate rule: a full day conflicts with anything; first and second half of the same day can coexist.
            if (days.Leaves.Any(l => l.Holds && l.From <= to && l.To >= req.From && !(half is not null && l.Half is not null && l.Half != half)))
                throw new HccException(HccErrorCode.NOT_AVAILABLE, "These dates overlap another leave request.");
            var n = PayrollRules.LeaveDays(st.Profile, days, req.From, to, half);
            if (n <= 0) throw HccException.Validation(half is not null ? "That day is a weekly off." : "The chosen dates are all weekly offs.");
            var policy = await db.LeavePolicyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            if (PayrollRules.Tracked(type))
            {
                var left = policy.Entitled(type) - LeaveRules.Used(days.Leaves, req.From, type);
                if (n > left) throw HccException.Validation($"Not enough {type} balance: {PayrollRules.Num(left)} day{(left == 1 ? "" : "s")} left in {FinancialYear.Containing(req.From).Label}.");
            }

            // SCC capacity: max staff off per day — per department; pending and approved requests hold a slot.
            var cap = dept?.LeaveCap ?? policy.DefaultCap;
            var others = (await db.DepartmentLeavesAsync(ctx.PropertyId, st.Profile.Dept, req.From, to, c2).ConfigureAwait(false))
                .Where(x => x.Staff.StaffId != st.StaffId).ToList();
            var otherDays = await db.DaysAsync(ctx.PropertyId, others.Select(o => o.Staff.StaffId).Distinct().ToList(), req.From, to, c2).ConfigureAwait(false);
            for (var d = req.From; d <= to; d = d.AddDays(1))
            {
                if (PayrollRules.ShiftFor(st.Profile, days, d) == ShiftCode.OFF) continue;
                var off = others.Count(o => o.Leave.Covers(d)
                    && PayrollRules.ShiftFor(o.Staff, otherDays.GetValueOrDefault(o.Staff.StaffId) ?? new StaffDays(), d) != ShiftCode.OFF);
                if (off >= cap)
                    throw new HccException(HccErrorCode.NOT_AVAILABLE, $"{dept?.Name ?? st.Profile.Dept} already has {off} staff off on {d:d MMM} (maximum {cap} per day).");
            }

            var row = await db.InsertLeaveAsync(ctx.PropertyId, new LeaveSpan(0, st.StaffId, type, req.From, to, half, n, LeaveStatus.Pending), reason, ctx.PrincipalId, Now, c2)
                .ConfigureAwait(false);
            var text = $"{st.Profile.Name} · {type} {PayrollRules.Num(n)} day{(n == 1 ? "" : "s")}{(half is { } h ? $" ({(h == LeaveHalf.AM ? "first" : "second")} half)" : "")} requested";
            await tx.AuditAsync("HR_LEAVE_REQUESTED", Target("hr.leave", row.Span.LeaveId), text, c2).ConfigureAwait(false);
            return ToResult(row);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LeaveResult>> DecideLeaveAsync(CommandContext ctx, DecideLeaveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.LeaveDecide, ct: ct).ConfigureAwait(false);
        var note = (req.Note ?? string.Empty).Trim();
        if (!req.Approve && note.Length == 0) throw HccException.Validation("Give a reason for rejecting.");
        if (note.Length > 200) throw HccException.Validation("Note: up to 200 characters.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var l = await db.LockLeaveAsync(ctx.PropertyId, req.LeaveId, c2).ConfigureAwait(false) ?? throw NotFound("Leave request not found.");
            if (l.Span.Status != LeaveStatus.Pending) throw Invalid("This request is no longer pending.");
            var st = await RequireStaffAsync(db, ctx, l.Span.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (l.RequestedBy == ctx.PrincipalId || IsOwn(ctx, st)) throw Forbidden("You cannot approve your own request. Another authorised user decides it.");
            if (req.Approve)
            {
                if (await db.AnyAttendanceAsync(st.StaffId, l.Span.From, l.Span.To, exceptHalfDayMarks: l.Span.Half is not null, c2).ConfigureAwait(false))
                    throw Invalid("Attendance is already recorded inside these dates.");
                if (PayrollRules.Tracked(l.Span.Type))
                {
                    var policy = await db.LeavePolicyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
                    var days = (await db.DaysAsync(ctx.PropertyId, [st.StaffId], l.Span.From, l.Span.To, c2).ConfigureAwait(false)).GetValueOrDefault(st.StaffId) ?? new StaffDays();
                    var left = policy.Entitled(l.Span.Type) - LeaveRules.Used(days.Leaves, l.Span.From, l.Span.Type, l.Span.LeaveId);
                    if (l.Span.Days > left) throw HccException.Validation("Leave balance is no longer sufficient.");
                }
            }

            var status = req.Approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
            var version = await db.UpdateLeaveStatusAsync(l.Span.LeaveId, req.ExpectedVersion, status, ctx.PrincipalId, Now, note.Length > 0 ? note : null, c2).ConfigureAwait(false);
            await tx.AuditAsync(req.Approve ? "HR_LEAVE_APPROVED" : "HR_LEAVE_REJECTED", Target("hr.leave", l.Span.LeaveId),
                $"{st.Profile.Name} · {l.Span.Type} {l.Span.From:yyyy-MM-dd}…{l.Span.To:yyyy-MM-dd} · {status}{(note.Length > 0 ? " · " + Clip(note, 200) : "")}", c2).ConfigureAwait(false);
            await tx.PublishAsync("LeaveDecided", l.Span.LeaveId, new { leaveId = l.Span.LeaveId, staffId = st.StaffId, status = status.ToString(), from = l.Span.From, to = l.Span.To },
                c2).ConfigureAwait(false);
            return ToResult(l with { Span = l.Span with { Status = status } }, version);
        }, ct).ConfigureAwait(false);
    }

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
            var l = await db.LockLeaveAsync(ctx.PropertyId, req.LeaveId, c2).ConfigureAwait(false) ?? throw NotFound("Leave request not found.");
            var st = await RequireStaffAsync(db, ctx, l.Span.StaffId, lockRow: false, c2).ConfigureAwait(false);
            var own = l.RequestedBy == ctx.PrincipalId || IsOwn(ctx, st);
            if (l.Span.Status == LeaveStatus.Pending)
            {
                if (!own && !manage) throw Forbidden("Only the requester or a manager can cancel this request.");
            }
            else
            {
                if (l.Span.Status != LeaveStatus.Approved || l.Span.From < clockInfo.BusinessDate)
                    throw Invalid("Only pending requests, or approved leave that has not started, can be cancelled.");
                if (!manage) throw Forbidden("Access denied: a manager cancels approved leave.");
            }

            var version = await db.UpdateLeaveStatusAsync(l.Span.LeaveId, req.ExpectedVersion, LeaveStatus.Cancelled, ctx.PrincipalId, Now, null, c2).ConfigureAwait(false);
            await tx.AuditAsync("HR_LEAVE_CANCELLED", Target("hr.leave", l.Span.LeaveId), $"{st.Profile.Name} · {l.Span.Type} {l.Span.From:yyyy-MM-dd} · cancelled", c2).ConfigureAwait(false);
            return ToResult(l with { Span = l.Span with { Status = LeaveStatus.Cancelled } }, version);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Leave policy (SCC entitlements + default daily capacity). Owner (DEC-058). New requests only; F-15 reports staff already over a cut.</summary>
    public async Task<CommandResult<LeavePolicyResult>> SetLeavePolicyAsync(CommandContext ctx, SetLeavePolicyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        foreach (var (v, t) in new[] { (req.Cl, "CL"), (req.Sl, "SL"), (req.El, "EL") })
            if (v is < 0 or > 60) throw HccException.Validation($"Entitlement for {t}: 0–60 days.");
        if (req.DefaultCap is < 1 or > 20) throw HccException.Validation("Maximum staff off per day: 1–20.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var old = await db.LeavePolicyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var p = new LeavePolicy(req.Cl, req.Sl, req.El, req.DefaultCap, old.DeptCap);
            await db.SetLeavePolicyAsync(ctx.PropertyId, p, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var staff = (await db.AllStaffAsync(ctx.PropertyId, c2).ConfigureAwait(false)).Where(s => s.Profile.Active).ToList();
            var fy = FinancialYear.Containing(clockInfo.BusinessDate);
            var leaves = await db.DaysAsync(ctx.PropertyId, staff.Select(s => s.StaffId).ToList(), fy.From, fy.From, c2).ConfigureAwait(false);
            var over = staff.Count(s => LeaveRules.TrackedTypes.Any(t => LeaveRules.Used((leaves.GetValueOrDefault(s.StaffId) ?? new StaffDays()).Leaves, clockInfo.BusinessDate, t) > p.Entitled(t)));
            var text = $"Leave policy · CL {p.Cl} · SL {p.Sl} · EL {p.El} · max {p.DefaultCap}/department/day · new requests only{(over > 0 ? $" · {over} staff already over the new entitlement" : "")}";
            await tx.AuditAsync("HR_LEAVE_POLICY_SET", "hr.leave-policy:" + ctx.PropertyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{text} (was CL {old.Cl} · SL {old.Sl} · EL {old.El} · max {old.DefaultCap})", c2).ConfigureAwait(false);
            return new LeavePolicyResult(p.Cl, p.Sl, p.El, p.DefaultCap, over, text);
        }, ct).ConfigureAwait(false);
    }
}
