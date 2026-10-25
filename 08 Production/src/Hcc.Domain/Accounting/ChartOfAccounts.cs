using System.Text.RegularExpressions;

namespace Hcc.Domain.Accounting;

/// <summary>Account group (prototype <c>ACCGroup</c>).</summary>
public enum AccountGroup
{
    Asset,
    Liability,
    Income,
    Expense,
    Suspense,
}

/// <summary>One ledger of the synthetic chart of accounts.</summary>
public sealed record LedgerAccount(string Code, string Name, AccountGroup Group, string? Note = null);

/// <summary>
/// Synthetic chart of accounts and default source-classification → ledger mapping, ported from the prototype
/// (<c>03 Prototype/src/domain/streams/accounting.ts</c> <c>ACC_CHART</c>, <c>ACC_DEFAULT_MAP</c>; module spec 20 §3–§4) with the
/// DEC-069 GST ledgers (2110 Output CGST, 2120 Output SGST; 2100 kept for legacy snapshots only). Production adds the payroll
/// ledgers (5100/5110, 2300–2340, 1200) for the W2-2 <c>PayrollPosted</c>/<c>PayrollPaid</c> contracts. Illustrative — CA to confirm.
/// </summary>
public static partial class ChartOfAccounts
{
    public const string Suspense = "9999";

    public static readonly IReadOnlyList<LedgerAccount> All =
    [
        new("1000", "Cash in hand", AccountGroup.Asset, "Front-desk drawer"),
        new("1010", "UPI clearing", AccountGroup.Asset, "Settles to bank (simulated)"),
        new("1020", "Card clearing", AccountGroup.Asset, "Settles to bank (simulated)"),
        new("1030", "Bank account", AccountGroup.Asset, "Bank transfers"),
        new("1100", "Guest ledger", AccountGroup.Asset, "Open folio balances"),
        new("1110", "Company receivables (city ledger)", AccountGroup.Asset, "Authorised company credit"),
        new("1200", "Staff advances", AccountGroup.Asset, "Salary advances recovered through payroll"),
        new("2000", "Advance deposits", AccountGroup.Liability, "Refundable deposits held"),
        new("2100", "Synthetic tax payable (legacy)", AccountGroup.Liability, "Before DEC-069 only — kept for locked snapshots"),
        new("2110", "Output CGST", AccountGroup.Liability, "GST master, intra-state half (illustrative — CA to confirm)"),
        new("2120", "Output SGST", AccountGroup.Liability, "GST master, intra-state half (illustrative — CA to confirm)"),
        new("2200", "Vendor payables", AccountGroup.Liability, "Vendor-credit expenses"),
        new("2300", "Salaries payable", AccountGroup.Liability, "Net pay due to staff"),
        new("2310", "PF payable", AccountGroup.Liability, "Employee + employer PF (illustrative — CA to confirm)"),
        new("2320", "ESIC payable", AccountGroup.Liability, "Employee + employer ESIC (illustrative — CA to confirm)"),
        new("2330", "Professional tax payable", AccountGroup.Liability),
        new("2340", "Other payroll deductions", AccountGroup.Liability),
        new("4000", "Room revenue", AccountGroup.Income),
        new("4100", "Other revenue", AccountGroup.Income, "Services and extras"),
        new("4900", "Discounts allowed", AccountGroup.Income, "Contra revenue"),
        new("4910", "Refunds, reversals & credit notes", AccountGroup.Income, "Contra revenue (folio adjustments)"),
        new("5000", "Supplies", AccountGroup.Expense),
        new("5010", "Laundry", AccountGroup.Expense),
        new("5020", "Repairs & maintenance", AccountGroup.Expense),
        new("5030", "Utilities", AccountGroup.Expense),
        new("5040", "Petty cash & sundry", AccountGroup.Expense),
        new("5050", "General expenses", AccountGroup.Expense),
        new("5100", "Salaries & wages", AccountGroup.Expense),
        new("5110", "Employer PF & ESIC", AccountGroup.Expense),
        new("5900", "Cash over / short", AccountGroup.Expense, "Accepted drawer differences"),
        new(Suspense, "Suspense (unmapped)", AccountGroup.Suspense, "Never a mapping target"),
    ];

    private static readonly Dictionary<string, LedgerAccount> ByCode = All.ToDictionary(a => a.Code, StringComparer.Ordinal);

    public static LedgerAccount? Find(string? code) => code is not null && ByCode.TryGetValue(code, out var a) ? a : null;

    public static string NameOf(string code) => Find(code)?.Name ?? code;

    /// <summary>Position in the chart (trial balance order); unknown codes sort last.</summary>
    public static int OrderOf(string code)
    {
        for (var i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Code, code, StringComparison.Ordinal)) return i;
        return 999;
    }

    /// <summary>Default mapping (prototype ACC_DEFAULT_MAP + payroll keys).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultMapping = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["line:room"] = "4000", ["line:service"] = "4100", ["line:discount"] = "4900", ["line:adjustment"] = "4910",
        ["folio:guest"] = "1100", ["folio:cgst"] = "2110", ["folio:sgst"] = "2120",
        ["method:Cash"] = "1000", ["method:UPI"] = "1010", ["method:Card"] = "1020", ["method:Bank transfer"] = "1030",
        ["receipt:payment"] = "1100", ["receipt:refund"] = "1100", ["receipt:deposit"] = "2000", ["receipt:deposit-return"] = "2000",
        ["receipt:company-credit"] = "1110",
        ["expense:Supplies"] = "5000", ["expense:Laundry"] = "5010", ["expense:Maintenance"] = "5020", ["expense:Utilities"] = "5030",
        ["expense:Petty cash"] = "5040",
        ["funding:Cash"] = "1000", ["funding:UPI"] = "1010", ["funding:Card"] = "1020", ["funding:Bank transfer"] = "1030",
        ["funding:Vendor credit"] = "2200",
        ["closing:cash-difference"] = "5900",
        ["pos:sales"] = "4100", ["pos:cgst"] = "2110", ["pos:sgst"] = "2120",
        ["payroll:salaries"] = "5100", ["payroll:employer-pf"] = "5110", ["payroll:employer-esic"] = "5110",
        ["payroll:net-payable"] = "2300", ["payroll:pf-payable"] = "2310", ["payroll:esic-payable"] = "2320",
        ["payroll:pt-payable"] = "2330", ["payroll:other-deductions"] = "2340", ["payroll:advances"] = "1200",
    };

    private static readonly (string Prefix, string Label)[] KeyGroups =
    [
        ("line", "Folio line kind"), ("folio", "Folio"), ("method", "Receipt method (tender)"), ("receipt", "Receipt kind (counter-ledger)"),
        ("expense", "Expense category"), ("funding", "Expense funding"), ("closing", "Closing record"), ("pos", "Outlet (POS) bill"),
        ("payroll", "Payroll"),
    ];

    [GeneratedRegex(@"^(line|folio|method|receipt|expense|funding|closing|pos|payroll):.{1,72}$")]
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    public static string KeyGroup(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var prefix = key.Split(':')[0];
        foreach (var (p, label) in KeyGroups)
            if (p == prefix) return label;
        return "Other";
    }

    public static int KeyGroupOrder(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var prefix = key.Split(':')[0];
        for (var i = 0; i < KeyGroups.Length; i++)
            if (KeyGroups[i].Prefix == prefix) return i;
        return -1;
    }

    public static string KeyValue(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var i = key.IndexOf(':', StringComparison.Ordinal);
        return i < 0 ? key : key[(i + 1)..];
    }

    /// <summary>Effective mapping = defaults overlaid with the property's overrides.</summary>
    public static Dictionary<string, string> Effective(IReadOnlyDictionary<string, string>? overrides)
    {
        var m = new Dictionary<string, string>(DefaultMapping, StringComparer.Ordinal);
        if (overrides is not null)
            foreach (var (k, v) in overrides) m[k] = v;
        return m;
    }

    /// <summary>A key resolves when it maps to an existing, non-suspense ledger.</summary>
    public static string? Resolve(IReadOnlyDictionary<string, string> mapping, string key)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return mapping.TryGetValue(key, out var a) && Find(a) is not null && a != Suspense ? a : null;
    }
}
