using Hcc.Application.Commands;
using Hcc.Domain.Maintenance;

namespace Hcc.Application.Maintenance;

/// <summary>
/// PERM ids of Maintenance: the module's own set from role matrix v2 (DEC-079, migration 3700) instead of the borrowed housekeeping, room and
/// finance-report ids. Reception holds none of them (it raises repairs through a guest request, DEC-063 G7); money rides only on
/// <see cref="Money"/> and <see cref="Amc"/>, which the Cashier, Supervisor, Housekeeper and Maintenance do not hold (D3, DEC-061).
/// </summary>
public static class MaintenancePermissions
{
    /// <summary>Use the module at all (Owner, Manager, Supervisor property-wide; Housekeeper, Maintenance assigned scope).</summary>
    public const string View = "PERM-MNT-001";

    /// <summary>Assign work orders (supervisor level).</summary>
    public const string Assign = "PERM-MNT-003";

    /// <summary>
    /// Housekeeping task work (a core housekeeping id, not a maintenance grant): identifies a Housekeeper, who cannot create work orders and
    /// reports repairs from the task card, and sees only the work orders of rooms with an open task of theirs.
    /// </summary>
    public const string HousekeeperWork = "PERM-HK-003";

    /// <summary>Supervisor-level control: verify, cancel, PM generation, registers, reports.</summary>
    public const string Manage = "PERM-MNT-003";

    /// <summary>Technician work on an assigned work order (start, hold, parts, complete) and a repair from an assigned task — assigned scope.</summary>
    public const string Work = "PERM-MNT-002";

    /// <summary>Apply / lift a room block (downtime).</summary>
    public const string Block = "PERM-MNT-003";

    /// <summary>Reveal money: part unit prices, order cost, parts cost in reports. Owner, Manager, Accountant (view) property-wide.</summary>
    public const string Money = "PERM-MNT-004";

    /// <summary>AMC contracts (cost, renewal): Owner and Manager edit, the Accountant reads (DEC-063 G12).</summary>
    public const string Amc = "PERM-MNT-005";
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
