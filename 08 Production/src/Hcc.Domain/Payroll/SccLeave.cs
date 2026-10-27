using System.Globalization;

namespace Hcc.Domain.Payroll;

/// <summary>SCC getCapacity for one date (pending + approved records; rejected and cancelled hold no slot).</summary>
public sealed record LeaveCapacity(int AmUsed, int PmUsed, int DayUsed, bool DayFull, bool AmFull, bool PmFull, bool BothFull, bool CanAddFullDay, bool CanAddAm, bool CanAddPm)
{
    /// <summary>SCC canAddLeave(key, type).</summary>
    public bool CanAdd(LeaveHalf? half) => half switch
    {
        LeaveHalf.AM => CanAddAm,
        LeaveHalf.PM => CanAddPm,
        _ => CanAddFullDay,
    };
}

/// <summary>What SCC submitLeave did with one date of a request.</summary>
public enum LeaveDateResult { Added, Blocked, Duplicate, WeekOff }

/// <summary>SCC submitLeave's outcome per date and its four counts.</summary>
public sealed record LeaveRequestPlan(IReadOnlyList<(DateOnly Date, LeaveDateResult Result)> Dates)
{
    public int Added => Dates.Count(d => d.Result == LeaveDateResult.Added);

    public int Blocked => Dates.Count(d => d.Result == LeaveDateResult.Blocked);

    public int Duplicates => Dates.Count(d => d.Result == LeaveDateResult.Duplicate);

    public int WeekOff => Dates.Count(d => d.Result == LeaveDateResult.WeekOff);

    /// <summary>SCC's toast (L1401-1406); nothing added = SCC's error toast.</summary>
    public string Toast
    {
        get
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add($"{Added} day(s) submitted — pending approval");
            if (Blocked > 0) parts.Add($"{Blocked} skipped — day/shift full (max {SccLeave.MaxDay}/day)");
            if (Duplicates > 0) parts.Add($"{Duplicates} skipped — duplicate");
            if (WeekOff > 0 && Added == 0 && Blocked == 0 && Duplicates == 0) parts.Add($"{WeekOff} day(s) skipped — weekend/weekly-off");
            return parts.Count > 0 ? string.Join(", ", parts) : "Nothing added";
        }
    }
}

/// <summary>
/// SCC LeaveDesk rules (leave\index.html, ported in full — DEC-116; prototype 03 Prototype/src/domain/streams/staff.ts + staff-leave.ts, the same
/// texts). One leave record per date; MAX_DAY 3 records a date for the whole property, MAX_SHIFT 4 per shift; the hotel's week-off set; SCC's
/// duplicate rule (AM + PM coexist); fyUsage = approved days by date; requestedLeaveDays with SCC's 400-day guard; blackouts.
/// </summary>
public static class SccLeave
{
    /// <summary>SCC MAX_DAY: the binding cap — at most 3 leave records (pending + approved) a date for the whole property.</summary>
    public const int MaxDay = 3;

    /// <summary>SCC MAX_SHIFT: at most 4 a shift. MAX_DAY binds first, so it never decides (ported as SCC has it; owner question).</summary>
    public const int MaxShift = 4;

    /// <summary>SCC's own week-off default (Saturday + Sunday) as a mask; HCC's default is 0 (none, a 7-day hotel).</summary>
    public const int SccWeekOffDefault = 0b100_0001;

    /// <summary>The look-ahead guard of SCC requestedLeaveDays and the blackout walk.</summary>
    public const int Guard = 400;

    /// <summary>Weekday bit (0 = Sunday … 6 = Saturday).</summary>
    public static bool IsWeekOff(int mask, DateOnly d) => ((mask >> (int)d.DayOfWeek) & 1) == 1;

    public static int Mask(IEnumerable<int> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        return days.Where(d => d is >= 0 and <= 6).Aggregate(0, (m, d) => m | (1 << d));
    }

    public static IReadOnlyList<int> Days(int mask) => Enumerable.Range(0, 7).Where(d => ((mask >> d) & 1) == 1).ToList();

    /// <summary>SCC getCapacity over the records of one date.</summary>
    public static LeaveCapacity Capacity(IEnumerable<LeaveSpan> sameDate)
    {
        ArgumentNullException.ThrowIfNull(sameDate);
        var act = sameDate.Where(l => l.Active).ToList();
        var am = act.Count(l => l.Half is null or LeaveHalf.AM);
        var pm = act.Count(l => l.Half is null or LeaveHalf.PM);
        var day = act.Count;
        var full = day >= MaxDay;
        return new LeaveCapacity(am, pm, day, full, am >= MaxShift || full, pm >= MaxShift || full, (am >= MaxShift && pm >= MaxShift) || full,
            am < MaxShift && pm < MaxShift && !full, am < MaxShift && !full, pm < MaxShift && !full);
    }

    /// <summary>SCC duplicate rule: the same staff member with a record not rejected (nor cancelled), and the same half or either a full day.</summary>
    public static bool IsDuplicate(IEnumerable<LeaveSpan> sameDate, long staffId, LeaveHalf? half)
    {
        ArgumentNullException.ThrowIfNull(sameDate);
        return sameDate.Any(l => l.StaffId == staffId && l.Active && (l.Half == half || l.Half is null || half is null));
    }

    /// <summary>SCC submitLeave's loop: week-off → duplicate → capacity → add. <paramref name="leaves"/> = the property's records in [from, to].</summary>
    public static LeaveRequestPlan Plan(int weekOffMask, IReadOnlyList<LeaveSpan> leaves, long staffId, DateOnly from, DateOnly to, LeaveHalf? half)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        var dates = new List<(DateOnly, LeaveDateResult)>();
        var added = new List<LeaveSpan>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (IsWeekOff(weekOffMask, d))
            {
                dates.Add((d, LeaveDateResult.WeekOff));
                continue;
            }

            var day = leaves.Where(l => l.Date == d).Concat(added.Where(l => l.Date == d)).ToList();
            if (IsDuplicate(day, staffId, half)) dates.Add((d, LeaveDateResult.Duplicate));
            else if (!Capacity(day).CanAdd(half)) dates.Add((d, LeaveDateResult.Blocked));
            else
            {
                dates.Add((d, LeaveDateResult.Added));
                added.Add(new LeaveSpan(0, staffId, LeaveType.CL, d, d, half, half is null ? 1m : 0.5m, LeaveStatus.Pending));
            }
        }

        return new LeaveRequestPlan(dates);
    }

    /// <summary>SCC requestedLeaveDays: non-week-off dates from…to (at most 400 looked at) × 1 or 0.5.</summary>
    public static decimal RequestedDays(int weekOffMask, DateOnly from, DateOnly to, LeaveHalf? half)
    {
        if (to < from) return 0;
        var n = 0;
        var g = 0;
        for (var d = from; d <= to && g++ < Guard; d = d.AddDays(1)) if (!IsWeekOff(weekOffMask, d)) n++;
        return n * (half is null ? 1m : 0.5m);
    }

    /// <summary>SCC fyUsage: APPROVED days per category of one staff member, each record by its own date inside the financial year (pending not used).</summary>
    public static IReadOnlyDictionary<LeaveType, decimal> FyUsage(IEnumerable<LeaveSpan> leaves, long staffId, FinancialYear fy, long exceptLeaveId = 0)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(fy);
        return leaves.Where(l => l.StaffId == staffId && l.Status == LeaveStatus.Approved && l.LeaveId != exceptLeaveId && l.Date >= fy.From && l.Date <= fy.To)
            .GroupBy(l => l.Type).ToDictionary(g => g.Key, g => g.Sum(l => l.Days));
    }

    /// <summary>SCC whole-number-or-one-decimal format (n % 1 === 0 ? n : n.toFixed(1)).</summary>
    public static string Fmt(decimal n) => n % 1 == 0 ? n.ToString("0", CultureInfo.InvariantCulture) : n.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>A quota as JavaScript prints a number (12, 7.5, 10.25).</summary>
    public static string Num(decimal n) => n.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>SCC category name (VALID_CATEGORIES) — "Casual Leave" …; HCC's LOP is "Leave without pay".</summary>
    public static string Category(LeaveType t) => t.Value switch
    {
        "CL" => "Casual Leave",
        "SL" => "Sick Leave",
        "EL" => "Earned Leave",
        "ML" => "Maternity / Paternity",
        "CO" => "Compensatory Off",
        "OTH" => "Other",
        "LOP" => "Leave without pay",
        _ => t.Value,
    };

    /// <summary>SCC's balance confirm (L1350) when a tracked category's approved use + this request exceeds the quota; null = no warning.</summary>
    public static string? OverBalanceText(LeavePolicy policy, IEnumerable<LeaveSpan> staffLeaves, long staffId, string staffName, LeaveType type, DateOnly from, DateOnly to,
        LeaveHalf? half)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.IsTracked(type)) return null;
        var fy = FinancialYear.Containing(from);
        var used = FyUsage(staffLeaves, staffId, fy).GetValueOrDefault(type);
        var quota = policy.EntFor(staffId).Of(type);
        var req = RequestedDays(policy.WeekOffMask, from, to, half);
        if (used + req <= quota) return null;
        return $"{staffName} has used {Fmt(used)} of {Num(quota)} {Category(type)} day(s) this {fy.Label}.\nThis request adds {Fmt(req)} more and EXCEEDS the balance.\n\n"
            + "Record it anyway (it will still need approval)?";
    }

    /// <summary>DEC-122 (owner, 27 Oct 2026 — SCC re-checks nothing at approval): approving one date record of a tracked category re-checks the
    /// balance of that date's financial year — approved days by date (this record left out) + this date (SCC leaveDaysOf: full day 1, half 0.5)
    /// over the staff member's quota (override, else the default) → SCC's over-quota text asking "Approve it anyway?"; null = no warning. LOP and
    /// the untracked categories have no quota.</summary>
    public static string? ApproveOverBalanceText(LeavePolicy policy, IEnumerable<LeaveSpan> staffLeaves, string staffName, LeaveSpan leave)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(leave);
        if (!policy.IsTracked(leave.Type)) return null;
        var fy = FinancialYear.Containing(leave.Date);
        var used = FyUsage(staffLeaves, leave.StaffId, fy, leave.LeaveId).GetValueOrDefault(leave.Type);
        var quota = policy.EntFor(leave.StaffId).Of(leave.Type);
        var add = leave.Half is null ? 1m : 0.5m;
        if (used + add <= quota) return null;
        return $"{staffName} has used {Fmt(used)} of {Num(quota)} {Category(leave.Type)} day(s) this {fy.Label}.\nThis request adds {Fmt(add)} more and EXCEEDS the balance.\n\n"
            + "Approve it anyway?";
    }

    /// <summary>SCC blackoutsForKey with a scope: entries covering a date that apply to every department or to <paramref name="dept"/>.</summary>
    public static IEnumerable<LeaveBlackout> BlackoutsFor(IEnumerable<LeaveBlackout> all, DateOnly d, string? dept)
    {
        ArgumentNullException.ThrowIfNull(all);
        return all.Where(b => b.Covers(d) && (dept is null || b.Dept.Length == 0 || b.Dept == dept));
    }

    /// <summary>SCC submit gate: blackouts on the non-week-off dates from…to (at most 400 looked at) for a department; labels in first-seen order.</summary>
    public static (IReadOnlyList<LeaveBlackout> Hits, bool Hard, string Labels) BlackoutHits(LeavePolicy policy, DateOnly from, DateOnly to, string dept)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var hits = new List<LeaveBlackout>();
        var g = 0;
        for (var d = from; d <= to && g++ < Guard; d = d.AddDays(1))
            if (!policy.IsWeekOff(d)) hits.AddRange(BlackoutsFor(policy.BlackoutList, d, dept));
        var labels = string.Join(", ", hits.Select(b => b.Text).Distinct());
        return (hits, hits.Any(b => b.Hard), labels);
    }

    public static string HardBlackoutText(string labels) => $"Leave falls in a hard blackout window ({labels}) — blocked";

    public static string SoftBlackoutText(string labels, LeaveType type) => $"These dates fall in a blackout window ({labels}): {Category(type)} leave is discouraged.\nRecord it anyway?";

    /// <summary>SCC short type label (Full Day / Half Day AM / Half Day PM).</summary>
    public static string KindLabel(LeaveHalf? half) => half switch
    {
        LeaveHalf.AM => "Half Day AM",
        LeaveHalf.PM => "Half Day PM",
        _ => "Full Day",
    };
}
