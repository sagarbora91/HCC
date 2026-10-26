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
        return await store.ReadCheckAsync(c.PropertyId, orderId, (await taxRates.FoodAsync(null, c.PropertyId, date, ct).ConfigureAwait(false)).Bps, ct).ConfigureAwait(false)
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
        return await store.DaySummaryAsync(c.PropertyId, date ?? today, (await taxRates.FoodAsync(null, c.PropertyId, today, ct).ConfigureAwait(false)).Bps, ct).ConfigureAwait(false);
    }

    public async Task<PosCloseBlockers> BlockersAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.BlockersAsync(c.PropertyId, ct).ConfigureAwait(false);
    }

    /// <summary>Settled / void checks of a business date (default today) with their lines and bill, for the "closed today" list and bill reprint. At most 50.</summary>
    public async Task<IReadOnlyList<CheckView>> ClosedChecksAsync(PosCaller c, DateOnly? date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        var today = await TodayAsync(c, ct).ConfigureAwait(false);
        var bps = (await taxRates.FoodAsync(null, c.PropertyId, today, ct).ConfigureAwait(false)).Bps;
        var list = new List<CheckView>();
        foreach (var id in await store.ClosedCheckIdsAsync(c.PropertyId, date ?? today, 50, ct).ConfigureAwait(false))
            if (await store.ReadCheckAsync(c.PropertyId, id, bps, ct).ConfigureAwait(false) is { } v) list.Add(v);
        return list;
    }

    /// <summary>Pending void requests (amount, reason, who asked) - the approvals queue and the locked check read this.</summary>
    public async Task<IReadOnlyList<VoidRequestView>> PendingVoidsAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.PendingVoidsAsync(c.PropertyId, ct).ConfigureAwait(false);
    }

    /// <summary>In-house guests (stay, folio, reservation, room) the outlet can charge: room-service targets and "post to room".</summary>
    public async Task<IReadOnlyList<PosStayView>> InHouseAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.InHouseStaysAsync(c.PropertyId, ct).ConfigureAwait(false);
    }

    /// <summary>The shared front-desk till and whether its shift is open (outlet cash needs it, DEC-052).</summary>
    public async Task<PosTillView> TillAsync(PosCaller c, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        await TodayAsync(c, ct).ConfigureAwait(false);
        return await store.TillAsync(c.PropertyId, ct).ConfigureAwait(false);
    }
}
