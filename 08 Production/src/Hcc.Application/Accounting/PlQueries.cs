using Hcc.Application.Access;
using Hcc.Application.Import;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;

namespace Hcc.Application.Accounting;

/// <summary>
/// ACC-3 lane acc3-pl: profit &amp; loss, balance sheet and cash-ratio report (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §1, §2, §9; rulebook §7.4,
/// §7.5, §7.9). Reads the postings only, the same way as <see cref="AccountingReportQueries"/> (lock snapshots for locked dates, live elsewhere,
/// the opening entry or the imported opening, the month-balance opening when composed). PERM-ACC-003 view as the trial balance.
/// </summary>
public sealed class PlQueries(IAccountingStore store, IAccessPolicy policy, IOpeningBalanceSource? imported = null,
    IReportOpeningSource? openingEntry = null, IReportPartyNames? partyNames = null)
{
    public async Task<PlView> PlAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (from > to) throw HccException.Validation("Choose a valid range: \"from\" on or before \"to\".");
        var d = await LoadAsync(caller, to, ct).ConfigureAwait(false);
        var (_, period) = AccountingReports.Split(d.All, from, to);
        return PlReports.Pl(from, to, period, d.Chart, d.Names, d.Pending || PlReports.MixedLocks(d.Locks, from, to));
    }

    public async Task<BalanceSheetView> BalanceSheetAsync(Caller caller, DateOnly asOf, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var d = await LoadAsync(caller, asOf, ct).ConfigureAwait(false);
        return PlReports.BalanceSheet(asOf, d.All, d.Chart, d.Names, d.Pending || PlReports.MixedLocks(d.Locks, DateOnly.MinValue, asOf));
    }

    public async Task<CashRatioView> CashRatioAsync(Caller caller, string fy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var (from, to) = PlReports.FyRange(fy);
        var d = await LoadAsync(caller, to, ct).ConfigureAwait(false);
        var (_, period) = AccountingReports.Split(d.All, from, to);
        return PlReports.CashRatio(PlReports.FyLabel(from), period, d.Chart);
    }

    private sealed record Loaded(List<Voucher> All, EffectiveChart Chart, LedgerNames Names, bool Pending, IReadOnlyList<(DateOnly Date, bool Locked)> Locks);

    private async Task<Loaded> LoadAsync(Caller caller, DateOnly to, CancellationToken ct)
    {
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var days = await AccountingDays.RangeWithLocksAsync(store, caller.PropertyId, DateOnly.MinValue, to, ct).ConfigureAwait(false);
        var all = days.SelectMany(x => x.Vouchers).ToList();
        var entry = openingEntry is null ? null : await openingEntry.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var extra = new Dictionary<string, (string Name, string Group)>(StringComparer.Ordinal);
        if (entry?.Voucher is { } ov) all.Add(ov);
        else if (imported is not null && AccountingReports.ImportedVoucher(await imported.GetAsync(caller.PropertyId, ct).ConfigureAwait(false), extra) is { } iv) all.Add(iv);
        var parties = partyNames is null ? null : await partyNames.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var chart = await store.ChartAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        return new Loaded(all, chart, new LedgerNames(parties, extra, chart), AccountingReports.OpeningPending(entry),
            days.Select(x => (x.Date, x.Locked)).ToList());
    }
}

/// <summary>Pure P&amp;L / balance sheet / cash-ratio builders — the twin of the prototype's accounting-pl.ts (same buckets, signs and rules).</summary>
public static class PlReports
{
    public static readonly IReadOnlyList<string> Revenue = ["4000", "4100", "4200", "4300", "4900", "4910"];
    public static readonly IReadOnlyList<string> OtherIncome = ["4950", "4960"];
    public const int CashRatioLimitBps = 500;

    private static readonly Dictionary<string, string> Bucket = new(StringComparer.Ordinal)
    {
        ["5000"] = "consumption", ["5010"] = "consumption", ["5015"] = "consumption", ["5100"] = "employee", ["5110"] = "employee", ["5120"] = "employee",
        ["5080"] = "finance", ["5090"] = "finance", ["5200"] = "depreciation", ["5300"] = "other", ["5400"] = "other", ["5900"] = "other",
    };

    private static readonly (string Key, string Name)[] Buckets =
        [("consumption", "Cost of materials consumed"), ("employee", "Employee benefits expense"), ("finance", "Finance costs"), ("depreciation", "Depreciation"), ("other", "Other expenses")];

    private static readonly string[] TallyOrder = ["sales", "direct-income", "indirect-income", "purchases", "direct-expenses", "indirect-expenses"];

    private static readonly HashSet<string> Duties = new(["2100", "2110", "2120", "2130", "2140", "2150", "2310", "2320", "2330", "1300", "1310", "1320", "1330", "1350"], StringComparer.Ordinal);
    private static readonly HashSet<string> Cash = new(["1000", "1005"], StringComparer.Ordinal);
    private static readonly HashSet<string> Money = new(["1000", "1005", "1010", "1020", "1030"], StringComparer.Ordinal);

    private static bool InRange(string c, string lo, string hi) => string.CompareOrdinal(c, lo) >= 0 && string.CompareOrdinal(c, hi) <= 0;

    /// <summary>Primary group of a ledger code; legacy groups of unknown codes mapped to a primary.</summary>
    public static LedgerGroup Root(EffectiveChart chart, LedgerNames names, string code)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(names);
        var gid = chart.Find(code)?.GroupId ?? names.Group(code);
        if (chart.RootOf(gid) is { } r) return r;
        var legacy = gid switch { "Income" => "indirect-income", "Expense" => "indirect-expenses", "Asset" => "current-assets", "Liability" => "current-liabilities", _ => "suspense" };
        return chart.RootOf(legacy) ?? ChartOfAccounts.GroupOf("suspense")!;
    }

    /// <summary>Contract §1: the active dates of the range mix locked and open days.</summary>
    public static bool MixedLocks(IEnumerable<(DateOnly Date, bool Locked)> days, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(days);
        var inRange = days.Where(d => d.Date >= from && d.Date <= to).ToList();
        return inRange.Any(d => d.Locked) && inRange.Any(d => !d.Locked);
    }

    public static PlView Pl(DateOnly from, DateOnly to, IEnumerable<Voucher> period, EffectiveChart? chart = null, LedgerNames? names = null, bool provisional = false)
    {
        ArgumentNullException.ThrowIfNull(period);
        chart ??= ChartOfAccounts.Builtin;
        names ??= new LedgerNames(chart: chart);
        var mv = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in period.SelectMany(v => v.Entries))
        {
            var c = AccountingReports.ControlOf(e.Account);
            mv[c] = checked(mv.GetValueOrDefault(c) + e.DebitPaise - e.CreditPaise);
        }

        var warnings = new List<string>();
        var rev = new List<PlLineView>();
        var oth = new List<PlLineView>();
        var ex = Buckets.ToDictionary(b => b.Key, _ => new List<PlLineView>(), StringComparer.Ordinal);
        var tally = new Dictionary<string, (string Name, long Paise)>(StringComparer.Ordinal);
        foreach (var (code, dr) in mv)
        {
            if (dr == 0) continue;
            var root = Root(chart, names, code);
            var isRev = Revenue.Contains(code);
            var isOth = OtherIncome.Contains(code);
            var bucket = Bucket.TryGetValue(code, out var b) ? b : InRange(code, "5020", "5070") ? "other" : null;
            var known = isRev || isOth || bucket is not null;
            if (!known && root.Side != "PL") continue;
            var name = names.Name(code);
            var income = isRev || isOth || (!known && root.Nature == "Cr");
            var line = new PlLineView(code, name, income ? -dr : dr, names.Group(code));
            if (isRev) rev.Add(line);
            else if (isOth) oth.Add(line);
            else if (income) (root.Id == "indirect-income" ? oth : rev).Add(line);
            else
            {
                if (bucket is null)
                {
                    if (root.Id is "direct-expenses" or "purchases") bucket = "consumption";
                    else
                    {
                        bucket = "other";
                        if (root.Id != "indirect-expenses") warnings.Add($"{code} {name}: no P&L bucket — shown under other expenses");
                    }
                }

                ex[bucket].Add(line);
            }

            var t = tally.GetValueOrDefault(root.Id, (root.Name, 0L));
            tally[root.Id] = (root.Name, checked(t.Item2 + (root.Nature == "Cr" ? -dr : dr)));
        }

        static List<PlLineView> Sorted(List<PlLineView> ls) => [.. ls.OrderBy(l => l.Code, StringComparer.Ordinal)];
        PlGroupView G(string k) { var ls = Sorted(ex[k]); return new PlGroupView(Buckets.Single(x => x.Key == k).Name, ls, ls.Sum(l => l.Paise)); }
        var expenses = new PlExpensesView(G("consumption"), G("employee"), G("finance"), G("depreciation"), G("other"), 0);
        expenses = expenses with { Total = expenses.Consumption.Total + expenses.Employee.Total + expenses.Finance.Total + expenses.Depreciation.Total + expenses.Other.Total };
        var revenue = new PlSectionView(Sorted(rev), rev.Sum(l => l.Paise));
        var other = new PlSectionView(Sorted(oth), oth.Sum(l => l.Paise));
        int Ord(string g) { var i = Array.IndexOf(TallyOrder, g); return i < 0 ? 99 : i; }
        var byTally = tally.OrderBy(x => Ord(x.Key)).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => new PlTallyGroupView(x.Key, x.Value.Name, x.Value.Paise)).ToList();
        return new PlView(from, to, provisional, revenue, other, expenses, revenue.Total + other.Total - expenses.Total, byTally, warnings.Count > 0 ? warnings : null);
    }

    public static DateOnly FyStart(DateOnly d) => new(d.Month >= 4 ? d.Year : d.Year - 1, 4, 1);

    public static string FyLabel(DateOnly start) => $"{start.Year}-{(start.Year + 1) % 100:00}";

    public static (DateOnly From, DateOnly To) FyRange(string fy)
    {
        if (string.IsNullOrWhiteSpace(fy) || fy.Length < 4 || !int.TryParse(fy.AsSpan(0, 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var y)
            || y < 2000 || y > 2100)
            throw HccException.Validation("Choose a financial year such as 2026-27.");
        return (new DateOnly(y, 4, 1), new DateOnly(y + 1, 3, 31));
    }

    private enum Place { Owners, Ncl, Cl, Nca, Ca }

    private static Place PlaceOf(EffectiveChart chart, LedgerNames names, string code)
    {
        if (code is "3000" or "3100") return Place.Owners;
        if (code is "2400" or "2410") return Place.Ncl;
        if (InRange(code, "1500", "1599")) return Place.Nca;
        if (code is "2000" or "2200" or "2300" or "2340") return Place.Cl;
        return Root(chart, names, code).Id switch
        {
            "capital" => Place.Owners, "loans" => Place.Ncl, "current-liabilities" => Place.Cl, "fixed-assets" or "investments" => Place.Nca, _ => Place.Ca,
        };
    }

    /// <summary>Balance sheet over every voucher up to <paramref name="asOf"/> (see accounting-pl.ts accBalanceSheet for the rules).</summary>
    public static BalanceSheetView BalanceSheet(DateOnly asOf, IEnumerable<Voucher> vouchers, EffectiveChart? chart = null, LedgerNames? names = null, bool provisional = false)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        chart ??= ChartOfAccounts.Builtin;
        names ??= new LedgerNames(chart: chart);
        var upTo = vouchers.Where(v => v.Date <= asOf).ToList();
        var bal = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in upTo.SelectMany(v => v.Entries))
        {
            var c = AccountingReports.ControlOf(e.Account);
            bal[c] = checked(bal.GetValueOrDefault(c) + e.DebitPaise - e.CreditPaise);
        }

        var fy = FyStart(asOf);
        var (_, period) = AccountingReports.Split(upTo, fy, asOf);
        var profit = Pl(fy, asOf, period, chart, names).ProfitBeforeTax;
        var blocks = Enum.GetValues<Place>().ToDictionary(p => p, _ => new List<BsLineView>());
        static bool Liab(Place p) => p is Place.Owners or Place.Ncl or Place.Cl;
        long plLedgers = 0, duties = 0;
        var dutyLines = new List<BsLineView>();
        foreach (var (code, b) in bal)
        {
            if (b == 0) continue;
            if (Root(chart, names, code).Side == "PL" || code[0] is '4' or '5') { plLedgers += b; continue; }
            var name = names.Name(code);
            if (Duties.Contains(code)) { duties += b; dutyLines.Add(new BsLineView(code, name, -b)); continue; }
            var p = PlaceOf(chart, names, code);
            var paise = Liab(p) ? -b : b;
            if (paise < 0 && p != Place.Owners) { p = Liab(p) ? Place.Ca : Place.Cl; paise = -paise; }
            blocks[p].Add(new BsLineView(code, name, paise));
        }

        if (duties < 0) blocks[Place.Cl].AddRange(dutyLines);
        else if (duties > 0) blocks[Place.Ca].Add(new BsLineView("1300", "GST input credit", duties));
        blocks[Place.Owners].Add(new BsLineView("PL", "Profit for the year to date", profit));
        var earlier = -plLedgers - profit;
        if (earlier != 0) blocks[Place.Owners].Add(new BsLineView("PL-EARLIER", "Profit & loss of earlier years (not closed)", earlier));
        BsBlockView B(Place p, string n) { var ls = blocks[p].OrderBy(l => l.Code, StringComparer.Ordinal).ToList(); return new BsBlockView(n, ls, ls.Sum(l => l.Paise)); }
        var (ow, ncl, cl, nca, ca) = (B(Place.Owners, "Owner's funds"), B(Place.Ncl, "Non-current liabilities"), B(Place.Cl, "Current liabilities"),
            B(Place.Nca, "Non-current assets"), B(Place.Ca, "Current assets"));
        var lt = ow.Total + ncl.Total + cl.Total;
        var at = nca.Total + ca.Total;
        var suspense = bal.GetValueOrDefault(ChartOfAccounts.Suspense);
        var openingDiff = bal.GetValueOrDefault(ChartOfAccounts.OpeningDifference);
        return new BalanceSheetView(asOf, provisional || suspense != 0 || openingDiff != 0, new BsLiabilitiesView(ow, ncl, cl, lt), new BsAssetsView(nca, ca, at),
            lt == at, suspense, openingDiff);
    }

    /// <summary>Cash ratio over the FY's Receipt / Payment vouchers: money side only, cash = 1000 / 1005; basis points; flagged above 500.</summary>
    public static CashRatioView CashRatio(string fy, IEnumerable<Voucher> period, EffectiveChart? chart = null)
    {
        ArgumentNullException.ThrowIfNull(period);
        chart ??= ChartOfAccounts.Builtin;
        long cr = 0, tr = 0, cp = 0, tp = 0;
        foreach (var v in period)
        {
            if (v.Type is not (VoucherType.Receipt or VoucherType.Payment)) continue;
            foreach (var e in v.Entries)
            {
                var c = AccountingReports.ControlOf(e.Account);
                if (!Money.Contains(c) && chart.Find(c)?.GroupId is not ("cash" or "bank")) continue;
                if (v.Type == VoucherType.Receipt) { tr += e.DebitPaise; if (Cash.Contains(c)) cr += e.DebitPaise; }
                else { tp += e.CreditPaise; if (Cash.Contains(c)) cp += e.CreditPaise; }
            }
        }

        static int Bps(long a, long b) => b == 0 ? 0 : (int)Math.Round(a * 10000m / b, MidpointRounding.AwayFromZero);
        var rp = Bps(cr, tr);
        var pp = Bps(cp, tp);
        return new CashRatioView(fy, cr, tr, cp, tp, rp, pp, rp > CashRatioLimitBps || pp > CashRatioLimitBps);
    }
}
