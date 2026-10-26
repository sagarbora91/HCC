using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Masters;
using Hcc.Contracts;
using Hcc.Contracts.Catalogue;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Application.Catalogue;

/// <summary>
/// Catalogue reads: GET /catalogue/meal-plans, /catalogue/extras, /catalogue/discount-reasons (PERM-FOL-001 — prices are hotel money, so
/// housekeeping and maintenance never load them, DEC-061), their change history (PERM-MST-002, the Owner who changes them) and
/// GET /catalogue/links (plans of bookings and catalogue links of charges, PERM-FOL-001).
/// </summary>
public sealed class CatalogueQueries(IAccessPolicy policy, ICatalogueReadStore reads, IMastersReadStore history)
{
    private Task ViewAsync(int propertyId, int principalId, CancellationToken ct) =>
        policy.RequireAsync(propertyId, principalId, CataloguePermissions.View, null, null, ct);

    private static void Status(string st)
    {
        if (st is not ("all" or "active" or "retired")) throw HccException.Validation("status must be active, retired or all.");
    }

    private async Task<CataloguePage<T>> ListAsync<T>(int propertyId, int principalId, CatalogueMaster m, Func<CatRow, int, T> view, int? page, int? pageSize,
        string? status, CancellationToken ct)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var st = status ?? "all";
        Status(st);
        var all = (await reads.RowsAsync(m, propertyId, ct).ConfigureAwait(false)).Where(x => st == "all" || x.Row.Active == (st == "active")).ToList();
        return new CataloguePage<T>(p.Page, p.PageSize, all.Count, all.Skip(p.Skip).Take(p.PageSize).Select(x => view(x.Row, x.Usage)).ToList());
    }

    public Task<CataloguePage<MealPlanView>> MealPlansAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default) =>
        ListAsync(propertyId, principalId, CatalogueMaster.MealPlans, CatalogueCommands.MealPlanOf, page, pageSize, status, ct);

    public Task<CataloguePage<ExtraItemView>> ExtrasAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default) =>
        ListAsync(propertyId, principalId, CatalogueMaster.Extras, CatalogueCommands.ExtraOf, page, pageSize, status, ct);

    public Task<CataloguePage<DiscountReasonView>> DiscountReasonsAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default) =>
        ListAsync(propertyId, principalId, CatalogueMaster.DiscountReasons, CatalogueCommands.ReasonOf, page, pageSize, status, ct);

    /// <summary>Change history of one list (one row, or all rows when <paramref name="rowId"/> is null).</summary>
    public async Task<MasterHistoryList> HistoryAsync(int propertyId, int principalId, string master, long? rowId = null, int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        if (CatalogueMaster.All.All(m => m.Id != master)) throw HccException.Validation("Unknown list.");
        await policy.RequireAsync(propertyId, principalId, CataloguePermissions.ManageMoney, null, null, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var (total, items) = await history.HistoryAsync(propertyId, master, rowId, p.Skip, p.PageSize, ct).ConfigureAwait(false);
        return new MasterHistoryList(p.Page, p.PageSize, total, items);
    }

    /// <summary>Links for [from, to] (default: 62 days before the business date to 400 days after; at most 731 days).</summary>
    public async Task<CatalogueLinks> LinksAsync(int propertyId, int principalId, string? from = null, string? to = null, CancellationToken ct = default)
    {
        await ViewAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var f = Date(from, "from") ?? bd.AddDays(-62);
        var t = Date(to, "to") ?? bd.AddDays(400);
        if (t < f || t.DayNumber - f.DayNumber > 731) throw HccException.Validation("Choose a range of at most 731 days (from on or before to).");
        return await reads.LinksAsync(propertyId, f, t, ct).ConfigureAwait(false);
    }

    private static DateOnly? Date(string? v, string name)
    {
        if (string.IsNullOrEmpty(v)) return null;
        return DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation($"{name} must be yyyy-MM-dd.");
    }
}
