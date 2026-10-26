using System.Globalization;
using System.Text;
using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Payroll;
using Hcc.Payroll.Tests.Harness;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// Wave B item B4 on SQL Server with the real policy: DEC-075 correction of past attendance in the open pay month (audit A04-02), DEC-083 paid
/// holidays (A04-05) and DEC-084 bank-detail change with the Owner's approval (O-16). Golden BASE fixture (business date 24 Oct 2026, August locked
/// in each test that needs it). Money worked out by hand in the comments.
/// </summary>
[Trait("Category", "Sql")]
public sealed class PayrollCorrectionSqlTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly D(string s) => PayrollGolden.Date(s);

    private static async Task<PayRunResult> PrepareAsync(Hotel h, string month, string user = "accountant")
        => (await h.Payroll.PreparePayRunAsync(h.As(user, PayrollService.OpPreparePayRun), new PreparePayRunRequest(month), Ct)).Value;

    private static async Task<PayRunResult> LockAsync(Hotel h, long runId, string user = "owner")
    {
        var v = await h.RowVersionAsync("hr.pay_run", "run_id", runId);
        return (await h.Payroll.LockPayRunAsync(h.As(user, PayrollService.OpLockPayRun), new LockPayRunRequest(runId, v), Ct)).Value;
    }

    private static Task<Hcc.Application.Commands.CommandResult<CorrectionResult>> CorrectAsync(Hotel h, string user, string staff, string date, string mark, int? ot = null,
        string reason = "Register checked (fictional)")
        => h.Payroll.CorrectAttendanceAsync(h.As(user, PayrollService.OpCorrectAttendance), new CorrectAttendanceRequest(h.S(staff), D(date), mark, reason, ot), Ct);

    private static async Task<Hcc.Application.Commands.CommandResult<PayrollRequestResult>> DecideAsync(Hotel h, string user, long requestId, bool approve, string? note = null)
        => await h.Payroll.DecidePayrollRequestAsync(h.As(user, PayrollService.OpDecidePayrollRequest),
            new DecidePayrollRequestRequest(requestId, await h.RowVersionAsync("hr.change_request", "request_id", requestId), approve, note), Ct);

    private static async Task<PayLine> LineAsync(Hotel h, long runId, string staff)
    {
        await using var r = await new Infrastructure.SqlServer.Payroll.SqlPayrollStore(h.Db.Platform.Connections).OpenReadAsync(Ct);
        return (await r.RunLinesAsync(runId, Ct)).Single(l => l.StaffId == h.S(staff));
    }

    private async Task<string?> MarkAsync(Hotel h, string staff, string date)
        => (string?)await db.ScalarAsync("SELECT status FROM hr.attendance WHERE staff_id = @s AND work_date = @d;", ("@s", h.S(staff)), ("@d", date));

    private async Task<Hotel> GoldenWithAugustLockedAsync()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        await LockAsync(h, (await PrepareAsync(h, "2026-08")).RunId);
        return h;
    }

    [Fact]
    public async Task DEC075_a_correction_that_lowers_pay_applies_at_once_audited_and_the_September_draft_is_recalculated()
    {
        var h = await GoldenWithAugustLockedAsync();
        var sep = await PrepareAsync(h, "2026-09");
        Assert.Equal(1_680_000, (await LineAsync(h, sep.RunId, "HR-S02")).Earned);   // golden: Aarav ₹18,000, payable 28 of 30 days
        // Thu 3 Sep: Aarav was marked present; the register shows he was absent. ₹18,000 × 54 / 60 = ₹16,200.00 → −₹600.00: applies at once.
        var r = (await CorrectAsync(h, "manager", "HR-S02", "2026-09-03", "A")).Value;
        Assert.Equal(("Applied", false, "Present", "Absent"), (r.Status, r.RaisesPay, r.Before, r.After));
        Assert.Contains("September 2026 draft pay-run recalculated", r.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("₹", r.Notes, StringComparison.Ordinal);   // the Manager sees no money
        Assert.Equal("A", await MarkAsync(h, "HR-S02", "2026-09-03"));
        var line = await LineAsync(h, sep.RunId, "HR-S02");
        Assert.Equal((27m, 1_620_000L), (line.PayableDays, line.Earned));
        Assert.Equal(("Approved", true, h.U("manager"), -60_000L), ((string)(await db.ScalarAsync("SELECT status FROM hr.change_request WHERE request_id = @r;", ("@r", r.RequestId)))!,
            (bool)(await db.ScalarAsync("SELECT direct FROM hr.change_request WHERE request_id = @r;", ("@r", r.RequestId)))!,
            (int)(await db.ScalarAsync("SELECT decided_by FROM hr.change_request WHERE request_id = @r;", ("@r", r.RequestId)))!,
            await db.LongAsync("SELECT pay_effect_paise FROM hr.change_request WHERE request_id = @r;", ("@r", r.RequestId))));
        var audit = (string)(await db.ScalarAsync("SELECT detail FROM plat.audit_event WHERE property_id = @p AND action = 'HR_ATTENDANCE_CORRECTED';", ("@p", h.PropertyId)))!;
        Assert.Contains("Aarav Joshi · 2026-09-03 · Present → Absent · lowers gross pay by ₹600.00 · reason: Register checked (fictional)", audit, StringComparison.Ordinal);
        // The recalculated draft is current: the other payroll user locks it without a "recalculate" refusal; the preparer is still the maker.
        await Hotel.FailsAsync(() => LockAsync(h, sep.RunId, "accountant"), HccErrorCode.FORBIDDEN);
        Assert.Equal("Locked", (await LockAsync(h, sep.RunId)).Status);
        // September is now locked: never changed again.
        var locked = await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-09-04", "A"), HccErrorCode.INVALID_TRANSITION);
        Assert.Contains("locked", locked.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DEC075_a_correction_that_raises_pay_waits_for_the_Owner_blocks_the_lock_and_the_approval_recalculates()
    {
        var h = await GoldenWithAugustLockedAsync();
        var sep = await PrepareAsync(h, "2026-09");
        var before = await LineAsync(h, sep.RunId, "HR-S05");
        Assert.Equal((28.5m, 1_087_662L), (before.PayableDays, before.Net));   // golden Ravi September: net ₹10,876.62
        // Tue 22 Sep: Ravi marked absent but was on duty. Absent → present raises pay → a request; nothing changes until the Owner approves.
        var r = (await CorrectAsync(h, "manager", "HR-S05", "2026-09-22", "P", reason: "Duty register shows him on shift")).Value;
        Assert.Equal(("PendingApproval", true), (r.Status, r.RaisesPay));
        Assert.Equal("A", await MarkAsync(h, "HR-S05", "2026-09-22"));
        Assert.Equal(before, await LineAsync(h, sep.RunId, "HR-S05"));
        // ₹13,500: payable 28.5 → 29.5 days → earned 13,500 × 59 / 60 = ₹13,275.00 (was ₹12,825.00) → gross +₹450.00.
        Assert.Equal(45_000, await db.LongAsync("SELECT pay_effect_paise FROM hr.change_request WHERE request_id = @r;", ("@r", r.RequestId)));
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S05", "2026-09-22", "L"), HccErrorCode.NOT_AVAILABLE);   // one request per day
        var blocked = await Hotel.FailsAsync(() => LockAsync(h, sep.RunId), HccErrorCode.INVALID_TRANSITION);
        Assert.Contains("1 attendance correction for September 2026 waits for the Owner", blocked.Message, StringComparison.Ordinal);
        // The board: the Manager sees the request without money; the Owner sees the pay effect.
        var mgr = (await h.Queries.GetBoardAsync(h.PropertyId, h.U("manager"), null, null, Ct)).Requests.Single(x => x.RequestId == r.RequestId);
        Assert.Equal(("Pending", "mark", "A", "P", (long?)null), (mgr.Status, mgr.Before!.Kind, mgr.Before.Status, mgr.After!.Status, mgr.PayEffectPaise));
        var own = await h.Queries.GetBoardAsync(h.PropertyId, h.U("owner"), null, null, Ct);
        Assert.Equal(45_000, own.Requests.Single(x => x.RequestId == r.RequestId).PayEffectPaise);
        Assert.Equal((D("2026-09-01"), D("2026-10-23")), (own.CorrectFrom!.Value, own.CorrectTo!.Value));
        Assert.Empty((await h.Queries.GetBoardAsync(h.PropertyId, h.U("housekeeper"), null, null, Ct)).Requests);
        // Only the Owner decides (PERM-HR-009), never the requester.
        await Hotel.FailsAsync(() => DecideAsync(h, "manager", r.RequestId, true), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => DecideAsync(h, "accountant", r.RequestId, true), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => DecideAsync(h, "owner", r.RequestId, false), HccErrorCode.VALIDATION);   // a rejection needs a reason
        var ok = (await DecideAsync(h, "owner", r.RequestId, true, "Checked with the supervisor")).Value;
        Assert.Equal("Approved", ok.Status);
        Assert.Contains("September 2026 draft pay-run recalculated", ok.Notes, StringComparison.Ordinal);
        Assert.Equal("P", await MarkAsync(h, "HR-S05", "2026-09-22"));
        Assert.Equal(h.U("manager"), (int)(await db.ScalarAsync("SELECT marked_by FROM hr.attendance WHERE staff_id = @s AND work_date = '2026-09-22';", ("@s", h.S("HR-S05"))))!);
        // Ravi's recalculated line by hand: earned ₹13,275.00 + OT 120 min (13,500 ÷ 30 ÷ 8 × 2 h = ₹112.50) = gross ₹13,387.50; basic 50 % ₹6,637.50 →
        // PF 12 % ₹796.50; washing 10 % of basic ₹663.75 → ESIC 0.75 % of (13,275.00 − 663.75 = 12,611.25) = ₹94.58; PT ₹200; advance instalment ₹1,000
        // → net 13,387.50 − 796.50 − 94.58 − 200 − 1,000 = ₹11,296.42 (= golden ₹10,876.62 + ₹419.80).
        var after = await LineAsync(h, sep.RunId, "HR-S05");
        Assert.Equal((29.5m, 1_327_500L, 1_338_750L, 79_650L, 9_458L, 20_000L, 100_000L, 1_129_642L),
            (after.PayableDays, after.Earned, after.Gross, after.Pf, after.Esic, after.Pt, after.Advance, after.Net));
        Assert.Contains("raises gross pay by ₹450.00 · request", (string)(await db.ScalarAsync(
            "SELECT detail FROM plat.audit_event WHERE property_id = @p AND action = 'HR_ATTENDANCE_CORRECTION_APPROVED';", ("@p", h.PropertyId)))!, StringComparison.Ordinal);
        Assert.Equal("Locked", (await LockAsync(h, sep.RunId)).Status);   // decided → the lock goes ahead (prepared by the Accountant, locked by the Owner)
        await Hotel.FailsAsync(() => DecideAsync(h, "owner", r.RequestId, true), HccErrorCode.INVALID_TRANSITION);   // no longer pending
    }

    [Fact]
    public async Task DEC075_refuses_locked_month_future_today_outside_the_open_month_own_record_and_staff_without_rights()
    {
        var h = await GoldenWithAugustLockedAsync();
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-08-12", "P"), HccErrorCode.INVALID_TRANSITION);   // August is locked
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-10-25", "P"), HccErrorCode.INVALID_TRANSITION);   // future
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-10-24", "P"), HccErrorCode.INVALID_TRANSITION);   // today: MarkAttendance
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-07-15", "P"), HccErrorCode.INVALID_TRANSITION);   // before the open month
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S01", "2026-09-03", "A"), HccErrorCode.FORBIDDEN);            // the Manager's own record
        await Hotel.FailsAsync(() => CorrectAsync(h, "accountant", "HR-S02", "2026-09-03", "A"), HccErrorCode.FORBIDDEN);         // payroll, not attendance
        await Hotel.FailsAsync(() => CorrectAsync(h, "housekeeper", "HR-S02", "2026-09-03", "A"), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-09-03", "A", reason: "x"), HccErrorCode.VALIDATION); // reason required
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-09-03", "A", ot: 30), HccErrorCode.VALIDATION);     // absent + overtime
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S02", "2026-09-03", "P"), HccErrorCode.VALIDATION);             // already present
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S05", "2026-09-14", "P"), HccErrorCode.INVALID_TRANSITION);   // inside a 2-day approved CL
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM hr.change_request WHERE property_id = @p;", ("@p", h.PropertyId)));
    }

    [Fact]
    public async Task DEC075_back_dated_leave_paid_leave_waits_for_the_Owner_leave_without_pay_applies_and_the_leave_rules_hold()
    {
        var h = await GoldenWithAugustLockedAsync();
        // Wed 16 Sep, Kavya absent → casual leave: absent loses 2 half-days, paid leave none → ₹13,500 × 2 / 60 = +₹450.00 → the Owner approves.
        var cl = (await CorrectAsync(h, "manager", "HR-S06", "2026-09-16", "CL", reason: "Leave form found (fictional)")).Value;
        Assert.Equal(("PendingApproval", "Absent", "Casual leave"), (cl.Status, cl.Before, cl.After));
        Assert.Equal(45_000, await db.LongAsync("SELECT pay_effect_paise FROM hr.change_request WHERE request_id = @r;", ("@r", cl.RequestId)));
        await DecideAsync(h, "owner", cl.RequestId, true);
        Assert.Null(await MarkAsync(h, "HR-S06", "2026-09-16"));   // the absent mark is replaced by the leave
        Assert.Equal(("CL", "Approved", h.U("manager"), h.U("owner"), cl.RequestId, 2L), await LeaveOfAsync(h, "HR-S06", "2026-09-16"));
        // Balance: CL 12 a year − 2 pending (28–29 Oct) − this day = 9 left.
        Assert.Equal(9m, (await h.Queries.GetLeaveBalanceAsync(h.PropertyId, h.U("owner"), h.S("HR-S06"), D("2026-10-24"), Ct)).Single(b => b.LeaveType == "CL").Left);
        // Mon 28 Sep, Neha absent → leave without pay: both lose the day → no pay change → applies at once; the manager is maker and decider of a
        // direct correction (CK_leave_not_self allows it through correction_id).
        var lop = (await CorrectAsync(h, "manager", "HR-S04", "2026-09-28", "LOP")).Value;
        Assert.Equal(("Applied", false), (lop.Status, lop.RaisesPay));
        Assert.Equal(("LOP", "Approved", h.U("manager"), h.U("manager"), lop.RequestId, 2L), await LeaveOfAsync(h, "HR-S04", "2026-09-28"));
        // Back to a mark: the one-day leave is cancelled and the day holds the mark again (lowers pay? LOP → absent: no change → applies).
        Assert.Equal("Applied", (await CorrectAsync(h, "manager", "HR-S04", "2026-09-28", "A")).Value.Status);
        Assert.Equal("Cancelled", (string)(await db.ScalarAsync("SELECT status FROM hr.leave_request WHERE correction_id = @c;", ("@c", lop.RequestId)))!);
        Assert.Equal("A", await MarkAsync(h, "HR-S04", "2026-09-28"));
        // Rules: daily-wage staff take leave without pay only; no leave on a weekly off; a pending leave covering the day is decided first.
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S10", "2026-09-14", "CL"), HccErrorCode.VALIDATION);
        await Hotel.FailsAsync(() => CorrectAsync(h, "manager", "HR-S06", "2026-09-21", "SL"), HccErrorCode.VALIDATION);   // Monday = Kavya's weekly off
        await Hotel.FailsAsync(() => h.Payroll.RequestLeaveAsync(h.As("manager", PayrollService.OpRequestLeave),
            new RequestLeaveRequest(h.S("HR-S06"), "CL", D("2026-09-30"), null, null, "Back-dated by request"), Ct), HccErrorCode.VALIDATION);   // RequestLeave stays forward-only
        // The requester may withdraw a pending request; nobody else.
        var w = (await CorrectAsync(h, "manager", "HR-S06", "2026-09-30", "OFF", ot: 60)).Value;
        await Hotel.FailsAsync(() => h.Payroll.WithdrawPayrollRequestAsync(h.As("owner", PayrollService.OpWithdrawPayrollRequest),
            new WithdrawPayrollRequestRequest(w.RequestId, w.Version), Ct), HccErrorCode.FORBIDDEN);
        Assert.Equal("Withdrawn", (await h.Payroll.WithdrawPayrollRequestAsync(h.As("manager", PayrollService.OpWithdrawPayrollRequest),
            new WithdrawPayrollRequestRequest(w.RequestId, w.Version), Ct)).Value.Status);
    }

    private async Task<(string, string, int, int, long, long)> LeaveOfAsync(Hotel h, string staff, string date)
    {
        await using var conn = await db.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT leave_type, status, requested_by, decided_by, correction_id, days_x2 FROM hr.leave_request
            WHERE staff_id = @s AND from_date = @d AND to_date = @d AND correction_id IS NOT NULL AND status = 'Approved';
            """, conn);
        cmd.Parameters.AddWithValue("@s", h.S(staff));
        cmd.Parameters.AddWithValue("@d", date);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        Assert.True(await r.ReadAsync(Ct));
        return (r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4), r.GetInt16(5));
    }

    [Fact]
    public async Task DEC083_paid_holiday_is_paid_like_a_weekly_off_for_monthly_staff_and_like_weekly_offs_for_daily_wage()
    {
        var h = await db.NewHotelAsync("2026-10-05");
        var monthly = h.Staff["M"] = await h.AddStaffAsync("Holiday Monthly (fictional)", 3_000_000, "FO", weeklyOff: 0);
        var daily = h.Staff["D"] = await h.AddStaffAsync("Holiday Daily (fictional)", 0, "KIT", weeklyOff: 0, payType: "daily", daily: 80_000);
        // Present on every September working day (Sundays 6, 13, 20, 27 off) except Thu 17 Sep, the holiday.
        var sql = new StringBuilder("INSERT hr.attendance(staff_id, work_date, status, ot_minutes, marked_by, marked_utc) VALUES ");
        var rows = new List<string>();
        foreach (var d in new PayMonth(2026, 9).Dates().Where(d => d.DayOfWeek != DayOfWeek.Sunday && d.Day != 17))
            foreach (var s in new[] { monthly, daily })
                rows.Add(string.Create(CultureInfo.InvariantCulture, $"({s}, '{d:yyyy-MM-dd}', 'P', 0, {h.U("manager")}, '2026-10-01')"));
        await db.ScalarAsync(sql.AppendJoin(",", rows).ToString());
        async Task<HolidayResult> Save(string user) => (await h.Payroll.SaveHolidayAsync(h.As(user, PayrollService.OpSaveHoliday),
            new SaveHolidayRequest(D("2026-09-17"), "Founders’ Day (fictional)"), Ct)).Value;
        await Hotel.FailsAsync(() => Save("manager"), HccErrorCode.FORBIDDEN);   // Owner only (PERM-HR-009)
        await Hotel.FailsAsync(() => Save("accountant"), HccErrorCode.FORBIDDEN);
        Assert.Contains("added · paid like a weekly off", (await Save("owner")).Notes, StringComparison.Ordinal);

        var sep = await PrepareAsync(h, "2026-09");
        Assert.Empty(sep.Warnings);   // the holiday is not an unmarked day
        // Monthly ₹30,000: 25 present + 4 weekly offs + 1 holiday = 30 of 30 days → earned ₹30,000.00; PF 12 % of basic ₹15,000.00 = ₹1,800.00;
        // ESIC: contract ₹30,000 > ₹21,000 → none; PT ₹200.00 → net ₹28,000.00.
        var m = await LineAsync(h, sep.RunId, "M");
        Assert.Equal((25m, 4m, 1m, 30m, 3_000_000L, 180_000L, 0L, 20_000L, 2_800_000L), (m.Counts.P, m.Counts.Off, m.Counts.Hol, m.PayableDays, m.Earned, m.Pf, m.Esic, m.Pt, m.Net));
        // Daily ₹800: 25 days worked × 800 = ₹20,000.00 (the holiday, like a weekly off, is not paid — CA A04-04 open); ESIC covered (800 × 26 = ₹20,800)
        // 0.75 % = ₹150.00; PT ₹200.00 → net ₹19,650.00.
        var dl = await LineAsync(h, sep.RunId, "D");
        Assert.Equal((1m, 25m, 2_000_000L, 15_000L, 20_000L, 1_965_000L), (dl.Counts.Hol, dl.PayableDays, dl.Earned, dl.Esic, dl.Pt, dl.Net));
        var board = await h.Queries.GetBoardAsync(h.PropertyId, h.U("housekeeper"), null, null, Ct);   // everyone sees the calendar
        Assert.Equal([new HolidayView(D("2026-09-17"), "Founders’ Day (fictional)")], board.Holidays);

        // Removing it: the draft is stale (the lock refuses it); recalculated, the day is unmarked = absent → ₹30,000 × 58 / 60 = ₹29,000.00, with the hint.
        var removed = (await h.Payroll.RemoveHolidayAsync(h.As("owner", PayrollService.OpRemoveHoliday), new RemoveHolidayRequest(D("2026-09-17")), Ct)).Value;
        Assert.Contains("September 2026 draft pay-run must be recalculated", removed.Notes, StringComparison.Ordinal);
        await Hotel.FailsAsync(() => LockAsync(h, sep.RunId), HccErrorCode.STALE_VERSION);
        sep = await PrepareAsync(h, "2026-09");
        Assert.Equal(2_900_000, (await LineAsync(h, sep.RunId, "M")).Earned);
        Assert.Contains(sep.Warnings, w => w.Contains("1 unmarked day counted as absent — a manager can correct them on Staff & payroll › Attendance › Past days", StringComparison.Ordinal));
        await Hotel.FailsAsync(() => h.Payroll.RemoveHolidayAsync(h.As("owner", PayrollService.OpRemoveHoliday), new RemoveHolidayRequest(D("2026-09-17")), Ct), HccErrorCode.NOT_FOUND);
        // Back on the calendar, recalculated and locked: September's holidays can no longer change.
        await Save("owner");
        sep = await PrepareAsync(h, "2026-09");
        Assert.Equal(3_000_000, (await LineAsync(h, sep.RunId, "M")).Earned);
        await LockAsync(h, sep.RunId);
        await Hotel.FailsAsync(() => Save("owner"), HccErrorCode.INVALID_TRANSITION);
        Assert.Equal(3, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND action IN ('HR_HOLIDAY_SAVED','HR_HOLIDAY_REMOVED');", ("@p", h.PropertyId)));
    }

    [Fact]
    public async Task DEC084_bank_change_waits_for_the_Owner_payments_keep_the_old_details_and_the_Owners_own_change_applies_audited()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var ravi = h.S("HR-S05");
        var old = (string)(await db.ScalarAsync("SELECT account_no FROM hr.staff WHERE staff_id = @s;", ("@s", ravi)))!;
        SaveStaffRequest Ravi(string version, string account, string? why = null) => new(ravi, version, "Ravi Shinde", "HK", "Room attendant", "M", 5, LinkUserId: h.U("housekeeper"),
            AccountNo: account, Ifsc: "DEMO0000999", BankReason: why);
        var save = (await h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), Ravi(await h.StaffVersionAsync(ravi), "999999999999", "New account (fictional)"), Ct)).Value;
        Assert.NotNull(save.BankRequestId);
        Assert.Contains("bank details sent to the Owner for approval", save.Notes, StringComparison.Ordinal);
        Assert.Equal(old, (string)(await db.ScalarAsync("SELECT account_no FROM hr.staff WHERE staff_id = @s;", ("@s", ravi)))!);   // not applied
        var audit = (string)(await db.ScalarAsync("SELECT STRING_AGG(detail, '|') FROM plat.audit_event WHERE property_id = @p;", ("@p", h.PropertyId)))!;
        Assert.Contains($"bank Demo Co-operative Bank (fictional) ••••{old[^4..]}", audit, StringComparison.Ordinal);
        Assert.Contains("→ Demo Co-operative Bank (fictional) ••••9999 DEMO0000999 · waiting for the Owner", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("999999999999", audit, StringComparison.Ordinal);
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), Ravi(await h.StaffVersionAsync(ravi), "888888888888"), Ct),
            HccErrorCode.INVALID_TRANSITION);   // one pending change per staff member
        // Meanwhile August is locked and paid: the bank file carries the OLD account.
        var aug = await LockAsync(h, (await PrepareAsync(h, "2026-08")).RunId);
        await h.Payroll.IssueBankFileAsync(h.As("accountant", PayrollService.OpIssueBankFile), new IssueBankFileRequest(aug.RunId), Ct);
        var csv = (await h.Queries.BankCsvAsync(h.PropertyId, h.U("accountant"), aug.RunId, 1, Ct)).Content;
        Assert.Contains("'" + old, csv, StringComparison.Ordinal);
        Assert.DoesNotContain("999999999999", csv, StringComparison.Ordinal);
        // Payroll roles see the request (masked); the Manager does not. Only the Owner approves.
        var acc = (await h.Queries.GetBoardAsync(h.PropertyId, h.U("accountant"), null, null, Ct)).Requests.Single(x => x.RequestId == save.BankRequestId);
        Assert.Equal(("bank", "Pending", "••••9999", "DEMO0000999"), (acc.Kind, acc.Status, acc.BankAfter!.AccountMasked, acc.BankAfter.Ifsc));
        Assert.Empty((await h.Queries.GetBoardAsync(h.PropertyId, h.U("manager"), null, null, Ct)).Requests);
        await Hotel.FailsAsync(() => DecideAsync(h, "accountant", save.BankRequestId!.Value, true), HccErrorCode.FORBIDDEN);
        Assert.Equal("Approved", (await DecideAsync(h, "owner", save.BankRequestId!.Value, true)).Value.Status);
        Assert.Equal(("999999999999", "DEMO0000999"), ((string)(await db.ScalarAsync("SELECT account_no FROM hr.staff WHERE staff_id = @s;", ("@s", ravi)))!,
            (string)(await db.ScalarAsync("SELECT ifsc FROM hr.staff WHERE staff_id = @s;", ("@s", ravi)))!));
        Assert.Contains("'" + old, (await h.Queries.BankCsvAsync(h.PropertyId, h.U("accountant"), aug.RunId, 1, Ct)).Content, StringComparison.Ordinal);   // the locked run stays frozen
        // A second change rejected by the Owner leaves the approved details in place.
        var again = (await h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), Ravi(await h.StaffVersionAsync(ravi), "777777777777"), Ct)).Value;
        await Hotel.FailsAsync(() => DecideAsync(h, "owner", again.BankRequestId!.Value, false), HccErrorCode.VALIDATION);
        Assert.Equal("Rejected", (await DecideAsync(h, "owner", again.BankRequestId!.Value, false, "Not the staff member’s account")).Value.Status);
        Assert.Equal("999999999999", (string)(await db.ScalarAsync("SELECT account_no FROM hr.staff WHERE staff_id = @s;", ("@s", ravi)))!);
        // The Owner's own change applies at once (no request), audited with masked old → new; a new staff member's details by the Accountant wait too.
        var suresh = h.S("HR-S09");
        var direct = (await h.Payroll.SaveStaffAsync(h.As("owner", PayrollService.OpSaveStaff), new SaveStaffRequest(suresh, await h.StaffVersionAsync(suresh), "Suresh Pawar", "KIT", "Cook",
            "M", 3, AccountNo: "123412341234"), Ct)).Value;
        Assert.Null(direct.BankRequestId);
        Assert.Contains("→ Demo Co-operative Bank (fictional) ••••1234", direct.Notes, StringComparison.Ordinal);
        Assert.Equal("123412341234", (string)(await db.ScalarAsync("SELECT account_no FROM hr.staff WHERE staff_id = @s;", ("@s", suresh)))!);
        var added = (await h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), new SaveStaffRequest(null, null, "New Cook (fictional)", "KIT", "Cook", "M", 2,
            PayType: "monthly", MonthlyPaise: 1_500_000, BankName: "Demo Bank (fictional)", AccountNo: "555566667777", Ifsc: "DEMO0000102"), Ct)).Value;
        Assert.NotNull(added.BankRequestId);
        Assert.True(await db.ScalarAsync("SELECT account_no FROM hr.staff WHERE staff_id = @s;", ("@s", added.StaffId)) is DBNull);
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM hr.change_request WHERE property_id = @p AND kind = 'bank' AND status <> 'Pending';", ("@p", h.PropertyId)));
    }
}
