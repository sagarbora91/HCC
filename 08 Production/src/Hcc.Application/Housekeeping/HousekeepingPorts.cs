using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts.Housekeeping;
using Hcc.Domain.Housekeeping;

namespace Hcc.Application.Housekeeping;

/// <summary>PERM ids (RBAC matrix v1 §5) used by the Housekeeping module.</summary>
public static class HousekeepingPermissions
{
    public const string Board = "PERM-HK-001";
    public const string Assign = "PERM-HK-002";
    public const string Work = "PERM-HK-003";
    public const string Inspect = "PERM-HK-004";
    public const string BlockRequest = "PERM-ROOM-001";
    public const string BlockApply = "PERM-ROOM-002";

    /// <summary>
    /// Working a repair task (start/complete). The matrix has no maintenance work-order PERM yet; Maintenance holds
    /// PERM-ROOM-001 with assigned scope, so it stands in until a PERM-MNT-* id is added (decision listed in the P3 report).
    /// </summary>
    public const string RepairWork = BlockRequest;

    public static string WorkFor(string kind) => kind == HkTaskKind.Repair ? RepairWork : Work;
}

// Scope conventions: Hcc.Application.Access.AccessScopes / AccessScopeKinds (one set for every module since the Wave 1
// integration). Housekeeping uses HkTask(taskId) and Room(roomId) (assigned scope "A", resolved by
// Infrastructure.SqlServer.Housekeeping.SqlHousekeepingScopeResolver), Property(...) to ask "property-wide?", and Any for
// list screens that then filter to the principal's own tasks. Room blocks reach inventory through
// Hcc.Application.Inventory.IRoomBlockPort (P2), called inside the Block/Unblock command transaction.

/// <summary>
/// Port toward P1: may this principal be assigned a task of this kind (prototype: enabled Housekeeper for cleans, enabled
/// Maintenance user for repairs)? P1's staff/role directory should implement it; <see cref="PolicyTaskAssigneeEligibility"/>
/// is the interim rule over <see cref="IAccessPolicy"/>.
/// </summary>
public interface ITaskAssigneeEligibility
{
    Task<bool> CanBeAssignedAsync(int propertyId, int principalId, string taskKind, CancellationToken ct = default);
}

/// <summary>
/// Interim eligibility from grants: cleans need PERM-HK-003; repairs need PERM-ROOM-001 without PERM-HK-003 (the
/// Maintenance profile in RBAC v1). Disabled users hold no grants, so they are never eligible.
/// </summary>
public sealed class PolicyTaskAssigneeEligibility(IAccessPolicy policy) : ITaskAssigneeEligibility
{
    public async Task<bool> CanBeAssignedAsync(int propertyId, int principalId, string taskKind, CancellationToken ct = default)
    {
        if (principalId <= 0) return false;
        var cleans = await policy.HasAsync(propertyId, principalId, HousekeepingPermissions.Work, AccessScopes.Any, ct).ConfigureAwait(false);
        if (taskKind != HkTaskKind.Repair) return cleans;
        return !cleans && await policy.HasAsync(propertyId, principalId, HousekeepingPermissions.RepairWork, AccessScopes.Any, ct).ConfigureAwait(false);
    }
}

/// <summary>Housekeeping settings. Inspection policy belongs to Master configuration (DEC-057); default = prototype (on).</summary>
public sealed class HousekeepingOptions
{
    public bool InspectionRequired { get; set; } = true;
}

public sealed record HkTaskRecord(long TaskId, int PropertyId, int RoomId, string RoomNo, string Kind, string Status, int ChecklistMask, int? AssigneeId, string? RejectReason, byte[] Version)
{
    public HkTaskState State => new(Kind, Status, ChecklistMask, AssigneeId, RejectReason);
}

public sealed record HkRoomRecord(int RoomId, int PropertyId, string RoomNo, string Hk);

public sealed record RoomBlockRecord(long BlockId, int RoomId, DateOnly? From, DateOnly? To, string Reason, string Source, byte[] Version);

/// <summary>Persistence port of the Housekeeping commands (SQL adapter in Infrastructure/Housekeeping).</summary>
public interface IHousekeepingStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<HkTaskRecord?> LockTaskAsync(ICommandTransaction tx, int propertyId, long taskId, CancellationToken ct = default);

    /// <summary>Applies a transition (versioned; STALE_VERSION on mismatch).</summary>
    Task UpdateTaskAsync(ICommandTransaction tx, long taskId, byte[] expectedVersion, HkTransition t, int principalId, DateTime utc, CancellationToken ct = default);

    Task<HousekeepingTaskView> TaskViewAsync(ICommandTransaction tx, long taskId, CancellationToken ct = default);

    Task<HkRoomRecord?> LockRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default);

    Task SetRoomHkAsync(ICommandTransaction tx, int roomId, string hk, CancellationToken ct = default);

    /// <summary>True when the principal has a not-Done task on the room (assigned scope for repair requests).</summary>
    Task<bool> HasOpenTaskAssignedAsync(ICommandTransaction tx, int roomId, int principalId, CancellationToken ct = default);

    Task<bool> HasOpenRepairAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default);

    Task<long> InsertTaskAsync(ICommandTransaction tx, int propertyId, int roomId, string kind, string? note, DateOnly businessDate, DateTime utc, CancellationToken ct = default);

    Task<RoomBlockRecord?> LockActiveBlockAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default);

    /// <summary>Reservations holding the room's inventory on any night of [from, to): Confirmed, InHouse, Held with a live hold.</summary>
    Task<int> AllocationsOverlappingAsync(ICommandTransaction tx, int roomId, DateOnly from, DateOnly? to, DateTime utcNow, CancellationToken ct = default);

    Task<(long BlockId, byte[] Version)> InsertBlockAsync(ICommandTransaction tx, int propertyId, int roomId, DateOnly from, DateOnly? to, string reason, string source, int principalId, DateTime utc, CancellationToken ct = default);

    Task ReleaseBlockAsync(ICommandTransaction tx, long blockId, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);
}

/// <summary>Raw board data for one room (read side).</summary>
public sealed record BoardRoomData(int RoomId, string RoomNo, string RoomType, string Hk, byte[] Version, RoomBlockRecord? Block, BoardGuest? Current, BoardGuest? Next);

public interface IHousekeepingReadStore
{
    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<BoardRoomData>> BoardAsync(int propertyId, CancellationToken ct = default);

    /// <summary>Open (not Done) tasks; <paramref name="assigneeId"/> filters to one principal's tasks.</summary>
    Task<IReadOnlyList<HousekeepingTaskView>> OpenTasksAsync(int propertyId, int? assigneeId, CancellationToken ct = default);

    /// <summary>The task's kind (immutable) for choosing the PERM before the transaction; null when absent.</summary>
    Task<string?> TaskKindAsync(int propertyId, long taskId, CancellationToken ct = default);
}
