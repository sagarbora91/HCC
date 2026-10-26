using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Application.Masters;

/// <summary>
/// Masters commands (prototype MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE for master "floors"): SaveFloor,
/// RetireFloor, ReactivateFloor, ReorderFloors, DeleteFloor. Each checks PERM-MST-001 (DEC-MA-1: Owner, Tech admin), runs through
/// <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox "MasterChanged" in one transaction), checks expectedVersion
/// (STALE_VERSION) on edits and writes one mst.master_history row per changed field. Masters are configuration: no business-day lock.
/// </summary>
public sealed class MastersCommands(ICommandRunner runner, IAccessPolicy policy, IMastersStore store, IEnumerable<IMasterUsage> usages, TimeProvider clock)
{
    public const string OpSaveFloor = "SaveFloor";
    public const string OpRetireFloor = "RetireFloor";
    public const string OpReactivateFloor = "ReactivateFloor";
    public const string OpReorderFloors = "ReorderFloors";
    public const string OpDeleteFloor = "DeleteFloor";
    public const string FloorsLabel = "Floors";

    private readonly IReadOnlyList<IMasterUsage> _usages = usages.ToList();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    /// <summary>Sum of every registered provider of the master (0 when none is registered).</summary>
    internal async Task<MasterUsageCount> UsageAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct)
    {
        var count = 0;
        var where = new List<string>();
        foreach (var u in _usages.Where(u => u.Masters.Contains(master)))
        {
            var r = await u.CountAsync(tx, propertyId, master, rowId, ct).ConfigureAwait(false);
            count += r.Count;
            where.AddRange(r.Where);
        }

        return new MasterUsageCount(count, where);
    }

    private Task RequireAsync(CommandContext ctx, CancellationToken ct) => policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MastersPermissions.Manage, null, null, ct);

    public async Task<CommandResult<FloorView>> SaveFloorAsync(CommandContext ctx, SaveFloorRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        byte[]? expected = req.FloorId is null ? null : VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpSaveFloor }, req, async (tx, c) =>
        {
            var floors = await store.LockFloorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            FloorRec? old = null;
            if (req.FloorId is { } id)
            {
                old = floors.FirstOrDefault(f => f.FloorId == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This floor was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This floor");
                MastersRules.EnsureActive(old.Active, old.Name);
                var used = await UsageAsync(tx, ctx.PropertyId, MasterIds.Floors, id, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, "rooms");
            }

            if (floors.Any(f => f.FloorId != old?.FloorId && string.Equals(f.Code, clean.Code, StringComparison.Ordinal)))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in Floors (retired floors keep their code).", "code");
            if (floors.Any(f => f.FloorId != old?.FloorId && f.Active && string.Equals(f.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active floor is already called “{clean.Name}”.", "name");
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(floors.Select(f => f.SortOrder));
            var changes = MastersRules.Diff(old is null ? null : new MastersRules.CleanRow(old.Code, old.Name, old.SortOrder), clean, sort);
            int floorId;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                floorId = old.FloorId;
                await store.UpdateFloorAsync(tx, floorId, expected!, clean.Code, clean.Name, sort, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = MastersRules.AuditText(FloorsLabel, changes);
            }
            else
            {
                floorId = await store.InsertFloorAsync(tx, ctx.PropertyId, clean.Code, clean.Name, sort, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = $"Master {FloorsLabel}: added {clean.Code} · {clean.Name}";
            }

            foreach (var (field, o, n) in changes)
                await store.AddHistoryAsync(tx, ctx.PropertyId, MasterIds.Floors, floorId, clean.Code, field, o, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            return await FinishAsync(tx, ctx, floorId, old is null ? "created" : "changed", detail, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public Task<CommandResult<FloorView>> RetireFloorAsync(CommandContext ctx, RetireFloorRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpRetireFloor, req, req.FloorId, req.ExpectedVersion, false, ct);
    }

    public Task<CommandResult<FloorView>> ReactivateFloorAsync(CommandContext ctx, ReactivateFloorRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(ctx, OpReactivateFloor, req, req.FloorId, req.ExpectedVersion, true, ct);
    }

    private async Task<CommandResult<FloorView>> SetActiveAsync<TReq>(CommandContext ctx, string op, TReq req, int floorId, string? version, bool active, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        return await runner.RunAsync(ctx with { Operation = op }, req, async (tx, c) =>
        {
            var floors = await store.LockFloorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var f = floors.FirstOrDefault(x => x.FloorId == floorId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This floor was not found.");
            VersionToken.EnsureMatches(f.RowVer, expected, "This floor");
            if (f.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{f.Name} is already active." : $"{f.Name} is already retired.");
            if (active && floors.Any(x => x.FloorId != f.FloorId && x.Active && string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active floor is already called “{f.Name}” — rename one first.", "name");
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await store.SetFloorActiveAsync(tx, f.FloorId, expected, active, active ? null : date, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await store.AddHistoryAsync(tx, ctx.PropertyId, MasterIds.Floors, f.FloorId, f.Code, "active", f.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var used = await UsageAsync(tx, ctx.PropertyId, MasterIds.Floors, f.FloorId, c).ConfigureAwait(false);
            var detail = $"Master {FloorsLabel}: active {(f.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({f.Code} · {f.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used.Count > 0 ? $"; still used: {used.Count}, old records keep the name)" : ")");
            return await FinishAsync(tx, ctx, f.FloorId, active ? "reactivated" : "retired", detail, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<DeleteFloorResult>> DeleteFloorAsync(CommandContext ctx, DeleteFloorRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = OpDeleteFloor }, req, async (tx, c) =>
        {
            var f = await store.LockFloorAsync(tx, ctx.PropertyId, req.FloorId, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This floor was not found.");
            VersionToken.EnsureMatches(f.RowVer, expected, "This floor");
            var used = await UsageAsync(tx, ctx.PropertyId, MasterIds.Floors, f.FloorId, c).ConfigureAwait(false);
            MastersRules.EnsureDeletable(f.Name, used.Count, "rooms");
            await store.DeleteFloorAsync(tx, f.FloorId, expected, c).ConfigureAwait(false);
            await store.AddHistoryAsync(tx, ctx.PropertyId, MasterIds.Floors, f.FloorId, f.Code, "deleted", f.Name, null, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("MST_FLOOR", Target(f.FloorId), $"Master {FloorsLabel}: {f.Code} · {f.Name} deleted (never used)", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", f.FloorId, new { master = MasterIds.Floors, rowId = f.FloorId, action = "deleted" }, c).ConfigureAwait(false);
            return new DeleteFloorResult(f.FloorId, f.Code, true);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FloorList>> ReorderFloorsAsync(CommandContext ctx, ReorderFloorsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        return await runner.RunAsync(ctx with { Operation = OpReorderFloors }, req, async (tx, c) =>
        {
            var floors = await store.LockFloorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var order = MastersRules.Reorder(floors.Select(f => f.FloorId).ToList(), req.FloorIds, "floor");
            var moved = new List<string>();
            foreach (var f in floors)
            {
                var n = order[f.FloorId];
                if (n == f.SortOrder) continue;
                await store.SetFloorSortAsync(tx, f.FloorId, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await store.AddHistoryAsync(tx, ctx.PropertyId, MasterIds.Floors, f.FloorId, f.Code, "sortOrder", f.SortOrder.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                moved.Add(f.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            var codes = floors.OrderBy(f => order[f.FloorId]).Select(f => f.Code);
            await tx.AuditAsync("MST_FLOOR", "mst-floor:order", $"Master {FloorsLabel}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", codes)})", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", 0, new { master = MasterIds.Floors, action = "reordered" }, c).ConfigureAwait(false);
            var after = await store.LockFloorsAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var views = new List<FloorView>();
            foreach (var f in after) views.Add(View(f, (await UsageAsync(tx, ctx.PropertyId, MasterIds.Floors, f.FloorId, c).ConfigureAwait(false)).Count));
            return new FloorList(1, views.Count, views.Count, views);
        }, ct).ConfigureAwait(false);
    }

    private async Task<FloorView> FinishAsync(ICommandTransaction tx, CommandContext ctx, int floorId, string action, string detail, CancellationToken ct)
    {
        await tx.AuditAsync("MST_FLOOR", Target(floorId), detail, ct).ConfigureAwait(false);
        await tx.PublishAsync("MasterChanged", floorId, new { master = MasterIds.Floors, rowId = floorId, action }, ct).ConfigureAwait(false);
        var f = (await store.LockFloorAsync(tx, ctx.PropertyId, floorId, ct).ConfigureAwait(false))!;
        return View(f, (await UsageAsync(tx, ctx.PropertyId, MasterIds.Floors, floorId, ct).ConfigureAwait(false)).Count);
    }

    internal static FloorView View(FloorRec f, int rooms) => new(f.FloorId, f.Code, f.Name, f.SortOrder, f.Active,
        f.RetiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), rooms, f.UpdatedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), VersionToken.ToWire(f.RowVer));

    private static string Target(int floorId) => "mst-floor:" + floorId.ToString(CultureInfo.InvariantCulture);
}
