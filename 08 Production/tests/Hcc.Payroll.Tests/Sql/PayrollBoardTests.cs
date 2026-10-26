using Hcc.Application.Payroll;
using Hcc.Contracts.Payroll;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Payroll.Tests.Harness;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>
/// W4-6 HR desk board (GET /payroll/board): one read for the Staff &amp; payroll and My shifts &amp; pay screens. Payroll figures are the stored
/// run lines; Manager sees people without pay; staff see only their own record and their own locked lines (DEC-061).
/// </summary>
[Trait("Category", "Sql")]
public sealed class PayrollBoardTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Board_gives_payroll_roles_everything_managers_people_only_and_staff_their_own_pay_only()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        var ravi = h.S("HR-S05");
        var aug = (await h.Payroll.PreparePayRunAsync(h.As("accountant", PayrollService.OpPreparePayRun), new PreparePayRunRequest("2026-08"), Ct)).Value;
        aug = (await h.Payroll.LockPayRunAsync(h.As("owner", PayrollService.OpLockPayRun), new LockPayRunRequest(aug.RunId, aug.Version), Ct)).Value;
        await h.Payroll.IssueBankFileAsync(h.As("accountant", PayrollService.OpIssueBankFile), new IssueBankFileRequest(aug.RunId), Ct);
        await h.Payroll.SendPayslipAsync(h.As("accountant", PayrollService.OpSendPayslip), new SendPayslipRequest(aug.RunId, ravi), Ct);
        var sep = (await h.Payroll.PreparePayRunAsync(h.As("accountant", PayrollService.OpPreparePayRun), new PreparePayRunRequest("2026-09"), Ct)).Value;
        await h.Payroll.SetRosterAsync(h.As("manager", PayrollService.OpSetRoster), new SetRosterRequest(ravi, new DateOnly(2026, 10, 27), "E"), Ct);
        await h.Payroll.MarkAttendanceAsync(h.As("manager", PayrollService.OpMarkAttendance), new MarkAttendanceRequest(ravi, "P", 30, "Stayed late"), Ct);
        var leave = await h.Payroll.RequestLeaveAsync(h.As("housekeeper", PayrollService.OpRequestLeave),
            new RequestLeaveRequest(ravi, "CL", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), null, "Family function"), Ct);

        var acc = await h.Queries.GetBoardAsync(h.PropertyId, h.U("accountant"), null, null, Ct);
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 19), true, true), (acc.From, acc.To, acc.AllStaff, acc.Payroll));
        Assert.Equal(7, acc.Departments.Count);
        Assert.Equal((12, 7, 15, 2), (acc.LeavePolicy.Cl, acc.LeavePolicy.Sl, acc.LeavePolicy.El, acc.LeavePolicy.DefaultCap));
        Assert.Equal(["2026-08", "2026-09"], acc.Runs.Select(r => r.Month));
        var a = acc.Runs[0];
        Assert.Equal(("Locked", 1, 12, aug.NetPaise), (a.Status, a.LockSeq, a.Lines.Count, a.Lines.Sum(l => l.NetPaise)));
        Assert.Equal(h.U("accountant"), a.PreparedBy);
        Assert.Equal(h.U("owner"), a.LockedBy);
        Assert.EndsWith("Z", a.PreparedUtc, StringComparison.Ordinal);
        Assert.Single(a.BankFiles);
        Assert.Equal(ravi, a.Sends.Single().StaffId);
        Assert.StartsWith("••••", a.Sends.Single().ToMasked, StringComparison.Ordinal);
        var line = a.Lines.Single(l => l.StaffId == ravi);
        Assert.Equal((31, "S", "M"), (line.Counts.Days, line.Structure, line.Gender));
        Assert.True(line.BasePaise > 0);
        Assert.Equal(sep.NetPaise, acc.Runs[1].Lines.Sum(l => l.NetPaise));
        Assert.Equal("Draft", acc.Runs[1].Status);
        Assert.Equal("paise", acc.RateVersions.Single().Rounding);
        var adv = acc.Advances.Single();
        Assert.Equal((ravi, 100_000L, "Festival advance (fictional)"), (adv.StaffId, adv.RecoveredPaise, adv.Reason));
        Assert.Equal(aug.RunId, adv.Recoveries.Single().RunId);
        var att = acc.Attendance.Single(x => x.StaffId == ravi && x.Date == new DateOnly(2026, 10, 24));
        Assert.Equal(("P", 30, h.U("manager"), "Stayed late"), (att.Status, att.OtMinutes, att.MarkedBy, att.Note));
        Assert.Contains(acc.Roster, x => x.StaffId == ravi && x.Date == new DateOnly(2026, 10, 27) && x.Shift == "E");
        var lv = acc.Leaves.Single(x => x.LeaveId == leave.Value.LeaveId);
        Assert.Equal(("Pending", 2m, h.U("housekeeper"), "Family function"), (lv.Status, lv.Days, lv.RequestedBy, lv.Reason));

        var mgr = await h.Queries.GetBoardAsync(h.PropertyId, h.U("manager"), null, null, Ct);
        Assert.Equal((true, false), (mgr.AllStaff, mgr.Payroll));
        var mgrStaff = (await h.Queries.GetStaffAsync(h.PropertyId, h.U("manager"), Ct)).Single(s => s.LinkedUserId == h.U("manager")).StaffId;
        Assert.All(mgr.Runs, r => Assert.Equal([mgrStaff], r.Lines.Select(l => l.StaffId)));   // own pay only, locked runs only
        Assert.Equal(["2026-08"], mgr.Runs.Select(r => r.Month));
        Assert.Empty(mgr.Advances);
        Assert.Empty(mgr.RateVersions);
        Assert.Equal(acc.Leaves.Count, mgr.Leaves.Count);

        // DEC-061: the housekeeper sees only his own record and only his own line of the LOCKED run — no other figures, no draft.
        var hk = await h.Queries.GetBoardAsync(h.PropertyId, h.U("housekeeper"), null, null, Ct);
        Assert.Equal((false, false), (hk.AllStaff, hk.Payroll));
        var own = Assert.Single(hk.Runs);
        Assert.Equal(("2026-08", ravi), (own.Month, Assert.Single(own.Lines).StaffId));
        Assert.Equal(line, own.Lines[0]);
        Assert.Empty(own.Warnings);
        Assert.Empty(own.Sends);
        Assert.Empty(own.BankFiles);
        Assert.All(hk.Leaves, x => Assert.Equal(ravi, x.StaffId));
        Assert.All(hk.Attendance, x => Assert.Equal(ravi, x.StaffId));
        Assert.Empty(hk.Advances);
        Assert.Empty(hk.Fnf);

        // Staff list keeps the login link so an edit keeps it and "my record" is found.
        var staff = await h.Queries.GetStaffAsync(h.PropertyId, h.U("manager"), Ct);
        Assert.Equal(h.U("housekeeper"), staff.Single(s => s.StaffId == ravi).LinkedUserId);
        Assert.Null(staff.Single(s => s.StaffId == ravi).MonthlyPaise);

        await Hotel.FailsAsync(() => h.Queries.GetBoardAsync(h.PropertyId, h.U("manager"), new DateOnly(2026, 1, 1), new DateOnly(2027, 6, 1), Ct), HccErrorCode.VALIDATION);
        var tech = await h.Queries.GetBoardAsync(h.PropertyId, h.U("tech"), null, null, Ct);   // Tech admin: PERM-HR-001 view, no pay
        Assert.Empty(tech.Runs);
    }
}
