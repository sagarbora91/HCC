using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Application.Inventory;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Maintenance;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Maintenance;

namespace Hcc.Application.Maintenance;

/// <summary>
/// Maintenance use cases (prototype module 15, handlers/maintenance.ts). Technicians act only on work orders assigned to them
/// (RBAC scope A through the "workOrder" scope resolver); supervisors/managers act property-wide. Room downtime goes through the
/// existing room-block port: the Housekeeping block record (<see cref="IHousekeepingStore"/>) plus inventory (<see cref="IRoomBlockPort"/>),
/// both inside the command transaction. Money (part prices, AMC values) never reaches Maintenance (DEC-063 G12).
/// </summary>
public sealed partial class MaintenanceCommands(ICommandRunner runner, IAccessPolicy policy, IMaintenanceStore store, IHousekeepingStore housekeeping, IRoomBlockPort roomBlocks, TimeProvider clock)
{
    public const string OpCreate = "CreateWorkOrder";
    public const string OpConvertRepair = "ConvertRepairToWorkOrder";
    public const string OpAssign = "AssignWorkOrder";
    public const string OpStart = "StartWorkOrder";
    public const string OpHold = "HoldWorkOrder";
    public const string OpPart = "AddWorkOrderPart";
    public const string OpComplete = "CompleteWorkOrder";
    public const string OpVerify = "VerifyWorkOrder";
    public const string OpCancel = "CancelWorkOrder";
    public const string OpBlockRoom = "BlockWorkOrderRoom";
    public const string OpGeneratePm = "GeneratePmWorkOrders";
    public const string OpSavePlan = "SavePmPlan";
    public const string OpSaveAsset = "SaveMaintenanceAsset";
    public const string OpSaveVendor = "SaveMaintenanceVendor";
    public const string OpSaveAmc = "SaveAmc";
    public const string OpReceiveSpare = "ReceiveSpare";

    /// <summary><c>inv.room_block.source</c> for blocks placed through housekeeping/maintenance (same constant as housekeeping).</summary>
    private const string InventoryBlockSource = HousekeepingCommands.InventoryBlockSource;

    [GeneratedRegex(@"^[+\d][\d\s-]{6,}$")]
    private static partial Regex PhonePattern();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static string Target(string kind, long id) => kind + "-" + id.ToString(CultureInfo.InvariantCulture);

    private static string Text(string? v, int min, int max, string message)
    {
        var t = (v ?? string.Empty).Trim();
        return t.Length < min || t.Length > max ? throw HccException.Validation(message) : t;
    }

    private static DateOnly Date(string? v, string message) =>
        DateOnly.TryParseExact(v, StayDates.WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw HccException.Validation(message);

    // ── Access helpers ──
    private Task<bool> IsManagerAsync(CommandContext ctx, CancellationToken ct) =>
        policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Manage, AccessScopes.Property(ctx.PropertyId), ct);

    private Task RequireManagerAsync(CommandContext ctx, CancellationToken ct) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Manage, null, null, ct);

    /// <summary>Start / hold / part / complete: a supervisor-level user, or the assigned technician (scope A on the work order).</summary>
    private async Task RequireWorkerAsync(CommandContext ctx, long workOrderId, CancellationToken ct)
    {
        if (await IsManagerAsync(ctx, ct).ConfigureAwait(false)) return;
        // Module access first: Reception holds PERM-ROOM-001 property-wide but has no housekeeping/maintenance access (DEC-063 G7).
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.View, AccessScopes.Any, null, ct).ConfigureAwait(false);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Work, new AccessScope(AccessScopeKinds.WorkOrder, workOrderId), null, ct).ConfigureAwait(false);
    }

    private static MntOrder Require(MntOrder? w, byte[] expected)
    {
        if (w is null) throw new HccException(HccErrorCode.NOT_FOUND, "Work order not found.");
        VersionToken.EnsureMatches(w.Version, expected, "This work order");
        return w;
    }

    private static WorkOrderResult Result(MntOrder w, byte[] version, string detail) => new(w.Id, w.No, w.Status, VersionToken.ToWire(version), detail);

    private async Task<byte[]> SaveAsync(ICommandTransaction tx, MntOrder w, byte[] expected, string action, string? note, string detail, CancellationToken ct)
    {
        var version = await store.UpdateOrderAsync(tx, w, expected, ct).ConfigureAwait(false);
        await store.AddHistoryAsync(tx, w.Id, UtcNow, tx.Context.PrincipalId, action, note, ct).ConfigureAwait(false);
        await tx.AuditAsync("MNT_WO", Target("wo", w.Id), $"{w.No} · {detail}", ct).ConfigureAwait(false);
        return version;
    }

    // ── Work orders ──
    public async Task<CommandResult<WorkOrderResult>> CreateAsync(CommandContext ctx, CreateWorkOrderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.View, AccessScopes.Any, null, ct).ConfigureAwait(false);
        var manager = await IsManagerAsync(ctx, ct).ConfigureAwait(false);
        var housekeeper = !manager && await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.HousekeeperWork, AccessScopes.Any, ct).ConfigureAwait(false);
        if (housekeeper) throw new HccException(HccErrorCode.FORBIDDEN, "Housekeepers report repairs from their task card.");
        var title = MntMachine.Title(req.Title);
        var priority = MntMachine.Priority(req.Priority);
        var notes = string.IsNullOrWhiteSpace(req.Notes) ? null : Text(req.Notes, 1, 500, "Notes are at most 500 characters.");

        return await runner.RunAsync(ctx with { Operation = OpCreate }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var asset = req.AssetId is { } aid ? await store.LockAssetAsync(tx, ctx.PropertyId, aid, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Asset not found.") : null;
            var roomId = req.RoomId ?? asset?.RoomId;
            string? roomNo = null;
            if (roomId is { } rid)
            {
                var room = await housekeeping.LockRoomAsync(tx, ctx.PropertyId, rid, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
                roomNo = room.RoomNo;
            }

            var area = roomId is null ? (string.IsNullOrWhiteSpace(req.Area) ? asset?.Area : Text(req.Area, 1, 60, "The area is at most 60 characters.")) : null;
            if (roomId is null && area is null) throw HccException.Validation("Choose a room, an area or an asset.");
            int? assignee = manager ? req.AssigneeId : ctx.PrincipalId; // technicians raise faults for themselves
            if (manager && assignee is { } a && !await store.IsTechnicianAsync(tx, ctx.PropertyId, a, c).ConfigureAwait(false))
                throw HccException.Validation("Select an enabled maintenance user.");
            var draft = New(MntKind.Corrective, title, priority, roomId, roomNo, asset?.Id, area, assignee, null, ctx.PrincipalId, UtcNow, null, null);
            draft = draft with { Note = notes };
            var (id, version) = await store.InsertOrderAsync(tx, ctx.PropertyId, draft, c).ConfigureAwait(false);
            var w = draft with { Id = id, Version = version };
            if (asset is { Status: "Working" }) await store.SetAssetStatusAsync(tx, asset.Id, "Faulty", c).ConfigureAwait(false);
            await store.AddHistoryAsync(tx, id, UtcNow, ctx.PrincipalId, "Created", notes, c).ConfigureAwait(false);
            await tx.AuditAsync("MNT_WO", Target("wo", id), $"{w.No} · {title} · {priority} · {(roomNo is null ? area : "room " + roomNo)}", c).ConfigureAwait(false);
            await tx.PublishAsync("MaintenanceWorkOrderCreated", id, new { workOrderId = id, no = w.No, priority, roomId }, c).ConfigureAwait(false);
            return Result(w, version, "created");
        }, ct).ConfigureAwait(false);
    }

    private static MntOrder New(string kind, string title, string priority, int? roomId, string? roomNo, int? assetId, string? area, int? assignee, int? vendor, int createdBy, DateTime now, int? planId, DateOnly? due) =>
        new(0, kind, title, priority, MntStatus.Open, roomId, roomNo, assetId, area, assignee, vendor, now, createdBy, null, null, null, null, null,
            null, null, null, null, null, null, planId, due, null, null, null, [], []);

    public async Task<CommandResult<WorkOrderResult>> ConvertRepairAsync(CommandContext ctx, ConvertRepairRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.View, AccessScopes.Any, null, ct).ConfigureAwait(false);
        if (!await IsManagerAsync(ctx, ct).ConfigureAwait(false))
            await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Work, AccessScopes.HkTask(req.TaskId), null, ct).ConfigureAwait(false);
        var priority = req.Priority is null ? MntPriority.P2 : MntMachine.Priority(req.Priority);

        return await runner.RunAsync(ctx with { Operation = OpConvertRepair }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var task = await store.LockRepairTaskAsync(tx, ctx.PropertyId, req.TaskId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Repair request not found.");
            if (task.Status == "Done") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This repair is already done.");
            if (await store.OrderExistsForRepairAsync(tx, task.TaskId, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "A work order already exists for this repair.");
            MntAsset? asset = null;
            if (req.AssetId is { } aid)
            {
                asset = await store.LockAssetAsync(tx, ctx.PropertyId, aid, c).ConfigureAwait(false);
                if (asset is null || asset.RoomId != task.RoomId) asset = null; // prototype: only an asset of that room is linked
            }

            int? assignee = task.AssigneeId is { } t && await store.IsTechnicianAsync(tx, ctx.PropertyId, t, c).ConfigureAwait(false) ? t : null;
            var block = await housekeeping.LockActiveBlockAsync(tx, task.RoomId, c).ConfigureAwait(false);
            var draft = New(MntKind.Corrective, string.IsNullOrWhiteSpace(task.Note) ? "Repair" : Text(task.Note, 1, 200, "Repair note too long."), priority, task.RoomId, task.RoomNo, asset?.Id, null, assignee, null, ctx.PrincipalId, task.CreatedUtc, null, null)
                with { Status = task.Status == "InProgress" ? MntStatus.InProgress : MntStatus.Open, StartedUtc = task.StartedUtc, RepairTaskId = task.TaskId, DowntimeFromUtc = block is null ? null : task.CreatedUtc };
            var (id, version) = await store.InsertOrderAsync(tx, ctx.PropertyId, draft, c).ConfigureAwait(false);
            var w = draft with { Id = id, Version = version };
            await store.AddHistoryAsync(tx, id, UtcNow, ctx.PrincipalId, "Converted from repair request", Target("hk-task", task.TaskId), c).ConfigureAwait(false);
            await tx.AuditAsync("MNT_WO", Target("wo", id), $"{w.No} · from repair {Target("hk-task", task.TaskId)} · room {task.RoomNo}", c).ConfigureAwait(false);
            await tx.PublishAsync("MaintenanceWorkOrderCreated", id, new { workOrderId = id, no = w.No, priority, roomId = task.RoomId }, c).ConfigureAwait(false);
            return Result(w, version, "converted from repair");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WorkOrderResult>> AssignAsync(CommandContext ctx, AssignWorkOrderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Assign, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpAssign }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            if (req.VendorId is { } v && !await store.VendorExistsAsync(tx, ctx.PropertyId, v, c).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Vendor not found.");
            if (req.TechnicianId is { } t && !await store.IsTechnicianAsync(tx, ctx.PropertyId, t, c).ConfigureAwait(false)) throw HccException.Validation("Select an enabled maintenance user.");
            var n = MntMachine.Assign(w, req.TechnicianId, req.VendorId);
            if (n.AssigneeId is { } tech && n.RepairTaskId is { } rt)
            {
                var task = await store.LockRepairTaskAsync(tx, ctx.PropertyId, rt, c).ConfigureAwait(false);
                if (task is not null && task.Status != "Done") await store.SetRepairAssigneeAsync(tx, rt, tech, c).ConfigureAwait(false);
            }

            var to = n.VendorId is { } ? "vendor " + n.VendorId.Value.ToString(CultureInfo.InvariantCulture) : "user " + n.AssigneeId!.Value.ToString(CultureInfo.InvariantCulture);
            var ver = await SaveAsync(tx, n, expected, "Assigned", to, "assigned to " + to, c).ConfigureAwait(false);
            return Result(n, ver, "assigned");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WorkOrderResult>> StartAsync(CommandContext ctx, StartWorkOrderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await RequireWorkerAsync(ctx, req.WorkOrderId, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpStart }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            var (n, resumed) = MntMachine.Start(w, UtcNow);
            if (n.RepairTaskId is { } rt)
            {
                var task = await store.LockRepairTaskAsync(tx, ctx.PropertyId, rt, c).ConfigureAwait(false);
                if (task is { Status: "ToDo" }) await store.SetRepairStartedAsync(tx, rt, UtcNow, c).ConfigureAwait(false);
            }

            var ver = await SaveAsync(tx, n, expected, resumed ? "Resumed" : "Started", null, resumed ? "resumed" : "started", c).ConfigureAwait(false);
            return Result(n, ver, resumed ? "resumed" : "started");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WorkOrderResult>> HoldAsync(CommandContext ctx, HoldWorkOrderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await RequireWorkerAsync(ctx, req.WorkOrderId, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpHold }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            if (req.SpareId is { } s && await store.LockSpareAsync(tx, ctx.PropertyId, s, c).ConfigureAwait(false) is null) throw new HccException(HccErrorCode.NOT_FOUND, "Spare not found.");
            var n = MntMachine.Hold(w, req.Reason, req.SpareId);
            var ver = await SaveAsync(tx, n, expected, "On hold", n.HoldReason, "on hold · " + n.HoldReason, c).ConfigureAwait(false);
            return Result(n, ver, "on hold");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WorkOrderResult>> AddPartAsync(CommandContext ctx, AddWorkOrderPartRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        MntMachine.PartQty(req.Qty);
        await RequireWorkerAsync(ctx, req.WorkOrderId, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpPart }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            var spare = await store.LockSpareAsync(tx, ctx.PropertyId, req.SpareId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Spare not found.");
            var n = MntMachine.AddPart(w, spare, req.Qty);
            await store.SetSpareStockAsync(tx, spare.Id, spare.Stock - req.Qty, c).ConfigureAwait(false);
            // No unit price in the audit text or the result: this command is available to Maintenance (DEC-063 G12).
            var ver = await SaveAsync(tx, n, expected, "Part used", $"{req.Qty.ToString(CultureInfo.InvariantCulture)} × {spare.Name}", $"{req.Qty.ToString(CultureInfo.InvariantCulture)} × {spare.Name}", c).ConfigureAwait(false);
            return Result(n, ver, "part booked");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WorkOrderResult>> CompleteAsync(CommandContext ctx, CompleteWorkOrderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await RequireWorkerAsync(ctx, req.WorkOrderId, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpComplete }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            var n = MntMachine.Complete(w, req.Note, ctx.PrincipalId, UtcNow);
            if (n.RepairTaskId is { } rt)
            {
                var task = await store.LockRepairTaskAsync(tx, ctx.PropertyId, rt, c).ConfigureAwait(false);
                if (task is not null && task.Status != "Done") await store.SetRepairDoneAsync(tx, rt, UtcNow, c).ConfigureAwait(false);
            }

            var blocked = n.RoomId is { } r && await housekeeping.LockActiveBlockAsync(tx, r, c).ConfigureAwait(false) is not null;
            var ver = await SaveAsync(tx, n, expected, "Completed", n.Note, "done" + (blocked ? " · room stays blocked until verified" : string.Empty), c).ConfigureAwait(false);
            await tx.PublishAsync("MaintenanceWorkOrderCompleted", n.Id, new { workOrderId = n.Id, no = n.No, roomId = n.RoomId }, c).ConfigureAwait(false);
            return Result(n, ver, "done");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WorkOrderResult>> VerifyAsync(CommandContext ctx, VerifyWorkOrderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        var unblock = req.Approve && req.Unblock;
        if (unblock) await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Block, null, null, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpVerify }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            var n = MntMachine.Verify(w, req.Approve, req.Reason, ctx.PrincipalId, UtcNow);
            if (!req.Approve)
            {
                var back = await SaveAsync(tx, n, expected, "Returned", n.ReturnReason, "returned · " + n.ReturnReason, c).ConfigureAwait(false);
                return Result(n, back, "returned");
            }

            if (n.AssetId is { } assetId)
            {
                var asset = await store.LockAssetAsync(tx, ctx.PropertyId, assetId, c).ConfigureAwait(false);
                if (asset is { Status: "Faulty" } && !await store.OtherActiveOrderOnAssetAsync(tx, assetId, n.Id, c).ConfigureAwait(false))
                    await store.SetAssetStatusAsync(tx, assetId, "Working", c).ConfigureAwait(false);
            }

            if (n.PlanId is { } planId && w.DoneUtc is { } done)
            {
                var plan = await store.LockPlanAsync(tx, ctx.PropertyId, planId, c).ConfigureAwait(false);
                var d = DateOnly.FromDateTime(done);
                if (plan is not null && (plan.LastDone is null || plan.LastDone < d)) await store.SetPlanLastDoneAsync(tx, planId, d, c).ConfigureAwait(false);
            }

            var extra = string.Empty;
            if (unblock)
            {
                var room = n.RoomId is { } rid ? await housekeeping.LockRoomAsync(tx, ctx.PropertyId, rid, c).ConfigureAwait(false) : null;
                var block = room is null ? null : await housekeeping.LockActiveBlockAsync(tx, room.RoomId, c).ConfigureAwait(false);
                if (room is null || block is null) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This room is not blocked.");
                await housekeeping.ReleaseBlockAsync(tx, block.BlockId, block.Version, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await roomBlocks.RemoveBySourceAsync(tx, ctx.PropertyId, InventoryBlockSource, block.BlockId, c).ConfigureAwait(false);
                if (n.DowntimeFromUtc is not null && n.DowntimeToUtc is null) n = n with { DowntimeToUtc = UtcNow };
                extra = $" · room {room.RoomNo} unblocked";
            }

            var ver = await SaveAsync(tx, n, expected, "Verified", extra.Length > 0 ? extra[3..] : null, "verified" + extra, c).ConfigureAwait(false);
            await tx.PublishAsync("MaintenanceWorkOrderVerified", n.Id, new { workOrderId = n.Id, no = n.No, roomId = n.RoomId, unblocked = unblock }, c).ConfigureAwait(false);
            return Result(n, ver, "verified" + extra);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<WorkOrderResult>> CancelAsync(CommandContext ctx, CancelWorkOrderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpCancel }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            var n = MntMachine.Cancel(w, req.Reason);
            var ver = await SaveAsync(tx, n, expected, "Cancelled", n.CancelReason, "cancelled · " + n.CancelReason, c).ConfigureAwait(false);
            return Result(n, ver, "cancelled");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Out-of-service downtime for the work order's room through the room-block port: Housekeeping's block record (source
    /// "maintenance") and the inventory block (<see cref="IRoomBlockPort.PlaceAsync"/>), one transaction. Refused NOT_AVAILABLE while
    /// bookings overlap; an already blocked room is linked instead (prototype).
    /// </summary>
    public async Task<CommandResult<BlockWorkOrderRoomResult>> BlockRoomAsync(CommandContext ctx, BlockWorkOrderRoomRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Block, null, null, ct).ConfigureAwait(false);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        DateOnly? to = string.IsNullOrEmpty(req.To) ? null : Date(req.To, "Enter a valid end date.");

        return await runner.RunAsync(ctx with { Operation = OpBlockRoom }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var w = Require(await store.LockOrderAsync(tx, ctx.PropertyId, req.WorkOrderId, c).ConfigureAwait(false), expected);
            MntMachine.EnsureCanBlock(w);
            var room = await housekeeping.LockRoomAsync(tx, ctx.PropertyId, w.RoomId!.Value, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.");
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var active = await housekeeping.LockActiveBlockAsync(tx, room.RoomId, c).ConfigureAwait(false);
            long blockId;
            bool linked;
            string detail;
            if (active is not null)
            {
                (blockId, linked) = (active.BlockId, true);
                detail = $"room {room.RoomNo} already blocked — linked";
            }
            else
            {
                if (to is { } t && t <= bd) throw HccException.Validation("The block must end after today.");
                if (await housekeeping.AllocationsOverlappingAsync(tx, room.RoomId, bd, to, UtcNow, c).ConfigureAwait(false) > 0)
                    throw new HccException(HccErrorCode.NOT_AVAILABLE, "Resolve active and future allocations before blocking this room.");
                var reason = MntMachine.BlockReason(w);
                (blockId, _) = await housekeeping.InsertBlockAsync(tx, ctx.PropertyId, room.RoomId, bd, to, reason, "maintenance", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await roomBlocks.PlaceAsync(tx, ctx.PropertyId, room.RoomId, bd, to, reason, InventoryBlockSource, blockId, c).ConfigureAwait(false);
                linked = false;
                detail = $"room {room.RoomNo} blocked {StayDates.ToWire(bd)} → {(to is { } x ? StayDates.ToWire(x) : "open-ended")}";
            }

            var n = w with { DowntimeFromUtc = w.DowntimeFromUtc ?? UtcNow, DowntimeToUtc = null, BlockId = blockId };
            var ver = await SaveAsync(tx, n, expected, linked ? "Linked existing room block" : "Room blocked", detail, detail, c).ConfigureAwait(false);
            await tx.PublishAsync("MaintenanceRoomBlocked", n.Id, new { workOrderId = n.Id, no = n.No, roomId = room.RoomId, blockId, linked }, c).ConfigureAwait(false);
            return new BlockWorkOrderRoomResult(n.Id, n.No, room.RoomId, room.RoomNo, blockId, linked, VersionToken.ToWire(ver));
        }, ct).ConfigureAwait(false);
    }

    // ── Preventive maintenance ──
    public async Task<CommandResult<GeneratePmResult>> GeneratePmAsync(CommandContext ctx, GeneratePmWorkOrdersRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpGeneratePm }, req, async (tx, c) =>
        {
            // P6-GUARD: refuse when the business date is closed (DAY_CLOSED) once IBusinessDayGuard exists.
            var bd = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var plans = await store.LockActivePlansAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var exists = new HashSet<(int, DateOnly)>();
            foreach (var p in plans)
                for (var (d, n) = (p.NextDue, 0); d <= bd.AddDays(MntRules.PmHorizonDays) && n < 12; (d, n) = (d.AddDays(p.EveryDays), n + 1))
                    if (await store.OccurrenceExistsAsync(tx, p.Id, d, c).ConfigureAwait(false)) exists.Add((p.Id, d));
            var due = MntRules.PmDue(plans, (plan, date) => exists.Contains((plan, date)), bd);
            var tech = await store.FirstTechnicianAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var ids = new List<long>();
            foreach (var (plan, date) in due.Select(d => (d.Plan, d.Due)))
            {
                var asset = await store.LockAssetAsync(tx, ctx.PropertyId, plan.AssetId, c).ConfigureAwait(false);
                var vendor = asset is { VendorId: not null, Kind: "Lift" or "Generator" } ? asset.VendorId : null;
                var draft = New(MntKind.Preventive, plan.Title, MntPriority.P3, asset?.RoomId, asset?.RoomNo, plan.AssetId, asset?.RoomId is null ? asset?.Area : null,
                    vendor is null ? tech : null, vendor, ctx.PrincipalId, UtcNow, plan.Id, date);
                var (id, _) = await store.InsertOrderAsync(tx, ctx.PropertyId, draft, c).ConfigureAwait(false);
                await store.AddHistoryAsync(tx, id, UtcNow, ctx.PrincipalId, "Generated from PM plan", $"PM-{plan.Id.ToString(CultureInfo.InvariantCulture)} · due {StayDates.ToWire(date)}", c).ConfigureAwait(false);
                ids.Add(id);
            }

            foreach (var p in plans)
            {
                var next = MntRules.AdvanceNextDue(p, bd);
                if (next != p.NextDue) await store.UpdatePlanAsync(tx, p with { NextDue = next }, p.Version, c).ConfigureAwait(false);
            }

            var horizon = StayDates.ToWire(bd.AddDays(MntRules.PmHorizonDays));
            await tx.AuditAsync("MNT_PM_GENERATE", Target("property", ctx.PropertyId), $"{ids.Count.ToString(CultureInfo.InvariantCulture)} preventive work order(s) generated up to {horizon}", c).ConfigureAwait(false);
            return new GeneratePmResult(ids.Count, ids, horizon);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RegisterSaveResult>> SavePlanAsync(CommandContext ctx, SavePmPlanRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        var title = Text(req.Title, 3, 200, "Enter the PM task (3–200 characters).");
        if (req.EveryDays is < 1 or > 365) throw HccException.Validation("Repeat every 1–365 days.");
        var next = Date(req.NextDue, "Enter the next due date.");
        var expected = req.PlanId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSavePlan }, req, async (tx, c) =>
        {
            if (await store.LockAssetAsync(tx, ctx.PropertyId, req.AssetId, c).ConfigureAwait(false) is null) throw HccException.Validation("Choose an asset.");
            if (req.PlanId is { } pid)
            {
                var p = await store.LockPlanAsync(tx, ctx.PropertyId, pid, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Plan not found.");
                var v = await store.UpdatePlanAsync(tx, p with { AssetId = req.AssetId, Title = title, EveryDays = req.EveryDays, NextDue = next, Active = req.Active }, expected!, c).ConfigureAwait(false);
                await tx.AuditAsync("MNT_PLAN", Target("pm", pid), $"{title} · plan updated", c).ConfigureAwait(false);
                return new RegisterSaveResult(pid, VersionToken.ToWire(v), "plan updated");
            }

            var (id, ver) = await store.InsertPlanAsync(tx, ctx.PropertyId, new MntPlan(0, req.AssetId, title, req.EveryDays, next, req.Active, null, []), c).ConfigureAwait(false);
            await tx.AuditAsync("MNT_PLAN", Target("pm", id), $"{title} every {req.EveryDays.ToString(CultureInfo.InvariantCulture)} d", c).ConfigureAwait(false);
            return new RegisterSaveResult(id, VersionToken.ToWire(ver), "plan added");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RegisterSaveResult>> SaveAssetAsync(CommandContext ctx, SaveAssetRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        var name = Text(req.Name, 2, 120, "Enter the asset name (2–120 characters).");
        if (!MntAssetKinds.All.Contains(req.Kind)) throw HccException.Validation("Choose an asset type.");
        if (!MntAssetKinds.Statuses.Contains(req.Status)) throw HccException.Validation("Invalid asset status.");
        var area = req.RoomId is null ? Text(req.Area, 1, 60, "Choose a room or enter an area.") : null;
        var make = (req.Make ?? string.Empty).Trim();
        var model = (req.Model ?? string.Empty).Trim();
        if (make.Length > 60 || model.Length > 60) throw HccException.Validation("Make and model are at most 60 characters.");
        DateOnly? installed = string.IsNullOrWhiteSpace(req.InstalledOn) ? null : Date(req.InstalledOn, "Enter a valid install date.");
        var expected = req.AssetId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveAsset }, req, async (tx, c) =>
        {
            string? roomNo = null;
            if (req.RoomId is { } r) roomNo = (await housekeeping.LockRoomAsync(tx, ctx.PropertyId, r, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Room not found.")).RoomNo;
            if (req.VendorId is { } v && !await store.VendorExistsAsync(tx, ctx.PropertyId, v, c).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "Vendor not found.");
            var draft = new MntAsset(req.AssetId ?? 0, name, req.Kind, req.RoomId, roomNo, area, make, model, installed, req.VendorId, req.Status, []);
            if (req.AssetId is { } id)
            {
                _ = await store.LockAssetAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Asset not found.");
                var ver = await store.UpdateAssetAsync(tx, draft, expected!, c).ConfigureAwait(false);
                await tx.AuditAsync("MNT_ASSET", Target("asset", id), $"{name} updated", c).ConfigureAwait(false);
                return new RegisterSaveResult(id, VersionToken.ToWire(ver), "asset updated");
            }

            var (nid, nver) = await store.InsertAssetAsync(tx, ctx.PropertyId, draft, c).ConfigureAwait(false);
            await tx.AuditAsync("MNT_ASSET", Target("asset", nid), $"{name} added", c).ConfigureAwait(false);
            return new RegisterSaveResult(nid, VersionToken.ToWire(nver), "asset added");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<RegisterSaveResult>> SaveVendorAsync(CommandContext ctx, SaveVendorRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        var name = Text(req.Name, 3, 120, "Enter the vendor name (3–120 characters).");
        var phone = (req.Phone ?? string.Empty).Trim();
        if (phone.Length > 30 || !PhonePattern().IsMatch(phone)) throw HccException.Validation("Enter a phone number.");
        var trade = string.IsNullOrWhiteSpace(req.Trade) ? "General" : Text(req.Trade, 1, 60, "The trade is at most 60 characters.");
        var expected = req.VendorId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveVendor }, req, async (tx, c) =>
        {
            var draft = new MntVendor(req.VendorId ?? 0, name, trade, phone, []);
            if (req.VendorId is { } id)
            {
                _ = await store.LockVendorAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Vendor not found.");
                var ver = await store.UpdateVendorAsync(tx, draft, expected!, c).ConfigureAwait(false);
                await tx.AuditAsync("MNT_VENDOR", Target("vendor", id), $"{name} updated", c).ConfigureAwait(false);
                return new RegisterSaveResult(id, VersionToken.ToWire(ver), "vendor updated");
            }

            var (nid, nver) = await store.InsertVendorAsync(tx, ctx.PropertyId, draft, c).ConfigureAwait(false);
            await tx.AuditAsync("MNT_VENDOR", Target("vendor", nid), $"{name} added", c).ConfigureAwait(false);
            return new RegisterSaveResult(nid, VersionToken.ToWire(nver), "vendor added");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>AMC contracts carry money: Owner and Manager only (supervisor rights plus finance rights; DEC-063 G12).</summary>
    public async Task<CommandResult<RegisterSaveResult>> SaveAmcAsync(CommandContext ctx, SaveAmcRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MaintenancePermissions.Money, null, null, ct).ConfigureAwait(false);
        var scope = Text(req.Scope, 3, 200, "Describe what the AMC covers (3–200 characters).");
        var from = Date(req.From, "Enter a valid start date.");
        var to = Date(req.To, "Enter a valid end date.");
        if (to <= from) throw HccException.Validation("The AMC must end after it starts.");
        if (req.Visits is < 0 or > 52) throw HccException.Validation("Visits per year: 0–52.");
        if (req.ValuePaise <= 0) throw HccException.Validation("Enter the contract value in paise (greater than zero).");
        if (req.ValuePaise > 100_000_000_000) throw HccException.Validation("The contract value is out of range.");
        var expected = req.AmcId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveAmc }, req, async (tx, c) =>
        {
            if (!await store.VendorExistsAsync(tx, ctx.PropertyId, req.VendorId, c).ConfigureAwait(false)) throw HccException.Validation("Choose a vendor.");
            var draft = new MntAmc(req.AmcId ?? 0, req.VendorId, scope, from, to, req.Visits, req.ValuePaise, []);
            // The audit text carries no contract value (the audit log is readable by roles without finance rights).
            if (req.AmcId is { } id)
            {
                _ = await store.LockAmcAsync(tx, ctx.PropertyId, id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "AMC not found.");
                var ver = await store.UpdateAmcAsync(tx, draft, expected!, c).ConfigureAwait(false);
                await tx.AuditAsync("MNT_AMC", Target("amc", id), "AMC updated", c).ConfigureAwait(false);
                return new RegisterSaveResult(id, VersionToken.ToWire(ver), "AMC updated");
            }

            var (nid, nver) = await store.InsertAmcAsync(tx, ctx.PropertyId, draft, c).ConfigureAwait(false);
            await tx.AuditAsync("MNT_AMC", Target("amc", nid), $"AMC {StayDates.ToWire(from)} → {StayDates.ToWire(to)}", c).ConfigureAwait(false);
            return new RegisterSaveResult(nid, VersionToken.ToWire(nver), "AMC added");
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ReceiveSpareResult>> ReceiveSpareAsync(CommandContext ctx, ReceiveSpareRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        await RequireManagerAsync(ctx, ct).ConfigureAwait(false);
        if (req.Qty is < 1 or > 999) throw HccException.Validation("Enter a quantity between 1 and 999.");
        return await runner.RunAsync(ctx with { Operation = OpReceiveSpare }, req, async (tx, c) =>
        {
            var spare = await store.LockSpareAsync(tx, ctx.PropertyId, req.SpareId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Spare not found.");
            VersionToken.EnsureMatches(spare.Version, expected, "This spare");
            var stock = spare.Stock + req.Qty;
            var ver = await store.SetSpareStockAsync(tx, spare.Id, stock, c).ConfigureAwait(false);
            await tx.AuditAsync("MNT_SPARE", Target("spare", spare.Id), $"{spare.Name} · +{req.Qty.ToString(CultureInfo.InvariantCulture)} {spare.Unit} (stock {stock.ToString(CultureInfo.InvariantCulture)})", c).ConfigureAwait(false);
            return new ReceiveSpareResult(spare.Id, stock, VersionToken.ToWire(ver));
        }, ct).ConfigureAwait(false);
    }
}
