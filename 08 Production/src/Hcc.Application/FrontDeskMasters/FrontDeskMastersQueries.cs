using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.FrontDeskMasters;
using Hcc.Domain.Invoices;
using Hcc.Domain.Masters;

namespace Hcc.Application.FrontDeskMasters;

/// <summary>
/// MA-5 reads: GET /masters/{booking-sources|id-types|number-series|policies} for every signed-in user (booking forms, registration,
/// documents and policy lines read them); commission is null for readers without money visibility (PERM-MST-002, PERM-RPT-002 or
/// PERM-RES-001; DEC-061). A property that has not stored a list yet gets its defaults with RowId −1, −2, … and an empty version.
/// History: …/history (PERM-MST-001 for sources and ID types, PERM-MST-002 for numbering and policies).
/// </summary>
public sealed class FrontDeskMastersQueries(IAccessPolicy policy, IFrontDeskMastersReadStore reads, IMastersReadStore history, ISettingsReader? settings = null)
{
    private async Task<(List<(FdmRecord Row, int Usage)> Rows, List<FdmRecord> All)> RowsAsync(int propertyId, string master, string? status, CancellationToken ct)
    {
        var st = status ?? "all";
        if (st is not ("all" or "active" or "retired")) throw HccException.Validation("status must be active, retired or all.");
        var all = (await reads.ListAsync(propertyId, master, ct).ConfigureAwait(false)).ToList();
        if (all.Count == 0)
        {
            var prefix = master == FrontDeskMasterIds.NumberSeries && settings is not null ? (await settings.GetAsync(propertyId, "invoicePrefix", null, ct).ConfigureAwait(false)).Text : null;
            var i = 0;
            all = FrontDeskMastersRules.Defaults(master, prefix).Select(d => { i++; return new FdmRecord(-i, d.Code, d.Name, i * MastersRules.SortStep, true, null, d.Fields, DateTime.UnixEpoch, []); }).ToList();
        }

        var list = new List<(FdmRecord, int)>();
        foreach (var r in all.Where(r => st == "all" || r.Active == (st == "active")))
            list.Add((r, await reads.UsageAsync(propertyId, master, r, all, ct).ConfigureAwait(false)));
        return (list, all);
    }

    private static FrontDeskList<T> Page<T>(IReadOnlyList<T> items, int? page, int? pageSize)
    {
        var p = MastersRules.Page(page, pageSize);
        return new FrontDeskList<T>(p.Page, p.PageSize, items.Count, items.Skip(p.Skip).Take(p.PageSize).ToList());
    }

    private static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Ver(FdmRecord r) => r.RowVer.Length == 0 ? string.Empty : VersionToken.ToWire(r.RowVer);

    private static string? Retired(FdmRecord r) => r.RetiredOn is { } d ? FrontDeskMastersRules.Iso(d) : null;

    public async Task<FrontDeskList<BookingSourceView>> BookingSourcesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        var money = false;
        foreach (var perm in new[] { MastersPermissions.ManageMoney, "PERM-RPT-002", "PERM-RES-001" })
            if (money = await policy.HasAsync(propertyId, principalId, perm, null, ct).ConfigureAwait(false)) break;
        var (rows, _) = await RowsAsync(propertyId, FrontDeskMasterIds.BookingSources, status, ct).ConfigureAwait(false);
        return Page(rows.Select(x => new BookingSourceView(x.Row.RowId, x.Row.Code, x.Row.Name, x.Row.Get<string>("segment"), x.Row.Get<string>("channel"),
            money ? x.Row.Get<int>("commissionBps") : null, x.Row.SortOrder, x.Row.Active, Retired(x.Row), x.Usage, Utc(x.Row.UpdatedUtc), Ver(x.Row))).ToList(), page, pageSize);
    }

    public async Task<FrontDeskList<IdTypeView>> IdTypesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId; // session checked by the endpoint group; no money, no guest data
        var (rows, _) = await RowsAsync(propertyId, FrontDeskMasterIds.IdTypes, status, ct).ConfigureAwait(false);
        return Page(rows.Select(x => new IdTypeView(x.Row.RowId, x.Row.Code, x.Row.Name, x.Row.Get<bool>("accepted"), x.Row.Get<bool>("needsPhoto"), x.Row.Get<bool>("foreignerOnly"),
            x.Row.SortOrder, x.Row.Active, Retired(x.Row), x.Usage, Utc(x.Row.UpdatedUtc), Ver(x.Row))).ToList(), page, pageSize);
    }

    public async Task<FrontDeskList<NumberSeriesView>> NumberSeriesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId; // numbering carries no money or guest data
        var (rows, all) = await RowsAsync(propertyId, FrontDeskMasterIds.NumberSeries, status, ct).ConfigureAwait(false);
        var today = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var items = new List<NumberSeriesView>();
        foreach (var (r, usage) in rows)
        {
            var doc = r.Get<string>("document");
            var inForce = NumberSeriesSource.InForce(all, doc, today)?.RowId == r.RowId;
            string? next = null;
            if (inForce)
            {
                var fy = InvoiceNumbering.FyCode(today);
                var srv = FrontDeskMastersRules.IsGst(doc) ? await reads.FySeriesAsync(propertyId, fy, doc, ct).ConfigureAwait(false) : null;
                next = srv is { } x
                    ? InvoiceNumbering.Format(x.Prefix, fy, x.LastSeq + 1, r.Get<int>("padding"))
                    : FrontDeskMastersRules.Format(r.Get<string>("prefix"), r.Get<string>("reset"), r.Get<int>("padding"), today, r.Get<int>("startNo"));
            }

            var issued = Math.Max(0, usage - (inForce ? 1 : 0));
            items.Add(new NumberSeriesView(r.RowId, r.Code, r.Name, doc, r.Get<string>("prefix"), r.Get<int>("startNo"), r.Get<int>("padding"), r.Get<string>("reset"),
                FrontDeskMastersRules.Iso(r.Get<DateOnly>("activeFrom")), r.SortOrder, r.Active, Retired(r), issued, next, usage, Utc(r.UpdatedUtc), Ver(r)));
        }

        return Page(items, page, pageSize);
    }

    public async Task<FrontDeskList<BookingPolicyView>> PoliciesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId; // policy terms are told to guests at booking
        var (rows, _) = await RowsAsync(propertyId, FrontDeskMasterIds.Policies, status, ct).ConfigureAwait(false);
        return Page(rows.Select(x => new BookingPolicyView(x.Row.RowId, x.Row.Code, x.Row.Name, x.Row.Get<int>("freeCancelHours"), x.Row.Get<int>("cancelChargeBps"),
            x.Row.Get<int>("noShowChargeBps"), x.Row.Get<int>("depositBps"), x.Row.Get<IReadOnlyList<string>>("sources") ?? [], x.Row.SortOrder, x.Row.Active, Retired(x.Row), x.Usage,
            Utc(x.Row.UpdatedUtc), Ver(x.Row))).ToList(), page, pageSize);
    }

    public async Task<MasterHistoryList> HistoryAsync(int propertyId, int principalId, string master, int? rowId = null, int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        if (!FrontDeskMasterIds.All.Contains(master)) throw HccException.Validation("Unknown list.");
        await policy.RequireAsync(propertyId, principalId, FrontDeskMastersRules.IsMoneyList(master) ? MastersPermissions.ManageMoney : MastersPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var (total, items) = await history.HistoryAsync(propertyId, master, rowId, p.Skip, p.PageSize, ct).ConfigureAwait(false);
        return new MasterHistoryList(p.Page, p.PageSize, total, items);
    }
}

/// <summary>
/// <see cref="INumberSeriesSource"/> over bill.number_series: the active series of a document with the latest start date on or before the
/// date. Null when the property has not stored its list (the invoices module then keeps its built-in numbering — the same as the defaults).
/// </summary>
public sealed class NumberSeriesSource(IFrontDeskMastersStore store) : INumberSeriesSource
{
    public async Task<SeriesInForce?> InForceAsync(ICommandTransaction tx, int propertyId, string document, DateOnly date, CancellationToken ct = default)
    {
        var rows = await store.ReadAllAsync(tx, propertyId, FrontDeskMasterIds.NumberSeries, ct).ConfigureAwait(false);
        if (rows.Count == 0) return null;
        return InForce(rows, document, date)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION,
                $"No numbering series is in force for the {FrontDeskMastersRules.DocumentLabel(document)} on {FrontDeskMastersRules.Iso(date)}. The Owner adds one in Master configuration › Front-desk policies › Numbering series.");
    }

    /// <summary>The series in force (null when none of the document's active series has started by the date).</summary>
    public static SeriesInForce? InForce(IEnumerable<FdmRecord> rows, string document, DateOnly date) =>
        rows.Where(r => r.Active && r.Get<string>("document") == document && r.Get<DateOnly>("activeFrom") <= date)
            .OrderByDescending(r => r.Get<DateOnly>("activeFrom"))
            .Select(r => new SeriesInForce(r.RowId, r.Code, r.Get<string>("prefix"), r.Get<int>("startNo"), r.Get<int>("padding"), r.Get<string>("reset")))
            .FirstOrDefault();
}
