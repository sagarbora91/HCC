using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Payroll;

namespace Hcc.Payroll.Tests.Domain;

/// <summary>Wave B item B4 rules without SQL: DEC-075 correction window and raises-pay test, DEC-083 paid holidays (figures by hand in the comments).</summary>
public sealed class CorrectionRulesTests
{
    private static readonly PayrollRates R = PayrollRates.Defaults();

    private static readonly PayMonth Sep = new(2026, 9);

    /// <summary>A fictional staff member (staff id 1), joined 1 Apr 2025, morning shift.</summary>
    private static StaffProfile Staff(PayType type, long monthly, long daily, int weeklyOff) =>
        new(1, "Test Person", "FO", "x", type, monthly, daily, ShiftCode.M, weeklyOff, new DateOnly(2025, 4, 1), null, true, Gender.M);

    /// <summary>Present on every rostered working day of September 2026 except <paramref name="skip"/> (unmarked) and <paramref name="absent"/>.</summary>
    private static StaffDays September(StaffProfile st, IReadOnlyDictionary<DateOnly, string>? holidays = null, DateOnly[]? skip = null, DateOnly? absent = null,
        IReadOnlyList<LeaveSpan>? leaves = null)
    {
        var probe = new StaffDays();
        var att = new Dictionary<DateOnly, AttendanceMark>();
        foreach (var d in Sep.Dates())
        {
            if (PayrollRules.ShiftFor(st, probe, d) == ShiftCode.OFF || (skip ?? []).Contains(d)) continue;
            att[d] = new AttendanceMark(d, d == absent ? AttendanceStatus.A : AttendanceStatus.P, 0);
        }

        return new StaffDays(null, att, leaves, holidays);
    }

    private static DateOnly D(int day) => new(2026, 9, day);

    [Fact]
    public void DEC075_correction_window_is_the_open_pay_month_after_the_latest_locked_run_and_never_today()
    {
        var today = new DateOnly(2026, 10, 24);
        Assert.Equal((D(1), new DateOnly(2026, 10, 23)), PayrollCorrections.Window(today, new PayMonth(2026, 8)));   // August locked: September + October so far
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 23)), PayrollCorrections.Window(today, Sep));   // September locked: October only
        Assert.Equal((D(1), new DateOnly(2026, 10, 23)), PayrollCorrections.Window(today, null));   // nothing locked: the month before at the earliest
        Assert.Equal((D(1), new DateOnly(2026, 10, 23)), PayrollCorrections.Window(today, new PayMonth(2026, 6)));   // an old lock does not reopen July / August
        var (from, to) = PayrollCorrections.Window(new DateOnly(2026, 11, 1), new PayMonth(2026, 10));
        Assert.True(to < from, "1 Nov with October locked: nothing to correct yet");
    }

    [Fact]
    public void DEC075_raises_pay_is_the_change_of_the_month_lines_gross_pay()
    {
        // ₹13,500 a month, weekly off Friday: September 2026 has 30 days, 4 Fridays (4, 11, 18, 25 Sep) → 26 working days, absent on 22 Sep.
        var st = Staff(PayType.Monthly, 1_350_000, 0, 5);
        var days = September(st, absent: D(22));
        // SCC sheet (DEC-115): 25 present, absent 4 offs + 22 Sep = 5 → deduction 5, +4 extra → 29 salary days → 13,500 × 29 / 30 = ₹13,050.00.
        Assert.Equal(1_305_000, PayrollCorrections.Gross(st, days, Sep, R));
        long Effect(DayEntry e, DateOnly d) =>
            PayrollCorrections.Gross(st, PayrollCorrections.With(days, st, d, e, PayrollCorrections.Current(days, d).OneDayLeave?.LeaveId), Sep, R)
            - PayrollCorrections.Gross(st, days, Sep, R);
        // Absent → present: deduction 4 → 26 + 4 = 30 → ₹13,500.00, raises gross by ₹450.00 (Owner's approval).
        Assert.Equal(45_000, Effect(DayEntry.Mark(AttendanceStatus.P, 0), D(22)));
        // Absent → casual leave: one absent day fewer and SCC adds the leave on top → 26 + 1 + 4 = 31 → 30 days + 1 OT day: +₹900.00;
        // absent → leave without pay: still absent, no change (applies at once).
        Assert.Equal(90_000, Effect(DayEntry.Leave(LeaveType.CL), D(22)));
        Assert.Equal(0, Effect(DayEntry.Leave(LeaveType.LOP), D(22)));
        // Present → absent: deduction 6 → only 2 extra days → 24 + 2 = 26 → ₹11,700.00: −₹1,350.00 (applies at once). One more late mark
        // (1 < 3 late marks per ½ day): no change.
        Assert.Equal(-135_000, Effect(DayEntry.Mark(AttendanceStatus.A, 0), D(21)));
        Assert.Equal(0, Effect(DayEntry.Mark(AttendanceStatus.L, 0), D(21)));
        // 60 minutes' overtime on a present day: SCC pays OT days, not minutes → no change.
        Assert.Equal(0, Effect(DayEntry.Mark(AttendanceStatus.P, 60), D(21)));
        // Clearing a present mark leaves the day unmarked = absent for pay: −₹1,350.00.
        Assert.Equal(-135_000, Effect(DayEntry.Unmarked, D(21)));
        // SCC No Thumb: still a present day, but ½ day deduction → 5.5 (still 4 extra days) → 28.5 salary days → ₹12,825.00: −₹225.00.
        Assert.Equal(-22_500, Effect(DayEntry.Mark(AttendanceStatus.NT, 0), D(21)));
        Assert.Equal("No thumb punch", DayEntry.Mark(AttendanceStatus.NT, 0).Label);
    }

    [Fact]
    public void DEC075_day_entries_parse_and_the_day_state_names_what_a_correction_replaces()
    {
        Assert.Equal(DayEntry.Mark(AttendanceStatus.OFF, 120), DayEntry.Parse("OFF", 120));
        Assert.Equal(DayEntry.Leave(LeaveType.SL), DayEntry.Parse("SL", null));
        Assert.Equal(DayEntry.Unmarked, DayEntry.Parse("none", 0));
        foreach (var (code, ot) in new[] { ("A", 30), ("CL", 10), ("P", 481), ("none", 5), ("X-1", 0), ("p", 0) })   // masters integration: "X" is a valid MA-6 leave-type code now (the service refuses an unknown type); old: ("X", 0)
            Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => DayEntry.Parse(code, ot)).Code);
        Assert.Equal("Present · OT 60 min → Casual leave", $"{DayEntry.Mark(AttendanceStatus.P, 60).Label} → {DayEntry.Leave(LeaveType.CL).Label}");

        var st = Staff(PayType.Monthly, 1_350_000, 0, 5);
        var one = new LeaveSpan(7, 1, LeaveType.CL, D(8), D(8), null, 1m, LeaveStatus.Approved);
        var two = new LeaveSpan(8, 1, LeaveType.EL, D(14), D(15), null, 2m, LeaveStatus.Approved);
        var half = new LeaveSpan(9, 1, LeaveType.CL, D(21), D(21), LeaveHalf.AM, 0.5m, LeaveStatus.Approved);
        var pending = new LeaveSpan(10, 1, LeaveType.SL, D(23), D(23), null, 1m, LeaveStatus.Pending);
        var days = September(st, skip: [D(8), D(14), D(15), D(23)], leaves: [one, two, half, pending]);
        Assert.Equal(new DayState(DayEntry.Leave(LeaveType.CL), one, false, null, false), PayrollCorrections.Current(days, D(8)));   // a one-day leave is replaced
        Assert.Equal(two, PayrollCorrections.Current(days, D(14)).MultiDayLeave);                                                       // several days: refused
        Assert.True(PayrollCorrections.Current(days, D(21)).HalfDayLeave);
        Assert.True(PayrollCorrections.Current(days, D(23)).PendingLeave);
        Assert.Equal(DayEntry.Mark(AttendanceStatus.P, 0), PayrollCorrections.Current(days, D(22)).Entry);
        // The replaced one-day leave is cancelled in the corrected days, and the counts follow: 8 Sep becomes a present day.
        var after = PayrollCorrections.With(days, st, D(8), DayEntry.Mark(AttendanceStatus.P, 0), one.LeaveId);
        Assert.Equal(LeaveStatus.Cancelled, after.Leaves.Single(l => l.LeaveId == 7).Status);
        Assert.Equal(PayrollRules.Counts(st, days, Sep).PaidLeave - 1, PayrollRules.Counts(st, after, Sep).PaidLeave);
    }

    [Fact]
    public void DEC083_DEC115_paid_holiday_shortens_the_total_days_and_nobody_loses_pay()
    {
        // Fictional holiday Thu 17 Sep 2026. Weekly off Sunday: Sundays 6, 13, 20, 27 Sep → 4 weekly offs, 25 working days marked present.
        var holidays = new Dictionary<DateOnly, string> { [D(17)] = "Founders’ Day (fictional)" };
        var monthly = Staff(PayType.Monthly, 3_000_000, 0, 0);
        var mDays = September(monthly, holidays, skip: [D(17)]);
        var mc = PayrollRules.Counts(monthly, mDays, Sep);
        Assert.Equal((25m, 4m, 1m, 0m), (mc.P, mc.Off, mc.Hol, mc.Unmarked));
        var m = PayrollRules.ComputeLine(monthly, mc, R);
        // SCC (DEC-115): total days 30 − 1 holiday = 29; 25 present, 4 weekly offs = 4 absent → +4 extra → 29 of 29. Monthly ₹30,000 → earned ₹30,000.00; basic 50 % ₹15,000.00 (= PF cap) → PF 12 % ₹1,800.00,
        // employer 13 % ₹1,950.00; HRA 80 % of basic ₹12,000.00, washing 10 % ₹1,500.00, special ₹1,500.00; ESIC: contract ₹30,000 > ₹21,000 →
        // not covered; PT (man, above ₹10,000, September) ₹200.00 → net 30,000 − 1,800 − 200 = ₹28,000.00.
        Assert.Equal((29m, 3_000_000L, 1_500_000L, 1_200_000L, 150_000L, 150_000L), (m.PayableDays, m.Earned, m.Basic, m.Hra, m.Washing, m.Special));
        Assert.Equal((180_000L, 195_000L, 0L, 20_000L, 2_800_000L), (m.Pf, m.PfEr, m.Esic, m.Pt, m.Net));
        // Without the calendar entry the same day is unmarked = absent: 30 total days, 5 absent → 25 + 4 = 29 of 30 → ₹29,000.00, and the run warns.
        var noHol = PayrollRules.ComputeLine(monthly, PayrollRules.Counts(monthly, September(monthly, skip: [D(17)]), Sep), R);
        Assert.Equal(2_900_000, noHol.Earned);
        Assert.Contains("Attendance › Past days", PayrollRules.Warnings(monthly, PayrollRules.Counts(monthly, September(monthly, skip: [D(17)]), Sep)).Single(), StringComparison.Ordinal);

        // Daily wage ₹800 (basic only, PF off by default): gross = 800 × 29 total days; 29 salary days → ₹23,200.00; ESIC month by month: earned
        // above ₹21,000 → none; PT ₹200.00 → net ₹23,000.00.
        var daily = Staff(PayType.Daily, 0, 80_000, 0);
        var dc = PayrollRules.Counts(daily, September(daily, holidays, skip: [D(17)]), Sep);
        var dl = PayrollRules.ComputeLine(daily, dc, R);
        Assert.Equal((1m, 29m, 2_320_000L, 0L, 0L, 20_000L, 2_300_000L), (dc.Hol, dl.PayableDays, dl.Earned, dl.Esic, dl.EsicEr, dl.Pt, dl.Net));
        // Worked on the holiday (marked present): 26 present, 3 absent → 26 + 4 = 30 → 29 salary days + 1 OT day — daily ₹23,200.00 + ₹800.00 OT;
        // monthly stays ₹30,000.00 earned.
        var worked = PayrollRules.ComputeLine(daily, PayrollRules.Counts(daily, September(daily, holidays), Sep), R);
        Assert.Equal((2_320_000L, 80_000L), (worked.Earned, worked.Ot));
        Assert.Equal(3_000_000, PayrollRules.ComputeLine(monthly, PayrollRules.Counts(monthly, September(monthly, holidays), Sep), R).Earned);
        // Leave skips the holiday as it skips weekly offs: CL Wed 16 – Fri 18 Sep = 2 days; a half day on the holiday = 0.
        Assert.Equal(2m, PayrollRules.LeaveDays(monthly, mDays, D(16), D(18), null));
        Assert.Equal(0m, PayrollRules.LeaveDays(monthly, mDays, D(17), D(17), LeaveHalf.PM));
        Assert.True(PayrollRules.DayOff(monthly, mDays, D(17)) && PayrollRules.DayOff(monthly, mDays, D(20)) && !PayrollRules.DayOff(monthly, mDays, D(18)));
    }
}
