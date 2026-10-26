namespace Hcc.Contracts.Payroll;

// Wire contracts for payroll and leave (W2-2). Commands: POST /api/v1/commands/<Name> with Idempotency-Key; money = integer paise;
// dates YYYY-MM-DD; months YYYY-MM; versions = base64 rowversion (expectedVersion → STALE_VERSION). Enumerations travel as strings:
// shifts M|E|N|G|OFF, attendance P|L|H|A|OFF, leave CL|SL|EL|CO|ML|LOP, half AM|PM, gender M|F, structure S|B, payType monthly|daily,
// rounding paise|rupee. Every result is a JSON object.

public sealed record SaveDepartmentRequest(string Code, string Name, int? LeaveCap = null);

public sealed record DepartmentResult(string Code, string Name, int? LeaveCap, string Notes);

/// <summary>
/// Create (StaffId null) or edit a staff record. The non-money fields are the full record (an omitted phone keeps the old number).
/// <see cref="LinkUserId"/> is the desired login link (null = none): changing it is Owner-only and never on your own record (F-03).
/// Money and payroll fields (null = unchanged) need PERM-HR-005; changing them on your own record is refused (F-10, F-11).
/// </summary>
public sealed record SaveStaffRequest(long? StaffId, string? ExpectedVersion, string Name, string Dept, string Designation, string DefaultShift, int WeeklyOff,
    string? Phone = null, int? LinkUserId = null, bool ConfirmRelink = false, DateOnly? Joined = null,
    string? PayType = null, long? MonthlyPaise = null, long? DailyPaise = null, string? Gender = null, string? Structure = null,
    bool? PfApplicable = null, bool? EsicApplicable = null, string? BankName = null, string? AccountNo = null, string? Ifsc = null);

public sealed record StaffResult(long StaffId, string Name, string Version, string Notes);

/// <summary>Exit (Active false: exit on the business date) or reactivate a staff record.</summary>
public sealed record SetStaffActiveRequest(long StaffId, string ExpectedVersion, bool Active);

public sealed record SetRosterRequest(long StaffId, DateOnly Date, string Shift);

public sealed record RosterResult(int Cells, string Notes);

/// <summary>Copies the effective roster of the week containing <see cref="Week"/> onto the following week (dates ≥ business date only).</summary>
public sealed record CopyRosterWeekRequest(DateOnly Week);

/// <summary>Attendance for the business date. No status = self check-in from the server clock; with a status = a manager's mark.</summary>
public sealed record MarkAttendanceRequest(long StaffId, string? Status = null, int? OtMinutes = null, string? Note = null);

public sealed record AttendanceResult(long StaffId, DateOnly Date, string Status, int OtMinutes, bool Corrected, string Notes);

public sealed record RequestLeaveRequest(long StaffId, string LeaveType, DateOnly From, DateOnly? To, string? Half, string Reason);

public sealed record LeaveResult(long LeaveId, long StaffId, string LeaveType, DateOnly From, DateOnly To, string? Half, decimal Days, string Status, string Version);

public sealed record DecideLeaveRequest(long LeaveId, string ExpectedVersion, bool Approve, string? Note = null);

public sealed record CancelLeaveRequest(long LeaveId, string ExpectedVersion);

public sealed record SetLeavePolicyRequest(int Cl, int Sl, int El, int DefaultCap);

public sealed record LeavePolicyResult(int Cl, int Sl, int El, int DefaultCap, int StaffOverEntitlement, string Notes);

public sealed record GiveAdvanceRequest(long StaffId, long AmountPaise, long InstallmentPaise, string Reason, string? FromMonth = null);

public sealed record AdvanceResult(long AdvanceId, long StaffId, long AmountPaise, long InstallmentPaise, string FromMonth, string Notes);

/// <summary>New dated statutory rate version; null fields keep the value in force on <see cref="EffectiveFrom"/> (F-05). Owner only.</summary>
public sealed record SetStatutoryRatesRequest(DateOnly? EffectiveFrom = null, int? BasicBps = null, int? HraBps = null, int? WashBps = null,
    int? PfEeBps = null, int? PfErBps = null, long? PfWageCap = null, int? EsicEeBps = null, int? EsicErBps = null, long? EsicCeiling = null,
    long? PtMaleExempt = null, long? PtMaleMid = null, long? PtMaleMidAmt = null, long? PtStd = null, long? PtFeb = null, long? PtFemaleExempt = null,
    int? OtBps = null, int? LateBlock = null, string? Rounding = null, bool? EsicPeriod = null);

public sealed record RatesResult(DateOnly EffectiveFrom, IReadOnlyList<string> Changed, IReadOnlyList<string> Carried, IReadOnlyList<string> Kept,
    IReadOnlyList<string> CompletedMonthsToRecalculate, string Notes);

public sealed record PreparePayRunRequest(string Month);

public sealed record PayRunResult(long RunId, string Month, string Status, int Staff, long NetPaise, string Version, IReadOnlyList<string> Warnings, string Notes);

public sealed record LockPayRunRequest(long RunId, string ExpectedVersion);

/// <summary>Owner only, latest locked month only, with a reason; <see cref="ConfirmBankFile"/> once a bank file was issued (F-09).</summary>
public sealed record UnlockPayRunRequest(long RunId, string ExpectedVersion, string Reason, bool ConfirmBankFile = false);

/// <summary>Records a bank transfer file for a locked run (simulated); a repeat needs <see cref="ConfirmReissue"/> (F-09).</summary>
public sealed record IssueBankFileRequest(long RunId, bool ConfirmReissue = false);

public sealed record BankFileResult(long RunId, int IssueNo, long NetPaise, bool Reissue, string Notes);

/// <summary>Simulated WhatsApp payslip send (DEC-056): nothing leaves the server.</summary>
public sealed record SendPayslipRequest(long RunId, long StaffId);

public sealed record PayslipSendResult(long RunId, long StaffId, string ToMasked, string Notes);

public sealed record FnfAdjustmentDto(string Label, string Kind, long AmountPaise);

public sealed record PrepareFnfRequest(long StaffId, IReadOnlyList<FnfAdjustmentDto>? Adjustments = null);

public sealed record FnfResult(long FnfId, long StaffId, string Status, long NetPaise, long EarningsPaise, long RecoveriesPaise, bool SalaryIncluded,
    string Version, string Notes);

public sealed record DecideFnfRequest(long FnfId, string ExpectedVersion);

// ── Queries ──

public sealed record StaffView(long StaffId, string Name, string Dept, string Designation, string DefaultShift, int WeeklyOff, DateOnly Joined, DateOnly? ExitedOn,
    bool Active, bool Linked, string PhoneMasked, string Version,
    // Payroll fields: null unless the caller holds PERM-HR-005 (the Manager sees the master without salaries, module 17 §2).
    string? PayType = null, long? MonthlyPaise = null, long? DailyPaise = null, string? Gender = null, string? Structure = null, bool? PfApplicable = null,
    bool? EsicApplicable = null, string? BankName = null, string? AccountMasked = null,
    // W4-6: the linked login (null = none), so a manager's edit keeps the link and "my record" is found (DEC-061); not money.
    int? LinkedUserId = null,
    // W4 G2: whether the stored mobile is a usable WhatsApp number (the payslip send resolves it on the server; the number stays masked),
    // and the IFSC for PERM-HR-005 holders (not secret; shown so it can be corrected or cleared). Null IFSC = hidden or none.
    bool PhoneUsable = false, string? Ifsc = null);

public sealed record PayLineView(long StaffId, string Name, string Dept, string Designation, string PayType, decimal PayableDays,
    long EarnedPaise, long BasicPaise, long HraPaise, long WashingPaise, long SpecialPaise, long OtPaise, long GrossPaise, long PfWagesPaise, long PfPaise,
    long PfErPaise, long EsicWagesPaise, long EsicPaise, long EsicErPaise, long PtPaise, long AdvancePaise, long DeductionsPaise, long NetPaise, long CtcPaise,
    bool? EsicCovered, string PayslipMark);

public sealed record PayRunView(long RunId, string Month, string Status, DateOnly RatesFrom, int LockSeq, string Version, IReadOnlyList<string> Warnings,
    IReadOnlyList<PayLineView> Lines, long NetPaise, int BankFilesIssued);

/// <summary>A payslip (own locked payslip for every staff member, DEC-061; any payslip for payroll roles). Rate labels come from the run's snapshot (F-14).</summary>
public sealed record PayslipView(long RunId, string Month, string Status, PayLineView Line, string PfLabel, string EsicLabel, string NetInWords, string Disclaimer);

public sealed record LeaveBalanceView(string LeaveType, int Entitled, decimal Used, decimal Left, decimal Over);

public sealed record CsvFile(string FileName, string Content);

// ── HR desk board (W4-6): one read for the Staff & payroll and My shifts & pay screens ──
// GET /payroll/board?from&to. Departments and the leave policy for every caller; roster, attendance and leave of every staff member
// for PERM-HR-001 holders, otherwise only the caller's own record (PERM-HR-004, DEC-061). Payroll (rate versions, runs with full
// lines, advances, full and final) only for PERM-HR-006 holders; everyone else gets only their OWN line of each LOCKED run (own pay
// only — never hotel money). Figures are the server's stored run lines (never recomputed). Times are ISO-8601 UTC strings.

public sealed record DepartmentView(string Code, string Name, int? LeaveCap);

public sealed record LeavePolicyView(int Cl, int Sl, int El, int DefaultCap);

public sealed record RateVersionView(DateOnly From, int BasicBps, int HraBps, int WashBps, int PfEeBps, int PfErBps, long PfWageCap, int EsicEeBps, int EsicErBps,
    long EsicCeiling, long PtMaleExempt, long PtMaleMid, long PtMaleMidAmt, long PtStd, long PtFeb, long PtFemaleExempt, int OtBps, int LateBlock, string Rounding,
    bool EsicPeriod);

public sealed record AttendanceCountsView(string Ym, int Days, decimal P, decimal L, decimal H, decimal HL, decimal A, decimal OFFW, decimal Off,
    decimal PaidLeave, decimal LopLeave, decimal Unmarked, decimal Na, int OtMin);

/// <summary>A stored pay-run line in full (counts, structure, base) as calculated by the server.</summary>
public sealed record BoardLineView(long StaffId, string Name, string Dept, string Designation, string PayType, string Structure, string Gender, long BasePaise,
    AttendanceCountsView Counts, int LatePenaltyHalf, decimal PayableDays, long EarnedPaise, long BasicPaise, long HraPaise, long WashingPaise, long SpecialPaise,
    long OtPaise, long GrossPaise, long PfWagesPaise, long PfPaise, long PfErPaise, long EsicWagesPaise, long EsicPaise, long EsicErPaise, long PtPaise,
    long AdvancePaise, long DeductionsPaise, long NetPaise, long CtcPaise, bool? EsicCovered);

public sealed record BoardSendView(long StaffId, int LockSeq, string ToMasked, int SentBy, string SentUtc);

public sealed record BoardBankFileView(int IssueNo, int LockSeq, long NetPaise, int IssuedBy, string IssuedUtc);

/// <summary>An undone lock: reason and the net pay per staff id of that lock (F-09).</summary>
public sealed record BoardUnlockView(int LockSeq, string Reason, IReadOnlyDictionary<string, long> Nets, int BankFiles, int UnlockedBy, string UnlockedUtc);

public sealed record BoardRunView(long RunId, string Month, string Status, RateVersionView Rates, IReadOnlyList<string> Warnings, int LockSeq, string Version,
    int PreparedBy, string PreparedUtc, int? LockedBy, string? LockedUtc, IReadOnlyList<BoardLineView> Lines, IReadOnlyList<BoardSendView> Sends,
    IReadOnlyList<BoardBankFileView> BankFiles, IReadOnlyList<BoardUnlockView> Unlocks);

public sealed record BoardRosterView(long StaffId, DateOnly Date, string Shift);

public sealed record BoardAttendanceView(long StaffId, DateOnly Date, string Status, int OtMinutes, int MarkedBy, string MarkedUtc, string? Note);

public sealed record BoardLeaveView(long LeaveId, long StaffId, string LeaveType, DateOnly From, DateOnly To, string? Half, decimal Days, string Reason, string Status,
    int RequestedBy, string RequestedUtc, int? DecidedBy, string? DecidedUtc, string? Note, string Version);

public sealed record BoardRecoveryView(long? RunId, long? FnfId, long AmountPaise);

public sealed record BoardAdvanceView(long AdvanceId, long StaffId, long AmountPaise, long InstallmentPaise, long RecoveredPaise, DateOnly Date, string FromMonth,
    string Reason, int GivenBy, IReadOnlyList<BoardRecoveryView> Recoveries);

public sealed record BoardFnfView(long FnfId, long StaffId, DateOnly LastDay, string Month, bool SalaryIncluded, BoardLineView? Line, RateVersionView Rates,
    long AdvanceOutstandingPaise, IReadOnlyList<FnfAdjustmentDto> Adjustments, long EarningsPaise, long RecoveriesPaise, long NetPaise, string Status,
    int PreparedBy, string PreparedUtc, int? DecidedBy, string? DecidedUtc, string Version);

public sealed record PayrollBoardView(DateOnly BusinessDate, DateOnly From, DateOnly To, bool AllStaff, bool Payroll, IReadOnlyList<DepartmentView> Departments,
    LeavePolicyView LeavePolicy, IReadOnlyList<BoardRosterView> Roster, IReadOnlyList<BoardAttendanceView> Attendance, IReadOnlyList<BoardLeaveView> Leaves,
    IReadOnlyList<RateVersionView> RateVersions, IReadOnlyList<BoardRunView> Runs, IReadOnlyList<BoardAdvanceView> Advances, IReadOnlyList<BoardFnfView> Fnf);
