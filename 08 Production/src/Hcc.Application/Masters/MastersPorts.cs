using Hcc.Application.Commands;
using Hcc.Contracts.Masters;

namespace Hcc.Application.Masters;

/// <summary>
/// PERM ids of the Masters module (migration 2801, DEC-MA-1): PERM-MST-001 = change operational lists (Owner, Tech admin; grantable),
/// PERM-MST-002 = change money lists and money fields (Owner). Reading floors needs only a session: floor names label every board.
/// </summary>
public static class MastersPermissions
{
    public const string Manage = "PERM-MST-001";
    public const string ManageMoney = "PERM-MST-002";
}

/// <summary>A floor row as stored.</summary>
public sealed record FloorRec(int FloorId, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, DateTime UpdatedUtc, byte[] RowVer);

/// <summary>How many records use a master row, and where (counts with a short label each).</summary>
public sealed record MasterUsageCount(int Count, IReadOnlyList<string> Where)
{
    public static readonly MasterUsageCount None = new(0, []);
}

/// <summary>
/// "In use" port of the masters framework (plan §9.1): a module that references a master row registers one implementation for that
/// master (e.g. Inventory counts rooms per floor). The commands sum every registered provider of the master. Runs inside the command
/// transaction so the decision (delete / code change) is made on locked data.
/// </summary>
public interface IMasterUsage
{
    /// <summary>The master ids this provider counts (MasterIds.*).</summary>
    IReadOnlyCollection<string> Masters { get; }

    Task<MasterUsageCount> CountAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct = default);
}

/// <summary>Write side (inside the command transaction; rows locked with UPDLOCK).</summary>
public interface IMastersStore
{
    Task<FloorRec?> LockFloorAsync(ICommandTransaction tx, int propertyId, int floorId, CancellationToken ct = default);

    /// <summary>Every floor of the property, locked (reorder, next sort order, code uniqueness).</summary>
    Task<IReadOnlyList<FloorRec>> LockFloorsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<int> InsertFloorAsync(ICommandTransaction tx, int propertyId, string code, string name, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateFloorAsync(ICommandTransaction tx, int floorId, byte[] expectedVersion, string code, string name, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetFloorActiveAsync(ICommandTransaction tx, int floorId, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetFloorSortAsync(ICommandTransaction tx, int floorId, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task DeleteFloorAsync(ICommandTransaction tx, int floorId, byte[] expectedVersion, CancellationToken ct = default);

    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task AddHistoryAsync(ICommandTransaction tx, int propertyId, string master, long rowId, string? code, string field, string? oldValue, string? newValue, int principalId, DateTime utc, CancellationToken ct = default);
}

/// <summary>Read side (own connection).</summary>
public interface IMastersReadStore
{
    /// <summary>Floors in sort order with the number of rooms on each.</summary>
    Task<IReadOnlyList<(FloorRec Floor, int Rooms)>> FloorsAsync(int propertyId, CancellationToken ct = default);

    Task<(int Total, IReadOnlyList<MasterHistoryView> Items)> HistoryAsync(int propertyId, string master, long? rowId, int skip, int take, CancellationToken ct = default);
}
