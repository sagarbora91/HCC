namespace Hcc.Contracts.Accounting;

/// <summary>
/// Outbox events the accounting consumer (<c>accounting-vouchers</c>) turns into voucher sources, and the one it publishes.
/// Billing/Payments events exist today (P4). The others are the contracts W2 agents publish (payload records below; camelCase
/// JSON via the command runner's canonical serializer; dates <c>yyyy-MM-dd</c>; money paise). One source per operational record:
/// a re-sent event for the same record is ignored (unique source key per property).
/// </summary>
public static class AccountingEvents
{
    // P4 billing / payments (existing payloads).
    public const string CounterBillIssued = "CounterBillIssued";     // LF A7: Hcc.Application.Counter.CounterBillPostedEvent
    public const string CounterBillCredited = "CounterBillCredited"; // LF A7: the void's credit note (same payload + creditNoteNo)
    public const string ChargePosted = "ChargePosted";          // {folioId, chargeId, kind room|service|discount|correction, serviceDate, amountPaise, taxPaise}
    public const string ReceiptRecorded = "ReceiptRecorded";    // {folioId, receiptId, kind, tender, amountPaise, businessDate}
    public const string ApprovalDecided = "ApprovalDecided";    // {approvalId, action, status, folioId}: correction + Executed → reversal line (not published as ChargePosted today)

    // Requested from P4 Payments (consumed when present; until then accepted differences are swept from pay.shift).
    public const string ShiftDifferenceDecided = "ShiftDifferenceDecided";

    // W2-6 POS, W2-4 Expenses, W2-2 Payroll.
    public const string PosBillSettled = "PosBillSettled";   // POS publishes for every settled bill; mode = folio is skipped (booked from its ChargePosted)
    public const string PosBillVoided = "PosBillVoided";     // POS: a settled outlet bill voided the same day (positive amounts, booked as a negative Sales voucher)
    public const string ExpensePosted = "ExpensePosted";
    public const string ExpenseReversed = "ExpenseReversed";
    public const string PayrollPosted = "PayrollPosted";
    public const string PayrollPaid = "PayrollPaid";
    public const string PayRunUnlocked = "PayRunUnlocked";   // W3-b: reversal of that lock's PayrollPosted voucher

    // ── DEC-074 (wave B3) · books before go-live: group master folio, staff advances, full and final ──
    public const string GroupReceiptRecorded = "GroupReceiptRecorded";   // Groups: master-folio payment / deposit / refund (UPI, card, bank transfer; never cash)
    public const string GroupAttritionPosted = "GroupAttritionPosted";   // Groups: approved attrition lines on the master folio (net + stored per-line CGST / SGST)
    public const string GroupSettled = "GroupSettled";                   // Groups: master folio settled at balance 0 (deposits still held are applied)
    public const string AdvanceGiven = "AdvanceGiven";                   // Payroll: a salary advance paid out
    public const string FnfSettled = "FnfSettled";                       // Payroll: full and final confirmed (salary journal + net pay paid)
    // ── Wave B1 (DEC-073, A03b-01): cash leaving the drawer. A till transfer is cash to cash in the one 1000 ledger: no event consumed. ──
    public const string CashBanked = "CashBanked";                     // {transferId, tillId, shiftId, businessDate, amountPaise, bank, slipNo}: Dr bank / Cr cash
    public const string CashBankingRejected = "CashBankingRejected";   // {transferId, tillId, shiftId, businessDate (rejection), amountPaise, bank, slipNo, reason}: the reverse
    public const string OwnerDrawingRecorded = "OwnerDrawingRecorded"; // {transferId, tillId, shiftId, businessDate, amountPaise, reason}: Dr Owner's drawings / Cr cash
    // ── end Wave B1 ──
    // DEC-076 company credit: money received from a company against its company-credit receivable (Dr tender · Cr company receivables).
    public const string CompanyPaymentRecorded = "CompanyPaymentRecorded";

    // DEC-130 (SCC decisions wave): an Udhaar (customer credit) payment taken as a desk receipt (Dr tender · Cr Udhaar receivables).
    public const string UdhaarPaymentRecorded = "UdhaarPaymentRecorded";

    // ACC-1 (contract §4, rulebook A17, DEC-144): an Udhaar credit raised is a sale booked the same day (Dr 1120/<customer> · Cr revenue · Cr CGST · Cr SGST).
    public const string UdhaarCreditRaised = "UdhaarCreditRaised";

    // ACC-2 (acc2-manual, contract §2): a manual voucher approved (booked as manual:<id>) / voided (books nothing: an unlocked day's source stops
    // counting; a locked day's reversal comes as its own ManualVoucherApproved).
    public const string ManualVoucherApproved = "ManualVoucherApproved";
    public const string ManualVoucherVoided = "ManualVoucherVoided";
    // ACC-2 flows (contract §4, §8): an OTA payout from the statement (A13) and a mistaken Udhaar payment reversed (A17b). Deposit applied (A6a) and
    // the OTA credit at check-out are ReceiptRecorded events of kind deposit-apply / ota-credit (SourceRules.DepositApplied / OtaCredit).
    public const string OtaPayoutRecorded = "OtaPayoutRecorded";
    public const string UdhaarPaymentReversed = "UdhaarPaymentReversed";

    /// <summary>Published by accounting when a period is locked / unlocked.</summary>
    public const string AccountingPeriodLocked = "AccountingPeriodLocked";

    public const string AccountingPeriodUnlocked = "AccountingPeriodUnlocked";

    public static readonly IReadOnlyList<string> Consumed =
        [ChargePosted, ReceiptRecorded, ApprovalDecided, ShiftDifferenceDecided, PosBillSettled, PosBillVoided, ExpensePosted, ExpenseReversed, PayrollPosted, PayrollPaid, PayRunUnlocked,
         GroupReceiptRecorded, GroupAttritionPosted, GroupSettled, AdvanceGiven, FnfSettled,   // DEC-074 (wave B3)
         CashBanked, CashBankingRejected, OwnerDrawingRecorded,                                  // Wave B1 (DEC-073)
         CompanyPaymentRecorded,                                                                 // DEC-076 (wave B5)
         UdhaarPaymentRecorded,                                                                  // DEC-130 (SCC decisions wave)
         UdhaarCreditRaised,                                                                     // ACC-1 (acc1-parties, DEC-144)
         ManualVoucherApproved, ManualVoucherVoided,                                             // ACC-2 (acc2-manual, contract §2)
         OtaPayoutRecorded, UdhaarPaymentReversed,                                               // ACC-2 flows
         CounterBillIssued, CounterBillCredited,                                                 // LF A7
         .. PurchaseEvents.All];                                                                 // ACC-2 acc2-purchase (contract §3)
}

/// <summary>Wave B1 (DEC-073): a bank deposit recorded (<see cref="AccountingEvents.CashBanked"/>) or rejected (<see cref="AccountingEvents.CashBankingRejected"/>).</summary>
public sealed record CashBankingEvent(long TransferId, int TillId, long ShiftId, DateOnly BusinessDate, long AmountPaise, string Bank, string SlipNo, string? Reason = null);

/// <summary>Wave B1 (DEC-073): cash the Owner took from a drawer.</summary>
public sealed record OwnerDrawingEvent(long TransferId, int TillId, long ShiftId, DateOnly BusinessDate, long AmountPaise, string Reason);

/// <summary>
/// DEC-076: a payment received from a company account against its company-credit receivable (Groups <c>RecordCompanyPayment</c>; tender UPI | Card |
/// Bank transfer). Booked as a Receipt voucher: Dr the tender · Cr company receivables (the <c>receipt:company-credit</c> ledger).
/// </summary>
/// ACC-2 tds (contract §4 A9): <c>TdsPaise</c> = TDS the company deducted (0 / absent in older payloads) — booked Dr 1360 TDS receivable.
public sealed record CompanyPaymentRecordedEvent(long CompanyId, long PaymentId, DateOnly BusinessDate, string Tender, long AmountPaise, string Reference, string Company,
    long TdsPaise = 0);

/// <summary>
/// DEC-130: an Udhaar (customer credit) payment taken as a desk receipt (Expenses <c>PayUdhaar</c>; any front-desk tender, cash into the drawer).
/// Booked as a Receipt voucher: Dr the tender · Cr Udhaar receivables (<c>receipt:udhaar</c>) — it settles the credit, never income.
/// </summary>
public sealed record UdhaarPaymentRecordedEvent(long ReceivableId, long SettlementId, DateOnly BusinessDate, string Tender, long AmountPaise, string Customer, string? Reference = null,
    long? CashMovementId = null);

/// <summary>
/// ACC-1 (contract §4, rulebook A17, DEC-144): an Udhaar credit raised (Expenses <c>AddUdhaar</c>). <see cref="Kind"/> = restaurant | room | other
/// (null on a request without it = other), <see cref="GstBps"/> = the GST rate of what was sold (0 = no GST lines). Booked as a Sales voucher:
/// Dr Udhaar receivables (party <c>UD-&lt;mobile&gt;</c> or <c>UD-&lt;slug of name&gt;</c>) gross · Cr F&amp;B / room / other revenue · Cr CGST · Cr SGST.
/// </summary>
public sealed record UdhaarCreditRaisedEvent(long ReceivableId, DateOnly BusinessDate, string Customer, string? Mobile, long AmountPaise, string? Kind = null, int GstBps = 0);

/// <summary>P4 Payments (requested): a counted cash difference was accepted or rejected. Only <c>Approved</c> posts a voucher.</summary>
public sealed record ShiftDifferenceDecidedEvent(long ShiftId, long ApprovalId, string Status, DateOnly BusinessDate, long ExpectedPaise, long CountedPaise);

/// <summary>
/// W2-6 POS: an outlet bill settled AT THE OUTLET (cash / UPI / card; bills charged to a room go through the folio as a
/// ChargePosted service line instead). <c>Kind</c> = sale | void (a void carries negative amounts). CGST/SGST are the stored
/// per-bill amounts. Total = net + cgst + sgst.
/// </summary>
public sealed record PosBillSettledEvent(long BillId, string Kind, DateOnly BusinessDate, string Outlet, string Tender, long TotalPaise, long NetPaise,
    long CgstPaise, long SgstPaise, string Reference, string? Mode = null);

/// <summary>
/// W2-4 Expenses: a posted expense (or its reversal record, negative amount, <c>ReversalOf</c> = original id). Funding = Cash | UPI | Card | Bank transfer | Vendor credit.
/// DEC-135: <c>GstPaise</c> (the bill's GST, negative on a reversal record) and <c>Itc</c> = "allowed" books that GST to the GST input-credit
/// ledger; absent (events published before) or "disallowed" = the whole amount to the category.
/// </summary>
public sealed record ExpensePostedEvent(long ExpenseId, DateOnly BusinessDate, string Category, string Funding, long AmountPaise, string Vendor, long? ReversalOf,
    long? GstPaise = null, string? Itc = null,
    // ACC-2 (acc2-purchase, contract §3.1): IGST bill, TDS held back (signed like the amount), the funding is a credit method (Purchase voucher).
    string? GstKind = null, long? TdsPaise = null, bool? Credit = null, string? TdsSection = null);

/// <summary>
/// W2-2 Payroll: a locked pay run. Gross = net + employee PF + employee ESIC + PT + advance recovery + other deductions.
/// <see cref="LockSeq"/> = which lock of the run (1 = first; a re-lock after an unlock is 2, …) — one voucher per lock.
/// ACC-2 (coordinator ruling 2): <see cref="BusinessDate"/> = the pay period's last day when the lock is later (the month's trial balance holds
/// at month end); <see cref="LockedOn"/> = the business date of the lock (narration). Null on older events (then the lock's date = BusinessDate).
/// ACC-2 (contract §8): <see cref="AdvanceRecoveries"/> = the advance recovered per staff member (the voucher credits 1200/STF-&lt;id&gt; each);
/// null on events published before ACC-2 (the recovery then stays on the 1200 control with a <c>no-party</c> warning).
/// </summary>
public sealed record PayrollPostedEvent(long PayRunId, string Period, DateOnly BusinessDate, long GrossPaise, long EmployerPfPaise, long EmployerEsicPaise,
    long EmployeePfPaise, long EmployeeEsicPaise, long ProfessionalTaxPaise, long AdvanceRecoveryPaise, long OtherDeductionsPaise, long NetPayablePaise, int LockSeq = 1,
    IReadOnlyList<AdvanceRecoveryLine>? AdvanceRecoveries = null, DateOnly? LockedOn = null);

/// <summary>ACC-2 (contract §8): one staff member's advance recovered in a pay run (paise).</summary>
public sealed record AdvanceRecoveryLine(long StaffId, long Paise); // wire: {staffId, paise} (coordinator ruling 3)

/// <summary>W2-2 Payroll: net pay paid out (first bank file of a lock). Funding as for expenses.</summary>
/// ACC-2 (coordinator ruling 2): <see cref="BusinessDate"/> = the pay period's last day when the file is generated later; <see cref="PaidOn"/> = the day it was.
public sealed record PayrollPaidEvent(long PayRunId, DateOnly BusinessDate, string Funding, long AmountPaise, string Reference, int LockSeq = 1, DateOnly? PaidOn = null);

/// <summary>
/// W2-2 Payroll: lock <see cref="LockSeq"/> of the run was unlocked on <see cref="BusinessDate"/>. Accounting books the exact reversal of that
/// lock's PayrollPosted voucher (once). The PayrollPaid voucher of a bank file already issued (<see cref="BankFilesIssued"/> &gt; 0) is kept —
/// the money may have left the bank — and the reversal's narration says so.
/// </summary>
public sealed record PayRunUnlockedEvent(long RunId, string Month, int LockSeq, int BankFilesIssued, DateOnly? BusinessDate = null);

// ── DEC-074 (wave B3) · payloads of the group, advance and full-and-final events (fields added after W3 are optional, so events published
// before DEC-074 still map: the consumer reads a missing figure from the source tables or uses the property's business date) ──

/// <summary>
/// Groups (W3-4): a receipt on a group's master folio. <see cref="Kind"/> = payment | deposit | refund (UPI, Card or Bank transfer — the master
/// folio takes no cash). A refund names the kind of the receipt it refunds (<see cref="RefundOfKind"/>, payment | deposit).
/// </summary>
public sealed record GroupReceiptRecordedEvent(long GroupId, long EntryId, string Kind, string Tender, long AmountPaise, DateOnly BusinessDate, string? RefundOfKind = null);

/// <summary>Groups (W3-4): approved attrition posted on the master folio. CGST / SGST = the stored per-line split, summed; tax = CGST + SGST.</summary>
public sealed record GroupAttritionPostedEvent(long GroupId, long AttritionId, IReadOnlyList<long> EntryIds, long NetPaise, long TaxPaise, long CgstPaise, long SgstPaise,
    DateOnly BusinessDate);

/// <summary>
/// Groups (W3-4): the master folio was settled at balance 0. <see cref="DepositsHeldPaise"/> = deposits − refunds of deposits (the advance the
/// company paid, applied to its receivable at settlement).
/// </summary>
public sealed record GroupSettledEvent(long GroupId, long ChargesPaise, long ReceivedPaise, long? DepositsHeldPaise = null, DateOnly? BusinessDate = null);

/// <summary>Payroll (W2-2): a salary advance paid out on <see cref="Date"/> (the business date). Funding as for expenses; HCC advances are bank transfers.</summary>
public sealed record AdvanceGivenEvent(long AdvanceId, long StaffId, long AmountPaise, long InstallmentPaise, DateOnly Date, string? Funding = null);

/// <summary>
/// Payroll (W2-2): a full-and-final settlement confirmed (and paid, simulated transfer). Earnings = exit-month gross + additions; recoveries =
/// employee PF + ESIC + PT + the advance recovered + deductions; net = earnings − recoveries (negative = recoverable from the leaver).
/// DEC-074 / A04-06: <see cref="AdvanceRecoveredPaise"/> is only the part of the open advance the settlement covers; the rest
/// (<see cref="AdvanceLeftOpenPaise"/>) stays an open advance. <see cref="AdvanceWrittenOffPaise"/> (W2 name) = the advance cleared by this settlement.
/// </summary>
public sealed record FnfSettledEvent(long FnfId, long StaffId, string Month, long EarningsPaise, long RecoveriesPaise, long NetPaise, long AdvanceWrittenOffPaise,
    DateOnly? BusinessDate = null, long? GrossPaise = null, long? AdditionsPaise = null, long? EmployeePfPaise = null, long? EmployeeEsicPaise = null,
    long? ProfessionalTaxPaise = null, long? EmployerPfPaise = null, long? EmployerEsicPaise = null, long? DeductionsPaise = null, long? AdvanceRecoveredPaise = null,
    long? AdvanceLeftOpenPaise = null, string? Funding = null);

// ── API ──

public sealed record LockPeriodRequest(DateOnly BusinessDate);

public sealed record UnlockPeriodRequest(DateOnly BusinessDate, string Reason, string ExpectedVersion);

public sealed record SetLedgerMappingRequest(string MapKey, string AccountCode);

public sealed record ResetLedgerMappingRequest(string MapKey);

public sealed record PeriodLockResult(DateOnly BusinessDate, long LockId, string Status, int VoucherCount, long DebitPaise, long CreditPaise, string Version);

public sealed record LedgerMappingResult(string MapKey, string? Before, string? After, bool IsDefault);

public sealed record VoucherEntryView(string Account, string Ledger, long DebitPaise, long CreditPaise);

public sealed record VoucherView(string Number, DateOnly Date, string Type, string Narration, string SourceKey, string SourceKind, string Sub,
    long DebitPaise, long CreditPaise, bool Balanced, IReadOnlyList<VoucherEntryView> Entries, IReadOnlyList<string> Unmapped);

public sealed record AccountingIssueView(string Id, string Severity, string Kind, string Title, string Detail, string? Voucher, string? Source, string? Key);

public sealed record ReconciliationView(string Id, string Label, long ReportPaise, long LedgerPaise, bool Ok, string Note);

public sealed record LockInfoView(long LockId, DateTime LockedUtc, int LockedBy, string Version);

public sealed record AccountingDayView(DateOnly Date, string Status, bool Provisional, int VoucherCount, long DebitPaise, long CreditPaise, bool Balanced,
    int Errors, int Warnings, LockInfoView? Lock, IReadOnlyList<VoucherView> Vouchers, IReadOnlyList<AccountingIssueView> Exceptions,
    IReadOnlyList<ReconciliationView> Reconciliation);

public sealed record TrialBalanceRowView(string Account, string Name, string Group, long DebitPaise, long CreditPaise, long BalancePaise);

/// <summary>
/// Trial balance for one date (day movement) or up to it (<see cref="Cumulative"/>). N5: a cumulative balance on or after the imported
/// opening balances date (W4-13 Import, <see cref="OpeningAsOf"/>) includes the imported opening trial balance in <see cref="Rows"/> and the
/// totals (<see cref="OpeningIncluded"/>); a ledger mapped to an HCC account adds to that account, an unmapped one keeps its own code,
/// name and group. <see cref="Opening"/> repeats the imported opening rows on every cumulative answer (empty for a day movement), so a
/// client that folds an earlier cumulative balance can add the opening on its own date.
/// </summary>
public sealed record TrialBalanceView(DateOnly Date, bool Cumulative, long DebitPaise, long CreditPaise, bool Balanced, IReadOnlyList<TrialBalanceRowView> Rows,
    DateOnly? OpeningAsOf = null, bool OpeningIncluded = false, IReadOnlyList<TrialBalanceRowView>? Opening = null);

public sealed record MappingRowView(string MapKey, string Group, string Value, string? Account, string? DefaultAccount, bool Changed, int Uses, bool Mapped);

public sealed record AccountView(string Code, string Name, string Group, string? Note);

public sealed record MappingChangeView(long ChangeId, DateTime AtUtc, int Actor, string MapKey, string? Before, string? After);

public sealed record LockEventView(long LockId, DateOnly BusinessDate, string Action, DateTime AtUtc, int Actor, string? Reason);

/// <summary>Simulated Tally XML (never sent anywhere; saving it does not mean it was imported).</summary>
/// <param name="ReexportDates">DEC-077: dates carried again because they changed after an earlier export (their day was reopened); the XML names each.</param>
/// <param name="ProvisionalDates">DEC-077: the reopened day, when carried (it may still change; it stays flagged). A03b-05: also every carried
/// date that is not locked while accounting events are pending (<paramref name="PendingEvents"/> &gt; 0); flagged, so the next export carries it again.</param>
/// <param name="ExportId">The export log row (acc.tally_export) this download was recorded as.</param>
/// <param name="PendingEvents">A03b-05: accounting events of the property not yet in the vouchers (still processing or parked), as for a lock.</param>
/// <param name="LegacyDates">A03b-05: carried dates an export made before vouchers carried a REMOTEID had taken to Tally (the XML says to delete those copies).</param>
public sealed record TallyExportView(DateOnly From, DateOnly To, int Dates, int VoucherCount, long DebitPaise, long CreditPaise, bool Simulated, string FileName, string Xml,
    IReadOnlyList<DateOnly>? ReexportDates = null, IReadOnlyList<DateOnly>? ProvisionalDates = null, long? ExportId = null, int PendingEvents = 0,
    IReadOnlyList<DateOnly>? LegacyDates = null);
