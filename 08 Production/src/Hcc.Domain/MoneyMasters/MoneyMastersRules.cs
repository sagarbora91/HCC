using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Accounting;
using Hcc.Domain.Masters;

namespace Hcc.Domain.MoneyMasters;

/// <summary>One stored row of a money master: the framework columns plus the master's own fields (canonical values: string, bool, int,
/// long, IReadOnlyList&lt;string&gt;; null = not set). Field names are the prototype keys (money-master.ts).</summary>
public sealed record MoneyRow(int Id, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, DateTime UpdatedUtc, byte[] RowVer,
    IReadOnlyDictionary<string, object?> Fields)
{
    public object? this[string field] => Fields.TryGetValue(field, out var v) ? v : null;

    public string? Text(string field) => this[field] as string;

    public bool Flag(string field) => this[field] is true;

    public IReadOnlyList<string> List(string field) => this[field] as IReadOnlyList<string> ?? [];
}

/// <summary>A validated row from a Save request: framework part + the master's fields.</summary>
public sealed record MoneyDraft(MastersRules.CleanRow Row, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// Rules of the four money masters (prototype money-master-specs.ts): field normalisation and validation, the cross-row rules (cash settles
/// to a till, credit to nothing, the rest to a bank account; one default account per method kind; the last front-desk method and an account
/// that active methods settle to cannot be retired; kind / settles-to locked once used; the opening balance is setup-only) and the value →
/// row resolution records use (name, code, or an earlier name of the row). No I/O.
/// </summary>
public static partial class MoneyMastersRules
{
    // Field keys (= prototype keys, also the mst.master_history field names).
    public const string Kind = "kind";
    public const string SettlesTo = "settlesTo";
    public const string NeedsReference = "needsReference";
    public const string UsableAt = "usableAt";
    public const string SurchargePct = "surchargePct";
    public const string Ledger = "ledger";
    public const string Bank = "bank";
    public const string AccountLast4 = "accountLast4";
    public const string Ifsc = "ifsc";
    public const string OpeningBalance = "openingBalance";
    public const string DefaultFor = "defaultFor";
    public const string NeedsBill = "needsBill";
    public const string ApprovalLimit = "approvalLimit";
    public const string Type = "type";
    public const string Gstin = "gstin";
    public const string Pan = "pan";
    public const string Tds = "tds";
    public const string Contact = "contact";
    public const string Terms = "terms";
    // ACC-2 acc2-purchase (contract §3, §5; migration 4231).
    public const string Rcm = "rcm";
    public const string RcmBps = "rcmBps";
    public const string Capital = "capital";
    public const string ItcBlocked = "itcBlocked";
    public const string State = "state";
    public const string TdsSection = "tdsSection";

    public const int MaxSurchargeBps = 1000;
    public const long MaxMoneyPaise = 10_000_000_000;

    [GeneratedRegex("^[A-Z]{4}0[A-Z0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex IfscPattern();

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.CultureInvariant)]
    private static partial Regex GstinPattern();

    [GeneratedRegex("^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.CultureInvariant)]
    private static partial Regex PanPattern();

    [GeneratedRegex("^[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Last4Pattern();

    /// <summary>Label and singular of a master (audit text "Master &lt;label&gt;: …").</summary>
    public static (string Label, string Singular) Describe(string master) => master switch
    {
        MoneyMasterIds.PaymentMethods => ("Payment methods", "payment method"),
        MoneyMasterIds.BankAccounts => ("Bank accounts and tills", "account"),
        MoneyMasterIds.ExpenseCategories => ("Expense categories", "expense category"),
        MoneyMasterIds.ExpenseVendors => ("Expense vendors", "expense vendor"),
        _ => throw new ArgumentOutOfRangeException(nameof(master), master, "Unknown money master."),
    };

    /// <summary>The field keys of a master, in display / history order.</summary>
    public static IReadOnlyList<string> FieldsOf(string master) => master switch
    {
        MoneyMasterIds.PaymentMethods => [Kind, SettlesTo, NeedsReference, UsableAt, SurchargePct, Ledger],
        MoneyMasterIds.BankAccounts => [Kind, Bank, AccountLast4, Ifsc, OpeningBalance, DefaultFor],
        MoneyMasterIds.ExpenseCategories => [Ledger, NeedsBill, ApprovalLimit, ItcBlocked, Rcm, RcmBps, Capital],
        MoneyMasterIds.ExpenseVendors => [Type, Gstin, Pan, Tds, State, TdsSection, Contact, Terms],
        _ => throw new ArgumentOutOfRangeException(nameof(master), master, "Unknown money master."),
    };

    private static string? Opt(string? v, int max, string label, string field)
    {
        var t = (v ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        if (t.Length > max) throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: at most {max} characters.", field);
        return t;
    }

    private static string? Up(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant();

    private static IReadOnlyList<string> Pick(IReadOnlyList<string>? values, IReadOnlyList<string> allowed, string label, string field)
    {
        var set = (values ?? []).Select(x => (x ?? string.Empty).Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        if (set.Any(x => !allowed.Contains(x))) throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: choose from the list.", field);
        return allowed.Where(set.Contains).ToList(); // canonical order
    }

    private static string One(string? value, IReadOnlyList<string> allowed, string label, string field)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!allowed.Contains(v)) throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: choose from the list.", field);
        return v;
    }

    private static string? LedgerOf(string? code, bool required, IReadOnlyCollection<AccountGroup> groups)
    {
        var c = (code ?? string.Empty).Trim();
        if (c.Length == 0)
        {
            if (required) throw MastersRules.Fail(MasterReasons.Invalid, "Choose the ledger.", Ledger);
            return null;
        }

        var a = ChartOfAccounts.Find(c);
        if (a is null || a.Code == ChartOfAccounts.Suspense || !groups.Contains(a.Group))
            throw MastersRules.Fail(MasterReasons.Invalid, $"Ledger {c} is not a {string.Join(" / ", groups.Select(g => g.ToString().ToLowerInvariant()))} ledger of the chart of accounts.", Ledger);
        return a.Code;
    }

    /// <summary>Payment method fields (SettlesTo resolved to the account's code, cross-row rules in <see cref="CheckPaymentMethod"/>).</summary>
    public static MoneyDraft PaymentMethod(SavePaymentMethodRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var row = MastersRules.Validate(r.Code, r.Name, r.SortOrder);
        var kind = One(r.Kind, MoneyMasterValues.Kinds, "Kind", Kind);
        if (r.SurchargeBps is < 0 or > MaxSurchargeBps) throw MastersRules.Fail(MasterReasons.Invalid, "Surcharge: enter a percentage from 0 to 10.", SurchargePct);
        var fields = new Dictionary<string, object?>
        {
            [Kind] = kind,
            [SettlesTo] = MastersRules.NormalizeCode(r.SettlesTo) is { Length: > 0 } s ? s : null,
            [NeedsReference] = r.NeedsReference,
            [UsableAt] = Pick(r.UsableAt, MoneyMasterValues.Places, "Usable at", UsableAt),
            [SurchargePct] = r.SurchargeBps,
            [Ledger] = LedgerOf(r.Ledger, false, [AccountGroup.Asset, AccountGroup.Liability]),
        };
        return new MoneyDraft(row, fields);
    }

    /// <summary>Cross-row rules of a payment method against the accounts (code → kind, active) and its stored self.</summary>
    public static void CheckPaymentMethod(MoneyDraft d, MoneyRow? old, int usage, IReadOnlyList<MoneyRow> accounts)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(accounts);
        var kind = (string)d.Fields[Kind]!;
        var settles = d.Fields[SettlesTo] as string;
        var acc = settles is null ? null : accounts.FirstOrDefault(a => a.Code == settles);
        if (settles is not null && acc is null) throw MastersRules.Fail(MasterReasons.Invalid, $"Settles to: {settles} is not in Bank accounts and tills.", SettlesTo);
        if (acc is { Active: false } && (old?.Text(SettlesTo) != settles))
            throw MastersRules.Fail(MasterReasons.Retired, $"Settles to: {acc.Name} is retired — choose an active account.", SettlesTo);
        if (kind == MoneyMasterValues.Cash && acc?.Text(Kind) != MoneyMasterValues.Till) throw MastersRules.Fail(MasterReasons.Invalid, "A cash method settles to a till (the front-desk drawer).", SettlesTo);
        if (kind == MoneyMasterValues.Credit && settles is not null) throw MastersRules.Fail(MasterReasons.Invalid, "A credit method settles to no account — leave “Settles to” empty.", SettlesTo);
        if (kind is not (MoneyMasterValues.Cash or MoneyMasterValues.Credit) && acc?.Text(Kind) != MoneyMasterValues.Bank)
            throw MastersRules.Fail(MasterReasons.Invalid, "Choose the bank account this method settles to.", SettlesTo);
        var at = (IReadOnlyList<string>)d.Fields[UsableAt]!;
        if (kind == MoneyMasterValues.Credit && (at.Contains(MoneyMasterValues.FrontDesk) || at.Contains(MoneyMasterValues.Pos)))
            throw MastersRules.Fail(MasterReasons.Invalid, "Credit is not a guest payment: company credit goes through the approval on the folio. Use it for expenses only.", UsableAt);
        if (old is not null && usage > 0 && (old.Text(Kind) != kind || old.Text(SettlesTo) != settles))
            throw MastersRules.Fail(MasterReasons.Invalid, $"Kind and “settles to” cannot change once the method is used ({usage} records) — add a new method and retire this one.", Kind);
    }

    /// <summary>Refuses retiring the last active payment method usable at the front desk.</summary>
    public static void EnsureMethodMayRetire(MoneyRow row, IReadOnlyList<MoneyRow> methods)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(methods);
        if (row.List(UsableAt).Contains(MoneyMasterValues.FrontDesk)
            && !methods.Any(m => m.Id != row.Id && m.Active && m.List(UsableAt).Contains(MoneyMasterValues.FrontDesk)))
            throw MastersRules.Fail(MasterReasons.InUse, "Keep at least one active payment method for the front desk.");
    }

    public static MoneyDraft BankAccount(SaveBankAccountRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var row = MastersRules.Validate(r.Code, r.Name, r.SortOrder);
        var kind = One(r.Kind, MoneyMasterValues.AccountKinds, "Type", Kind);
        var last4 = Opt(r.AccountLast4, 4, "Account number", AccountLast4);
        if (last4 is not null && !Last4Pattern().IsMatch(last4)) throw MastersRules.Fail(MasterReasons.Invalid, "Account number: enter only the last 4 digits.", AccountLast4);
        var ifsc = Up(r.Ifsc);
        if (ifsc is not null && !IfscPattern().IsMatch(ifsc)) throw MastersRules.Fail(MasterReasons.Invalid, "IFSC: 4 letters, 0, then 6 letters or digits (e.g. ABCD0123456).", Ifsc);
        if (r.OpeningBalancePaise is < 0 or > MaxMoneyPaise) throw MastersRules.Fail(MasterReasons.Invalid, "Opening balance: enter an amount in rupees (0 or more).", OpeningBalance);
        var bank = Opt(r.Bank, 60, "Bank", Bank);
        var defaults = Pick(r.DefaultFor, MoneyMasterValues.DefaultKinds, "Default for", DefaultFor);
        if (kind == MoneyMasterValues.Till && (bank is not null || last4 is not null || ifsc is not null))
            throw MastersRules.Fail(MasterReasons.Invalid, "A till has no bank, account number or IFSC.", Bank);
        if (kind == MoneyMasterValues.Till && defaults.Count > 0) throw MastersRules.Fail(MasterReasons.Invalid, "A till is not a default for UPI, card or bank methods.", DefaultFor);
        if (kind == MoneyMasterValues.Bank && bank is null) throw MastersRules.Fail(MasterReasons.Invalid, "Enter the bank name.", Bank);
        var fields = new Dictionary<string, object?>
        {
            [Kind] = kind, [Bank] = bank, [AccountLast4] = last4, [Ifsc] = ifsc, [OpeningBalance] = r.OpeningBalancePaise, [DefaultFor] = defaults,
        };
        return new MoneyDraft(row, fields);
    }

    /// <summary>One active default per method kind; type fixed while methods settle to it; opening balance fixed once records settle through it.</summary>
    public static void CheckBankAccount(MoneyDraft d, MoneyRow? old, int methodsSettling, int recordsThrough, IReadOnlyList<MoneyRow> accounts)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(accounts);
        foreach (var k in (IReadOnlyList<string>)d.Fields[DefaultFor]!)
        {
            var other = accounts.FirstOrDefault(a => a.Id != old?.Id && a.Active && a.List(DefaultFor).Contains(k));
            if (other is not null) throw MastersRules.Fail(MasterReasons.Invalid, $"{other.Name} is already the default for {k} — remove it there first.", DefaultFor);
        }

        if (old is null) return;
        if (old.Text(Kind) != (string)d.Fields[Kind]! && methodsSettling > 0)
            throw MastersRules.Fail(MasterReasons.Invalid, "Type cannot change while payment methods settle to this account.", Kind);
        if (!Equals(old[OpeningBalance], d.Fields[OpeningBalance]) && recordsThrough > 0)
            throw MastersRules.Fail(MasterReasons.Invalid, $"The opening balance is setup-only: {recordsThrough} records already settle through {old.Name}.", OpeningBalance);
    }

    /// <summary>An account that active payment methods settle to cannot be retired.</summary>
    public static void EnsureAccountMayRetire(MoneyRow row, IReadOnlyList<MoneyRow> methods)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(methods);
        var ms = methods.Where(m => m.Active && m.Text(SettlesTo) == row.Code).Select(m => m.Name).ToList();
        if (ms.Count > 0) throw MastersRules.Fail(MasterReasons.InUse, $"Active payment methods settle to {row.Name} ({string.Join(", ", ms)}) — retire or move them first.");
    }

    public static MoneyDraft ExpenseCategory(SaveExpenseCategoryRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var row = MastersRules.Validate(r.Code, r.Name, r.SortOrder);
        if (r.ApprovalLimitPaise is < 0 or > MaxMoneyPaise) throw MastersRules.Fail(MasterReasons.Invalid, "Manager approval limit: enter an amount in rupees (0 or more).", ApprovalLimit);
        // ACC-2 (contract §3.4): a capital category posts to a fixed-asset block (15x0) and only a capital category does; an RCM category needs its
        // rate; vehicles / buildings are ITC-blocked (rulebook §4.5). Same messages as the prototype money-master-specs.
        var ledgerCode = (r.Ledger ?? string.Empty).Trim();
        var asset = Hcc.Domain.Expenses.ExpensePurchase.IsAssetLedger(ledgerCode) && ChartOfAccounts.Find(ledgerCode) is not null;
        if (r.Capital && !asset)
            throw MastersRules.Fail(MasterReasons.Invalid, "A capital category posts to a fixed-asset block (15x0 — Buildings, Furniture, Plant & machinery, Computers, Vehicles).", Ledger);
        if (!r.Capital && asset)
            throw MastersRules.Fail(MasterReasons.Invalid, "A fixed-asset block is the ledger of a capital category only — turn on “Capital purchase” or choose an expense ledger.", Ledger);
        if (r.Rcm && r.RcmBps is not (> 0 and <= 2800)) throw MastersRules.Fail(MasterReasons.Invalid, "Enter the reverse-charge GST rate (e.g. 18 %; GTA 5 %).", RcmBps);
        var fields = new Dictionary<string, object?>
        {
            [Ledger] = asset ? ledgerCode : LedgerOf(r.Ledger, true, [AccountGroup.Expense]), [NeedsBill] = r.NeedsBill, [ApprovalLimit] = r.ApprovalLimitPaise,
            [ItcBlocked] = r.ItcBlocked || (r.Capital && Hcc.Domain.Expenses.ExpensePurchase.ItcBlockedAssetLedgers.Contains(ledgerCode)), [Rcm] = r.Rcm,
            [RcmBps] = r.Rcm ? r.RcmBps : null, [Capital] = r.Capital,
        };
        return new MoneyDraft(row, fields);
    }

    public static MoneyDraft ExpenseVendor(SaveExpenseVendorRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var row = MastersRules.Validate(r.Code, r.Name, r.SortOrder);
        var type = One(r.Type, MoneyMasterValues.VendorTypes, "Type", Type);
        var g = Up(r.Gstin);
        var p = Up(r.Pan);
        if (g is not null && !GstinPattern().IsMatch(g)) throw MastersRules.Fail(MasterReasons.Invalid, "GSTIN: 15 characters, e.g. 27ABCDE1234F1Z5.", Gstin);
        if (p is not null && !PanPattern().IsMatch(p)) throw MastersRules.Fail(MasterReasons.Invalid, "PAN: 5 letters, 4 digits, 1 letter (e.g. ABCDE1234F).", Pan);
        if (g is not null && p is not null && g.Substring(2, 10) != p) throw MastersRules.Fail(MasterReasons.Invalid, "The PAN inside the GSTIN (characters 3–12) does not match the PAN.", Pan);
        if (r.Tds && p is null) throw MastersRules.Fail(MasterReasons.Invalid, "Enter the PAN when TDS applies.", Pan);
        // ACC-2 (contract §3, §5): two-letter GST state; the TDS section only with TDS on.
        var st = Up(r.State);
        if (st is not null && (st.Length != 2 || !st.All(char.IsAsciiLetterUpper))) throw MastersRules.Fail(MasterReasons.Invalid, "GST state: two letters (e.g. MH, KA, GJ).", State);
        var sec = string.IsNullOrWhiteSpace(r.TdsSection) ? null : r.TdsSection.Trim().ToLowerInvariant();
        if (sec is not null && !Hcc.Domain.Expenses.ExpensePurchase.TdsSections.Contains(sec)) throw MastersRules.Fail(MasterReasons.Invalid, "Choose a TDS section from the list.", TdsSection);
        if (sec is not null && !r.Tds) throw MastersRules.Fail(MasterReasons.Invalid, "Turn on “TDS applies” to choose a TDS section.", TdsSection);
        var fields = new Dictionary<string, object?>
        {
            [Type] = type, [Gstin] = g, [Pan] = p, [Tds] = r.Tds, [State] = st, [TdsSection] = sec, [Contact] = Opt(r.Contact, 100, "Contact", Contact), [Terms] = Opt(r.Terms, 60, "Payment terms", Terms),
        };
        return new MoneyDraft(row, fields);
    }

    // ── Resolution of stored record values (pay.receipt.method, pos.bill.tender, ops.expense.method / category / vendor) ──

    /// <summary>Row of a stored value: its name, its code, an earlier name of the row (rename history), or its name ignoring case.</summary>
    public static MoneyRow? Resolve(IReadOnlyList<MoneyRow> rows, IReadOnlyDictionary<string, int> renames, string? value)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(renames);
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        return rows.FirstOrDefault(r => string.Equals(r.Name, t, StringComparison.Ordinal))
            ?? rows.FirstOrDefault(r => string.Equals(r.Code, t.ToUpperInvariant(), StringComparison.Ordinal))
            ?? (renames.TryGetValue(t, out var id) ? rows.FirstOrDefault(r => r.Id == id) : null)
            ?? rows.FirstOrDefault(r => string.Equals(r.Name, t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Records per row: the counts of each stored value summed onto the row the value resolves to.</summary>
    public static IReadOnlyDictionary<int, int> CountsByRow(IReadOnlyList<MoneyRow> rows, IReadOnlyDictionary<string, int> renames, IEnumerable<(string Value, int Count)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var m = new Dictionary<int, int>();
        foreach (var (v, n) in values)
            if (Resolve(rows, renames, v) is { } r) m[r.Id] = m.GetValueOrDefault(r.Id) + n;
        return m;
    }

    // ── Display (history values and audit text) ──

    /// <summary>A field value as shown to people (history old → new).</summary>
    public static string? Show(string field, object? v) => v switch
    {
        null => null,
        bool b => b ? "Yes" : "No",
        IReadOnlyList<string> l => l.Count == 0 ? null : string.Join(", ", l),
        long p when field is OpeningBalance or ApprovalLimit => "₹" + (p / 100m).ToString("#,##0.##", CultureInfo.InvariantCulture),
        int bps when field is SurchargePct or RcmBps => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    /// <summary>Changed fields (framework + master fields) between the stored row and the draft, as shown to people.</summary>
    public static IReadOnlyList<(string Field, string? Old, string? New)> Diff(string master, MoneyRow? old, MoneyDraft d, int sort)
    {
        ArgumentNullException.ThrowIfNull(d);
        var list = new List<(string, string?, string?)>(MastersRules.Diff(old is null ? null : new MastersRules.CleanRow(old.Code, old.Name, old.SortOrder), d.Row, sort));
        foreach (var f in FieldsOf(master))
        {
            var o = old is null ? null : Show(f, old[f]);
            var n = Show(f, d.Fields.GetValueOrDefault(f));
            if (old is null ? n is not null : !string.Equals(o, n, StringComparison.Ordinal)) list.Add((f, o, n));
        }

        return list;
    }
}
