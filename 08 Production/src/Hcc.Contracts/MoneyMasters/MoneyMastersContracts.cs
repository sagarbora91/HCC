namespace Hcc.Contracts.MoneyMasters;

// Wire contracts of the MoneyMasters module (MA-3, plan Part 2 §8 M15–M18): payment methods, bank accounts and tills, expense categories and
// expense vendors. Same framework as MA-0 (Hcc.Contracts.Masters): master errors travel as VALIDATION / INVALID_TRANSITION with details.reason
// = MasterReasons.* (and details.field); history rows use MasterHistoryView / MasterHistoryList. Every change needs PERM-MST-002 (Owner, DEC-058).
// The production sync layer (03 Prototype/src/clients/sync/money-master.ts) maps these shapes to the prototype rows.

/// <summary>List ids of the four money masters (mst.master_history.master, IMasterUsage, MST_SERVER keys on the client).</summary>
public static class MoneyMasterIds
{
    public const string PaymentMethods = "paymentMethods";
    public const string BankAccounts = "bankAccounts";
    public const string ExpenseCategories = "expenseCategories";
    public const string ExpenseVendors = "expenseVendors";

    public static readonly IReadOnlyList<string> All = [PaymentMethods, BankAccounts, ExpenseCategories, ExpenseVendors];
}

/// <summary>Fixed choice lists (prototype money-master.ts MNY_KINDS / MNY_PLACES / MNY_VENDOR_TYPES).</summary>
public static class MoneyMasterValues
{
    public const string Cash = "cash";
    public const string Upi = "upi";
    public const string Card = "card";
    public const string Bank = "bank";
    public const string Wallet = "wallet";
    public const string Credit = "credit";
    public const string Till = "till";
    public const string FrontDesk = "frontdesk";
    public const string Pos = "pos";
    public const string Expenses = "expenses";

    public static readonly IReadOnlyList<string> Kinds = [Cash, Upi, Card, Bank, Wallet, Credit];
    public static readonly IReadOnlyList<string> Places = [FrontDesk, Pos, Expenses];
    public static readonly IReadOnlyList<string> AccountKinds = [Till, Bank];
    /// <summary>Method kinds an account may be the default for (never cash or credit).</summary>
    public static readonly IReadOnlyList<string> DefaultKinds = [Upi, Card, Bank, Wallet];
    public static readonly IReadOnlyList<string> VendorTypes = ["supplier", "utility", "laundry", "other"];
}

// ── Payment methods (M15) ──

/// <summary>One payment method. <c>SettlesTo</c> = code of a bank account / till (null for credit). <c>UsageCount</c> = folio receipts, outlet
/// bills and expenses that store this method (by name, code or an earlier name).</summary>
public sealed record PaymentMethodView(int PaymentMethodId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, string Kind, string? SettlesTo,
    bool NeedsReference, IReadOnlyList<string> UsableAt, int? SurchargeBps, string? Ledger, int UsageCount, string UpdatedUtc, string Version); // MA-11: SurchargeBps null without money visibility

public sealed record PaymentMethodList(int Page, int PageSize, int Total, IReadOnlyList<PaymentMethodView> Items);

/// <summary>SavePaymentMethod K [V]: <c>PaymentMethodId</c> null = create; an edit needs <c>ExpectedVersion</c>.</summary>
public sealed record SavePaymentMethodRequest(int? PaymentMethodId, string Code, string Name, string Kind, string? SettlesTo, bool NeedsReference, IReadOnlyList<string>? UsableAt,
    int SurchargeBps = 0, string? Ledger = null, int? SortOrder = null, string? ExpectedVersion = null);

public sealed record PaymentMethodIdRequest(int PaymentMethodId, string ExpectedVersion);

public sealed record ReorderPaymentMethodsRequest(IReadOnlyList<int> PaymentMethodIds);

// ── Bank accounts and tills (M16) ──

/// <summary>One bank account or till. <c>OpeningBalancePaise</c> is null for callers without PERM-MST-002 (money field, DEC-061).
/// <c>UsageCount</c> = payment methods that settle to it.</summary>
public sealed record BankAccountView(int BankAccountId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, string Kind, string? Bank, string? AccountLast4,
    string? Ifsc, long? OpeningBalancePaise, IReadOnlyList<string> DefaultFor, int UsageCount, string UpdatedUtc, string Version);

public sealed record BankAccountList(int Page, int PageSize, int Total, IReadOnlyList<BankAccountView> Items);

/// <summary>SaveBankAccount K [V]: only the last four digits of an account number are accepted; the opening balance is setup-only.</summary>
public sealed record SaveBankAccountRequest(int? BankAccountId, string Code, string Name, string Kind, string? Bank = null, string? AccountLast4 = null, string? Ifsc = null,
    long OpeningBalancePaise = 0, IReadOnlyList<string>? DefaultFor = null, int? SortOrder = null, string? ExpectedVersion = null);

public sealed record BankAccountIdRequest(int BankAccountId, string ExpectedVersion);

public sealed record ReorderBankAccountsRequest(IReadOnlyList<int> BankAccountIds);

// ── Expense categories (M17) ──

/// <summary>One expense category. <c>ApprovalLimitPaise</c> null = the hotel limit (Manager ₹5,000).</summary>
public sealed record ExpenseCategoryView(int ExpenseCategoryId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, string Ledger, bool NeedsBill,
    long? ApprovalLimitPaise, int UsageCount, string UpdatedUtc, string Version);

public sealed record ExpenseCategoryList(int Page, int PageSize, int Total, IReadOnlyList<ExpenseCategoryView> Items);

public sealed record SaveExpenseCategoryRequest(int? ExpenseCategoryId, string Code, string Name, string Ledger, bool NeedsBill = false, long? ApprovalLimitPaise = null,
    int? SortOrder = null, string? ExpectedVersion = null);

public sealed record ExpenseCategoryIdRequest(int ExpenseCategoryId, string ExpectedVersion);

public sealed record ReorderExpenseCategoriesRequest(IReadOnlyList<int> ExpenseCategoryIds);

// ── Expense vendors (M18, DEC-MA-6) ──

/// <summary>One expense vendor. <c>Pan</c> is null for callers without PERM-MST-002.</summary>
public sealed record ExpenseVendorView(int ExpenseVendorId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, string Type, string? Gstin, string? Pan,
    bool Tds, string? Contact, string? Terms, int UsageCount, string UpdatedUtc, string Version);

public sealed record ExpenseVendorList(int Page, int PageSize, int Total, IReadOnlyList<ExpenseVendorView> Items);

public sealed record SaveExpenseVendorRequest(int? ExpenseVendorId, string Code, string Name, string Type, string? Gstin = null, string? Pan = null, bool Tds = false,
    string? Contact = null, string? Terms = null, int? SortOrder = null, string? ExpectedVersion = null);

public sealed record ExpenseVendorIdRequest(int ExpenseVendorId, string ExpectedVersion);

public sealed record ReorderExpenseVendorsRequest(IReadOnlyList<int> ExpenseVendorIds);

/// <summary>Result of a Delete* command (only a never-used row is deleted; MASTER_IN_USE otherwise — retire it instead).</summary>
public sealed record MoneyMasterDeleteResult(string Master, int Id, string Code, bool Deleted);
