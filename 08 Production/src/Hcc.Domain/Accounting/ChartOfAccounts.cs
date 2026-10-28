using System.Text.RegularExpressions;

namespace Hcc.Domain.Accounting;

/// <summary>Legacy account group (prototype <c>ACCGroup</c>): derived from the Tally group of the ledger (ACC-1 contract §1).</summary>
public enum AccountGroup
{
    Asset,
    Liability,
    Income,
    Expense,
    Suspense,
}

/// <summary>GST flag of a ledger (rulebook §3.4.2).</summary>
public enum LedgerGst
{
    None,
    Input,
    Output,
}

/// <summary>
/// A ledger group: one of Tally's 28 predefined groups (fixed ids, ACC-1 contract §1) or an Owner custom sub-group (<c>g-&lt;slug&gt;</c>,
/// <see cref="Custom"/>; nature and side inherited from the parent). <see cref="Nature"/> = normal balance "Dr" / "Cr"; <see cref="Side"/> =
/// "BS" (balance sheet) or "PL" (profit &amp; loss).
/// </summary>
public sealed record LedgerGroup(string Id, string Name, string? Parent, string Nature, string Side, bool Custom = false);

/// <summary>
/// One ledger of the chart (ACC-1 contract §2). <see cref="Group"/> = the legacy group derived from <see cref="GroupId"/> (the Tally group);
/// <see cref="System"/> = built-in (never deleted; renamable, re-groupable within the same Tally nature); <see cref="Control"/> = holds party
/// sub-ledgers <c>&lt;code&gt;/&lt;party&gt;</c> (contract §3), never posted to directly.
/// </summary>
public sealed record LedgerAccount(string Code, string Name, AccountGroup Group, string? Note = null)
{
    public string GroupId { get; init; } = "";

    public bool System { get; init; } = true;

    public bool Control { get; init; }

    public LedgerGst Gst { get; init; } = LedgerGst.None;

    public string? TallyName { get; init; }

    public bool Active { get; init; } = true;

    /// <summary>Name used in the Tally XML masters (contract §2: default = name).</summary>
    public string EffectiveTallyName => string.IsNullOrWhiteSpace(TallyName) ? Name : TallyName;
}

/// <summary>An Owner row of the chart as stored per property (<c>acc.ledger</c>): an override of a built-in or an Owner-added ledger.</summary>
public sealed record StoredLedger(string Code, string Name, string GroupId, LedgerGst Gst, string? TallyName, string? Note, bool Active);

/// <summary>
/// Chart of accounts and the default source-classification → ledger mapping (prototype <c>03 Prototype/src/domain/streams/accounting.ts</c>
/// <c>ACC_GROUPS</c>, <c>ACC_LEDGERS</c>, <c>ACC_DEFAULT_MAP</c>). ACC-1 (posting contract §1, §2; rulebook §3): Tally's 28 groups, the built-in
/// ledgers of the §2 table with their group and control flag, and the mapping re-points (<c>pos:sales</c> 4200, <c>line:attrition</c> 4300,
/// <c>closing:cash-over</c> 4950, <c>tax:gst-input</c> 1310 + <c>tax:gst-input-sgst</c> 1320). The Owner's overrides and Owner-added ledgers
/// are merged per property by <see cref="Merge"/> into an <see cref="EffectiveChart"/>. Illustrative — CA to confirm.
/// </summary>
public static partial class ChartOfAccounts
{
    public const string Suspense = "9999";

    /// <summary>The opening-balance difference ledger (DEC-139); with Suspense it never retires.</summary>
    public const string OpeningDifference = "1400";

    private static LedgerGroup G(string id, string name, string? parent, string nature, string side) => new(id, name, parent, nature, side);

    /// <summary>Tally's 28 predefined groups (15 primary, 13 sub-groups), contract §1 order. Never renamed, deleted or re-parented.</summary>
    public static readonly IReadOnlyList<LedgerGroup> Groups =
    [
        G("capital", "Capital Account", null, "Cr", "BS"), G("reserves", "Reserves & Surplus", "capital", "Cr", "BS"),
        G("current-assets", "Current Assets", null, "Dr", "BS"), G("bank", "Bank Accounts", "current-assets", "Dr", "BS"),
        G("cash", "Cash-in-Hand", "current-assets", "Dr", "BS"), G("deposits-asset", "Deposits (Asset)", "current-assets", "Dr", "BS"),
        G("loans-advances", "Loans & Advances (Asset)", "current-assets", "Dr", "BS"), G("stock", "Stock-in-Hand", "current-assets", "Dr", "BS"),
        G("debtors", "Sundry Debtors", "current-assets", "Dr", "BS"),
        G("current-liabilities", "Current Liabilities", null, "Cr", "BS"), G("duties-taxes", "Duties & Taxes", "current-liabilities", "Cr", "BS"),
        G("provisions", "Provisions", "current-liabilities", "Cr", "BS"), G("creditors", "Sundry Creditors", "current-liabilities", "Cr", "BS"),
        G("fixed-assets", "Fixed Assets", null, "Dr", "BS"), G("investments", "Investments", null, "Dr", "BS"),
        G("loans", "Loans (Liability)", null, "Cr", "BS"), G("bank-od", "Bank OD A/c", "loans", "Cr", "BS"),
        G("secured-loans", "Secured Loans", "loans", "Cr", "BS"), G("unsecured-loans", "Unsecured Loans", "loans", "Cr", "BS"),
        G("suspense", "Suspense A/c", null, "Dr", "BS"), G("misc-expenses-asset", "Misc. Expenses (Asset)", null, "Dr", "BS"),
        G("branches", "Branch / Divisions", null, "Dr", "BS"),
        G("sales", "Sales Accounts", null, "Cr", "PL"), G("purchases", "Purchase Accounts", null, "Dr", "PL"),
        G("direct-income", "Direct Incomes", null, "Cr", "PL"), G("direct-expenses", "Direct Expenses", null, "Dr", "PL"),
        G("indirect-income", "Indirect Incomes", null, "Cr", "PL"), G("indirect-expenses", "Indirect Expenses", null, "Dr", "PL"),
    ];

    /// <summary>Legacy group of a primary group (contract §1): BS+Dr Asset, BS+Cr Liability, PL+Cr Income, PL+Dr Expense, suspense Suspense.</summary>
    public static AccountGroup LegacyOfPrimary(LedgerGroup root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.Id == "suspense" ? AccountGroup.Suspense
            : root.Side == "PL" ? (root.Nature == "Cr" ? AccountGroup.Income : AccountGroup.Expense)
            : root.Nature == "Dr" ? AccountGroup.Asset : AccountGroup.Liability;
    }

    /// <summary>A built-in group by id.</summary>
    public static LedgerGroup? GroupOf(string? id) => id is null ? null : Groups.FirstOrDefault(g => g.Id == id);

    private static AccountGroup BuiltinLegacy(string groupId)
    {
        var g = GroupOf(groupId) ?? throw new InvalidOperationException("Unknown built-in group " + groupId);
        while (g.Parent is not null) g = GroupOf(g.Parent)!;
        return LegacyOfPrimary(g);
    }

    private static LedgerAccount L(string code, string name, string group, string? note = null, bool control = false, LedgerGst gst = LedgerGst.None)
        => new(code, name, BuiltinLegacy(group), note) { GroupId = group, Control = control, Gst = gst };

    /// <summary>The built-in (system) ledgers of the contract §2 table, code order (prototype <c>ACC_LEDGERS</c>, same names and notes).</summary>
    public static readonly IReadOnlyList<LedgerAccount> All =
    [
        L("1000", "Cash in hand", "cash", "Front-desk drawer; one party per till (1000/<till code>)", control: true),
        L("1005", "Petty cash float", "cash", "SCC petty-cash float (expenses wave)"),
        L("1010", "UPI clearing", "bank", "Settles to bank (simulated)"),
        L("1020", "Card clearing", "bank", "Settles to bank net of MDR (simulated)"),
        L("1030", "Bank accounts", "bank", "One party per row of the Bank accounts master (kind bank)", control: true),
        L("1100", "Guest ledger (in-house folios)", "debtors", "Open folio balances; zero after check-out"),
        L("1110", "Company receivables (city ledger)", "debtors", "Authorised company credit; one party per company account", control: true),
        L("1115", "OTA receivables", "debtors", "One party per OTA channel (ACC-2 posts it)", control: true),
        L("1120", "Udhaar receivables (customer credit)", "debtors", "DEC-130 / DEC-144: one party per Udhaar customer", control: true),
        L("1200", "Staff advances", "loans-advances", "Salary advances recovered through payroll; one party per staff member", control: true),
        L("1210", "Recoverable from leavers (F&F)", "loans-advances"),
        L("1300", "GST input credit (legacy, locked snapshots only)", "duties-taxes", "DEC-135 one-ledger ITC before ACC-1; new ITC goes to 1310 / 1320", gst: LedgerGst.Input),
        L("1310", "Input CGST", "duties-taxes", "ITC marked allowed, CGST half", gst: LedgerGst.Input),
        L("1320", "Input SGST", "duties-taxes", "ITC marked allowed, SGST half", gst: LedgerGst.Input),
        L("1330", "Input IGST", "duties-taxes", "Bills from vendors outside Maharashtra (ACC-2)", gst: LedgerGst.Input),
        L("1340", "GST electronic cash ledger", "duties-taxes", "Challan deposits with the government"),
        L("1350", "GST paid on advances (adjustable)", "duties-taxes"),
        L("1360", "TDS receivable", "loans-advances", "TDS deducted by companies / OTAs"),
        L("1370", "Prepaid expenses", "loans-advances"),
        L(OpeningDifference, "Opening-balance difference", "suspense", "DEC-139: parks an unbalanced opening entry; must be zero before the first period lock"),
        L("1500", "Buildings", "fixed-assets"),
        L("1510", "Furniture, fittings & electrical fittings", "fixed-assets"),
        L("1520", "Plant & machinery", "fixed-assets"),
        L("1530", "Computers, software & POS hardware", "fixed-assets"),
        L("1540", "Vehicles", "fixed-assets"),
        L("2000", "Advance deposits (guest and group)", "current-liabilities", "Refundable deposits held; never income"),
        L("2100", "Synthetic tax payable (legacy)", "duties-taxes", "Before DEC-069 only — kept for locked snapshots", gst: LedgerGst.Output),
        L("2110", "Output CGST", "duties-taxes", "GST master, intra-state half (illustrative — CA to confirm)", gst: LedgerGst.Output),
        L("2120", "Output SGST", "duties-taxes", "GST master, intra-state half (illustrative — CA to confirm)", gst: LedgerGst.Output),
        L("2130", "Output IGST", "duties-taxes", "Normally zero (DEC-136 refuses inter-state supply)", gst: LedgerGst.Output),
        L("2140", "GST payable under reverse charge", "duties-taxes", gst: LedgerGst.Output),
        L("2150", "TDS payable", "duties-taxes"),
        L("2200", "Vendor payables", "creditors", "Vendor-credit expenses; one party per expense vendor", control: true),
        L("2300", "Salaries payable (net)", "current-liabilities", "Net pay due to staff"),
        L("2310", "PF payable", "duties-taxes", "Employee + employer PF (illustrative — CA to confirm)"),
        L("2320", "ESIC payable", "duties-taxes", "Employee + employer ESIC (illustrative — CA to confirm)"),
        L("2330", "Professional tax payable", "duties-taxes"),
        L("2340", "Other payroll deductions", "current-liabilities"),
        L("2400", "Bank loans (secured)", "secured-loans", "One party per loan (Owner-added loan rows, ACC-2)", control: true),
        L("2410", "Unsecured loans", "unsecured-loans"),
        L("3000", "Owner's drawings", "capital", "Capital account (contra): cash the Owner takes from the drawer (DEC-073) — CA to confirm"),
        L("3100", "Owner's capital", "capital", "Opening capital + profit − drawings"),
        L("4000", "Room revenue", "sales"),
        L("4100", "Other services revenue", "sales", "Laundry, extras and other services"),
        L("4200", "Food & beverage revenue", "sales", "Outlet (POS) bills"),
        L("4300", "Cancellation, no-show & attrition charges", "sales"),
        L("4400", "Banquet & events revenue", "sales"),
        L("4900", "Discounts allowed", "sales", "Contra revenue"),
        L("4910", "Refunds, reversals & credit notes", "sales", "Contra revenue (folio adjustments)"),
        L("4950", "Cash over", "indirect-income", "Accepted drawer overs (shorts go to 5900)"),
        L("4960", "Interest & other income", "indirect-income"),
        L("5000", "Housekeeping & guest supplies", "direct-expenses"),
        L("5010", "Linen & laundry", "direct-expenses"),
        L("5015", "Kitchen provisions & beverages", "direct-expenses"),
        L("5020", "Repairs & maintenance", "indirect-expenses"),
        L("5030", "Electricity, water & fuel", "direct-expenses"),
        L("5040", "Petty cash & sundry", "indirect-expenses"),
        L("5050", "General & administrative expenses", "indirect-expenses"),
        L("5060", "Rent & licence fees", "indirect-expenses"),
        L("5070", "OTA commission", "indirect-expenses"),
        L("5080", "Bank & card charges", "indirect-expenses"),
        L("5090", "Interest on loans", "indirect-expenses"),
        L("5100", "Salaries & wages", "indirect-expenses"),
        L("5110", "Employer PF & ESIC", "indirect-expenses"),
        L("5120", "Staff welfare", "indirect-expenses"),
        L("5200", "Depreciation", "indirect-expenses"),
        L("5300", "Bad debts written off", "indirect-expenses"),
        L("5400", "GST / TDS interest & late fees", "indirect-expenses"),
        L("5900", "Cash short", "indirect-expenses", "Accepted drawer shorts (overs go to 4950)"),
        L(Suspense, "Suspense (unmapped)", "suspense", "Never a mapping target"),
    ];

    private static readonly Dictionary<string, LedgerAccount> ByCode = All.ToDictionary(a => a.Code, StringComparer.Ordinal);

    /// <summary>The built-in ledgers and groups with no Owner rows (every property before its first chart change).</summary>
    public static readonly EffectiveChart Builtin = new(Groups, All);

    /// <summary>
    /// A built-in ledger by code. A party ledger id <c>&lt;control&gt;/&lt;party&gt;</c> resolves to its control ledger (contract §3: name and
    /// group of the control; the party's own name comes from the party sources).
    /// </summary>
    public static LedgerAccount? Find(string? code)
    {
        if (code is null) return null;
        if (ByCode.TryGetValue(code, out var a)) return a;
        var i = code.IndexOf('/', StringComparison.Ordinal);
        return i > 0 && ByCode.TryGetValue(code[..i], out var c) && c.Control ? c : null;
    }

    public static string NameOf(string code) => Find(code)?.Name ?? code;

    /// <summary>Position in the chart (trial balance order); a party sorts with its control; unknown codes sort last.</summary>
    public static int OrderOf(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var key = Find(code)?.Code ?? code;
        for (var i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Code, key, StringComparison.Ordinal)) return i;
        return 999;
    }

    /// <summary>The control code of a party ledger id (<c>1110/CMP-0007</c> → <c>1110</c>), the code itself otherwise.</summary>
    public static string ControlPart(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var i = code.IndexOf('/', StringComparison.Ordinal);
        return i > 0 ? code[..i] : code;
    }

    /// <summary>Merges the property's Owner rows into the built-ins: overrides of built-in codes (code, system and control never change) and
    /// Owner-added ledgers (system = false, control = false), plus the custom sub-groups.</summary>
    public static EffectiveChart Merge(IEnumerable<StoredLedger>? stored, IEnumerable<LedgerGroup>? customGroups)
    {
        var groups = Groups.Concat((customGroups ?? []).Select(g => g with { Custom = true })).ToList();
        var rows = (stored ?? []).ToDictionary(r => r.Code, StringComparer.Ordinal);
        var probe = new EffectiveChart(groups, All);
        var ledgers = new List<LedgerAccount>();
        foreach (var b in All)
        {
            if (!rows.TryGetValue(b.Code, out var o)) { ledgers.Add(b); continue; }
            ledgers.Add(b with
            {
                Name = o.Name, Group = probe.LegacyOf(o.GroupId), GroupId = o.GroupId, Gst = o.Gst, TallyName = string.IsNullOrWhiteSpace(o.TallyName) ? null : o.TallyName,
                Note = o.Note is null ? b.Note : o.Note.Length == 0 ? null : o.Note, Active = o.Active,
            });
        }

        foreach (var o in rows.Values.Where(r => !ByCode.ContainsKey(r.Code)))
            ledgers.Add(new LedgerAccount(o.Code, o.Name, probe.LegacyOf(o.GroupId), string.IsNullOrEmpty(o.Note) ? null : o.Note)
            {
                GroupId = o.GroupId, System = false, Control = false, Gst = o.Gst, TallyName = string.IsNullOrWhiteSpace(o.TallyName) ? null : o.TallyName, Active = o.Active,
            });
        return new EffectiveChart(groups, ledgers.OrderBy(l => l.Code, StringComparer.Ordinal).ToList());
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

        // ACC-1 (contract §2): an accepted short posts Dr 5900 Cash short; an over posts Cr 4950 Cash over (CashDiffKey).
        ["closing:cash-difference"] = "5900", ["closing:cash-over"] = "4950",
        ["closing:bank-deposit"] = "1030", ["closing:owner-drawing"] = "3000", // Wave B1 (DEC-073): drawer cash banked / drawn by the Owner

        // ACC-1 (contract §2): outlet sales move to 4200 Food & beverage revenue.
        ["pos:sales"] = "4200", ["pos:cgst"] = "2110", ["pos:sgst"] = "2120",
        ["payroll:salaries"] = "5100", ["payroll:employer-pf"] = "5110", ["payroll:employer-esic"] = "5110",
        ["payroll:net-payable"] = "2300", ["payroll:pf-payable"] = "2310", ["payroll:esic-payable"] = "2320",
        ["payroll:pt-payable"] = "2330", ["payroll:other-deductions"] = "2340", ["payroll:advances"] = "1200",

        // DEC-074 (wave B3): group attrition and deposits; full-and-final additions, deductions and an amount the leaver owes (illustrative — CA to confirm).
        // ACC-1 (contract §2): attrition / retention charges move to 4300.
        ["line:attrition"] = "4300", ["receipt:group-deposit"] = "2000",
        ["payroll:fnf-additions"] = "5100", ["payroll:fnf-deductions"] = "2340", ["payroll:fnf-recoverable"] = "1200",

        // DEC-130 an Udhaar payment settles the customer's credit; ACC-1 (contract §2) the expense ITC splits half / half like output GST:
        // CGST half → 1310 (tax:gst-input), SGST half → 1320 (tax:gst-input-sgst); 1300 stays for locked snapshots only.
        ["receipt:udhaar"] = "1120", ["tax:gst-input"] = "1310", ["tax:gst-input-sgst"] = "1320",

        // ACC-2 (acc2-purchase, contract §3): IGST input credit, RCM payable, TDS payable, and the vendor payable a vendor payment / advance
        // debits (party = the vendor, 2200/<vendor>).
        ["tax:gst-input-igst"] = "1330", ["tax:rcm-payable"] = "2140", ["tax:tds-payable"] = "2150", ["vendor:payable"] = "2200",
    };

    /// <summary>ACC-1: the SGST-half ITC key (contract §2).</summary>
    public const string GstInputSgstKey = "tax:gst-input-sgst";

    /// <summary>ACC-1: the mapping key of an accepted cash difference (counted − expected): short → <c>closing:cash-difference</c> (5900),
    /// over → <c>closing:cash-over</c> (4950). Prototype <c>accCashDiffKey</c>.</summary>
    public static string CashDiffKey(long differencePaise) => differencePaise > 0 ? "closing:cash-over" : "closing:cash-difference";

    /// <summary>ACC-1: an expense ITC split half / half exactly as output GST (<see cref="GstSplit.Of"/>): (CGST half, SGST half).
    /// Prototype <c>accItcSplit</c>.</summary>
    public static (long Cgst, long Sgst) ItcSplit(long itcPaise) => GstSplit.Of(itcPaise);

    private static readonly (string Prefix, string Label)[] KeyGroups =
    [
        ("line", "Folio line kind"), ("folio", "Folio"), ("method", "Receipt method (tender)"), ("receipt", "Receipt kind (counter-ledger)"),
        ("expense", "Expense category"), ("funding", "Expense funding"), ("closing", "Closing record"), ("pos", "Outlet (POS) bill"),
        ("payroll", "Payroll"), ("tax", "GST input credit (ITC)"), ("vendor", "Vendor payments (payables)"), // ACC-2 acc2-purchase
    ];

    [GeneratedRegex(@"^(line|folio|method|receipt|expense|funding|closing|pos|payroll|tax|vendor):.{1,72}$")]
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

    /// <summary>Effective mapping = defaults, then the ledgers stored on the masters (MA-11: payment methods → <c>method:</c> / <c>funding:</c>,
    /// expense categories → <c>expense:</c>; only codes of the chart, never Suspense), then the property's explicit overrides (Accounting ›
    /// Ledger mapping wins). <paramref name="chart"/> = the property's effective chart (ACC-1: Owner-added ledgers count); null = built-ins.</summary>
    public static Dictionary<string, string> Effective(IReadOnlyDictionary<string, string>? overrides, IReadOnlyDictionary<string, string>? masterLedgers = null,
        EffectiveChart? chart = null)
    {
        var c = chart ?? Builtin;
        var m = new Dictionary<string, string>(DefaultMapping, StringComparer.Ordinal);
        if (masterLedgers is not null)
            foreach (var (k, v) in masterLedgers)
                if (IsValidKey(k) && c.Find(v) is not null && v != Suspense) m[k] = v;
        if (overrides is not null)
            foreach (var (k, v) in overrides) m[k] = v;
        return m;
    }

    /// <summary>A key resolves when it maps to an existing, ACTIVE, non-suspense ledger of <paramref name="chart"/> (null = built-ins;
    /// ACC-1 integration: a retired ledger posts to Suspense, as the prototype's accLiveVouchers).</summary>
    public static string? Resolve(IReadOnlyDictionary<string, string> mapping, string key, EffectiveChart? chart = null)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(key);
        // ACC-2 (acc2-manual): a manual voucher line names its ledger (ledger:<code>) — an active ledger of the chart, never Suspense.
        if (key.StartsWith(SourceRules.LedgerKeyPrefix, StringComparison.Ordinal))
            return key[SourceRules.LedgerKeyPrefix.Length..] is var code && (chart ?? Builtin).Find(code) is { Active: true } && code != Suspense ? code : null;
        return mapping.TryGetValue(key, out var a) && (chart ?? Builtin).Find(a) is { Active: true } && a != Suspense ? a : null;
    }
}

/// <summary>
/// The chart of one property: Tally's groups + the Owner's custom sub-groups, the built-in ledgers with the Owner's overrides + the
/// Owner-added ledgers (prototype <c>accChart(s)</c> / <c>accGroups(s)</c>, <c>accounting-chart.ts</c>).
/// </summary>
public sealed class EffectiveChart
{
    private readonly Dictionary<string, LedgerAccount> byCode;
    private readonly Dictionary<string, LedgerGroup> byGroup;

    public EffectiveChart(IReadOnlyList<LedgerGroup> groups, IReadOnlyList<LedgerAccount> ledgers)
    {
        Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        Ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
        byCode = ledgers.ToDictionary(l => l.Code, StringComparer.Ordinal);
        byGroup = new Dictionary<string, LedgerGroup>(StringComparer.Ordinal);
        foreach (var g in groups) byGroup[g.Id] = g;
    }

    public IReadOnlyList<LedgerGroup> Groups { get; }

    public IReadOnlyList<LedgerAccount> Ledgers { get; }

    /// <summary>A ledger by code; a party ledger id resolves to its control (contract §3).</summary>
    public LedgerAccount? Find(string? code)
    {
        if (code is null) return null;
        if (byCode.TryGetValue(code, out var a)) return a;
        var i = code.IndexOf('/', StringComparison.Ordinal);
        return i > 0 && byCode.TryGetValue(code[..i], out var c) && c.Control ? c : null;
    }

    public string NameOf(string code) => Find(code)?.Name ?? code;

    public string TallyNameOf(string code) => Find(code)?.EffectiveTallyName ?? code;

    public LedgerGroup? GroupOf(string? id) => id is not null && byGroup.TryGetValue(id, out var g) ? g : null;

    /// <summary>The primary group above <paramref name="id"/> (itself when primary); null for an unknown id or a broken / looping parent chain.</summary>
    public LedgerGroup? RootOf(string? id)
    {
        var g = GroupOf(id);
        for (var i = 0; g?.Parent is not null && i < 32; i++) g = GroupOf(g.Parent);
        return g is { Parent: null } ? g : null;
    }

    /// <summary>Legacy group (Tally nature) of a group, from its primary ancestor; unknown → Suspense.</summary>
    public AccountGroup LegacyOf(string? groupId) => RootOf(groupId) is { } r ? ChartOfAccounts.LegacyOfPrimary(r) : AccountGroup.Suspense;

    /// <summary>Normal balance of a ledger ("Dr" / "Cr") = its group's nature; null for an unknown code.</summary>
    public string? NatureOf(string code) => Find(code) is { } l ? GroupOf(l.GroupId)?.Nature : null;

    /// <summary>Group names from the primary group down to <paramref name="groupId"/>.</summary>
    public IReadOnlyList<string> PathOf(string groupId)
    {
        var path = new List<string>();
        var g = GroupOf(groupId);
        for (var i = 0; g is not null && i < 32; i++)
        {
            path.Insert(0, g.Name);
            g = GroupOf(g.Parent);
        }

        return path;
    }

    /// <summary>True when <paramref name="id"/> is <paramref name="ancestor"/> or below it.</summary>
    public bool IsUnder(string id, string ancestor)
    {
        var g = GroupOf(id);
        for (var i = 0; g is not null && i < 32; i++)
        {
            if (g.Id == ancestor) return true;
            g = GroupOf(g.Parent);
        }

        return false;
    }

    /// <summary>Chart health (prototype <c>accChartProblems</c>): broken custom groups, ledgers in unknown groups, built-ins that changed
    /// nature, duplicate (Tally) names, mappings to unknown or retired ledgers.</summary>
    public IReadOnlyList<ChartProblem> Problems(IReadOnlyDictionary<string, string>? mapping)
    {
        var out_ = new List<ChartProblem>();
        foreach (var g in Groups.Where(g => g.Custom))
        {
            if (RootOf(g.Id) is null) out_.Add(new("group:" + g.Id, "error", null, $"Group {g.Name} has no valid parent", $"Its parent “{g.Parent}” is missing or loops back. Choose another parent."));
            if (!ChartRules.IsGroupId(g.Id)) out_.Add(new("group-id:" + g.Id, "warning", null, $"Group id {g.Id} is not g-<slug>", "Custom sub-group ids start with g-."));
        }

        foreach (var l in Ledgers)
        {
            if (GroupOf(l.GroupId) is null)
                out_.Add(new("ledger-group:" + l.Code, "error", l.Code, $"{l.Code} {l.Name} is in an unknown group", $"Group “{l.GroupId}” does not exist. Move the ledger to a group of the chart."));
            else if (ChartOfAccounts.Find(l.Code) is { } b && l.System && LegacyOf(l.GroupId) != b.Group)
                out_.Add(new("ledger-nature:" + l.Code, "error", l.Code, $"{l.Code} {l.Name} changed nature", $"A built-in {b.Group} ledger sits in a {LegacyOf(l.GroupId)} group."));
        }

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in Ledgers)
        {
            if (seen.TryGetValue(l.EffectiveTallyName, out var other))
                out_.Add(new("dup:" + l.Code, "error", l.Code, $"Duplicate ledger name “{l.EffectiveTallyName}”", $"{other} and {l.Code} carry the same name; Tally needs unique ledger names."));
            else seen[l.EffectiveTallyName] = l.Code;
        }

        foreach (var (k, v) in mapping ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrEmpty(v) || v == ChartOfAccounts.Suspense) continue;
            if (!byCode.TryGetValue(v, out var l))
                out_.Add(new("map:" + k, "error", v, $"Mapping {k} points at {v}, which is not in the chart", "Map it to a ledger of the chart (Mapping tab)."));
            else if (!l.Active)
                out_.Add(new("map:" + k, "error", v, $"Mapping {k} points at retired ledger {v} {l.Name}", "Map it to an active ledger or reactivate the ledger."));
        }

        return out_;
    }
}

/// <summary>One chart health finding (prototype <c>ACCChartProblem</c>).</summary>
public sealed record ChartProblem(string Id, string Severity, string? Code, string Title, string Detail);
