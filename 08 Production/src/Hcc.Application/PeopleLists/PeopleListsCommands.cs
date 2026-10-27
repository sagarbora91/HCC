using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.PeopleLists;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.PeopleLists;

namespace Hcc.Application.PeopleLists;

/// <summary>
/// People lists commands (MA-6; prototype MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE for the masters designations, shiftPatterns,
/// leaveTypes, holidays): Save/Retire/Reactivate/Reorder/Delete × Designation, ShiftPattern, LeaveType, Holiday. PERM-MST-001 (Owner, Tech admin;
/// DEC-MA-1) changes the lists; the pay fields (designation pay rule; leave paid / quota / carry-forward / encashment, every new leave type) and the
/// whole Holiday calendar need PERM-MST-002 (Owner, DEC-058). Every command runs through <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox
/// "MasterChanged"), checks expectedVersion (STALE_VERSION) on edits and writes one mst.master_history row per changed field. Masters are configuration:
/// no business-day lock. The rules are the prototype's (retire-not-delete when in use, code immutable once used, code taken even by retired rows).
/// </summary>
public sealed class PeopleListsCommands(ICommandRunner runner, IAccessPolicy policy, IPeopleListsStore store, IMastersStore history, IEnumerable<IMasterUsage> usages, TimeProvider clock)
{
    private readonly IReadOnlyList<IMasterUsage> _usages = usages.ToList();

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    internal static string Op(string verb, PeopleList l) => verb + l switch
    {
        PeopleList.Designations => "Designation",
        PeopleList.ShiftPatterns => "ShiftPattern",
        PeopleList.LeaveTypes => "LeaveType",
        _ => "Holiday",
    };

    internal static string OpReorder(PeopleList l) => "Reorder" + l switch
    {
        PeopleList.Designations => "Designations",
        PeopleList.ShiftPatterns => "ShiftPatterns",
        PeopleList.LeaveTypes => "LeaveTypes",
        _ => "Holidays",
    };

    private static string UsedBy(PeopleList l) => l switch
    {
        PeopleList.Designations => "staff records",
        PeopleList.ShiftPatterns => "staff or roster cells",
        PeopleList.LeaveTypes => "leave requests",
        _ => "locked pay-runs",
    };

    private async Task<MasterUsageCount> UsageAsync(ICommandTransaction tx, int propertyId, PeopleList l, long rowId, CancellationToken ct)
    {
        var master = PeopleListsRules.Id(l);
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

    private Task RequireAsync(CommandContext ctx, PeopleList l, CancellationToken ct) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, PeopleListsRules.OwnerOnlyList(l) ? MastersPermissions.ManageMoney : MastersPermissions.Manage, null, null, ct);

    /// <summary>
    /// MA-11 (merge report §6.1): a PAID holiday whose pay month is locked cannot be added, changed, moved, retired, reactivated or deleted —
    /// the rule of Wave B's SavePaidHoliday / RemoveHoliday (DEC-083), so the two holiday commands agree. Restricted holidays do not change pay.
    /// </summary>
    private async Task EnsureHolidayMonthsOpenAsync(ICommandTransaction tx, int propertyId, IEnumerable<(string? Code, string? Kind)> rows, CancellationToken ct)
    {
        foreach (var (code, kind) in rows)
        {
            if (kind != "paid" || !DateOnly.TryParseExact(code, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
            if (await store.PayMonthLockedAsync(tx, propertyId, d, ct).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, $"{Hcc.Domain.Payroll.PayMonth.Of(d).Label} payroll is locked — its holidays cannot change.");
        }
    }

    private Task RequireMoneyAsync(CommandContext ctx, CancellationToken ct) => policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, MastersPermissions.ManageMoney, null, null, ct);

    private static string Target(PeopleList l, int id) => "mst-" + PeopleListsRules.Id(l) + ":" + id.ToString(CultureInfo.InvariantCulture);

    // ── Save ──

    private async Task<CommandResult<TView>> SaveAsync<TView>(PeopleList l, CommandContext ctx, object req, int? id, string? version, CleanListRow cleanIn, Func<ListRec, int, TView> view, CancellationToken ct,
        Func<ListRec, CleanListRow, CleanListRow>? keepHidden = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, l, ct).ConfigureAwait(false);
        byte[]? expected = id is null ? null : VersionToken.Parse(version);
        var label = PeopleListsRules.Label(l);
        return await runner.RunAsync(ctx with { Operation = Op("Save", l) }, req, async (tx, c) =>
        {
            await store.EnsureSeededAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var rows = await store.LockRowsAsync(tx, l, ctx.PropertyId, c).ConfigureAwait(false);
            ListRec? old = null;
            var clean = cleanIn;
            if (id is { } oid)
            {
                old = rows.FirstOrDefault(r => r.Id == oid) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {PeopleListsRules.Singular(l)} was not found.");
                VersionToken.EnsureMatches(old.RowVer, expected!, "This " + PeopleListsRules.Singular(l));
                MastersRules.EnsureActive(old.Active, old.Name);
                var used = await UsageAsync(tx, ctx.PropertyId, l, oid, c).ConfigureAwait(false);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, used.Count, UsedBy(l));
                if (keepHidden is not null) clean = keepHidden(old, clean);
            }

            if (rows.Any(r => r.Id != old?.Id && string.Equals(r.Code, clean.Code, StringComparison.Ordinal)))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in {label} (retired rows keep their code).", "code");
            if (rows.Any(r => r.Id != old?.Id && r.Active && string.Equals(r.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {PeopleListsRules.Singular(l)} is already called “{clean.Name}”.", "name");

            // Pay fields: Owner only (DEC-058), also inside an operational list.
            if (!PeopleListsRules.OwnerOnlyList(l))
            {
                var pay = PeopleListsRules.PayFieldKeys(l);
                var payChanged = pay.Count > 0 && (old is null
                    ? l == PeopleList.LeaveTypes || pay.Any(k => clean.Fields.TryGetValue(k, out var nv) && nv is not null)
                    : pay.Any(k => !Equals(old.Fields.GetValueOrDefault(k), clean.Fields.GetValueOrDefault(k))));
                if (payChanged) await RequireMoneyAsync(ctx, c).ConfigureAwait(false);
            }

            await CheckListRulesAsync(tx, l, ctx.PropertyId, rows, old, clean, c).ConfigureAwait(false);
            if (l == PeopleList.Holidays)
                await EnsureHolidayMonthsOpenAsync(tx, ctx.PropertyId, [(old?.Code, old?.Text("kind")), (clean.Code, clean.Fields.GetValueOrDefault("kind") as string)], c).ConfigureAwait(false);

            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(r => r.SortOrder));
            var changes = PeopleListsRules.Diff(l, old?.Code, old?.Name, old?.SortOrder, old?.Fields, clean, sort);
            int rowId;
            string detail;
            var now = UtcNow;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                rowId = old.Id;
                await store.UpdateAsync(tx, l, rowId, expected!, clean, sort, ctx.PrincipalId, now, c).ConfigureAwait(false);
                detail = MastersRules.AuditText(label, changes);
            }
            else
            {
                rowId = await store.InsertAsync(tx, l, ctx.PropertyId, clean, sort, ctx.PrincipalId, now, c).ConfigureAwait(false);
                detail = $"Master {label}: added {clean.Code} · {clean.Name}";
            }

            foreach (var (field, o, n) in changes)
                await history.AddHistoryAsync(tx, ctx.PropertyId, PeopleListsRules.Id(l), rowId, clean.Code, field, o, n, ctx.PrincipalId, now, c).ConfigureAwait(false);
            if (l == PeopleList.LeaveTypes && clean.Code is "CL" or "SL" or "EL" && (old is null || changes.Any(x => x.Field == "yearlyQuota")))
                await store.MirrorLeavePolicyAsync(tx, ctx.PropertyId, clean.Code, clean.Fields["yearlyQuota"] as decimal? ?? 0m, ctx.PrincipalId, now, c).ConfigureAwait(false);
            return await FinishAsync(tx, l, ctx, rowId, old is null ? "created" : "changed", detail, view, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>List-specific rules that need the other rows or other tables.</summary>
    private async Task CheckListRulesAsync(ICommandTransaction tx, PeopleList l, int propertyId, IReadOnlyList<ListRec> rows, ListRec? old, CleanListRow clean, CancellationToken ct)
    {
        switch (l)
        {
            case PeopleList.Designations:
                if (clean.Fields["dept"] is string dept && !await store.DepartmentExistsAsync(tx, propertyId, dept, ct).ConfigureAwait(false))
                    throw MastersRules.Fail(MasterReasons.Invalid, $"Department {dept} does not exist.", "dept");
                break;
            case PeopleList.ShiftPatterns:
                var s = (string)clean.Fields["start"]!;
                var e = (string)clean.Fields["end"]!;
                if (rows.Any(r => r.Id != old?.Id && r.Active && PeopleListsRules.SameTimes(s, e, r.Text("start")!, r.Text("end")!)))
                    throw MastersRules.Fail(MasterReasons.Invalid, "Another active pattern already has these times.");
                break;
            case PeopleList.LeaveTypes:
                // SCC leave port: the category list is fixed (PeopleListsRules.LeaveType); nothing depends on the other rows any more.
                break;
        }
    }

    // ── Retire / reactivate ──

    private async Task<CommandResult<TView>> SetActiveAsync<TView>(PeopleList l, CommandContext ctx, string verb, object req, int id, string? version, bool active, Func<ListRec, int, TView> view, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, l, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        var label = PeopleListsRules.Label(l);
        return await runner.RunAsync(ctx with { Operation = Op(verb, l) }, req, async (tx, c) =>
        {
            await store.EnsureSeededAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var rows = await store.LockRowsAsync(tx, l, ctx.PropertyId, c).ConfigureAwait(false);
            var r = rows.FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {PeopleListsRules.Singular(l)} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, "This " + PeopleListsRules.Singular(l));
            if (r.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{r.Name} is already active." : $"{r.Name} is already retired.");
            if (active && rows.Any(x => x.Id != r.Id && x.Active && string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {PeopleListsRules.Singular(l)} is already called “{r.Name}” — rename one first.", "name");
            if (active && l == PeopleList.ShiftPatterns && rows.Any(x => x.Id != r.Id && x.Active && PeopleListsRules.SameTimes(r.Text("start")!, r.Text("end")!, x.Text("start")!, x.Text("end")!)))
                throw MastersRules.Fail(MasterReasons.Invalid, "Another active pattern already has these times.");
            if (!active) await CheckCanRetireAsync(tx, l, ctx.PropertyId, rows, r, c).ConfigureAwait(false);
            if (l == PeopleList.Holidays) await EnsureHolidayMonthsOpenAsync(tx, ctx.PropertyId, [(r.Code, r.Text("kind"))], c).ConfigureAwait(false);
            var date = await store.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var now = UtcNow;
            await store.SetActiveAsync(tx, l, r.Id, expected, active, active ? null : date, ctx.PrincipalId, now, c).ConfigureAwait(false);
            await history.AddHistoryAsync(tx, ctx.PropertyId, PeopleListsRules.Id(l), r.Id, r.Code, "active", r.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, now, c).ConfigureAwait(false);
            var used = await UsageAsync(tx, ctx.PropertyId, l, r.Id, c).ConfigureAwait(false);
            var detail = $"Master {label}: active {(r.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({r.Code} · {r.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used.Count > 0 ? $"; still used: {used.Count}, old records keep the name)" : ")");
            return await FinishAsync(tx, l, ctx, r.Id, active ? "reactivated" : "retired", detail, view, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task CheckCanRetireAsync(ICommandTransaction tx, PeopleList l, int propertyId, IReadOnlyList<ListRec> rows, ListRec r, CancellationToken ct)
    {
        if (l == PeopleList.ShiftPatterns)
        {
            var staff = await store.ActiveStaffOnShiftAsync(tx, propertyId, r.Code, ct).ConfigureAwait(false);
            if (staff.Count > 0) throw MastersRules.Fail(MasterReasons.InUse, $"{r.Name} is the default shift of {string.Join(", ", staff)} — change their default shift first.");
        }
        else if (l == PeopleList.LeaveTypes && PeopleListsRules.FixedLeaveCodes.Contains(r.Code))
            throw MastersRules.Fail(MasterReasons.InUse, PeopleListsRules.FixedLeave);
    }

    // ── Delete ──

    private async Task<CommandResult<DeletePeopleListResult>> DeleteAsync(PeopleList l, CommandContext ctx, object req, int id, string? version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, l, ct).ConfigureAwait(false);
        var expected = VersionToken.Parse(version);
        var label = PeopleListsRules.Label(l);
        return await runner.RunAsync(ctx with { Operation = Op("Delete", l) }, req, async (tx, c) =>
        {
            await store.EnsureSeededAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var rows = await store.LockRowsAsync(tx, l, ctx.PropertyId, c).ConfigureAwait(false);
            var r = rows.FirstOrDefault(x => x.Id == id) ?? throw new HccException(HccErrorCode.NOT_FOUND, $"This {PeopleListsRules.Singular(l)} was not found.");
            VersionToken.EnsureMatches(r.RowVer, expected, "This " + PeopleListsRules.Singular(l));
            var used = await UsageAsync(tx, ctx.PropertyId, l, r.Id, c).ConfigureAwait(false);
            MastersRules.EnsureDeletable(r.Name, used.Count, UsedBy(l));
            if (l == PeopleList.Holidays) await EnsureHolidayMonthsOpenAsync(tx, ctx.PropertyId, [(r.Code, r.Text("kind"))], c).ConfigureAwait(false);
            if (l == PeopleList.LeaveTypes && PeopleListsRules.FixedLeaveCodes.Contains(r.Code))
                throw MastersRules.Fail(MasterReasons.InUse, "SCC’s leave categories are fixed — they cannot be deleted.");
            await store.DeleteAsync(tx, l, r.Id, expected, c).ConfigureAwait(false);
            await history.AddHistoryAsync(tx, ctx.PropertyId, PeopleListsRules.Id(l), r.Id, r.Code, "deleted", r.Name, null, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync(PeopleListsRules.AuditAction(l), Target(l, r.Id), $"Master {label}: {r.Code} · {r.Name} deleted (never used)", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", r.Id, new { master = PeopleListsRules.Id(l), rowId = r.Id, action = "deleted" }, c).ConfigureAwait(false);
            return new DeletePeopleListResult(r.Id, r.Code, true);
        }, ct).ConfigureAwait(false);
    }

    // ── Reorder ──

    private async Task<CommandResult<PeopleListPage<TView>>> ReorderAsync<TView>(PeopleList l, CommandContext ctx, object req, IReadOnlyList<int>? ids, Func<ListRec, int, TView> view, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, l, ct).ConfigureAwait(false);
        var label = PeopleListsRules.Label(l);
        return await runner.RunAsync(ctx with { Operation = OpReorder(l) }, req, async (tx, c) =>
        {
            await store.EnsureSeededAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            var rows = await store.LockRowsAsync(tx, l, ctx.PropertyId, c).ConfigureAwait(false);
            var order = MastersRules.Reorder(rows.Select(r => r.Id).ToList(), ids, PeopleListsRules.Singular(l));
            var moved = new List<string>();
            var now = UtcNow;
            foreach (var r in rows)
            {
                var n = order[r.Id];
                if (n == r.SortOrder) continue;
                await store.SetSortAsync(tx, l, r.Id, n, ctx.PrincipalId, now, c).ConfigureAwait(false);
                await history.AddHistoryAsync(tx, ctx.PropertyId, PeopleListsRules.Id(l), r.Id, r.Code, "sortOrder", r.SortOrder.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture), ctx.PrincipalId, now, c).ConfigureAwait(false);
                moved.Add(r.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            var codes = rows.OrderBy(r => order[r.Id]).Select(r => r.Code);
            await tx.AuditAsync(PeopleListsRules.AuditAction(l), "mst-" + PeopleListsRules.Id(l) + ":order", $"Master {label}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", codes)})", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", 0, new { master = PeopleListsRules.Id(l), action = "reordered" }, c).ConfigureAwait(false);
            var after = await store.LockRowsAsync(tx, l, ctx.PropertyId, c).ConfigureAwait(false);
            var views = new List<TView>();
            foreach (var r in after) views.Add(view(r, (await UsageAsync(tx, ctx.PropertyId, l, r.Id, c).ConfigureAwait(false)).Count));
            return new PeopleListPage<TView>(1, views.Count, views.Count, views);
        }, ct).ConfigureAwait(false);
    }

    private async Task<TView> FinishAsync<TView>(ICommandTransaction tx, PeopleList l, CommandContext ctx, int id, string action, string detail, Func<ListRec, int, TView> view, CancellationToken ct)
    {
        await tx.AuditAsync(PeopleListsRules.AuditAction(l), Target(l, id), detail, ct).ConfigureAwait(false);
        await tx.PublishAsync("MasterChanged", id, new { master = PeopleListsRules.Id(l), rowId = id, action }, ct).ConfigureAwait(false);
        var rec = (await store.LockRowsAsync(tx, l, ctx.PropertyId, ct).ConfigureAwait(false)).First(r => r.Id == id);
        return view(rec, (await UsageAsync(tx, ctx.PropertyId, l, id, ct).ConfigureAwait(false)).Count);
    }

    // ── Views ──

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string? Retired(ListRec r) => r.RetiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static DesignationView ToDesignation(ListRec r, int used, bool showPay = true) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), r.Text("dept"), r.Text("grade"),
        showPay ? r.Text("defaultPayRule") : null, used, Iso(r.UpdatedUtc), VersionToken.ToWire(r.RowVer));

    internal static ShiftPatternView ToShift(ListRec r, int used) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), r.Text("start")!, r.Text("end")!, r.Int("breakMinutes") ?? 0,
        r.Flag("night"), r.Text("weeklyOffRule") ?? "fixed", used, Iso(r.UpdatedUtc), VersionToken.ToWire(r.RowVer));

    internal static LeaveTypeView ToLeave(ListRec r, int used) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), r.Flag("paid"), r.Dec("yearlyQuota"),
        used, Iso(r.UpdatedUtc), VersionToken.ToWire(r.RowVer));

    internal static HolidayView ToHoliday(ListRec r, int used) => new(r.Id, r.Code, r.Name, r.SortOrder, r.Active, Retired(r), r.Text("kind") ?? "paid", PeopleListsRules.Fy(r.Code), used, Iso(r.UpdatedUtc),
        VersionToken.ToWire(r.RowVer));

    // ── Designations ──

    public Task<CommandResult<DesignationView>> SaveDesignationAsync(CommandContext ctx, SaveDesignationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        // DefaultPayRule null = unchanged (the pay rule is hidden from roles without PERM-MST-002 / PERM-HR-005, so their form cannot send it back); "" = cleared.
        return SaveAsync(PeopleList.Designations, ctx, req, req.DesignationId, req.ExpectedVersion, PeopleListsRules.Designation(req.Code, req.Name, req.SortOrder, req.Dept, req.Grade, req.DefaultPayRule),
            (r, u) => ToDesignation(r, u), ct, req.DefaultPayRule is null ? (old, c) => c with { Fields = new Dictionary<string, object?>(c.Fields) { ["defaultPayRule"] = old.Text("defaultPayRule") } } : null);
    }

    public Task<CommandResult<DesignationView>> RetireDesignationAsync(CommandContext ctx, RetireDesignationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.Designations, ctx, "Retire", req, req.DesignationId, req.ExpectedVersion, false, (r, u) => ToDesignation(r, u), ct);
    }

    public Task<CommandResult<DesignationView>> ReactivateDesignationAsync(CommandContext ctx, ReactivateDesignationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.Designations, ctx, "Reactivate", req, req.DesignationId, req.ExpectedVersion, true, (r, u) => ToDesignation(r, u), ct);
    }

    public Task<CommandResult<DeletePeopleListResult>> DeleteDesignationAsync(CommandContext ctx, DeleteDesignationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(PeopleList.Designations, ctx, req, req.DesignationId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<PeopleListPage<DesignationView>>> ReorderDesignationsAsync(CommandContext ctx, ReorderDesignationsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(PeopleList.Designations, ctx, req, req.DesignationIds, (r, u) => ToDesignation(r, u), ct);
    }

    // ── Shift patterns ──

    public Task<CommandResult<ShiftPatternView>> SaveShiftPatternAsync(CommandContext ctx, SaveShiftPatternRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(PeopleList.ShiftPatterns, ctx, req, req.ShiftPatternId, req.ExpectedVersion,
            PeopleListsRules.ShiftPattern(req.Code, req.Name, req.SortOrder, req.Start, req.End, req.BreakMinutes, req.Night, req.WeeklyOffRule), ToShift, ct);
    }

    public Task<CommandResult<ShiftPatternView>> RetireShiftPatternAsync(CommandContext ctx, RetireShiftPatternRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.ShiftPatterns, ctx, "Retire", req, req.ShiftPatternId, req.ExpectedVersion, false, ToShift, ct);
    }

    public Task<CommandResult<ShiftPatternView>> ReactivateShiftPatternAsync(CommandContext ctx, ReactivateShiftPatternRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.ShiftPatterns, ctx, "Reactivate", req, req.ShiftPatternId, req.ExpectedVersion, true, ToShift, ct);
    }

    public Task<CommandResult<DeletePeopleListResult>> DeleteShiftPatternAsync(CommandContext ctx, DeleteShiftPatternRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(PeopleList.ShiftPatterns, ctx, req, req.ShiftPatternId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<PeopleListPage<ShiftPatternView>>> ReorderShiftPatternsAsync(CommandContext ctx, ReorderShiftPatternsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(PeopleList.ShiftPatterns, ctx, req, req.ShiftPatternIds, ToShift, ct);
    }

    // ── Leave types ──

    public Task<CommandResult<LeaveTypeView>> SaveLeaveTypeAsync(CommandContext ctx, SaveLeaveTypeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(PeopleList.LeaveTypes, ctx, req, req.LeaveTypeId, req.ExpectedVersion,
            PeopleListsRules.LeaveType(req.Code, req.Name, req.SortOrder, req.Paid, req.YearlyQuota), ToLeave, ct);
    }

    public Task<CommandResult<LeaveTypeView>> RetireLeaveTypeAsync(CommandContext ctx, RetireLeaveTypeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.LeaveTypes, ctx, "Retire", req, req.LeaveTypeId, req.ExpectedVersion, false, ToLeave, ct);
    }

    public Task<CommandResult<LeaveTypeView>> ReactivateLeaveTypeAsync(CommandContext ctx, ReactivateLeaveTypeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.LeaveTypes, ctx, "Reactivate", req, req.LeaveTypeId, req.ExpectedVersion, true, ToLeave, ct);
    }

    public Task<CommandResult<DeletePeopleListResult>> DeleteLeaveTypeAsync(CommandContext ctx, DeleteLeaveTypeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(PeopleList.LeaveTypes, ctx, req, req.LeaveTypeId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<PeopleListPage<LeaveTypeView>>> ReorderLeaveTypesAsync(CommandContext ctx, ReorderLeaveTypesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(PeopleList.LeaveTypes, ctx, req, req.LeaveTypeIds, ToLeave, ct);
    }

    // ── Holiday calendar ──

    public Task<CommandResult<HolidayView>> SaveHolidayAsync(CommandContext ctx, SaveHolidayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(PeopleList.Holidays, ctx, req, req.HolidayId, req.ExpectedVersion, PeopleListsRules.Holiday(req.Code, req.Name, req.SortOrder, req.Kind), ToHoliday, ct);
    }

    public Task<CommandResult<HolidayView>> RetireHolidayAsync(CommandContext ctx, RetireHolidayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.Holidays, ctx, "Retire", req, req.HolidayId, req.ExpectedVersion, false, ToHoliday, ct);
    }

    public Task<CommandResult<HolidayView>> ReactivateHolidayAsync(CommandContext ctx, ReactivateHolidayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SetActiveAsync(PeopleList.Holidays, ctx, "Reactivate", req, req.HolidayId, req.ExpectedVersion, true, ToHoliday, ct);
    }

    public Task<CommandResult<DeletePeopleListResult>> DeleteHolidayAsync(CommandContext ctx, DeleteHolidayRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return DeleteAsync(PeopleList.Holidays, ctx, req, req.HolidayId, req.ExpectedVersion, ct);
    }

    public Task<CommandResult<PeopleListPage<HolidayView>>> ReorderHolidaysAsync(CommandContext ctx, ReorderHolidaysRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return ReorderAsync(PeopleList.Holidays, ctx, req, req.HolidayIds, ToHoliday, ct);
    }
}
