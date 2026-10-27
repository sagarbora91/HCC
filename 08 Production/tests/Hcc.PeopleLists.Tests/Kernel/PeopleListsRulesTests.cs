using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;
using Hcc.Domain.PeopleLists;
using Hcc.Domain.Payroll;

namespace Hcc.PeopleLists.Tests.Kernel;

/// <summary>People lists rules without a database (prototype domain/streams/people-masters.ts parity).</summary>
public class PeopleListsRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private static string Field(HccException e) => e.Details!["field"].GetString()!;

    private static HccException Bad(Action a) => Assert.Throws<HccException>(a);

    [Fact]
    public void Designation_department_grade_and_pay_rule_are_checked()
    {
        var ok = PeopleListsRules.Designation(" chef ", " Head chef ", null, " kit ", " G3 ", "monthly-basic");
        Assert.Equal(("CHEF", "Head chef"), (ok.Code, ok.Name));
        Assert.Equal(("KIT", "G3", "monthly-basic"), ((string?)ok.Fields["dept"], (string?)ok.Fields["grade"], (string?)ok.Fields["defaultPayRule"]));
        Assert.Null(PeopleListsRules.Designation("CHEF", "Chef", null, "", null, null).Fields["dept"]);
        Assert.Equal("dept", Field(Bad(() => PeopleListsRules.Designation("CHEF", "Chef", null, "K", null, null))));
        Assert.Equal("grade", Field(Bad(() => PeopleListsRules.Designation("CHEF", "Chef", null, null, "G3 #", null))));
        var rule = Bad(() => PeopleListsRules.Designation("CHEF", "Chef", null, null, null, "weekly"));
        Assert.Equal((MasterReasons.Invalid, "defaultPayRule"), (Reason(rule), Field(rule)));
    }

    [Fact]
    public void Shift_pattern_code_times_break_night_and_weekly_off_rule_are_checked()
    {
        var ok = PeopleListsRules.ShiftPattern("s1", "Split", null, "10:00", "20:00", 60, false, null);
        Assert.Equal(("S1", "fixed", 60), (ok.Code, (string?)ok.Fields["weeklyOffRule"], (int?)ok.Fields["breakMinutes"]));
        Assert.Equal("code", Field(Bad(() => PeopleListsRules.ShiftPattern("OFF", "Off", null, "10:00", "20:00", 0, false, null))));
        Assert.Equal("code", Field(Bad(() => PeopleListsRules.ShiftPattern("ABCD", "Long", null, "10:00", "20:00", 0, false, null))));
        Assert.Equal("start", Field(Bad(() => PeopleListsRules.ShiftPattern("S2", "x", null, "9:00", "20:00", 0, false, null))));
        Assert.Equal("end", Field(Bad(() => PeopleListsRules.ShiftPattern("S2", "x", null, "09:00", "24:00", 0, false, null))));
        Assert.Equal("end", Field(Bad(() => PeopleListsRules.ShiftPattern("S2", "x", null, "09:00", "09:00", 0, false, null))));
        Assert.Equal("night", Field(Bad(() => PeopleListsRules.ShiftPattern("S2", "x", null, "20:00", "04:00", 0, false, null))));
        Assert.Equal(480, PeopleListsRules.ShiftMinutes(new TimeOnly(20, 0), new TimeOnly(4, 0)));
        Assert.Equal("breakMinutes", Field(Bad(() => PeopleListsRules.ShiftPattern("S2", "x", null, "09:00", "10:00", 90, false, null))));
        Assert.Equal("breakMinutes", Field(Bad(() => PeopleListsRules.ShiftPattern("S2", "x", null, "06:00", "23:00", 241, false, null))));
        Assert.Equal("weeklyOffRule", Field(Bad(() => PeopleListsRules.ShiftPattern("S2", "x", null, "09:00", "18:00", 0, false, "rotating"))));
        Assert.Equal("roster", (string?)PeopleListsRules.ShiftPattern("S2", "x", null, "09:00", "18:00", 0, false, "roster").Fields["weeklyOffRule"]);
    }

    [Fact]
    public void Leave_type_quota_carry_forward_encashment_and_unpaid_rules()
    {
        var cl = PeopleListsRules.LeaveType("cl", "Casual leave", null, true, 12, 3, false);
        Assert.Equal(("CL", 12, 3), (cl.Code, (int?)cl.Fields["yearlyQuota"], (int?)cl.Fields["carryForward"]));
        Assert.Equal("carryForward", Field(Bad(() => PeopleListsRules.LeaveType("BL", "Bereavement", null, true, null, 2, false))));
        Assert.Equal("paid", Field(Bad(() => PeopleListsRules.LeaveType("UL", "Unpaid", null, false, 2, 0, false))));
        Assert.Equal("paid", Field(Bad(() => PeopleListsRules.LeaveType("UL", "Unpaid", null, false, null, 0, true))));
        Assert.Equal("yearlyQuota", Field(Bad(() => PeopleListsRules.LeaveType("BL", "Bereavement", null, true, 61, 0, false))));
        Assert.Equal("code", Field(Bad(() => PeopleListsRules.LeaveType("TOOLONG1", "x", null, true, null, 0, false))));
        Assert.Null(PeopleListsRules.LeaveType("UL", "Unpaid", null, false, null, 0, false).Fields["yearlyQuota"]);
    }

    [Fact]
    public void Holiday_date_kind_and_financial_year()
    {
        var h = PeopleListsRules.Holiday("2026-11-08", " Diwali ", null, "paid");
        Assert.Equal(("2026-11-08", "Diwali", "paid"), (h.Code, h.Name, (string?)h.Fields["kind"]));
        Assert.Equal("code", Field(Bad(() => PeopleListsRules.Holiday("2026-02-30", "x", null, "paid"))));
        Assert.Equal("code", Field(Bad(() => PeopleListsRules.Holiday("2026-2-3", "x", null, "paid"))));
        Assert.Equal("kind", Field(Bad(() => PeopleListsRules.Holiday("2026-11-08", "x", null, null))));
        Assert.Equal("kind", Field(Bad(() => PeopleListsRules.Holiday("2026-11-08", "x", null, "optional"))));
        Assert.Equal(["FY 2026–27", "FY 2026–27", "FY 2027–28", "FY 2099–00"], new[] { "2026-08-15", "2027-01-26", "2027-04-01", "2099-12-31" }.Select(PeopleListsRules.Fy));
    }

    [Fact]
    public void Seeds_reproduce_todays_constants()
    {
        Assert.Equal(["M 07:00-15:00", "E 15:00-23:00", "N 23:00-07:00", "G 09:00-18:00"], PeopleListsRules.SeedShiftPatterns().Select(r => $"{r.Code} {r.Fields["start"]}-{r.Fields["end"]}"));
        Assert.True((bool)PeopleListsRules.SeedShiftPatterns().Single(r => r.Code == "N").Fields["night"]!);
        var leave = PeopleListsRules.SeedLeaveTypes(10, 5, 20);
        Assert.Equal([10, 5, 20], leave.Take(3).Select(r => (int)r.Fields["yearlyQuota"]!));
        Assert.Equal(["CL", "SL", "EL", "CO", "ML", "LOP"], leave.Select(r => r.Code));
        Assert.Equal([true, true, true, true, true, false], leave.Select(r => (bool)r.Fields["paid"]!));
        Assert.True((bool)leave.Single(r => r.Code == "EL").Fields["encashment"]!);
        var d = PeopleListsRules.SeedDesignations([("Cook", "KIT", false, null), ("cook", "KIT", false, null), ("Kitchen helper (daily wage)", "KIT", true, "B"), ("Security guard (day)", "SEC", false, "B"), ("Hotel manager", "MGT", false, "S")]);
        Assert.Equal(["COOK", "KITCHEN-HELPER-DAILY", "SECURITY-GUARD-DAY", "HOTEL-MANAGER"], d.Select(r => r.Code));
        Assert.Equal(["monthly-structured", "daily", "monthly-basic", "monthly-structured"], d.Select(r => (string?)r.Fields["defaultPayRule"]));
        Assert.Equal("DESIG", PeopleListsRules.SlugCode("***"));
    }

    [Fact]
    public void Slug_codes_stay_unique()
    {
        var d = PeopleListsRules.SeedDesignations([("Front desk!", "FO", false, null), ("Front desk?", "FO", false, null)]);
        Assert.Equal(["FRONT-DESK", "FRONT-DESK-2"], d.Select(r => r.Code));
    }

    [Fact]
    public void Diff_lists_changed_fields_in_fixed_order_with_people_readable_values()
    {
        var before = new Dictionary<string, object?> { ["paid"] = true, ["yearlyQuota"] = 12, ["carryForward"] = 0, ["encashment"] = false };
        var after = PeopleListsRules.LeaveType("CL", "Casual leave", 10, true, 10, 3, false);
        var diff = PeopleListsRules.Diff(PeopleList.LeaveTypes, "CL", "Casual leave", 10, before, after, 10);
        Assert.Equal([("yearlyQuota", "12", "10"), ("carryForward", "0", "3")], diff);
        var added = PeopleListsRules.Diff(PeopleList.Holidays, null, null, null, null, PeopleListsRules.Holiday("2026-11-08", "Diwali", null, "paid"), 10);
        Assert.Equal(["code", "name", "sortOrder", "kind"], added.Select(x => x.Field));
        Assert.All(added, x => Assert.Null(x.Old));
        Assert.Equal("no", PeopleListsRules.Show(false));
        Assert.Null(PeopleListsRules.Show(null));
    }

    // ── Payroll rules with the lists ──

    private static StaffProfile Staff(PayType type = PayType.Monthly, string shift = "M", int weeklyOff = 2, bool auto = true) =>
        new(1, "Test", "FO", "Associate", type, type == PayType.Monthly ? 3_100_000 : 0, type == PayType.Daily ? 60_000 : 0, new ShiftCode(shift), weeklyOff,
            new DateOnly(2025, 4, 1), null, true, WeeklyOffAuto: auto);

    [Fact]
    public void Weekly_off_follows_the_staff_record_unless_the_pattern_leaves_it_to_the_roster()
    {
        var tue = new DateOnly(2026, 10, 27);
        Assert.Equal(ShiftCode.OFF, PayrollRules.ShiftFor(Staff(), new StaffDays(), tue));
        Assert.Equal(new ShiftCode("M"), PayrollRules.ShiftFor(Staff(auto: false), new StaffDays(), tue));
        var rostered = new StaffDays(new Dictionary<DateOnly, ShiftCode> { [tue] = ShiftCode.OFF });
        Assert.Equal(ShiftCode.OFF, PayrollRules.ShiftFor(Staff(auto: false), rostered, tue));
    }

    [Fact]
    public void Paid_holidays_are_not_leave_days_and_count_as_paid_days_without_attendance()
    {
        var holiday = new DateOnly(2026, 11, 8);   // a Sunday; staff off day is Tuesday
        var days = new StaffDays(paidHolidays: new HashSet<DateOnly> { holiday });
        Assert.Equal(2m, PayrollRules.LeaveDays(Staff(), days, new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 9), null));
        Assert.Equal(0m, PayrollRules.LeaveDays(Staff(), days, holiday, holiday, LeaveHalf.AM));
        var month = new PayMonth(2026, 11);
        var c = PayrollRules.Counts(Staff(), days, month, new DateOnly(2026, 11, 8));
        Assert.Equal((1m, 6m), (c.Hol, c.Unmarked));   // 1–8 Nov: one Tuesday off (3 Nov), 8 Nov holiday, the other six unmarked
        var withAtt = new StaffDays(attendance: new Dictionary<DateOnly, AttendanceMark> { [holiday] = new(holiday, AttendanceStatus.A, 0) }, paidHolidays: new HashSet<DateOnly> { holiday });
        var a = PayrollRules.Counts(Staff(), withAtt, month, holiday);
        Assert.Equal((0m, 1m), (a.Hol, a.A));   // explicit absence stays absent
        var offDay = new DateOnly(2026, 11, 3);
        var onOff = PayrollRules.Counts(Staff(), new StaffDays(paidHolidays: new HashSet<DateOnly> { offDay }), month, offDay);
        Assert.Equal((0m, 1m), (onOff.Hol, onOff.Off));   // a weekly off on a holiday stays a weekly off
    }

    [Fact]
    public void A_paid_holiday_shortens_the_total_days_SCC_Mgmt_holidays_and_nobody_loses_pay_DEC115()
    {
        var r = PayrollRates.Defaults();
        var daily = Staff(PayType.Daily);
        var none = new AttendanceCounts("2026-11", 30, 20, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0);
        var plus = none with { Hol = 1, MgmtHol = 1 };
        // DEC-115 (SCC): the holiday takes a day off the total and off the absent days alike — 20 salary days either way (deduction ≥ 7: no extra days).
        Assert.Equal(0m, PayrollRules.ComputeLine(daily, plus, r).PayableDays - PayrollRules.ComputeLine(daily, none, r).PayableDays);
        var monthly = Staff();
        var lop = new AttendanceCounts("2026-11", 30, 25, 0, 0, 0, 0, 0, 4, 0, 0, 1, 0, 0);
        Assert.Equal(29m, PayrollRules.ComputeLine(monthly, lop, r).PayableDays);                       // 1 unmarked = absent: 5 absent → 25 + 4 = 29 of 30
        var hol = PayrollRules.ComputeLine(monthly, lop with { Unmarked = 0, Hol = 1, MgmtHol = 1 }, r);   // the holiday: 29 total days, 4 absent → 29 of 29
        Assert.Equal((29m, 29m, monthly.MonthlyPaise), (hol.Days!.Total, hol.PayableDays, hol.Earned));
    }

    [Fact]
    public void Unpaid_spans_count_as_loss_of_pay_whatever_their_code()
    {
        var d = new DateOnly(2026, 11, 4);
        var unpaid = new LeaveSpan(1, 1, new LeaveType("UL"), d, d, null, 1m, LeaveStatus.Approved, Paid: false);
        var paid = unpaid with { Paid = true };
        var month = new PayMonth(2026, 11);
        Assert.Equal((1m, 0m), (PayrollRules.Counts(Staff(), new StaffDays(leaves: [unpaid]), month, d).LopLeave, PayrollRules.Counts(Staff(), new StaffDays(leaves: [unpaid]), month, d).PaidLeave));
        Assert.Equal((0m, 1m), (PayrollRules.Counts(Staff(), new StaffDays(leaves: [paid]), month, d).LopLeave, PayrollRules.Counts(Staff(), new StaffDays(leaves: [paid]), month, d).PaidLeave));
        Assert.False(new LeaveSpan(1, 1, LeaveType.LOP, d, d, null, 1m, LeaveStatus.Approved).IsPaid);
        Assert.True(new LeaveSpan(1, 1, LeaveType.CL, d, d, null, 1m, LeaveStatus.Approved).IsPaid);
    }

    private static LeavePolicy Policy(int cl = 12, int carry = 0) => new(cl, 7, 15, 2, new Dictionary<string, int>(), new Dictionary<string, LeaveTypeDef>
    {
        ["CL"] = new("CL", "Casual leave", true, cl, carry, false, true),
        ["BL"] = new("BL", "Bereavement", true, null, 0, false, true),
        ["UL"] = new("UL", "Unpaid", false, null, 0, false, true),
        ["XX"] = new("XX", "Retired", true, null, 0, false, false),
    });

    [Fact]
    public void Leave_policy_per_type_decides_known_paid_tracked_and_carry()
    {
        var p = Policy(carry: 3);
        Assert.Equal([LeaveType.CL], p.TrackedTypes());
        Assert.Equal((true, false, true), (p.IsTracked(LeaveType.CL), p.IsTracked(new LeaveType("BL")), p.IsKnown(new LeaveType("BL"))));
        Assert.False(p.IsKnown(new LeaveType("XX")));
        Assert.False(p.IsKnown(new LeaveType("ZZ")));
        Assert.False(p.IsPaid(new LeaveType("UL")));
        Assert.True(p.IsPaid(LeaveType.CL));
        Assert.Equal((12, 3, 0), (p.Entitled(LeaveType.CL), p.CarryCap(LeaveType.CL), p.Entitled(new LeaveType("BL"))));
        // Without a stored list the six built-in types and CL SL EL tracked with Cl Sl El (as before MA-6).
        var legacy = LeavePolicy.Defaults();
        Assert.Equal([LeaveType.CL, LeaveType.SL, LeaveType.EL], legacy.TrackedTypes());
        Assert.True(legacy.IsKnown(LeaveType.CO));
        Assert.False(legacy.IsKnown(new LeaveType("BL")));
        Assert.False(legacy.IsPaid(LeaveType.LOP));
        Assert.Equal(15, legacy.Entitled(LeaveType.EL));
    }

    [Fact]
    public void Carry_forward_is_unused_quota_up_to_the_limit_for_staff_who_were_there()
    {
        var p = Policy(carry: 3);
        var used = new LeaveSpan(1, 1, LeaveType.CL, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10), null, 10m, LeaveStatus.Approved);   // FY 2026–27: 10 of 12 used
        var fy27 = new DateOnly(2027, 5, 10);
        Assert.Equal(2, LeaveRules.Carried(p, [used], fy27, LeaveType.CL, new DateOnly(2025, 4, 1)));          // only 2 unused (limit 3)
        Assert.Equal(3, LeaveRules.Carried(p, [], fy27, LeaveType.CL, new DateOnly(2025, 4, 1)));              // 12 unused, limit 3
        Assert.Equal(0, LeaveRules.Carried(p, [], fy27, LeaveType.CL, new DateOnly(2027, 4, 15)));             // joined after the previous year
        Assert.Equal(0, LeaveRules.Carried(Policy(), [], fy27, LeaveType.CL, null));                          // no carry-forward set
        var b = LeaveRules.Balance(p, [used], fy27, new DateOnly(2025, 4, 1)).Single();
        Assert.Equal((14, 0m, 14m), (b.Entitled, b.Used, b.Left));   // 12 + 2 carried
        Assert.Equal(14m, LeaveRules.Available(p, [used], fy27, LeaveType.CL, new DateOnly(2025, 4, 1)));
    }

    [Fact]
    public void Check_in_follows_the_pattern_start_night_flag_and_off()
    {
        var catalog = new ShiftCatalog([new ShiftPatternDef("M", "Morning", new TimeOnly(8, 0), new TimeOnly(16, 0), 30, false, "fixed", true),
            new ShiftPatternDef("W", "Watch", new TimeOnly(20, 0), new TimeOnly(6, 0), 0, true, "fixed", true),
            new ShiftPatternDef("Q", "Quiet", new TimeOnly(9, 0), new TimeOnly(10, 0), 0, false, "roster", false)]);
        Assert.Equal((AttendanceStatus.P, AttendanceStatus.L), (Shifts.CheckinStatus(new TimeOnly(8, 10), new ShiftCode("M"), catalog), Shifts.CheckinStatus(new TimeOnly(8, 20), new ShiftCode("M"), catalog)));
        Assert.Equal(AttendanceStatus.P, Shifts.CheckinStatus(new TimeOnly(23, 0), new ShiftCode("W"), catalog));
        Assert.Equal(AttendanceStatus.P, Shifts.CheckinStatus(new TimeOnly(9, 30), ShiftCode.OFF, catalog));
        // The built-in M E N G when no list is stored; the default morning start is 07:00.
        Assert.Equal(AttendanceStatus.L, Shifts.CheckinStatus(new TimeOnly(7, 20), ShiftCode.M));
        Assert.Equal(AttendanceStatus.P, Shifts.CheckinStatus(new TimeOnly(1, 0), ShiftCode.N));
        Assert.Equal(new TimeOnly(9, 0), Shifts.Start(ShiftCode.G));
        Assert.True(catalog.IsActive(ShiftCode.OFF) && catalog.IsActive(new ShiftCode("M")) && !catalog.IsActive(new ShiftCode("Q")) && !catalog.IsActive(new ShiftCode("Z")));
        Assert.False(catalog.Find(new ShiftCode("Q"))!.WeeklyOffAuto);
        Assert.Equal(4, new ShiftCatalog().Patterns.Count);
    }

    [Fact]
    public void Shift_and_leave_codes_are_validated_as_values()
    {
        Assert.True(ShiftCode.IsValid("S1") && ShiftCode.IsValid("OFF") && !ShiftCode.IsValid("s1") && !ShiftCode.IsValid("ABCD") && !ShiftCode.IsValid(""));
        Assert.True(LeaveType.IsValid("BL") && !LeaveType.IsValid("TOOLONG") && !LeaveType.IsValid("b l"));
        Assert.Equal("M", ShiftCode.M.ToString());
        Assert.Equal(new ShiftCode("G"), ShiftCode.G);
        Assert.Throws<HccException>(() => ShiftCode.Parse("x!", "Choose a shift."));
        Assert.Throws<HccException>(() => LeaveType.Parse("", "Choose a leave type."));
    }

    // ── Compliance due dates (SCC tax port, DEC-116: SCC never moves a due date — the MA-6 paid-holiday / Sunday shift is gone) ──

    [Fact]
    public void Compliance_due_dates_do_not_move_off_paid_holidays_or_sundays()
    {
        var epfJuly = SccTaxCatalogue.BuiltIn("August").Single(c => c.Id == "ag5");
        Assert.Equal(new DateOnly(2026, 8, 15), SccTaxRules.ParseDue(epfJuly.Due, 2026));            // 15 Aug: a paid holiday in the demo, a Saturday — unchanged
        Assert.Equal(new DateOnly(2026, 10, 11), SccTaxRules.ParseDue("11 Oct", 2026));                // a Sunday — unchanged
        Assert.Equal(-70, SccTaxRules.DaysLeft(epfJuly.Due, 2026, new DateOnly(2026, 10, 24)));
    }
}
