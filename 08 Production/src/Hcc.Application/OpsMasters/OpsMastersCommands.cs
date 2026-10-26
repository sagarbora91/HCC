using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.OpsMasters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.OpsMasters;

namespace Hcc.Application.OpsMasters;

/// <summary>
/// Commands of the four operations masters (MA-7): Save/Retire/Reactivate/Delete + HkTaskType | HkZone | ComplaintCategory | LostFoundCategory
/// and Reorder + HkTaskTypes | HkZones | ComplaintCategories | LostFoundCategories. Same contract as the floors commands (Masters module):
/// PERM-MST-001 (DEC-MA-1: Owner, Tech admin), ICommandRunner (Idempotency-Key, audit, outbox "MasterChanged" in one transaction),
/// expectedVersion on edits (STALE_VERSION), one mst.master_history row per changed field, retire-not-delete once in use, no business-day
/// lock. The prototype's MST_MASTER_* commands for hkTaskTypes / hkZones / complaintCategories / lostFoundCategories map onto these.
/// </summary>
public sealed class OpsMastersCommands(ICommandRunner runner, IAccessPolicy policy, IOpsMastersStore store, IMastersStore mst, ITaskAssigneeEligibility eligibility,
    IEnumerable<IMasterUsage> usages, TimeProvider clock)
{
    private readonly IReadOnlyList<IMasterUsage> _usages = usages.ToList();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private async Task<MasterUsageCount> UsageAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct)
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

    // ───────────── Save (one method per master; rules differ, the flow is shared) ─────────────
    public async Task<CommandResult<HkTaskTypeView>> SaveHkTaskTypeAsync(CommandContext ctx, SaveHkTaskTypeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var def = OpsMasterDefs.HkTaskTypes;
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var t = OpsMastersRules.ValidateTaskType(req.AppliesTo, req.StandardMinutes, req.NeedsInspection, req.Checklist);
        var fields = new Dictionary<string, string?>
        {
            ["appliesTo"] = t.AppliesTo, ["standardMinutes"] = OpsMastersRules.Int(t.Minutes), ["needsInspection"] = t.NeedsInspection ? "1" : "0",
            ["checklist"] = OpsMastersRules.ChecklistJson(t.Checklist),
        };
        return await SaveAsync(def, ctx, req, req.Id, req.ExpectedVersion, clean, fields, (_, rows, old) =>
        {
            OpsMastersRules.EnsureKindsFree(t.AppliesTo, old?.Active ?? true, rows.Select(r => (r.Id, r.Name, r.Active, r.Fields["appliesTo"] ?? string.Empty)), old?.Id);
            return Task.CompletedTask;
        }, (row, used) => OpsMastersViews.HkTaskType(row, used), ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<HkZoneView>> SaveHkZoneAsync(CommandContext ctx, SaveHkZoneRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        var def = OpsMasterDefs.HkZones;
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var z = OpsMastersRules.ValidateZone(req.Floors, req.Rooms);
        if (req.DefaultAttendantId is { } a && !await eligibility.CanBeAssignedAsync(ctx.PropertyId, a, CleanKind, ct).ConfigureAwait(false))
            throw MastersRules.Fail(MasterReasons.Invalid, "Default attendant: choose an enabled housekeeper.", "defaultAttendant");
        var fields = new Dictionary<string, string?>
        {
            ["floors"] = z.Floors, ["rooms"] = z.Rooms, ["defaultAttendant"] = req.DefaultAttendantId?.ToString(CultureInfo.InvariantCulture),
        };
        return await SaveAsync(def, ctx, req, req.Id, req.ExpectedVersion, clean, fields, async (tx, rows, old) =>
        {
            OpsMastersRules.EnsureZoneFree(z, old?.Active ?? true, rows.Select(r => (r.Id, r.Name, r.Active, r.Fields["floors"] ?? string.Empty, r.Fields["rooms"] ?? string.Empty)), old?.Id);
            var floors = await store.FloorCodesAsync(tx, ctx.PropertyId, ct).ConfigureAwait(false);
            if (OpsMastersRules.SplitCsv(z.Floors).FirstOrDefault(f => !floors.Contains(f)) is { } badFloor)
                throw MastersRules.Fail(MasterReasons.Invalid, $"Floors: {badFloor} is not in Floors.", "floors");
            var roomNos = await store.RoomNosAsync(tx, ctx.PropertyId, ct).ConfigureAwait(false);
            if (OpsMastersRules.SplitCsv(z.Rooms).FirstOrDefault(r => !roomNos.Contains(r)) is { } badRoom)
                throw MastersRules.Fail(MasterReasons.Invalid, $"Extra rooms: room {badRoom} does not exist.", "rooms");
        }, (row, used) => OpsMastersViews.HkZone(row, used), ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<ComplaintCategoryView>> SaveComplaintCategoryAsync(CommandContext ctx, SaveComplaintCategoryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var fields = new Dictionary<string, string?>
        {
            ["department"] = OpsMastersRules.ValidateDepartment(req.Department), ["slaHours"] = OpsMastersRules.Int(OpsMastersRules.ValidateSla(req.SlaHours)),
        };
        return await SaveAsync(OpsMasterDefs.ComplaintCategories, ctx, req, req.Id, req.ExpectedVersion, clean, fields, (_, _, _) => Task.CompletedTask,
            (row, used) => OpsMastersViews.ComplaintCategory(row, used), ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LostFoundCategoryView>> SaveLostFoundCategoryAsync(CommandContext ctx, SaveLostFoundCategoryRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(req.Code, req.Name, req.SortOrder);
        var fields = new Dictionary<string, string?>
        {
            ["department"] = OpsMastersRules.ValidateDepartment(req.Department), ["slaHours"] = OpsMastersRules.Int(OpsMastersRules.ValidateSla(req.SlaHours)),
            ["valuable"] = req.Valuable ? "1" : "0",
        };
        return await SaveAsync(OpsMasterDefs.LostFoundCategories, ctx, req, req.Id, req.ExpectedVersion, clean, fields, (_, _, _) => Task.CompletedTask,
            (row, used) => OpsMastersViews.LostFoundCategory(row, used), ct).ConfigureAwait(false);
    }

    private const string CleanKind = Hcc.Domain.Housekeeping.HkTaskKind.Checkout;

    private async Task<CommandResult<TView>> SaveAsync<TReq, TView>(OpsMasterDef def, CommandContext ctx, TReq req, long? id, string? version, MastersRules.CleanRow clean,
        Dictionary<string, string?> fields, Func<ICommandTransaction, IReadOnlyList<OpsRow>, OpsRow?, Task> check, Func<OpsRow, int, TView> view, CancellationToken ct)
        where TView : class
    {
        byte[]? expected = id is null ? null : VersionToken.Parse(version);
        return await runner.RunAsync(ctx with { Operation = def.Save }, req, async (tx, c) =>
        {
            var rows = await store.LockAllAsync(tx, def, ctx.PropertyId, c).ConfigureAwait(false);
            OpsRow? old = null;
            if (id is { } rid)
            {
                old = rows.FirstOrDefault(r => r.Id == rid) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {def.Singular} was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, $"This {def.Singular}");
                MastersRules.EnsureActive(old.Active, old.Name);
                var used = await UsageAsync(tx, ctx.PropertyId, def.Master, rid, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, def.UsedWhat);
            }

            if (rows.Any(r => r.Id != old?.Id && string.Equals(r.Code, clean.Code, StringComparison.Ordinal)))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in {def.Label} (retired rows keep their code).", "code");
            OpsMastersRules.EnsureNameFree(clean.Name, true, rows.Select(r => (r.Id, r.Name, r.Active)), old?.Id, def.Singular);
            await check(tx, rows, old).ConfigureAwait(false);
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(r => r.SortOrder));
            var changes = Diff(def, old, clean, sort, fields);
            long rowId;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                rowId = old.Id;
                await store.UpdateAsync(tx, def, rowId, expected!, clean.Code, clean.Name, sort, fields, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = MastersRules.AuditText(def.Label, changes);
            }
            else
            {
                rowId = await store.InsertAsync(tx, def, ctx.PropertyId, clean.Code, clean.Name, sort, fields, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = $"Master {def.Label}: added {clean.Code} · {clean.Name}";
            }

            foreach (var (field, o, n) in changes)
                await mst.AddHistoryAsync(tx, ctx.PropertyId, def.Master, rowId, clean.Code, field, o, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync(def.AuditKind, Target(def, rowId), detail, c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", rowId, new { master = def.Master, rowId, action = old is null ? "created" : "changed" }, c).ConfigureAwait(false);
            var after = (await store.LockOneAsync(tx, def, ctx.PropertyId, rowId, c).ConfigureAwait(false))!;
            return view(after, (await UsageAsync(tx, ctx.PropertyId, def.Master, rowId, c).ConfigureAwait(false)).Count);
        }, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<(string Field, string? Old, string? New)> Diff(OpsMasterDef def, OpsRow? old, MastersRules.CleanRow clean, int sort, IReadOnlyDictionary<string, string?> fields)
    {
        var list = new List<(string, string?, string?)>();
        void Add(string key, string? o, string? n)
        {
            if (old is null || !string.Equals(o, n, StringComparison.Ordinal))
                list.Add((key, old is null ? null : OpsMastersRules.Show(key, o), OpsMastersRules.Show(key, n)));
        }

        Add("code", old?.Code, clean.Code);
        Add("name", old?.Name, clean.Name);
        Add("sortOrder", old?.SortOrder.ToString(CultureInfo.InvariantCulture), sort.ToString(CultureInfo.InvariantCulture));
        foreach (var k in def.Keys) Add(k, old?.Fields.GetValueOrDefault(k), fields.GetValueOrDefault(k));
        return list;
    }

    // ───────────── Retire / Reactivate / Delete / Reorder (shared) ─────────────
    public Task<CommandResult<OpsStatusResult>> RetireAsync(OpsMasterDef def, CommandContext ctx, OpsRowRequest req, CancellationToken ct = default) => SetActiveAsync(def, ctx, req, false, ct);

    public Task<CommandResult<OpsStatusResult>> ReactivateAsync(OpsMasterDef def, CommandContext ctx, OpsRowRequest req, CancellationToken ct = default) => SetActiveAsync(def, ctx, req, true, ct);

    private async Task<CommandResult<OpsStatusResult>> SetActiveAsync(OpsMasterDef def, CommandContext ctx, OpsRowRequest req, bool active, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = active ? def.Reactivate : def.Retire }, req, async (tx, c) =>
        {
            var rows = await store.LockAllAsync(tx, def, ctx.PropertyId, c).ConfigureAwait(false);
            var r = rows.FirstOrDefault(x => x.Id == req.Id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {def.Singular} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, $"This {def.Singular}");
            if (r.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{r.Name} is already active." : $"{r.Name} is already retired.");
            if (active)
            {
                if (rows.Any(x => x.Id != r.Id && x.Active && string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
                    throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {def.Singular} is already called “{r.Name}” — rename one first.", "name");
                if (def.Master == OpsMasterIds.HkTaskTypes)
                    OpsMastersRules.EnsureKindsFree(r.Fields["appliesTo"] ?? string.Empty, true, rows.Select(x => (x.Id, x.Name, x.Active, x.Fields["appliesTo"] ?? string.Empty)), r.Id);
                if (def.Master == OpsMasterIds.HkZones)
                    OpsMastersRules.EnsureZoneFree(new OpsMastersRules.CleanZone(r.Fields["floors"] ?? string.Empty, r.Fields["rooms"] ?? string.Empty), true,
                        rows.Select(x => (x.Id, x.Name, x.Active, x.Fields["floors"] ?? string.Empty, x.Fields["rooms"] ?? string.Empty)), r.Id);
            }

            var date = await mst.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await store.SetActiveAsync(tx, def, r.Id, expected, active, active ? null : date, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await mst.AddHistoryAsync(tx, ctx.PropertyId, def.Master, r.Id, r.Code, "active", r.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var used = await UsageAsync(tx, ctx.PropertyId, def.Master, r.Id, c).ConfigureAwait(false);
            var detail = $"Master {def.Label}: active {(r.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({r.Code} · {r.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used.Count > 0 ? $"; still used: {used.Count}, old records keep the name)" : ")");
            await tx.AuditAsync(def.AuditKind, Target(def, r.Id), detail, c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", r.Id, new { master = def.Master, rowId = r.Id, action = active ? "reactivated" : "retired" }, c).ConfigureAwait(false);
            var after = (await store.LockOneAsync(tx, def, ctx.PropertyId, r.Id, c).ConfigureAwait(false))!;
            return new OpsStatusResult(after.Id, after.Code, after.Active, after.RetiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), VersionToken.ToWire(after.RowVer));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<OpsDeleteResult>> DeleteAsync(OpsMasterDef def, CommandContext ctx, OpsRowRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(req.ExpectedVersion);
        return await runner.RunAsync(ctx with { Operation = def.Delete }, req, async (tx, c) =>
        {
            var r = await store.LockOneAsync(tx, def, ctx.PropertyId, req.Id, c).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {def.Singular} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, $"This {def.Singular}");
            var used = await UsageAsync(tx, ctx.PropertyId, def.Master, r.Id, c).ConfigureAwait(false);
            MastersRules.EnsureDeletable(r.Name, used.Count, def.UsedWhat);
            await store.DeleteAsync(tx, def, r.Id, expected, c).ConfigureAwait(false);
            await mst.AddHistoryAsync(tx, ctx.PropertyId, def.Master, r.Id, r.Code, "deleted", r.Name, null, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync(def.AuditKind, Target(def, r.Id), $"Master {def.Label}: {r.Code} · {r.Name} deleted (never used)", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", r.Id, new { master = def.Master, rowId = r.Id, action = "deleted" }, c).ConfigureAwait(false);
            return new OpsDeleteResult(r.Id, r.Code, true);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<OpsReorderResult>> ReorderAsync(OpsMasterDef def, CommandContext ctx, OpsReorderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, ct).ConfigureAwait(false);
        var ids = (req.Ids ?? []).Select(i => checked((int)i)).ToList();
        return await runner.RunAsync(ctx with { Operation = def.Reorder }, req, async (tx, c) =>
        {
            var rows = await store.LockAllAsync(tx, def, ctx.PropertyId, c).ConfigureAwait(false);
            var order = MastersRules.Reorder(rows.Select(r => checked((int)r.Id)).ToList(), ids, def.Singular);
            var moved = new List<string>();
            foreach (var r in rows)
            {
                var n = order[checked((int)r.Id)];
                if (n == r.SortOrder) continue;
                await store.SetSortAsync(tx, def, r.Id, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await mst.AddHistoryAsync(tx, ctx.PropertyId, def.Master, r.Id, r.Code, "sortOrder", r.SortOrder.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                moved.Add(r.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            var codes = rows.OrderBy(r => order[checked((int)r.Id)]).Select(r => r.Code);
            await tx.AuditAsync(def.AuditKind, def.Master + ":order", $"Master {def.Label}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", codes)})", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", 0, new { master = def.Master, action = "reordered" }, c).ConfigureAwait(false);
            return new OpsReorderResult(moved.Count);
        }, ct).ConfigureAwait(false);
    }

    private static string Target(OpsMasterDef def, long id) => def.Master + ":" + id.ToString(CultureInfo.InvariantCulture);
}
