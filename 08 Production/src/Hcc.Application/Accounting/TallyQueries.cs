using System.Globalization;
using System.Text;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Import;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Domain.ThirdParty.Etp;

namespace Hcc.Application.Accounting;

/// <summary>One recorded Tally file (masters per FY / vouchers per range): kind, range, counts, SHA-256 of the exact bytes, provisional flag.</summary>
public sealed record NewTallyFile(int PropertyId, string Kind, DateOnly From, DateOnly To, int Items, int Bytes, string Sha256, bool Provisional, int ExportedBy, DateTime ExportedUtc);

/// <summary>The last file of a kind for a range start (re-export note of the masters).</summary>
public sealed record TallyFileLogRow(long FileId, DateTime ExportedUtc, int ExportedBy, string Sha256);

/// <summary>Log of the Tally files (<c>acc.tally_file</c>, migration 4266). Append-only.</summary>
public interface ITallyFileLog
{
    Task<TallyFileLogRow?> LastAsync(int propertyId, string kind, DateOnly from, CancellationToken ct);

    Task<long> RecordAsync(ICommandTransaction tx, NewTallyFile file, CancellationToken ct);
}

/// <summary>GSTIN of the party sub-ledgers that have one (companies <c>1110/CMP-&lt;id&gt;</c>, expense vendors <c>2200/&lt;code&gt;</c>): ledger id → GSTIN.</summary>
public interface ITallyPartyGstin
{
    Task<IReadOnlyDictionary<string, string>> GetAsync(int propertyId, CancellationToken ct);
}

/// <summary>Answer of <c>GET /accounting/tally/masters</c>: the masters file of one financial year.</summary>
public sealed record TallyMastersView(string Fy, DateOnly From, DateOnly To, int Groups, int Ledgers, int PartyLedgers, bool Provisional, string FileName, string Sha256, int Bytes,
    string Xml, IReadOnlyList<string> Notes, long? FileId);

/// <summary>Answer of <c>GET /accounting/tally/vouchers</c>: the vouchers file of one range (≤ 31 days).</summary>
public sealed record TallyVouchersView(DateOnly From, DateOnly To, int Dates, int VoucherCount, long DebitPaise, long CreditPaise, string FileName, string Sha256, int Bytes, string Xml,
    IReadOnlyList<DateOnly> ReexportDates, IReadOnlyList<DateOnly> ProvisionalDates, int PendingEvents, IReadOnlyList<DateOnly> LegacyDates, long? ExportId, long? FileId);

/// <summary>
/// ACC-3 acc3-tally (contract §4; PERM-ACC-006 as today's <see cref="AccountingQueries.TallyAsync"/>): Tally "Import Data" files built with XmlWriter —
/// the masters of a financial year (Owner sub-groups, every ledger of the chart, every party sub-ledger with the opening balance at the FY start, bill-wise
/// on debtors / creditors, GSTIN on companies / vendors) and the vouchers of a range with bill allocations (New Ref / Agst Ref / On Account). Nothing is sent to Tally.
/// The DEC-077 re-export / provisional flags and the A03b-05 pending / legacy notes of the old vouchers export apply unchanged; every file is logged with its SHA-256.
/// Bill allocations: acc3-ageing's explicit ACC-2 vendor-payment allocations first (<see cref="IAgeingAllocationSource"/>), the rest oldest-first over the financial year.
/// </summary>
public sealed class TallyQueries(IAccountingStore store, IAccessPolicy policy, IReportPartyNames? partyNames = null, ITallyPartyGstin? gstins = null,
    IReportOpeningSource? openingEntry = null, IOpeningBalanceSource? imported = null, ITallyFileLog? log = null, ICommandRunner? runner = null, TimeProvider? clock = null,
    Hcc.Application.Configuration.ISettingsReader? settings = null, IAgeingAllocationSource? allocations = null)
{
    /// <summary>Company name in the files (SVCURRENTCOMPANY): Master configuration <c>brand.legalName</c> (Owner) when set, else the property name.</summary>
    private async Task<string?> CompanyAsync(int propertyId, CancellationToken ct)
    {
        if (settings is not null)
        {
            var legal = (await settings.GetAsync(propertyId, "brand.legalName", null, ct).ConfigureAwait(false)).Text;
            if (!string.IsNullOrWhiteSpace(legal)) return legal.Trim();
        }

        return await store.PropertyNameAsync(propertyId, ct).ConfigureAwait(false);
    }

    public const string OpExportTallyMasters = "ExportTallyMasters";

    public const string OpExportTallyVouchers = "ExportTallyVouchers";

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Financial year (April to March) from "2026" or "2026-27": the first day, or null when it is not a valid year.</summary>
    public static DateOnly? FyStartOf(string? fy)
    {
        var t = (fy ?? "").Trim();
        if (t.Length is not (4 or 7) || !int.TryParse(t.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y is < 2000 or > 2100) return null;
        if (t.Length == 7 && (t[4] != '-' || !int.TryParse(t.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var yy) || yy != (y + 1) % 100)) return null;
        return new DateOnly(y, 4, 1);
    }

    public static DateOnly FyStartOfDate(DateOnly d) => new(d.Month >= 4 ? d.Year : d.Year - 1, 4, 1);

    public static string FyLabel(DateOnly fyStart) => fyStart.Year.ToString(CultureInfo.InvariantCulture) + "-" + ((fyStart.Year + 1) % 100).ToString("00", CultureInfo.InvariantCulture);

    private sealed record Loaded(List<Voucher> All, ReportOpening? Entry);

    /// <summary>Every voucher up to <paramref name="to"/> (locks as locked, live elsewhere) plus the approved opening entry, else the imported opening — as the report queries read them.</summary>
    private async Task<Loaded> LoadAsync(int propertyId, DateOnly to, CancellationToken ct)
    {
        var days = await AccountingDays.RangeAsync(store, propertyId, DateOnly.MinValue, to, ct).ConfigureAwait(false);
        var all = days.SelectMany(d => d.Vouchers).ToList();
        var entry = openingEntry is null ? null : await openingEntry.GetAsync(propertyId, ct).ConfigureAwait(false);
        if (entry?.Voucher is { } ov) all.Add(ov);
        else if (imported is not null && AccountingReports.ImportedVoucher(await imported.GetAsync(propertyId, ct).ConfigureAwait(false), new Dictionary<string, (string Name, string Group)>()) is { } iv) all.Add(iv);
        return new Loaded(all, entry);
    }

    private async Task<Dictionary<string, TallyPartyInfo>> PartiesAsync(int propertyId, CancellationToken ct)
    {
        var names = partyNames is null ? new Dictionary<string, string>() : new Dictionary<string, string>(await partyNames.GetAsync(propertyId, ct).ConfigureAwait(false));
        var tax = gstins is null ? new Dictionary<string, string>() : new Dictionary<string, string>(await gstins.GetAsync(propertyId, ct).ConfigureAwait(false));
        var parties = new Dictionary<string, TallyPartyInfo>(StringComparer.Ordinal);
        foreach (var (id, name) in names) parties[id] = new TallyPartyInfo(name, tax.GetValueOrDefault(id));
        return parties;
    }

    private static Dictionary<string, long> Balances(IEnumerable<Voucher> vouchers)
    {
        var open = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in vouchers.SelectMany(v => v.Entries)) open[e.Account] = open.GetValueOrDefault(e.Account) + e.DebitPaise - e.CreditPaise;
        return open;
    }

    private static (string Sha, int Bytes) Fingerprint(string xml)
    {
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        return (EtpTallyXml.Sha256Hex(bytes), bytes.Length);
    }

    /// <summary>The masters file of financial year <paramref name="fy"/> ("2026" or "2026-27").</summary>
    public async Task<TallyMastersView> MastersAsync(Caller caller, string fy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Tally, ct: ct).ConfigureAwait(false);
        var start = FyStartOf(fy) ?? throw HccException.Validation("Choose a financial year such as 2026 or 2026-27.");
        var end = start.AddYears(1).AddDays(-1);
        var data = await LoadAsync(caller.PropertyId, start, ct).ConfigureAwait(false);
        var (opening, _) = AccountingReports.Split(data.All, start, start);
        var chart = await store.ChartAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var parties = await PartiesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var book = TallyBook.Build(chart, Balances(opening), parties, []);
        var provisional = AccountingReports.OpeningPending(data.Entry);
        var notes = new List<string>();
        if (provisional)
            notes.Add($"PROVISIONAL · FY {FyLabel(start)} has no approved opening entry with a zero Opening-balance difference yet, so the opening balances in this file may change; export the masters again once the opening is approved.");
        var last = log is null ? null : await log.LastAsync(caller.PropertyId, "masters", start, ct).ConfigureAwait(false);
        if (last is not null)
            notes.Add($"RE-EXPORT · the masters of FY {FyLabel(start)} were exported before (last on {Iso(DateOnly.FromDateTime(last.ExportedUtc))}). Tally creates masters that do not exist yet and does not create a master twice; an opening balance already in the company is not changed by importing again.");
        var company = await CompanyAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var xml = VoucherExport.TallyMastersXml(book, company, start, notes);
        var (sha, bytes) = Fingerprint(xml);
        long? fileId = null;
        if (runner is not null && log is not null)
        {
            var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var row = new NewTallyFile(caller.PropertyId, "masters", start, end, book.Groups.Count + book.Ledgers.Count, bytes, sha, provisional, caller.PrincipalId, now);
            var r = await runner.RunAsync(new CommandContext(caller.PropertyId, caller.PrincipalId, OpExportTallyMasters, "tally-masters:" + Guid.NewGuid().ToString("N")), new { fy = FyLabel(start) },
                async (tx, ct2) =>
                {
                    var id = await log.RecordAsync(tx, row, ct2).ConfigureAwait(false);
                    await tx.AuditAsync("TALLY_MASTERS_EXPORTED", "acc-tally-file:" + id.ToString(CultureInfo.InvariantCulture),
                        $"FY {FyLabel(start)} · {book.Groups.Count} group(s), {book.Ledgers.Count} ledger(s) · sha256 {sha[..12]}…" + (provisional ? " · provisional" : ""), ct2).ConfigureAwait(false);
                    return id;
                }, ct).ConfigureAwait(false);
            fileId = r.Value;
        }

        return new TallyMastersView(FyLabel(start), start, end, book.Groups.Count, book.Ledgers.Count, book.Ledgers.Count(l => l.Party), provisional,
            $"HCC-tally-masters-SIMULATED-FY{FyLabel(start)}.xml", sha, bytes, xml, notes, fileId);
    }

    /// <summary>The vouchers file of <paramref name="from"/>..<paramref name="to"/> (≤ 31 days) with bill allocations.</summary>
    public async Task<TallyVouchersView> VouchersAsync(Caller caller, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.Tally, ct: ct).ConfigureAwait(false);
        if (VoucherExport.TallyRangeError(from, to) is { } error) throw HccException.Validation(error);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var fyStart = FyStartOfDate(from);
        var flags = await store.TallyReexportsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var reopened = await store.ReopenedDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
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
        var written = days.Where(x => x.Date >= fyStart).SelectMany(x => x.Vouchers).Where(v => !AccountingReports.IsOpening(v)).ToList();

        var data = await LoadAsync(caller.PropertyId, to, ct).ConfigureAwait(false);
        var (opening, _) = AccountingReports.Split(data.All, fyStart, fyStart);
        var stream = data.All.Where(v => v.Date >= fyStart && !AccountingReports.IsOpening(v)).ToList();
        // re-export days outside the loaded range are already in the stream (all dates up to `to`); order = date, number
        stream = [.. stream.OrderBy(v => v.Date).ThenBy(v => v.Number, StringComparer.Ordinal)];
        var emit = written.Select(v => v.SourceKey).ToHashSet(StringComparer.Ordinal);
        var chart = await store.ChartAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var parties = await PartiesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var book = TallyBook.Build(chart, Balances(opening), parties, stream.SelectMany(v => v.Entries.Select(e => e.Account)));
        var identity = await store.TallyIdentityAsync(null, caller.PropertyId, now, ct).ConfigureAwait(false);
        var company = await CompanyAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var explicitAllocations = allocations is null ? null : await allocations.GetAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var xml = VoucherExport.TallyVouchersXml(stream, emit, book, chart, company, identity, notes, explicitAllocations);
        var (sha, bytes) = Fingerprint(xml);
        var emitted = stream.Where(v => emit.Contains(v.SourceKey)).ToList();
        var (d, c) = VoucherBuilder.Totals(emitted);
        long? exportId = null;
        long? fileId = null;
        if (runner is not null)
        {
            var clear = flags.Where(f => !provisional.Contains(f.BusinessDate) && days.Any(x => x.Date == f.BusinessDate)).Select(f => f.ReexportId).ToList();
            var logRow = new NewTallyExport(caller.PropertyId, from, to, emitted.Count, d, c, caller.PrincipalId, now,
                days.Select(x => (x.Date, x.Vouchers.Count, flagged.ContainsKey(x.Date), provisional.Contains(x.Date))).ToList(), clear);
            var file = new NewTallyFile(caller.PropertyId, "vouchers", from, to, emitted.Count, bytes, sha, provisional.Count > 0, caller.PrincipalId, now);
            var r = await runner.RunAsync(new CommandContext(caller.PropertyId, caller.PrincipalId, OpExportTallyVouchers, "tally-vouchers:" + Guid.NewGuid().ToString("N")), new { from, to },
                async (tx, ct2) =>
                {
                    var id = await store.RecordTallyExportAsync(tx, logRow, ct2).ConfigureAwait(false);
                    foreach (var x in lagging)
                        await store.FlagTallyReexportAsync(tx, caller.PropertyId, x, null, AccountingQueries.PendingFlagReason(pending), now, ct2).ConfigureAwait(false);
                    long? fid = log is null ? null : await log.RecordAsync(tx, file, ct2).ConfigureAwait(false);
                    await tx.AuditAsync("TALLY_EXPORTED", "acc-tally:" + id.ToString(CultureInfo.InvariantCulture),
                        $"{Iso(from)} to {Iso(to)} · {emitted.Count} voucher(s) over {days.Count} date(s) · simulated · bill-wise · sha256 {sha[..12]}…"
                        + (reexport.Count > 0 ? " · re-export " + string.Join(", ", reexport.Select(Iso)) : "")
                        + (provisional.Count > 0 ? " · provisional " + string.Join(", ", provisional.Select(Iso)) : "")
                        + (pending > 0 ? $" · {pending} accounting event(s) pending" : ""), ct2).ConfigureAwait(false);
                    return (id, fid);
                }, ct).ConfigureAwait(false);
            (exportId, fileId) = (r.Value.id, r.Value.fid);
        }

        return new TallyVouchersView(from, to, days.Count, emitted.Count, d, c, $"HCC-tally-vouchers-SIMULATED-{Iso(from)}_{Iso(to)}.xml", sha, bytes, xml, reexport, provisional, pending, legacy, exportId, fileId);
    }
}
