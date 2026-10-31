using Hcc.Application.Commands;
using Hcc.Domain.Expenses;
using Hcc.Domain.Money;

namespace Hcc.Application.Expenses;

/// <summary>PERM ids (RBAC matrix v1 section 5) used by the Expenses module. The grants are seeded in 0101_access_rbac_seed.</summary>
public static class ExpensePermissions
{
    public const string Submit = "PERM-EXP-001";
    public const string Decide = "PERM-EXP-002";
    public const string PostReverse = "PERM-EXP-003";

    /// <summary>
    /// DEC-120: the SCC month lock and its override are the Owner's (DEC-058 Owner-only money settings): masters.manage-money. SCC decisions
    /// wave: DEC-129 monthly budgets and recurring templates, DEC-131 the month unlock — the same Owner-only permission (no new PERM id).
    /// </summary>
    public const string Owner = "PERM-MST-002";

    /// <summary>DEC-130: an Udhaar payment taken in cash is a desk receipt into the drawer — payment.record on the open shift (PERM-PAY-001,
    /// own shift for Reception / Cashier), as every cash receipt.</summary>
    public const string DeskReceipt = "PERM-PAY-001";
}

/// <summary>
/// Outbox event names = Accounting's contract (<see cref="Contracts.Accounting.AccountingEvents"/>, payload <c>ExpensePostedEvent</c>): one per
/// posted (approved) expense and one per reversal record (negative amount, <c>reversalOf</c> = the original).
/// </summary>
public static class ExpenseEvents
{
    public const string Posted = Contracts.Accounting.AccountingEvents.ExpensePosted;
    public const string Reversed = Contracts.Accounting.AccountingEvents.ExpenseReversed;
    public const string RcmPosted = Contracts.Accounting.PurchaseEvents.ExpenseRcmPosted; // ACC-2 (contract §3.3)
    public const string Returned = Contracts.Accounting.PurchaseEvents.ExpenseReturned;   // ACC-2 (contract §3.5)
}

public sealed record ExpenseRecord(long ExpenseId, int PropertyId, string Vendor, string Category, Paise Amount, string Method, DateOnly BusinessDate,
    string Status, int SubmittedBy, string? Notes, string? Receipt, string? Reason, int? DecidedBy, DateTime? DecidedUtc, int? TillId, long? ReversalOf,
    long? ReversedBy, byte[] Version, string? Description = null, int? GstRateBps = null, long? GstPaise = null, string? Source = null, string? SourceRef = null,
    string? VoidReason = null, int? VoidedBy = null, DateTime? VoidedUtc = null, DateTime? CreatedUtc = null, bool HasBillPhoto = false, bool ItcAllowed = false,
    // ACC-2 (acc2-purchase, contract §3; migration 4230): IGST bill, reverse charge + rate, capital purchase + register no, TDS held back, return of a bill.
    string? GstKind = null, bool Rcm = false, int? RcmBps = null, bool Capital = false, string? AssetNo = null, long? TdsPaise = null, string? TdsSection = null,
    long? ReturnOf = null,
    // ACC-2 integration (flows item 8; migrations 4230 / 4254): the B10 cash guard tripped on this cash expense ("deduction lost").
    bool CashDeductionLost = false)
{
    public bool IsReversal => ReversalOf is not null;

    public ExpenseFact Fact => new(ExpenseId, Vendor, Amount, BusinessDate, Method, Status, IsReversal, Description);
}

public sealed record NewExpense(int PropertyId, string Vendor, string Category, Paise Amount, string Method, DateOnly BusinessDate, string Status,
    int SubmittedBy, string? Notes, string? Receipt, string? Reason, int? DecidedBy, int? TillId, long? ReversalOf, DateTime NowUtc,
    string? Description = null, int? GstRateBps = null, long? GstPaise = null, string? Source = null, string? SourceRef = null, bool ItcAllowed = false,
    string? GstKind = null, bool Rcm = false, int? RcmBps = null, bool Capital = false, long? TdsPaise = null, string? TdsSection = null, long? ReturnOf = null); // ACC-2

public sealed record ExpenseFilter(string? Status, DateOnly? From, DateOnly? To, int? SubmittedBy);

/// <summary>One logged change (ops.expense_edit).</summary>
public sealed record ExpenseEditRecord(long EditId, long ExpenseId, int By, DateTime AtUtc, string Reason, string OldJson, string NewJson);

/// <summary>Persistence port of the Expenses commands and queries (SQL adapter in Infrastructure/Expenses).</summary>
public interface IExpenseStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<ExpenseRecord> InsertAsync(ICommandTransaction tx, NewExpense e, CancellationToken ct = default);

    /// <summary>Row lock (UPDLOCK) inside the command transaction; null when unknown at the property.</summary>
    Task<ExpenseRecord?> LockAsync(ICommandTransaction tx, int propertyId, long expenseId, CancellationToken ct = default);

    /// <summary>
    /// Writes the mutable columns (status, date, amount, category, method, description, GST, receipt, reason, decision, till, reversed_by, void)
    /// when the version still matches; STALE_VERSION otherwise. Returns the new row.
    /// </summary>
    Task<ExpenseRecord> UpdateAsync(ICommandTransaction tx, ExpenseRecord updated, byte[] expectedVersion, DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseFact>> FactsOnAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default);

    // DEC-120 · SCC features.

    /// <summary>Stores (or replaces) the bill photo of an expense.</summary>
    Task PutBillPhotoAsync(ICommandTransaction tx, long expenseId, string contentType, byte[] image, int by, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>The bill photo (null when none) — at the property only.</summary>
    Task<(string ContentType, byte[] Image)?> BillPhotoAsync(int propertyId, long expenseId, CancellationToken ct = default);

    Task InsertEditAsync(ICommandTransaction tx, long expenseId, int by, DateTime nowUtc, string reason, string oldJson, string newJson, CancellationToken ct = default);

    /// <summary>Everyone who changed the expense (the maker-checker rule refuses their approval when they are not its submitter).</summary>
    Task<IReadOnlyList<int>> EditorsAsync(ICommandTransaction tx, long expenseId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<long, IReadOnlyList<ExpenseEditRecord>>> EditsAsync(int propertyId, IReadOnlyCollection<long> expenseIds, CancellationToken ct = default);

    /// <summary>Every record of the property (optionally only dated within [from, to]) with the submitter's name — ledger CSV, month figures, budgets.</summary>
    Task<IReadOnlyList<LedgerRow>> LedgerAsync(ICommandTransaction? tx, int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>Records posted from a recurring template this month that still stand (not void, rejected or reversed; no reversal records): count and Σ.</summary>
    Task<(int Count, long Sum)> SourceRefAsync(ICommandTransaction tx, int propertyId, string sourceRef, CancellationToken ct = default);

    Task<string> HotelNameAsync(int propertyId, CancellationToken ct = default);

    /// <summary>The property's business date, read on its own connection (registers view, CSV file name).</summary>
    Task<DateOnly> ReadBusinessDateAsync(int propertyId, CancellationToken ct = default);

    // Reads (own connection).
    Task<ExpenseRecord?> GetAsync(int propertyId, long expenseId, CancellationToken ct = default);

    Task<(IReadOnlyList<ExpenseRecord> Items, int Total)> ListAsync(int propertyId, ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseFact>> FactsOnAsync(int propertyId, DateOnly date, CancellationToken ct = default);
}

/// <summary>The Expense categories / vendors masters as the expense commands read them (ops.expense_category / ops.expense_vendor, MA-3).</summary>
public interface IExpenseMasterSource
{
    /// <summary>Categories (retired included); an empty list = not seeded yet (the commands then use the seed list).</summary>
    Task<IReadOnlyList<ExpenseCategoryRow>> CategoriesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseVendorRow>> VendorsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);

    /// <summary>SCC vendor auto-add: seeds the lists first (idempotent), then adds an active row with a history entry unless the name exists.</summary>
    Task AddVendorAsync(ICommandTransaction tx, int propertyId, string name, int by, DateTime nowUtc, CancellationToken ct = default);
}

public sealed record RecurringRecord(int TemplateId, int PropertyId, string Name, long Amount, int DueDay, string Category, string Method, string Vendor, bool Active,
    int CreatedBy, DateTime CreatedUtc);

public sealed record PettyFloatRecord(long EntryId, int PropertyId, long Amount, DateOnly BusinessDate, string Status, string? Note, int RecordedBy, DateTime RecordedUtc,
    int? DecidedBy, DateTime? DecidedUtc, string? Reason);

/// <summary><c>Desk</c> (DEC-130): taken as a desk receipt (cash into the drawer of <c>TillId</c>, booked in Accounting); false = recorded before DEC-130.</summary>
public sealed record UdhaarSettlementRecord(long SettlementId, long ReceivableId, long Amount, DateOnly Date, string Mode, int RecordedBy, DateTime RecordedUtc,
    bool Desk = false, string? Reference = null, int? TillId = null);

/// <summary>ACC-1 (contract §4): <c>Kind</c> restaurant | room | other (null = raised before ACC-1: other), <c>GstBps</c> its GST rate (migration 4215).</summary>
public sealed record UdhaarRecord(long ReceivableId, int PropertyId, string Name, string? Mobile, long Amount, DateOnly CreditDate, DateOnly? DueDate, string? Note,
    int CreatedBy, DateTime CreatedUtc, bool Void, string? VoidReason, int? VoidedBy, DateOnly? VoidedDate, IReadOnlyList<UdhaarSettlementRecord> Settlements,
    string? Kind = null, int GstBps = 0)
{
    public long Paid => Settlements.Sum(s => s.Amount);

    public long Outstanding => ExpenseScc.UdhaarOut(Amount, Paid);
}

/// <summary>A month's feed; DEC-135 <c>GstItc</c> = the ITC-allowed part of the GST paid; DEC-131 the Owner's unlock (null = locked).</summary>
public sealed record MonthLockRecord(string Month, long Expense, int Count, long GstPaid, string ByCategoryJson, int GeneratedBy, DateTime GeneratedUtc, bool Stale, DateTime? StaleUtc,
    long? GstItc = null, int? UnlockedBy = null, DateTime? UnlockedUtc = null, string? UnlockReason = null)
{
    /// <summary>DEC-131: the Owner unlocked the month — writes into it need no override until it is locked again.</summary>
    public bool Unlocked => UnlockedUtc is not null;
}

/// <summary>DEC-131: one Owner unlock of a month (ops.expense_month_unlock).</summary>
public sealed record MonthUnlockRecord(string Month, int By, DateTime AtUtc, string Reason);

/// <summary>DEC-120 SCC registers: budgets, recurring templates, petty-cash float, Udhaar, month locks (SQL adapter SqlExpenseRegisterStore).</summary>
public interface IExpenseRegisterStore
{
    Task<IReadOnlyDictionary<string, long>> BudgetsAsync(ICommandTransaction? tx, int propertyId, string month, CancellationToken ct = default);

    Task<IReadOnlyList<(string Month, string Category, long Amount)>> AllBudgetsAsync(int propertyId, CancellationToken ct = default);

    Task SetBudgetAsync(ICommandTransaction tx, int propertyId, string month, string category, long amount, int by, DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<RecurringRecord>> RecurringAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);

    Task<RecurringRecord?> LockRecurringAsync(ICommandTransaction tx, int propertyId, int templateId, CancellationToken ct = default);

    Task<int> InsertRecurringAsync(ICommandTransaction tx, RecurringRecord t, CancellationToken ct = default);

    Task UpdateRecurringAsync(ICommandTransaction tx, RecurringRecord t, CancellationToken ct = default);

    Task DeleteRecurringAsync(ICommandTransaction tx, int templateId, int by, DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<PettyFloatRecord>> PettyAsync(int propertyId, CancellationToken ct = default);

    Task<long> InsertPettyAsync(ICommandTransaction tx, PettyFloatRecord e, CancellationToken ct = default);

    Task<PettyFloatRecord?> LockPettyAsync(ICommandTransaction tx, int propertyId, long entryId, CancellationToken ct = default);

    Task DecidePettyAsync(ICommandTransaction tx, long entryId, string status, int by, DateTime nowUtc, string? reason, CancellationToken ct = default);

    Task<IReadOnlyList<UdhaarRecord>> UdhaarAsync(int propertyId, CancellationToken ct = default);

    Task<long> InsertUdhaarAsync(ICommandTransaction tx, UdhaarRecord r, CancellationToken ct = default);

    Task<UdhaarRecord?> LockUdhaarAsync(ICommandTransaction tx, int propertyId, long receivableId, CancellationToken ct = default);

    Task<long> InsertSettlementAsync(ICommandTransaction tx, UdhaarSettlementRecord s, CancellationToken ct = default);

    Task VoidUdhaarAsync(ICommandTransaction tx, long receivableId, string? reason, int by, DateOnly date, CancellationToken ct = default);

    Task<IReadOnlyList<MonthLockRecord>> MonthLocksAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);

    Task<MonthLockRecord?> MonthLockAsync(ICommandTransaction tx, int propertyId, string month, CancellationToken ct = default);

    /// <summary>Generate / regenerate the month's feed (replaces it; clears the stale flag, as SCC's wholesale replace).</summary>
    Task SaveMonthLockAsync(ICommandTransaction tx, int propertyId, MonthLockRecord m, CancellationToken ct = default);

    Task MarkStaleAsync(ICommandTransaction tx, int propertyId, string month, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>DEC-131: marks the month unlocked (and stale) with the reason, and records the unlock in the history.</summary>
    Task UnlockMonthAsync(ICommandTransaction tx, int propertyId, string month, int by, DateTime nowUtc, string reason, CancellationToken ct = default);

    /// <summary>DEC-131: every unlock of the property's months, oldest first.</summary>
    Task<IReadOnlyList<MonthUnlockRecord>> MonthUnlocksAsync(int propertyId, CancellationToken ct = default);
}
