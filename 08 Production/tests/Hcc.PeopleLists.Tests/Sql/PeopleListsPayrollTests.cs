using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Contracts.Payroll;
using Hcc.Contracts.PeopleLists;
using Hcc.Domain.Compliance;
using Hcc.Domain.Payroll;
using Hcc.Infrastructure.SqlServer.Compliance;
using Hcc.PeopleLists.Tests.Harness;

namespace Hcc.PeopleLists.Tests.Sql;

/// <summary>
/// The People lists inside payroll, roster, attendance, leave and compliance on SQL Server: custom shift patterns and leave types, the pattern's late
/// rule and weekly-off rule, paid holidays in day counts, SCC's fixed leave categories and their quota (SCC leave port, DEC-116: leave is one record a
/// date — the roster and paid holidays do not decide leave days; no carry-forward), designation codes on staff, due-date shifting.
/// </summary>
[Trait("Category", "Sql")]
public sealed class PeopleListsPayrollTests(PeopleDb db) : IClassFixture<PeopleDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task SaveShift(Hotel h, string code, string name, string start, string end, bool night = false, string? rule = null) =>
        h.Lists.SaveShiftPatternAsync(h.As("owner", "SaveShiftPattern"), new SaveShiftPatternRequest(null, code, name, start, end, null, 30, night, rule), Ct);

    private static Task SaveHoliday(Hotel h, string date, string kind = "paid") =>
        h.Lists.SaveHolidayAsync(h.As("owner", "SaveHoliday"), new SaveHolidayRequest(null, date, "Holiday " + date, kind), Ct);

    private static Task<Hcc.Application.Commands.CommandResult<LeaveRequestResult>> Leave(Hotel h, long staff, string type, string from, string? to = null, string? half = null,
        string user = "owner", bool confirm = false) =>
        h.Db.Payroll.RequestLeaveAsync(h.As(user, PayrollService.OpRequestLeave), new RequestLeaveRequest(staff, type, DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), half,
            "Family matter", h.U("manager"), confirm), Ct);

    private static Task<Hcc.Application.Commands.CommandResult<AttendanceResult>> CheckIn(Hotel h, long staff, string user) =>
        h.Db.Payroll.MarkAttendanceAsync(h.As(user, PayrollService.OpMarkAttendance), new MarkAttendanceRequest(staff), Ct);

    private static async Task<StaffDays> DaysOf(Hotel h, long staff, string from, string to)
    {
        await using var r = await h.Db.PayrollStore.OpenReadAsync(Ct);
        return (await r.DaysAsync(h.PropertyId, [staff], DateOnly.Parse(from), DateOnly.Parse(to), Ct))[staff];
    }

    private static async Task<StaffProfile> ProfileOf(Hotel h, long staff)
    {
        await using var r = await h.Db.PayrollStore.OpenReadAsync(Ct);
        return (await r.StaffAsync(h.PropertyId, staff, false, Ct))!.Profile;
    }

    [Fact]
    public async Task Default_shift_and_roster_take_active_patterns_of_the_list_only_a_retired_one_stays_on_records_that_have_it()
    {
        var h = await db.NewHotelAsync();
        await Hotel.FailsAsync(() => h.AddStaffAsync("Asha", shift: "S"), HccErrorCode.VALIDATION);   // no such pattern yet
        await SaveShift(h, "S", "Split", "10:00", "20:00");
        var asha = await h.AddStaffAsync("Asha", shift: "S");
        Assert.Equal("S", (await ProfileOf(h, asha)).DefaultShift.Value);
        await h.Db.Payroll.SetRosterAsync(h.As("owner", PayrollService.OpSetRoster), new SetRosterRequest(asha, new DateOnly(2026, 10, 26), "G"), Ct);
        await h.Db.Payroll.SetRosterAsync(h.As("owner", PayrollService.OpSetRoster), new SetRosterRequest(asha, new DateOnly(2026, 10, 27), "OFF"), Ct);
        await Hotel.FailsAsync(() => h.Db.Payroll.SetRosterAsync(h.As("owner", PayrollService.OpSetRoster), new SetRosterRequest(asha, new DateOnly(2026, 10, 28), "ZZ"), Ct), HccErrorCode.VALIDATION);
        Assert.Equal(["G", "OFF"], (await DaysOf(h, asha, "2026-10-26", "2026-10-28")).Roster.OrderBy(x => x.Key).Select(x => x.Value.Value));

        // Retire S: a new record cannot use it (the retire itself is refused while Asha's default is S, so move her first).
        var s = (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "S");
        await Hotel.FailsAsync(() => h.Lists.RetireShiftPatternAsync(h.As("owner", "RetireShiftPattern"), new RetireShiftPatternRequest(s.ShiftPatternId, s.Version), Ct), HccErrorCode.INVALID_TRANSITION, "MASTER_IN_USE");
        var ashaRow = await h.Db.PayrollQueries.GetStaffAsync(h.PropertyId, h.U("owner"), Ct);
        var v = ashaRow.Single(x => x.StaffId == asha).Version;
        await h.Db.Payroll.SaveStaffAsync(h.As("owner", PayrollService.OpSaveStaff), new SaveStaffRequest(asha, v, "Asha", "FO", "Associate", "M", 2), Ct);
        await db.ScalarAsync("DELETE hr.roster WHERE staff_id = @s;", ("@s", asha));
        await h.Lists.RetireShiftPatternAsync(h.As("owner", "RetireShiftPattern"), new RetireShiftPatternRequest(s.ShiftPatternId, s.Version), Ct);
        await Hotel.FailsAsync(() => h.AddStaffAsync("Bala", shift: "S"), HccErrorCode.VALIDATION);
        await Hotel.FailsAsync(() => h.Db.Payroll.SetRosterAsync(h.As("owner", PayrollService.OpSetRoster), new SetRosterRequest(asha, new DateOnly(2026, 10, 28), "S"), Ct), HccErrorCode.VALIDATION);
    }

    [Fact]
    public async Task Self_check_in_is_late_after_the_pattern_start_plus_15_minutes_and_never_for_night_patterns()
    {
        var h = await db.NewHotelAsync();   // the clock is 08:00 IST on Saturday 24 Oct 2026
        await SaveShift(h, "S8", "Eight", "08:00", "17:00");
        await SaveShift(h, "W", "Watch", "20:00", "06:00", night: true);
        var early = await h.AddStaffAsync("Early", shift: "M", linkUser: "reception");          // starts 07:00 → 08:00 is 60 minutes late
        var ontime = await h.AddStaffAsync("OnTime", shift: "S8", linkUser: "cashier");        // starts 08:00
        var night = await h.AddStaffAsync("Night", shift: "W", linkUser: "supervisor");        // night: never late
        Assert.Equal("L", (await CheckIn(h, early, "reception")).Value.Status);
        Assert.Equal("P", (await CheckIn(h, ontime, "cashier")).Value.Status);
        Assert.Equal("P", (await CheckIn(h, night, "supervisor")).Value.Status);
        // Moving the Morning start to 08:00 makes the same check-in on time.
        var m = (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "M");
        await h.Lists.SaveShiftPatternAsync(h.As("owner", "SaveShiftPattern"), new SaveShiftPatternRequest(m.ShiftPatternId, "M", "Morning", "08:00", "16:00", null, 30, false, "fixed", m.Version), Ct);
        var early2 = await h.AddStaffAsync("Early two", shift: "M", linkUser: "housekeeper");
        Assert.Equal("P", (await CheckIn(h, early2, "housekeeper")).Value.Status);
    }

    [Fact]
    public async Task A_pattern_that_leaves_the_weekly_off_to_the_roster_removes_the_automatic_weekly_off()
    {
        var h = await db.NewHotelAsync();
        var asha = await h.AddStaffAsync("Asha", shift: "M", weeklyOff: 2);
        var bala = await h.AddStaffAsync("Bala", shift: "G", weeklyOff: 2);
        var tue = new DateOnly(2026, 10, 27);
        var days = new StaffDays();
        Assert.Equal(ShiftCode.OFF, PayrollRules.ShiftFor(await ProfileOf(h, asha), days, tue));
        var m = (await h.Queries.ShiftPatternsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "M");
        await h.Lists.SaveShiftPatternAsync(h.As("owner", "SaveShiftPattern"), new SaveShiftPatternRequest(m.ShiftPatternId, "M", "Morning", "07:00", "15:00", null, 30, false, "roster", m.Version), Ct);
        Assert.Equal(ShiftCode.M, PayrollRules.ShiftFor(await ProfileOf(h, asha), days, tue));
        Assert.Equal(ShiftCode.OFF, PayrollRules.ShiftFor(await ProfileOf(h, bala), days, tue));   // other patterns keep the fixed day
        // SCC leave port (DEC-116): leave days do not follow the roster — an approved leave record on Bala's fixed weekly off is a leave day in pay.
        var leaveTue = new StaffDays(leaves: [new LeaveSpan(1, bala, LeaveType.CL, tue, tue, null, 1, LeaveStatus.Approved)]);
        var c = PayrollRules.Counts(await ProfileOf(h, bala), leaveTue, new PayMonth(2026, 10));
        Assert.Equal((1m, 0m), (c.PaidLeave, c.LopLeave));
    }

    [Fact]
    public async Task Leave_requests_take_SCC_fixed_categories_for_every_pay_type_and_the_quota_warns_until_confirmed()
    {
        var h = await db.NewHotelAsync();
        var asha = await h.AddStaffAsync("Asha", dept: "FO");
        var imran = await h.AddStaffAsync("Imran", dept: "KIT", payType: "daily", monthly: 0, daily: 60_000, weeklyOff: 3);
        // SCC's six categories + LOP are fixed: no other code, none retired.
        await Hotel.FailsAsync(() => h.Lists.SaveLeaveTypeAsync(h.As("owner", "SaveLeaveType"), new SaveLeaveTypeRequest(null, "BL", "Bereavement leave", true), Ct), HccErrorCode.VALIDATION);
        var oth = (await Leave(h, asha, "OTH", "2026-11-04", "2026-11-05")).Value;   // paid, not tracked: no balance check
        Assert.Equal([("OTH", 1m), ("OTH", 1m)], oth.Added.Select(x => (x.LeaveType, x.Days)));
        Assert.Equal(["CL", "SL", "EL"], (await h.Db.PayrollQueries.GetLeaveBalanceAsync(h.PropertyId, h.U("owner"), asha, null, Ct)).Select(b => b.LeaveType));
        await Hotel.FailsAsync(() => Leave(h, asha, "XX", "2026-11-09"), HccErrorCode.VALIDATION);
        Assert.Equal("CL", (await Leave(h, imran, "CL", "2026-11-09")).Value.Added.Single().LeaveType);          // SCC: any category for daily wage too
        Assert.Equal("LOP", (await Leave(h, imran, "LOP", "2026-11-12")).Value.Added.Single().LeaveType);
        var types = (await h.Queries.LeaveTypesAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items;
        Assert.Equal(["CL", "SL", "EL", "ML", "CO", "OTH", "LOP"], types.Select(x => x.Code));
        var ml = types.Single(x => x.Code == "ML");
        await Hotel.FailsAsync(() => h.Lists.RetireLeaveTypeAsync(h.As("owner", "RetireLeaveType"), new RetireLeaveTypeRequest(ml.LeaveTypeId, ml.Version), Ct),
            HccErrorCode.INVALID_TRANSITION, "MASTER_IN_USE");
        // The quota decides the balance; over it SCC warns and records only once confirmed (no hard block).
        var cl = types.Single(x => x.Code == "CL");
        await h.Lists.SaveLeaveTypeAsync(h.As("owner", "SaveLeaveType"), new SaveLeaveTypeRequest(cl.LeaveTypeId, "CL", "Casual leave", true, 1, null, cl.Version), Ct);
        var over = await Hotel.FailsAsync(() => Leave(h, asha, "CL", "2026-11-16", "2026-11-18"), HccErrorCode.INVALID_TRANSITION);
        Assert.StartsWith("Asha has used 0 of 1 Casual Leave day(s) this FY 2026–27.", over.Message, StringComparison.Ordinal);   // SCC confirm text
        Assert.Contains("This request adds 3 more and EXCEEDS the balance.", over.Message, StringComparison.Ordinal);
        Assert.Equal(3, (await Leave(h, asha, "CL", "2026-11-16", "2026-11-18", confirm: true)).Value.Added.Count);
    }

    [Fact]
    public async Task SCC_has_no_carry_forward_every_financial_year_gets_the_same_entitlement_halves_allowed()
    {
        var h = await db.NewHotelAsync();
        var asha = await h.AddStaffAsync("Asha", joined: "2025-04-01");
        var late = await h.AddStaffAsync("Late joiner", joined: "2026-06-01");
        var cl = (await h.Queries.LeaveTypesAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "CL");
        Assert.Equal(12m, (await Balance(h, asha, "2027-05-10")).Entitled);
        await h.Lists.SaveLeaveTypeAsync(h.As("owner", "SaveLeaveType"), new SaveLeaveTypeRequest(cl.LeaveTypeId, "CL", "Casual leave", true, 12.5m, null, cl.Version), Ct);
        Assert.Equal(12.5m, (await Balance(h, asha, "2027-05-10")).Entitled);   // nothing carried from an unused year
        Assert.Equal(12.5m, (await Balance(h, asha, "2026-10-24")).Entitled);
        Assert.Equal(12.5m, (await Balance(h, late, "2026-10-24")).Entitled);   // SCC: no pro-rating for a late joiner
    }

    private static async Task<LeaveBalanceView> Balance(Hotel h, long staff, string on) =>
        (await h.Db.PayrollQueries.GetLeaveBalanceAsync(h.PropertyId, h.U("owner"), staff, DateOnly.Parse(on), Ct)).Single(b => b.LeaveType == "CL");

    [Fact]
    public async Task Paid_holidays_are_paid_days_in_the_counts_and_not_leave_days_restricted_holidays_change_nothing()
    {
        var h = await db.NewHotelAsync();
        var asha = await h.AddStaffAsync("Asha", weeklyOff: 2);
        var before = PayrollRules.Counts(await ProfileOf(h, asha), await DaysOf(h, asha, "2026-09-01", "2026-09-30"), new PayMonth(2026, 9));
        await SaveHoliday(h, "2026-09-14");
        await SaveHoliday(h, "2026-09-15", "restricted");
        await SaveHoliday(h, "2026-09-22");   // a Tuesday = Asha's weekly off: stays a weekly off
        var after = PayrollRules.Counts(await ProfileOf(h, asha), await DaysOf(h, asha, "2026-09-01", "2026-09-30"), new PayMonth(2026, 9));
        Assert.Equal((0m, 1m), (before.Hol, after.Hol));
        Assert.Equal(before.Unmarked - 1, after.Unmarked);
        Assert.Equal(before.Off, after.Off);
        var days = await DaysOf(h, asha, "2026-09-01", "2026-09-30");
        Assert.Equal([new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 22)], days.PaidHolidays.OrderBy(x => x));
        // SCC leave port: leave on 9–11 Nov with a paid holiday on Mon 9 Nov and Asha's off day on Tue 10 Nov is three date records (SCC leaveDaysOf).
        await SaveHoliday(h, "2026-11-09");
        Assert.Equal(3m, (await Leave(h, asha, "CL", "2026-11-09", "2026-11-11")).Value.Added.Sum(x => x.Days));
    }

    [Fact]
    public async Task A_leave_on_a_paid_holiday_is_recorded_like_any_date_and_a_retired_holiday_no_longer_counts()
    {
        var h = await db.NewHotelAsync();
        var asha = await h.AddStaffAsync("Asha", weeklyOff: 2);
        await SaveHoliday(h, "2026-11-09");
        // SCC leave port (DEC-116): a paid holiday is not a leave rule (SCC store holidays only warn) — the record is kept; a half day then duplicates it.
        Assert.Equal(1m, (await Leave(h, asha, "CL", "2026-11-09")).Value.Added.Single().Days);
        Assert.Equal("1 skipped — duplicate", (await Hotel.FailsAsync(() => Leave(h, asha, "CL", "2026-11-09", half: "AM"), HccErrorCode.NOT_AVAILABLE)).Message);
        var holiday = (await h.Queries.HolidaysAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single();
        await h.Lists.RetireHolidayAsync(h.As("owner", "RetireHoliday"), new RetireHolidayRequest(holiday.HolidayId, holiday.Version), Ct);
        Assert.Empty((await DaysOf(h, asha, "2026-11-01", "2026-11-30")).PaidHolidays);
    }

    [Fact]
    public async Task Leave_without_pay_is_loss_of_pay_and_every_other_SCC_category_is_paid_leave_in_the_counts()
    {
        var h = await db.NewHotelAsync();
        var asha = await h.AddStaffAsync("Asha", weeklyOff: 2);
        foreach (var (type, date) in new[] { ("LOP", "2026-09-07"), ("OTH", "2026-09-09") })
            await db.ScalarAsync("""
                INSERT hr.leave_request(property_id, staff_id, leave_type, from_date, to_date, days_x2, reason, status, requested_by, requested_utc, decided_by, decided_utc,
                                        request_from, request_to)
                VALUES (@p, @s, @t, @d, @d, 2, N'x', 'Approved', @a, '2026-09-01', @b, '2026-09-02', @d, @d);
                """, ("@p", h.PropertyId), ("@s", asha), ("@t", type), ("@d", date), ("@a", h.U("reception")), ("@b", h.U("manager")));
        var days = await DaysOf(h, asha, "2026-09-01", "2026-09-30");
        Assert.Equal([false, true], days.Leaves.OrderBy(l => l.From).Select(l => l.IsPaid));
        var c = PayrollRules.Counts(await ProfileOf(h, asha), days, new PayMonth(2026, 9));
        Assert.Equal((1m, 1m), (c.LopLeave, c.PaidLeave));
    }

    [Fact]
    public async Task Staff_pick_a_designation_by_code_department_and_retirement_are_checked_free_text_unlinks()
    {
        var h = await db.NewHotelAsync();
        await h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation"), new SaveDesignationRequest(null, "CHEF", "Head chef", null, "KIT"), Ct);
        await h.Lists.SaveDesignationAsync(h.As("owner", "SaveDesignation"), new SaveDesignationRequest(null, "ANY", "Trainee"), Ct);
        var asha = await h.AddStaffAsync("Asha", dept: "KIT", designation: "ignored", designationCode: "CHEF");
        var list = await h.Db.PayrollQueries.GetStaffAsync(h.PropertyId, h.U("owner"), Ct);
        var row = list.Single(x => x.StaffId == asha);
        Assert.Equal(("Head chef", "CHEF"), (row.Designation, row.DesignationCode));
        Assert.Equal(1, (await h.Queries.DesignationsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "CHEF").Used);
        await Hotel.FailsAsync(() => h.AddStaffAsync("Bala", dept: "FO", designationCode: "CHEF"), HccErrorCode.VALIDATION);      // belongs to KIT
        await Hotel.FailsAsync(() => h.AddStaffAsync("Chitra", dept: "FO", designationCode: "NOPE"), HccErrorCode.VALIDATION);
        var dev = await h.AddStaffAsync("Dev", dept: "FO", designationCode: "ANY");
        Assert.Equal("Trainee", (await h.Db.PayrollQueries.GetStaffAsync(h.PropertyId, h.U("owner"), Ct)).Single(x => x.StaffId == dev).Designation);
        // Editing with free text moves the printed name and drops the link; retired designations cannot be newly chosen but stay on records that have them.
        var save = async (string? code, string designation) =>
        {
            var cur = (await h.Db.PayrollQueries.GetStaffAsync(h.PropertyId, h.U("owner"), Ct)).Single(x => x.StaffId == asha);
            return await h.Db.Payroll.SaveStaffAsync(h.As("owner", PayrollService.OpSaveStaff), new SaveStaffRequest(asha, cur.Version, "Asha", "KIT", designation, "M", 2, DesignationCode: code), Ct);
        };
        var chef = (await h.Queries.DesignationsAsync(h.PropertyId, h.U("owner"), ct: Ct)).Items.Single(x => x.Code == "CHEF");
        await h.Lists.RetireDesignationAsync(h.As("owner", "RetireDesignation"), new RetireDesignationRequest(chef.DesignationId, chef.Version), Ct);
        await save("CHEF", "Head chef");   // unchanged retired choice stays valid
        await Hotel.FailsAsync(() => h.AddStaffAsync("Eve", dept: "KIT", designationCode: "CHEF"), HccErrorCode.INVALID_TRANSITION);
        await save(null, "Sous chef");
        var after = (await h.Db.PayrollQueries.GetStaffAsync(h.PropertyId, h.U("owner"), Ct)).Single(x => x.StaffId == asha);
        Assert.Equal("Sous chef", after.Designation);
        Assert.Null(after.DesignationCode);
    }

    [Fact]
    public async Task Compliance_calendar_keeps_SCC_due_dates_on_paid_holidays_of_the_calendar()
    {
        // SCC tax port (DEC-116): SCC never moves a due date, so a paid holiday of the Holiday calendar no longer shifts the compliance calendar.
        var h = await db.NewHotelAsync();
        await SaveHoliday(h, "2026-08-15");
        await SaveHoliday(h, "2026-08-18", "restricted");
        var store = new SqlComplianceStore(h.Db.Platform.Connections);
        var cal = (await store.SnapshotAsync(h.PropertyId, Ct)).Calendar();
        var (cards, _) = cal.MonthPage("August", 2026, new DateOnly(2026, 8, 14));
        Assert.Equal(new DateOnly(2026, 8, 15), cards.Single(v => v.Card.Id == "ag5").Due);   // EPF July wages, Sat 15 Aug (paid holiday): unchanged
    }
}
