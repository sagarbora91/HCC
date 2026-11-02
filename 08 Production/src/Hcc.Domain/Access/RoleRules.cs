using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Configuration;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Access;

/// <summary>ADM-2 access level of one role on one sub-function (brief §1): pages hidden + commands refused · pages visible, commands refused · the base's behaviour.</summary>
public enum RoleFeatureAccess
{
    Off = 0,
    View = 1,
    Full = 2,
}

/// <summary>
/// One role as data (ADM-2, DEC-216). Built-ins: <see cref="IsCustom"/> false, no base, no feature rows (always full). Custom roles: a base
/// built-in (never OWN), a money flag and the non-full feature levels (absent = full).
/// </summary>
public sealed record RoleDefinition(string RoleCode, string Name, bool IsCustom, string? BaseRole, bool Money, IReadOnlyDictionary<string, RoleFeatureAccess> Features,
    int Version, bool Retired = false);

/// <summary>A permission id and its money flag (plat.permission.is_money).</summary>
public sealed record PermissionInfo(string PermissionId, bool IsMoney);

/// <summary>An Advanced-editor cell of a custom role (SetRoleGrant). <see cref="Mode"/> null = the permission is removed.</summary>
public sealed record GrantOverride(string PermissionId, GrantMode? Mode, GrantScope Scope, long? Limit, string? LimitUnit);

/// <summary>
/// ADM-2 invariants of custom roles (brief §0–§2), pure: Owner is never a base (DEC-216); HKP / SUP / MNT / RST bases never hold money
/// except own pay PERM-HR-004 (DEC-061, DEC-063 G12); SoD cells may get narrower than the base, never wider (DEC-219); the role grid
/// grants / removes permission ids by the catalogue's <see cref="FeatureDef.PermPrefixes"/> (-001 view, -002 act; -003 approve and
/// -004 money are never added by the grid).
/// </summary>
public static class RoleRules
{
    /// <summary>Own pay (DEC-061): kept by every role, money or not.</summary>
    public const string OwnPay = "PERM-HR-004";

    /// <summary>Refusal reason of a role-level feature refusal (same as a feature switch, DEC-214).</summary>
    public const string ModuleOffReason = "MODULE_OFF";

    public const int MinNameLength = 2;

    public const int MaxNameLength = 40;

    /// <summary>Bases whose money flag is forced false and locked.</summary>
    public static IReadOnlySet<string> MoneyLockedBases { get; } = new HashSet<string>(StringComparer.Ordinal)
        { RoleCodes.Housekeeper, RoleCodes.Supervisor, RoleCodes.Maintenance, RoleCodes.Restaurant };

    /// <summary>SoD limits that may only go down from the base (approval limit, discount limit).</summary>
    public static IReadOnlySet<string> SodLimitNotAboveBase { get; } = new HashSet<string>(StringComparer.Ordinal) { "PERM-APR-001", "PERM-FOL-003" };

    /// <summary>SoD permissions that are never added to a custom role whose base does not hold them (corrections / credit-note approval).</summary>
    public static IReadOnlySet<string> SodNeverAdded { get; } = new HashSet<string>(StringComparer.Ordinal) { "PERM-FOL-005", "PERM-APR-001" };

    /// <summary>Custom role codes: C01…C99.</summary>
    public static bool IsCustomCode(string? code) =>
        code is { Length: 3 } && code[0] == 'C' && char.IsAsciiDigit(code[1]) && char.IsAsciiDigit(code[2]) && code != "C00";

    /// <summary>The first free code C01…C99 (retired codes are never reused).</summary>
    public static string NextCode(IEnumerable<string> used)
    {
        ArgumentNullException.ThrowIfNull(used);
        var taken = used.ToHashSet(StringComparer.Ordinal);
        for (var i = 1; i <= 99; i++)
        {
            var code = "C" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(code)) return code;
        }

        throw HccException.Validation("No more custom roles can be created (C01–C99 are all used).");
    }

    /// <summary>DEC-216: any built-in except the Owner can be a base.</summary>
    public static bool CanBeBase(string? code) => code is not null && code != RoleCodes.Owner && RoleCodes.All.Contains(code, StringComparer.Ordinal);

    public static bool MoneyLocked(string baseRole) => MoneyLockedBases.Contains(baseRole);

    /// <summary>The stored money flag: forced false for the four locked bases.</summary>
    public static bool EffectiveMoney(string baseRole, bool requested) => requested && !MoneyLocked(baseRole);

    public static string NormalizeName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length is < MinNameLength or > MaxNameLength) throw HccException.Validation($"The role name must be {MinNameLength}–{MaxNameLength} characters.");
        return n;
    }

    public static string ToWire(RoleFeatureAccess access) => access switch
    {
        RoleFeatureAccess.Off => "off",
        RoleFeatureAccess.View => "view",
        _ => "full",
    };

    public static string ToDb(RoleFeatureAccess access) => ToWire(access).ToUpperInvariant();

    /// <summary>Parses off / view / full (any case); VALIDATION otherwise.</summary>
    public static RoleFeatureAccess Parse(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "OFF" => RoleFeatureAccess.Off,
        "VIEW" => RoleFeatureAccess.View,
        "FULL" => RoleFeatureAccess.Full,
        _ => throw HccException.Validation("Access must be off, view or full."),
    };

    /// <summary>A role's level on a feature: core features and missing rows are full; built-ins are always full.</summary>
    public static RoleFeatureAccess LevelOf(RoleDefinition role, string featureId)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.IsCustom ? LevelOf(role.Features, featureId) : RoleFeatureAccess.Full;
    }

    public static RoleFeatureAccess LevelOf(IReadOnlyDictionary<string, RoleFeatureAccess> features, string featureId)
    {
        ArgumentNullException.ThrowIfNull(features);
        var f = FeatureCatalog.Feature(featureId);
        if (f is null || f.Core) return RoleFeatureAccess.Full;
        return features.TryGetValue(featureId, out var level) ? level : RoleFeatureAccess.Full;
    }

    /// <summary>A user with several roles gets the union (the highest level); no role at all = full (the permission check refuses anyway).</summary>
    public static RoleFeatureAccess UserLevel(IEnumerable<RoleDefinition> held, string featureId)
    {
        ArgumentNullException.ThrowIfNull(held);
        var levels = held.Select(r => LevelOf(r, featureId)).ToList();
        return levels.Count == 0 ? RoleFeatureAccess.Full : levels.Max();
    }

    /// <summary>The non-full levels of a user (capabilities <c>features</c>), in catalogue order.</summary>
    public static IReadOnlyDictionary<string, RoleFeatureAccess> UserLevels(IReadOnlyList<RoleDefinition> held)
    {
        ArgumentNullException.ThrowIfNull(held);
        var result = new Dictionary<string, RoleFeatureAccess>(StringComparer.Ordinal);
        if (!held.Any(r => r.IsCustom)) return result;
        foreach (var f in FeatureCatalog.Switchable)
        {
            var level = UserLevel(held, f.Id);
            if (level != RoleFeatureAccess.Full) result[f.Id] = level;
        }

        return result;
    }

    /// <summary>
    /// Sets one feature level (brief §2.2 SetRoleFeature): non-core features only; off / view take every feature that (transitively)
    /// requires this one down to the same level when it was higher (the ADM-1 closure). An explicit full is kept as a row: it is what asks
    /// the grid to add the feature's view + act ids when the base lacked them (an absent row = full = the base's behaviour, nothing added).
    /// </summary>
    public static IReadOnlyDictionary<string, RoleFeatureAccess> ApplyFeature(IReadOnlyDictionary<string, RoleFeatureAccess> current, string featureId, RoleFeatureAccess access)
    {
        ArgumentNullException.ThrowIfNull(current);
        var f = FeatureCatalog.Feature(featureId ?? string.Empty) ?? throw HccException.Validation($"Unknown feature '{featureId}'.");
        if (f.Core) throw HccException.Validation($"{f.Label} is a core sub-function: it follows the base role unchanged (DEC-215).");
        var next = new Dictionary<string, RoleFeatureAccess>(current, StringComparer.Ordinal) { [f.Id] = access };
        if (access != RoleFeatureAccess.Full)
        {
            foreach (var d in FeatureCatalog.Closure(f.Id))
                if (LevelOf(next, d) > access) next[d] = access;
        }

        return next.Where(kv => FeatureCatalog.Feature(kv.Key) is { Core: false })
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>Every distinct permission prefix of the catalogue.</summary>
    public static IReadOnlyList<string> AllPrefixes { get; } = FeatureCatalog.All.SelectMany(f => f.PermPrefixes).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// A prefix is shared by several features (accounting.* all use PERM-ACC-); its level is the highest level among the features using it,
    /// core features counting as full. So a core sibling keeps the ids, and the route gate (RequireHccFeature) refuses the switched part.
    /// </summary>
    public static RoleFeatureAccess PrefixLevel(IReadOnlyDictionary<string, RoleFeatureAccess> features, string prefix)
    {
        ArgumentNullException.ThrowIfNull(features);
        var users = FeatureCatalog.All.Where(f => f.PermPrefixes.Contains(prefix, StringComparer.Ordinal)).ToList();
        return users.Count == 0 ? RoleFeatureAccess.Full : users.Max(f => LevelOf(features, f.Id));
    }

    /// <summary>Feature → the permission ids of the catalogue that the grid adds (<c>-001</c> view, <c>-002</c> act).</summary>
    public static IReadOnlyList<string> GridIds(string prefix, RoleFeatureAccess access) => access switch
    {
        RoleFeatureAccess.Full => new[] { prefix + "001", prefix + "002" },
        RoleFeatureAccess.View => new[] { prefix + "001" },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// The custom role's effective grants (what plat.role_grant holds): the frozen base copy, then per prefix off = remove every id,
    /// view = keep only <c>-001</c> (added A·P when the base lacked it), full = the base's ids or, when the base had none and a feature using the
    /// prefix is set to full explicitly, <c>-001</c> + <c>-002</c> A·P; then the money flag (money off removes every money id except own pay; the grid never adds a money id); then the Advanced overrides.
    /// </summary>
    public static IReadOnlyList<RoleGrant> Materialize(string roleCode, bool money, IReadOnlyDictionary<string, RoleFeatureAccess> features,
        IReadOnlyList<RoleGrant> origin, IReadOnlyList<GrantOverride> overrides, IReadOnlyList<PermissionInfo> permissions)
    {
        ArgumentNullException.ThrowIfNull(roleCode);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(permissions);
        var info = permissions.ToDictionary(p => p.PermissionId, StringComparer.Ordinal);
        bool IsMoney(string id) => info.TryGetValue(id, out var p) && p.IsMoney && id != OwnPay;
        var map = origin.ToDictionary(g => g.PermissionId, g => g with { RoleCode = roleCode }, StringComparer.Ordinal);

        foreach (var prefix in AllPrefixes)
        {
            var level = PrefixLevel(features, prefix);
            var ids = map.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            switch (level)
            {
                case RoleFeatureAccess.Off:
                    foreach (var k in ids) map.Remove(k);
                    break;
                case RoleFeatureAccess.View:
                    foreach (var k in ids.Where(x => !x.EndsWith("-001", StringComparison.Ordinal))) map.Remove(k);
                    foreach (var add in GridIds(prefix, level).Where(a => info.ContainsKey(a) && !IsMoney(a) && !map.ContainsKey(a)))
                        map[add] = new RoleGrant(roleCode, add, GrantMode.Allow, GrantScope.Property, null);
                    break;
                default:
                    var asked = FeatureCatalog.All.Any(f => !f.Core && f.PermPrefixes.Contains(prefix, StringComparer.Ordinal)
                        && features.TryGetValue(f.Id, out var explicitLevel) && explicitLevel == RoleFeatureAccess.Full);
                    if (ids.Count == 0 && asked)
                        foreach (var add in GridIds(prefix, level).Where(a => info.ContainsKey(a) && !IsMoney(a)))
                            map[add] = new RoleGrant(roleCode, add, GrantMode.Allow, GrantScope.Property, null);
                    break;
            }
        }

        if (!money)
            foreach (var k in map.Keys.Where(IsMoney).ToList()) map.Remove(k);

        foreach (var o in overrides)
        {
            if (o.Mode is null) map.Remove(o.PermissionId);
            else if (money || !IsMoney(o.PermissionId)) map[o.PermissionId] = new RoleGrant(roleCode, o.PermissionId, o.Mode.Value, o.Scope, o.Limit, o.LimitUnit);
        }

        return map.Values.OrderBy(g => g.PermissionId, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Advanced editor checks (SetRoleGrant): the money invariant (DEC-061 / DEC-063 G12) and SoD (DEC-219): the approval and discount limits
    /// may only go down from the base, corrections / credit-note approval and the approval permission are never added when the base lacks them,
    /// and a SoD cell never becomes wider than the base's (an unlimited A where the base had a limit).
    /// </summary>
    public static void ValidateOverride(string baseRole, bool money, PermissionInfo permission, GrantOverride cell, RoleGrant? baseGrant)
    {
        ArgumentNullException.ThrowIfNull(baseRole);
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Mode is null) return;   // removing is always allowed (narrower)
        if ((cell.Mode == GrantMode.Limit) != (cell.Limit is not null) || (cell.Limit is null) != (cell.LimitUnit is null) || cell.Limit < 0)
            throw HccException.Validation("A limit (with unit PAISE or BPS) goes with mode L only.");
        if (cell.LimitUnit is not (null or "PAISE" or "BPS")) throw HccException.Validation("The limit unit must be PAISE or BPS.");
        if (permission.IsMoney && permission.PermissionId != OwnPay)
        {
            if (MoneyLocked(baseRole))
                throw new HccException(HccErrorCode.FORBIDDEN, $"A role based on {baseRole} never holds money permissions such as {permission.PermissionId} (DEC-061).");
            if (!money) throw new HccException(HccErrorCode.FORBIDDEN, $"{permission.PermissionId} is a money permission: switch the role's money access on first.");
        }

        if (SodNeverAdded.Contains(permission.PermissionId) && baseGrant is null)
            throw new HccException(HccErrorCode.FORBIDDEN, $"{permission.PermissionId} is a separation-of-duties permission and cannot be added to a role whose base does not hold it (DEC-219).");
        if (SodLimitNotAboveBase.Contains(permission.PermissionId) && baseGrant is not null)
        {
            var baseLimited = baseGrant.Mode == GrantMode.Limit && baseGrant.Limit is not null;
            if (baseLimited && (cell.Mode is GrantMode.Allow || (cell.Mode == GrantMode.Limit && (cell.Limit ?? long.MaxValue) > baseGrant.Limit)))
                throw new HccException(HccErrorCode.FORBIDDEN, $"The {permission.PermissionId} limit may only go down from the base's {baseGrant.Limit} {baseGrant.LimitUnit} (DEC-219).");
            if (baseGrant.Mode == GrantMode.Request && cell.Mode is GrantMode.Allow or GrantMode.Limit)
                throw new HccException(HccErrorCode.FORBIDDEN, $"{permission.PermissionId} is request-only for the base; it cannot become wider (DEC-219).");
        }
    }

    /// <summary>DEC-217: approve (-003) and money (-004) cells of Accounting granted by hand get a warning (they stay Owner + Accountant by default).</summary>
    public static string? AdvancedWarning(PermissionInfo permission, GrantOverride cell)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Mode is null || !permission.PermissionId.StartsWith("PERM-ACC-", StringComparison.Ordinal)) return null;
        return permission.IsMoney || permission.PermissionId.EndsWith("-003", StringComparison.Ordinal) || permission.PermissionId.EndsWith("-004", StringComparison.Ordinal)
            ? "Accounting approve / money cells normally stay with the Owner and the Accountant (DEC-217). Check this grant with the CA."
            : null;
    }

    /// <summary>The refusal of a command whose feature is off or view only for every role the actor holds (brief §1): FORBIDDEN, MODULE_OFF, featureId, roleCode.</summary>
    public static HccException FeatureRefusal(FeatureDef feature, RoleFeatureAccess level, string roleCode, string roleName)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var m = FeatureCatalog.ModuleOf(feature.Id);
        var label = $"{feature.Label} ({m?.Label ?? feature.Id})";
        var message = level == RoleFeatureAccess.View ? $"{label} is view only for your role {roleName}." : $"{label} is not part of your role {roleName}.";
        return new HccException(HccErrorCode.FORBIDDEN, message, new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonSerializer.SerializeToElement(ModuleOffReason),
            ["featureId"] = JsonSerializer.SerializeToElement(feature.Id),
            ["roleCode"] = JsonSerializer.SerializeToElement(roleCode),
            ["access"] = JsonSerializer.SerializeToElement(ToWire(level)),
        });
    }
}
