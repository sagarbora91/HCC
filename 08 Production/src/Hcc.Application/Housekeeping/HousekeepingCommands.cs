using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Housekeeping;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Housekeeping;

namespace Hcc.Application.Housekeeping;

/// <summary>
/// Housekeeping use cases: TransitionHousekeepingTask (WF-HK-001), BlockRoom / UnblockRoom (room out of order, PERM-ROOM-002),
/// RequestRepair (PERM-ROOM-001). Housekeepers and maintenance act only on records assigned to them (RBAC scope A):
/// the policy is asked with an <see cref="AccessScopes.HkTask"/>/<see cref="AccessScopes.Room"/> scope, and the handler
/// re-checks the assignee under the row lock when the principal is not property-wide.
/// </summary>
public sealed class HousekeepingCommands(ICommandRunner runner, IAccessPolicy policy, IHousekeepingStore store, IHousekeepingReadStore reads, ITaskAssigneeEligibility eligibility, HousekeepingOptions options, TimeProvider clock)
{
    public const string OpTransition = "TransitionHousekeepingTask";
    public const string OpBlockRoom = "BlockRoom";
    public const string OpUnblockRoom = "UnblockRoom";
    public const string OpRequestRepair = "RequestRepair";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<CommandResult<TransitionHousekeepingTaskResult>> TransitionAsync(CommandContext ctx, TransitionHousekeepingTaskRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        var action = (req.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (!HkAction.All.Contains(action)) throw HccException.Validation("Unknown task action.");

        var kind = await reads.TaskKindAsync(ctx.PropertyId, req.TaskId, ct).ConfigureAwait(false);
        // Board permission first so a principal with no housekeeping access cannot probe task ids.
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HousekeepingPermissions.Board, null, null, ct).ConfigureAwait(false);
        if (kind is null) throw new HccException(HccErrorCode.NOT_FOUND, "Task not found.");
        var perm = action switch
        {
            HkAction.Assign => HousekeepingPermissions.Assign,
            HkAction.Inspect or HkAction.Reject => HousekeepingPermissions.Inspect,
            _ => HousekeepingPermissions.WorkFor(kind),
        };
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, perm, AccessScopes.HkTask(req.TaskId), null, ct).ConfigureAwait(false);
        var wide = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, perm, AccessScopes.Property(ctx.PropertyId), ct).ConfigureAwait(false);
        if (action == HkAction.Assign)
        {
            if (req.AssigneeId is not { } a || !await eligibility.CanBeAssignedAsync(ctx.PropertyId, a, kind, ct).ConfigureAwait(false))
                throw HccException.Validation(kind == HkTaskKind.Repair ? "Select an enabled maintenance user." : "Select an enabled housekeeper.");
        }

        return await runner.RunAsync(ctx with { Operation = OpTransition }, req, async (tx, c) =>
        {
            var task = await store.LockTaskAsync(tx, ctx.PropertyId, req.TaskId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Task not found.");
            if (!wide && task.AssigneeId != ctx.PrincipalId)
                throw new HccException(HccErrorCode.FORBIDDEN, "Only your assigned tasks are accessible.");
            VersionToken.EnsureMatches(task.Version, expected, "This task");
            var t = HkTaskMachine.Apply(task.State, action, options.InspectionRequired, req.AssigneeId, req.Index, req.Reason);
            await store.UpdateTaskAsync(tx, task.TaskId, expected, t, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var room = await store.LockRoomAsync(tx, ctx.PropertyId, task.RoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            var hk = room.Hk;
            if (t.RoomHk is { } newHk && newHk != room.Hk)
            {
                await store.SetRoomHkAsync(tx, room.RoomId, newHk, c).ConfigureAwait(false);
                hk = newHk;
            }

            await tx.AuditAsync("TASK", Target("hk-task", task.TaskId), $"{room.RoomNo} · {t.Detail}", c).ConfigureAwait(false);
            var view = await store.TaskViewAsync(tx, task.TaskId, c).ConfigureAwait(false);
            return new TransitionHousekeepingTaskResult(view, hk, $"{room.RoomNo} · {t.Detail}");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<BlockRoomResult>> BlockRoomAsync(CommandContext ctx, BlockRoomRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HousekeepingPermissions.BlockApply, AccessScopes.Room(req.RoomId), null, ct).ConfigureAwait(false);
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 3) throw HccException.Validation("Enter a reason for the block.");
        if (reason.Length > 200) throw HccException.Validation("The reason is at most 200 characters.");
        DateOnly? fromReq = string.IsNullOrEmpty(req.From) ? null : ParseDate(req.From);
        DateOnly? to = string.IsNullOrEmpty(req.To) ? null : ParseDate(req.To);

        return await runner.RunAsync(ctx with { Operation = OpBlockRoom }, req, async (tx, c) =>
        {
            var room = await store.LockRoomAsync(tx, ctx.PropertyId, req.RoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var from = fromReq ?? bd;
            if (from < bd) throw HccException.Validation("A block cannot start before the current business date.");
            if (to is { } t && t <= from) throw HccException.Validation("The block must end after it starts.");
            if (await store.AllocationsOverlappingAsync(tx, room.RoomId, from, to, UtcNow, c).ConfigureAwait(false) > 0)
                throw new HccException(HccErrorCode.NOT_AVAILABLE, "Resolve active and future allocations before blocking this room.");
            // Prototype BLOCK on a blocked room replaces the block (one active block per room).
            var current = await store.LockActiveBlockAsync(tx, room.RoomId, c).ConfigureAwait(false);
            if (current is not null) await store.ReleaseBlockAsync(tx, current.BlockId, current.Version, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var (blockId, version) = await store.InsertBlockAsync(tx, ctx.PropertyId, room.RoomId, from, to, reason, "manual", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var range = $"{StayDates.ToWire(from)} → {(to is { } x ? StayDates.ToWire(x) : "open-ended")}";
            await tx.AuditAsync("BLOCK", Target("room", room.RoomId), $"Room {room.RoomNo} · blocked for {reason} · {range}", c).ConfigureAwait(false);
            return new BlockRoomResult(blockId, room.RoomId, room.RoomNo, StayDates.ToWire(from), to is { } y ? StayDates.ToWire(y) : null, VersionToken.ToWire(version));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<UnblockRoomResult>> UnblockRoomAsync(CommandContext ctx, UnblockRoomRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HousekeepingPermissions.BlockApply, AccessScopes.Room(req.RoomId), null, ct).ConfigureAwait(false);

        return await runner.RunAsync(ctx with { Operation = OpUnblockRoom }, req, async (tx, c) =>
        {
            var room = await store.LockRoomAsync(tx, ctx.PropertyId, req.RoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            var block = await store.LockActiveBlockAsync(tx, room.RoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This room is not blocked.");
            VersionToken.EnsureMatches(block.Version, expected, "This block");
            await store.ReleaseBlockAsync(tx, block.BlockId, expected, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("UNBLOCK", Target("room", room.RoomId), $"Room {room.RoomNo} · unblocked (was: {block.Reason})", c).ConfigureAwait(false);
            return new UnblockRoomResult(block.BlockId, room.RoomId, room.RoomNo);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RequestRepairResult>> RequestRepairAsync(CommandContext ctx, RequestRepairRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, HousekeepingPermissions.BlockRequest, AccessScopes.Room(req.RoomId), null, ct).ConfigureAwait(false);
        var wide = await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, HousekeepingPermissions.BlockRequest, AccessScopes.Property(ctx.PropertyId), ct).ConfigureAwait(false);
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length < 3) throw HccException.Validation("Describe the repair needed.");
        if (reason.Length > 500) throw HccException.Validation("The description is at most 500 characters.");

        return await runner.RunAsync(ctx with { Operation = OpRequestRepair }, req, async (tx, c) =>
        {
            var room = await store.LockRoomAsync(tx, ctx.PropertyId, req.RoomId, c).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            if (!wide && !await store.HasOpenTaskAssignedAsync(tx, room.RoomId, ctx.PrincipalId, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.FORBIDDEN, "You can report repairs only for rooms assigned to you.");
            if (await store.HasOpenRepairAsync(tx, room.RoomId, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A repair request is already open for this room.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var taskId = await store.InsertTaskAsync(tx, ctx.PropertyId, room.RoomId, HkTaskKind.Repair, reason, bd, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("REPAIR_REQUEST", Target("hk-task", taskId), $"Room {room.RoomNo} · repair requested ({reason})", c).ConfigureAwait(false);
            return new RequestRepairResult(taskId, room.RoomId, room.RoomNo);
        }, ct).ConfigureAwait(false);
    }

    private static DateOnly ParseDate(string value) => DateOnly.TryParseExact(value, StayDates.WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        ? d : throw HccException.Validation("Enter valid block dates.");

    private static string Target(string kind, long id) => kind + "-" + id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Room status board and task lists (PERM-HK-001; housekeepers/maintenance see only rooms/tasks assigned to them). No money fields.</summary>
public sealed class HousekeepingQueries(IAccessPolicy policy, IHousekeepingReadStore reads)
{
    public async Task<RoomBoardPage> BoardAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        await policy.RequireAsync(propertyId, principalId, HousekeepingPermissions.Board, null, null, ct).ConfigureAwait(false);
        var wide = await policy.HasAsync(propertyId, principalId, HousekeepingPermissions.Board, AccessScopes.Property(propertyId), ct).ConfigureAwait(false);
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var rooms = await reads.BoardAsync(propertyId, ct).ConfigureAwait(false);
        var tasks = await reads.OpenTasksAsync(propertyId, wide ? null : principalId, ct).ConfigureAwait(false);
        var byRoom = tasks.GroupBy(t => t.RoomId).ToDictionary(g => g.Key, g => (IReadOnlyList<HousekeepingTaskView>)g.ToList());
        var rows = rooms
            .Where(r => wide || byRoom.ContainsKey(r.RoomId))
            .Select(r =>
            {
                var span = r.Block is null ? null : new RoomBlockSpan(r.Block.From, r.Block.To);
                var status = RoomBoardRules.RoomStatus(span, bd, r.Current is not null, r.Hk);
                var block = r.Block is null ? null : new RoomBlockView(r.Block.BlockId, r.Block.From is { } f ? StayDates.ToWire(f) : null,
                    r.Block.To is { } t ? StayDates.ToWire(t) : null, r.Block.Reason, r.Block.Source, VersionToken.ToWire(r.Block.Version));
                return new RoomBoardRow(r.RoomId, r.RoomNo, r.RoomType, r.Hk, status, block, r.Current, r.Next,
                    byRoom.TryGetValue(r.RoomId, out var ts) ? ts : [], VersionToken.ToWire(r.Version));
            })
            .OrderBy(r => r.RoomNo, StringComparer.Ordinal)
            .ToList();
        var counts = new RoomBoardCounts(rows.Count,
            rows.Count(r => r.Status == RoomBoardRules.Occupied),
            rows.Count(r => r.Status == RoomHk.Ready),
            rows.Count(r => r.Status != RoomBoardRules.Blocked),
            rows.Count(r => r.Status == RoomHk.Dirty),
            rows.Count(r => r.Status == RoomHk.Cleaning),
            rows.Count(r => r.Status == RoomHk.Inspection),
            rows.Count(r => r.Status == RoomBoardRules.Blocked));
        return new RoomBoardPage(StayDates.ToWire(bd), page, pageSize, rows.Count, rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), counts, !wide);
    }

    public async Task<HousekeepingTaskPage> OpenTasksAsync(int propertyId, int principalId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging(page, pageSize);
        await policy.RequireAsync(propertyId, principalId, HousekeepingPermissions.Board, null, null, ct).ConfigureAwait(false);
        var wide = await policy.HasAsync(propertyId, principalId, HousekeepingPermissions.Board, AccessScopes.Property(propertyId), ct).ConfigureAwait(false);
        var tasks = await reads.OpenTasksAsync(propertyId, wide ? null : principalId, ct).ConfigureAwait(false);
        return new HousekeepingTaskPage(page, pageSize, tasks.Count, tasks.Skip((page - 1) * pageSize).Take(pageSize).ToList(), !wide);
    }

    private static (int Page, int PageSize) Paging(int page, int pageSize)
    {
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        return (page, pageSize);
    }
}
