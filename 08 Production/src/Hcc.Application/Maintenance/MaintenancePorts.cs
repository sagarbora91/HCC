using Hcc.Application.Commands;
using Hcc.Domain.Maintenance;

namespace Hcc.Application.Maintenance;

/// <summary>
/// PERM ids used by Maintenance. No new permissions: the RBAC matrix (v1 + DEC-063) has no maintenance PERM, so the module rides
/// the housekeeping/room/report grants exactly as the prototype's <c>can()</c> checks did (see the W2-7 report for the PERM-MNT-* decision).
/// </summary>
public static class MaintenancePermissions
{
    /// <summary>Use the module at all (Owner, Manager, Supervisor property-wide; Housekeeper, Maintenance assigned scope).</summary>
    public const string View = "PERM-HK-001";

    /// <summary>Assign work orders (prototype <c>inspect</c> = supervisor level).</summary>
    public const string Assign = "PERM-HK-002";

    /// <summary>Housekeeping task work: marks a Housekeeper (cannot create work orders; reports repairs from the task card).</summary>
    public const string HousekeeperWork = "PERM-HK-003";

    /// <summary>Supervisor-level control: verify, cancel, PM generation, registers (prototype <c>inspect</c>).</summary>
    public const string Manage = "PERM-HK-004";

    /// <summary>Technician work on an assigned work order (start, hold, parts, complete) — assigned scope.</summary>
    public const string Work = "PERM-ROOM-001";

    /// <summary>Apply / lift a room block (downtime).</summary>
    public const string Block = "PERM-ROOM-002";

    /// <summary>Finance rights: part unit prices, AMC contract values (DEC-063 G12). Owner, Manager, Accountant property-wide.</summary>
    public const string Money = "PERM-RPT-002";
}

/// <summary>An open Housekeeping repair task (ops.hk_task kind repair) as Maintenance needs it.</summary>
public sealed record RepairTaskRecord(long TaskId, int RoomId, string RoomNo, string Status, int? AssigneeId, string? Note, DateTime CreatedUtc, DateTime? StartedUtc);

/// <summary>History line of a work order.</summary>
public sealed record MntHistoryRow(DateTime AtUtc, int By, string Action, string? Note);

/// <summary>Everything the read side needs for a property in one load (a hotel's maintenance data is small).</summary>
public sealed record MntSnapshot(
    DateOnly BusinessDate, IReadOnlyList<MntOrder> Orders, IReadOnlyList<MntAsset> Assets, IReadOnlyList<MntVendor> Vendors,
    IReadOnlyList<MntSpare> Spares, IReadOnlyList<MntPlan> Plans, IReadOnlyList<MntAmc> Amcs, ISet<int> BlockedRoomIds, int PendingRepairs);

/// <summary>Writes of the Maintenance commands (SQL adapter in Hcc.Infrastructure.SqlServer/Maintenance). Room blocks are NOT here: they go through IHousekeepingStore + IRoomBlockPort.</summary>
public interface IMaintenanceStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<MntOrder?> LockOrderAsync(ICommandTransaction tx, int propertyId, long workOrderId, CancellationToken ct = default);

    /// <summary>Inserts the draft (its Id/Version are ignored) with its parts; returns the new id and version.</summary>
    Task<(long Id, byte[] Version)> InsertOrderAsync(ICommandTransaction tx, int propertyId, MntOrder draft, CancellationToken ct = default);

    /// <summary>Writes every mutable column and the parts; STALE_VERSION when the row changed. Returns the new version.</summary>
    Task<byte[]> UpdateOrderAsync(ICommandTransaction tx, MntOrder order, byte[] expectedVersion, CancellationToken ct = default);

    Task AddHistoryAsync(ICommandTransaction tx, long workOrderId, DateTime utc, int by, string action, string? note, CancellationToken ct = default);

    Task<bool> OrderExistsForRepairAsync(ICommandTransaction tx, long repairTaskId, CancellationToken ct = default);

    Task<bool> OccurrenceExistsAsync(ICommandTransaction tx, int planId, DateOnly due, CancellationToken ct = default);

    Task<bool> OtherActiveOrderOnAssetAsync(ICommandTransaction tx, int assetId, long exceptWorkOrderId, CancellationToken ct = default);

    // Registers
    Task<MntSpare?> LockSpareAsync(ICommandTransaction tx, int propertyId, int spareId, CancellationToken ct = default);

    Task<byte[]> SetSpareStockAsync(ICommandTransaction tx, int spareId, int stock, CancellationToken ct = default);

    Task<MntAsset?> LockAssetAsync(ICommandTransaction tx, int propertyId, int assetId, CancellationToken ct = default);

    Task<(int Id, byte[] Version)> InsertAssetAsync(ICommandTransaction tx, int propertyId, MntAsset draft, CancellationToken ct = default);

    Task<byte[]> UpdateAssetAsync(ICommandTransaction tx, MntAsset asset, byte[] expectedVersion, CancellationToken ct = default);

    Task SetAssetStatusAsync(ICommandTransaction tx, int assetId, string status, CancellationToken ct = default);

    Task<bool> VendorExistsAsync(ICommandTransaction tx, int propertyId, int vendorId, CancellationToken ct = default);

    Task<MntVendor?> LockVendorAsync(ICommandTransaction tx, int propertyId, int vendorId, CancellationToken ct = default);

    Task<(int Id, byte[] Version)> InsertVendorAsync(ICommandTransaction tx, int propertyId, MntVendor draft, CancellationToken ct = default);

    Task<byte[]> UpdateVendorAsync(ICommandTransaction tx, MntVendor vendor, byte[] expectedVersion, CancellationToken ct = default);

    Task<MntAmc?> LockAmcAsync(ICommandTransaction tx, int propertyId, int amcId, CancellationToken ct = default);

    Task<(int Id, byte[] Version)> InsertAmcAsync(ICommandTransaction tx, int propertyId, MntAmc draft, CancellationToken ct = default);

    Task<byte[]> UpdateAmcAsync(ICommandTransaction tx, MntAmc amc, byte[] expectedVersion, CancellationToken ct = default);

    Task<MntPlan?> LockPlanAsync(ICommandTransaction tx, int propertyId, int planId, CancellationToken ct = default);

    Task<IReadOnlyList<MntPlan>> LockActivePlansAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<(int Id, byte[] Version)> InsertPlanAsync(ICommandTransaction tx, int propertyId, MntPlan draft, CancellationToken ct = default);

    Task<byte[]> UpdatePlanAsync(ICommandTransaction tx, MntPlan plan, byte[] expectedVersion, CancellationToken ct = default);

    Task SetPlanLastDoneAsync(ICommandTransaction tx, int planId, DateOnly date, CancellationToken ct = default);

    // Housekeeping repair tasks (ops.hk_task kind repair) — the repair task follows its work order (prototype handlers).
    Task<RepairTaskRecord?> LockRepairTaskAsync(ICommandTransaction tx, int propertyId, long taskId, CancellationToken ct = default);

    Task SetRepairAssigneeAsync(ICommandTransaction tx, long taskId, int assigneeId, CancellationToken ct = default);

    Task SetRepairStartedAsync(ICommandTransaction tx, long taskId, DateTime utc, CancellationToken ct = default);

    Task SetRepairDoneAsync(ICommandTransaction tx, long taskId, DateTime utc, CancellationToken ct = default);

    // Staff: enabled users holding the Maintenance role (interim, like PolicyTaskAssigneeEligibility for repairs).
    Task<bool> IsTechnicianAsync(ICommandTransaction tx, int propertyId, int userId, CancellationToken ct = default);

    Task<int?> FirstTechnicianAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);
}

public interface IMaintenanceReadStore
{
    Task<MntSnapshot> LoadAsync(int propertyId, DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<MntHistoryRow>> HistoryAsync(long workOrderId, CancellationToken ct = default);

    /// <summary>Rooms of the principal's own open (not Done) housekeeping tasks (Housekeepers see open work orders there).</summary>
    Task<ISet<int>> OwnOpenTaskRoomsAsync(int propertyId, int principalId, CancellationToken ct = default);
}
