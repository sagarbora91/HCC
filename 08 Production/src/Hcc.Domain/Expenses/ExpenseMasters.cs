using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Domain.Expenses;

/// <summary>One row of the Expense categories master (ops.expense_category, MA-3): needs a bill number, Manager approval-limit override.</summary>
public sealed record ExpenseCategoryRow(string Code, string Name, bool Active, bool NeedsBill, long? ApprovalLimitPaise,
    // ACC-2 (acc2-purchase, contract §3; migration 4231): reverse charge + its default rate, capital (fixed-asset block ledger), ITC blocked (s.17(5)).
    bool Rcm = false, int? RcmBps = null, bool Capital = false, bool ItcBlocked = false, string? Ledger = null,
    bool Transport = false); // ACC-2 integration (ruling 10; migration 4256): goods transport — the cash guard's ₹35,000 limit

/// <summary>One row of the Expense vendors master (ops.expense_vendor, MA-3).</summary>
public sealed record ExpenseVendorRow(int VendorId, string Code, string Name, bool Active,
    // ACC-2 (acc2-purchase, contract §3, §5; migration 4231): GST state (MH / null = intra), TDS flag and section.
    string? State = null, bool Tds = false, string? TdsSection = null);

/// <summary>
/// The expense commands' master checks — the server gaps the SCC gap report found (§4): the category must be an active row of the Expense
/// categories master (not any text), its "needs a bill" flag and its approval-limit override apply, a retired vendor is refused. As the
/// prototype EXPENSE handler (mnyCategory / mnyVendor / mnyApprovalLimit). A new vendor name is added to the list (SCC auto-add).
/// </summary>
public static partial class ExpenseMasters
{
    /// <summary>The seed list (prototype EXPENSE_CATEGORIES) used while a property's list is still empty.</summary>
    public static readonly IReadOnlyList<ExpenseCategoryRow> DefaultCategories =
        ExpenseMethods.SuggestedCategories.Select(n => new ExpenseCategoryRow(n.ToUpperInvariant(), n, true, false, null)).ToList();

    private static IReadOnlyList<ExpenseCategoryRow> Effective(IReadOnlyList<ExpenseCategoryRow>? rows) => rows is { Count: > 0 } ? rows : DefaultCategories;

    /// <summary>The category row of a value (name or code, any case), retired rows included; null when unknown.</summary>
    public static ExpenseCategoryRow? FindCategory(IReadOnlyList<ExpenseCategoryRow>? rows, string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0) return null;
        var list = Effective(rows);
        return list.FirstOrDefault(r => string.Equals(r.Name, v, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(r => string.Equals(r.Code, v, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An active category of the list (unknown → VALIDATION, retired → MASTER_RETIRED unless it is the record's unchanged value).</summary>
    public static ExpenseCategoryRow Category(IReadOnlyList<ExpenseCategoryRow>? rows, string? value, string? keep = null)
    {
        if (string.IsNullOrWhiteSpace(value)) throw HccException.Validation("Choose a category.");
        var c = FindCategory(rows, value)
                ?? throw HccException.Validation("Choose a category from the list (Master configuration › Money and tax › Expense categories).");
        if (!c.Active && !string.Equals(c.Name, keep, StringComparison.Ordinal))
            throw MastersRules.Fail(MasterReasons.Retired, $"Category {c.Name} is retired — choose an active category.", "category");
        return c;
    }

    /// <summary>Manager approval limit of an expense: the category's override, else the hotel limit (prototype <c>mnyApprovalLimit</c>).</summary>
    public static long ApprovalLimit(ExpenseCategoryRow? category, long hotelLimit) => category?.ApprovalLimitPaise ?? hotelLimit;

    /// <summary>"Needs a bill": the bill / receipt number is required once the expense goes for approval (a draft may wait).</summary>
    public static void EnsureBill(ExpenseCategoryRow category, string? receipt)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (category.NeedsBill && string.IsNullOrWhiteSpace(receipt)) throw HccException.Validation($"{category.Name} expenses need the bill / receipt number.");
    }

    /// <summary>
    /// The vendor text a record stores: empty (SCC: optional); a row of the list by name or code (stored as the list name; retired → MASTER_RETIRED);
    /// or a new name (≤ 60) that the command adds to the list.
    /// </summary>
    public static (string Name, bool IsNew) Vendor(IReadOnlyList<ExpenseVendorRow>? rows, string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0) return (string.Empty, false);
        var list = rows ?? [];
        var row = list.FirstOrDefault(r => string.Equals(r.Name, v, StringComparison.OrdinalIgnoreCase))
                  ?? list.FirstOrDefault(r => string.Equals(r.Code, v, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            if (!row.Active) throw MastersRules.Fail(MasterReasons.Retired, $"Vendor {row.Name} is retired — choose an active vendor.", "vendor");
            return (row.Name, false);
        }

        if (v.Length > MastersRules.NameMax) throw HccException.Validation("Keep the vendor name to 60 characters.");
        return (v, true);
    }

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NonCode();

    /// <summary>Code of a vendor added from free text: V-&lt;WORDS&gt; (capitals, digits, "-"; ≤ 20), unique among <paramref name="taken"/> (prototype seedVendorCode).</summary>
    public static string VendorCode(string name, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);
        var b = ("V-" + NonCode().Replace(name.ToUpperInvariant(), "-").Trim('-'));
        b = (b.Length > 20 ? b[..20] : b).TrimEnd('-');
        if (b.Length <= 2) b = "V-VENDOR";
        var code = b;
        var n = 2;
        while (taken.Contains(code))
        {
            var suf = "-" + n++.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var head = b.Length > 20 - suf.Length ? b[..(20 - suf.Length)] : b;
            code = head.TrimEnd('-') + suf;
        }

        taken.Add(code);
        return code;
    }

    /// <summary>Vendor type guessed from the name (prototype seedVendorType; the Owner corrects it in the master).</summary>
    public static string VendorType(string name) =>
        Regex.IsMatch(name, "linen|laundry|dhobi", RegexOptions.IgnoreCase) ? "laundry"
        : Regex.IsMatch(name, "power|water|broadband|electricity board|gas agency|internet", RegexOptions.IgnoreCase) ? "utility" : "supplier";
}
