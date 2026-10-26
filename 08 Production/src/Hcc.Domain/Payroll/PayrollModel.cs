using System.Globalization;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Payroll;

// Module 17 · payroll and leave (W2-2). Rules ported from the prototype (03 Prototype/src/domain/streams/staff.ts, after the
// W05-B review fixes F-01…F-17), which ported them from SCC payroll\index.html and leave\index.html. Money = long paise,
// rounding half-up on non-negative amounts (= half away from zero). Statutory values are DATED settings marked
// "Illustrative — CA to confirm" (CA question pack 25 Oct 2026, P1–P17). Fictional staff only.
//
// NOT PORTED FROM SCC (review F-08, owner decision pending, CA item P10): calcAttendance's "extra days" rule, its 5.5-day monthly
// cap on leaves applied, and day-based overtime. HCC pays rostered weekly offs from the roster and overtime per minute.

public enum ShiftCode { M, E, N, G, OFF }

public enum AttendanceStatus { P, L, H, A, OFF }

public enum LeaveType { CL, SL, EL, CO, ML, LOP }

public enum LeaveHalf { AM, PM }

public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }

/// <summary>Professional tax slab (Maharashtra). Default M (SCC; review F-04: payroll sets it where it applies).</summary>
public enum Gender { M, F }

/// <summary>S = structured (basic / HRA / washing / special, SCC 'S'); B = basic only (SCC 'B').</summary>
public enum PayStructure { S, B }

public enum PayType { Monthly, Daily }

/// <summary>paise = SCC (round to 0.01); rupee = PF to the nearest rupee, ESIC up to the next rupee (CA item P6).</summary>
public enum StatutoryRounding { Paise, Rupee }

/// <summary>A pay month (YYYY-MM).</summary>
public readonly record struct PayMonth(int Year, int Month) : IComparable<PayMonth>
{
    public static PayMonth Parse(string? value)
    {
        if (value is { Length: 7 } && value[4] == '-'
            && int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            && int.TryParse(value.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) && y is >= 2000 and <= 2999 && m is >= 1 and <= 12)
            return new PayMonth(y, m);
        throw HccException.Validation("Choose a month (YYYY-MM).");
    }

    public static PayMonth Of(DateOnly date) => new(date.Year, date.Month);

    public DateOnly First => new(Year, Month, 1);

    public int Days => DateTime.DaysInMonth(Year, Month);

    public DateOnly Last => new(Year, Month, Days);

    public PayMonth Next => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);

    public PayMonth Previous => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);

    public IEnumerable<DateOnly> Dates()
    {
        for (var d = 1; d <= Days; d++) yield return new DateOnly(Year, Month, d);
    }

    /// <summary>First month (YYYY-MM) of the ESIC contribution period (Apr–Sep, Oct–Mar) containing this month (F-07).</summary>
    public PayMonth EsicPeriodStart => Month >= 10 ? new(Year, 10) : Month >= 4 ? new(Year, 4) : new(Year - 1, 10);

    public string Label => First.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public int CompareTo(PayMonth other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public static bool operator <(PayMonth a, PayMonth b) => a.CompareTo(b) < 0;

    public static bool operator >(PayMonth a, PayMonth b) => a.CompareTo(b) > 0;

    public static bool operator <=(PayMonth a, PayMonth b) => a.CompareTo(b) <= 0;

    public static bool operator >=(PayMonth a, PayMonth b) => a.CompareTo(b) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}

/// <summary>Indian financial year (April → March) containing a date (SCC fyRangeOf).</summary>
public sealed record FinancialYear(DateOnly From, DateOnly To)
{
    public static FinancialYear Containing(DateOnly date)
    {
        var y = date.Month >= 4 ? date.Year : date.Year - 1;
        return new FinancialYear(new DateOnly(y, 4, 1), new DateOnly(y + 1, 3, 31));
    }

    public string Label => string.Create(CultureInfo.InvariantCulture, $"FY {From.Year}–{(From.Year + 1) % 100:D2}");
}

/// <summary>The payroll-relevant part of a staff record.</summary>
public sealed record StaffProfile(long StaffId, string Name, string Dept, string Designation, PayType PayType, long MonthlyPaise, long DailyPaise,
    ShiftCode DefaultShift, int WeeklyOff, DateOnly Joined, DateOnly? ExitedOn, bool Active, Gender? Gender = null, PayStructure? Structure = null,
    bool? PfApplicable = null, bool? EsicApplicable = null)
{
    public PayStructure EffectiveStructure => Structure ?? (PayType == PayType.Daily ? PayStructure.B : PayStructure.S);

    /// <summary>Default (SCC migration rule): PF only for structured pay (CA item P3).</summary>
    public bool PfOn => PfApplicable ?? EffectiveStructure == PayStructure.S;

    /// <summary>Default on; ESIC still applies only when covered (ceiling / contribution period).</summary>
    public bool EsicOn => EsicApplicable ?? true;

    public Gender EffectiveGender => Gender ?? Payroll.Gender.M;

    public long BasePaise => PayType == PayType.Monthly ? MonthlyPaise : DailyPaise;

    /// <summary>Contractual monthly wage for the ESIC coverage test: monthly salary, or daily wage × 26 (illustrative — CA item P5).</summary>
    public long EsicContractWage => PayType == PayType.Monthly ? MonthlyPaise : checked(DailyPaise * 26);

    public bool InService(DateOnly d) => d >= Joined && (ExitedOn is not { } x || d <= x);

    /// <summary>Whether the staff member belongs in a month's payroll (prototype hrInMonth).</summary>
    public bool InMonth(PayMonth m) => Joined <= m.Last && (Active || (ExitedOn is { } x && x >= m.First));
}

public sealed record AttendanceMark(DateOnly Date, AttendanceStatus Status, int OtMinutes);

/// <summary>A leave request as the rules see it (days in half-day units: 0.5 = a half-day).</summary>
public sealed record LeaveSpan(long LeaveId, long StaffId, LeaveType Type, DateOnly From, DateOnly To, LeaveHalf? Half, decimal Days, LeaveStatus Status)
{
    public bool Covers(DateOnly d) => From <= d && To >= d;

    public bool Holds => Status is LeaveStatus.Pending or LeaveStatus.Approved;
}

/// <summary>Roster overrides, attendance marks and leaves of one staff member (any date range the caller loaded).</summary>
public sealed class StaffDays
{
    public StaffDays(IReadOnlyDictionary<DateOnly, ShiftCode>? roster = null, IReadOnlyDictionary<DateOnly, AttendanceMark>? attendance = null,
        IReadOnlyList<LeaveSpan>? leaves = null)
    {
        Roster = roster ?? new Dictionary<DateOnly, ShiftCode>();
        Attendance = attendance ?? new Dictionary<DateOnly, AttendanceMark>();
        Leaves = leaves ?? [];
    }

    public IReadOnlyDictionary<DateOnly, ShiftCode> Roster { get; }

    public IReadOnlyDictionary<DateOnly, AttendanceMark> Attendance { get; }

    /// <summary>All leaves of the staff member (any status), in request order (lowest id first).</summary>
    public IReadOnlyList<LeaveSpan> Leaves { get; }
}

/// <summary>Month counts (prototype HRCounts). Leave and absence can be half days.</summary>
public sealed record AttendanceCounts(string Ym, int Days, decimal P, decimal L, decimal H, decimal HL, decimal A, decimal OFFW, decimal Off,
    decimal PaidLeave, decimal LopLeave, decimal Unmarked, decimal Na, int OtMin);

/// <summary>Dated statutory rate version (one per effective-from date, the 1st of a month). bps = basis points; money in paise.</summary>
public sealed record PayrollRates(DateOnly From,
    int BasicBps, int HraBps, int WashBps,
    int PfEeBps, int PfErBps, long PfWageCap,
    int EsicEeBps, int EsicErBps, long EsicCeiling,
    long PtMaleExempt, long PtMaleMid, long PtMaleMidAmt, long PtStd, long PtFeb, long PtFemaleExempt,
    int OtBps, int LateBlock, StatutoryRounding Rounding, bool EsicPeriod)
{
    /// <summary>SCC defaults from 1 Apr 2025 (illustrative — CA to confirm), with the F-07 contribution-period rule on.</summary>
    public static PayrollRates Defaults() => new(new DateOnly(2025, 4, 1), 5000, 8000, 1000, 1200, 1300, 1_500_000, 75, 325, 2_100_000,
        750_000, 1_000_000, 17_500, 20_000, 30_000, 2_500_000, 10_000, 3, StatutoryRounding.Paise, true);

    public const string Disclaimer = "Illustrative — CA to confirm for a hotel. PF, ESIC and Maharashtra PT rules ported from SCC as dated rates; not statutory advice. Fictional staff.";

    /// <summary>A04-11: what payslips and full-and-final documents do not include (shown on both, next to <see cref="Disclaimer"/>).</summary>
    public const string NotCalculated = "Not calculated: gratuity, notice pay, bonus, TDS on salary.";

    /// <summary>Range and consistency checks (prototype ratesFrom + rateRules).</summary>
    public void Validate(string where = "")
    {
        static void Range(long v, long min, long max, string label, string where)
        {
            if (v < min || v > max) throw HccException.Validation($"Invalid value for {label}.{where}");
        }

        if (From.Day != 1) throw HccException.Validation("Rates take effect from the 1st of a month.");
        Range(BasicBps, 1000, 10000, "basic share", where);
        Range(HraBps, 0, 10000, "HRA", where);
        Range(WashBps, 0, 10000, "washing allowance", where);
        Range(PfEeBps, 0, 2500, "PF employee rate", where);
        Range(PfErBps, 0, 2500, "PF employer rate", where);
        Range(EsicEeBps, 0, 1000, "ESIC employee rate", where);
        Range(EsicErBps, 0, 1000, "ESIC employer rate", where);
        Range(OtBps, 0, 30000, "overtime multiplier", where);
        Range(LateBlock, 0, 10, "late rule", where);
        foreach (var (v, n) in new[] { (PfWageCap, "PF wage cap"), (EsicCeiling, "ESIC ceiling"), (PtMaleExempt, "PT nil slab"), (PtMaleMid, "PT middle slab"),
                     (PtMaleMidAmt, "PT middle amount"), (PtStd, "PT standard amount"), (PtFeb, "PT February amount"), (PtFemaleExempt, "PT women's exemption") })
            Range(v, 0, 100_000_000, n, where);
        if (OtBps < 10000) throw HccException.Validation("Overtime multiplier must be at least 1×." + where);
        if ((long)BasicBps * (10000 + HraBps + WashBps) > 10000L * 10000) throw HccException.Validation("Basic + HRA + washing cannot exceed the earned pay." + where);
        if (PtMaleExempt >= PtMaleMid) throw HccException.Validation("The PT nil slab must end below the middle slab." + where);
    }
}

/// <summary>One payroll line (prototype HRLine). Figures in paise; counts as the rules saw them.</summary>
public sealed record PayLine(long StaffId, string Name, string Dept, string Designation, PayType PayType, PayStructure Structure, Gender Gender, long Base,
    AttendanceCounts Counts, int LatePenaltyHalf, decimal PayableDays,
    long Earned, long Basic, long Hra, long Washing, long Special, long Ot, long Gross,
    long PfWages, long Pf, long PfEr, long EsicWages, long Esic, long EsicEr, long Pt, long Advance, long Deductions, long Net,
    long Ctc, bool? EsicCovered);

/// <summary>Totals of a run's lines.</summary>
public sealed record PayTotals(int Staff, long Earned, long Basic, long Hra, long Washing, long Special, long Ot, long Gross, long PfWages, long Pf, long PfEr,
    long EsicWages, long Esic, long EsicEr, long Pt, long Advance, long Deductions, long Net, long Ctc)
{
    public static PayTotals Of(IEnumerable<PayLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var l = lines.ToList();
        return new PayTotals(l.Count, l.Sum(x => x.Earned), l.Sum(x => x.Basic), l.Sum(x => x.Hra), l.Sum(x => x.Washing), l.Sum(x => x.Special), l.Sum(x => x.Ot),
            l.Sum(x => x.Gross), l.Sum(x => x.PfWages), l.Sum(x => x.Pf), l.Sum(x => x.PfEr), l.Sum(x => x.EsicWages), l.Sum(x => x.Esic), l.Sum(x => x.EsicEr),
            l.Sum(x => x.Pt), l.Sum(x => x.Advance), l.Sum(x => x.Deductions), l.Sum(x => x.Net), l.Sum(x => x.Ctc));
    }
}

/// <summary>Leave policy (SCC ENT_DEFAULTS + MAX_DAY per department). Not dated: applies to new requests.</summary>
public sealed record LeavePolicy(int Cl, int Sl, int El, int DefaultCap, IReadOnlyDictionary<string, int> DeptCap)
{
    public static LeavePolicy Defaults() => new(12, 7, 15, 2, new Dictionary<string, int>());

    public int Entitled(LeaveType t) => t switch { LeaveType.CL => Cl, LeaveType.SL => Sl, LeaveType.EL => El, _ => 0 };

    public int CapFor(string dept) => DeptCap.TryGetValue(dept, out var c) ? c : DefaultCap;
}
