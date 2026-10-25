using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Payroll;
using Hcc.Payroll.Tests.Harness;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// Payroll and leave commands on SQL Server with the real P1 policy (0101 + 0901 grants). Every test runs in its own hotel (property + users).
/// Names follow the review findings they hold (W05B_Payroll_Code_Review_2026-10-25.md).
/// </summary>
[Trait("Category", "Sql")]
public sealed class PayrollSqlTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<PayRunResult> PrepareAsync(Hotel h, string month, string user = "accountant")
        => (await h.Payroll.PreparePayRunAsync(h.As(user, PayrollService.OpPreparePayRun), new PreparePayRunRequest(month), Ct)).Value;

    private static async Task<PayRunResult> LockAsync(Hotel h, PayRunResult run, string user = "owner")
        => (await h.Payroll.LockPayRunAsync(h.As(user, PayrollService.OpLockPayRun), new LockPayRunRequest(run.RunId, run.Version), Ct)).Value;

    private static async Task<IReadOnlyList<PayLine>> LinesAsync(Hotel h, long runId)
    {
        await using var r = await new Infrastructure.SqlServer.Payroll.SqlPayrollStore(h.Db.Platform.Connections).OpenReadAsync(Ct);
        return await r.RunLinesAsync(runId, Ct);
    }

    [Fact]
    public async Task Golden_BASE_august_locked_then_september_match_the_prototype_through_SQL()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var g = PayrollGolden.Data;

        var aug = await PrepareAsync(h, "2026-08");
        Assert.Equal((12, 21_210_166L), (aug.Staff, aug.NetPaise));
        foreach (var (gl, l) in g.August.Lines.Zip(await LinesAsync(h, aug.RunId))) PayrollGolden.AssertLine(gl, l);
        Assert.Equal(g.August.Warnings, aug.Warnings);
        var locked = await LockAsync(h, aug);
        Assert.Equal("Locked", locked.Status);
        Assert.Equal(100_000, await db.LongAsync("SELECT recovered_paise FROM hr.advance WHERE staff_id = @s;", ("@s", h.S("HR-S05"))));   // Ravi ₹1,000 of ₹3,000
        Assert.Equal(12, await db.LongAsync("SELECT COUNT(*) FROM hr.pay_run_bank WHERE run_id = @r AND lock_seq = 1;", ("@r", aug.RunId)));
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'PayRunLocked' AND aggregate_id = @r;", ("@r", aug.RunId)));

        var sep = await PrepareAsync(h, "2026-09");
        var lines = await LinesAsync(h, sep.RunId);
        foreach (var (gl, l) in g.September.Lines.Zip(lines)) PayrollGolden.AssertLine(gl, l);
        Assert.Equal(g.September.Warnings, sep.Warnings);
        Assert.Equal(20_888_900, sep.NetPaise);   // ₹2,08,889.00
        Assert.False(lines.Single(l => l.StaffId == h.S("HR-S04")).EsicCovered);   // Neha ₹22,000: not covered for Apr–Sep (F-07)

        var csv = await h.Queries.StatutoryCsvAsync(h.PropertyId, h.U("accountant"), new PayMonth(2026, 9), Ct);
        Assert.Contains("DRAFT — NOT FINAL", csv.Content, StringComparison.Ordinal);
        var reg = await h.Queries.RegisterCsvAsync(h.PropertyId, h.U("owner"), new PayMonth(2026, 8), Ct);
        Assert.Contains("Locked run", reg.Content, StringComparison.Ordinal);
        Assert.Contains(",Housekeeping,", reg.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lock_is_maker_checker_and_refuses_a_stale_draft()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        await Hotel.FailsAsync(() => PrepareAsync(h, "2026-08", "manager"), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => PrepareAsync(h, "2026-10"), HccErrorCode.INVALID_TRANSITION);   // month not complete
        var aug = await PrepareAsync(h, "2026-08");
        await Hotel.FailsAsync(() => LockAsync(h, aug, "accountant"), HccErrorCode.FORBIDDEN);       // preparer cannot lock
        await Hotel.FailsAsync(() => LockAsync(h, aug, "manager"), HccErrorCode.FORBIDDEN);
        // Owner changes August's PF after the draft: the lock must refuse the stale draft.
        var rates = await h.Payroll.SetStatutoryRatesAsync(h.As("owner", PayrollService.OpSetStatutoryRates), new SetStatutoryRatesRequest(new DateOnly(2026, 8, 1), PfEeBps: 1000), Ct);
        Assert.Equal(["2026-08", "2026-09"], rates.Value.CompletedMonthsToRecalculate);
        await Hotel.FailsAsync(() => LockAsync(h, aug), HccErrorCode.STALE_VERSION);
        var again = await PrepareAsync(h, "2026-08");
        await Hotel.FailsAsync(() => LockAsync(h, aug with { Version = aug.Version }), HccErrorCode.STALE_VERSION);   // old expectedVersion
        var done = await LockAsync(h, again);
        Assert.Equal("Locked", done.Status);
        await Hotel.FailsAsync(() => PrepareAsync(h, "2026-08"), HccErrorCode.INVALID_TRANSITION);   // locked = immutable
        // Rates for a locked month are frozen.
        await Hotel.FailsAsync(() => h.Payroll.SetStatutoryRatesAsync(h.As("owner", PayrollService.OpSetStatutoryRates), new SetStatutoryRatesRequest(new DateOnly(2026, 8, 1), PfEeBps: 1100), Ct),
            HccErrorCode.INVALID_TRANSITION);
        await Hotel.FailsAsync(() => h.Payroll.SetStatutoryRatesAsync(h.As("accountant", PayrollService.OpSetStatutoryRates), new SetStatutoryRatesRequest(PfEeBps: 1100), Ct),
            HccErrorCode.FORBIDDEN);
    }

    [Fact]
    public async Task F02_bank_file_uses_the_details_frozen_at_lock_and_F09_unlock_supersedes_files_and_payslips()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var aug = await LockAsync(h, await PrepareAsync(h, "2026-08"));
        var ravi = h.S("HR-S05");
        var old = (string)(await db.ScalarAsync("SELECT account_no FROM hr.staff WHERE staff_id = @s;", ("@s", ravi)))!;
        // Accountant edits Ravi's bank account after the lock.
        var save = await h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), new SaveStaffRequest(ravi, await h.StaffVersionAsync(ravi), "Ravi Shinde", "HK",
            "Room attendant", "M", 5, LinkUserId: h.U("housekeeper"), AccountNo: "999999999999", Ifsc: "EVIL0000001"), Ct);
        Assert.Contains("••••9999", save.Value.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("999999999999", save.Value.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("999999999999", (string)(await db.ScalarAsync("SELECT STRING_AGG(detail, '|') FROM plat.audit_event;"))!, StringComparison.Ordinal);

        var file = await h.Payroll.IssueBankFileAsync(h.As("accountant", PayrollService.OpIssueBankFile), new IssueBankFileRequest(aug.RunId), Ct);
        Assert.Equal((1, aug.NetPaise), (file.Value.IssueNo, file.Value.NetPaise));
        var csv = (await h.Queries.BankCsvAsync(h.PropertyId, h.U("accountant"), aug.RunId, 1, Ct)).Content;
        Assert.Contains("'" + old, csv, StringComparison.Ordinal);
        Assert.DoesNotContain("999999999999", csv, StringComparison.Ordinal);
        await Hotel.FailsAsync(() => h.Payroll.IssueBankFileAsync(h.As("accountant", PayrollService.OpIssueBankFile), new IssueBankFileRequest(aug.RunId), Ct), HccErrorCode.INVALID_TRANSITION);
        Assert.True((await h.Payroll.IssueBankFileAsync(h.As("accountant", PayrollService.OpIssueBankFile), new IssueBankFileRequest(aug.RunId, ConfirmReissue: true), Ct)).Value.Reissue);

        var sent = await h.Payroll.SendPayslipAsync(h.As("accountant", PayrollService.OpSendPayslip), new SendPayslipRequest(aug.RunId, ravi), Ct);
        Assert.Contains("simulated", sent.Value.Notes, StringComparison.Ordinal);
        await Hotel.FailsAsync(() => h.Payroll.SendPayslipAsync(h.As("housekeeper", PayrollService.OpSendPayslip), new SendPayslipRequest(aug.RunId, ravi), Ct), HccErrorCode.FORBIDDEN);
        Assert.Equal("sent", (await h.Queries.GetPayRunAsync(h.PropertyId, h.U("owner"), new PayMonth(2026, 8), Ct)).Lines.Single(l => l.StaffId == ravi).PayslipMark);

        // Unlock: Owner only, a reason, and confirmation because a bank file was issued.
        var version = aug.Version;
        await Hotel.FailsAsync(() => h.Payroll.UnlockPayRunAsync(h.As("accountant", PayrollService.OpUnlockPayRun), new UnlockPayRunRequest(aug.RunId, version, "Correction"), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.UnlockPayRunAsync(h.As("owner", PayrollService.OpUnlockPayRun), new UnlockPayRunRequest(aug.RunId, version, "Correction"), Ct), HccErrorCode.INVALID_TRANSITION);
        var unlocked = await h.Payroll.UnlockPayRunAsync(h.As("owner", PayrollService.OpUnlockPayRun), new UnlockPayRunRequest(aug.RunId, version, "Correction", ConfirmBankFile: true), Ct);
        Assert.Equal("Draft", unlocked.Value.Status);
        Assert.Equal(0, await db.LongAsync("SELECT recovered_paise FROM hr.advance WHERE staff_id = @s;", ("@s", ravi)));   // recoveries reversed
        Assert.Equal("superseded", (await h.Queries.GetPayRunAsync(h.PropertyId, h.U("owner"), new PayMonth(2026, 8), Ct)).Lines.Single(l => l.StaffId == ravi).PayslipMark);
        await Hotel.FailsAsync(() => h.Queries.BankCsvAsync(h.PropertyId, h.U("accountant"), aug.RunId, 1, Ct), HccErrorCode.INVALID_TRANSITION);
        // Relock (lock 2) freezes the NEW bank details; the old file stays superseded.
        var relocked = await h.Payroll.LockPayRunAsync(h.As("owner", PayrollService.OpLockPayRun), new LockPayRunRequest(aug.RunId, unlocked.Value.Version), Ct);
        Assert.Equal("Locked", relocked.Value.Status);
        Assert.Equal("999999999999", (string)(await db.ScalarAsync("SELECT account_no FROM hr.pay_run_bank WHERE run_id = @r AND lock_seq = 2 AND staff_id = @s;", ("@r", aug.RunId), ("@s", ravi)))!);
        Assert.Equal("superseded", (await h.Queries.GetPayRunAsync(h.PropertyId, h.U("owner"), new PayMonth(2026, 8), Ct)).Lines.Single(l => l.StaffId == ravi).PayslipMark);
    }

    [Fact]
    public async Task F10_F11_no_self_money_changes_and_payroll_fields_need_payroll_rights()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var farhan = h.S("HR-S07");   // the Accountant's own record
        var v = await h.StaffVersionAsync(farhan);
        SaveStaffRequest Own(long? monthly = null, string? account = null) => new(farhan, v, "Farhan Qureshi", "ACC", "Accountant", "G", 0, LinkUserId: h.U("accountant"),
            MonthlyPaise: monthly, AccountNo: account);
        await Hotel.FailsAsync(() => h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), Own(monthly: 9_000_000), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), Own(account: "123456789012"), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.GiveAdvanceAsync(h.As("accountant", PayrollService.OpGiveAdvance), new GiveAdvanceRequest(farhan, 5_000_000, 0, "Self"), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.MarkAttendanceAsync(h.As("manager", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(h.S("HR-S01"), "P", 480), Ct),
            HccErrorCode.FORBIDDEN);   // the Manager's own overtime

        var ravi = h.S("HR-S05");
        SaveStaffRequest Ravi(string? gender = null, string? phone = null, long? monthly = null) => new(ravi, null!, "Ravi Shinde", "HK", "Room attendant", "M", 5,
            phone, h.U("housekeeper"), Gender: gender, MonthlyPaise: monthly);
        async Task<SaveStaffRequest> Fresh(SaveStaffRequest r) => r with { ExpectedVersion = await h.StaffVersionAsync(ravi) };
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("manager", PayrollService.OpSaveStaff), await Fresh(Ravi(gender: "F")), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("manager", PayrollService.OpSaveStaff), await Fresh(Ravi(phone: "90000 99999")), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("manager", PayrollService.OpSaveStaff), await Fresh(Ravi(monthly: 2_000_000)), Ct), HccErrorCode.FORBIDDEN);
        // The Manager may resave the unchanged record (and change non-money fields).
        var ok = await h.Payroll.SaveStaffAsync(h.As("manager", PayrollService.OpSaveStaff), await Fresh(Ravi() with { Designation = "Senior room attendant" }), Ct);
        Assert.Contains("updated", ok.Value.Notes, StringComparison.Ordinal);
        var phone = await h.Payroll.SaveStaffAsync(h.As("accountant", PayrollService.OpSaveStaff), await Fresh(Ravi(gender: "F", phone: "90000 99999")), Ct);
        Assert.Contains("PT slab men → women", phone.Value.Notes, StringComparison.Ordinal);
        Assert.Contains("••••9999", phone.Value.Notes, StringComparison.Ordinal);
        // Advance for someone else is fine; a second open advance is not.
        await Hotel.FailsAsync(() => h.Payroll.GiveAdvanceAsync(h.As("accountant", PayrollService.OpGiveAdvance), new GiveAdvanceRequest(ravi, 100_000, 0, "Second"), Ct), HccErrorCode.INVALID_TRANSITION);
        var adv = await h.Payroll.GiveAdvanceAsync(h.As("accountant", PayrollService.OpGiveAdvance), new GiveAdvanceRequest(h.S("HR-S09"), 500_000, 250_000, "Medical (fictional)"), Ct);
        Assert.Equal("2026-10", adv.Value.FromMonth);
        await Hotel.FailsAsync(() => h.Payroll.GiveAdvanceAsync(h.As("manager", PayrollService.OpGiveAdvance), new GiveAdvanceRequest(h.S("HR-S10"), 100_000, 0, "x"), Ct), HccErrorCode.FORBIDDEN);
    }

    [Fact]
    public async Task F03_login_links_are_Owner_only_never_your_own_and_a_relink_with_locked_payslips_needs_confirmation()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        await LockAsync(h, await PrepareAsync(h, "2026-08"));
        var farhan = h.S("HR-S07");
        SaveStaffRequest Farhan(int? link, bool confirm = false) => new(farhan, null!, "Farhan Qureshi", "ACC", "Accountant", "G", 0, null, link, confirm);
        async Task<SaveStaffRequest> Fresh(SaveStaffRequest r) => r with { ExpectedVersion = await h.StaffVersionAsync(farhan) };
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("manager", PayrollService.OpSaveStaff), await Fresh(Farhan(h.U("manager"))), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("owner", PayrollService.OpSaveStaff), await Fresh(Farhan(h.U("owner"))), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("owner", PayrollService.OpSaveStaff), await Fresh(Farhan(h.U("tech"))), Ct), HccErrorCode.INVALID_TRANSITION);
        await Hotel.FailsAsync(async () => await h.Payroll.SaveStaffAsync(h.As("owner", PayrollService.OpSaveStaff), await Fresh(Farhan(h.U("manager"), true)), Ct), HccErrorCode.VALIDATION);   // already linked
        var ok = await h.Payroll.SaveStaffAsync(h.As("owner", PayrollService.OpSaveStaff), await Fresh(Farhan(h.U("tech"), true)), Ct);
        Assert.Contains($"login {h.U("accountant")} → {h.U("tech")}", ok.Value.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task F12_full_and_final_is_maker_checker_with_DEC066_limits_and_writes_off_the_advance()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var ravi = h.S("HR-S05");
        await h.Payroll.SetStaffActiveAsync(h.As("manager", PayrollService.OpSetStaffActive), new SetStaffActiveRequest(ravi, await h.StaffVersionAsync(ravi), false), Ct);
        var big = new PrepareFnfRequest(ravi, [new FnfAdjustmentDto("Ex gratia (fictional)", "add", 15_000_000)]);
        await Hotel.FailsAsync(() => h.Payroll.PrepareFnfAsync(h.As("accountant", PayrollService.OpPrepareFnf), new PrepareFnfRequest(ravi, [new FnfAdjustmentDto("x", "add", 20_000_001)]), Ct),
            HccErrorCode.VALIDATION);   // each adjustment ≤ ₹2,00,000
        await Hotel.FailsAsync(() => h.Payroll.PrepareFnfAsync(h.As("manager", PayrollService.OpPrepareFnf), big, Ct), HccErrorCode.FORBIDDEN);
        var draft = (await h.Payroll.PrepareFnfAsync(h.As("owner", PayrollService.OpPrepareFnf), big, Ct)).Value;
        Assert.Equal("Draft", draft.Status);
        Assert.Contains("the Owner", draft.Notes, StringComparison.Ordinal);
        Assert.Equal(300_000, await db.LongAsync("SELECT amount_paise - recovered_paise FROM hr.advance WHERE staff_id = @s;", ("@s", ravi)));   // nothing written off yet
        await Hotel.FailsAsync(() => h.Payroll.ConfirmFnfAsync(h.As("owner", PayrollService.OpConfirmFnf), new DecideFnfRequest(draft.FnfId, draft.Version), Ct), HccErrorCode.FORBIDDEN);   // maker
        await Hotel.FailsAsync(() => h.Payroll.ConfirmFnfAsync(h.As("accountant", PayrollService.OpConfirmFnf), new DecideFnfRequest(draft.FnfId, draft.Version), Ct), HccErrorCode.APPROVAL_REQUIRED);   // > ₹1,00,000 additions: Owner
        await Hotel.FailsAsync(() => h.Payroll.PrepareFnfAsync(h.As("accountant", PayrollService.OpPrepareFnf), new PrepareFnfRequest(ravi), Ct), HccErrorCode.INVALID_TRANSITION);   // one draft
        var discarded = await h.Payroll.DiscardFnfAsync(h.As("accountant", PayrollService.OpDiscardFnf), new DecideFnfRequest(draft.FnfId, draft.Version), Ct);
        Assert.Equal("Discarded", discarded.Value.Status);

        var small = (await h.Payroll.PrepareFnfAsync(h.As("accountant", PayrollService.OpPrepareFnf), new PrepareFnfRequest(ravi, [new FnfAdjustmentDto("EL encashment", "add", 500_000)]), Ct)).Value;
        await Hotel.FailsAsync(() => h.Payroll.ConfirmFnfAsync(h.As("accountant", PayrollService.OpConfirmFnf), new DecideFnfRequest(small.FnfId, small.Version), Ct), HccErrorCode.FORBIDDEN);
        var settled = await h.Payroll.ConfirmFnfAsync(h.As("owner", PayrollService.OpConfirmFnf), new DecideFnfRequest(small.FnfId, small.Version), Ct);
        Assert.Equal(("Settled", small.NetPaise), (settled.Value.Status, settled.Value.NetPaise));
        Assert.True(small.SalaryIncluded);
        Assert.Equal(0, await db.LongAsync("SELECT amount_paise - recovered_paise FROM hr.advance WHERE staff_id = @s;", ("@s", ravi)));   // written off on confirmation
        Assert.Equal(1, await db.LongAsync("SELECT COUNT(*) FROM plat.outbox WHERE event_type = 'FnfSettled' AND aggregate_id = @f;", ("@f", small.FnfId)));
        await Hotel.FailsAsync(() => h.Payroll.SetStaffActiveAsync(h.As("manager", PayrollService.OpSetStaffActive), new SetStaffActiveRequest(ravi, "AAAAAAAAAAA=", true), Ct),
            HccErrorCode.INVALID_TRANSITION);
        // Your own settlement: the Accountant cannot prepare F&F for their own record.
        var farhan = h.S("HR-S07");
        await h.Payroll.SetStaffActiveAsync(h.As("manager", PayrollService.OpSetStaffActive), new SetStaffActiveRequest(farhan, await h.StaffVersionAsync(farhan), false), Ct);
        await Hotel.FailsAsync(() => h.Payroll.PrepareFnfAsync(h.As("accountant", PayrollService.OpPrepareFnf), new PrepareFnfRequest(farhan), Ct), HccErrorCode.FORBIDDEN);
        // October's run no longer includes Ravi once he is settled with October's salary (run for October when complete).
        await h.SetBusinessDateAsync("2026-11-02");
        var oct = await PrepareAsync(h, "2026-10");
        Assert.DoesNotContain(await LinesAsync(h, oct.RunId), l => l.StaffId == ravi);
    }

    [Fact]
    public async Task Leave_requests_weekly_offs_capacity_overlap_and_no_self_approval()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        Task<Hcc.Application.Commands.CommandResult<LeaveResult>> Req(string user, string staff, string type, string from, string? to = null, string? half = null)
            => h.Payroll.RequestLeaveAsync(h.As(user, PayrollService.OpRequestLeave), new RequestLeaveRequest(h.S(staff), type, PayrollGolden.Date(from), to is null ? null : PayrollGolden.Date(to), half, "Fictional reason"), Ct);
        // F-01 on the server: Aarav LOP Mon 9 – Wed 11 Nov (Tuesday off) = 2 days.
        Assert.Equal(2m, (await Req("reception", "HR-S02", "LOP", "2026-11-09", "2026-11-11")).Value.Days);
        await Hotel.FailsAsync(() => Req("housekeeper", "HR-S06", "CL", "2026-11-12"), HccErrorCode.FORBIDDEN);          // only for yourself
        await Hotel.FailsAsync(() => Req("manager", "HR-S10", "CL", "2026-11-12"), HccErrorCode.VALIDATION);             // daily wage: LOP only
        await Hotel.FailsAsync(() => Req("manager", "HR-S05", "CL", "2026-10-20"), HccErrorCode.VALIDATION);             // before the business date
        await Hotel.FailsAsync(() => Req("housekeeper", "HR-S05", "CL", "2026-10-30", half: "AM"), HccErrorCode.VALIDATION);   // Friday = Ravi's weekly off
        await Hotel.FailsAsync(() => Req("manager", "HR-S05", "CL", "2027-03-30", "2027-04-02"), HccErrorCode.VALIDATION);    // crosses 31 March
        // Capacity: Housekeeping max 2 off per day (Ravi + Kavya hold Wed 4 Nov → Neha refused).
        var r1 = (await Req("housekeeper", "HR-S05", "CL", "2026-11-04")).Value;
        await Req("housekeeper2", "HR-S06", "SL", "2026-11-04");
        var full = await Hotel.FailsAsync(() => Req("supervisor", "HR-S04", "CL", "2026-11-04"), HccErrorCode.NOT_AVAILABLE);
        Assert.Contains("Housekeeping already has 2 staff off", full.Message, StringComparison.Ordinal);
        await Hotel.FailsAsync(() => Req("housekeeper", "HR-S05", "SL", "2026-11-04"), HccErrorCode.NOT_AVAILABLE);      // overlap
        // Half days: AM and PM coexist; same half conflicts.
        Assert.Equal(0.5m, (await Req("housekeeper", "HR-S05", "CL", "2026-11-09", half: "AM")).Value.Days);
        await Req("housekeeper", "HR-S05", "SL", "2026-11-09", half: "PM");
        await Hotel.FailsAsync(() => Req("housekeeper", "HR-S05", "CL", "2026-11-09", half: "AM"), HccErrorCode.NOT_AVAILABLE);

        // Decisions: never your own (the Manager's own leave goes to the Owner); reject needs a reason.
        var mine = (await Req("manager", "HR-S01", "CL", "2026-11-16")).Value;
        await Hotel.FailsAsync(() => h.Payroll.DecideLeaveAsync(h.As("manager", PayrollService.OpDecideLeave), new DecideLeaveRequest(mine.LeaveId, mine.Version, true), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.DecideLeaveAsync(h.As("housekeeper", PayrollService.OpDecideLeave), new DecideLeaveRequest(r1.LeaveId, r1.Version, true), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.DecideLeaveAsync(h.As("owner", PayrollService.OpDecideLeave), new DecideLeaveRequest(mine.LeaveId, mine.Version, false), Ct), HccErrorCode.VALIDATION);
        var approved = await h.Payroll.DecideLeaveAsync(h.As("owner", PayrollService.OpDecideLeave), new DecideLeaveRequest(mine.LeaveId, mine.Version, true), Ct);
        Assert.Equal("Approved", approved.Value.Status);
        await Hotel.FailsAsync(() => h.Payroll.DecideLeaveAsync(h.As("owner", PayrollService.OpDecideLeave), new DecideLeaveRequest(mine.LeaveId, approved.Value.Version, true), Ct), HccErrorCode.INVALID_TRANSITION);
        await Hotel.FailsAsync(() => h.Payroll.DecideLeaveAsync(h.As("manager", PayrollService.OpDecideLeave), new DecideLeaveRequest(r1.LeaveId, "AAAAAAAAAAA=", true), Ct), HccErrorCode.STALE_VERSION);
        // Cancel: the requester while pending; approved leave only by a manager.
        Assert.Equal("Cancelled", (await h.Payroll.CancelLeaveAsync(h.As("housekeeper", PayrollService.OpCancelLeave), new CancelLeaveRequest(r1.LeaveId, r1.Version), Ct)).Value.Status);
        await Hotel.FailsAsync(() => h.Payroll.CancelLeaveAsync(h.As("reception", PayrollService.OpCancelLeave), new CancelLeaveRequest(mine.LeaveId, approved.Value.Version), Ct), HccErrorCode.FORBIDDEN);
        Assert.Equal("Cancelled", (await h.Payroll.CancelLeaveAsync(h.As("owner", PayrollService.OpCancelLeave), new CancelLeaveRequest(mine.LeaveId, approved.Value.Version), Ct)).Value.Status);

        // Balance: CL 12 per FY; Ravi used CL 2 (Sep) + pending ½ → 9.5 left; the housekeeper reads only his own balance.
        var bal = await h.Queries.GetLeaveBalanceAsync(h.PropertyId, h.U("housekeeper"), h.S("HR-S05"), null, Ct);
        Assert.Equal(9.5m, bal.Single(b => b.LeaveType == "CL").Left);
        await Hotel.FailsAsync(() => h.Queries.GetLeaveBalanceAsync(h.PropertyId, h.U("housekeeper"), h.S("HR-S06"), null, Ct), HccErrorCode.NOT_FOUND);
        // Leave policy and department caps are Owner-only (DEC-058); F-15 counts staff over a cut.
        await Hotel.FailsAsync(() => h.Payroll.SetLeavePolicyAsync(h.As("manager", PayrollService.OpSetLeavePolicy), new SetLeavePolicyRequest(12, 7, 15, 3), Ct), HccErrorCode.FORBIDDEN);
        var cut = await h.Payroll.SetLeavePolicyAsync(h.As("owner", PayrollService.OpSetLeavePolicy), new SetLeavePolicyRequest(1, 7, 15, 2), Ct);
        Assert.True(cut.Value.StaffOverEntitlement >= 1);
        await Hotel.FailsAsync(() => h.Payroll.SaveDepartmentAsync(h.As("manager", PayrollService.OpSaveDepartment), new SaveDepartmentRequest("HK", "Housekeeping", 3), Ct), HccErrorCode.FORBIDDEN);
        Assert.Equal(3, (await h.Payroll.SaveDepartmentAsync(h.As("owner", PayrollService.OpSaveDepartment), new SaveDepartmentRequest("HK", "Housekeeping", 3), Ct)).Value.LeaveCap);
    }

    [Fact]
    public async Task Attendance_self_checkin_is_late_after_grace_and_managers_mark_others()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var ravi = h.S("HR-S05");
        var mark = await h.Payroll.MarkAttendanceAsync(h.As("housekeeper", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(ravi), Ct);   // 08:00 IST, shift 07:00
        Assert.Equal(("L", new DateOnly(2026, 10, 24)), (mark.Value.Status, mark.Value.Date));
        await Hotel.FailsAsync(() => h.Payroll.MarkAttendanceAsync(h.As("housekeeper", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(ravi), Ct), HccErrorCode.INVALID_TRANSITION);
        await Hotel.FailsAsync(() => h.Payroll.MarkAttendanceAsync(h.As("housekeeper", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(h.S("HR-S06")), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.MarkAttendanceAsync(h.As("housekeeper", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(h.S("HR-S06"), "P"), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Payroll.MarkAttendanceAsync(h.As("manager", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(h.S("HR-S09"), "P", 600), Ct), HccErrorCode.VALIDATION);
        await Hotel.FailsAsync(() => h.Payroll.MarkAttendanceAsync(h.As("manager", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(h.S("HR-S09"), "A", 30), Ct), HccErrorCode.VALIDATION);
        var p = await h.Payroll.MarkAttendanceAsync(h.As("manager", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(h.S("HR-S09"), "P", 60), Ct);
        Assert.False(p.Value.Corrected);
        Assert.True((await h.Payroll.MarkAttendanceAsync(h.As("manager", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(h.S("HR-S09"), "H"), Ct)).Value.Corrected);
        // Roster: today or later only; copy week.
        await Hotel.FailsAsync(() => h.Payroll.SetRosterAsync(h.As("manager", PayrollService.OpSetRoster), new SetRosterRequest(ravi, new DateOnly(2026, 10, 23), "E"), Ct), HccErrorCode.INVALID_TRANSITION);
        Assert.Equal(1, (await h.Payroll.SetRosterAsync(h.As("manager", PayrollService.OpSetRoster), new SetRosterRequest(ravi, new DateOnly(2026, 10, 27), "E"), Ct)).Value.Cells);
        Assert.Equal(84, (await h.Payroll.CopyRosterWeekAsync(h.As("manager", PayrollService.OpCopyRosterWeek), new CopyRosterWeekRequest(new DateOnly(2026, 10, 27)), Ct)).Value.Cells);
        Assert.Equal("E", (string)(await db.ScalarAsync("SELECT shift_code FROM hr.roster WHERE staff_id = @s AND work_date = '2026-11-03';", ("@s", ravi)))!);
        await Hotel.FailsAsync(() => h.Payroll.SetRosterAsync(h.As("housekeeper", PayrollService.OpSetRoster), new SetRosterRequest(ravi, new DateOnly(2026, 10, 27), "M"), Ct), HccErrorCode.FORBIDDEN);
    }

    [Fact]
    public async Task DEC061_own_payslip_only_and_salaries_hidden_from_the_Manager()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var aug = await LockAsync(h, await PrepareAsync(h, "2026-08"));
        await PrepareAsync(h, "2026-09");   // draft: invisible to staff
        var ravi = h.S("HR-S05");
        var mine = await h.Queries.GetMyPayslipsAsync(h.PropertyId, h.U("housekeeper"), Ct);
        Assert.Equal(["2026-08"], mine.Select(p => p.Month));
        var slip = await h.Queries.GetPayslipAsync(h.PropertyId, h.U("housekeeper"), aug.RunId, ravi, Ct);
        Assert.Equal(("Provident Fund (12%)", "ESIC (0.75%)"), (slip.PfLabel, slip.EsicLabel));   // F-14: from the run's rates
        Assert.EndsWith("Only", slip.NetInWords, StringComparison.Ordinal);
        await Hotel.FailsAsync(() => h.Queries.GetPayslipAsync(h.PropertyId, h.U("housekeeper"), aug.RunId, h.S("HR-S06"), Ct), HccErrorCode.NOT_FOUND);
        await Hotel.FailsAsync(() => h.Queries.GetPayRunAsync(h.PropertyId, h.U("housekeeper"), new PayMonth(2026, 8), Ct), HccErrorCode.FORBIDDEN);
        await Hotel.FailsAsync(() => h.Queries.GetPayRunAsync(h.PropertyId, h.U("manager"), new PayMonth(2026, 8), Ct), HccErrorCode.FORBIDDEN);
        Assert.Single(await h.Queries.GetMyPayslipsAsync(h.PropertyId, h.U("maint"), Ct));   // maintenance sees own pay (DEC-061)
        Assert.Empty(await h.Queries.GetMyPayslipsAsync(h.PropertyId, h.U("tech"), Ct));     // no staff record linked
        // Staff master: the Manager sees no salaries except their own; the housekeeper sees only their own record.
        var staff = await h.Queries.GetStaffAsync(h.PropertyId, h.U("manager"), Ct);
        Assert.Equal(12, staff.Count);
        Assert.All(staff.Where(s => s.StaffId != h.S("HR-S01")), s => Assert.Null(s.MonthlyPaise));
        Assert.Equal(4_500_000, staff.Single(s => s.StaffId == h.S("HR-S01")).MonthlyPaise);
        Assert.All(staff, s => Assert.Null(s.AccountMasked));
        var own = await h.Queries.GetStaffAsync(h.PropertyId, h.U("housekeeper"), Ct);
        Assert.Equal([ravi], own.Select(s => s.StaffId));
        var pay = await h.Queries.GetStaffAsync(h.PropertyId, h.U("accountant"), Ct);
        Assert.All(pay, s => Assert.StartsWith("••••", s.AccountMasked!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Commands_are_idempotent_and_rates_keep_a_dated_history()
    {
        var h = await db.NewHotelAsync();
        var ctx = h.As("owner", PayrollService.OpSetStatutoryRates, "rates-key-1");
        var first = await h.Payroll.SetStatutoryRatesAsync(ctx, new SetStatutoryRatesRequest(PfEeBps: 1100), Ct);
        Assert.Equal(new DateOnly(2026, 10, 1), first.Value.EffectiveFrom);   // F-06 default: current month (nothing locked)
        Assert.False(first.Replayed);
        var replay = await h.Payroll.SetStatutoryRatesAsync(ctx, new SetStatutoryRatesRequest(PfEeBps: 1100), Ct);
        Assert.True(replay.Replayed);
        await Hotel.FailsAsync(() => h.Payroll.SetStatutoryRatesAsync(ctx, new SetStatutoryRatesRequest(PfEeBps: 1000), Ct), HccErrorCode.IDEMPOTENCY_MISMATCH);
        await h.Payroll.SetStatutoryRatesAsync(h.As("owner", PayrollService.OpSetStatutoryRates), new SetStatutoryRatesRequest(new DateOnly(2027, 4, 1), EsicEeBps: 100, Rounding: "rupee"), Ct);
        var carried = await h.Payroll.SetStatutoryRatesAsync(h.As("owner", PayrollService.OpSetStatutoryRates), new SetStatutoryRatesRequest(new DateOnly(2027, 1, 1), PfErBps: 1250), Ct);
        Assert.Equal(["pfErBps → 2027-04-01"], carried.Value.Carried);
        var apr = await h.Queries.GetRatesAsync(h.PropertyId, h.U("accountant"), new PayMonth(2027, 4), Ct);
        Assert.Equal((1100, 1250, 100, StatutoryRounding.Rupee), (apr.PfEeBps, apr.PfErBps, apr.EsicEeBps, apr.Rounding));
        Assert.Equal(1200, (await h.Queries.GetRatesAsync(h.PropertyId, h.U("owner"), new PayMonth(2026, 9), Ct)).PfEeBps);
        Assert.Equal(3, await db.LongAsync("SELECT COUNT(*) FROM hr.rate_change WHERE property_id = @p;", ("@p", h.PropertyId)));
        Assert.Equal(4, await db.LongAsync("SELECT COUNT(*) FROM hr.rate_version WHERE property_id = @p;", ("@p", h.PropertyId)));   // default + Oct + Jan + Apr
        Assert.Equal(3, await db.LongAsync("SELECT COUNT(*) FROM plat.audit_event WHERE property_id = @p AND action = 'HR_RATES_SET';", ("@p", h.PropertyId)));
    }
}
