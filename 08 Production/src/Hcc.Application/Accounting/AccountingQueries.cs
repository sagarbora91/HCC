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
/// Accounting reads (PERM-ACC-001): day vouchers + exceptions + reconciliation, trial balance (day or cumulative), mapping rows,
/// chart, histories; exports: generic CSV (PERM-ACC-005) and simulated Tally XML (PERM-ACC-006, ≤ 31 days).
/// N5: the cumulative trial balance reads the imported opening trial balance through the Import module's read port
/// (<see cref="IOpeningBalanceSource"/>; null = no Import module composed, nothing added).
/// </summary>
public sealed class AccountingQueries(IAccountingStore store, IAccessPolicy policy, IOpeningBalanceSource? opening = null)
{
    public const int CsvMaxDays = 366;

    private Task RequireAsync(Caller c, string perm, CancellationToken ct) => policy.RequireAsync(c.PropertyId, c.PrincipalId, perm, ct: ct);

    public async Task<AccountingDayView> DayAsync(Caller caller, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.View, ct).ConfigureAwait(false);
        return await DayViewAsync(caller, date, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// W4-7: every business date in [from, to] with vouchers or a lock, plus <paramref name="to"/> itself (the business date the
    /// screen opens on), each evaluated exactly as <see cref="DayAsync"/> — one request for the production accounting screens.
    /// At most <see cref="DaysMaxRange"/> days.
    /// </summary>
    public async Task<IReadOnlyList<AccountingDayView>> DaysAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.View, ct).ConfigureAwait(false);
        if (from > to) throw HccException.Validation("Choose a valid range: \"from\" on or before \"to\".");
        if (to.DayNumber - from.DayNumber + 1 > DaysMaxRange) throw HccException.Validation($"Choose at most {DaysMaxRange} days.");
        var active = await AccountingDays.RangeAsync(store, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var dates = active.Select(d => d.Date).Append(to).Distinct().Order().ToList();
        var list = new List<AccountingDayView>(dates.Count);
        foreach (var d in dates) list.Add(await DayViewAsync(caller, d, ct).ConfigureAwait(false));
        return list;
    }

    public const int DaysMaxRange = 62;

    private async Task<AccountingDayView> DayViewAsync(Caller caller, DateOnly date, CancellationToken ct)
    {
        var day = await AccountingDays.EvaluateAsync(store, null, caller.PropertyId, date, ct).ConfigureAwait(false);
        var figures = await store.OperationalFiguresAsync(null, caller.PropertyId, date, ct).ConfigureAwait(false);
        var (debit, credit) = VoucherBuilder.Totals(day.Shown);
        var lockInfo = day.Lock is { } l ? new LockInfoView(l.LockId, l.LockedUtc, l.LockedBy, l.Version) : null;
        return new AccountingDayView(date, day.Status.ToString(), day.Lock is null, day.Shown.Count, debit, credit, debit == credit,
            day.Issues.Count(i => i.Severity == "error"), day.Issues.Count(i => i.Severity == "warning"), lockInfo,
            day.Shown.Select(ToView).ToList(),
            day.Issues.Select(i => new AccountingIssueView(i.Id, i.Severity, i.Kind, i.Title, i.Detail, i.Voucher, i.Source, i.Key)).ToList(),
            Reconciliation.Check(day.Shown, day.Mapping, figures).Select(c => new ReconciliationView(c.Id, c.Label, c.ReportPaise, c.LedgerPaise, c.Ok, c.Note)).ToList());
    }

    public async Task<TrialBalanceView> TrialBalanceAsync(Caller caller, DateOnly date, bool cumulative, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.View, ct).ConfigureAwait(false);
        var days = await AccountingDays.RangeAsync(store, caller.PropertyId, cumulative ? DateOnly.MinValue : date, date, ct).ConfigureAwait(false);
        var vouchers = days.SelectMany(d => d.Vouchers).ToList();
        var rows = VoucherBuilder.TrialBalance(vouchers)
            .Select(r => new TrialBalanceRowView(r.Account, r.Name, r.Group.ToString(), r.DebitPaise, r.CreditPaise, r.BalancePaise)).ToList();
        var (d, c) = VoucherBuilder.Totals(vouchers);
        if (!cumulative || opening is null) return new TrialBalanceView(date, cumulative, d, c, d == c, rows);
        var ob = await opening.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var open = OpeningRows(ob);
        var included = open.Count > 0 && (ob.AsOfDate is null || date >= ob.AsOfDate.Value);
        if (included)
        {
            rows = MergeRows(rows, open);
            d = checked(d + open.Sum(r => r.DebitPaise));
            c = checked(c + open.Sum(r => r.CreditPaise));
        }

        return new TrialBalanceView(date, cumulative, d, c, d == c, rows, ob.AsOfDate, included, open);
    }

    /// <summary>Imported opening rows as trial-balance rows: a ledger mapped to an HCC account adds to that account (chart name and group),
    /// an unmapped ledger keeps its own code, name and group (e.g. Equity). Chart order, other codes after it.</summary>
    private static List<TrialBalanceRowView> OpeningRows(OpeningBalancesView ob)
    {
        var m = new Dictionary<string, TrialBalanceRowView>(StringComparer.Ordinal);
        foreach (var o in ob.Rows)
        {
            var chart = ChartOfAccounts.Find(o.HccAccount);
            var mapped = chart is not null && chart.Group != AccountGroup.Suspense;
            var code = mapped ? chart!.Code : o.LedgerCode;
            var row = m.GetValueOrDefault(code) ?? new TrialBalanceRowView(code, mapped ? chart!.Name : o.LedgerName, mapped ? chart!.Group.ToString() : o.Group, 0, 0, 0);
            m[code] = Row(row, o.DebitPaise, o.CreditPaise);
        }

        return Ordered(m.Values);
    }

    private static TrialBalanceRowView Row(TrialBalanceRowView r, long debit, long credit)
    {
        var dr = checked(r.DebitPaise + debit);
        var cr = checked(r.CreditPaise + credit);
        return r with { DebitPaise = dr, CreditPaise = cr, BalancePaise = checked(dr - cr) };
    }

    private static List<TrialBalanceRowView> MergeRows(List<TrialBalanceRowView> rows, List<TrialBalanceRowView> open)
    {
        var m = rows.ToDictionary(r => r.Account, StringComparer.Ordinal);
        foreach (var o in open) m[o.Account] = m.TryGetValue(o.Account, out var r) ? Row(r, o.DebitPaise, o.CreditPaise) : o;
        return Ordered(m.Values);
    }

    private static List<TrialBalanceRowView> Ordered(IEnumerable<TrialBalanceRowView> rows)
        => rows.OrderBy(r => ChartOfAccounts.OrderOf(r.Account)).ThenBy(r => r.Account, StringComparer.Ordinal).ToList();

    public async Task<IReadOnlyList<MappingRowView>> MappingAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.View, ct).ConfigureAwait(false);
        var map = ChartOfAccounts.Effective(await store.MappingOverridesAsync(null, caller.PropertyId, ct).ConfigureAwait(false));
        var uses = await store.KeyUsesAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        return ChartOfAccounts.DefaultMapping.Keys.Concat(uses.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(ChartOfAccounts.KeyGroupOrder).ThenBy(k => k, StringComparer.Ordinal)
            .Select(k =>
            {
                var account = map.GetValueOrDefault(k);
                var def = ChartOfAccounts.DefaultMapping.GetValueOrDefault(k);
                return new MappingRowView(k, ChartOfAccounts.KeyGroup(k), ChartOfAccounts.KeyValue(k), account, def, account is not null && account != def,
                    uses.GetValueOrDefault(k), ChartOfAccounts.Resolve(map, k) is not null);
            }).ToList();
    }

    public async Task<IReadOnlyList<AccountView>> ChartAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.View, ct).ConfigureAwait(false);
        return ChartOfAccounts.All.Select(a => new AccountView(a.Code, a.Name, a.Group.ToString(), a.Note)).ToList();
    }

    public async Task<IReadOnlyList<MappingChangeView>> MappingHistoryAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.View, ct).ConfigureAwait(false);
        return (await store.MappingHistoryAsync(caller.PropertyId, ct).ConfigureAwait(false))
            .Select(m => new MappingChangeView(m.ChangeId, m.AtUtc, m.Actor, m.MapKey, m.Before, m.After)).ToList();
    }

    public async Task<IReadOnlyList<LockEventView>> LockHistoryAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.View, ct).ConfigureAwait(false);
        return (await store.LockHistoryAsync(caller.PropertyId, ct).ConfigureAwait(false))
            .Select(e => new LockEventView(e.LockId, e.BusinessDate, e.Action, e.AtUtc, e.Actor, e.Reason)).ToList();
    }

    /// <summary>Generic voucher CSV (spec §9). File name <c>HCC-vouchers-&lt;from&gt;_&lt;to&gt;.csv</c>.</summary>
    // ETP-REUSE: Excel (.xlsx) voucher export through the ETP exporters when V:\ETP is available.
    public async Task<(string FileName, string Csv)> CsvAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.Export, ct).ConfigureAwait(false);
        if (from > to) throw HccException.Validation("Choose a valid range: \"from\" on or before \"to\".");
        if (to.DayNumber - from.DayNumber + 1 > CsvMaxDays) throw HccException.Validation($"Choose at most {CsvMaxDays} days.");
        var days = await AccountingDays.RangeAsync(store, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        return ($"HCC-vouchers-{Iso(from)}_{Iso(to)}.csv", VoucherExport.Csv(days.SelectMany(d => d.Vouchers)));
    }

    /// <summary>Simulated Tally XML for ≤ 31 days. Nothing is sent to Tally; only Tally's own import confirms an import.</summary>
    // ETP-REUSE: ETP Tally preparation (ledger/voucher-type masters, company GUID, import report parsing) when V:\ETP is available.
    public async Task<TallyExportView> TallyAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.Tally, ct).ConfigureAwait(false);
        if (VoucherExport.TallyRangeError(from, to) is { } error) throw HccException.Validation(error);
        var days = await AccountingDays.RangeAsync(store, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var vouchers = days.SelectMany(d => d.Vouchers).ToList();
        var (d, c) = VoucherBuilder.Totals(vouchers);
        var company = await store.PropertyNameAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new TallyExportView(from, to, days.Count, vouchers.Count, d, c, true, $"HCC-tally-SIMULATED-{Iso(from)}_{Iso(to)}.xml", VoucherExport.TallyXml(vouchers, company));
    }

    public static VoucherView ToView(Voucher v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return new VoucherView(v.Number, v.Date, v.Type.ToString(), v.Narration, v.SourceKey, v.SourceKind, v.Sub, v.Debit, v.Credit, v.Balanced,
            v.Entries.Select(e => new VoucherEntryView(e.Account, ChartOfAccounts.NameOf(e.Account), e.DebitPaise, e.CreditPaise)).ToList(), v.Unmapped);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
