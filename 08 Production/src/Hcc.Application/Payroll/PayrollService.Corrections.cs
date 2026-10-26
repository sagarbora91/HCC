using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

// Wave B item B4 — owner decisions DEC-075 (past attendance, audit A04-02), DEC-083 (paid holidays, A04-05), DEC-084 (bank-detail change, O-16).
// Requests live in hr.change_request (migration 3400), the module request pattern (ivc.credit_note_request): Pending → the Owner decides,
// never the requester (PERM-SOD-001), one pending per target, direct = applied at once. Prototype mirror: handlers/staff.ts HR_ATT_CORRECT,
// HR_REQUEST_DECIDE, HR_REQUEST_WITHDRAW, HR_HOLIDAY_SAVE, HR_HOLIDAY_REMOVE.
public sealed partial class PayrollService
{
    public const string OpCorrectAttendance = "CorrectAttendance";
    public const string OpDecidePayrollRequest = "DecidePayrollRequest";
    public const string OpWithdrawPayrollRequest = "WithdrawPayrollRequest";
    public const string OpSaveHoliday = "SaveHoliday";
    public const string OpRemoveHoliday = "RemoveHoliday";

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Dm(DateOnly d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Id(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static HccException Stale(string message) => new(HccErrorCode.STALE_VERSION, message);

    private sealed record CorrectionPlan(DayState State, PayMonth Month, long Effect);

    /// <summary>DEC-075: the date must lie in the open pay month (never a locked month, never today or the future).</summary>
    private static async Task EnsureCorrectableAsync(IPayrollDb db, int propertyId, DateOnly date, CancellationToken ct)
    {
        var clock = await db.PropertyAsync(propertyId, ct).ConfigureAwait(false);
        var runs = await db.RunsAsync(propertyId, ct).ConfigureAwait(false);
        var month = PayMonth.Of(date);
        if (runs.Any(r => r.Locked && r.Month == month)) throw Invalid($"{month.Label} payroll is locked — a locked month is never changed (DEC-075).");
        if (date > clock.BusinessDate) throw Invalid("A future day cannot be corrected.");
        if (date == clock.BusinessDate) throw Invalid("Today’s attendance is marked on the Today view; corrections are for past days.");
        var (from, to) = PayrollCorrections.Window(clock.BusinessDate, LastLocked(runs));
        if (date < from) throw Invalid($"Only the open pay month can be corrected: {Dm(from)} – {Dm(to)}.");
    }

    /// <summary>Validates a correction of <paramref name="date"/> to <paramref name="target"/> and works out its effect on the month's gross pay.</summary>
    private static async Task<CorrectionPlan> PlanCorrectionAsync(IPayrollDb db, int propertyId, StaffRow st, DateOnly date, DayEntry target, CancellationToken ct)
    {
        var p = st.Profile;
        if (!p.InService(date)) throw Invalid($"{p.Name} was not in service on {Dm(date)}.");
        var month = PayMonth.Of(date);
        var days = (await db.DaysAsync(propertyId, [st.StaffId], month.First, month.Last, ct).ConfigureAwait(false)).GetValueOrDefault(st.StaffId) ?? new StaffDays();
        var state = PayrollCorrections.Current(days, date);
        if (state.PendingLeave) throw Invalid("A pending leave request covers this day — decide it first.");
        if (state.MultiDayLeave is { } ml)
            throw Invalid($"Approved {DayEntry.LeaveLabel(ml.Type).ToLowerInvariant()} {Day(ml.From)} – {Day(ml.To)} covers this day; a leave of several days is not corrected day by day.");
        if (state.Entry == target) throw HccException.Validation($"{Dm(date)} already holds {target.Label.ToLowerInvariant()} — nothing to correct.");
        if (target.Kind == DayEntryKind.Leave)
        {
            var type = target.LeaveType!.Value;
            if (state.HalfDayLeave) throw Invalid("A half-day leave covers this day; correct the attendance mark beside it instead.");
            if (p.PayType == PayType.Daily && type != LeaveType.LOP) throw HccException.Validation("Daily-wage staff can take leave without pay only (illustrative policy).");
            if (PayrollRules.DayOff(p, days, date)) throw HccException.Validation("That day is a weekly off or a paid holiday — no leave is needed.");
            if (PayrollRules.Tracked(type))
            {
                var policy = await db.LeavePolicyAsync(propertyId, ct).ConfigureAwait(false);
                var left = policy.Entitled(type) - LeaveRules.Used(days.Leaves, date, type, state.OneDayLeave?.LeaveId ?? 0);
                if (left < 1)
                    throw HccException.Validation($"Not enough {type} balance: {PayrollRules.Num(Math.Max(0, left))} day{(left == 1 ? "" : "s")} left in {FinancialYear.Containing(date).Label}.");
            }
        }

        var rates = RateVersions.For(await db.RateVersionsAsync(propertyId, ct).ConfigureAwait(false), month);
        var after = PayrollCorrections.With(days, p, date, target, state.OneDayLeave?.LeaveId);
        return new CorrectionPlan(state, month, PayrollCorrections.Gross(p, after, month, rates) - PayrollCorrections.Gross(p, days, month, rates));
    }

    /// <summary>Writes the corrected day (mark, removed mark or one-day approved leave; a replaced one-day leave is cancelled) and recalculates
    /// the month's draft pay-run. The mark is recorded as the corrector's; the request row holds who approved it.</summary>
    private async Task<string?> ApplyCorrectionAsync(IPayrollDb db, int propertyId, StaffRow st, DateOnly date, CorrectionPlan plan, DayEntry target, long requestId,
        string reason, int requestedBy, int decidedBy, CancellationToken ct)
    {
        if (plan.State.OneDayLeave is { } one)
        {
            var l = await db.LockLeaveAsync(propertyId, one.LeaveId, ct).ConfigureAwait(false) ?? throw NotFound("Leave request not found.");
            await db.UpdateLeaveStatusAsync(one.LeaveId, l.Version, LeaveStatus.Cancelled, decidedBy, Now, "Replaced by attendance correction " + Id(requestId), ct).ConfigureAwait(false);
        }

        switch (target.Kind)
        {
            case DayEntryKind.Mark:
                await db.UpsertAttendanceAsync(st.StaffId, new AttendanceMark(date, target.Status!.Value, target.OtMinutes), "Correction " + Id(requestId), requestedBy, Now, ct)
                    .ConfigureAwait(false);
                break;
            case DayEntryKind.Leave:
                await db.DeleteAttendanceAsync(st.StaffId, date, ct).ConfigureAwait(false);
                await db.InsertCorrectionLeaveAsync(propertyId, new LeaveSpan(0, st.StaffId, target.LeaveType!.Value, date, date, null, 1m, LeaveStatus.Approved), reason,
                    requestedBy, decidedBy, requestId, Now, ct).ConfigureAwait(false);
                break;
            default:
                await db.DeleteAttendanceAsync(st.StaffId, date, ct).ConfigureAwait(false);
                break;
        }

        return await RecomputeDraftAsync(db, propertyId, plan.Month, ct).ConfigureAwait(false);
    }

    /// <summary>DEC-075 "the draft pay-run reflects corrections": the month's DRAFT is recalculated (the preparer stays the maker; the lock still
    /// compares a fresh computation). Returns the note, or null when the month has no draft.</summary>
    private async Task<string?> RecomputeDraftAsync(IPayrollDb db, int propertyId, PayMonth month, CancellationToken ct)
    {
        var run = (await db.RunsAsync(propertyId, ct).ConfigureAwait(false)).FirstOrDefault(r => r.Month == month);
        if (run is null || run.Locked) return null;
        var rates = RateVersions.For(await db.RateVersionsAsync(propertyId, ct).ConfigureAwait(false), month);
        var (lines, warnings) = PayrollCalculator.ComputeRun(await PayrollCalculator.LoadAsync(db, propertyId, month, ct).ConfigureAwait(false), rates);
        if (lines.Count == 0) return null;
        await db.ReplaceDraftAsync(run.RunId, rates, lines, warnings, run.PreparedBy, Now, ct).ConfigureAwait(false);
        return $"{month.Label} draft pay-run recalculated";
    }

    // ── CorrectAttendance (DEC-075) ──

    public async Task<CommandResult<CorrectionResult>> CorrectAttendanceAsync(CommandContext ctx, CorrectAttendanceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        var target = DayEntry.Parse(req.Mark, req.OtMinutes);
        var reason = Required(req.Reason, 3, 200, "Give the reason for the correction (3–200 characters).");
        // The Owner holds the approval (RBAC v2 D7, as credit notes): the Owner's own correction applies at once, audited.
        var owner = await Has(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);   // pay changes serialize with pay-runs
            await EnsureCorrectableAsync(db, ctx.PropertyId, req.Date, c2).ConfigureAwait(false);
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            NotSelf(ctx, st, "correct attendance");
            var waiting = (await db.RequestsAsync(ctx.PropertyId, PayrollRequests.Pending, c2).ConfigureAwait(false))
                .FirstOrDefault(r => r.Kind == PayrollRequests.Attendance && r.StaffId == st.StaffId && r.Date == req.Date);
            if (waiting is not null)
                throw new HccException(HccErrorCode.NOT_AVAILABLE,
                    $"A correction of {st.Profile.Name} on {Dm(req.Date)} is already waiting for the Owner (request {Id(waiting.RequestId)}). Withdraw or decide it first.");
            var plan = await PlanCorrectionAsync(db, ctx.PropertyId, st, req.Date, target, c2).ConfigureAwait(false);
            var raises = plan.Effect > 0;
            var direct = !raises || owner;
            var row = await db.InsertRequestAsync(new NewPayrollRequest(ctx.PropertyId, PayrollRequests.Attendance, st.StaffId, req.Date, plan.State.Entry, target, null, null,
                plan.Effect, reason, direct, ctx.PrincipalId, Now), c2).ConfigureAwait(false);
            var what = $"{st.Profile.Name} · {Day(req.Date)} · {plan.State.Entry.Label} → {target.Label}";
            if (!direct)
            {
                await tx.AuditAsync("HR_ATTENDANCE_CORRECTION_REQUESTED", Target("hr.staff", st.StaffId),
                    Clip($"{what} · raises gross pay by {Rs(plan.Effect)} — waiting for the Owner (request {Id(row.RequestId)}) · reason: {reason}", 1000), c2).ConfigureAwait(false);
                return new CorrectionResult(row.RequestId, st.StaffId, req.Date, "PendingApproval", plan.State.Entry.Label, target.Label, true, row.Version,
                    $"{what} · raises pay — sent to the Owner for approval (request {Id(row.RequestId)})");
            }

            var redo = await ApplyCorrectionAsync(db, ctx.PropertyId, st, req.Date, plan, target, row.RequestId, reason, ctx.PrincipalId, ctx.PrincipalId, c2).ConfigureAwait(false);
            var effect = plan.Effect < 0 ? $"lowers gross pay by {Rs(-plan.Effect)}" : plan.Effect == 0 ? "gross pay unchanged" : $"raises gross pay by {Rs(plan.Effect)} (Owner, applied directly)";
            await tx.AuditAsync("HR_ATTENDANCE_CORRECTED", Target("hr.staff", st.StaffId),
                Clip($"{what} · {effect} · reason: {reason} · request {Id(row.RequestId)}{(redo is null ? "" : " · " + redo)}", 1000), c2).ConfigureAwait(false);
            return new CorrectionResult(row.RequestId, st.StaffId, req.Date, "Applied", plan.State.Entry.Label, target.Label, raises, row.Version,
                $"{what} · corrected{(redo is null ? "" : " · " + redo)}");
        }, ct).ConfigureAwait(false);
    }

    // ── DecidePayrollRequest / WithdrawPayrollRequest ──

    private static void EnsureRequestVersion(PayrollRequestRow r, string expectedVersion)
    {
        if (!string.Equals(r.Version, expectedVersion, StringComparison.Ordinal)) throw Stale("The request changed since you loaded it. Refresh and try again.");
    }

    private static string Masked(BankChange? b) => b is null || (b.BankName is null && b.AccountNo is null && b.Ifsc is null)
        ? "(none)"
        : $"{b.BankName ?? "(none)"} {BankDetails.Mask(b.AccountNo)} {b.Ifsc}".TrimEnd();

    /// <summary>The Owner approves or rejects a pending attendance correction (the day must still be as requested and in the open pay month) or a
    /// bank-detail change (the staff master must still hold the details the request started from). Never the requester; never your own record.</summary>
    public async Task<CommandResult<PayrollRequestResult>> DecidePayrollRequestAsync(CommandContext ctx, DecidePayrollRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(req.ExpectedVersion)) throw HccException.Validation("expectedVersion is required to decide a request.");
        var note = (req.Note ?? string.Empty).Trim();
        if (!req.Approve && note.Length < 3) throw HccException.Validation("Give the reason for rejecting (at least 3 characters).");
        if (note.Length > 200) throw HccException.Validation("Note: up to 200 characters.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var peek = await db.RequestAsync(ctx.PropertyId, req.RequestId, lockRow: false, c2).ConfigureAwait(false) ?? throw NotFound("Request not found.");
            var st = await RequireStaffAsync(db, ctx, peek.StaffId, lockRow: true, c2).ConfigureAwait(false);   // lock order: anchor → staff → request
            var r = (await db.RequestAsync(ctx.PropertyId, req.RequestId, lockRow: true, c2).ConfigureAwait(false))!;
            if (r.Status != PayrollRequests.Pending) throw Invalid("This request is no longer pending.");
            EnsureRequestVersion(r, req.ExpectedVersion);
            if (r.RequestedBy == ctx.PrincipalId) throw Forbidden("You cannot decide your own request (no self-approval).");
            if (IsOwn(ctx, st)) throw Forbidden("You cannot decide a request about your own staff record.");
            var bank = r.Kind == PayrollRequests.Bank;
            var what = bank ? $"{st.Profile.Name} · bank {Masked(r.BankBefore)} → {Masked(r.BankAfter)}" : $"{st.Profile.Name} · {Day(r.Date!.Value)} · {r.Before!.Label} → {r.After!.Label}";
            string version, text;
            if (!req.Approve)
            {
                version = await db.DecideRequestAsync(r.RequestId, req.ExpectedVersion, PayrollRequests.Rejected, ctx.PrincipalId, Now, note, c2).ConfigureAwait(false);
                text = $"{what} · request {Id(r.RequestId)} rejected · {Clip(note, 200)}";
                await tx.AuditAsync(bank ? "HR_BANK_CHANGE_REJECTED" : "HR_ATTENDANCE_CORRECTION_REJECTED", Target("hr.staff", st.StaffId), Clip(text, 1000), c2).ConfigureAwait(false);
                return new PayrollRequestResult(r.RequestId, r.Kind, st.StaffId, PayrollRequests.Rejected, version, text);
            }

            string? redo = null;
            if (bank)
            {
                if (new BankChange(st.BankName, st.AccountNo, st.Ifsc) != r.BankBefore)
                    throw Stale($"{st.Profile.Name}’s bank details changed after this request. Reject it and enter the change again.");
                var a = r.BankAfter!;
                await db.UpdateStaffAsync(st with { BankName = a.BankName, AccountNo = a.AccountNo, Ifsc = a.Ifsc }, st.Version, c2).ConfigureAwait(false);
            }
            else
            {
                var date = r.Date!.Value;
                await EnsureCorrectableAsync(db, ctx.PropertyId, date, c2).ConfigureAwait(false);
                var plan = await PlanCorrectionAsync(db, ctx.PropertyId, st, date, r.After!, c2).ConfigureAwait(false);
                if (plan.State.Entry != r.Before)
                    throw Stale($"{st.Profile.Name}’s {Dm(date)} changed after this request (now {plan.State.Entry.Label.ToLowerInvariant()}). Reject it; the day can be corrected again.");
                redo = await ApplyCorrectionAsync(db, ctx.PropertyId, st, date, plan, r.After!, r.RequestId, r.Reason, r.RequestedBy, ctx.PrincipalId, c2).ConfigureAwait(false);
            }

            version = await db.DecideRequestAsync(r.RequestId, req.ExpectedVersion, PayrollRequests.Approved, ctx.PrincipalId, Now, note.Length > 0 ? note : null, c2).ConfigureAwait(false);
            text = bank
                ? $"{what} · request {Id(r.RequestId)} approved (future runs only; locked runs keep their frozen details){(note.Length > 0 ? " · " + Clip(note, 200) : "")}"
                : $"{what} · raises gross pay by {Rs(r.PayEffect ?? 0)} · request {Id(r.RequestId)} approved{(note.Length > 0 ? " · " + Clip(note, 200) : "")}{(redo is null ? "" : " · " + redo)}";
            await tx.AuditAsync(bank ? "HR_BANK_CHANGE_APPROVED" : "HR_ATTENDANCE_CORRECTION_APPROVED", Target("hr.staff", st.StaffId), Clip(text, 1000), c2).ConfigureAwait(false);
            return new PayrollRequestResult(r.RequestId, r.Kind, st.StaffId, PayrollRequests.Approved, version, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<PayrollRequestResult>> WithdrawPayrollRequestAsync(CommandContext ctx, WithdrawPayrollRequestRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        if (!await Has(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false) && !await Has(ctx, PayrollPermissions.Salary, ct: ct).ConfigureAwait(false))
            throw Forbidden("Access denied: only managers or payroll roles make and withdraw payroll requests.");
        if (string.IsNullOrWhiteSpace(req.ExpectedVersion)) throw HccException.Validation("expectedVersion is required to withdraw a request.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var peek = await db.RequestAsync(ctx.PropertyId, req.RequestId, lockRow: false, c2).ConfigureAwait(false) ?? throw NotFound("Request not found.");
            var st = await RequireStaffAsync(db, ctx, peek.StaffId, lockRow: true, c2).ConfigureAwait(false);
            var r = (await db.RequestAsync(ctx.PropertyId, req.RequestId, lockRow: true, c2).ConfigureAwait(false))!;
            if (r.Status != PayrollRequests.Pending) throw Invalid("This request is no longer pending.");
            if (r.RequestedBy != ctx.PrincipalId) throw Forbidden("Only the person who made the request can withdraw it.");
            EnsureRequestVersion(r, req.ExpectedVersion);
            var version = await db.DecideRequestAsync(r.RequestId, req.ExpectedVersion, PayrollRequests.Withdrawn, ctx.PrincipalId, Now, null, c2).ConfigureAwait(false);
            var text = $"{st.Profile.Name} · {(r.Kind == PayrollRequests.Bank ? "bank-detail change" : "attendance correction " + Day(r.Date!.Value))} · request {Id(r.RequestId)} withdrawn";
            await tx.AuditAsync("HR_REQUEST_WITHDRAWN", Target("hr.staff", st.StaffId), text, c2).ConfigureAwait(false);
            return new PayrollRequestResult(r.RequestId, r.Kind, st.StaffId, PayrollRequests.Withdrawn, version, text);
        }, ct).ConfigureAwait(false);
    }

    // ── Paid holidays (DEC-083, Owner) ──

    private static async Task<bool> HolidayMonthAsync(IPayrollDb db, int propertyId, DateOnly date, CancellationToken ct)
    {
        var month = PayMonth.Of(date);
        var run = (await db.RunsAsync(propertyId, ct).ConfigureAwait(false)).FirstOrDefault(r => r.Month == month);
        if (run is { Locked: true }) throw Invalid($"{month.Label} payroll is locked — its holidays cannot change.");
        return run is not null;
    }

    public async Task<CommandResult<HolidayResult>> SaveHolidayAsync(CommandContext ctx, SaveHolidayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        var name = Required(req.Name, 2, 60, "Name the holiday (2–60 characters).");
        if (req.Date.Year is < 2000 or > 2999) throw HccException.Validation("Choose the holiday's date.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var draft = await HolidayMonthAsync(db, ctx.PropertyId, req.Date, c2).ConfigureAwait(false);
            var old = await db.UpsertHolidayAsync(ctx.PropertyId, req.Date, name, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var text = $"Paid holiday {Day(req.Date)} · {name}{(old is null ? " added" : old == name ? " (unchanged)" : $" (was {old})")} · paid like a weekly off"
                       + (draft ? $" · {PayMonth.Of(req.Date).Label} draft pay-run must be recalculated" : "");
            await tx.AuditAsync("HR_HOLIDAY_SAVED", "hr.holiday:" + Day(req.Date), Clip(text, 1000), c2).ConfigureAwait(false);
            return new HolidayResult(req.Date, name, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HolidayResult>> RemoveHolidayAsync(CommandContext ctx, RemoveHolidayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.OwnerPayroll, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            await db.LockAnchorAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var draft = await HolidayMonthAsync(db, ctx.PropertyId, req.Date, c2).ConfigureAwait(false);
            var name = await db.DeleteHolidayAsync(ctx.PropertyId, req.Date, c2).ConfigureAwait(false) ?? throw NotFound("No paid holiday on that date.");
            var text = $"Paid holiday {Day(req.Date)} · {name} removed" + (draft ? $" · {PayMonth.Of(req.Date).Label} draft pay-run must be recalculated" : "");
            await tx.AuditAsync("HR_HOLIDAY_REMOVED", "hr.holiday:" + Day(req.Date), Clip(text, 1000), c2).ConfigureAwait(false);
            return new HolidayResult(req.Date, name, text);
        }, ct).ConfigureAwait(false);
    }
}
