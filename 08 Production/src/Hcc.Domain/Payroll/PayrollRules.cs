using System.Globalization;

namespace Hcc.Domain.Payroll;

/// <summary>Exact integer arithmetic (half-up on non-negative amounts, Int128 so large products never lose precision).</summary>
public static class PayMath
{
    /// <summary>round(a·b·c / den), half-up, for non-negative integers (prototype hrMulDiv).</summary>
    public static long MulDiv(long a, long b, long c, long den)
    {
        if (a < 0 || b < 0 || c < 0 || den <= 0) throw new ArgumentOutOfRangeException(nameof(a), "MulDiv takes non-negative operands and a positive denominator.");
        var n = (Int128)a * b * c;
        return checked((long)((n * 2 + den) / (2 * (Int128)den)));
    }

    /// <summary>ceil(a·b / den) for non-negative integers.</summary>
    public static long CeilDiv(long a, long b, long den)
    {
        if (a < 0 || b < 0 || den <= 0) throw new ArgumentOutOfRangeException(nameof(a), "CeilDiv takes non-negative operands and a positive denominator.");
        var n = (Int128)a * b;
        return checked((long)((n + den - 1) / den));
    }

    /// <summary>Percentage (bps) of a paise amount with the version's statutory rounding (rupee mode: PF nearest rupee, ESIC next rupee).</summary>
    public static long Pct(long paise, int bps, StatutoryRounding mode, bool esic = false)
    {
        if (mode == StatutoryRounding.Rupee)
            return checked((esic ? CeilDiv(paise, bps, 10000L * 100) : MulDiv(paise, bps, 1, 10000L * 100)) * 100);
        return MulDiv(paise, bps, 1, 10000);
    }
}

/// <summary>One shift pattern of the Shift patterns list (MA-6): duty times, unpaid break, night flag, weekly-off rule ("fixed" | "roster").</summary>
public sealed record ShiftPatternDef(string Code, string Name, TimeOnly Start, TimeOnly End, int BreakMinutes, bool Night, string WeeklyOffRule, bool Active)
{
    /// <summary>The weekly off follows the staff record unless the roster decides.</summary>
    public bool WeeklyOffAuto => WeeklyOffRule != "roster";
}

/// <summary>The property's shift patterns by code. Without a stored list the built-in M E N G (07–15, 15–23, 23–07, 09–18; N is a night shift).</summary>
public sealed class ShiftCatalog
{
    private readonly IReadOnlyDictionary<string, ShiftPatternDef> _byCode;

    public ShiftCatalog(IEnumerable<ShiftPatternDef>? patterns = null)
    {
        var list = patterns?.ToList() ?? [];
        Patterns = list.Count > 0 ? list : Defaults;
        _byCode = Patterns.ToDictionary(p => p.Code);
    }

    public static readonly IReadOnlyList<ShiftPatternDef> Defaults =
    [
        new("M", "Morning", new TimeOnly(7, 0), new TimeOnly(15, 0), 30, false, "fixed", true),
        new("E", "Evening", new TimeOnly(15, 0), new TimeOnly(23, 0), 30, false, "fixed", true),
        new("N", "Night", new TimeOnly(23, 0), new TimeOnly(7, 0), 30, true, "fixed", true),
        new("G", "General", new TimeOnly(9, 0), new TimeOnly(18, 0), 30, false, "fixed", true),
    ];

    public IReadOnlyList<ShiftPatternDef> Patterns { get; }

    public ShiftPatternDef? Find(ShiftCode c) => _byCode.GetValueOrDefault(c.Value);

    public bool IsActive(ShiftCode c) => c == ShiftCode.OFF || (Find(c)?.Active ?? false);
}

/// <summary>Shift times come from the Shift patterns list; late grace 15 minutes.</summary>
public static class Shifts
{
    public const int GraceMinutes = 15;

    public static TimeOnly? Start(ShiftCode c, ShiftCatalog? catalog = null) => (catalog ?? new ShiftCatalog()).Find(c)?.Start;

    /// <summary>Self check-in status from the local time of day: Late after shift start + grace (night and off shifts: Present).</summary>
    public static AttendanceStatus CheckinStatus(TimeOnly local, ShiftCode shift, ShiftCatalog? catalog = null)
    {
        var p = (catalog ?? new ShiftCatalog()).Find(shift);
        if (shift == ShiftCode.OFF || p is null || p.Night) return AttendanceStatus.P;
        var start = p.Start;
        return local.Hour * 60 + local.Minute > start.Hour * 60 + start.Minute + GraceMinutes ? AttendanceStatus.L : AttendanceStatus.P;
    }
}

/// <summary>Roster, attendance counts, payroll line and statutory rules (prototype staff.ts).</summary>
public static class PayrollRules
{
    public static bool PaidLeave(LeaveType t) => t != LeaveType.LOP;

    /// <summary>Effective shift: roster override → weekly off → default shift.</summary>
    public static ShiftCode ShiftFor(StaffProfile st, StaffDays days, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(days);
        if (days.Roster.TryGetValue(date, out var o)) return o;
        return (int)date.DayOfWeek == st.WeeklyOff && st.WeeklyOffAuto ? ShiftCode.OFF : st.DefaultShift;
    }

    /// <summary>A day without duty for leave: a rostered weekly off or a DEC-083 paid holiday (leave days skip both; nobody is "off" on them).</summary>
    public static bool DayOff(StaffProfile st, StaffDays days, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(days);
        return ShiftFor(st, days, date) == ShiftCode.OFF || days.Holidays.ContainsKey(date);
    }

    public static IEnumerable<LeaveSpan> ApprovedOn(StaffDays days, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(days);
        return days.Leaves.Where(l => l.Status == LeaveStatus.Approved && l.Covers(date));
    }

    /// <summary>A full-day approved leave on the date, else the first half-day one.</summary>
    public static LeaveSpan? ApprovedLeaveOn(StaffDays days, DateOnly date)
    {
        var ls = ApprovedOn(days, date).ToList();
        return ls.FirstOrDefault(l => l.Half is null) ?? ls.FirstOrDefault();
    }

    /// <summary>Leave days in [from,to], excluding rostered weekly offs (SCC isWeekOffDate) and paid holidays (DEC-083 / MA-6 Holiday calendar); a half-day leave is 0.5.</summary>
    public static decimal LeaveDays(StaffProfile st, StaffDays days, DateOnly from, DateOnly to, LeaveHalf? half)
    {
        if (half is not null) return DayOff(st, days, from) ? 0m : 0.5m;
        var n = 0m;
        for (var d = from; d <= to; d = d.AddDays(1)) if (!DayOff(st, days, d)) n++;
        return n;
    }

    /// <summary>Counts for a staff member over a month (prototype hrCounts, review F-01: a rostered weekly off inside a leave span is a weekly off).</summary>
    public static AttendanceCounts Counts(StaffProfile st, StaffDays days, PayMonth ym, DateOnly? upTo = null)
    {
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(days);
        decimal p = 0, l = 0, h = 0, hl = 0, a = 0, offw = 0, off = 0, paid = 0, lop = 0, unmarked = 0, na = 0, hol = 0;
        var ot = 0;
        var mgmt = ym.Dates().Count(d => days.Holidays.ContainsKey(d));   // DEC-115: SCC "Mgmt holidays"
        int? elapsed = null;
        decimal nt = 0;
        var seen = 0;
        void AddLeave(LeaveSpan x, decimal n)
        {
            if (x.IsPaid) paid += n;
            else lop += n;
        }

        foreach (var d in ym.Dates())
        {
            if (upTo is { } u && d > u)
            {
                elapsed = seen;
                break;
            }

            seen++;
            if (!st.InService(d))
            {
                na++;
                continue;
            }

            var ls = ApprovedOn(days, d).ToList();
            var full = ls.FirstOrDefault(x => x.Half is null);
            var halves = ls.Where(x => x.Half is not null).Take(2).ToList();
            if (days.Attendance.TryGetValue(d, out var att))
            {
                ot += att.OtMinutes;
                if (full is null && halves.Count > 0 && att.Status is AttendanceStatus.H or AttendanceStatus.A)
                {
                    AddLeave(halves[0], 0.5m);
                    if (att.Status == AttendanceStatus.H) hl++;
                    else a += 0.5m;
                }
                else
                {
                    switch (att.Status)
                    {
                        case AttendanceStatus.OFF: offw++; break;
                        case AttendanceStatus.NT: nt++; break;
                        case AttendanceStatus.P: p++; break;
                        case AttendanceStatus.L: l++; break;
                        case AttendanceStatus.H: h++; break;
                        default: a++; break;
                    }
                }
            }
            else if (ShiftFor(st, days, d) == ShiftCode.OFF) off++;   // F-01
            else if (days.Holidays.ContainsKey(d)) hol++;              // DEC-083: an unmarked paid holiday counts like a weekly off (a worked one is marked)
            else if (full is not null) AddLeave(full, 1m);
            else if (halves.Count > 0)
            {
                foreach (var x in halves) AddLeave(x, 0.5m);
                unmarked += 1m - 0.5m * halves.Count;
            }
            else unmarked++;
        }

        return new AttendanceCounts(ym.ToString(), ym.Days, p, l, h, hl, a, offw, off, paid, lop, unmarked, na, ot, hol, mgmt, elapsed, nt);
    }

    /// <summary>Maharashtra professional tax for a month (SCC ptFor): women exempt up to the female limit; men nil / mid slab / standard; February top slab.</summary>
    public static long Pt(long basePaise, Gender gender, PayMonth ym, PayrollRates r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var top = ym.Month == 2 ? r.PtFeb : r.PtStd;
        if (gender == Gender.F) return basePaise <= r.PtFemaleExempt ? 0 : top;
        if (basePaise <= r.PtMaleExempt) return 0;
        if (basePaise <= r.PtMaleMid) return r.PtMaleMidAmt;
        return top;
    }

    private static long HalfUnits(decimal twice) => (long)Math.Round(twice, MidpointRounding.AwayFromZero);

    /// <summary>SCC roundHalf: to the nearest half day (prototype hrRoundHalf; negatives are clamped to 0 by every caller).</summary>
    public static decimal RoundHalf(decimal n) => Math.Round(n * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>SCC extra days: 4 when the deduction is at most 5.5 days, 2 below 7, none from 7.</summary>
    public static decimal SccExtra(decimal deduction) => deduction <= 5.5m ? 4 : deduction < 7 ? 2 : 0;

    /// <summary>SCC total days: calendar days (or the rate version's override) less the paid holidays (SCC "Mgmt holidays"), never below 0.</summary>
    public static decimal SccTotalDays(AttendanceCounts c, PayrollRates r)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(r);
        return Math.Max(0, RoundHalf((r.DaysOverride > 0 ? r.DaysOverride : c.Days) - c.MgmtHol));
    }

    /// <summary>
    /// DEC-115 — SCC calcAttendance on HCC's daily marks, read the way SCC's DSR/Leave feed fills the sheet (prototype hrSccDays): present =
    /// P + L + days worked on the weekly off (OFF mark) + no-thumb days (NT) + the worked half of a half-day leave; half days = H; leave = approved paid leave; absent = total − present − leave − half (weekly
    /// offs, A, unmarked days, leave without pay and days out of service). A month read up to a date counts its elapsed share of the total.
    /// </summary>
    public static SccDays SccDays(AttendanceCounts c, PayrollRates r)
    {
        var total = SccTotalDays(c, r);
        var bas = c.Elapsed is { } e && e < c.Days ? Math.Min(total, RoundHalf(total * e / c.Days)) : total;
        var noThumb = c.NT;
        var present = c.P + c.L + c.OFFW + noThumb + c.HL * 0.5m;
        var half = c.H;
        var leave = RoundHalf(c.PaidLeave);
        var absent = Math.Max(0, RoundHalf(bas - present - leave - half));
        var lateDed = r.LateBlock > 0 ? RoundHalf(Math.Floor(c.L / r.LateBlock) * 0.5m) : 0;
        var halfDed = RoundHalf(half * 0.5m);
        var noThumbDed = RoundHalf(noThumb * 0.5m);
        var deduction = RoundHalf(lateDed + absent + halfDed + noThumbDed);
        var extra = SccExtra(deduction);
        var finalPresent = Math.Max(0, RoundHalf(total - deduction));
        var raw = RoundHalf(finalPresent + leave + extra);
        return new SccDays(total, present, half, leave, absent, c.L, lateDed, absent, halfDed, deduction, extra, finalPresent,
            Math.Min(total, Math.Max(0, raw)), Math.Max(0, RoundHalf(raw - total)), noThumb, noThumbDed);
    }

    /// <summary>DEC-115 — SCC calcAttendance issues in SCC's words: Risk blocks the lock (SCC runBlockingErrors), otherwise shown only.</summary>
    public static IEnumerable<(bool Risk, string Text)> SccIssues(string name, SccDays d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.Leave > 5.5m) yield return (true, $"{name}: Leaves applied ({Num(d.Leave)}) exceed 5.5-day limit.");
        if (d.Deduction >= d.Total) yield return (true, $"{name}: Total deduction ({Num(d.Deduction)}) ≥ total days ({Num(d.Total)}) — zero salary days.");
        else if (d.Deduction > 5.5m) yield return (false, $"{name}: High total deduction — {Num(d.Deduction)} days.");
        if (d.OtDays > 0) yield return (false, $"{name}: {Num(d.OtDays)} OT day(s) — salary capped at {Num(d.Total)}, OT tracked separately.");
        if (d.Late >= 3) yield return (false, $"{name}: {Num(d.Late)} late mark(s).");
        if (d.NoThumb > 0) yield return (false, $"{name}: {Num(d.NoThumb)} no-thumb entry/entries.");
    }

    /// <summary>
    /// One payroll line (SCC calcGM, prototype hrComputeLine): staff + counts + rate version + open advance → figures, rounded to paise
    /// half-up in this order. <paramref name="esicCovered"/> = contribution-period coverage from <see cref="EsicCovered"/> (only when the
    /// rate version uses that rule).
    /// </summary>
    public static PayLine ComputeLine(StaffProfile st, AttendanceCounts c, PayrollRates r, (long Installment, long Balance)? advance = null, bool? esicCovered = null)
    {
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(r);
        var structure = st.EffectiveStructure;
        var gender = st.EffectiveGender;
        var days = SccDays(c, r);
        var t2 = HalfUnits(days.Total * 2);
        // SCC: grossPayable = gross × salary days ÷ total days; OT = gross ÷ total days × OT days. A daily-wage worker's gross = daily rate × total days.
        long DayPay(decimal n, int bps) => t2 <= 0 ? 0 : st.PayType == PayType.Monthly
            ? PayMath.MulDiv(st.MonthlyPaise, HalfUnits(n * 2), bps, t2 * 10000)
            : PayMath.MulDiv(st.DailyPaise, HalfUnits(n * 2), bps, 2L * 10000);
        var earned = DayPay(days.SalaryDays, 10000);
        var ot = DayPay(days.OtDays, r.OtBps);
        var late = (int)HalfUnits(days.LateDed * 2);

        long basic = earned, hra = 0, washing = 0;
        if (structure == PayStructure.S)
        {
            basic = PayMath.MulDiv(earned, r.BasicBps, 1, 10000);
            hra = PayMath.MulDiv(basic, r.HraBps, 1, 10000);
            washing = PayMath.MulDiv(basic, r.WashBps, 1, 10000);
        }

        var special = earned - basic - hra - washing;
        var gross = earned + ot;
        var pfWages = st.PfOn ? Math.Min(basic, r.PfWageCap) : 0;
        var pf = pfWages > 0 ? PayMath.Pct(pfWages, r.PfEeBps, r.Rounding) : 0;
        var pfEr = pfWages > 0 ? PayMath.Pct(pfWages, r.PfErBps, r.Rounding) : 0;
        var covered = r.EsicPeriod ? esicCovered ?? st.EsicContractWage <= r.EsicCeiling : earned <= r.EsicCeiling;
        var esicWages = st.EsicOn && earned > 0 && covered ? earned - washing : 0;
        var esic = esicWages > 0 ? PayMath.Pct(esicWages, r.EsicEeBps, r.Rounding, esic: true) : 0;
        var esicEr = esicWages > 0 ? PayMath.Pct(esicWages, r.EsicErBps, r.Rounding, esic: true) : 0;
        var pt = earned > 0 ? Pt(earned, gender, PayMonth.Parse(c.Ym), r) : 0;
        var due = advance is { } adv ? (adv.Installment > 0 ? Math.Min(adv.Installment, adv.Balance) : adv.Balance) : 0;
        var advanceRecovery = Math.Max(0, Math.Min(due, gross - pf - esic - pt));
        var deductions = pf + esic + pt + advanceRecovery;
        return new PayLine(st.StaffId, st.Name, st.Dept, st.Designation, st.PayType, structure, gender, st.BasePaise, c, late, days.SalaryDays,
            earned, basic, hra, washing, special, ot, gross, pfWages, pf, pfEr, esicWages, esic, esicEr, pt, advanceRecovery, deductions,
            gross - deductions, gross + pfEr + esicEr, r.EsicPeriod ? st.EsicOn && covered : null, days);
    }

    /// <summary>
    /// ESIC coverage under the contribution-period rule (review F-07): the earliest LOCKED run of the same period decides
    /// (<paramref name="priorLockedCoverage"/>, months before <paramref name="ym"/> in the period, ascending); with none, the contractual
    /// wage (≤ ceiling = covered). Null when the version uses the SCC month-by-month rule.
    /// </summary>
    public static bool? EsicCovered(StaffProfile st, PayMonth ym, PayrollRates r, IEnumerable<(PayMonth Month, bool? Covered)> priorLockedCoverage)
    {
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(priorLockedCoverage);
        if (!r.EsicPeriod) return null;
        var ps = ym.EsicPeriodStart;
        var prior = priorLockedCoverage.Where(x => x.Month >= ps && x.Month < ym && x.Covered is not null).OrderBy(x => x.Month).FirstOrDefault();
        return prior.Covered ?? st.EsicContractWage <= r.EsicCeiling;
    }

    /// <summary>DEC-075: the unmarked-days warning points to the correction action (prototype HR_UNMARKED_HINT).</summary>
    public const string UnmarkedHint = "a manager can correct them on Staff & payroll › Attendance › Past days before the lock.";

    /// <summary>Run warnings for one line (prototype hrComputeRun).</summary>
    public static IEnumerable<string> Warnings(StaffProfile st, AttendanceCounts c)
    {
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(c);
        if (c.Unmarked != 0) yield return $"{st.Name}: {Num(c.Unmarked)} unmarked day{(c.Unmarked == 1 ? "" : "s")} counted as absent — {UnmarkedHint}";
        if (st.BasePaise <= 0) yield return $"{st.Name}: salary not set — pays ₹0.";
    }

    /// <summary>Errors that block locking (SCC runBlockingErrors): salary not set, and the SCC risk issues of the day sheet (DEC-115).</summary>
    public static IEnumerable<string> LockErrors(IEnumerable<PayLine> lines) => lines.SelectMany(l =>
        (l.Base <= 0 ? [$"{l.Name}: salary not set"] : Array.Empty<string>())
            .Concat(l.Days is { } d ? SccIssues(l.Name, d).Where(i => i.Risk).Select(i => i.Text) : []));

    /// <summary>A decimal count as the prototype prints it (0.5, 3).</summary>
    public static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Advance recovery due in a month: the open advance whose recovery has started.</summary>
    public static bool AdvanceDueIn(DateOnly advanceDate, PayMonth? fromMonth, PayMonth ym) => (fromMonth ?? PayMonth.Of(advanceDate)) <= ym;
}
