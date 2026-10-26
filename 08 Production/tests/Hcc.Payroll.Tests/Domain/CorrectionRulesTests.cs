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
        // Before: 1 absent day = 2 half-days of loss of pay → earned 13,500 × 58 / 60 = ₹13,050.00.
        Assert.Equal(1_305_000, PayrollCorrections.Gross(st, days, Sep, R));
        long Effect(DayEntry e, DateOnly d) =>
            PayrollCorrections.Gross(st, PayrollCorrections.With(days, st, d, e, PayrollCorrections.Current(days, d).OneDayLeave?.LeaveId), Sep, R)
            - PayrollCorrections.Gross(st, days, Sep, R);
        // Absent → present: 60 / 60 → ₹13,500.00, raises gross by ₹450.00 (Owner's approval).
        Assert.Equal(45_000, Effect(DayEntry.Mark(AttendanceStatus.P, 0), D(22)));
        // Absent → casual leave (paid): also +₹450.00; absent → leave without pay: no change (applies at once).
        Assert.Equal(45_000, Effect(DayEntry.Leave(LeaveType.CL), D(22)));
        Assert.Equal(0, Effect(DayEntry.Leave(LeaveType.LOP), D(22)));
        // Present → absent: −₹450.00 (applies at once). One more late mark (1 < 3 late marks per ½ day): no change.
        Assert.Equal(-45_000, Effect(DayEntry.Mark(AttendanceStatus.A, 0), D(21)));
        Assert.Equal(0, Effect(DayEntry.Mark(AttendanceStatus.L, 0), D(21)));
        // 60 minutes' overtime on a present day: 13,500 ÷ 30 days ÷ 8 h × 1 h × 1 = ₹56.25 more.
        Assert.Equal(5_625, Effect(DayEntry.Mark(AttendanceStatus.P, 60), D(21)));
        // Clearing a present mark leaves the day unmarked = absent for pay: −₹450.00.
        Assert.Equal(-45_000, Effect(DayEntry.Unmarked, D(21)));
    }

    [Fact]
    public void DEC075_day_entries_parse_and_the_day_state_names_what_a_correction_replaces()
    {
        Assert.Equal(DayEntry.Mark(AttendanceStatus.OFF, 120), DayEntry.Parse("OFF", 120));
        Assert.Equal(DayEntry.Leave(LeaveType.SL), DayEntry.Parse("SL", null));
        Assert.Equal(DayEntry.Unmarked, DayEntry.Parse("none", 0));
        foreach (var (code, ot) in new[] { ("A", 30), ("CL", 10), ("P", 481), ("none", 5), ("X", 0), ("p", 0) })
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
    public void DEC083_paid_holiday_counts_like_a_weekly_off_monthly_paid_daily_wage_as_weekly_offs()
    {
        // Fictional holiday Thu 17 Sep 2026. Weekly off Sunday: Sundays 6, 13, 20, 27 Sep → 4 weekly offs, 25 working days marked present.
        var holidays = new Dictionary<DateOnly, string> { [D(17)] = "Founders’ Day (fictional)" };
        var monthly = Staff(PayType.Monthly, 3_000_000, 0, 0);
        var mDays = September(monthly, holidays, skip: [D(17)]);
        var mc = PayrollRules.Counts(monthly, mDays, Sep);
        Assert.Equal((25m, 4m, 1m, 0m), (mc.P, mc.Off, mc.Hol, mc.Unmarked));
        var m = PayrollRules.ComputeLine(monthly, mc, R);
        // Monthly ₹30,000: the holiday is paid → 30 of 30 days → earned ₹30,000.00; basic 50 % ₹15,000.00 (= PF cap) → PF 12 % ₹1,800.00,
        // employer 13 % ₹1,950.00; HRA 80 % of basic ₹12,000.00, washing 10 % ₹1,500.00, special ₹1,500.00; ESIC: contract ₹30,000 > ₹21,000 →
        // not covered; PT (man, above ₹10,000, September) ₹200.00 → net 30,000 − 1,800 − 200 = ₹28,000.00.
        Assert.Equal((30m, 3_000_000L, 1_500_000L, 1_200_000L, 150_000L, 150_000L), (m.PayableDays, m.Earned, m.Basic, m.Hra, m.Washing, m.Special));
        Assert.Equal((180_000L, 195_000L, 0L, 20_000L, 2_800_000L), (m.Pf, m.PfEr, m.Esic, m.Pt, m.Net));
        // Without the calendar entry the same day is unmarked = absent: 58 / 60 → ₹29,000.00, and the run warns with the correction hint.
        var noHol = PayrollRules.ComputeLine(monthly, PayrollRules.Counts(monthly, September(monthly, skip: [D(17)]), Sep), R);
        Assert.Equal(2_900_000, noHol.Earned);
        Assert.Contains("Attendance › Past days", PayrollRules.Warnings(monthly, PayrollRules.Counts(monthly, September(monthly, skip: [D(17)]), Sep)).Single(), StringComparison.Ordinal);

        // Daily wage ₹800 (basic only, PF off by default): days worked only, the holiday like a weekly off is not paid (CA A04-04 still open) →
        // 25 × 800 = ₹20,000.00; ESIC covered (contract 800 × 26 = ₹20,800 ≤ ₹21,000) on ₹20,000.00 → 0.75 % ₹150.00, employer 3.25 % ₹650.00;
        // PT ₹200.00 → net 20,000 − 150 − 200 = ₹19,650.00.
        var daily = Staff(PayType.Daily, 0, 80_000, 0);
        var dc = PayrollRules.Counts(daily, September(daily, holidays, skip: [D(17)]), Sep);
        var dl = PayrollRules.ComputeLine(daily, dc, R);
        Assert.Equal((1m, 25m, 2_000_000L, 15_000L, 65_000L, 20_000L, 1_965_000L), (dc.Hol, dl.PayableDays, dl.Earned, dl.Esic, dl.EsicEr, dl.Pt, dl.Net));
        // Worked on the holiday (marked present): a normal paid day — daily 26 × 800 = ₹20,800.00; monthly stays ₹30,000.00.
        Assert.Equal(2_080_000, PayrollRules.ComputeLine(daily, PayrollRules.Counts(daily, September(daily, holidays), Sep), R).Earned);
        Assert.Equal(3_000_000, PayrollRules.ComputeLine(monthly, PayrollRules.Counts(monthly, September(monthly, holidays), Sep), R).Earned);
        // Leave skips the holiday as it skips weekly offs: CL Wed 16 – Fri 18 Sep = 2 days; a half day on the holiday = 0.
        Assert.Equal(2m, PayrollRules.LeaveDays(monthly, mDays, D(16), D(18), null));
        Assert.Equal(0m, PayrollRules.LeaveDays(monthly, mDays, D(17), D(17), LeaveHalf.PM));
        Assert.True(PayrollRules.DayOff(monthly, mDays, D(17)) && PayrollRules.DayOff(monthly, mDays, D(20)) && !PayrollRules.DayOff(monthly, mDays, D(18)));
    }
}
