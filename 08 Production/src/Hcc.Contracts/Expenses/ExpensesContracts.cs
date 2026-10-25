
namespace Hcc.Contracts.Expenses;

// Wire contracts for the Expenses module (W2-4). Money is integer paise; dates are YYYY-MM-DD business dates; the actor always
// comes from the session. Prototype: closing.ts EXPENSE / EXPENSE_SUBMIT / EXPENSE_APPROVE / EXPENSE_REVERSE.

/// <summary>
/// SubmitExpense K (PERM-EXP-001). <c>Draft</c> true saves a private Draft; otherwise the expense goes to approval.
/// Method: Cash | UPI | Bank transfer | Vendor credit. Category is free text up to 60 characters (suggestions: Supplies, Laundry,
/// Maintenance, Utilities, Petty cash). Receipt is a file-name placeholder only (REQ-EXP-004, no upload).
/// </summary>
public sealed record SubmitExpenseRequest(string Vendor, string Category, long AmountPaise, string Method, bool Draft = false, string? Notes = null, string? Receipt = null);

/// <summary>SubmitExpenseDraft K V (PERM-EXP-001): the submitter sends their own draft for approval (dated the current business day).</summary>
public sealed record SubmitExpenseDraftRequest(long ExpenseId, string ExpectedVersion);

/// <summary>
/// DecideExpense K V (PERM-EXP-002 + PERM-EXP-003): approve (posts) or reject a pending expense. Approving a Cash expense needs
/// <c>TillId</c>: the drawer with an open shift that pays it out (DEC-052). Not your own expense (PERM-SOD-001); approval above
/// the approver's limit answers APPROVAL_REQUIRED.
/// </summary>
public sealed record DecideExpenseRequest(long ExpenseId, string ExpectedVersion, bool Approve, string? Reason = null, int? TillId = null);

/// <summary>ReverseExpense K V (PERM-EXP-003): a reason is required; a Cash expense needs <c>TillId</c> (cash comes back into that drawer).</summary>
public sealed record ReverseExpenseRequest(long ExpenseId, string ExpectedVersion, string Reason, int? TillId = null);

public sealed record ExpenseView(
    long ExpenseId,
    string Ref,
    string Vendor,
    string Category,
    long AmountPaise,
    string Method,
    string BusinessDate,
    string Status,
    int SubmittedBy,
    string? Notes,
    string? Receipt,
    string? Reason,
    int? DecidedBy,
    string? DecidedUtc,
    long? ReversalOf,
    long? ReversedBy,
    string Version);

/// <summary>Result of Submit / SubmitDraft / Decide. <c>DuplicateOf</c> lists same vendor + amount + date expenses (a warning, never a block).</summary>
public sealed record ExpenseResult(ExpenseView Expense, IReadOnlyList<long> DuplicateOf, long? CashMovementId);

/// <summary>Result of ReverseExpense: the original (now Reversed) and the negative reversal record.</summary>
public sealed record ExpenseReversalResult(ExpenseView Original, ExpenseView Reversal, long? CashMovementId);

public sealed record ExpensePage(IReadOnlyList<ExpenseView> Items, int Page, int PageSize, int Total, bool OwnOnly);

/// <summary>GET /expenses/summary?date= — posted figures for a business date (prototype report pack: cash / UPI+bank / vendor credit / total, net of reversals).</summary>
public sealed record ExpenseSummaryView(string BusinessDate, long CashPaise, long NonCashPaise, long VendorCreditPaise, long TotalPaise, long ReversalsPaise, int Count);

public sealed record ExpenseOptionsView(IReadOnlyList<string> Methods, IReadOnlyList<string> Categories);
