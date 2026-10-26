using System.Text.Json;
using System.Text.Json.Serialization;
using Hcc.Application.Commands;
using Hcc.Domain.Payroll;

namespace Hcc.Application.Payroll;

/// <summary>Payroll and leave PERM ids (seeded by migration 0901; RBAC matrix v1 has no payroll rows — see the 0901 header).</summary>
public static class PayrollPermissions
{
    /// <summary>Staff master, roster, attendance and leave of others, without salaries (Owner, Manager, Accountant; Tech admin view).</summary>
    public const string StaffView = "PERM-HR-001";

    /// <summary>Non-money staff edits, roster, attendance for others, exit (Owner, Manager — prototype hrCanManage).</summary>
    public const string StaffManage = "PERM-HR-002";

    public const string LeaveDecide = "PERM-HR-003";

    /// <summary>Own roster, self check-in, own leave and OWN payslip (every role, scope O; DEC-061).</summary>
    public const string Self = "PERM-HR-004";

    /// <summary>Salaries, PF/ESIC flags, bank details, PT slab, payslip mobile (Owner, Accountant — prototype hrCanPay).</summary>
    public const string Salary = "PERM-HR-005";

    /// <summary>Prepare/lock pay-runs, bank and statutory files, payslips of others (Owner, Accountant).</summary>
    public const string Run = "PERM-HR-006";

    public const string Advance = "PERM-HR-007";

    /// <summary>Full and final (maker-checker); the Accountant's limit = DEC-066 additions threshold, above it the Owner confirms.</summary>
    public const string Fnf = "PERM-HR-008";

    /// <summary>Statutory rates, leave policy, pay-run unlock, login link (Owner only, DEC-058 — prototype hrCanRates).</summary>
    public const string OwnerPayroll = "PERM-HR-009";
}

/// <summary>Business date and time zone of the property (the server's business date, never the client's).</summary>
public sealed record PropertyClock(DateOnly BusinessDate, string TimeZone);

/// <summary>A staff record as stored (profile + identity/contact/bank fields + version).</summary>
public sealed record StaffRow(StaffProfile Profile, int? UserId, string Phone, string? BankName, string? AccountNo, string? Ifsc, string Version)
{
    public long StaffId => Profile.StaffId;
}

public sealed record NewStaff(int PropertyId, StaffProfile Profile, int? UserId, string Phone, string? BankName, string? AccountNo, string? Ifsc, int By, DateTime Utc);

public sealed record DepartmentRow(string Code, string Name, int? LeaveCap);

public sealed record LeaveRow(LeaveSpan Span, int PropertyId, string Reason, int RequestedBy, DateTime RequestedUtc, int? DecidedBy, string? Note, string Version);

public sealed record AdvanceRow(long AdvanceId, long StaffId, long AmountPaise, long InstallmentPaise, long RecoveredPaise, DateOnly Date, PayMonth FromMonth)
{
    public long Balance => AmountPaise - RecoveredPaise;
}

public sealed record RunRow(long RunId, int PropertyId, PayMonth Month, bool Locked, PayrollRates Rates, IReadOnlyList<string> Warnings, int LockSeq, int PreparedBy,
    int? LockedBy, string Version);

public sealed record FnfRow(long FnfId, int PropertyId, long StaffId, PayMonth Month, bool SalaryIncluded, PayLine? Line, long AdvanceOutstanding,
    IReadOnlyList<FnfAdjustment> Adjustments, long Additions, long Earnings, long Recoveries, long Net, string Status, int PreparedBy, string Version);

public sealed record NewFnf(int PropertyId, long StaffId, FnfFigures Figures, IReadOnlyList<FnfAdjustment> Adjustments, int By, DateTime Utc);

/// <summary>HR desk board detail (W4-6 read model): who / when / notes that the rule inputs (<see cref="StaffDays"/>, <see cref="RunRow"/>) leave out.</summary>
public sealed record AttendanceDetail(long StaffId, DateOnly Date, AttendanceStatus Status, int OtMinutes, int MarkedBy, DateTime MarkedUtc, string? Note);

public sealed record RosterCell(long StaffId, DateOnly Date, ShiftCode Shift);

public sealed record RunDetail(long RunId, DateTime PreparedUtc, DateTime? LockedUtc);

public sealed record AdvanceDetail(long AdvanceId, string Reason, int GivenBy);

/// <summary>A recovery still in force (reversed recoveries are left out).</summary>
public sealed record RecoveryDetail(long AdvanceId, long? RunId, long? FnfId, long AmountPaise);

public sealed record BankFileDetail(long RunId, int IssueNo, int LockSeq, long NetPaise, int IssuedBy, DateTime IssuedUtc);

public sealed record PayslipSendDetail(long RunId, int LockSeq, long StaffId, string To, int SentBy, DateTime SentUtc);

public sealed record UnlockDetail(long RunId, int LockSeq, string Reason, IReadOnlyDictionary<long, long> Nets, int BankFiles, int UnlockedBy, DateTime UnlockedUtc);

public sealed record FnfDetail(long FnfId, DateOnly LastDay, PayrollRates Rates, DateTime PreparedUtc, int? DecidedBy, DateTime? DecidedUtc);

/// <summary>Everything the board needs beyond the existing reads, for one property: roster and attendance in [from, to], every leave with its decision time.</summary>
public sealed record PayrollBoardDetail(IReadOnlyList<RosterCell> Roster, IReadOnlyList<AttendanceDetail> Attendance, IReadOnlyList<LeaveRow> Leaves,
    IReadOnlyDictionary<long, DateTime> LeaveDecidedUtc, IReadOnlyList<RunDetail> Runs, IReadOnlyList<AdvanceDetail> Advances, IReadOnlyList<RecoveryDetail> Recoveries,
    IReadOnlyList<BankFileDetail> BankFiles, IReadOnlyList<PayslipSendDetail> Sends, IReadOnlyList<UnlockDetail> Unlocks, IReadOnlyList<FnfDetail> Fnfs);

/// <summary>ESIC coverage recorded on a locked run line (F-07 contribution period).</summary>
public sealed record LockedCoverage(long StaffId, PayMonth Month, bool? Covered);

/// <summary>DEC-083: a paid holiday of the property (hr.holiday).</summary>
public sealed record HolidayRow(DateOnly Date, string Name);

/// <summary>Bank details as a bank-change request holds them (full account number: the request applies it; screens and audit mask it).</summary>
public sealed record BankChange(string? BankName, string? AccountNo, string? Ifsc);

/// <summary>Payroll request kinds and statuses (hr.change_request, migration 3400).</summary>
public static class PayrollRequests
{
    public const string Attendance = "attendance";
    public const string Bank = "bank";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";
}

/// <summary>
/// A payroll request as stored: DEC-075 attendance correction (date, before → after entry, gross pay change of the month) or DEC-084 bank-detail
/// change (before → after details). <see cref="Direct"/> = applied at once without a checker (a correction that lowers or keeps pay, or the Owner's own).
/// </summary>
public sealed record PayrollRequestRow(long RequestId, int PropertyId, string Kind, long StaffId, DateOnly? Date, DayEntry? Before, DayEntry? After,
    BankChange? BankBefore, BankChange? BankAfter, long? PayEffect, string Reason, string Status, bool Direct, int RequestedBy, DateTime RequestedUtc,
    int? DecidedBy, DateTime? DecidedUtc, string? Note, string Version);

public sealed record NewPayrollRequest(int PropertyId, string Kind, long StaffId, DateOnly? Date, DayEntry? Before, DayEntry? After, BankChange? BankBefore,
    BankChange? BankAfter, long? PayEffect, string Reason, bool Direct, int By, DateTime Utc);

/// <summary>
/// Payroll persistence over one connection (inside a command transaction, or a read connection for queries). <c>Lock*</c> methods take
/// UPDLOCK; money commands first take the property's payroll anchor (<see cref="LockAnchorAsync"/>). Lock order: anchor → department → staff
/// → leave → run → F&amp;F.
/// </summary>
public interface IPayrollDb
{
    Task<PropertyClock> PropertyAsync(int propertyId, CancellationToken ct);

    Task LockAnchorAsync(int propertyId, CancellationToken ct);

    Task<IReadOnlyList<DepartmentRow>> DepartmentsAsync(int propertyId, CancellationToken ct);

    Task<DepartmentRow?> LockDepartmentAsync(int propertyId, string code, CancellationToken ct);

    Task UpsertDepartmentAsync(int propertyId, DepartmentRow dept, CancellationToken ct);

    Task<bool> UserAtPropertyAsync(int propertyId, int userId, CancellationToken ct);

    Task<StaffRow?> StaffAsync(int propertyId, long staffId, bool lockRow, CancellationToken ct);

    Task<StaffRow?> StaffOfUserAsync(int propertyId, int userId, CancellationToken ct);

    Task<IReadOnlyList<StaffRow>> AllStaffAsync(int propertyId, CancellationToken ct);

    Task<long> InsertStaffAsync(NewStaff staff, CancellationToken ct);

    /// <summary>Versioned update of every column; STALE_VERSION when <paramref name="expectedVersion"/> no longer matches.</summary>
    Task<string> UpdateStaffAsync(StaffRow row, string expectedVersion, CancellationToken ct);

    Task<int> LockedPayslipCountAsync(long staffId, CancellationToken ct);

    /// <summary>Roster overrides, attendance and the property's paid holidays in [from, to] and ALL leaves (any status, request order) for the given staff.</summary>
    Task<IReadOnlyDictionary<long, StaffDays>> DaysAsync(int propertyId, IReadOnlyCollection<long> staffIds, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>DEC-083: every paid holiday of the property, by date.</summary>
    Task<IReadOnlyList<HolidayRow>> HolidaysAsync(int propertyId, CancellationToken ct);

    /// <summary>Adds or renames a holiday; returns the previous name (null when it was added).</summary>
    Task<string?> UpsertHolidayAsync(int propertyId, DateOnly date, string name, int by, DateTime utc, CancellationToken ct);

    /// <summary>Removes a holiday; returns its name (null when there was none).</summary>
    Task<string?> DeleteHolidayAsync(int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>DEC-075: removes a day's attendance mark (the correction's request row keeps it as "before").</summary>
    Task DeleteAttendanceAsync(long staffId, DateOnly date, CancellationToken ct);

    /// <summary>DEC-075: a one-day approved leave created by an attendance correction (linked to its request; decided by the approver).</summary>
    Task<long> InsertCorrectionLeaveAsync(int propertyId, LeaveSpan span, string reason, int requestedBy, int decidedBy, long requestId, DateTime utc,
        CancellationToken ct);

    Task<PayrollRequestRow> InsertRequestAsync(NewPayrollRequest request, CancellationToken ct);

    Task<PayrollRequestRow?> RequestAsync(int propertyId, long requestId, bool lockRow, CancellationToken ct);

    /// <summary>The property's requests (all, or only those in <paramref name="status"/>), oldest first.</summary>
    Task<IReadOnlyList<PayrollRequestRow>> RequestsAsync(int propertyId, string? status, CancellationToken ct);

    /// <summary>Pending → Approved / Rejected / Withdrawn, versioned (STALE_VERSION when the request changed).</summary>
    Task<string> DecideRequestAsync(long requestId, string expectedVersion, string status, int by, DateTime utc, string? note, CancellationToken ct);

    Task SetRosterAsync(long staffId, DateOnly date, ShiftCode shift, int by, DateTime utc, CancellationToken ct);

    /// <summary>Upserts the mark; returns true when a mark existed (a correction).</summary>
    Task<bool> UpsertAttendanceAsync(long staffId, AttendanceMark mark, string? note, int by, DateTime utc, CancellationToken ct);

    Task<bool> AnyAttendanceAsync(long staffId, DateOnly from, DateOnly to, bool exceptHalfDayMarks, CancellationToken ct);

    Task<LeaveRow> InsertLeaveAsync(int propertyId, LeaveSpan span, string reason, int by, DateTime utc, CancellationToken ct);

    Task<LeaveRow?> LockLeaveAsync(int propertyId, long leaveId, CancellationToken ct);

    Task<string> UpdateLeaveStatusAsync(long leaveId, string expectedVersion, LeaveStatus status, int by, DateTime utc, string? note, CancellationToken ct);

    /// <summary>Pending/approved leaves of a department's staff overlapping [from, to], with the staff profile (capacity check).</summary>
    Task<IReadOnlyList<(StaffProfile Staff, LeaveSpan Leave)>> DepartmentLeavesAsync(int propertyId, string dept, DateOnly from, DateOnly to, CancellationToken ct);

    Task<LeavePolicy> LeavePolicyAsync(int propertyId, CancellationToken ct);

    Task SetLeavePolicyAsync(int propertyId, LeavePolicy policy, int by, DateTime utc, CancellationToken ct);

    /// <summary>Stored versions, oldest first; the SCC defaults (from 1 Apr 2025) when none is stored yet.</summary>
    Task<IReadOnlyList<PayrollRates>> RateVersionsAsync(int propertyId, CancellationToken ct);

    /// <summary>Replaces the property's versions with the full list <paramref name="versions"/> (saved at <paramref name="from"/>) and appends the change history row.</summary>
    Task ReplaceRateVersionsAsync(int propertyId, DateOnly from, IReadOnlyList<PayrollRates> versions, PayrollRates? before, IReadOnlyList<string> changed,
        IReadOnlyList<string> carried, int by, DateTime utc, CancellationToken ct);

    Task<IReadOnlyList<AdvanceRow>> AdvancesAsync(int propertyId, CancellationToken ct);

    Task<long> InsertAdvanceAsync(int propertyId, long staffId, long amount, long installment, DateOnly date, PayMonth fromMonth, string reason, int by, DateTime utc, CancellationToken ct);

    Task RecordRecoveryAsync(long advanceId, long? runId, long? fnfId, long amount, DateTime utc, CancellationToken ct);

    Task<int> ReverseRunRecoveriesAsync(long runId, int by, DateTime utc, CancellationToken ct);

    Task<IReadOnlyList<RunRow>> RunsAsync(int propertyId, CancellationToken ct);

    Task<RunRow?> RunAsync(int propertyId, long runId, bool lockRow, CancellationToken ct);

    Task<IReadOnlyList<PayLine>> RunLinesAsync(long runId, CancellationToken ct);

    Task<IReadOnlyList<LockedCoverage>> LockedCoverageAsync(int propertyId, PayMonth from, PayMonth toExclusive, CancellationToken ct);

    Task<(long RunId, string Version)> InsertRunAsync(int propertyId, PayMonth month, PayrollRates rates, IReadOnlyList<PayLine> lines, IReadOnlyList<string> warnings,
        int by, DateTime utc, CancellationToken ct);

    Task<string> ReplaceDraftAsync(long runId, PayrollRates rates, IReadOnlyList<PayLine> lines, IReadOnlyList<string> warnings, int by, DateTime utc, CancellationToken ct);

    /// <summary>Draft → Locked (lock_seq + 1) and the bank snapshot of the run's staff (F-02), versioned.</summary>
    Task<(string Version, int LockSeq)> LockRunAsync(long runId, string expectedVersion, int by, DateTime utc, CancellationToken ct);

    /// <summary>Locked → Draft with the unlock record (nets of the lock undone, bank files issued), versioned.</summary>
    Task<string> UnlockRunAsync(long runId, string expectedVersion, int lockSeq, string reason, IReadOnlyDictionary<long, long> nets, int bankFiles, int by, DateTime utc,
        CancellationToken ct);

    Task<IReadOnlyDictionary<long, BankDetails>> BankSnapshotAsync(long runId, int lockSeq, CancellationToken ct);

    /// <summary>(issue_no, lock_seq, net) of every bank file of the run, oldest first.</summary>
    Task<IReadOnlyList<(int IssueNo, int LockSeq, long Net, DateTime Utc)>> BankFilesAsync(long runId, CancellationToken ct);

    Task InsertBankFileAsync(long runId, int issueNo, int lockSeq, long net, int by, DateTime utc, CancellationToken ct);

    /// <summary>(staff, lock_seq) of every payslip send of the run.</summary>
    Task<IReadOnlyList<(long StaffId, int LockSeq)>> PayslipSendsAsync(long runId, CancellationToken ct);

    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<long, long>>> UnlockNetsAsync(long runId, CancellationToken ct);

    Task InsertPayslipSendAsync(long runId, int lockSeq, long staffId, string to, long net, int by, DateTime utc, CancellationToken ct);

    Task<IReadOnlyList<FnfRow>> FnfsAsync(int propertyId, CancellationToken ct);

    Task<FnfRow?> FnfAsync(int propertyId, long fnfId, bool lockRow, CancellationToken ct);

    Task<(long FnfId, string Version)> InsertFnfAsync(NewFnf fnf, CancellationToken ct);

    Task<string> DecideFnfAsync(long fnfId, string expectedVersion, string status, int by, DateTime utc, CancellationToken ct);

    /// <summary>Read model of the HR desk board (W4-6): roster and attendance in [from, to] plus the who/when detail of leaves, runs, advances and F&amp;F.</summary>
    Task<PayrollBoardDetail> BoardDetailAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>A read connection for queries (read committed, no transaction).</summary>
public interface IPayrollReadDb : IPayrollDb, IAsyncDisposable
{
    /// <summary>W4 G2: an audit row for a file download (register, statutory or bank file) — append-only plat.audit_event, operation 'Download'.</summary>
    Task AuditDownloadAsync(int propertyId, int principalId, string action, string target, string detail, CancellationToken ct);
}

/// <summary>Payroll store: the same operations inside a command transaction or on a read connection.</summary>
public interface IPayrollStore
{
    IPayrollDb In(ICommandTransaction tx);

    Task<IPayrollReadDb> OpenReadAsync(CancellationToken ct);
}

/// <summary>JSON for stored snapshots (rates, lines, adjustments): camelCase, enums as camelCase strings.</summary>
public static class PayrollJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException("Empty payroll snapshot.");
}
