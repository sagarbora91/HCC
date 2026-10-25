using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Payments;

namespace Hcc.Domain.Expenses;

/// <summary>Expense status (prototype <c>Expense.status</c>, stored in <c>ops.expense.status</c>).</summary>
public static class ExpenseState
{
    public const string Draft = "Draft";
    public const string Pending = "Pending";
    public const string Posted = "Posted";
    public const string Rejected = "Rejected";
    public const string Reversed = "Reversed";
}

/// <summary>Funding sources (prototype EXPENSE_METHODS, REQ-EXP-001). Cash is drawer cash; Vendor credit has no drawer effect.</summary>
public static class ExpenseMethods
{
    public const string Cash = "Cash";
    public const string Upi = "UPI";
    public const string BankTransfer = "Bank transfer";
    public const string VendorCredit = "Vendor credit";

    public static readonly IReadOnlyList<string> All = [Cash, Upi, BankTransfer, VendorCredit];

    /// <summary>Suggestions only (prototype EXPENSE_CATEGORIES); the category list becomes a Master configuration setting (DEC-057).</summary>
    public static readonly IReadOnlyList<string> SuggestedCategories = ["Supplies", "Laundry", "Maintenance", "Utilities", "Petty cash"];
}

/// <summary>The fields of an expense that the figures and duplicate rules read.</summary>
public sealed record ExpenseFact(long ExpenseId, string Vendor, Paise Amount, DateOnly Date, string Method, string Status, bool IsReversal);

/// <summary>Posted figures for one business date (prototype report pack "Expenses and cash" tab).</summary>
public sealed record ExpenseTotals(Paise Cash, Paise NonCash, Paise VendorCredit, Paise Total, Paise Reversals, int Count);

/// <summary>Expense rules ported from <c>closing.ts</c> and the EXPENSE* mock handlers (no I/O).</summary>
public static class ExpenseRules
{
    public const int VendorMax = 120;
    public const int CategoryMax = 60;
    public const int NotesMax = 300;
    public const int ReceiptMax = 120;
    public const int ReasonMax = 300;

    /// <summary>Posted and later-reversed expenses both happened on their date; the negative reversal record offsets on its own date (<c>expenseCounts</c>).</summary>
    public static bool Counts(string status) => status is ExpenseState.Posted or ExpenseState.Reversed;

    public static (string Vendor, string Category, string Method, Paise Amount, string? Notes, string? Receipt) NormalizeNew(
        string? vendor, string? category, string? method, long amountPaise, string? notes, string? receipt)
    {
        var v = (vendor ?? string.Empty).Trim();
        if (v.Length == 0) throw HccException.Validation("Enter a vendor.");
        if (v.Length > VendorMax) throw HccException.Validation($"The vendor is at most {VendorMax} characters.");
        var c = (category ?? string.Empty).Trim();
        if (c.Length == 0) throw HccException.Validation("Choose a category.");
        if (c.Length > CategoryMax) throw HccException.Validation($"The category is at most {CategoryMax} characters.");
        var m = (method ?? string.Empty).Trim();
        if (!ExpenseMethods.All.Contains(m, StringComparer.Ordinal)) throw HccException.Validation("Choose a funding source.");
        var amount = Paise.FromWire(amountPaise).EnsureReceiptAmount();
        return (v, c, m, amount, Clip(notes, NotesMax), Clip(receipt, ReceiptMax));
    }

    private static string? Clip(string? s, int max)
    {
        var t = (s ?? string.Empty).Trim();
        return t.Length == 0 ? null : t.Length <= max ? t : t[..max];
    }

    /// <summary>Optional reason (rejection): trimmed, at most <see cref="ReasonMax"/> characters, null when blank.</summary>
    public static string? OptionalReason(string? reason) => Clip(reason, ReasonMax);

    /// <summary>A reversal always carries a reason (prototype: "A reason is required to reverse an expense.").</summary>
    public static string RequiredReversalReason(string? reason)
    {
        var t = (reason ?? string.Empty).Trim();
        if (t.Length == 0) throw HccException.Validation("A reason is required to reverse an expense.");
        if (t.Length > ReasonMax) throw HccException.Validation($"The reason is at most {ReasonMax} characters.");
        return t;
    }

    /// <summary>Same vendor (case-insensitive) + amount + date, excluding rejected items and reversal records (prototype <c>expenseDuplicates</c>). A warning only.</summary>
    public static IReadOnlyList<long> Duplicates(IEnumerable<ExpenseFact> all, string vendor, Paise amount, DateOnly date, long exceptId = 0)
    {
        ArgumentNullException.ThrowIfNull(all);
        var v = (vendor ?? string.Empty).Trim();
        if (v.Length == 0 || amount == Paise.Zero) return [];
        return all.Where(e => e.ExpenseId != exceptId && !e.IsReversal && e.Status != ExpenseState.Rejected && e.Amount == amount && e.Date == date
                && string.Equals(e.Vendor.Trim(), v, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ExpenseId).ToList();
    }

    /// <summary>Net posted figures for a date, split by funding source (prototype report pack: cash, UPI+bank, vendor credit, total; reversals shown separately).</summary>
    public static ExpenseTotals Totals(IEnumerable<ExpenseFact> all, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(all);
        Paise cash = Paise.Zero, nonCash = Paise.Zero, vendor = Paise.Zero, reversals = Paise.Zero;
        var count = 0;
        foreach (var e in all.Where(e => e.Date == date && Counts(e.Status)))
        {
            count++;
            switch (e.Method)
            {
                case ExpenseMethods.Cash: cash += e.Amount; break;
                case ExpenseMethods.Upi or ExpenseMethods.BankTransfer: nonCash += e.Amount; break;
                case ExpenseMethods.VendorCredit: vendor += e.Amount; break;
            }

            if (e.IsReversal) reversals += e.Amount;
        }

        return new ExpenseTotals(cash, nonCash, vendor, cash + nonCash + vendor, reversals, count);
    }

    /// <summary>
    /// The drawer effect of a posted/reversed expense: only Cash moves the drawer (negative on posting, positive on reversal);
    /// null for UPI, bank transfer and vendor credit. Amount is the positive expense amount.
    /// </summary>
    public static CashMovement? DrawerMovement(string method, Paise amount, bool reversal)
    {
        if (method != ExpenseMethods.Cash) return null;
        return reversal ? new CashMovement(CashMovementKind.ExpenseReversal, amount) : new CashMovement(CashMovementKind.Expense, -amount);
    }

    // ── Transitions (prototype EXPENSE_SUBMIT / EXPENSE_APPROVE / EXPENSE_REVERSE) ──

    public static void EnsureCanSubmitDraft(string status, int submittedBy, int actor)
    {
        if (status != ExpenseState.Draft) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a draft can be submitted.");
        if (submittedBy != actor) throw new HccException(HccErrorCode.FORBIDDEN, "You can submit only your own drafts.");
    }

    /// <summary>Pending only; never your own expense (PERM-SOD-001). Limits are the access policy's job (PERM-EXP-002 limit).</summary>
    public static void EnsureCanDecide(string status, int submittedBy, int actor)
    {
        if (status != ExpenseState.Pending) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Expense is no longer pending.");
        if (submittedBy == actor) throw new HccException(HccErrorCode.FORBIDDEN, "You cannot approve your own expense.");
    }

    /// <summary>Only a posted original can be reversed, once (not a reversal record).</summary>
    public static void EnsureCanReverse(string status, bool isReversal)
    {
        if (status != ExpenseState.Posted || isReversal) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Only a posted expense can be reversed.");
    }
}
