using Hcc.Application.Commands;
using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;
using Hcc.Payroll.Tests.Harness;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// SCC leave port (DEC-116) on SQL Server: the hotel's leave week-off set, per-staff entitlement overrides and SCC's balance confirm, blackouts and
/// store holidays by department, agenda items, the SCC backup import merge and Clear All (cancel with an audit record, locked months kept — DEC-119).
/// </summary>
[Trait("Category", "Sql")]
public sealed class LeaveSccSqlTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<CommandResult<LeaveRequestResult>> Req(Hotel h, string user, string staff, string from, string? to = null, string type = "CL", string? half = null,
        bool confirm = false, bool blackout = false) =>
        h.Payroll.RequestLeaveAsync(h.As(user, PayrollService.OpRequestLeave), new RequestLeaveRequest(h.S(staff), type, PayrollGolden.Date(from),
            to is null ? null : PayrollGolden.Date(to), half, null, h.U("manager"), confirm, blackout), Ct);

    private static DateOnly D(string s) => PayrollGolden.Date(s);

    private static async Task<LeaveBalanceView> ClAsync(Hotel h, string staff) =>
        (await h.Queries.GetLeaveBalanceAsync(h.PropertyId, h.U("owner"), h.S(staff), D("2026-10-24"), Ct)).Single(b => b.LeaveType == "CL");

    [Fact]
    public async Task Week_off_set_skips_dates_and_an_entitlement_override_moves_the_SCC_balance_confirm()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        // The Owner ticks Saturday + Sunday (SCC's own default; HCC starts with none).
        var set = (await h.Payroll.SetLeavePolicyAsync(h.As("owner", PayrollService.OpSetLeavePolicy), new SetLeavePolicyRequest(12, 7, 15, SccLeave.SccWeekOffDefault), Ct)).Value;
        Assert.Equal(65, set.WeekOffMask);
        Assert.Contains("week-off Sun + Sat", set.Notes, StringComparison.Ordinal);
        var r = (await Req(h, "housekeeper", "HR-S05", "2026-11-06", "2026-11-09")).Value;   // Fri – Mon
        Assert.Equal([D("2026-11-06"), D("2026-11-09")], r.Added.Select(x => x.From));
        Assert.Equal((2, "2 day(s) submitted — pending approval"), (r.WeekOff, r.Notes));
        var weekend = await Hotel.FailsAsync(() => Req(h, "housekeeper", "HR-S05", "2026-11-14", "2026-11-15"), HccErrorCode.NOT_AVAILABLE);
        Assert.Equal("2 day(s) skipped — weekend/weekly-off", weekend.Message);
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM hr.leave_request WHERE staff_id = @s AND request_from = '2026-11-06' AND request_to = '2026-11-09';", ("@s", h.S("HR-S05"))));

        // SCC ⚙ override (Owner only): Ravi CL 3 — he used 2 (approved Sep); the pending 6 / 9 Nov are not used, so 2 more exceed (2 + 2 > 3).
        await Hotel.FailsAsync(() => h.Payroll.SetLeaveEntOverrideAsync(h.As("manager", PayrollService.OpSetLeaveEntOverride), new SetLeaveEntOverrideRequest(h.S("HR-S05"), 3, null, null), Ct),
            HccErrorCode.FORBIDDEN);
        var ov = (await h.Payroll.SetLeaveEntOverrideAsync(h.As("owner", PayrollService.OpSetLeaveEntOverride), new SetLeaveEntOverrideRequest(h.S("HR-S05"), 3, null, null), Ct)).Value;
        Assert.Equal("Entitlement override saved for Ravi Shinde · CL 3", ov.Notes);
        Assert.Equal((3m, 2m, 1m), ((await ClAsync(h, "HR-S05")).Entitled, (await ClAsync(h, "HR-S05")).Used, (await ClAsync(h, "HR-S05")).Left));
        var over = await Hotel.FailsAsync(() => Req(h, "housekeeper", "HR-S05", "2026-11-10", "2026-11-11"), HccErrorCode.INVALID_TRANSITION);
        Assert.Equal("Ravi Shinde has used 2 of 3 Casual Leave day(s) this FY 2026–27.\nThis request adds 2 more and EXCEEDS the balance.\n\nRecord it anyway (it will still need approval)?",
            over.Message);
        Assert.Equal(2, (await Req(h, "housekeeper", "HR-S05", "2026-11-10", "2026-11-11", confirm: true)).Value.Added.Count);   // SCC: warns, never blocks
        Assert.Null(await SccNoteAsync(h, "HR-S06"));   // others keep the default
        var board = await h.Queries.GetBoardAsync(h.PropertyId, h.U("owner"), null, null, Ct);
        Assert.Equal((65, h.S("HR-S05"), 3m, (decimal?)null), (board.LeavePolicy.WeekOffMask, board.LeavePolicy.Overrides.Single().StaffId, board.LeavePolicy.Overrides.Single().Cl,
            board.LeavePolicy.Overrides.Single().Sl));
        var cleared = (await h.Payroll.SetLeaveEntOverrideAsync(h.As("owner", PayrollService.OpSetLeaveEntOverride), new SetLeaveEntOverrideRequest(h.S("HR-S05"), null, null, null), Ct)).Value;
        Assert.Equal("Ravi Shinde · Override cleared — using defaults", cleared.Notes);
        Assert.Equal(12m, (await ClAsync(h, "HR-S05")).Entitled);
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM hr.leave_ent_override WHERE property_id = @p;", ("@p", h.PropertyId)));
    }

    [Fact]
    public async Task Approval_rechecks_the_balance_two_pending_requests_over_the_quota_the_first_approves_the_second_needs_a_confirmation()
    {
        // DEC-122 (owner, 27 Oct 2026): SCC re-checks nothing at approval; HCC re-checks each date — approved days of its FY + this date over the quota.
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        await h.Payroll.SetLeaveEntOverrideAsync(h.As("owner", PayrollService.OpSetLeaveEntOverride), new SetLeaveEntOverrideRequest(h.S("HR-S05"), 3, null, null), Ct);
        Task<CommandResult<LeaveResult>> Decide(LeaveResult l, bool approve, bool confirm = false) =>
            h.Payroll.DecideLeaveAsync(h.As("owner", PayrollService.OpDecideLeave), new DecideLeaveRequest(l.LeaveId, l.Version, approve, null, confirm), Ct);
        async Task<string> StatusAsync(LeaveResult l) => (string)(await db.ScalarAsync("SELECT status FROM hr.leave_request WHERE leave_id = @l;", ("@l", l.LeaveId)))!;
        async Task<string> AuditAsync(LeaveResult l, string action) => (string)(await db.ScalarAsync(
            "SELECT detail FROM plat.audit_event WHERE property_id = @p AND action = @a AND target = @t;", ("@p", h.PropertyId), ("@a", action), ("@t", "hr.leave:" + l.LeaveId)))!;

        // Ravi: CL 3, 2 used (September). Two pending 1-day CL requests — each within the quota when recorded (pending is not used, SCC): no warning.
        var a = (await Req(h, "housekeeper", "HR-S05", "2026-12-01")).Value.Added.Single();
        var b = (await Req(h, "housekeeper", "HR-S05", "2026-12-08")).Value.Added.Single();
        Assert.Equal("Approved", (await Decide(a, true)).Value.Status);   // 2 + 1 = 3: approved silently
        Assert.DoesNotContain("over the quota", await AuditAsync(a, "HR_LEAVE_APPROVED"), StringComparison.Ordinal);
        const string Want = "Ravi Shinde has used 3 of 3 Casual Leave day(s) this FY 2026–27.\nThis request adds 1 more and EXCEEDS the balance.\n\nApprove it anyway?";
        Assert.Equal(Want, (await Hotel.FailsAsync(() => Decide(b, true), HccErrorCode.INVALID_TRANSITION)).Message);   // 3 + 1 > 3
        Assert.Equal("Pending", await StatusAsync(b));
        Assert.Equal("Approved", (await Decide(b, true, confirm: true)).Value.Status);   // confirmed: approved, over by 1
        Assert.EndsWith("Leave approved — now live for floor gate & payroll · over the quota (confirmed)", await AuditAsync(b, "HR_LEAVE_APPROVED"), StringComparison.Ordinal);
        var cl = await ClAsync(h, "HR-S05");
        Assert.Equal((3m, 4m, 0m, 1m), (cl.Entitled, cl.Used, cl.Left, cl.Over));

        // A half day adds 0.5; rejecting is never checked; the next FY starts again at the quota; LOP has no quota.
        var am = (await Req(h, "housekeeper", "HR-S05", "2026-12-15", half: "AM", confirm: true)).Value.Added.Single();
        Assert.Equal("Ravi Shinde has used 4 of 3 Casual Leave day(s) this FY 2026–27.\nThis request adds 0.5 more and EXCEEDS the balance.\n\nApprove it anyway?",
            (await Hotel.FailsAsync(() => Decide(am, true), HccErrorCode.INVALID_TRANSITION)).Message);
        Assert.Equal("Rejected", (await Decide(am, false)).Value.Status);
        var next = (await Req(h, "housekeeper", "HR-S05", "2027-04-05")).Value.Added.Single();
        Assert.Equal("Approved", (await Decide(next, true)).Value.Status);   // FY 2027–28: 0 + 1 ≤ 3
        var lop = (await Req(h, "housekeeper", "HR-S05", "2026-12-22", type: "LOP")).Value.Added.Single();
        Assert.Equal("Approved", (await Decide(lop, true)).Value.Status);
        // Another staff member keeps the default (CL 12): no warning.
        var other = (await Req(h, "reception", "HR-S02", "2026-12-08")).Value.Added.Single();
        Assert.Equal("Approved", (await Decide(other, true)).Value.Status);
    }

    private async Task<object?> SccNoteAsync(Hotel h, string staff) =>
        await db.ScalarAsync("SELECT cl_days FROM hr.leave_ent_override WHERE property_id = @p AND staff_id = @s;", ("@p", h.PropertyId), ("@s", h.S(staff)));

    [Fact]
    public async Task Blackouts_hard_refuse_soft_and_store_holidays_warn_until_confirmed_by_department()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        Task<CommandResult<LeaveBlackoutResult>> Add(string user, string from, string to, string dept, string kind, bool hard, string label) =>
            h.Payroll.AddLeaveBlackoutAsync(h.As(user, PayrollService.OpAddLeaveBlackout), new AddLeaveBlackoutRequest(D(from), D(to), dept, kind, hard, label), Ct);
        await Hotel.FailsAsync(() => Add("manager", "2026-12-24", "2026-12-26", "HK", "blackout", true, "Year end"), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => Add("owner", "2026-12-24", "2026-12-26", "ZZ", "blackout", true, "Year end"), HccErrorCode.VALIDATION);   // unknown department
        var hard = (await Add("owner", "2026-12-26", "2026-12-24", "HK", "blackout", true, "Year end")).Value;   // SCC: a swapped range is put right
        Assert.Equal((D("2026-12-24"), D("2026-12-26"), true), (hard.From, hard.To, hard.Hard));
        await Add("owner", "2026-12-28", "2026-12-28", "", "blackout", false, "Audit");
        var holiday = (await Add("owner", "2026-12-31", "2026-12-31", "FO", "holiday", true, "")).Value;
        Assert.False(holiday.Hard);   // a store holiday never blocks

        Assert.Equal("Leave falls in a hard blackout window (Year end) — blocked",
            (await Hotel.FailsAsync(() => Req(h, "housekeeper", "HR-S05", "2026-12-24"), HccErrorCode.NOT_AVAILABLE)).Message);
        Assert.Single((await Req(h, "reception", "HR-S02", "2026-12-24")).Value.Added);   // Front office: the HK blackout does not apply
        var soft = await Hotel.FailsAsync(() => Req(h, "housekeeper", "HR-S05", "2026-12-28"), HccErrorCode.INVALID_TRANSITION);
        Assert.Equal("These dates fall in a blackout window (Audit): Casual Leave leave is discouraged.\nRecord it anyway?", soft.Message);
        Assert.Single((await Req(h, "housekeeper", "HR-S05", "2026-12-28", blackout: true)).Value.Added);
        var store = await Hotel.FailsAsync(() => Req(h, "reception", "HR-S02", "2026-12-30", "2026-12-31", type: "SL"), HccErrorCode.INVALID_TRANSITION);
        Assert.Equal("These dates fall in a blackout window (Store holiday): Sick Leave leave is discouraged.\nRecord it anyway?", store.Message);
        Assert.Single((await Req(h, "housekeeper", "HR-S05", "2026-12-31")).Value.Added);   // Housekeeping: the FO store holiday does not apply

        var board = await h.Queries.GetBoardAsync(h.PropertyId, h.U("manager"), null, null, Ct);
        Assert.Equal(["Year end", "Audit", ""], board.LeavePolicy.Blackouts.OrderBy(b => b.From).Select(b => b.Label));
        await Hotel.FailsAsync(() => h.Payroll.RemoveLeaveBlackoutAsync(h.As("manager", PayrollService.OpRemoveLeaveBlackout), new RemoveLeaveBlackoutRequest(hard.BlackoutId), Ct),
            HccErrorCode.FORBIDDEN);
        Assert.Equal("Year end 2026-12-24 – 2026-12-26 · Blackout removed",
            (await h.Payroll.RemoveLeaveBlackoutAsync(h.As("owner", PayrollService.OpRemoveLeaveBlackout), new RemoveLeaveBlackoutRequest(hard.BlackoutId), Ct)).Value.Notes);
        Assert.Single((await Req(h, "housekeeper", "HR-S05", "2026-12-24")).Value.Added);
        await Hotel.FailsAsync(() => h.Payroll.RemoveLeaveBlackoutAsync(h.As("owner", PayrollService.OpRemoveLeaveBlackout), new RemoveLeaveBlackoutRequest(hard.BlackoutId), Ct),
            HccErrorCode.NOT_FOUND);
    }

    [Fact]
    public async Task Agenda_items_are_added_by_managers_listed_on_the_board_and_removed()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        Task<CommandResult<LeaveAgendaResult>> Add(string user, string title, string? time = "10:30", string? type = "meeting") =>
            h.Payroll.AddLeaveAgendaAsync(h.As(user, PayrollService.OpAddLeaveAgenda), new AddLeaveAgendaRequest(D("2026-11-05"), title, time, type, "Roster for December"), Ct);
        await Hotel.FailsAsync(() => Add("housekeeper", "Staff meeting"), HccErrorCode.FORBIDDEN);
        Assert.Equal("Date and title are required", (await Hotel.FailsAsync(() => Add("manager", "  "), HccErrorCode.VALIDATION)).Message);
        await Hotel.FailsAsync(() => Add("manager", "Staff meeting", "25:00"), HccErrorCode.VALIDATION);
        Assert.Equal("Invalid type", (await Hotel.FailsAsync(() => Add("manager", "Staff meeting", type: "party"), HccErrorCode.VALIDATION)).Message);
        var a = (await Add("manager", "Staff meeting")).Value;
        Assert.Equal("2026-11-05 10:30 · meeting · Staff meeting · Agenda item added", a.Notes);
        await Add("manager", "Fire drill", "", "reminder");
        var hk = await h.Queries.GetBoardAsync(h.PropertyId, h.U("housekeeper"), null, null, Ct);
        Assert.Equal([("Staff meeting", "10:30", "meeting"), ("Fire drill", "", "reminder")], hk.Agendas.Select(x => (x.Title, x.Time, x.Type)));
        Assert.Equal("2026-11-05 · Staff meeting · Agenda removed",
            (await h.Payroll.RemoveLeaveAgendaAsync(h.As("manager", PayrollService.OpRemoveLeaveAgenda), new RemoveLeaveAgendaRequest(a.AgendaId), Ct)).Value.Notes);
        await Hotel.FailsAsync(() => h.Payroll.RemoveLeaveAgendaAsync(h.As("manager", PayrollService.OpRemoveLeaveAgenda), new RemoveLeaveAgendaRequest(a.AgendaId), Ct), HccErrorCode.NOT_FOUND);
    }

    [Fact]
    public async Task Import_merges_an_SCC_backup_and_Clear_All_cancels_leave_keeping_locked_months_and_the_audit_record()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        foreach (var m in new[] { "2026-08", "2026-09" })
        {
            var run = (await h.Payroll.PreparePayRunAsync(h.As("accountant", PayrollService.OpPreparePayRun), new PreparePayRunRequest(m), Ct)).Value;
            await h.Payroll.LockPayRunAsync(h.As("owner", PayrollService.OpLockPayRun), new LockPayRunRequest(run.RunId, await h.RowVersionAsync("hr.pay_run", "run_id", run.RunId)), Ct);
        }

        ImportLeaveDay Day(string id, string staff, string date, string? half, string type, string status, bool legacy = false, int? approver = null, string? approverName = null) =>
            new(id, h.S(staff), D(date), half, type, "From the SCC backup", approver, approverName, D(date), D(date), status, legacy, legacy ? "Store Manager" : null, null);
        var file = new ImportLeaveRequest(1,
            [
                Day("a1", "HR-S05", "2026-11-16", null, "CL", "Approved", approver: h.U("owner")),
                Day("a2", "HR-S05", "2026-11-16", "AM", "SL", "Pending", approver: h.U("owner")),   // same person, a record not rejected → skipped (SCC)
                Day("a3", "HR-S06", "2026-09-21", null, "CL", "Approved", approver: h.U("owner")),  // a locked pay month → skipped (HCC)
                Day("a4", "HR-S04", "2026-11-17", "PM", "OTH", "Approved", legacy: true, approverName: "Store Manager (SCC)"),
            ],
            [new ImportLeaveAgenda("g1", D("2026-11-05"), "Staff meeting", "meeting", "10:30", null)],
            new ImportLeaveDefaults(10, null, null), [new ImportLeaveOverride(h.S("HR-S05"), null, 2, null)]);
        await Hotel.FailsAsync(() => h.Payroll.ImportLeaveAsync(h.As("manager", PayrollService.OpImportLeave), file, Ct), HccErrorCode.FORBIDDEN);
        var first = (await h.Payroll.ImportLeaveAsync(h.As("owner", PayrollService.OpImportLeave), file, Ct)).Value;
        Assert.Equal((2, 1, 3, "Imported: 2 leaves, 1 agendas. Skipped: 3"), (first.Added, first.Agendas, first.Skipped, first.Notes));
        var again = (await h.Payroll.ImportLeaveAsync(h.As("owner", PayrollService.OpImportLeave), file, Ct)).Value;   // the same ids are ignored
        Assert.Equal((0, 0, 3), (again.Added, again.Agendas, again.Skipped));
        var legacy = await db.ScalarAsync("SELECT CONCAT(status, '|', CAST(legacy AS INT), '|', approver_name, '|', ISNULL(CAST(approver_id AS VARCHAR(10)), '-'), '|', scc_id, '|', decided_by_name) FROM hr.leave_request WHERE property_id = @p AND scc_id = 'a4';",
            ("@p", h.PropertyId));
        Assert.Equal("Approved|1|Store Manager (SCC)|-|a4|Store Manager", legacy);
        var board = await h.Queries.GetBoardAsync(h.PropertyId, h.U("owner"), null, null, Ct);
        Assert.Equal((10m, 7m, 2m), (board.LeavePolicy.Cl, board.LeavePolicy.Sl, board.LeavePolicy.Overrides.Single().Sl));
        Assert.Single(board.Agendas);
        Assert.Contains(board.Leaves, l => l.SccId == "a4" && l.Legacy && l.ApproverName == "Store Manager (SCC)" && l.Days == 0.5m);

        // Clear All (SCC executeClearData): type DELETE; Owner only; leave is CANCELLED with an audit record (DEC-119), never deleted.
        await Hotel.FailsAsync(() => h.Payroll.ClearLeaveAsync(h.As("owner", PayrollService.OpClearLeave), new ClearLeaveRequest("delete"), Ct), HccErrorCode.VALIDATION);
        await Hotel.FailsAsync(() => h.Payroll.ClearLeaveAsync(h.As("manager", PayrollService.OpClearLeave), new ClearLeaveRequest("DELETE"), Ct), HccErrorCode.FORBIDDEN);
        var rows = await db.LongAsync("SELECT COUNT(*) FROM hr.leave_request WHERE property_id = @p;", ("@p", h.PropertyId));
        var clear = (await h.Payroll.ClearLeaveAsync(h.As("owner", PayrollService.OpClearLeave), new ClearLeaveRequest("DELETE"), Ct)).Value;
        // The golden 8 records + 2 imported; the 2 September records (HR-LV-1, locked month) stay.
        Assert.Equal((8, 2, 1), (clear.Cancelled, clear.Kept, clear.Agendas));
        Assert.Equal(rows, await db.LongAsync("SELECT COUNT(*) FROM hr.leave_request WHERE property_id = @p;", ("@p", h.PropertyId)));
        Assert.Equal(2, await db.LongAsync("SELECT COUNT(*) FROM hr.leave_request WHERE property_id = @p AND status = 'Approved' AND from_date BETWEEN '2026-09-01' AND '2026-09-30';",
            ("@p", h.PropertyId)));
        Assert.Equal(8, await db.LongAsync("SELECT COUNT(*) FROM hr.leave_request WHERE property_id = @p AND status = 'Cancelled' AND cancelled_by = @o AND note = N'Clear All';",
            ("@p", h.PropertyId), ("@o", h.U("owner"))));
        Assert.Equal(0, await db.LongAsync("SELECT COUNT(*) FROM hr.leave_agenda WHERE property_id = @p;", ("@p", h.PropertyId)));
        var after = await h.Queries.GetBoardAsync(h.PropertyId, h.U("owner"), null, null, Ct);
        Assert.Equal((12m, 7m, 15m, 0), (after.LeavePolicy.Cl, after.LeavePolicy.Sl, after.LeavePolicy.El, after.LeavePolicy.Overrides.Count));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND action = 'HR_LEAVE_CLEARED';", ("@p", h.PropertyId)));
    }
}
