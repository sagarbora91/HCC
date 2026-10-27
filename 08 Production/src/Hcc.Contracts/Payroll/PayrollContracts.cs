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
/// DEC-084: bank details set by anyone but the Owner are not applied: they become a pending bank-change request the Owner approves
/// (<see cref="StaffResult.BankRequestId"/>; payments keep the current details meanwhile). <see cref="BankReason"/> = optional reason for it.
/// </summary>
public sealed record SaveStaffRequest(long? StaffId, string? ExpectedVersion, string Name, string Dept, string Designation, string DefaultShift, int WeeklyOff,
    string? Phone = null, int? LinkUserId = null, bool ConfirmRelink = false, DateOnly? Joined = null,
    string? PayType = null, long? MonthlyPaise = null, long? DailyPaise = null, string? Gender = null, string? Structure = null,
    bool? PfApplicable = null, bool? EsicApplicable = null, string? BankName = null, string? AccountNo = null, string? Ifsc = null, string? BankReason = null,
    // MA-6: the designation chosen from the Designations list (code); its name is stored in Designation. Null = free text / unchanged link.
    string? DesignationCode = null);

public sealed record StaffResult(long StaffId, string Name, string Version, string Notes, long? BankRequestId = null);

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
    int? OtBps = null, int? LateBlock = null, string? Rounding = null, bool? EsicPeriod = null, int? DaysOverride = null);

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

/// <summary>
/// DEC-075: correct a PAST day of the open pay month (from the 1st of the month after the latest locked pay-run, at the earliest the month before
/// the business date's month, to yesterday). <see cref="Mark"/> = P|L|H|A|OFF (with <see cref="OtMinutes"/> 0–480), a full-day leave type
/// CL|SL|EL|CO|ML|LOP (back-dated leave, one day) or <c>none</c> (clear the day). A correction that raises the month's gross pay becomes a
/// pending request the Owner approves; one that lowers or keeps pay (or any correction by the Owner) applies at once. Audited before → after.
/// </summary>
public sealed record CorrectAttendanceRequest(long StaffId, DateOnly Date, string Mark, string Reason, int? OtMinutes = null);

/// <summary>Status Applied (the day changed; the month's draft pay-run is recalculated) or PendingApproval (waiting for the Owner).
/// Money is not in the result (the Manager sees no salaries); the request view shows the pay effect to payroll roles.</summary>
public sealed record CorrectionResult(long RequestId, long StaffId, DateOnly Date, string Status, string Before, string After, bool RaisesPay, string Version,
    string Notes);

/// <summary>The Owner approves or rejects a pending request (attendance correction or bank-detail change); a rejection needs a note.</summary>
public sealed record DecidePayrollRequestRequest(long RequestId, string ExpectedVersion, bool Approve, string? Note = null);

/// <summary>The requester withdraws their own pending request.</summary>
public sealed record WithdrawPayrollRequestRequest(long RequestId, string ExpectedVersion);

public sealed record PayrollRequestResult(long RequestId, string Kind, long StaffId, string Status, string Version, string Notes);

/// <summary>DEC-083: add or rename a paid holiday (Owner). A holiday in a locked pay month cannot change.</summary>
public sealed record SavePaidHolidayRequest(DateOnly Date, string Name);

public sealed record RemoveHolidayRequest(DateOnly Date);

public sealed record HolidayResult(DateOnly Date, string Name, string Notes);

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
    bool PhoneUsable = false, string? Ifsc = null,
    // MA-6: code of the designation list row (null = older record matched by name).
    string? DesignationCode = null);

public sealed record PayLineView(long StaffId, string Name, string Dept, string Designation, string PayType, decimal PayableDays,
    long EarnedPaise, long BasicPaise, long HraPaise, long WashingPaise, long SpecialPaise, long OtPaise, long GrossPaise, long PfWagesPaise, long PfPaise,
    long PfErPaise, long EsicWagesPaise, long EsicPaise, long EsicErPaise, long PtPaise, long AdvancePaise, long DeductionsPaise, long NetPaise, long CtcPaise,
    bool? EsicCovered, string PayslipMark, SccDaysView? Days = null);

/// <summary>DEC-115: the SCC day sheet behind a line (days; halves allowed) — prototype HRSccDays.</summary>
public sealed record SccDaysView(decimal Total, decimal Present, decimal Half, decimal Leave, decimal Absent, decimal Late, decimal LateDed,
    decimal AbsentDed, decimal HalfDed, decimal Deduction, decimal Extra, decimal FinalPresent, decimal SalaryDays, decimal OtDays, decimal NoThumb = 0,
    decimal NoThumbDed = 0);

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
    bool EsicPeriod, int DaysOverride = 0);

public sealed record AttendanceCountsView(string Ym, int Days, decimal P, decimal L, decimal H, decimal HL, decimal A, decimal OFFW, decimal Off,
    decimal PaidLeave, decimal LopLeave, decimal Unmarked, decimal Na, int OtMin, decimal Hol = 0, int MgmtHol = 0, int? Elapsed = null, decimal Nt = 0);

/// <summary>A stored pay-run line in full (counts, structure, base) as calculated by the server.</summary>
public sealed record BoardLineView(long StaffId, string Name, string Dept, string Designation, string PayType, string Structure, string Gender, long BasePaise,
    AttendanceCountsView Counts, int LatePenaltyHalf, decimal PayableDays, long EarnedPaise, long BasicPaise, long HraPaise, long WashingPaise, long SpecialPaise,
    long OtPaise, long GrossPaise, long PfWagesPaise, long PfPaise, long PfErPaise, long EsicWagesPaise, long EsicPaise, long EsicErPaise, long PtPaise,
    long AdvancePaise, long DeductionsPaise, long NetPaise, long CtcPaise, bool? EsicCovered, SccDaysView? Days = null);

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

/// <summary>DEC-083: a paid holiday of the property (every caller; not money).</summary>
public sealed record HolidayView(DateOnly Date, string Name);

/// <summary>A day entry: Kind none|mark|leave, Status P|L|H|A|OFF (mark), LeaveType (leave).</summary>
public sealed record DayEntryView(string Kind, string? Status, int OtMinutes, string? LeaveType);

/// <summary>Bank details in a bank-change request; the account number is always masked (••••1234).</summary>
public sealed record BankDetailsView(string? BankName, string? AccountMasked, string? Ifsc);

/// <summary>
/// A payroll request (DEC-075 attendance correction, DEC-084 bank-detail change): pending, decided, withdrawn, or applied at once (Direct).
/// Attendance requests for PERM-HR-001 / PERM-HR-006 holders, with <see cref="PayEffectPaise"/> (gross pay change of the month) only for
/// payroll roles (PERM-HR-005 / PERM-HR-006); bank requests only for PERM-HR-005 holders. Pending ones and those requested in the board window.
/// </summary>
public sealed record PayrollRequestView(long RequestId, string Kind, long StaffId, DateOnly? Date, DayEntryView? Before, DayEntryView? After,
    BankDetailsView? BankBefore, BankDetailsView? BankAfter, long? PayEffectPaise, string Reason, string Status, bool Direct, int RequestedBy, string RequestedUtc,
    int? DecidedBy, string? DecidedUtc, string? Note, string Version);

public sealed record PayrollBoardView(DateOnly BusinessDate, DateOnly From, DateOnly To, bool AllStaff, bool Payroll, IReadOnlyList<DepartmentView> Departments,
    LeavePolicyView LeavePolicy, IReadOnlyList<BoardRosterView> Roster, IReadOnlyList<BoardAttendanceView> Attendance, IReadOnlyList<BoardLeaveView> Leaves,
    IReadOnlyList<RateVersionView> RateVersions, IReadOnlyList<BoardRunView> Runs, IReadOnlyList<BoardAdvanceView> Advances, IReadOnlyList<BoardFnfView> Fnf,
    IReadOnlyList<HolidayView> Holidays, IReadOnlyList<PayrollRequestView> Requests, DateOnly? CorrectFrom, DateOnly? CorrectTo);
