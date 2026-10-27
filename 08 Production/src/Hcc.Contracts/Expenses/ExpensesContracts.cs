
namespace Hcc.Contracts.Expenses;

// Wire contracts for the Expenses module (W2-4). Money is integer paise; dates are YYYY-MM-DD business dates; the actor always
// comes from the session. Prototype: closing.ts EXPENSE / EXPENSE_SUBMIT / EXPENSE_APPROVE / EXPENSE_REVERSE.

/// <summary>
/// SubmitExpense K (PERM-EXP-001). <c>Draft</c> true saves a private Draft; otherwise the expense goes to approval. Method: an active funding
/// source of the Payment methods master usable for expenses. Category: an active row of the Expense categories master (its "needs a bill"
/// flag applies). DEC-120 (SCC features): <c>Description</c> is required once the expense goes for approval; <c>Vendor</c> is optional (a new
/// name joins the Expense vendors list, a retired one is refused); <c>ExpenseDate</c> (default the business date) is never after the
/// business date and never a closed day (the Owner-reopened day is allowed, not for drawer cash); GST on the bill (rate in basis points,
/// amount in paise; GST above the amount needs <c>ConfirmGst</c>); a bill photo (JPEG / PNG, at most 4 MB); SCC's duplicate key (date +
/// amount + description) needs <c>ConfirmDuplicate</c>; a date in a tax-locked month needs the Owner and <c>OverrideReason</c>. DEC-135: <c>Itc</c>
/// = "allowed" | "disallowed" (default) for the bill's GST — allowed GST is booked to the GST input-credit ledger, never above the amount.
/// </summary>
public sealed record SubmitExpenseRequest(string? Vendor, string Category, long AmountPaise, string Method, bool Draft = false, string? Notes = null, string? Receipt = null,
    string? Description = null, string? ExpenseDate = null, int? GstRateBps = null, long? GstPaise = null, string? BillPhotoBase64 = null, string? BillPhotoContentType = null,
    bool ConfirmDuplicate = false, bool ConfirmGst = false, string? OverrideReason = null, string? Itc = null);

/// <summary>SubmitExpenseDraft K V (PERM-EXP-001): the submitter sends their own draft for approval (its date while open, else the business day).</summary>
public sealed record SubmitExpenseDraftRequest(long ExpenseId, string ExpectedVersion, string? OverrideReason = null);

/// <summary>
/// DecideExpense K V (PERM-EXP-002 + PERM-EXP-003): approve (posts) or reject a pending expense. Approving a Cash expense needs
/// <c>TillId</c>: the drawer with an open shift that pays it out (DEC-052). Not your own expense, nor one you changed (PERM-SOD-001); approval
/// above the approver's limit (the category's override, else the role limit) answers APPROVAL_REQUIRED.
/// </summary>
public sealed record DecideExpenseRequest(long ExpenseId, string ExpectedVersion, bool Approve, string? Reason = null, int? TillId = null, string? OverrideReason = null);

/// <summary>ReverseExpense K V (PERM-EXP-003): a reason is required; a Cash expense needs <c>TillId</c> (cash comes back into that drawer).</summary>
public sealed record ReverseExpenseRequest(long ExpenseId, string ExpectedVersion, string Reason, int? TillId = null, string? OverrideReason = null);

/// <summary>
/// EditExpense K V (DEC-120, SCC editEntry): amount, category, funding source, description, GST, bill number, bill photo of a Draft (its maker)
/// or a Pending expense (its maker, or PERM-EXP-002); the reason is required and every change is logged (old → new). An approved expense
/// changes only by a reversal and a new entry. Whoever changes someone else's expense may not approve it. DEC-135: <c>Itc</c> (null = keep the
/// expense's flag) — logged when "allowed" is set or cleared.
/// </summary>
public sealed record EditExpenseRequest(long ExpenseId, string ExpectedVersion, long AmountPaise, string Category, string Method, string? Description, string Reason,
    int? GstRateBps = null, long? GstPaise = null, string? Receipt = null, string? BillPhotoBase64 = null, string? BillPhotoContentType = null, bool ConfirmGst = false,
    string? Itc = null);

/// <summary>VoidExpense K V (DEC-120, SCC voidEntry): one of SCC's six reasons + an optional note; Draft / Pending only; PERM-EXP-003 (never the Cashier, DEC-063 G5) or the maker of their own draft.</summary>
public sealed record VoidExpenseRequest(long ExpenseId, string ExpectedVersion, string Reason, string? Note = null);

/// <summary>SetExpenseBudget K (DEC-129: the Owner, PERM-MST-002; DEC-120 / SCC setBudget): the cap of a category for the business month; 0 = no budget.</summary>
public sealed record SetExpenseBudgetRequest(string Category, long AmountPaise);

/// <summary>CopyExpenseBudgets K (DEC-129: the Owner, PERM-MST-002; SCC copyBudgets): last month's budgets into the business month for heads without one.</summary>
public sealed record CopyExpenseBudgetsRequest(string? Month = null);

/// <summary>SaveRecurringExpense K (DEC-129: the Owner, PERM-MST-002; SCC addRecTpl / editRecTpl): new when <c>TemplateId</c> is null; a same name needs <c>ConfirmDuplicate</c>.</summary>
public sealed record SaveRecurringExpenseRequest(int? TemplateId, string Name, long AmountPaise, int DueDay, string Category, string Method, string? Vendor = null, bool ConfirmDuplicate = false);

/// <summary>ToggleRecurringExpense / DeleteRecurringExpense K (DEC-129: the Owner, PERM-MST-002).</summary>
public sealed record RecurringExpenseRequest(int TemplateId);

/// <summary>PostRecurringExpense K (PERM-EXP-001, SCC postRecurring): raises a Draft for this month (amount editable); a second post needs <c>Again</c>.</summary>
public sealed record PostRecurringExpenseRequest(int TemplateId, long AmountPaise, bool Again = false, string? OverrideReason = null);

/// <summary>RequestPettyFloat K (PERM-EXP-001, SCC setFloat): a top-up of the petty-cash float, approved by a second person.</summary>
public sealed record RequestPettyFloatRequest(long AmountPaise, string? Note = null);

/// <summary>DecidePettyFloat K (PERM-EXP-002 with its limit; never your own).</summary>
public sealed record DecidePettyFloatRequest(long EntryId, bool Approve, string? Reason = null);

/// <summary>RecordPettyDisbursement K (PERM-EXP-001, SCC pettyOut): a drawer-cash expense "Petty: &lt;for&gt;" for approval.</summary>
public sealed record RecordPettyDisbursementRequest(long AmountPaise, string Category, string? For = null, string? Receipt = null, string? OverrideReason = null);

/// <summary>AddUdhaar K (PERM-EXP-001, SCC addReceivable): a credit sale on trust; never posts to cash or income.</summary>
public sealed record AddUdhaarRequest(string Name, long AmountPaise, string? Mobile = null, string? DueDate = null, string? Note = null);

/// <summary>
/// PayUdhaar K (PERM-EXP-001, SCC payReceivable): more than outstanding + ₹0.50 is capped to the outstanding. DEC-130: a desk receipt — a
/// front-desk payment method; cash goes into the drawer of <c>TillId</c> (its open shift on the business date, PERM-PAY-001 on that shift, an
/// open cash-book day), other modes need their <c>Reference</c> like other receipts. Booked as Dr tender · Cr Udhaar receivables (UdhaarPaymentRecorded).
/// </summary>
public sealed record PayUdhaarRequest(long ReceivableId, long AmountPaise, string Mode, string? Reference = null, int? TillId = null);

/// <summary>VoidUdhaar K (PERM-EXP-003, SCC voidReceivable): write off; kept for audit.</summary>
public sealed record VoidUdhaarRequest(long ReceivableId, string? Reason = null);

/// <summary>LockExpenseMonth K (Owner, PERM-MST-002; SCC genTaxFeed): generates the month's expense feed and locks the month (regenerate refreshes it).</summary>
public sealed record LockExpenseMonthRequest(string Month);

/// <summary>UnlockExpenseMonth K (DEC-131: the Owner, PERM-MST-002): unlocks a locked month with a reason (3–200 characters, audited); the feed turns
/// stale and the month is locked again by LockExpenseMonth.</summary>
public sealed record UnlockExpenseMonthRequest(string Month, string Reason);

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
    string Version,
    string? Description = null,
    int? GstRateBps = null,
    long? GstPaise = null,
    string? Source = null,
    string? SourceRef = null,
    string? VoidReason = null,
    int? VoidedBy = null,
    string? VoidedUtc = null,
    bool HasBillPhoto = false,
    string? CreatedUtc = null,
    IReadOnlyList<ExpenseEditView>? Edits = null,
    string? Itc = null);

/// <summary>One logged change (SCC editLog): who, when, why, and the changed fields old → new (amounts in paise, rates in basis points).</summary>
public sealed record ExpenseEditView(long EditId, int By, string AtUtc, string Reason, IReadOnlyDictionary<string, System.Text.Json.JsonElement> Old,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> New);

/// <summary>Result of Submit / SubmitDraft / Decide / Edit / Void. <c>DuplicateOf</c> lists same date + amount + description expenses;
/// <c>BudgetAlert</c> = SCC's budget alert text when the category reaches 90 % of its monthly budget.</summary>
public sealed record ExpenseResult(ExpenseView Expense, IReadOnlyList<long> DuplicateOf, long? CashMovementId, string? BudgetAlert = null);

/// <summary>Result of ReverseExpense: the original (now Reversed) and the negative reversal record.</summary>
public sealed record ExpenseReversalResult(ExpenseView Original, ExpenseView Reversal, long? CashMovementId);

public sealed record ExpensePage(IReadOnlyList<ExpenseView> Items, int Page, int PageSize, int Total, bool OwnOnly);

/// <summary>GET /expenses/summary?date= — posted figures for a business date (prototype report pack: cash / UPI+bank / vendor credit / total, net of reversals).</summary>
public sealed record ExpenseSummaryView(string BusinessDate, long CashPaise, long NonCashPaise, long VendorCreditPaise, long TotalPaise, long ReversalsPaise, int Count);

public sealed record ExpenseOptionsView(IReadOnlyList<string> Methods, IReadOnlyList<string> Categories);

// ── DEC-120 SCC expense registers ──

/// <summary>GET /expenses/registers: budgets, recurring templates, petty-cash float, Udhaar register and month locks of the property.</summary>
public sealed record ExpenseRegistersView(string BusinessDate, IReadOnlyList<ExpenseBudgetView> Budgets, IReadOnlyList<RecurringExpenseView> Recurring,
    IReadOnlyList<PettyFloatEntryView> Petty, IReadOnlyList<UdhaarView> Udhaar, IReadOnlyList<ExpenseMonthLockView> Months);

public sealed record ExpenseBudgetView(string Month, string Category, long AmountPaise);

public sealed record RecurringExpenseView(int TemplateId, string Name, long AmountPaise, int DueDay, string Category, string Method, string Vendor, bool Active,
    int CreatedBy, string CreatedUtc);

public sealed record PettyFloatEntryView(long EntryId, long AmountPaise, string BusinessDate, string Status, string? Note, int RecordedBy, string RecordedUtc,
    int? DecidedBy, string? DecidedUtc, string? Reason);

/// <summary>DEC-130: <c>Desk</c> = taken as a desk receipt (cash into the drawer of <c>TillId</c>, booked in Accounting); false for payments recorded before.</summary>
public sealed record UdhaarSettlementView(long SettlementId, long AmountPaise, string Date, string Mode, int RecordedBy, string RecordedUtc, bool Desk = false,
    string? Reference = null, int? TillId = null);

public sealed record UdhaarView(long ReceivableId, string Name, string? Mobile, long AmountPaise, string CreditDate, string? DueDate, string? Note, int CreatedBy,
    string CreatedUtc, bool Void, string? VoidReason, int? VoidedBy, string? VoidedDate, IReadOnlyList<UdhaarSettlementView> Settlements);

/// <summary>
/// A locked month (SCC tax feed): its figures when generated; <c>Stale</c> = changed since (stored flag or a later posting). DEC-135
/// <c>GstItcPaise</c> = the ITC-allowed part of the GST paid; DEC-131 <c>Unlocked</c> by the Owner (with the reason) and every unlock.
/// </summary>
public sealed record ExpenseMonthLockView(string Month, long ExpensePaise, int Count, long GstPaidPaise, IReadOnlyDictionary<string, long> ByCategory, int GeneratedBy,
    string GeneratedUtc, bool Stale, string? StaleUtc, long? GstItcPaise = null, bool Unlocked = false, int? UnlockedBy = null, string? UnlockedUtc = null,
    string? UnlockReason = null, IReadOnlyList<ExpenseMonthUnlockView>? Unlocks = null);

/// <summary>DEC-131: one Owner unlock of a month.</summary>
public sealed record ExpenseMonthUnlockView(int By, string AtUtc, string Reason);

/// <summary>Result of the register commands: what was done (audit text) and the record id when one was made.</summary>
public sealed record ExpenseRegisterResult(string Message, long? Id = null, string? BudgetAlert = null);

/// <summary>GET /expenses/{id}/bill-photo (the photo travels on its own, never in lists).</summary>
public sealed record ExpenseBillPhotoView(long ExpenseId, string ContentType, string ImageBase64);

/// <summary>GET /expenses/export/ledger and /expenses/export/month?month= — SCC's CSV files (columns + HCC Ref, Status).</summary>
public sealed record ExpenseExportView(string FileName, string Csv, int Rows);
