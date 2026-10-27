namespace Hcc.Contracts.PeopleLists;

// Wire contracts of the People lists module (MA-6, plan Part 2 §8 M19–M22): designations, shift patterns, leave types, holiday calendar.
// Rows follow the masters framework (Hcc.Contracts.Masters): id, code, name, sortOrder, active, retiredOn, version; master errors travel as
// VALIDATION / INVALID_TRANSITION with details.reason (MasterReasons) and details.field. Tables hr.designation, hr.shift_pattern,
// hr.leave_type, hr.holiday (migration 2860); change history in mst.master_history (master ids below).

/// <summary>List ids used in <c>mst.master_history.master</c> and by <c>IMasterUsage</c> (the prototype's master spec ids).</summary>
public static class PeopleListIds
{
    public const string Designations = "designations";
    public const string ShiftPatterns = "shiftPatterns";
    public const string LeaveTypes = "leaveTypes";
    public const string Holidays = "holidays";

    public static readonly IReadOnlyList<string> All = [Designations, ShiftPatterns, LeaveTypes, Holidays];
}

/// <summary>A page of one list (status = active | retired | all).</summary>
public sealed record PeopleListPage<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);

/// <summary>Result of a delete (never-used rows only).</summary>
public sealed record DeletePeopleListResult(int Id, string Code, bool Deleted);

// ── Designations (M19) ──

/// <summary><c>DefaultPayRule</c> (monthly-structured | monthly-basic | daily) is a pay field: shown only to PERM-MST-002 / PERM-HR-005 holders, else null.
/// <c>Used</c> = staff records holding the designation.</summary>
public sealed record DesignationView(int DesignationId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, string? Dept, string? Grade,
    string? DefaultPayRule, int Used, string UpdatedUtc, string Version);

/// <summary>SaveDesignation K [V]: <c>DesignationId</c> null = create. <c>DefaultPayRule</c> changes need PERM-MST-002 (Owner); null on an edit = unchanged, "" = cleared.</summary>
public sealed record SaveDesignationRequest(int? DesignationId, string Code, string Name, int? SortOrder = null, string? Dept = null, string? Grade = null,
    string? DefaultPayRule = null, string? ExpectedVersion = null);

public sealed record RetireDesignationRequest(int DesignationId, string ExpectedVersion);

public sealed record ReactivateDesignationRequest(int DesignationId, string ExpectedVersion);

public sealed record DeleteDesignationRequest(int DesignationId, string ExpectedVersion);

public sealed record ReorderDesignationsRequest(IReadOnlyList<int> DesignationIds);

// ── Shift patterns (M20) ──

/// <summary>Start / End are HH:mm (24-hour); End earlier than Start = ends the next morning (a night shift). <c>Used</c> = staff on it as default shift + future roster cells.</summary>
public sealed record ShiftPatternView(int ShiftPatternId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, string Start, string End, int BreakMinutes,
    bool Night, string WeeklyOffRule, int Used, string UpdatedUtc, string Version);

/// <summary>SaveShiftPattern K [V]. WeeklyOffRule: fixed (the staff record's weekly off day, default) | roster (set in the roster, no automatic weekly off).</summary>
public sealed record SaveShiftPatternRequest(int? ShiftPatternId, string Code, string Name, string Start, string End, int? SortOrder = null, int? BreakMinutes = null,
    bool Night = false, string? WeeklyOffRule = null, string? ExpectedVersion = null);

public sealed record RetireShiftPatternRequest(int ShiftPatternId, string ExpectedVersion);

public sealed record ReactivateShiftPatternRequest(int ShiftPatternId, string ExpectedVersion);

public sealed record DeleteShiftPatternRequest(int ShiftPatternId, string ExpectedVersion);

public sealed record ReorderShiftPatternsRequest(IReadOnlyList<int> ShiftPatternIds);

// ── Leave types (M21) ──

/// <summary>SCC's fixed leave categories + LOP (DEC-116). <c>YearlyQuota</c> = CL / SL / EL days per financial year (decimal; null = not tracked).
/// <c>Used</c> = leave records of this category.</summary>
public sealed record LeaveTypeView(int LeaveTypeId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, bool Paid, decimal? YearlyQuota,
    int Used, string UpdatedUtc, string Version);

/// <summary>SaveLeaveType K [V]. Paid (fixed: only LOP unpaid) and the quota are pay rules: every create and any change of them needs PERM-MST-002 (Owner).</summary>
public sealed record SaveLeaveTypeRequest(int? LeaveTypeId, string Code, string Name, bool Paid = true, decimal? YearlyQuota = null, int? SortOrder = null,
    string? ExpectedVersion = null);

public sealed record RetireLeaveTypeRequest(int LeaveTypeId, string ExpectedVersion);

public sealed record ReactivateLeaveTypeRequest(int LeaveTypeId, string ExpectedVersion);

public sealed record DeleteLeaveTypeRequest(int LeaveTypeId, string ExpectedVersion);

public sealed record ReorderLeaveTypesRequest(IReadOnlyList<int> LeaveTypeIds);

// ── Holiday calendar (M22) ──

/// <summary><c>Code</c> = the date (YYYY-MM-DD); Fy = "FY 2026–27". Kind: paid | restricted. <c>Used</c> = locked pay-runs of the holiday's month.</summary>
public sealed record HolidayView(int HolidayId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, string Kind, string Fy, int Used, string UpdatedUtc, string Version);

/// <summary>SaveHoliday K [V]: Owner only (PERM-MST-002) — holidays change pay and statutory due dates.</summary>
public sealed record SaveHolidayRequest(int? HolidayId, string Code, string Name, string Kind, int? SortOrder = null, string? ExpectedVersion = null);

public sealed record RetireHolidayRequest(int HolidayId, string ExpectedVersion);

public sealed record ReactivateHolidayRequest(int HolidayId, string ExpectedVersion);

public sealed record DeleteHolidayRequest(int HolidayId, string ExpectedVersion);

public sealed record ReorderHolidaysRequest(IReadOnlyList<int> HolidayIds);
