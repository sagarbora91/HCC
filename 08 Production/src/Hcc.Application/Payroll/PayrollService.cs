using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

/// <summary>
/// Payroll and leave commands (W2-2). Rules ported from the prototype handlers (<c>03 Prototype/src/services/mock/handlers/staff.ts</c>,
/// after the W05-B review fixes) with the production rules: actor only from the session, every command through <see cref="ICommandRunner"/>
/// (Idempotency-Key, audit, outbox), PERM ids from migration 0901, expectedVersion → STALE_VERSION, money long paise.
/// Separation of duties: no self money changes (F-10/F-11), Owner-only login links (F-03), maker-checker lock and F&amp;F (F-12, DEC-066).
/// </summary>
public sealed partial class PayrollService(ICommandRunner runner, IPayrollStore store, IAccessPolicy policy, TimeProvider clock)
{
    public const string OpSaveDepartment = "SaveDepartment";
    public const string OpSaveStaff = "SaveStaff";
    public const string OpSetStaffActive = "SetStaffActive";
    public const string OpSetRoster = "SetRoster";
    public const string OpCopyRosterWeek = "CopyRosterWeek";
    public const string OpMarkAttendance = "MarkAttendance";
    public const string OpRequestLeave = "RequestLeave";
    public const string OpDecideLeave = "DecideLeave";
    public const string OpCancelLeave = "CancelLeave";
    public const string OpSetLeavePolicy = "SetLeavePolicy";
    public const string OpGiveAdvance = "GiveAdvance";
    public const string OpSetStatutoryRates = "SetStatutoryRates";
    public const string OpPreparePayRun = "PreparePayRun";
    public const string OpLockPayRun = "LockPayRun";
    public const string OpUnlockPayRun = "UnlockPayRun";
    public const string OpIssueBankFile = "IssueBankFile";
    public const string OpSendPayslip = "SendPayslip";
    public const string OpPrepareFnf = "PrepareFnf";
    public const string OpConfirmFnf = "ConfirmFnf";
    public const string OpDiscardFnf = "DiscardFnf";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private Task Require(CommandContext c, string permission, AccessScope? scope = null, long? amount = null, CancellationToken ct = default)
        => policy.RequireAsync(c.PropertyId, c.PrincipalId, permission, scope, amount, ct);

    private Task<bool> Has(CommandContext c, string permission, AccessScope? scope = null, CancellationToken ct = default)
        => policy.HasAsync(c.PropertyId, c.PrincipalId, permission, scope, ct);

    internal static string Target(string kind, long id) => kind + ":" + id.ToString(CultureInfo.InvariantCulture);

    internal static HccException Invalid(string message) => new(HccErrorCode.INVALID_TRANSITION, message);

    internal static HccException Forbidden(string message) => new(HccErrorCode.FORBIDDEN, message);

    internal static HccException NotFound(string message) => new(HccErrorCode.NOT_FOUND, message);

    internal static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private static bool IsOwn(CommandContext c, StaffRow? st) => st?.UserId is { } u && u == c.PrincipalId;

    /// <summary>F-10: no self-approval of money — salary, PF/ESIC flags, advances, attendance/overtime on your own record go to another person.</summary>
    private static void NotSelf(CommandContext c, StaffRow? st, string what)
    {
        if (IsOwn(c, st))
            throw Forbidden($"Self-approval is not allowed: you cannot {what} on your own staff record. Ask another authorised user (the other payroll user or the Owner).");
    }

    private static string Required(string? value, int min, int max, string message)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length < min || v.Length > max) throw HccException.Validation(message);
        return v;
    }

    private static T ParseEnum<T>(string? value, string message) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: false, out var r) && Enum.IsDefined(r) && !int.TryParse(value, out _) ? r : throw HccException.Validation(message);

    private static PayType ParsePayType(string? v) => v switch
    {
        "monthly" => PayType.Monthly,
        "daily" => PayType.Daily,
        _ => throw HccException.Validation("Choose a pay type (monthly or daily)."),
    };

    internal static string PayTypeWire(PayType t) => t == PayType.Monthly ? "monthly" : "daily";

    private static string MaskPhone(string? phone)
    {
        var d = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return d.Length == 0 ? "(none)" : "••••" + d[^Math.Min(4, d.Length)..];
    }

    private async Task<StaffRow> RequireStaffAsync(IPayrollDb db, CommandContext c, long staffId, bool lockRow, CancellationToken ct)
        => await db.StaffAsync(c.PropertyId, staffId, lockRow, ct).ConfigureAwait(false) ?? throw NotFound("Staff member not found.");

    // ── SaveDepartment ──

    public async Task<CommandResult<DepartmentResult>> SaveDepartmentAsync(CommandContext ctx, SaveDepartmentRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        var code = Required(req.Code, 2, 10, "Department code: 2–10 capital letters or digits.");
        if (!DeptCode().IsMatch(code)) throw HccException.Validation("Department code: 2–10 capital letters or digits.");
        var name = Required(req.Name, 2, 60, "Enter the department name.");
        if (req.LeaveCap is { } cap && cap is < 1 or > 20) throw HccException.Validation("Department limit: 1–20.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var existing = await db.LockDepartmentAsync(ctx.PropertyId, code, c2).ConfigureAwait(false);
            if (existing?.LeaveCap != req.LeaveCap && !(existing is null && req.LeaveCap is null))
                await Require(ctx, PayrollPermissions.OwnerPayroll, ct: c2).ConfigureAwait(false);   // leave capacity = leave policy (Owner)
            await db.UpsertDepartmentAsync(ctx.PropertyId, new DepartmentRow(code, name, req.LeaveCap), c2).ConfigureAwait(false);
            var notes = existing is null ? "added" : "updated";
            await tx.AuditAsync("HR_DEPT_SAVED", "hr.dept:" + code, $"{code} · {name} {notes}{(req.LeaveCap is { } x ? $" · max {x} off per day" : "")}", c2).ConfigureAwait(false);
            return new DepartmentResult(code, name, req.LeaveCap, notes);
        }, ct).ConfigureAwait(false);
    }

    [GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial Regex DeptCode();

    [GeneratedRegex("^[A-Z]{4}0[A-Z0-9]{6}$")]
    private static partial Regex Ifsc();

    [GeneratedRegex(@"^\d{6,18}$")]
    private static partial Regex Account();

    // ── SaveStaff ──

    public async Task<CommandResult<StaffResult>> SaveStaffAsync(CommandContext ctx, SaveStaffRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var manage = await Has(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        var pay = await Has(ctx, PayrollPermissions.Salary, ct: ct).ConfigureAwait(false);
        if (!manage && !pay) throw Forbidden("Access denied: only managers or payroll roles can edit the staff master.");
        var name = Required(req.Name, 2, 100, "Enter the staff member’s name.");
        var designation = Required(req.Designation, 1, 80, "Enter a designation.");
        var shift = req.DefaultShift is "M" or "E" or "N" or "G" ? ParseEnum<ShiftCode>(req.DefaultShift, "Choose a default shift.") : throw HccException.Validation("Choose a default shift.");
        if (req.WeeklyOff is < 0 or > 6) throw HccException.Validation("Choose a weekly off day.");
        Gender? gender = req.Gender is null ? null : req.Gender is "M" or "F" ? ParseEnum<Gender>(req.Gender, "") : throw HccException.Validation("Choose M or F for the professional-tax slab.");
        PayStructure? structure = req.Structure is null ? null : req.Structure is "S" or "B" ? ParseEnum<PayStructure>(req.Structure, "") : throw HccException.Validation("Choose a salary structure.");
        PayType? payType = req.PayType is null ? null : ParsePayType(req.PayType);
        if (req.MonthlyPaise is { } m && m is < 0 or > 100_000_000) throw HccException.Validation("Enter a valid monthly salary.");
        if (req.DailyPaise is { } dly && dly is < 0 or > 100_000_000) throw HccException.Validation("Enter a valid daily wage.");
        var ifsc = req.Ifsc?.Trim();
        var account = req.AccountNo?.Trim();
        var bankName = req.BankName?.Trim();
        if (!string.IsNullOrEmpty(ifsc) && !Ifsc().IsMatch(ifsc)) throw HccException.Validation("IFSC is 11 characters, e.g. DEMO0000001.");
        if (!string.IsNullOrEmpty(account) && !Account().IsMatch(account)) throw HccException.Validation("Account number: 6–18 digits.");
        if (bankName is { Length: > 80 }) throw HccException.Validation("Bank name: up to 80 characters.");
        if (req.Phone is { } ph && ph.Trim().Length > 20) throw HccException.Validation("Mobile number: up to 20 characters.");
        if (req.StaffId is not null && string.IsNullOrWhiteSpace(req.ExpectedVersion)) throw HccException.Validation("expectedVersion is required to edit a staff record.");

        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var dept = Required(req.Dept, 1, 10, "Choose a department.");
            if (await db.LockDepartmentAsync(ctx.PropertyId, dept, c2).ConfigureAwait(false) is null) throw HccException.Validation("Choose a department.");
            StaffRow? existing = null;
            if (req.StaffId is { } id) existing = await RequireStaffAsync(db, ctx, id, lockRow: true, c2).ConfigureAwait(false);
            var notes = new List<string>();

            // F-03: the login link decides whose payslips a user sees as "own" — Owner only, never your own link, audited old → new.
            var linkChanged = req.LinkUserId != existing?.UserId;
            if (linkChanged)
            {
                if (!await Has(ctx, PayrollPermissions.OwnerPayroll, ct: c2).ConfigureAwait(false))
                    throw Forbidden("Only the Owner can link or unlink a login — payslip access follows the link.");
                if (req.LinkUserId == ctx.PrincipalId || existing?.UserId == ctx.PrincipalId) throw Forbidden("You cannot change the login link of your own staff record.");
                if (req.LinkUserId is { } nu)
                {
                    if (!await db.UserAtPropertyAsync(ctx.PropertyId, nu, c2).ConfigureAwait(false)) throw HccException.Validation("Linked user not found.");
                    var other = await db.StaffOfUserAsync(ctx.PropertyId, nu, c2).ConfigureAwait(false);
                    if (other is not null && other.StaffId != existing?.StaffId) throw HccException.Validation("That user is already linked to another staff record.");
                    if (existing is not null && !req.ConfirmRelink)
                    {
                        var slips = await db.LockedPayslipCountAsync(existing.StaffId, c2).ConfigureAwait(false);
                        if (slips > 0)
                            throw Invalid($"{existing.Profile.Name} has payslips in {slips} locked pay-run{(slips == 1 ? "" : "s")}. The linked login will see them — confirm the relink.");
                    }
                }

                notes.Add($"login {existing?.UserId?.ToString(CultureInfo.InvariantCulture) ?? "(none)"} → {req.LinkUserId?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
            }

            var old = existing?.Profile;
            var salaryChanged = (payType is not null || req.MonthlyPaise is not null || req.DailyPaise is not null)
                && (old is null || old.PayType != (payType ?? old.PayType) || old.MonthlyPaise != (req.MonthlyPaise ?? old.MonthlyPaise) || old.DailyPaise != (req.DailyPaise ?? old.DailyPaise));
            if (salaryChanged)
            {
                if (!pay) throw Forbidden("Only the Owner or Accountant can set salary details.");
                NotSelf(ctx, existing, "change the salary");
            }

            var flagsChanged = (structure is not null && structure != old?.Structure) || (req.PfApplicable is not null && req.PfApplicable != old?.PfApplicable)
                || (req.EsicApplicable is not null && req.EsicApplicable != old?.EsicApplicable);
            static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            var bankChanged = (req.BankName is not null && Norm(req.BankName) != existing?.BankName) || (req.AccountNo is not null && Norm(req.AccountNo) != existing?.AccountNo)
                || (req.Ifsc is not null && Norm(req.Ifsc) != existing?.Ifsc);
            if ((flagsChanged || bankChanged) && !pay) throw Forbidden("Only the Owner or Accountant can set PF / ESIC and bank details.");
            if (flagsChanged && existing is not null) NotSelf(ctx, existing, "change the salary structure or PF / ESIC");
            if (bankChanged && IsOwn(ctx, existing)) throw Forbidden("You cannot change the bank details on your own staff record (no self-approval). Ask the other payroll user.");

            // F-11: the PT slab (gender) and the payslip WhatsApp number of an existing record are payroll fields.
            var phone = req.Phone is null ? existing?.Phone ?? string.Empty : req.Phone.Trim();
            var genderChanged = existing is not null && gender is not null && gender != old!.EffectiveGender;
            var phoneChanged = existing is not null && phone != existing.Phone;
            if ((genderChanged || phoneChanged) && !pay)
                throw Forbidden($"Only the Owner or Accountant can change the {(genderChanged ? "professional-tax slab" : "payslip mobile number")} of a staff record.");
            if ((genderChanged || phoneChanged) && IsOwn(ctx, existing)) throw Forbidden("You cannot change the PT slab or payslip mobile on your own staff record.");

            var joined = existing?.Profile.Joined ?? req.Joined ?? clockInfo.BusinessDate;
            if (existing is null && joined > clockInfo.BusinessDate) throw HccException.Validation("The joining date cannot be after the business date.");
            var profile = new StaffProfile(existing?.StaffId ?? 0, name, dept, designation,
                pay ? payType ?? old?.PayType ?? PayType.Monthly : old?.PayType ?? PayType.Monthly,
                pay ? req.MonthlyPaise ?? old?.MonthlyPaise ?? 0 : old?.MonthlyPaise ?? 0,
                pay ? req.DailyPaise ?? old?.DailyPaise ?? 0 : old?.DailyPaise ?? 0,
                shift, req.WeeklyOff, joined, old?.ExitedOn, old?.Active ?? true,
                gender ?? old?.Gender,
                pay && req.Structure is not null ? structure : old?.Structure,
                pay && req.PfApplicable is not null ? req.PfApplicable : old?.PfApplicable,
                pay && req.EsicApplicable is not null ? req.EsicApplicable : old?.EsicApplicable);
            var newBank = pay && req.BankName is not null ? Norm(req.BankName) : existing?.BankName;
            var newAccount = pay && req.AccountNo is not null ? Norm(req.AccountNo) : existing?.AccountNo;
            var newIfsc = pay && req.Ifsc is not null ? Norm(req.Ifsc) : existing?.Ifsc;

            if (existing is null)
            {
                var sid = await db.InsertStaffAsync(new NewStaff(ctx.PropertyId, profile, req.LinkUserId, phone, newBank, newAccount, newIfsc, ctx.PrincipalId, Now), c2).ConfigureAwait(false);
                var created = await RequireStaffAsync(db, ctx, sid, lockRow: false, c2).ConfigureAwait(false);
                var detail = $"{name} added{(req.LinkUserId is { } u ? $" · login {u}" : "")}{(pay ? "" : " (salary to be set by payroll)")}";
                await tx.AuditAsync("HR_STAFF_ADDED", Target("hr.staff", sid), detail, c2).ConfigureAwait(false);
                return new StaffResult(sid, name, created.Version, detail);
            }

            if (salaryChanged) notes.Add("salary changed");
            if (flagsChanged) notes.Add("PF / ESIC / structure changed");
            if (genderChanged) notes.Add($"PT slab {(old!.EffectiveGender == Gender.F ? "women" : "men")} → {(gender == Gender.F ? "women" : "men")}");
            if (phoneChanged) notes.Add($"payslip mobile {MaskPhone(existing.Phone)} → {MaskPhone(phone)}");
            // F-02: audit old → new bank details (masked); locked runs keep the details frozen at lock.
            if (bankChanged)
                notes.Add($"bank {existing.BankName ?? "(none)"} {BankDetails.Mask(existing.AccountNo)} {existing.Ifsc} → {newBank ?? "(none)"} {BankDetails.Mask(newAccount)} {newIfsc} (future runs only; locked runs keep their frozen details)");
            var row = new StaffRow(profile, req.LinkUserId, phone, newBank, newAccount, newIfsc, existing.Version);
            var version = await db.UpdateStaffAsync(row, req.ExpectedVersion!, c2).ConfigureAwait(false);
            var text = $"{name} updated{(notes.Count > 0 ? " · " + string.Join(" · ", notes) : "")}";
            await tx.AuditAsync("HR_STAFF_UPDATED", Target("hr.staff", existing.StaffId), Clip(text, 1000), c2).ConfigureAwait(false);
            return new StaffResult(existing.StaffId, name, version, text);
        }, ct).ConfigureAwait(false);
    }

    // ── SetStaffActive (exit / reactivate) ──

    public async Task<CommandResult<StaffResult>> SetStaffActiveAsync(CommandContext ctx, SetStaffActiveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            StaffProfile p;
            string text;
            if (req.Active)
            {
                if (st.Profile.Active) throw Invalid("Already active.");
                var f = (await db.FnfsAsync(ctx.PropertyId, c2).ConfigureAwait(false)).FirstOrDefault(x => x.StaffId == st.StaffId && x.Status != "Discarded");
                if (f?.Status == "Draft") throw Invalid("A draft full and final settlement is waiting — discard it before reactivating.");
                if (f is not null) throw Invalid("Full and final settlement is done — add a new staff record instead.");
                p = st.Profile with { Active = true, ExitedOn = null };
                text = $"{st.Profile.Name} · reactivated";
            }
            else
            {
                if (!st.Profile.Active) throw Invalid("Already inactive.");
                if (IsOwn(ctx, st)) throw Forbidden("You cannot deactivate your own staff record.");
                // P6-GUARD: refuse when the business date is closed (IBusinessDayGuard from P6 is not on this branch yet).
                if (clockInfo.BusinessDate < st.Profile.Joined) throw Invalid("The exit date cannot be before the joining date.");
                p = st.Profile with { Active = false, ExitedOn = clockInfo.BusinessDate };
                text = $"{st.Profile.Name} · exited {clockInfo.BusinessDate:yyyy-MM-dd}";
            }

            var version = await db.UpdateStaffAsync(st with { Profile = p }, req.ExpectedVersion, c2).ConfigureAwait(false);
            await tx.AuditAsync(req.Active ? "HR_STAFF_REACTIVATED" : "HR_STAFF_EXITED", Target("hr.staff", st.StaffId), text, c2).ConfigureAwait(false);
            return new StaffResult(st.StaffId, p.Name, version, text);
        }, ct).ConfigureAwait(false);
    }

    // ── Roster ──

    public async Task<CommandResult<RosterResult>> SetRosterAsync(CommandContext ctx, SetRosterRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        var shift = ParseEnum<ShiftCode>(req.Shift, "Choose a shift.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (!st.Profile.Active) throw Invalid("This staff member is inactive.");
            if (req.Date < clockInfo.BusinessDate) throw Invalid("Only today’s or future roster can change; past roster is history.");
            await db.SetRosterAsync(st.StaffId, req.Date, shift, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            var text = $"{st.Profile.Name} · {req.Date:yyyy-MM-dd} → {shift}";
            await tx.AuditAsync("HR_ROSTER_SET", Target("hr.staff", st.StaffId), text, c2).ConfigureAwait(false);
            return new RosterResult(1, text);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RosterResult>> CopyRosterWeekAsync(CommandContext ctx, CopyRosterWeekRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            var monday = req.Week.AddDays(-(((int)req.Week.DayOfWeek + 6) % 7));
            var staff = (await db.AllStaffAsync(ctx.PropertyId, c2).ConfigureAwait(false)).Where(s => s.Profile.Active).ToList();
            var days = await db.DaysAsync(ctx.PropertyId, staff.Select(s => s.StaffId).ToList(), monday, monday.AddDays(13), c2).ConfigureAwait(false);
            var n = 0;
            foreach (var s in staff)
            {
                var d0 = days.TryGetValue(s.StaffId, out var x) ? x : new StaffDays();
                for (var i = 0; i < 7; i++)
                {
                    var d = monday.AddDays(i);
                    var t = d.AddDays(7);
                    if (t < clockInfo.BusinessDate) continue;
                    await db.SetRosterAsync(s.StaffId, t, PayrollRules.ShiftFor(s.Profile, d0, d), ctx.PrincipalId, Now, c2).ConfigureAwait(false);
                    n++;
                }
            }

            if (n == 0) throw Invalid("The target week is already in the past.");
            var text = $"Roster week of {monday:yyyy-MM-dd} copied to {monday.AddDays(7):yyyy-MM-dd} · {n} cells";
            await tx.AuditAsync("HR_ROSTER_COPIED", "hr.roster:" + monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), text, c2).ConfigureAwait(false);
            return new RosterResult(n, text);
        }, ct).ConfigureAwait(false);
    }

    // ── Attendance ──

    public async Task<CommandResult<AttendanceResult>> MarkAttendanceAsync(CommandContext ctx, MarkAttendanceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        AttendanceStatus? status = req.Status is null ? null : ParseEnum<AttendanceStatus>(req.Status, "Choose an attendance status.");
        if (status is null) await Require(ctx, PayrollPermissions.Self, AccessScopes.Any, ct: ct).ConfigureAwait(false);
        else await Require(ctx, PayrollPermissions.StaffManage, ct: ct).ConfigureAwait(false);
        var ot = req.OtMinutes ?? 0;
        if (status is not null)
        {
            if (ot is < 0 or > 480) throw HccException.Validation("Overtime must be 0–480 minutes.");
            if (status == AttendanceStatus.A && ot > 0) throw HccException.Validation("An absent day cannot carry overtime.");
        }

        var note = (req.Note ?? string.Empty).Trim();
        if (note.Length > 120) throw HccException.Validation("Note: up to 120 characters.");
        return await runner.RunAsync(ctx, req, async (tx, c2) =>
        {
            var db = store.In(tx);
            var clockInfo = await db.PropertyAsync(ctx.PropertyId, c2).ConfigureAwait(false);
            // P6-GUARD: attendance is written for the business date — refuse with DAY_CLOSED once P6's IBusinessDayGuard is merged.
            var date = clockInfo.BusinessDate;
            var st = await RequireStaffAsync(db, ctx, req.StaffId, lockRow: true, c2).ConfigureAwait(false);
            if (!st.Profile.Active || !st.Profile.InService(date)) throw Invalid("This staff member is inactive.");
            var days = (await db.DaysAsync(ctx.PropertyId, [st.StaffId], date, date, c2).ConfigureAwait(false)).GetValueOrDefault(st.StaffId) ?? new StaffDays();
            var leave = PayrollRules.ApprovedLeaveOn(days, date);
            if (leave is not null && leave.Half is null) throw Invalid("Approved leave covers today. Cancel the leave first.");
            AttendanceMark mark;
            string text;
            if (status is null)
            {
                if (!IsOwn(ctx, st)) throw Forbidden("You can only check yourself in.");
                if (days.Attendance.ContainsKey(date)) throw Invalid("Attendance is already recorded for today.");
                var shift = PayrollRules.ShiftFor(st.Profile, days, date);
                if (shift == ShiftCode.OFF) throw Invalid("You are rostered off today — ask a manager to record worked-off attendance.");
                var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), CalendarDate.AssertTimeZone(clockInfo.TimeZone)).DateTime);
                var s = leave is not null ? AttendanceStatus.H : Shifts.CheckinStatus(local, shift);
                mark = new AttendanceMark(date, s, 0);
                note = leave is not null ? "Self check-in · half-day leave" : "Self check-in";
                text = $"{st.Profile.Name} · checked in {local:HH\\:mm} · {s}";
            }
            else
            {
                NotSelf(ctx, st, "record or correct attendance or overtime (use self check-in)");
                mark = new AttendanceMark(date, status.Value, ot);
                text = $"{st.Profile.Name} · {date:yyyy-MM-dd} · {status}{(ot > 0 ? $" · OT {ot} min" : "")}";
            }

            var corrected = await db.UpsertAttendanceAsync(st.StaffId, mark, note.Length > 0 ? note : null, ctx.PrincipalId, Now, c2).ConfigureAwait(false);
            if (corrected) text += " (corrected)";
            await tx.AuditAsync("HR_ATTENDANCE", Target("hr.staff", st.StaffId), text, c2).ConfigureAwait(false);
            return new AttendanceResult(st.StaffId, date, mark.Status.ToString(), mark.OtMinutes, corrected, text);
        }, ct).ConfigureAwait(false);
    }
}
