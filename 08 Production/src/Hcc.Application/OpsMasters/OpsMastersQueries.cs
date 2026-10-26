using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts.Masters;
using Hcc.Contracts.OpsMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.OpsMasters;

namespace Hcc.Application.OpsMasters;

/// <summary>Row → wire view of each master.</summary>
public static class OpsMastersViews
{
    private static string Utc(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? Retired(OpsRow r) => r.RetiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string F(OpsRow r, string key) => r.Fields.GetValueOrDefault(key) ?? string.Empty;

    private static int N(OpsRow r, string key) => int.Parse(F(r, key), CultureInfo.InvariantCulture);

    public static HkTaskTypeView HkTaskType(OpsRow r, int used) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), used, Utc(r.UpdatedUtc), VersionToken.ToWire(r.RowVer),
        OpsMastersRules.SplitCsv(F(r, "appliesTo")), N(r, "standardMinutes"), F(r, "needsInspection") == "1", OpsMastersRules.ParseChecklist(F(r, "checklist")));

    public static HkZoneView HkZone(OpsRow r, int used) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), used, Utc(r.UpdatedUtc), VersionToken.ToWire(r.RowVer),
        OpsMastersRules.SplitCsv(F(r, "floors")), OpsMastersRules.SplitCsv(F(r, "rooms")), F(r, "defaultAttendant") is { Length: > 0 } a ? int.Parse(a, CultureInfo.InvariantCulture) : null);

    public static ComplaintCategoryView ComplaintCategory(OpsRow r, int used) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), used, Utc(r.UpdatedUtc), VersionToken.ToWire(r.RowVer),
        F(r, "department"), N(r, "slaHours"));

    public static LostFoundCategoryView LostFoundCategory(OpsRow r, int used) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), used, Utc(r.UpdatedUtc), VersionToken.ToWire(r.RowVer),
        F(r, "department"), N(r, "slaHours"), F(r, "valuable") == "1");
}

/// <summary>
/// Reads of the four lists: GET /housekeeping/task-types, /housekeeping/zones, /guest-services/complaint-categories,
/// /guest-services/lost-found-categories (any signed-in user: housekeepers need the checklist, reception the categories; no money, no guest data)
/// and each list's change history (PERM-MST-001, the people who may change it).
/// </summary>
public sealed class OpsMastersQueries(IAccessPolicy policy, IOpsMastersReadStore reads, IMastersReadStore history)
{
    private async Task<(IReadOnlyList<T> Page, int Total)> ListAsync<T>(OpsMasterDef def, int propertyId, int? page, int? pageSize, string? status, Func<OpsRow, int, T> view, CancellationToken ct)
    {
        var p = MastersRules.Page(page, pageSize);
        var st = status ?? "all";
        if (st is not ("all" or "active" or "retired")) throw HccException.Validation("status must be active, retired or all.");
        var all = (await reads.ListAsync(def, propertyId, ct).ConfigureAwait(false)).Where(x => st == "all" || x.Row.Active == (st == "active")).ToList();
        return (all.Skip(p.Skip).Take(p.PageSize).Select(x => view(x.Row, x.Used)).ToList(), all.Count);
    }

    private async Task<OpsList<T>> PageAsync<T>(OpsMasterDef def, int propertyId, int? page, int? pageSize, string? status, Func<OpsRow, int, T> view, CancellationToken ct)
    {
        var p = MastersRules.Page(page, pageSize);
        var (items, total) = await ListAsync(def, propertyId, page, pageSize, status, view, ct).ConfigureAwait(false);
        return new OpsList<T>(p.Page, p.PageSize, total, items);
    }

    public Task<OpsList<HkTaskTypeView>> TaskTypesAsync(int propertyId, int? page, int? pageSize, string? status, CancellationToken ct = default) =>
        PageAsync(OpsMasterDefs.HkTaskTypes, propertyId, page, pageSize, status, OpsMastersViews.HkTaskType, ct);

    public Task<OpsList<HkZoneView>> ZonesAsync(int propertyId, int? page, int? pageSize, string? status, CancellationToken ct = default) =>
        PageAsync(OpsMasterDefs.HkZones, propertyId, page, pageSize, status, OpsMastersViews.HkZone, ct);

    public Task<OpsList<ComplaintCategoryView>> ComplaintCategoriesAsync(int propertyId, int? page, int? pageSize, string? status, CancellationToken ct = default) =>
        PageAsync(OpsMasterDefs.ComplaintCategories, propertyId, page, pageSize, status, OpsMastersViews.ComplaintCategory, ct);

    public Task<OpsList<LostFoundCategoryView>> LostFoundCategoriesAsync(int propertyId, int? page, int? pageSize, string? status, CancellationToken ct = default) =>
        PageAsync(OpsMasterDefs.LostFoundCategories, propertyId, page, pageSize, status, OpsMastersViews.LostFoundCategory, ct);

    public async Task<MasterHistoryList> HistoryAsync(OpsMasterDef def, int propertyId, int principalId, long? rowId, int? page, int? pageSize, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MastersPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var (total, items) = await history.HistoryAsync(propertyId, def.Master, rowId, p.Skip, p.PageSize, ct).ConfigureAwait(false);
        return new MasterHistoryList(p.Page, p.PageSize, total, items);
    }
}
