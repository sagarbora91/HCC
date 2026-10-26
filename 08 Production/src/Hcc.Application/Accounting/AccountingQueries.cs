using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
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
/// DEC-077: each Tally export is recorded (export log + audit, through <paramref name="runner"/>; null = not recorded) so a reopened day
/// that had been exported is known to have changed; flagged dates are carried again by the next export.
/// </summary>
public sealed class AccountingQueries(IAccountingStore store, IAccessPolicy policy, IOpeningBalanceSource? opening = null, ICommandRunner? runner = null,
    TimeProvider? clock = null)
{
    public const int CsvMaxDays = 366;

    public const string OpExportTally = "ExportTally";

    private sealed record ExportLogged(long ExportId, string Identity);

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

    /// <summary>
    /// Simulated Tally XML for ≤ 31 days. Nothing is sent to Tally; only Tally's own import confirms an import.
    /// A03b-05: every voucher carries a stable REMOTEID / GUID (<see cref="VoucherExport.RemoteId"/> of the property's Tally identity and
    /// the voucher's source key), so importing a voucher again replaces it in Tally (with "Overwrite vouchers when a voucher with same GUID
    /// exists" = Yes) instead of adding it twice; ACTION stays Create. DEC-077: dates flagged changed after an earlier export (their day was
    /// reopened) are carried again even outside the range, each with a RE-EXPORT note; the day still reopened is PROVISIONAL and stays
    /// flagged. A03b-05: while accounting events are pending or parked (<c>AccountingVoucherProjector.PendingSql</c>, the count that blocks
    /// a lock), every carried date that is not locked is PROVISIONAL as well (its vouchers may be incomplete; a locked date is its
    /// snapshot), the count is in the note and in the response, and the date is flagged so that the first export after the events are
    /// processed carries it again. A date that an export recorded before voucher ids existed took to Tally gets a LEGACY note. The export
    /// is recorded (log + audit) when a runner is composed; recording clears the flags of the dates it carried that are not provisional.
    /// </summary>
    // ETP-REUSE: ETP Tally preparation (ledger/voucher-type masters, company GUID, import report parsing) when V:\ETP is available.
    public async Task<TallyExportView> TallyAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await RequireAsync(caller, AccountingPermissions.Tally, ct).ConfigureAwait(false);
        if (VoucherExport.TallyRangeError(from, to) is { } error) throw HccException.Validation(error);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var flags = await store.TallyReexportsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var reopened = await store.ReopenedDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        // A03b-05 x A09-05 (Wave C3): still being processed and parked (stuck) both make an export provisional.
        var pending = (await store.PendingEventsAsync(null, caller.PropertyId, ct).ConfigureAwait(false)).Total;
        var days = (await AccountingDays.RangeWithLocksAsync(store, caller.PropertyId, from, to, ct).ConfigureAwait(false)).ToList();
        foreach (var f in flags.Where(f => f.BusinessDate < from || f.BusinessDate > to))
            days.AddRange(await AccountingDays.RangeWithLocksAsync(store, caller.PropertyId, f.BusinessDate, f.BusinessDate, ct).ConfigureAwait(false));
        days = [.. days.OrderBy(x => x.Date)];
        var flagged = flags.ToDictionary(f => f.BusinessDate);
        var reexport = days.Where(x => flagged.ContainsKey(x.Date)).Select(x => x.Date).ToList();
        var lagging = pending > 0 ? days.Where(x => !x.Locked).Select(x => x.Date).ToList() : [];
        var provisional = days.Where(x => x.Date == reopened || lagging.Contains(x.Date)).Select(x => x.Date).ToList();
        var legacy = days.Count == 0 ? [] : await store.TallyExportedWithoutIdsAsync(null, caller.PropertyId, days.Select(x => x.Date).ToList(), ct).ConfigureAwait(false);
        var notes = reexport.Select(x => flagged[x].ReopenId is null ? VoucherExport.ReexportAfterProvisionalNote(x, flagged[x].Reason) : VoucherExport.ReexportNote(x, flagged[x].Reason))
            .Concat(provisional.Where(x => x == reopened).Select(VoucherExport.ReopenedNote))
            .Concat(lagging.Select(x => VoucherExport.PendingNote(x, pending)))
            .Concat(legacy.Select(VoucherExport.LegacyNote)).ToList();
        var vouchers = days.SelectMany(x => x.Vouchers).ToList();
        var (d, c) = VoucherBuilder.Totals(vouchers);
        var company = await store.PropertyNameAsync(caller.PropertyId, ct).ConfigureAwait(false);
        long? exportId = null;
        string identity;
        if (runner is not null)
        {
            var clear = flags.Where(f => !provisional.Contains(f.BusinessDate) && days.Any(x => x.Date == f.BusinessDate)).Select(f => f.ReexportId).ToList();
            var log = new NewTallyExport(caller.PropertyId, from, to, vouchers.Count, d, c, caller.PrincipalId, now,
                days.Select(x => (x.Date, x.Vouchers.Count, flagged.ContainsKey(x.Date), provisional.Contains(x.Date))).ToList(), clear);
            var r = await runner.RunAsync(new CommandContext(caller.PropertyId, caller.PrincipalId, OpExportTally, "tally-export:" + Guid.NewGuid().ToString("N")),
                new { from, to }, async (tx, ct2) =>
                {
                    var tallyId = await store.TallyIdentityAsync(tx, caller.PropertyId, now, ct2).ConfigureAwait(false);
                    var id = await store.RecordTallyExportAsync(tx, log, ct2).ConfigureAwait(false);
                    // A03b-05: a date exported while events were pending is carried again by the first export after they are processed.
                    foreach (var x in lagging)
                        await store.FlagTallyReexportAsync(tx, caller.PropertyId, x, null, PendingFlagReason(pending), now, ct2).ConfigureAwait(false);
                    await tx.AuditAsync("TALLY_EXPORTED", "acc-tally:" + id.ToString(CultureInfo.InvariantCulture),
                        $"{Iso(from)} to {Iso(to)} · {vouchers.Count} voucher(s) over {days.Count} date(s) · simulated · voucher ids HCC-{tallyId}"
                        + (reexport.Count > 0 ? " · re-export " + string.Join(", ", reexport.Select(Iso)) : "")
                        + (provisional.Count > 0 ? " · provisional " + string.Join(", ", provisional.Select(Iso)) : "")
                        + (pending > 0 ? $" · {pending} accounting event(s) pending" : ""), ct2).ConfigureAwait(false);
                    return new ExportLogged(id, tallyId);
                }, ct).ConfigureAwait(false);
            exportId = r.Value.ExportId;
            identity = r.Value.Identity;
        }
        else
        {
            identity = await store.TallyIdentityAsync(null, caller.PropertyId, now, ct).ConfigureAwait(false);
        }

        return new TallyExportView(from, to, days.Count, vouchers.Count, d, c, true, $"HCC-tally-SIMULATED-{Iso(from)}_{Iso(to)}.xml",
            VoucherExport.TallyXml(vouchers, company, identity, notes), reexport, provisional, exportId, pending, legacy);
    }

    /// <summary>The re-export flag reason of a date exported while accounting events were pending (A03b-05; ≤ 200 characters).</summary>
    public static string PendingFlagReason(int pending) => $"{pending.ToString(CultureInfo.InvariantCulture)} accounting event(s) were still pending";

    public static VoucherView ToView(Voucher v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return new VoucherView(v.Number, v.Date, v.Type.ToString(), v.Narration, v.SourceKey, v.SourceKind, v.Sub, v.Debit, v.Credit, v.Balanced,
            v.Entries.Select(e => new VoucherEntryView(e.Account, ChartOfAccounts.NameOf(e.Account), e.DebitPaise, e.CreditPaise)).ToList(), v.Unmapped);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
