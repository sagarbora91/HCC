using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Application.Accounting;

/// <summary>ACC-1 chart permissions (posting contract §7, migration 4202). Reads stay PERM-ACC-001 (<see cref="AccountingPermissions.View"/>).</summary>
public static class ChartPermissions
{
    public const string Edit = "PERM-ACC-007";   // accounting.chart.edit — ledgers and custom sub-groups (Owner, DEC-058)
}

/// <summary>A stored chart history row (<c>acc.ledger_change</c>).</summary>
public sealed record ChartChange(long ChangeId, DateTime AtUtc, int Actor, string Kind, string Code, string Field, string Before, string After);

/// <summary>
/// The property's chart rows (migrations 4200–4201): the Owner's ledger rows (<c>acc.ledger</c>, overrides of built-ins and Owner-added
/// ledgers) and custom sub-groups (<c>acc.ledger_group</c>), plus the history (<c>acc.ledger_change</c>). A null transaction reads committed data.
/// </summary>
public interface IChartStore
{
    Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<IReadOnlyList<StoredLedger>> LedgersAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task<IReadOnlyList<LedgerGroup>> CustomGroupsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Writes the Owner row of <paramref name="code"/> (insert or update); null deletes it (a built-in back to its defaults).</summary>
    Task WriteLedgerAsync(ICommandTransaction tx, int propertyId, string code, StoredLedger? row, int actor, DateTime now, CancellationToken ct);

    Task WriteGroupAsync(ICommandTransaction tx, int propertyId, LedgerGroup group, int actor, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<ChartChange>> AppendChangesAsync(ICommandTransaction tx, int propertyId, string kind, string code, IReadOnlyList<ChartFieldChange> changes,
        int actor, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<ChartChange>> HistoryAsync(int propertyId, CancellationToken ct);

    /// <summary>The property's effective chart (built-ins + Owner rows + custom sub-groups).</summary>
    async Task<EffectiveChart> ChartAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct) =>
        ChartOfAccounts.Merge(await LedgersAsync(tx, propertyId, ct).ConfigureAwait(false), await CustomGroupsAsync(tx, propertyId, ct).ConfigureAwait(false));
}

/// <summary>
/// ACC-1 chart commands (prototype ACC_LEDGER_UPSERT / ACC_LEDGER_RETIRE / ACC_GROUP_UPSERT): Owner only (PERM-ACC-007); rules in
/// <see cref="ChartRules"/>; one history row per changed field; audited. Retiring reads the mapping and the balances of every voucher up to the
/// business date (locked snapshots as locked, live derivation elsewhere) of the ledger and its parties <c>&lt;code&gt;/&lt;party&gt;</c>.
/// </summary>
public sealed class ChartService(ICommandRunner runner, IChartStore chart, IAccountingStore accounting, IAccessPolicy policy, TimeProvider clock)
{
    public const string OpUpsertLedger = "UpsertLedger";
    public const string OpRetireLedger = "RetireLedger";
    public const string OpUpsertGroup = "UpsertLedgerGroup";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    private static ChartChangeView View(ChartChange c) => new(c.ChangeId, c.AtUtc, c.Actor, c.Kind, c.Code, c.Field, c.Before, c.After);

    /// <summary>The mapping keys pointing at <paramref name="code"/> and the balances of it and its parties (every voucher up to the business date).</summary>
    public static async Task<LedgerUsage> UsageAsync(IAccountingStore accounting, ICommandTransaction? tx, int propertyId, string code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accounting);
        ArgumentNullException.ThrowIfNull(code);
        var mapping = await accounting.EffectiveMappingAsync(tx, propertyId, ct).ConfigureAwait(false);
        var keys = mapping.Where(kv => kv.Value == code).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
        var businessDate = await accounting.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var days = await AccountingDays.RangeAsync(accounting, propertyId, DateOnly.MinValue, businessDate, ct).ConfigureAwait(false);
        long own = 0;
        var parties = new Dictionary<string, long>(StringComparer.Ordinal);
        var prefix = code + "/";
        foreach (var e in days.SelectMany(d => d.Vouchers).SelectMany(v => v.Entries))
        {
            if (e.Account == code) own += e.DebitPaise - e.CreditPaise;
            else if (e.Account.StartsWith(prefix, StringComparison.Ordinal))
            {
                var p = e.Account[prefix.Length..];
                parties[p] = parties.GetValueOrDefault(p) + e.DebitPaise - e.CreditPaise;
            }
        }

        return new LedgerUsage(keys, own, parties);
    }

    public async Task<CommandResult<LedgerChangeResult>> UpsertLedgerAsync(Caller caller, string idempotencyKey, UpsertLedgerRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ChartPermissions.Edit, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, OpUpsertLedger, idempotencyKey), req, async (tx, ct2) =>
        {
            await chart.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var current = await chart.ChartAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var code = (req.Code ?? "").Trim();
            // An Owner ledger may change nature only while unused: read its usage (mapping + balances) only for that case.
            var old = current.Ledgers.FirstOrDefault(l => l.Code == code);
            var group = req.GroupId?.Trim();
            var usage = old is { System: false } && group is not null && group != old.GroupId && current.GroupOf(group) is not null
                && current.LegacyOf(group) != current.LegacyOf(old.GroupId)
                ? await UsageAsync(accounting, tx, caller.PropertyId, code, ct2).ConfigureAwait(false)
                : LedgerUsage.None;
            var plan = ChartRules.UpsertLedger(current, new LedgerUpsert(req.Create, req.Code, req.Name, req.GroupId, req.Gst, req.TallyName, req.Note, req.Active), () => usage);
            await chart.WriteLedgerAsync(tx, caller.PropertyId, plan.Code, plan.Row, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var rows = await chart.AppendChangesAsync(tx, caller.PropertyId, "ledger", plan.Code, plan.Changes, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync(plan.Created ? "ACC_LEDGER_ADDED" : "ACC_LEDGER_CHANGED", "acc-ledger:" + plan.Code,
                plan.Created ? $"{plan.Code} {plan.Effective.Name} · {plan.Effective.GroupId}" : string.Join(" · ", plan.Changes.Select(c => $"{c.Field}: {Dash(c.Before)} → {Dash(c.After)}")), ct2).ConfigureAwait(false);
            return new LedgerChangeResult(plan.Code, plan.Effective.Name, plan.Created, plan.Effective.Active, rows.Select(View).ToList());
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LedgerChangeResult>> RetireLedgerAsync(Caller caller, string idempotencyKey, RetireLedgerRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ChartPermissions.Edit, ct: ct).ConfigureAwait(false);
        var code = (req.Code ?? "").Trim();
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length > 200) reason = reason[..200];
        return await runner.RunAsync(Ctx(caller, OpRetireLedger, idempotencyKey), req, async (tx, ct2) =>
        {
            await chart.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var current = await chart.ChartAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var l = current.Ledgers.FirstOrDefault(x => x.Code == code) ?? throw new HccException(HccErrorCode.NOT_FOUND, "This ledger was not found in the chart of accounts.");
            var usage = l.Active && !ChartRules.Fixed.Contains(code) ? await UsageAsync(accounting, tx, caller.PropertyId, code, ct2).ConfigureAwait(false) : LedgerUsage.None;
            if (ChartRules.RetireBlocker(current, code, usage) is { } why) throw MastersRules.Fail(why.Reason, why.Message);
            var row = new StoredLedger(l.Code, l.Name, l.GroupId, l.Gst, l.TallyName, l.System ? l.Note ?? "" : l.Note, false);
            await chart.WriteLedgerAsync(tx, caller.PropertyId, code, row, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var rows = await chart.AppendChangesAsync(tx, caller.PropertyId, "ledger", code,
                [new ChartFieldChange("active", "active", reason.Length > 0 ? "retired · " + reason : "retired")], caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync("ACC_LEDGER_RETIRED", "acc-ledger:" + code, $"{code} {l.Name} retired" + (reason.Length > 0 ? " · " + reason : ""), ct2).ConfigureAwait(false);
            return new LedgerChangeResult(code, l.Name, false, false, rows.Select(View).ToList());
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<LedgerGroupResult>> UpsertGroupAsync(Caller caller, string idempotencyKey, UpsertLedgerGroupRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, ChartPermissions.Edit, ct: ct).ConfigureAwait(false);
        return await runner.RunAsync(Ctx(caller, OpUpsertGroup, idempotencyKey), req, async (tx, ct2) =>
        {
            await chart.SerializeAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var current = await chart.ChartAsync(tx, caller.PropertyId, ct2).ConfigureAwait(false);
            var plan = ChartRules.UpsertGroup(current, new GroupUpsert(req.Id, req.Name, req.Parent));
            await chart.WriteGroupAsync(tx, caller.PropertyId, plan.Row, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            var rows = await chart.AppendChangesAsync(tx, caller.PropertyId, "group", plan.Row.Id, plan.Changes, caller.PrincipalId, Now, ct2).ConfigureAwait(false);
            await tx.AuditAsync(plan.Created ? "ACC_GROUP_ADDED" : "ACC_GROUP_CHANGED", "acc-group:" + plan.Row.Id,
                plan.Created ? $"{plan.Row.Name} ({plan.Row.Id}) under {plan.Row.Parent}" : string.Join(" · ", plan.Changes.Select(c => $"{c.Field}: {Dash(c.Before)} → {Dash(c.After)}")), ct2).ConfigureAwait(false);
            return new LedgerGroupResult(plan.Row.Id, plan.Row.Name, plan.Row.Parent ?? "", plan.Created, rows.Select(View).ToList());
        }, ct).ConfigureAwait(false);
    }

    private static string Dash(string s) => s.Length == 0 ? "—" : s;
}

/// <summary>ACC-1 chart reads (PERM-ACC-001): the property's groups + ledgers + chart problems, and the chart history.</summary>
public sealed class ChartQueries(IChartStore chart, IAccountingStore accounting, IAccessPolicy policy)
{
    public async Task<ChartMasterView> MasterAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        var stored = await chart.LedgersAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var c = ChartOfAccounts.Merge(stored, await chart.CustomGroupsAsync(null, caller.PropertyId, ct).ConfigureAwait(false));
        var mapping = await accounting.EffectiveMappingAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var overridden = stored.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        var canEdit = await policy.HasAsync(caller.PropertyId, caller.PrincipalId, ChartPermissions.Edit, ct: ct).ConfigureAwait(false);
        return new ChartMasterView(
            c.Groups.Select(g => new LedgerGroupView(g.Id, g.Name, g.Parent, g.Nature, g.Side, g.Custom, c.LegacyOf(g.Id).ToString(), c.PathOf(g.Id))).ToList(),
            c.Ledgers.Select(l => new LedgerView(l.Code, l.Name, l.GroupId, l.Group.ToString(), c.NatureOf(l.Code), l.System, l.Control, Gst(l.Gst), l.TallyName, l.Active, l.Note,
                l.System && overridden.Contains(l.Code), mapping.Where(kv => kv.Value == l.Code).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList())).ToList(),
            c.Problems(mapping).Select(p => new ChartProblemView(p.Id, p.Severity, p.Code, p.Title, p.Detail)).ToList(),
            canEdit);
    }

    public async Task<IReadOnlyList<ChartChangeView>> HistoryAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
        return (await chart.HistoryAsync(caller.PropertyId, ct).ConfigureAwait(false))
            .Select(c => new ChartChangeView(c.ChangeId, c.AtUtc, c.Actor, c.Kind, c.Code, c.Field, c.Before, c.After)).ToList();
    }

    private static string Gst(LedgerGst g) => g switch { LedgerGst.Input => "input", LedgerGst.Output => "output", _ => "none" };
}
