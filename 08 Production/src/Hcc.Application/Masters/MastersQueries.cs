using Hcc.Application.Access;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Application.Masters;

/// <summary>
/// Masters reads: GET /masters/floors (any signed-in user — floor names label the room and housekeeping boards; no money), and the
/// change history GET /masters/floors/history and /masters/floors/{id}/history (PERM-MST-001, the people who may change the list).
/// </summary>
public sealed class MastersQueries(IAccessPolicy policy, IMastersReadStore reads)
{
    public async Task<FloorList> FloorsAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId; // session checked by the endpoint group; floors carry no money and no guest data
        var p = MastersRules.Page(page, pageSize);
        var st = status ?? "all";
        if (st is not ("all" or "active" or "retired")) throw HccException.Validation("status must be active, retired or all.");
        var all = (await reads.FloorsAsync(propertyId, ct).ConfigureAwait(false))
            .Where(x => st == "all" || x.Floor.Active == (st == "active")).ToList();
        return new FloorList(p.Page, p.PageSize, all.Count, all.Skip(p.Skip).Take(p.PageSize).Select(x => MastersCommands.View(x.Floor, x.Rooms)).ToList());
    }

    public async Task<MasterHistoryList> FloorHistoryAsync(int propertyId, int principalId, int? floorId = null, int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, MastersPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var (total, items) = await reads.HistoryAsync(propertyId, MasterIds.Floors, floorId, p.Skip, p.PageSize, ct).ConfigureAwait(false);
        return new MasterHistoryList(p.Page, p.PageSize, total, items);
    }
}
