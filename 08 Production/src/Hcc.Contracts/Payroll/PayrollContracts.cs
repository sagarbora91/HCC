namespace Hcc.Contracts.Payroll;

// Wire contracts for payroll and leave (W2-2). Commands: POST /api/v1/commands/<Name> with Idempotency-Key; money = integer paise;
// dates YYYY-MM-DD; months YYYY-MM; versions = base64 rowversion (expectedVersion → STALE_VERSION). Enumerations travel as strings:
// shifts M|E|N|G|OFF, attendance P|L|H|A|OFF|NT, leave CL|SL|EL|ML|CO|OTH|LOP (SCC's six + LOP), half AM|PM, gender M|F, structure S|B,
// payType monthly|daily, rounding paise|rupee. Every result is a JSON object.
// SCC leave port (DEC-116 / DEC-119, migration 4110): a leave is ONE RECORD PER DATE; RequestLeave records every date of the range SCC's way
// (week-off → duplicate → 3 a day / 4 a shift) and returns SCC's counts; DecideLeave / CancelLeave act on one date record.

/// <summary>A department; <see cref="LeaveCap"/> = the SCC per-store leave cap, ADVISORY (whole number ≥ 0; null = SCC MAX_DAY 3).</summary>
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

/// <summary>SCC Add Leave: <see cref="ApproverId"/> required (the Owner or a Manager, never the person on leave); <see cref="Reason"/> optional (≤ 500);
/// a half day may cover a range. <see cref="ConfirmOverBalance"/> / <see cref="ConfirmBlackout"/> = SCC's confirm dialogs answered OK.</summary>
public sealed record RequestLeaveRequest(long StaffId, string LeaveType, DateOnly From, DateOnly? To, string? Half, string? Reason, int? ApproverId = null,
    bool ConfirmOverBalance = false, bool ConfirmBlackout = false);

/// <summary>SCC submitLeave's outcome: the records added (one per date) and the dates skipped as full / duplicate / week-off, with SCC's toast.</summary>
public sealed record LeaveRequestResult(IReadOnlyList<LeaveResult> Added, int Blocked, int Duplicates, int WeekOff, string Notes);

public sealed record LeaveResult(long LeaveId, long StaffId, string LeaveType, DateOnly From, DateOnly To, string? Half, decimal Days, string Status, string Version);

/// <summary>One date record approved or rejected. DEC-122: an approval that takes the person over the quota for that date's leave year needs
/// <see cref="ConfirmOverBalance"/> (SCC's over-quota warning answered OK); a rejection ignores it.</summary>
public sealed record DecideLeaveRequest(long LeaveId, string ExpectedVersion, bool Approve, string? Note = null, bool ConfirmOverBalance = false);

public sealed record CancelLeaveRequest(long LeaveId, string ExpectedVersion);

/// <summary>SCC leave settings (Owner): entitlement defaults (any number ≥ 0, no maximum) and the hotel's leave week-off set (bit 0 = Sunday;
/// null = unchanged). <see cref="DefaultCap"/> is no longer used (SCC: 3 a day for the property, department caps advisory).</summary>
public sealed record SetLeavePolicyRequest(decimal Cl, decimal Sl, decimal El, int? WeekOffMask = null, int? DefaultCap = null);

public sealed record LeavePolicyResult(decimal Cl, decimal Sl, decimal El, int WeekOffMask, int StaffOverEntitlement, string Notes);

/// <summary>SCC ⚙ per-staff entitlement override; a null category uses the default; all null = override removed.</summary>
public sealed record SetLeaveEntOverrideRequest(long StaffId, decimal? Cl, decimal? Sl, decimal? El);

public sealed record LeaveEntOverrideResult(long StaffId, decimal? Cl, decimal? Sl, decimal? El, string Notes);

/// <summary>SCC blackout / store holiday: Dept '' = every department; Kind blackout|holiday; Hard (blackouts only) refuses leave; Label ≤ 80.</summary>
public sealed record AddLeaveBlackoutRequest(DateOnly From, DateOnly To, string? Dept, string? Kind, bool Hard, string? Label);

public sealed record RemoveLeaveBlackoutRequest(long BlackoutId);

public sealed record LeaveBlackoutResult(long BlackoutId, DateOnly From, DateOnly To, string Dept, string Kind, bool Hard, string Label, string Notes);

/// <summary>SCC agenda item: Title ≤ 200, Time HH:MM or blank, Type meeting|agenda|reminder, Description ≤ 1000.</summary>
public sealed record AddLeaveAgendaRequest(DateOnly Date, string Title, string? Time, string? Type, string? Description);

public sealed record RemoveLeaveAgendaRequest(long AgendaId);

public sealed record LeaveAgendaResult(long AgendaId, string Notes);

/// <summary>One record of an SCC LeaveDesk backup, validated by the client with SCC's import rules (names → staff / users).</summary>
public sealed record ImportLeaveDay(string? SccId, long StaffId, DateOnly Date, string? Half, string LeaveType, string? Reason, int? ApproverId, string? ApproverName,
    DateOnly From, DateOnly To, string Status, bool Legacy, string? DecidedByName, string? DecidedAt);

public sealed record ImportLeaveAgenda(string? SccId, DateOnly Date, string Title, string Type, string? Time, string? Description);

public sealed record ImportLeaveDefaults(decimal? Cl, decimal? Sl, decimal? El);

public sealed record ImportLeaveOverride(long StaffId, decimal? Cl, decimal? Sl, decimal? El);

/// <summary>SCC importJSON merge (Owner). <see cref="Skipped"/> = what the client's validation already skipped (added to SCC's count).</summary>
public sealed record ImportLeaveRequest(int Skipped, IReadOnlyList<ImportLeaveDay> Leaves, IReadOnlyList<ImportLeaveAgenda> Agendas, ImportLeaveDefaults? Defaults = null,
    IReadOnlyList<ImportLeaveOverride>? Overrides = null, int? WeekOffMask = null);

public sealed record ImportLeaveResult(int Added, int Agendas, int Skipped, string Notes);

/// <summary>SCC Clear All (Owner): <see cref="Confirm"/> must be DELETE. Leave is cancelled and kept for audit (DEC-119), never deleted.</summary>
public sealed record ClearLeaveRequest(string Confirm);

public sealed record ClearLeaveResult(int Cancelled, int Kept, int Agendas, string Notes);

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

public sealed record LeaveBalanceView(string LeaveType, decimal Entitled, decimal Used, decimal Left, decimal Over);

public sealed record CsvFile(string FileName, string Content);

// ── HR desk board (W4-6): one read for the Staff & payroll and My shifts & pay screens ──
// GET /payroll/board?from&to. Departments and the leave policy for every caller; roster, attendance and leave of every staff member
// for PERM-HR-001 holders, otherwise only the caller's own record (PERM-HR-004, DEC-061). Payroll (rate versions, runs with full
// lines, advances, full and final) only for PERM-HR-006 holders; everyone else gets only their OWN line of each LOCKED run (own pay
// only — never hotel money). Figures are the server's stored run lines (never recomputed). Times are ISO-8601 UTC strings.

public sealed record DepartmentView(string Code, string Name, int? LeaveCap);

/// <summary>SCC leave settings on the board: entitlement defaults, the week-off mask (bit 0 = Sunday), per-staff overrides and blackouts.</summary>
public sealed record LeavePolicyView(decimal Cl, decimal Sl, decimal El, int DefaultCap, int WeekOffMask, IReadOnlyList<LeaveOverrideView> Overrides,
    IReadOnlyList<LeaveBlackoutView> Blackouts);

public sealed record LeaveOverrideView(long StaffId, decimal? Cl, decimal? Sl, decimal? El);

public sealed record LeaveBlackoutView(long BlackoutId, DateOnly From, DateOnly To, string Dept, string Kind, bool Hard, string Label);

public sealed record LeaveAgendaView(long AgendaId, DateOnly Date, string Title, string Time, string Type, string Description, int CreatedBy, string CreatedUtc);

/// <summary>An approver choice: the Owner and Manager users (SCC: Owner + Store Managers).</summary>
public sealed record ApproverView(int UserId, string Name, string Role);

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

/// <summary>One SCC leave record (one <see cref="Date"/>); <see cref="From"/> / <see cref="To"/> = the range it was requested with.</summary>
public sealed record BoardLeaveView(long LeaveId, long StaffId, DateOnly Date, string LeaveType, DateOnly From, DateOnly To, string? Half, decimal Days, string? Reason,
    int? ApproverId, string? ApproverName, string Status, int RequestedBy, string RequestedUtc, int? DecidedBy, string? DecidedUtc, string? Note, int? CancelledBy,
    string? CancelledUtc, bool Legacy, string? SccId, string? DecidedByName, string Version);

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
    IReadOnlyList<HolidayView> Holidays, IReadOnlyList<PayrollRequestView> Requests, DateOnly? CorrectFrom, DateOnly? CorrectTo,
    IReadOnlyList<LeaveAgendaView> Agendas, IReadOnlyList<ApproverView> Approvers);
