using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Import;
using Hcc.Contracts.Accounting;
using Hcc.Contracts.Import;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

/// <summary>
/// The hand-entered opening entry as the reports see it (ACC-1 contract §5): its Journal voucher (<c>JRN/&lt;asOf&gt;/000</c>, source sub
/// <c>opening-entry</c>, dated the FY start, the 1400 balancing line included) and whether the Owner approved it. Null voucher = no entry.
/// </summary>
public sealed record ReportOpening(Voucher? Voucher, bool Approved);

/// <summary>
/// Read port for the opening entry (implemented over the acc1-opening lane's store by the integrator; absent = no entry, the imported
/// opening of <see cref="IOpeningBalanceSource"/> is used and the trial balance says "opening balances pending").
/// </summary>
public interface IReportOpeningSource
{
    Task<ReportOpening?> GetAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>
/// Read port for party display names (contract §3: <c>&lt;control&gt;/&lt;party id&gt;</c> → master row name), implemented over the
/// acc1-parties lane's party list by the integrator; absent = a party ledger is named "&lt;control name&gt; · &lt;party id&gt;".
/// </summary>
public interface IReportPartyNames
{
    Task<IReadOnlyDictionary<string, string>> GetAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>
/// ACC-1 reports over the vouchers (PERM-ACC-001; contract §6; rulebook §7.1–7.3, §G2): ledger / party statement with a running balance,
/// six-column trial balance (control summary or party detail) and the day book. Opening = every voucher before <c>from</c> — the lock
/// snapshot for a locked date, the live derivation elsewhere (<see cref="AccountingDays.RangeAsync"/>) — plus the opening entry
/// (<see cref="IReportOpeningSource"/>; an approved one supersedes the imported opening of <see cref="IOpeningBalanceSource"/>), an
/// opening voucher dated on <c>from</c> counting as opening (the FY's opening journal is the opening balance of a report from the FY start).
/// </summary>
public sealed class AccountingReportQueries(IAccountingStore store, IAccessPolicy policy, IOpeningBalanceSource? imported = null,
    IReportOpeningSource? openingEntry = null, IReportPartyNames? partyNames = null)
{
    /// <summary>Longest report range (days), as the voucher CSV.</summary>
    public const int MaxRangeDays = 366;

    public async Task<StatementView> StatementAsync(Caller caller, string account, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrWhiteSpace(account) || account.Length > 60) throw HccException.Validation("Choose a ledger or a party.");
        var data = await LoadAsync(caller, from, to, ct).ConfigureAwait(false);
        return AccountingReports.Statement(account.Trim(), from, to, data.Opening, data.Period, data.Names);
    }

    public async Task<TrialBalance6View> TrialBalanceAsync(Caller caller, DateOnly from, DateOnly to, bool detail, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var data = await LoadAsync(caller, from, to, ct).ConfigureAwait(false);
        return AccountingReports.TrialBalance6(from, to, detail, data.Opening, data.Period, data.Names, AccountingReports.OpeningPending(data.Entry));
    }

    public async Task<DayBookView> DayBookAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var data = await LoadAsync(caller, from, to, ct).ConfigureAwait(false);
        return AccountingReports.DayBook(from, to, data.Period, data.Names);
    }

    private sealed record Loaded(IReadOnlyList<Voucher> Opening, IReadOnlyList<Voucher> Period, LedgerNames Names, ReportOpening? Entry);

    private async Task<Loaded> LoadAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        if (from > to) throw HccException.Validation("Choose a valid range: \"from\" on or before \"to\".");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays) throw HccException.Validation($"Choose at most {MaxRangeDays} days.");
        var days = await AccountingDays.RangeAsync(store, caller.PropertyId, DateOnly.MinValue, to, ct).ConfigureAwait(false);
        var all = days.SelectMany(d => d.Vouchers).ToList();
        var entry = openingEntry is null ? null : await openingEntry.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var extra = new Dictionary<string, (string Name, string Group)>(StringComparer.Ordinal);
        if (entry?.Voucher is { } ov) all.Add(ov);
        else if (imported is not null && AccountingReports.ImportedVoucher(await imported.GetAsync(caller.PropertyId, ct).ConfigureAwait(false), extra) is { } iv) all.Add(iv);
        var parties = partyNames is null ? null : await partyNames.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var (opening, period) = AccountingReports.Split(all, from, to);
        var chart = await store.ChartAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        return new Loaded(opening, period, new LedgerNames(parties, extra, chart), entry);
    }
}

/// <summary>Ledger display names and groups: chart names for codes, "&lt;control name&gt; · &lt;party name&gt;" for party ledgers, imported
/// ledgers outside the chart by their own name and group.</summary>
public sealed class LedgerNames(IReadOnlyDictionary<string, string>? parties = null, IReadOnlyDictionary<string, (string Name, string Group)>? extra = null,
    EffectiveChart? chart = null)
{
    public static readonly LedgerNames Chart = new();

    // ACC-1 (integration): the property's chart (Owner renames / Owner-added ledgers); null = the built-ins.
    private LedgerAccount? Ledger(string code) => (chart ?? ChartOfAccounts.Builtin).Find(code);

    private string CodeName(string code) => Ledger(code)?.Name ?? (extra is not null && extra.TryGetValue(code, out var x) ? x.Name : code);

    public string Name(string account)
    {
        var party = AccountingReports.PartyOf(account);
        if (party is null) return CodeName(account);
        var shown = parties is not null && parties.TryGetValue(account, out var n) ? n : party;
        return $"{CodeName(AccountingReports.ControlOf(account))} · {shown}";
    }

    /// <summary>Group of a ledger (a party ledger takes its control's group).</summary>
    public string Group(string account)
    {
        var code = AccountingReports.ControlOf(account);
        // ACC-1 (integration): the Tally group id of the chart (contract §1), as the prototype's accRptLedgerGroup.
        return Ledger(code)?.GroupId ?? (extra is not null && extra.TryGetValue(code, out var x) ? x.Group : nameof(AccountGroup.Suspense));
    }

    /// <summary>Party name alone (detail ordering).</summary>
    public string PartyName(string account) => parties is not null && parties.TryGetValue(account, out var n) ? n : AccountingReports.PartyOf(account) ?? account;
}

/// <summary>Pure report builders (prototype accounting-reports.ts; contract §6). Money = paise; signed balances Dr positive / Cr negative.</summary>
public static class AccountingReports
{
    private static readonly HashSet<string> OpeningSubs = new(["opening-entry", "opening-import", "opening"], StringComparer.Ordinal);

    public static string ControlOf(string account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var i = account.IndexOf('/', StringComparison.Ordinal);
        return i < 0 ? account : account[..i];
    }

    public static string? PartyOf(string account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var i = account.IndexOf('/', StringComparison.Ordinal);
        return i < 0 ? null : account[(i + 1)..];
    }

    /// <summary>A control (or any plain code) takes its party ledgers too; a party ledger only itself.</summary>
    public static bool Matches(string account, string entry)
        => string.Equals(entry, account, StringComparison.Ordinal)
           || (!account.Contains('/', StringComparison.Ordinal) && entry.StartsWith(account + "/", StringComparison.Ordinal));

    /// <summary>The hand-entered opening entry, the imported opening, or the balance before a window.</summary>
    public static bool IsOpening(Voucher v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return OpeningSubs.Contains(v.Sub) || string.Equals(v.SourceKind, "opening", StringComparison.Ordinal);
    }

    /// <summary>Vouchers before the range (an opening voucher dated on <paramref name="from"/> included) and in it, each in date, number order.</summary>
    public static (IReadOnlyList<Voucher> Opening, IReadOnlyList<Voucher> Period) Split(IEnumerable<Voucher> vouchers, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        var opening = new List<Voucher>();
        var period = new List<Voucher>();
        foreach (var v in vouchers)
        {
            if (v.Date < from || (v.Date == from && IsOpening(v))) opening.Add(v);
            else if (v.Date <= to) period.Add(v);
        }

        return (Ordered(opening), Ordered(period));
    }

    private static List<Voucher> Ordered(List<Voucher> vs) => [.. vs.OrderBy(v => v.Date).ThenBy(v => v.Number, StringComparer.Ordinal)];

    /// <summary>Rulebook §G2: pending while no approved opening entry exists or its 1400 Opening-balance difference is not zero.</summary>
    public static bool OpeningPending(ReportOpening? entry)
    {
        if (entry?.Voucher is not { } v || !entry.Approved) return true;
        return v.Entries.Where(e => e.Account == "1400").Sum(e => e.DebitPaise - e.CreditPaise) != 0;
    }

    /// <summary>The imported opening trial balance (N5) as one Journal dated its "as of" date (none = before everything): a ledger mapped to an
    /// HCC account posts to it, an unmapped one to its own code (name and group recorded in <paramref name="extra"/>).</summary>
    public static Voucher? ImportedVoucher(OpeningBalancesView? ob, IDictionary<string, (string Name, string Group)> extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        if (ob is null || ob.Rows.Count == 0) return null;
        var entries = new List<VoucherEntry>();
        foreach (var o in ob.Rows)
        {
            var chart = ChartOfAccounts.Find(o.HccAccount);
            var mapped = chart is not null && chart.Group != AccountGroup.Suspense;
            var code = mapped ? chart!.Code : o.LedgerCode;
            if (!mapped) extra[code] = (o.LedgerName, o.Group);
            if (o.DebitPaise != 0 || o.CreditPaise != 0) entries.Add(new VoucherEntry(code, o.DebitPaise, o.CreditPaise));
        }

        var date = ob.AsOfDate ?? DateOnly.MinValue;
        return entries.Count == 0 ? null
            : new Voucher("OPENING-IMPORT", date, VoucherType.Journal, $"Imported opening balances as of {Iso(date)}", "OPENING-IMPORT", "opening", "opening-import", entries, []);
    }

    public static StatementView Statement(string account, DateOnly from, DateOnly to, IEnumerable<Voucher> opening, IEnumerable<Voucher> period, LedgerNames? names = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(period);
        names ??= LedgerNames.Chart;
        long open = 0;
        foreach (var v in opening)
            foreach (var e in v.Entries.Where(e => Matches(account, e.Account)))
                open = checked(open + e.DebitPaise - e.CreditPaise);
        var rows = new List<StatementRowView>();
        long running = open, debit = 0, credit = 0;
        foreach (var v in period)
        {
            var mine = v.Entries.Where(e => Matches(account, e.Account)).ToList();
            if (mine.Count == 0) continue;
            var d = mine.Sum(e => e.DebitPaise);
            var c = mine.Sum(e => e.CreditPaise);
            if (d == 0 && c == 0) continue;
            var others = v.Entries.Where(e => !Matches(account, e.Account)).ToList();
            var counter = string.Join(", ", (others.Count > 0 ? others : mine).Select(e => names.Name(e.Account)).Distinct(StringComparer.Ordinal));
            running = checked(running + d - c);
            debit = checked(debit + d);
            credit = checked(credit + c);
            rows.Add(new StatementRowView(v.Date, v.Number, v.Type.ToString(), v.Narration, counter, d, c, running));
        }

        return new StatementView(account, names.Name(account), from, to, open, debit, credit, checked(open + debit - credit), rows);
    }

    /// <summary>
    /// Six-column trial balance. Summary (<paramref name="detail"/> false): parties summed into their control. Detail: a row per party ledger
    /// (<c>Control</c> set) after its control's own row (present only when something posted to the control itself). Opening and closing are
    /// netted per row; period columns are gross; rows with nothing in any column are left out; chart order, parties by name.
    /// </summary>
    public static TrialBalance6View TrialBalance6(DateOnly from, DateOnly to, bool detail, IEnumerable<Voucher> opening, IEnumerable<Voucher> period, LedgerNames? names = null,
        bool openingPending = true)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(period);
        names ??= LedgerNames.Chart;
        var m = new Dictionary<string, long[]>(StringComparer.Ordinal); // [opening Dr, opening Cr, period Dr, period Cr]
        void Add(string account, int k, long d, long c)
        {
            var id = detail ? account : ControlOf(account);
            if (!m.TryGetValue(id, out var x)) m[id] = x = new long[4];
            x[k] = checked(x[k] + d);
            x[k + 1] = checked(x[k + 1] + c);
        }

        foreach (var v in opening) foreach (var e in v.Entries) Add(e.Account, 0, e.DebitPaise, e.CreditPaise);
        foreach (var v in period) foreach (var e in v.Entries) Add(e.Account, 2, e.DebitPaise, e.CreditPaise);
        var rows = new List<TrialBalance6RowView>();
        foreach (var id in m.Keys.OrderBy(k => ChartOfAccounts.OrderOf(ControlOf(k))).ThenBy(ControlOf, StringComparer.Ordinal)
                     .ThenBy(k => PartyOf(k) is null ? 0 : 1).ThenBy(k => names.PartyName(k), StringComparer.OrdinalIgnoreCase).ThenBy(k => k, StringComparer.Ordinal))
        {
            var x = m[id];
            var o = x[0] - x[1];
            var c = checked(o + x[2] - x[3]);
            if (o == 0 && x[2] == 0 && x[3] == 0 && c == 0) continue;
            var party = PartyOf(id) is not null;
            rows.Add(new TrialBalance6RowView(id, names.Name(id), names.Group(id), party ? ControlOf(id) : null,
                Math.Max(o, 0), Math.Max(-o, 0), x[2], x[3], Math.Max(c, 0), Math.Max(-c, 0)));
        }

        var t = new TrialBalance6TotalsView(rows.Sum(r => r.OpeningDebitPaise), rows.Sum(r => r.OpeningCreditPaise), rows.Sum(r => r.DebitPaise), rows.Sum(r => r.CreditPaise),
            rows.Sum(r => r.ClosingDebitPaise), rows.Sum(r => r.ClosingCreditPaise));
        var balanced = t.OpeningDebitPaise == t.OpeningCreditPaise && t.DebitPaise == t.CreditPaise && t.ClosingDebitPaise == t.ClosingCreditPaise;
        return new TrialBalance6View(from, to, detail, rows, t, balanced, openingPending);
    }

    /// <summary>Day book: every period voucher in date, number order, one row per entry.</summary>
    public static DayBookView DayBook(DateOnly from, DateOnly to, IReadOnlyList<Voucher> period, LedgerNames? names = null)
    {
        ArgumentNullException.ThrowIfNull(period);
        names ??= LedgerNames.Chart;
        var rows = new List<DayBookRowView>();
        long d = 0, c = 0;
        foreach (var v in period)
            foreach (var e in v.Entries)
            {
                rows.Add(new DayBookRowView(v.Date, v.Number, v.Type.ToString(), v.Narration, e.Account, names.Name(e.Account), e.DebitPaise, e.CreditPaise, v.SourceKey));
                d = checked(d + e.DebitPaise);
                c = checked(c + e.CreditPaise);
            }

        return new DayBookView(from, to, rows, period.Count, d, c, d == c);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
