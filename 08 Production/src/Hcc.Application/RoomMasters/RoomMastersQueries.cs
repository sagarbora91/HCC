using Hcc.Application.Access;
using Hcc.Application.Masters;
using Hcc.Contracts.Masters;
using Hcc.Contracts.RoomMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Application.RoomMasters;

/// <summary>
/// Rooms masters reads. Lists for every signed-in user (room-type names, amenities, room floors label the boards, pickers and the booking
/// site); money (rack rates, extra-charge amounts) only for a principal with money visibility — PERM-MST-002 or the reservations read
/// PERM-RES-001 (rates are quoted at the front desk) — and empty for everyone else (DEC-061). History: PERM-MST-001, occupancy charges
/// PERM-MST-002.
/// </summary>
public sealed class RoomMastersQueries(IAccessPolicy policy, IRoomMastersReadStore reads, IMastersReadStore history)
{
    public const string RatesRead = "PERM-RES-001";

    private async Task<bool> MoneyAsync(int propertyId, int principalId, CancellationToken ct) =>
        await policy.HasAsync(propertyId, principalId, MastersPermissions.ManageMoney, null, ct).ConfigureAwait(false)
        || await policy.HasAsync(propertyId, principalId, RatesRead, null, ct).ConfigureAwait(false);

    private static string Status(string? status)
    {
        var st = status ?? "all";
        return st is "all" or "active" or "retired" ? st : throw HccException.Validation("status must be active, retired or all.");
    }

    private static bool Keep(string st, bool active) => st == "all" || active == (st == "active");

    public async Task<RoomTypeList> RoomTypesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        var p = MastersRules.Page(page, pageSize);
        var st = Status(status);
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var all = (await reads.RoomTypesAsync(propertyId, ct).ConfigureAwait(false)).Where(x => Keep(st, x.Type.Active)).ToList();
        return new RoomTypeList(p.Page, p.PageSize, all.Count, all.Skip(p.Skip).Take(p.PageSize)
            .Select(x => RoomMastersCommands.View(x.Type, x.Rooms, money ? RoomMastersCommands.RackList(x.Type.RackPaise, x.Tariffs) : [])).ToList());
    }

    public async Task<AmenityList> AmenitiesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        _ = principalId;
        var p = MastersRules.Page(page, pageSize);
        var st = Status(status);
        var all = (await reads.AmenitiesAsync(propertyId, ct).ConfigureAwait(false)).Where(x => Keep(st, x.Active)).ToList();
        return new AmenityList(p.Page, p.PageSize, all.Count, all.Skip(p.Skip).Take(p.PageSize).Select(RoomMastersCommands.View).ToList());
    }

    public async Task<OccupancyChargeList> OccupancyChargesAsync(int propertyId, int principalId, int? page = null, int? pageSize = null, string? status = null, CancellationToken ct = default)
    {
        var p = MastersRules.Page(page, pageSize);
        var st = Status(status);
        var money = await MoneyAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var all = (await reads.OccupancyChargesAsync(propertyId, ct).ConfigureAwait(false)).Where(x => Keep(st, x.Active)).ToList();
        return new OccupancyChargeList(p.Page, p.PageSize, all.Count, all.Skip(p.Skip).Take(p.PageSize).Select(x => RoomMastersCommands.View(x, money)).ToList());
    }

    /// <summary>Every room with its master fields (floor, type, amenities, notes, retired) — no money, no guests.</summary>
    public async Task<RoomMasterList> RoomsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        _ = principalId;
        return new RoomMasterList((await reads.RoomsAsync(propertyId, ct).ConfigureAwait(false)).Select(RoomMastersCommands.View).ToList());
    }

    /// <summary>Change history of one list (master id from <see cref="RoomMasterIds"/>), optionally of one row.</summary>
    public async Task<MasterHistoryList> HistoryAsync(int propertyId, int principalId, string master, long? rowId = null, int? page = null, int? pageSize = null, CancellationToken ct = default)
    {
        var perm = master == RoomMasterIds.OccupancyCharges ? MastersPermissions.ManageMoney : MastersPermissions.Manage;
        await policy.RequireAsync(propertyId, principalId, perm, null, null, ct).ConfigureAwait(false);
        var p = MastersRules.Page(page, pageSize);
        var (total, items) = await history.HistoryAsync(propertyId, master, rowId, p.Skip, p.PageSize, ct).ConfigureAwait(false);
        return new MasterHistoryList(p.Page, p.PageSize, total, items);
    }
}
