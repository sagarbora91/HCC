using System.Globalization;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Payroll;

// DEC-075 (audit A04-02): correcting past attendance in the open pay month. A past day holds nothing (unmarked = absent for pay), an
// attendance mark or a one-day approved leave; a correction replaces it with another entry. Whether the correction RAISES pay is decided by
// the month's own payroll rules: the gross pay (earned + overtime) of the staff member's month line with the corrected day against the line
// without it. Prototype mirror: 03 Prototype/src/domain/streams/staff.ts (hrCorrectionWindow, hrDayEntry, hrCorrectionEffect).

public enum DayEntryKind { None, Mark, Leave }

/// <summary>What a past day holds for pay: nothing (unmarked), an attendance mark (status + overtime) or a one-day approved leave.</summary>
public sealed record DayEntry(DayEntryKind Kind, AttendanceStatus? Status = null, int OtMinutes = 0, LeaveType? LeaveType = null)
{
    public static readonly DayEntry Unmarked = new(DayEntryKind.None);

    public static DayEntry Mark(AttendanceStatus status, int otMinutes) => new(DayEntryKind.Mark, status, otMinutes);

    public static DayEntry Leave(LeaveType type) => new(DayEntryKind.Leave, LeaveType: type);

    /// <summary>Wire and storage code: <c>none</c>, an attendance status P|L|H|A|OFF or a leave type CL|SL|EL|CO|ML|LOP.</summary>
    public string Code => Kind switch
    {
        DayEntryKind.Mark => Status!.Value.ToString(),
        DayEntryKind.Leave => LeaveType!.Value.ToString(),
        _ => "none",
    };

    /// <summary>Text for audit and screens (prototype HR_ATT_LABEL / HR_LEAVE_LABEL).</summary>
    public string Label => Kind switch
    {
        DayEntryKind.Mark => MarkLabel(Status!.Value) + (OtMinutes > 0 ? string.Create(CultureInfo.InvariantCulture, $" · OT {OtMinutes} min") : ""),
        DayEntryKind.Leave => LeaveLabel(LeaveType!.Value),
        _ => "Not marked",
    };

    public static string MarkLabel(AttendanceStatus s) => s switch
    {
        AttendanceStatus.P => "Present",
        AttendanceStatus.L => "Late",
        AttendanceStatus.H => "Half-day",
        AttendanceStatus.A => "Absent",
        _ => "Worked on off day",
    };

    public static string LeaveLabel(LeaveType t) => t switch
    {
        Payroll.LeaveType.CL => "Casual leave",
        Payroll.LeaveType.SL => "Sick leave",
        Payroll.LeaveType.EL => "Earned leave",
        Payroll.LeaveType.CO => "Compensatory off",
        Payroll.LeaveType.ML => "Maternity / paternity leave",
        _ => "Leave without pay",
    };

    /// <summary>Parses a correction target: <c>none</c> (clear the day), an attendance status (with 0–480 overtime minutes, none when absent) or a leave type.</summary>
    public static DayEntry Parse(string? code, int? otMinutes)
    {
        var ot = otMinutes ?? 0;
        if (code == "none")
        {
            if (ot != 0) throw HccException.Validation("A cleared day carries no overtime.");
            return Unmarked;
        }

        if (code is "P" or "L" or "H" or "A" or "OFF")
        {
            var s = Enum.Parse<AttendanceStatus>(code);
            if (ot is < 0 or > 480) throw HccException.Validation("Overtime must be 0–480 minutes.");
            if (s == AttendanceStatus.A && ot > 0) throw HccException.Validation("An absent day cannot carry overtime.");
            return Mark(s, ot);
        }

        if (code is "CL" or "SL" or "EL" or "CO" or "ML" or "LOP")
        {
            if (ot != 0) throw HccException.Validation("A leave day carries no overtime.");
            return Leave(Enum.Parse<LeaveType>(code));
        }

        throw HccException.Validation("Choose what the day should be: an attendance mark, a leave type or 'none'.");
    }
}

/// <summary>A day as a correction finds it: the entry, the one-day approved leave holding it (replaced by the correction), whether an approved
/// half-day leave sits beside a mark, a multi-day approved leave covering it (cannot be corrected day by day) and whether a pending leave covers it.</summary>
public sealed record DayState(DayEntry Entry, LeaveSpan? OneDayLeave, bool HalfDayLeave, LeaveSpan? MultiDayLeave, bool PendingLeave);

public static class PayrollCorrections
{
    /// <summary>
    /// The days a correction may touch (DEC-075 "the open pay month"): from the 1st of the month after the latest LOCKED pay-run — at the
    /// earliest the 1st of the month before the business date's month — to the day before the business date (today is marked with
    /// MarkAttendance; the future is never corrected). Empty when <c>To &lt; From</c>. A locked month is never inside the window.
    /// </summary>
    public static (DateOnly From, DateOnly To) Window(DateOnly businessDate, PayMonth? lastLocked)
    {
        var floor = PayMonth.Of(businessDate).Previous.First;
        var from = lastLocked is { } m && m.Next.First > floor ? m.Next.First : floor;
        return (from, businessDate.AddDays(-1));
    }

    /// <summary>What the date holds now, in the order the month counts read it (attendance mark first, then a full-day approved leave).</summary>
    public static DayState Current(StaffDays days, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(days);
        var approved = days.Leaves.Where(l => l.Status == LeaveStatus.Approved && l.Covers(date)).ToList();
        var full = approved.FirstOrDefault(l => l.Half is null);
        var half = approved.Any(l => l.Half is not null);
        var pending = days.Leaves.Any(l => l.Status == LeaveStatus.Pending && l.Covers(date));
        if (days.Attendance.TryGetValue(date, out var m)) return new DayState(DayEntry.Mark(m.Status, m.OtMinutes), null, half, null, pending);
        if (full is null) return new DayState(DayEntry.Unmarked, null, half, null, pending);
        return full.From == full.To ? new DayState(DayEntry.Leave(full.Type), full, false, null, pending) : new DayState(DayEntry.Leave(full.Type), null, false, full, pending);
    }

    /// <summary>The staff member's days with <paramref name="date"/> holding <paramref name="entry"/> instead: the day's mark is replaced or
    /// removed, the replaced one-day leave is cancelled and a leave entry is a new one-day approved leave (id 0).</summary>
    public static StaffDays With(StaffDays days, StaffProfile st, DateOnly date, DayEntry entry, long? replacedLeaveId)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(st);
        ArgumentNullException.ThrowIfNull(entry);
        var att = days.Attendance.Where(kv => kv.Key != date).ToDictionary(kv => kv.Key, kv => kv.Value);
        var leaves = days.Leaves.Select(l => replacedLeaveId is { } id && l.LeaveId == id ? l with { Status = LeaveStatus.Cancelled } : l).ToList();
        if (entry.Kind == DayEntryKind.Mark) att[date] = new AttendanceMark(date, entry.Status!.Value, entry.OtMinutes);
        else if (entry.Kind == DayEntryKind.Leave) leaves.Add(new LeaveSpan(0, st.StaffId, entry.LeaveType!.Value, date, date, null, 1m, LeaveStatus.Approved));
        return new StaffDays(days.Roster, att, leaves, days.Holidays);
    }

    /// <summary>Gross pay (earned + overtime) of the staff member's month line on these days — the DEC-075 "raises pay" test compares two of these.</summary>
    public static long Gross(StaffProfile st, StaffDays days, PayMonth month, PayrollRates rates)
        => PayrollRules.ComputeLine(st, PayrollRules.Counts(st, days, month), rates).Gross;
}
