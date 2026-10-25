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
    public const string ChargePosted = "ChargePosted";          // {folioId, chargeId, kind room|service|discount|correction, serviceDate, amountPaise, taxPaise}
    public const string ReceiptRecorded = "ReceiptRecorded";    // {folioId, receiptId, kind, tender, amountPaise, businessDate}
    public const string ApprovalDecided = "ApprovalDecided";    // {approvalId, action, status, folioId}: correction + Executed → reversal line (not published as ChargePosted today)

    // Requested from P4 Payments (consumed when present; until then accepted differences are swept from pay.shift).
    public const string ShiftDifferenceDecided = "ShiftDifferenceDecided";

    // W2-6 POS, W2-4 Expenses, W2-2 Payroll.
    public const string PosBillSettled = "PosBillSettled";
    public const string ExpensePosted = "ExpensePosted";
    public const string ExpenseReversed = "ExpenseReversed";
    public const string PayrollPosted = "PayrollPosted";
    public const string PayrollPaid = "PayrollPaid";

    /// <summary>Published by accounting when a period is locked / unlocked.</summary>
    public const string AccountingPeriodLocked = "AccountingPeriodLocked";

    public const string AccountingPeriodUnlocked = "AccountingPeriodUnlocked";

    public static readonly IReadOnlyList<string> Consumed =
        [ChargePosted, ReceiptRecorded, ApprovalDecided, ShiftDifferenceDecided, PosBillSettled, ExpensePosted, ExpenseReversed, PayrollPosted, PayrollPaid];
}

/// <summary>P4 Payments (requested): a counted cash difference was accepted or rejected. Only <c>Approved</c> posts a voucher.</summary>
public sealed record ShiftDifferenceDecidedEvent(long ShiftId, long ApprovalId, string Status, DateOnly BusinessDate, long ExpectedPaise, long CountedPaise);

/// <summary>
/// W2-6 POS: an outlet bill settled AT THE OUTLET (cash / UPI / card; bills charged to a room go through the folio as a
/// ChargePosted service line instead). <c>Kind</c> = sale | void (a void carries negative amounts). CGST/SGST are the stored
/// per-bill amounts. Total = net + cgst + sgst.
/// </summary>
public sealed record PosBillSettledEvent(long BillId, string Kind, DateOnly BusinessDate, string Outlet, string Tender, long TotalPaise, long NetPaise,
    long CgstPaise, long SgstPaise, string Reference);

/// <summary>W2-4 Expenses: a posted expense (or its reversal record, negative amount, <c>ReversalOf</c> = original id). Funding = Cash | UPI | Card | Bank transfer | Vendor credit.</summary>
public sealed record ExpensePostedEvent(long ExpenseId, DateOnly BusinessDate, string Category, string Funding, long AmountPaise, string Vendor, long? ReversalOf);

/// <summary>W2-2 Payroll: a locked pay run. Gross = net + employee PF + employee ESIC + PT + advance recovery + other deductions.</summary>
public sealed record PayrollPostedEvent(long PayRunId, string Period, DateOnly BusinessDate, long GrossPaise, long EmployerPfPaise, long EmployerEsicPaise,
    long EmployeePfPaise, long EmployeeEsicPaise, long ProfessionalTaxPaise, long AdvanceRecoveryPaise, long OtherDeductionsPaise, long NetPayablePaise);

/// <summary>W2-2 Payroll: net pay paid out (bank file / cash). Funding as for expenses.</summary>
public sealed record PayrollPaidEvent(long PayRunId, DateOnly BusinessDate, string Funding, long AmountPaise, string Reference);

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

public sealed record TrialBalanceView(DateOnly Date, bool Cumulative, long DebitPaise, long CreditPaise, bool Balanced, IReadOnlyList<TrialBalanceRowView> Rows);

public sealed record MappingRowView(string MapKey, string Group, string Value, string? Account, string? DefaultAccount, bool Changed, int Uses, bool Mapped);

public sealed record AccountView(string Code, string Name, string Group, string? Note);

public sealed record MappingChangeView(long ChangeId, DateTime AtUtc, int Actor, string MapKey, string? Before, string? After);

public sealed record LockEventView(long LockId, DateOnly BusinessDate, string Action, DateTime AtUtc, int Actor, string? Reason);

/// <summary>Simulated Tally XML (never sent anywhere; saving it does not mean it was imported).</summary>
public sealed record TallyExportView(DateOnly From, DateOnly To, int Dates, int VoucherCount, long DebitPaise, long CreditPaise, bool Simulated, string FileName, string Xml);
