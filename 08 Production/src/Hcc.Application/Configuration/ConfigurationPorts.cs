using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;

namespace Hcc.Application.Configuration;

/// <summary>
/// PERM ids of Master configuration: the module's own set from role matrix v2 (DEC-079, migration 3700; DEC-057/058, D4) instead of
/// PERM-ADM-003 and its view cell. <see cref="ConfigurationView"/> = read the hub (Owner, Tech admin); <see cref="NonMoney"/> = change the
/// non-money groups, with approval where a setting names an approver (Owner, Tech admin); <see cref="Configuration"/> = every group incl.
/// money, tax, payroll, compliance and approval limits, and deciding requests (Owner only, never their own). The Manager has no configuration
/// page (DEC-063 G9), so <see cref="DecideManagerApproved"/> is the Owner's permission too: a request naming the Manager is decided by the
/// Owner. Everyone signed in may read the module switches.
/// </summary>
public static partial class ConfigurationPermissions
{
    public const string ConfigurationView = "PERM-CFG-001";
    public const string NonMoney = "PERM-CFG-002";
    public const string Configuration = "PERM-CFG-003";
    public const string DecideManagerApproved = Configuration;

    [GeneratedRegex(@"^[a-z][a-zA-Z0-9._-]{1,79}$")]
    internal static partial Regex KeyPattern();
}

/// <summary>
/// Provider API (prototype <c>registerSettingsGroup</c>): any module contributes settings to one of the 12 fixed groups by
/// registering one provider in DI — <c>services.AddSingleton&lt;ISettingsProvider&gt;(new SettingsProvider("pos-menu", ConfigGroupIds.Pos, defs))</c>.
/// The hub stores the values (dated, approvals, history, revert); the module reads them with <see cref="ISettingsReader"/>.
/// A later provider with an existing key replaces that key.
/// </summary>
public interface ISettingsProvider
{
    string Id { get; }

    string Group { get; }

    IReadOnlyList<SettingDefinition> Definitions { get; }
}

public sealed record SettingsProvider(string Id, string Group, IReadOnlyList<SettingDefinition> Definitions) : ISettingsProvider;

/// <summary>Every setting in the registry, in group order (built-ins first, then module providers in registration order).</summary>
public sealed partial class SettingsRegistry
{
    private readonly Dictionary<string, RegisteredSetting> _byKey = new(StringComparer.Ordinal);

    // F12-MEAS2-C (M9): key families. A definition whose key is "<prefix>*" (prefix on the explicit list below) stands for every key
    // "<prefix><suffix>" with a suffix of 1 to 20 letters, digits, '_' or '-' (a room-type code). Families are not listed in All (the
    // concrete keys depend on the property's data); Find / Get resolve them, so the hub can store and the reader can read them.
    /// <summary>Prefixes that may register a key family. Keep this list short: a family makes every matching key look registered.</summary>
    public static IReadOnlyList<string> FamilyPrefixes { get; } = ["yld.overbook."];

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex FamilySuffixPattern();

    private readonly Dictionary<string, (string Source, ConfigGroup Group, SettingDefinition Definition)> _families = new(StringComparer.Ordinal);

    public SettingsRegistry(IEnumerable<ISettingsProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var order = new List<string>();
        foreach (var p in BuiltinSettings.Providers().Concat(providers))
        {
            var group = ConfigCatalog.Group(p.Group) ?? throw new InvalidOperationException($"Unknown configuration group {p.Group} (provider {p.Id}).");
            foreach (var d in p.Definitions)
            {
                if (d.Key.EndsWith('*'))
                {
                    var prefix = d.Key[..^1];
                    if (!FamilyPrefixes.Contains(prefix, StringComparer.Ordinal)) throw new InvalidOperationException($"Setting key family '{d.Key}' is not on the family list.");
                    if (string.IsNullOrWhiteSpace(d.Label)) throw new InvalidOperationException($"Setting family '{d.Key}' needs a label.");
                    _families[prefix] = (p.Id, group, d);
                    continue;
                }
                if (!ConfigurationPermissions.KeyPattern().IsMatch(d.Key)) throw new InvalidOperationException($"Setting key '{d.Key}' is not valid.");
                if (string.IsNullOrWhiteSpace(d.Label)) throw new InvalidOperationException($"Setting '{d.Key}' needs a label.");
                order.Remove(d.Key);
                order.Add(d.Key);
                _byKey[d.Key] = RegisteredSetting.From(p.Id, group, d);
            }
        }

        var groupOrder = ConfigCatalog.Groups.Select(g => g.Id).ToList();
        All = order.Select(k => _byKey[k]).OrderBy(s => groupOrder.IndexOf(s.Group.Id)).ToList();
    }

    public IReadOnlyList<RegisteredSetting> All { get; }

    /// <summary>The exact key first; else a registered family whose prefix matches and whose suffix is a valid code (label "&lt;family label&gt; · &lt;suffix&gt;").</summary>
    public RegisteredSetting? Find(string? key)
    {
        if (key is null) return null;
        if (_byKey.TryGetValue(key, out var s)) return s;
        foreach (var (prefix, f) in _families)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var suffix = key[prefix.Length..];
            if (!FamilySuffixPattern().IsMatch(suffix) || !ConfigurationPermissions.KeyPattern().IsMatch(key)) return null;
            return RegisteredSetting.From(f.Source, f.Group, f.Definition with { Key = key, Label = f.Definition.Label + " · " + suffix });
        }
        return null;
    }

    /// <summary>The family definitions (key "&lt;prefix&gt;*"), for parity tests and the hub.</summary>
    public IReadOnlyList<SettingDefinition> Families => _families.Values.Select(f => f.Definition).ToList();

    public RegisteredSetting Get(string key) => Find(key) ?? throw new InvalidOperationException($"Setting '{key}' is not in the Master configuration registry.");

    public IEnumerable<RegisteredSetting> InGroup(string groupId) => All.Where(s => s.Group.Id == groupId);
}

/// <summary>One <c>cfg.setting_change</c> row.</summary>
public sealed record SettingChangeRow(long ChangeId, string Key, DateOnly EffectiveFrom, string ValueJson, string BeforeJson, string BeforeText, string AfterText,
    string? Reason, string Source, int ChangedBy, string ChangedByName, int? ApprovedBy, string? ApprovedByName, long? RequestId, long? RevertOf,
    DateTime ChangedUtc, DateTime? WithdrawnUtc)
{
    public bool Withdrawn => WithdrawnUtc is not null;

    public DatedValue AsDated() => new(ChangeId, EffectiveFrom, ConfigValue.Parse(ValueJson));

    /// <summary>applied · scheduled (effective after the business date) · withdrawn.</summary>
    public string Status(DateOnly businessDate) => Withdrawn ? "withdrawn" : EffectiveFrom > businessDate ? "scheduled" : "applied";
}

public sealed record NewSettingChange(int PropertyId, string Key, DateOnly EffectiveFrom, string ValueJson, string BeforeJson, string BeforeText, string AfterText,
    string? Reason, string Source, int ChangedBy, int? ApprovedBy, long? RequestId, long? RevertOf, DateTime Utc);

/// <summary>One <c>cfg.change_request</c> row.</summary>
public sealed record SettingRequestRow(long RequestId, string Key, string ValueJson, string BeforeJson, DateOnly EffectiveFrom, string Approver, string Summary,
    string? Reason, long? RevertOf, int RequestedBy, string RequestedByName, DateTime RequestedUtc, DateTime ExpiresUtc, string Status, int? DecidedBy,
    string? DecidedByName, string? DecisionReason, long? ChangeId, byte[] Version);

public sealed record NewSettingRequest(int PropertyId, string Key, string ValueJson, string BeforeJson, DateOnly EffectiveFrom, string Approver, string Summary,
    string? Reason, long? RevertOf, int RequestedBy, DateTime RequestedUtc, DateTime ExpiresUtc);

/// <summary>Persistence inside a command transaction (SQL adapter in Infrastructure/Configuration).</summary>
public interface IConfigurationStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Locks the setting row (UPDLOCK, HOLDLOCK); its version, or null when the setting was never changed.</summary>
    Task<byte[]?> LockSettingAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default);

    /// <summary>Creates or touches the setting row; returns the new version.</summary>
    Task<byte[]> TouchSettingAsync(ICommandTransaction tx, int propertyId, string key, DateTime utc, CancellationToken ct = default);

    /// <summary>Every change of one setting (withdrawn included), oldest first.</summary>
    Task<IReadOnlyList<SettingChangeRow>> ChangesAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default);

    Task<SettingChangeRow?> ChangeAsync(ICommandTransaction tx, int propertyId, long changeId, CancellationToken ct = default);

    Task<long> InsertChangeAsync(ICommandTransaction tx, NewSettingChange change, CancellationToken ct = default);

    Task WithdrawChangeAsync(ICommandTransaction tx, long changeId, int principalId, DateTime utc, CancellationToken ct = default);

    Task<SettingRequestRow?> PendingRequestAsync(ICommandTransaction tx, int propertyId, string key, CancellationToken ct = default);

    Task<SettingRequestRow?> LockRequestAsync(ICommandTransaction tx, int propertyId, long requestId, CancellationToken ct = default);

    Task<long> InsertRequestAsync(ICommandTransaction tx, NewSettingRequest request, CancellationToken ct = default);

    Task DecideRequestAsync(ICommandTransaction tx, long requestId, byte[] expectedVersion, string status, int decidedBy, DateTime utc, string? reason, long? changeId, CancellationToken ct = default);

    Task<string> DisplayNameAsync(ICommandTransaction tx, int userId, CancellationToken ct = default);
}

/// <summary>Read side (queries and the reader outside a command).</summary>
public interface IConfigurationReadStore
{
    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    Task<string> PropertyNameAsync(int propertyId, CancellationToken ct = default);

    /// <summary>Not-withdrawn changes of every setting (or one key).</summary>
    Task<IReadOnlyList<SettingChangeRow>> ActiveChangesAsync(int propertyId, string? key, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, byte[]>> VersionsAsync(int propertyId, CancellationToken ct = default);

    /// <summary>Changes newest first (all keys or one), paged.</summary>
    Task<(int Total, IReadOnlyList<SettingChangeRow> Items)> HistoryAsync(int propertyId, string? key, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Every change of one key (withdrawn included), for revert checks on the history page.</summary>
    Task<IReadOnlyList<SettingChangeRow>> AllChangesAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<SettingRequestRow>> RequestsAsync(int propertyId, string? status, CancellationToken ct = default);

    Task<int> SchemaVersionAsync(CancellationToken ct = default);
}

/// <summary>
/// Reader for every module (design §5: one IConfiguration-style service): the value of a setting in force on a business date
/// (default: the property's current business date). Use the transaction overload inside a command for a consistent read.
/// </summary>
public interface ISettingsReader
{
    Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default);

    Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default);
}

/// <summary>
/// Modules on/off (DEC-057, A11-01): every command route of a switchable module carries <c>.RequireHccModule(id)</c> (the endpoint
/// filter calls <see cref="EnsureOnAsync"/>); a switched-off module's commands are refused with FORBIDDEN, <c>details.reason</c>
/// <see cref="ConfigCatalog.ModuleOffReason"/> and <c>details.moduleId</c>. Data is kept: its read routes (and, for document capture,
/// the audited image/number views and the manual erasure purge) keep answering, and its background projectors keep processing
/// already-published outbox events (DEC-057 does not pause them; pausing would also hold back the outbox retention purge).
/// Core modules are never switchable.
/// </summary>
public interface IModuleGate
{
    Task<bool> IsOnAsync(int propertyId, string moduleId, CancellationToken ct = default);

    Task EnsureOnAsync(int propertyId, string moduleId, CancellationToken ct = default);

    /// <summary>
    /// ADM-1 sub-function switch (DEC-214 / DEC-215): on when the feature is core, or when its module switch (if any) is on, its own
    /// <c>feature.&lt;id&gt;</c> setting is not false and every feature it requires is on. Unknown ids throw.
    /// </summary>
    Task<bool> IsFeatureOnAsync(int propertyId, string featureId, CancellationToken ct = default);

    Task EnsureFeatureOnAsync(int propertyId, string featureId, CancellationToken ct = default);
}

public sealed class SettingsReader(SettingsRegistry registry, IConfigurationStore store, IConfigurationReadStore reads) : ISettingsReader
{
    public async Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default)
    {
        var s = registry.Get(key);
        var date = on ?? await reads.BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        var rows = await reads.ActiveChangesAsync(propertyId, key, ct).ConfigureAwait(false);
        return SettingRules.ValueOn(rows.Select(r => r.AsDated()), date, s.Default);
    }

    public async Task<ConfigValue> GetAsync(ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default)
    {
        var s = registry.Get(key);
        var date = on ?? await store.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false);
        var rows = await store.ChangesAsync(tx, propertyId, key, ct).ConfigureAwait(false);
        return SettingRules.ValueOn(rows.Where(r => !r.Withdrawn).Select(r => r.AsDated()), date, s.Default);
    }
}

public sealed class ModuleGate(ISettingsReader reader) : IModuleGate
{
    public async Task<bool> IsOnAsync(int propertyId, string moduleId, CancellationToken ct = default)
    {
        if (ConfigCatalog.Module(moduleId) is null) throw new InvalidOperationException($"'{moduleId}' is not a switchable module.");
        var v = await reader.GetAsync(propertyId, ConfigModuleIds.SettingKey(moduleId), null, ct).ConfigureAwait(false);
        return v.Kind != ConfigValueKind.Boolean || v.Flag;
    }

    public async Task EnsureOnAsync(int propertyId, string moduleId, CancellationToken ct = default)
    {
        if (await IsOnAsync(propertyId, moduleId, ct).ConfigureAwait(false)) return;
        throw Refusal(moduleId);
    }

    public async Task<bool> IsFeatureOnAsync(int propertyId, string featureId, CancellationToken ct = default)
    {
        var f = FeatureCatalog.Feature(featureId) ?? throw new InvalidOperationException($"'{featureId}' is not a catalogue feature.");
        if (f.Core) return true;
        var m = FeatureCatalog.ModuleOf(featureId)!;
        if (m.Switch is { } sw && !await IsOnAsync(propertyId, sw, ct).ConfigureAwait(false)) return false;
        var v = await reader.GetAsync(propertyId, FeatureIds.SettingKey(featureId), null, ct).ConfigureAwait(false);
        if (v.Kind == ConfigValueKind.Boolean && !v.Flag) return false;
        foreach (var q in f.Requires)
            if (!await IsFeatureOnAsync(propertyId, q, ct).ConfigureAwait(false)) return false;
        return true;
    }

    public async Task EnsureFeatureOnAsync(int propertyId, string featureId, CancellationToken ct = default)
    {
        if (await IsFeatureOnAsync(propertyId, featureId, ct).ConfigureAwait(false)) return;
        throw FeatureRefusal(featureId);
    }

    /// <summary>The refusal of a switched-off sub-function's command: FORBIDDEN, the prototype's text, reason MODULE_OFF, the feature id and its module id.</summary>
    public static HccException FeatureRefusal(string featureId)
    {
        var f = FeatureCatalog.Feature(featureId) ?? throw new InvalidOperationException($"'{featureId}' is not a catalogue feature.");
        var m = FeatureCatalog.ModuleOf(featureId)!;
        return new HccException(HccErrorCode.FORBIDDEN, FeatureCatalog.FeatureOffMessage(f), new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["reason"] = System.Text.Json.JsonSerializer.SerializeToElement(ConfigCatalog.ModuleOffReason),
            ["moduleId"] = System.Text.Json.JsonSerializer.SerializeToElement(m.Switch ?? m.Id),
            ["featureId"] = System.Text.Json.JsonSerializer.SerializeToElement(f.Id),
        });
    }

    /// <summary>The refusal of a switched-off module's command: FORBIDDEN, the prototype's text, reason MODULE_OFF and the module id.</summary>
    public static HccException Refusal(string moduleId)
    {
        var m = ConfigCatalog.Module(moduleId) ?? throw new InvalidOperationException($"'{moduleId}' is not a switchable module.");
        return new HccException(HccErrorCode.FORBIDDEN, ConfigCatalog.ModuleOffMessage(m), new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["reason"] = System.Text.Json.JsonSerializer.SerializeToElement(ConfigCatalog.ModuleOffReason),
            ["moduleId"] = System.Text.Json.JsonSerializer.SerializeToElement(m.Id),
        });
    }
}

/// <summary>Resolves DEC-058 rights from the access policy (no cache: revocation applies on the next request).</summary>
public sealed class ConfigAuthorityResolver(IAccessPolicy policy)
{
    public async Task<ConfigAuthority> ResolveAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        try
        {
            await policy.RequireAsync(propertyId, principalId, ConfigurationPermissions.Configuration, null, null, ct).ConfigureAwait(false);
            return ConfigAuthority.Full;
        }
        catch (HccException ex) when (ex.Code is HccErrorCode.FORBIDDEN or HccErrorCode.APPROVAL_REQUIRED)
        {
            try
            {
                await policy.RequireAsync(propertyId, principalId, ConfigurationPermissions.NonMoney, null, null, ct).ConfigureAwait(false);
                return ConfigAuthority.Technical;
            }
            catch (HccException inner) when (inner.Code is HccErrorCode.FORBIDDEN or HccErrorCode.APPROVAL_REQUIRED)
            {
                return ConfigAuthority.None;
            }
        }
    }

    public Task<bool> HoldsApproveAsync(int propertyId, int principalId, CancellationToken ct = default)
        => policy.HasAsync(propertyId, principalId, ConfigurationPermissions.DecideManagerApproved, null, ct);
}
