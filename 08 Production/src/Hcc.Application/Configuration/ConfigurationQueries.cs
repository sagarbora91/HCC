using System.Globalization;
using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;

namespace Hcc.Application.Configuration;

/// <summary>
/// Master configuration reads. The hub (groups, settings, history, search, export, import dry run) needs PERM-ADM-003 at any mode
/// (Owner, Tech admin); groups a viewer cannot change are shown read-only with the reason, never hidden. Owner-only values are
/// masked for everyone else; secrets are never returned. Requests: hub viewers and approvers (PERM-APR-001). Module switches: everyone.
/// </summary>
public sealed class ConfigurationQueries(IAccessPolicy policy, SettingsRegistry registry, IConfigurationReadStore reads, ConfigurationService service, TimeProvider clock)
{
    private readonly ConfigAuthorityResolver _authority = new(policy);

    private sealed record Snapshot(DateOnly BusinessDate, ILookup<string, DatedValue> Versions, IReadOnlyDictionary<string, byte[]> RowVersions, IReadOnlyList<SettingRequestRow> Pending)
    {
        public IReadOnlyList<DatedValue> Of(string key) => Versions[key].ToList();
    }

    private async Task<ConfigAuthority> GateAsync(int propertyId, int principalId, CancellationToken ct)
    {
        await policy.RequireAsync(propertyId, principalId, ConfigurationPermissions.ConfigurationView, null, null, ct).ConfigureAwait(false);
        return await _authority.ResolveAsync(propertyId, principalId, ct).ConfigureAwait(false);
    }

    private async Task<Snapshot> SnapshotAsync(int propertyId, CancellationToken ct)
    {
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var rows = await reads.ActiveChangesAsync(propertyId, null, ct).ConfigureAwait(false);
        var versions = await reads.VersionsAsync(propertyId, ct).ConfigureAwait(false);
        var pending = await reads.RequestsAsync(propertyId, "Pending", ct).ConfigureAwait(false);
        return new Snapshot(bd, rows.ToLookup(r => r.Key, r => r.AsDated(), StringComparer.Ordinal), versions, pending);
    }

    private static ConfigGroupView GroupView(ConfigGroup g, IReadOnlyList<RegisteredSetting> all, Snapshot snap, ConfigAuthority a)
    {
        var keys = all.Where(s => s.Group.Id == g.Id).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var scheduled = keys.Sum(k => SettingRules.Scheduled(snap.Of(k), snap.BusinessDate).Count);
        var pending = snap.Pending.Count(p => keys.Contains(p.Key)) + scheduled;
        var modulesOn = ConfigCatalog.Modules.Count(m => On(all, snap, m.Id));
        var access = SettingRules.Access(g, all, a);
        return new ConfigGroupView(g.Id, g.Label, g.Section, g.Money, g.Blurb, SettingRules.Summary(g, all, pending, scheduled, modulesOn), access.ReadOnly, access.Reason);
    }

    private static bool On(IReadOnlyList<RegisteredSetting> all, Snapshot snap, string moduleId)
    {
        var key = ConfigModuleIds.SettingKey(moduleId);
        var s = all.First(x => x.Key == key);
        var v = SettingRules.ValueOn(snap.Of(key), snap.BusinessDate, s.Default);
        return v.Kind != ConfigValueKind.Boolean || v.Flag;
    }

    public async Task<ConfigGroupsResult> GroupsAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var a = await GateAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var snap = await SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        return new ConfigGroupsResult(SettingRules.Wire(snap.BusinessDate), ConfigCatalog.Groups.Select(g => GroupView(g, registry.All, snap, a)).ToList());
    }

    public async Task<ConfigGroupDetail> GroupAsync(int propertyId, int principalId, string groupId, CancellationToken ct = default)
    {
        var a = await GateAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var g = ConfigCatalog.Group(groupId) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Configuration group not found.");
        var snap = await SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        var settings = registry.InGroup(g.Id).Select(s => SettingView(s, snap, a)).ToList();
        return new ConfigGroupDetail(GroupView(g, registry.All, snap, a), settings);
    }

    private static ConfigSettingView SettingView(RegisteredSetting s, Snapshot snap, ConfigAuthority a)
    {
        var versions = snap.Of(s.Key);
        var v = SettingRules.ValueOn(versions, snap.BusinessDate, s.Default);
        var hidden = s.Secret || (s.OwnerOnlyVisible && a != ConfigAuthority.Full) || s.Type == SettingType.Fixed;
        var dec = SettingRules.Decide(s, a);
        var d = s.Definition;
        return new ConfigSettingView(s.Key, s.Label, s.Group.Id, Camel(s.Type.ToString()), d.Unit, d.Help, s.Editable, s.Approval, s.EffectiveText, Camel(s.Mode.ToString()),
            s.Approver.ToString(), s.Money, s.Secret, s.Dated, d.Illustrative, s.Affects, hidden ? null : v.ToElement(), SettingRules.Display(s, v, a),
            snap.RowVersions.TryGetValue(s.Key, out var rv) ? VersionToken.ToWire(rv) : null,
            new ChangeDecisionView(Camel(dec.Mode.ToString()), dec.Reason, dec.Approver == SettingApprover.None ? null : dec.Approver.ToString()),
            SettingRules.Scheduled(versions, snap.BusinessDate).Select(x => new ScheduledValueView(x.ChangeId, SettingRules.Wire(x.EffectiveFrom), SettingRules.Display(s, x.Value, a))).ToList(),
            snap.Pending.FirstOrDefault(p => p.Key == s.Key)?.RequestId, d.Options?.Select(o => o.ToElement()).ToList(), d.Min, d.Max, d.Suffix);
    }

    private static string Camel(string pascal) => char.ToLowerInvariant(pascal[0]) + pascal[1..];

    public async Task<ConfigSearchResult> SearchAsync(int propertyId, int principalId, string? query, CancellationToken ct = default)
    {
        await GateAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var hits = SettingRules.Search(registry.All, query);
        return new ConfigSearchResult(query ?? string.Empty, hits.Select(h => new ConfigSearchHit(h.Group.Id, h.Group.Label, h.GroupMatch, h.Settings.Select(s => s.Key).ToList())).ToList());
    }

    public async Task<SettingHistoryPage> HistoryAsync(int propertyId, int principalId, string? key, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1) throw HccException.Validation("page starts at 1.");
        if (pageSize < 1 || pageSize > ApiConventions.MaxPageSize) throw HccException.Validation($"pageSize must be 1–{ApiConventions.MaxPageSize}.");
        await GateAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var bd = await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var (total, items) = await reads.HistoryAsync(propertyId, string.IsNullOrWhiteSpace(key) ? null : key, page, pageSize, ct).ConfigureAwait(false);
        var all = (await reads.AllChangesAsync(propertyId, ct).ConfigureAwait(false)).ToLookup(r => r.Key, StringComparer.Ordinal);
        return new SettingHistoryPage(page, pageSize, total, items.Select(r =>
        {
            var s = registry.Find(r.Key);
            var block = ConfigurationService.RevertBlock(r, all[r.Key].ToList(), s, bd);
            return new SettingHistoryEntry(r.ChangeId, r.Key, s?.Label ?? r.Key, r.BeforeText, r.AfterText, SettingRules.Wire(r.EffectiveFrom), r.Status(bd),
                r.ChangedByName, r.ApprovedByName, r.Reason, r.Source, r.RevertOf, Iso(r.ChangedUtc), block is null, block);
        }).ToList());
    }

    public async Task<SettingRequestsResult> RequestsAsync(int propertyId, int principalId, string? status = "Pending", CancellationToken ct = default)
    {
        var a = await _authority.ResolveAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var approve = await _authority.HoldsApproveAsync(propertyId, principalId, ct).ConfigureAwait(false);
        if (a == ConfigAuthority.None && !approve)
            await policy.RequireAsync(propertyId, principalId, ConfigurationPermissions.ConfigurationView, null, null, ct).ConfigureAwait(false);
        var rows = await reads.RequestsAsync(propertyId, string.IsNullOrWhiteSpace(status) ? null : status, ct).ConfigureAwait(false);
        // A Manager without hub access sees only the requests they may decide (approver Manager).
        if (a == ConfigAuthority.None) rows = rows.Where(r => r.Approver == nameof(SettingApprover.Manager)).ToList();
        return new SettingRequestsResult(rows.Select(r => new SettingRequestView(r.RequestId, r.Key, registry.Find(r.Key)?.Label ?? r.Key, r.Summary, r.Reason, r.Approver,
            r.RequestedByName, Iso(r.RequestedUtc), Iso(r.ExpiresUtc), r.Status, r.DecidedByName, r.DecisionReason, SettingRules.Wire(r.EffectiveFrom), VersionToken.ToWire(r.Version),
            r.Status == "Pending" ? SettingRules.DecideBlock(r.RequestedBy, Enum.Parse<SettingApprover>(r.Approver), principalId, a, approve) : null)).ToList());
    }

    /// <summary>Every signed-in user: which optional modules are on (menus, tiles, search and phone tabs hide the others).</summary>
    public async Task<ModulesResult> ModulesAsync(int propertyId, CancellationToken ct = default)
    {
        var snap = await SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        return new ModulesResult(ConfigCatalog.Modules.Select(m => new ModuleStateView(m.Id, m.Label, m.Note, On(registry.All, snap, m.Id))).ToList(), ConfigCatalog.CoreModules);
    }

    /// <summary>
    /// Every signed-in user (W4-5): the values screens read (in force on the business date) and future-dated values. Secrets and fixed
    /// pointer lines are never returned; Owner-only-visible values only for full configuration authority (the Owner).
    /// </summary>
    public async Task<ConfigurationValuesResult> ValuesAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        var a = await _authority.ResolveAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var snap = await SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        var values = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        var scheduled = new List<ExportScheduled>();
        foreach (var s in registry.All)
        {
            if (s.Secret || s.Type == SettingType.Fixed || (s.OwnerOnlyVisible && a != ConfigAuthority.Full)) continue;
            values[s.Key] = SettingRules.ValueOn(snap.Of(s.Key), snap.BusinessDate, s.Default).ToElement();
            scheduled.AddRange(SettingRules.Scheduled(snap.Of(s.Key), snap.BusinessDate).Select(x => new ExportScheduled(s.Key, x.Value.ToElement(), SettingRules.Wire(x.EffectiveFrom))));
        }

        // Row versions for SetSetting's expectedVersion (W4 D2: without them every second edit of a setting was refused as STALE_VERSION).
        var rowVersions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in registry.All)
            if (s.Editable && snap.RowVersions.TryGetValue(s.Key, out var rv)) rowVersions[s.Key] = VersionToken.ToWire(rv);
        return new ConfigurationValuesResult(SettingRules.Wire(snap.BusinessDate), values, scheduled.OrderBy(x => x.EffectiveFrom, StringComparer.Ordinal).ToList(), rowVersions);
    }

    /// <summary>Configuration export for backup / a second property (prototype <c>cfgExport</c>): editable values, no secrets, nothing personal.</summary>
    public async Task<ConfigurationExport> ExportAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await GateAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var snap = await SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        var values = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        var excluded = new List<string>();
        var scheduled = new List<ExportScheduled>();
        foreach (var s in registry.All.Where(x => x.Editable))
        {
            if (s.Secret) { excluded.Add(s.Key); continue; }
            values[s.Key] = SettingRules.ValueOn(snap.Of(s.Key), snap.BusinessDate, s.Default).ToElement();
            scheduled.AddRange(SettingRules.Scheduled(snap.Of(s.Key), snap.BusinessDate).Select(x => new ExportScheduled(s.Key, x.Value.ToElement(), SettingRules.Wire(x.EffectiveFrom))));
        }

        return new ConfigurationExport(ConfigurationService.ExportFormat, 1, await reads.SchemaVersionAsync(ct).ConfigureAwait(false), Iso(clock.GetUtcNow().UtcDateTime),
            SettingRules.Wire(snap.BusinessDate), await reads.PropertyNameAsync(propertyId, ct).ConfigureAwait(false), values,
            scheduled.OrderBy(x => x.EffectiveFrom, StringComparer.Ordinal).ToList(), excluded);
    }

    /// <summary>Import dry run for the caller (no write): what would change, what is skipped and why, blocking errors.</summary>
    public async Task<ImportPlanView> ImportPlanAsync(int propertyId, int principalId, JsonElement config, CancellationToken ct = default)
    {
        var a = await GateAsync(propertyId, principalId, ct).ConfigureAwait(false);
        var snap = await SnapshotAsync(propertyId, ct).ConfigureAwait(false);
        var plan = await service.PlanAsync(config, a, snap.BusinessDate, (k, _) => Task.FromResult(snap.Of(k)), ct).ConfigureAwait(false);
        return new ImportPlanView(plan.Apply.Select(x => new ImportPlanItem(x.Setting.Key, x.Setting.Label, SettingRules.Show(x.Setting, x.Value), SettingRules.Wire(x.EffectiveFrom))).ToList(),
            plan.Skipped, plan.Errors, plan.Unchanged);
    }

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
