using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Contracts.Pos;
using Hcc.Domain.Errors;

namespace Hcc.Application.Pos;

/// <summary>POS reads: check, open checks, menu, kitchen board, day summary, close blockers. All need the operate permission (PERM-FOL-002).</summary>
public sealed class PosQueries(IPosStore store, IAccessPolicy policy, IPosTaxRates taxRates)
{
    private async Task<DateOnly> TodayAsync(PosCaller c, CancellationToken ct)
    {
        await policy.RequireAsync(c.PropertyId, c.PrincipalId, PosPermissions.Operate, ct: ct).ConfigureAwait(false);
        return await store.PropertyBusinessDateAsync(c.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task<CheckView> GetCheckAsync(PosCaller c, long orderId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        var date = await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.ReadCheckAsync(c.PropertyId, orderId, taxRates.FoodTaxBpsOn(c.PropertyId, date), ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Check not found.");
    }

    public async Task<IReadOnlyList<OpenCheckSummary>> OpenChecksAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.OpenChecksAsync(c.PropertyId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutletView>> OutletsAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.OutletsAsync(c.PropertyId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MenuItemView>> MenuAsync(PosCaller c, int? outletId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.MenuAsync(c.PropertyId, outletId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KitchenItem>> KitchenAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.KitchenAsync(c.PropertyId, ct).ConfigureAwait(false);
    }

    public async Task<DaySummaryView> DaySummaryAsync(PosCaller c, DateOnly? date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        var today = await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.DaySummaryAsync(c.PropertyId, date ?? today, taxRates.FoodTaxBpsOn(c.PropertyId, today), ct).ConfigureAwait(false);
    }

    public async Task<PosCloseBlockers> BlockersAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.BlockersAsync(c.PropertyId, ct).ConfigureAwait(false);
    }
}
