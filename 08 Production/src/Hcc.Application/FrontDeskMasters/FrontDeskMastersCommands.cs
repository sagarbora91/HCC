using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Application.Masters;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.FrontDeskMasters;
using Hcc.Domain.Masters;

namespace Hcc.Application.FrontDeskMasters;

/// <summary>
/// MA-5 commands (prototype MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _DELETE / _REORDER for masters bookingSources, idTypes,
/// numberSeries, policies): Save{BookingSource|IdType|NumberSeries|BookingPolicy}, Retire…, Reactivate…, Delete…, Reorder…. One generic
/// engine on the MA-0 rules (code / name / sort order, retire-not-delete, history in mst.master_history through IMastersStore) plus each
/// list's own checks. Permission: PERM-MST-001 for sources and ID types (a commission change needs PERM-MST-002), PERM-MST-002 (Owner)
/// for numbering and policies (DEC-058). Every command runs through <see cref="ICommandRunner"/> (Idempotency-Key, audit, outbox
/// "MasterChanged") and checks expectedVersion (STALE_VERSION) on stored rows. The first command on a list stores its defaults; rows the
/// client knows as defaults (RowId −1, −2, …) are then found by their default code.
/// </summary>
public sealed class FrontDeskMastersCommands(ICommandRunner runner, IAccessPolicy policy, IFrontDeskMastersStore store, IMastersStore masters, TimeProvider clock,
    ISettingsReader? settings = null)
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static readonly Dictionary<string, (string Save, string Retire, string Reactivate, string Delete, string Reorder)> Ops = new(StringComparer.Ordinal)
    {
        [FrontDeskMasterIds.BookingSources] = ("SaveBookingSource", "RetireBookingSource", "ReactivateBookingSource", "DeleteBookingSource", "ReorderBookingSources"),
        [FrontDeskMasterIds.IdTypes] = ("SaveIdType", "RetireIdType", "ReactivateIdType", "DeleteIdType", "ReorderIdTypes"),
        [FrontDeskMasterIds.NumberSeries] = ("SaveNumberSeries", "RetireNumberSeries", "ReactivateNumberSeries", "DeleteNumberSeries", "ReorderNumberSeries"),
        [FrontDeskMasterIds.Policies] = ("SaveBookingPolicy", "RetireBookingPolicy", "ReactivateBookingPolicy", "DeleteBookingPolicy", "ReorderBookingPolicies"),
    };

    /// <summary>Command (operation) name of a list action: save | retire | reactivate | delete | reorder.</summary>
    public static string Op(string master, string action)
    {
        var o = Ops.TryGetValue(master, out var x) ? x : throw HccException.Validation("Unknown list.");
        return action switch { "save" => o.Save, "retire" => o.Retire, "reactivate" => o.Reactivate, "delete" => o.Delete, "reorder" => o.Reorder, _ => throw new ArgumentOutOfRangeException(nameof(action)) };
    }

    private Task RequireAsync(CommandContext ctx, string master, CancellationToken ct) =>
        policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, FrontDeskMastersRules.IsMoneyList(master) ? MastersPermissions.ManageMoney : MastersPermissions.Manage, null, null, ct);

    private async Task<string?> InvoicePrefixAsync(ICommandTransaction tx, int propertyId, CancellationToken ct) =>
        settings is null ? null : (await settings.GetAsync(tx, propertyId, "invoicePrefix", null, ct).ConfigureAwait(false)).Text;

    /// <summary>The list's rows, locked; an empty list gets its defaults stored first (Fresh = stored by this command).</summary>
    private async Task<(List<FdmRecord> Rows, bool Fresh)> RowsAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct)
    {
        var rows = (await store.LockAllAsync(tx, propertyId, master, ct).ConfigureAwait(false)).ToList();
        if (rows.Count > 0) return (rows, false);
        var defaults = FrontDeskMastersRules.Defaults(master, master == FrontDeskMasterIds.NumberSeries ? await InvoicePrefixAsync(tx, propertyId, ct).ConfigureAwait(false) : null);
        var sort = 0;
        foreach (var d in defaults)
            await store.InsertAsync(tx, propertyId, master, d.Code, d.Name, sort += MastersRules.SortStep, d.Fields, tx.Context.PrincipalId, UtcNow, ct).ConfigureAwait(false);
        await tx.AuditAsync("MST_FDM", "mst-" + master + ":defaults", $"Master {FrontDeskMastersRules.Label(master)}: {defaults.Count} default rows stored", ct).ConfigureAwait(false);
        return ((await store.LockAllAsync(tx, propertyId, master, ct).ConfigureAwait(false)).ToList(), true);
    }

    /// <summary>The row of a request id: a stored row (positive id, version checked) or a default row by its default code (negative id).</summary>
    private async Task<FdmRecord> ResolveAsync(ICommandTransaction tx, string master, List<FdmRecord> rows, bool fresh, int rowId, string? version, CancellationToken ct)
    {
        var what = "This " + FrontDeskMastersRules.Singular(master);
        if (rowId > 0)
        {
            var r = rows.FirstOrDefault(x => x.RowId == rowId) ?? throw new HccException(HccErrorCode.NOT_FOUND, what + " was not found.");
            VersionToken.EnsureMatches(r.RowVer, VersionToken.Parse(version), what);
            return r;
        }

        var defaults = FrontDeskMastersRules.Defaults(master, master == FrontDeskMasterIds.NumberSeries ? await InvoicePrefixAsync(tx, tx.Context.PropertyId, ct).ConfigureAwait(false) : null);
        var i = -rowId - 1;
        if (rowId == 0 || i >= defaults.Count) throw new HccException(HccErrorCode.NOT_FOUND, what + " was not found.");
        if (!fresh) throw new HccException(HccErrorCode.STALE_VERSION, $"{FrontDeskMastersRules.Label(master)} were changed by someone else. Reload and try again.");
        return rows.FirstOrDefault(x => x.Code == defaults[i].Code) ?? throw new HccException(HccErrorCode.NOT_FOUND, what + " was not found.");
    }

    private Task<int> UsageAsync(ICommandTransaction tx, CommandContext ctx, string master, FdmRecord row, IReadOnlyList<FdmRecord> rows, CancellationToken ct) =>
        store.UsageAsync(tx, ctx.PropertyId, master, row, rows, ct);

    // ── Save (one per list) ──

    public Task<CommandResult<FrontDeskRowResult>> SaveBookingSourceAsync(CommandContext ctx, SaveBookingSourceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        _ = FrontDeskMastersRules.BookingSource(req.Segment, req.Channel, req.CommissionBps); // shape errors before the transaction
        return SaveAsync(ctx, FrontDeskMasterIds.BookingSources, req, req.RowId, req.ExpectedVersion, req.Code, req.Name, req.SortOrder,
            old => FrontDeskMastersRules.BookingSource(req.Segment, req.Channel, req.CommissionBps ?? old?.Get<int>("commissionBps") ?? 0),
            async (tx, rows, old, row, c) =>
            {
                var before = old?.Get<int>("commissionBps") ?? 0;
                if ((int)row["commissionBps"]! != before && !await policy.HasAsync(ctx.PropertyId, ctx.PrincipalId, MastersPermissions.ManageMoney, null, c).ConfigureAwait(false))
                    throw new HccException(HccErrorCode.FORBIDDEN, "Access denied: commission is a money field — Owner only (DEC-058).");
                if (row["channel"] is string ch && rows.FirstOrDefault(r => r.RowId != old?.RowId && r.Active && r.Get<string>("segment") == (string)row["segment"]!
                        && string.Equals(r.Get<string>("channel"), ch, StringComparison.OrdinalIgnoreCase)) is { } dup)
                    throw MastersRules.Fail(MasterReasons.Invalid, $"{dup.Name} already stands for {ch}.", "channel");
            }, ct);
    }

    public Task<CommandResult<FrontDeskRowResult>> SaveIdTypeAsync(CommandContext ctx, SaveIdTypeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        return SaveAsync(ctx, FrontDeskMasterIds.IdTypes, req, req.RowId, req.ExpectedVersion, req.Code, req.Name, req.SortOrder,
            _ => FrontDeskMastersRules.IdType(req.Accepted, req.NeedsPhoto, req.ForeignerOnly), null, ct);
    }

    public Task<CommandResult<FrontDeskRowResult>> SaveNumberSeriesAsync(CommandContext ctx, SaveNumberSeriesRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var fields = FrontDeskMastersRules.NumberSeries(req.Document, req.Prefix, req.StartNo, req.Padding, req.Reset, req.ActiveFrom);
        return SaveAsync(ctx, FrontDeskMasterIds.NumberSeries, req, req.RowId, req.ExpectedVersion, req.Code, req.Name, req.SortOrder, _ => fields,
            async (tx, rows, old, row, c) =>
            {
                var doc = (string)row["document"]!;
                var from = (DateOnly)row["activeFrom"]!;
                if (old is not null)
                    FrontDeskMastersRules.EnsureSeriesUnchanged(old.Code, await IssuedAsync(tx, ctx.PropertyId, old, rows, c).ConfigureAwait(false), old.Fields, row);
                if (old is null || old.Get<DateOnly>("activeFrom") != from)
                {
                    var docs = await store.DocumentsAsync(tx, ctx.PropertyId, doc, null, null, c).ConfigureAwait(false);
                    FrontDeskMastersRules.EnsureStartFree(doc, (string)row["reset"]!, from, await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false), docs.LastDocument, docs.FyCodes);
                }

                if (rows.FirstOrDefault(r => r.RowId != old?.RowId && r.Active && r.Get<string>("document") == doc && r.Get<DateOnly>("activeFrom") == from) is { } clash)
                    throw MastersRules.Fail(MasterReasons.Invalid,
                        $"{clash.Code} already starts {FrontDeskMastersRules.DocumentLabel(doc)} numbers on {FrontDeskMastersRules.Iso(from)} (one series at a time — DEC-MA-5).", "activeFrom");
            }, ct);
    }

    public Task<CommandResult<FrontDeskRowResult>> SaveBookingPolicyAsync(CommandContext ctx, SaveBookingPolicyRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var fields = FrontDeskMastersRules.Policy(req.FreeCancelHours, req.CancelChargeBps, req.NoShowChargeBps, req.DepositBps, req.Sources);
        return SaveAsync(ctx, FrontDeskMasterIds.Policies, req, req.RowId, req.ExpectedVersion, req.Code, req.Name, req.SortOrder, _ => fields,
            (_, rows, old, row, _) =>
            {
                FrontDeskMastersRules.EnsurePolicyFits(old?.Code ?? MastersRules.NormalizeCode(req.Code), (IReadOnlyList<string>)row["sources"]!,
                    rows.Where(r => r.RowId != old?.RowId).Select(r => (r.Code, r.Name, r.Active, r.Get<IReadOnlyList<string>>("sources") ?? [])));
                return Task.CompletedTask;
            }, ct);
    }

    /// <summary>Documents issued under a series: its document's documents dated from its start until the next series of that document.</summary>
    private async Task<int> IssuedAsync(ICommandTransaction tx, int propertyId, FdmRecord series, IReadOnlyList<FdmRecord> rows, CancellationToken ct)
    {
        var doc = series.Get<string>("document");
        var from = series.Get<DateOnly>("activeFrom");
        DateOnly? next = rows.Where(r => r.RowId != series.RowId && r.Get<string>("document") == doc && r.Get<DateOnly>("activeFrom") > from)
            .Select(r => (DateOnly?)r.Get<DateOnly>("activeFrom")).Min();
        return (await store.DocumentsAsync(tx, propertyId, doc, from, next, ct).ConfigureAwait(false)).Count;
    }

    private delegate Task Check(ICommandTransaction tx, List<FdmRecord> rows, FdmRecord? old, Dictionary<string, object?> row, CancellationToken ct);

    private async Task<CommandResult<FrontDeskRowResult>> SaveAsync<TReq>(CommandContext ctx, string master, TReq req, int? rowId, string? version, string? code, string? name,
        int? sortOrder, Func<FdmRecord?, Dictionary<string, object?>> fieldsOf, Check? check, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await RequireAsync(ctx, master, ct).ConfigureAwait(false);
        var clean = MastersRules.Validate(code, name, sortOrder);
        if (rowId is > 0) VersionToken.Parse(version);
        var label = FrontDeskMastersRules.Label(master);
        return await runner.RunAsync(ctx with { Operation = Op(master, "save") }, req, async (tx, c) =>
        {
            var (rows, fresh) = await RowsAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            FdmRecord? old = null;
            if (rowId is { } id)
            {
                old = await ResolveAsync(tx, master, rows, fresh, id, version, c).ConfigureAwait(false);
                MastersRules.EnsureActive(old.Active, old.Name);
                MastersRules.EnsureCodeMayChange(old.Code, clean.Code, await UsageAsync(tx, ctx, master, old, rows, c).ConfigureAwait(false), "records");
            }

            if (rows.Any(r => r.RowId != old?.RowId && string.Equals(r.Code, clean.Code, StringComparison.Ordinal)))
                throw MastersRules.Fail(MasterReasons.CodeTaken, $"Code {clean.Code} is already used in {label} (retired rows keep their code).", "code");
            if (rows.Any(r => r.RowId != old?.RowId && r.Active && string.Equals(r.Name, clean.Name, StringComparison.OrdinalIgnoreCase)))
                throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {FrontDeskMastersRules.Singular(master)} is already called “{clean.Name}”.", "name");
            var fields = fieldsOf(old);
            if (check is not null) await check(tx, rows, old, fields, c).ConfigureAwait(false);
            var sort = clean.SortOrder ?? old?.SortOrder ?? MastersRules.NextSort(rows.Select(r => r.SortOrder));
            var changes = Diff(old, clean.Code, clean.Name, sort, fields);
            int id2;
            string detail;
            if (old is not null)
            {
                if (changes.Count == 0) throw HccException.Validation("Nothing changed.");
                id2 = old.RowId;
                await store.UpdateAsync(tx, master, id2, old.RowVer, clean.Code, clean.Name, sort, fields, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = MastersRules.AuditText(label, changes);
            }
            else
            {
                id2 = await store.InsertAsync(tx, ctx.PropertyId, master, clean.Code, clean.Name, sort, fields, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                detail = $"Master {label}: added {clean.Code} · {clean.Name}";
            }

            foreach (var (f, o, n) in changes)
                await masters.AddHistoryAsync(tx, ctx.PropertyId, master, id2, clean.Code, f, o, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            return await FinishAsync(tx, ctx, master, id2, old is null ? "created" : "changed", detail, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private static List<(string Field, string? Old, string? New)> Diff(FdmRecord? old, string code, string name, int sort, IReadOnlyDictionary<string, object?> fields)
    {
        var list = MastersRules.Diff(old is null ? null : new MastersRules.CleanRow(old.Code, old.Name, old.SortOrder), new MastersRules.CleanRow(code, name, sort), sort).ToList();
        foreach (var (k, v) in fields)
        {
            var n = FrontDeskMastersRules.Show(k, v);
            var o = old is null ? null : FrontDeskMastersRules.Show(k, old.Fields.GetValueOrDefault(k));
            if (old is null ? n is not null : !string.Equals(o, n, StringComparison.Ordinal)) list.Add((k, o, n));
        }

        return list;
    }

    // ── Retire / reactivate / delete / reorder ──

    public Task<CommandResult<FrontDeskRowResult>> RetireAsync(CommandContext ctx, string master, FrontDeskRowRequest req, CancellationToken ct = default) =>
        SetActiveAsync(ctx, master, req, false, ct);

    public Task<CommandResult<FrontDeskRowResult>> ReactivateAsync(CommandContext ctx, string master, FrontDeskRowRequest req, CancellationToken ct = default) =>
        SetActiveAsync(ctx, master, req, true, ct);

    /// <summary>Why a row may not be retired or deleted (the series in force today; the default policy), or null.</summary>
    private async Task<string?> KeepReasonAsync(ICommandTransaction tx, CommandContext ctx, string master, FdmRecord row, IReadOnlyList<FdmRecord> rows, CancellationToken ct)
    {
        if (master == FrontDeskMasterIds.Policies && (row.Get<IReadOnlyList<string>>("sources") ?? []).Count == 0 && row.Active)
            return $"{row.Name} is the default policy — bookings from unnamed sources need it.";
        if (master != FrontDeskMasterIds.NumberSeries || !row.Active) return null;
        var today = await masters.BusinessDateAsync(tx, ctx.PropertyId, ct).ConfigureAwait(false);
        var inForce = NumberSeriesSource.InForce(rows, row.Get<string>("document"), today);
        return inForce?.RowId == row.RowId
            ? $"{row.Code} is the {FrontDeskMastersRules.DocumentLabel(row.Get<string>("document"))} series in force today — add the next series (from a date with no documents) before retiring it."
            : null;
    }

    private async Task<CommandResult<FrontDeskRowResult>> SetActiveAsync(CommandContext ctx, string master, FrontDeskRowRequest req, bool active, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, master, ct).ConfigureAwait(false);
        if (req.RowId > 0) VersionToken.Parse(req.ExpectedVersion);
        var label = FrontDeskMastersRules.Label(master);
        return await runner.RunAsync(ctx with { Operation = Op(master, active ? "reactivate" : "retire") }, req, async (tx, c) =>
        {
            var (rows, fresh) = await RowsAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            var r = await ResolveAsync(tx, master, rows, fresh, req.RowId, req.ExpectedVersion, c).ConfigureAwait(false);
            if (r.Active == active) throw new HccException(HccErrorCode.INVALID_TRANSITION, active ? $"{r.Name} is already active." : $"{r.Name} is already retired.");
            if (!active && await KeepReasonAsync(tx, ctx, master, r, rows, c).ConfigureAwait(false) is { } why) throw MastersRules.Fail(MasterReasons.InUse, why);
            if (active)
            {
                if (rows.Any(x => x.RowId != r.RowId && x.Active && string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
                    throw MastersRules.Fail(MasterReasons.Invalid, $"Another active {FrontDeskMastersRules.Singular(master)} is already called “{r.Name}” — rename one first.", "name");
                if (master == FrontDeskMasterIds.NumberSeries && rows.FirstOrDefault(x => x.RowId != r.RowId && x.Active && x.Get<string>("document") == r.Get<string>("document")
                        && x.Get<DateOnly>("activeFrom") == r.Get<DateOnly>("activeFrom")) is { } clash)
                    throw MastersRules.Fail(MasterReasons.Invalid, $"{clash.Code} already starts on the same date — retire it first.");
                if (master == FrontDeskMasterIds.Policies)
                    FrontDeskMastersRules.EnsurePolicyFits(r.Code, r.Get<IReadOnlyList<string>>("sources") ?? [],
                        rows.Where(x => x.RowId != r.RowId).Select(x => (x.Code, x.Name, x.Active, x.Get<IReadOnlyList<string>>("sources") ?? [])));
            }

            var date = await masters.BusinessDateAsync(tx, ctx.PropertyId, c).ConfigureAwait(false);
            await store.SetActiveAsync(tx, master, r.RowId, r.RowVer, active, active ? null : date, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, master, r.RowId, r.Code, "active", r.Active ? "active" : "retired", active ? "active" : "retired", ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            var used = await UsageAsync(tx, ctx, master, r, rows, c).ConfigureAwait(false);
            var detail = $"Master {label}: active {(r.Active ? "yes" : "no")} → {(active ? "yes" : "no")} ({r.Code} · {r.Name} {(active ? "reactivated" : "retired")}"
                + (!active && used > 0 ? $"; still used: {used}, old records keep the name)" : ")");
            return await FinishAsync(tx, ctx, master, r.RowId, active ? "reactivated" : "retired", detail, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FrontDeskDeleteResult>> DeleteAsync(CommandContext ctx, string master, FrontDeskRowRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, master, ct).ConfigureAwait(false);
        if (req.RowId > 0) VersionToken.Parse(req.ExpectedVersion);
        var label = FrontDeskMastersRules.Label(master);
        return await runner.RunAsync(ctx with { Operation = Op(master, "delete") }, req, async (tx, c) =>
        {
            var (rows, fresh) = await RowsAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            var r = await ResolveAsync(tx, master, rows, fresh, req.RowId, req.ExpectedVersion, c).ConfigureAwait(false);
            MastersRules.EnsureDeletable(r.Name, await UsageAsync(tx, ctx, master, r, rows, c).ConfigureAwait(false), "records");
            if (await KeepReasonAsync(tx, ctx, master, r, rows, c).ConfigureAwait(false) is { } why) throw MastersRules.Fail(MasterReasons.InUse, why);
            await store.DeleteAsync(tx, master, r.RowId, r.RowVer, c).ConfigureAwait(false);
            await masters.AddHistoryAsync(tx, ctx.PropertyId, master, r.RowId, r.Code, "deleted", r.Name, null, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
            await tx.AuditAsync("MST_FDM", Target(master, r.RowId), $"Master {label}: {r.Code} · {r.Name} deleted (never used)", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", r.RowId, new { master, rowId = r.RowId, action = "deleted" }, c).ConfigureAwait(false);
            return new FrontDeskDeleteResult(master, r.RowId, r.Code, true);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<FrontDeskReorderResult>> ReorderAsync(CommandContext ctx, string master, FrontDeskReorderRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await RequireAsync(ctx, master, ct).ConfigureAwait(false);
        var label = FrontDeskMastersRules.Label(master);
        return await runner.RunAsync(ctx with { Operation = Op(master, "reorder") }, req, async (tx, c) =>
        {
            var (rows, fresh) = await RowsAsync(tx, ctx.PropertyId, master, c).ConfigureAwait(false);
            var ids = new List<int>();
            foreach (var id in req.RowIds ?? []) ids.Add(id > 0 ? id : (await ResolveAsync(tx, master, rows, fresh, id, null, c).ConfigureAwait(false)).RowId);
            var order = MastersRules.Reorder(rows.Select(r => r.RowId).ToList(), ids, FrontDeskMastersRules.Singular(master));
            var moved = new List<string>();
            foreach (var r in rows)
            {
                var n = order[r.RowId];
                if (n == r.SortOrder) continue;
                await store.SetSortAsync(tx, master, r.RowId, n, ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                await masters.AddHistoryAsync(tx, ctx.PropertyId, master, r.RowId, r.Code, "sortOrder", r.SortOrder.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture), ctx.PrincipalId, UtcNow, c).ConfigureAwait(false);
                moved.Add(r.Code);
            }

            if (moved.Count == 0) throw HccException.Validation("The order did not change.");
            await tx.AuditAsync("MST_FDM", "mst-" + master + ":order",
                $"Master {label}: sortOrder changed for {string.Join(", ", moved)} (new order {string.Join(", ", rows.OrderBy(r => order[r.RowId]).Select(r => r.Code))})", c).ConfigureAwait(false);
            await tx.PublishAsync("MasterChanged", 0, new { master, action = "reordered" }, c).ConfigureAwait(false);
            return new FrontDeskReorderResult(master, rows.Count);
        }, ct).ConfigureAwait(false);
    }

    private async Task<FrontDeskRowResult> FinishAsync(ICommandTransaction tx, CommandContext ctx, string master, int rowId, string action, string detail, CancellationToken ct)
    {
        await tx.AuditAsync("MST_FDM", Target(master, rowId), detail, ct).ConfigureAwait(false);
        await tx.PublishAsync("MasterChanged", rowId, new { master, rowId, action }, ct).ConfigureAwait(false);
        var r = (await store.LockAllAsync(tx, ctx.PropertyId, master, ct).ConfigureAwait(false)).First(x => x.RowId == rowId);
        return new FrontDeskRowResult(master, rowId, r.Code, VersionToken.ToWire(r.RowVer));
    }

    private static string Target(string master, int rowId) => "mst-" + master + ":" + rowId.ToString(CultureInfo.InvariantCulture);
}
