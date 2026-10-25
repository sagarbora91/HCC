using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Money;

namespace Hcc.Domain.Configuration;

/// <summary>What the principal may do in Master configuration, resolved from the access policy (DEC-058).</summary>
public enum ConfigAuthority
{
    /// <summary>No configuration rights: read-only with the reason.</summary>
    None,

    /// <summary>Tech admin profile (PERM-ADM-003 view cell): non-money settings; settings naming an approver become requests.</summary>
    Technical,

    /// <summary>Owner profile (PERM-ADM-003 allow): every group, no approval.</summary>
    Full,
}

public enum ChangeMode
{
    Allow,
    Request,
    Deny,
}

public sealed record ChangeDecision(ChangeMode Mode, string Reason, SettingApprover Approver = SettingApprover.None);

public sealed record GroupAccess(bool ReadOnly, string? Reason);

/// <summary>A dated value version (one <c>cfg.setting_change</c> row that is not withdrawn).</summary>
public sealed record DatedValue(long ChangeId, DateOnly EffectiveFrom, ConfigValue Value);

/// <summary>
/// Rules of every setting, ported from <c>03 Prototype/src/domain/streams/config.ts</c>: display (<c>cfgShow</c>), validation
/// (<c>cfgValidate</c>), effective dates (<c>cfgEffectiveFrom</c>), dated values (<c>cfgValueOn</c>), DEC-058 permissions
/// (<c>cfgCanChange</c>, <c>cfgGroupAccess</c>, <c>cfgDecideBlock</c>), search and summaries. Messages are the prototype's.
/// </summary>
public static partial class SettingRules
{
    public const string OwnerOnly = "Owner changes this — money, tax, payroll, compliance and approval limits are Owner-only (DEC-058).";
    public const string NotConfigurable = "This setting is not configurable.";
    public const string NoChange = "No change: the setting already has this value.";

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"[^a-z0-9%₹ ]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // ───────────── display ─────────────
    public static string Show(RegisteredSetting s, ConfigValue v)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(v);
        if (s.Secret) return v.IsSet ? "Set (hidden)" : "Not set";
        if (s.Type == SettingType.Fixed) return s.Definition.FixedText ?? "—";
        if (s.Definition.Show is { } show) return show(v);
        if (v.IsNull || (v.Kind == ConfigValueKind.Text && v.Text!.Length == 0)) return "—";
        if (v.Kind != ConfigValueKind.Number && s.Type is SettingType.Money or SettingType.Percent or SettingType.Integer) return v.ToString();
        return s.Type switch
        {
            SettingType.Money => new Paise(v.Number).ToString(),
            SettingType.Boolean => v.Kind == ConfigValueKind.Boolean ? (v.Flag ? "On" : "Off") : v.ToString(),
            SettingType.Percent => Percent(v.Number),
            SettingType.Integer => v.Number.ToString(CultureInfo.InvariantCulture) + (s.Definition.Suffix is { Length: > 0 } x ? " " + x : string.Empty),
            _ => v.ToString(),
        };
    }

    /// <summary>Basis points as the prototype shows them: 1250 → "12.5%".</summary>
    public static string Percent(long bps) => (bps / 100m).ToString("#,##0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>Display for a viewer: owner-only values are masked for everyone without full rights (prototype <c>cfgDisplay</c>).</summary>
    public static string Display(RegisteredSetting s, ConfigValue v, ConfigAuthority viewer)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.OwnerOnlyVisible && viewer != ConfigAuthority.Full)
            return s.Secret ? (v.IsSet ? "Set (hidden)" : "Not set") : "Hidden (Owner only)";
        return Show(s, v);
    }

    // ───────────── validation ─────────────
    /// <summary>Text is trimmed (prototype <c>cfgClean</c>).</summary>
    public static ConfigValue Clean(RegisteredSetting s, ConfigValue v)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(v);
        return s.Type == SettingType.Text && v.Kind == ConfigValueKind.Text ? ConfigValue.Of(v.Text!.Trim()) : v;
    }

    /// <summary>Plain validation message for one value (null = valid).</summary>
    public static string? Validate(RegisteredSetting s, ConfigValue v)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(v);
        var d = s.Definition;
        if (!s.Editable) return NotConfigurable;
        switch (s.Type)
        {
            case SettingType.Text:
            {
                if (v.Kind != ConfigValueKind.Text) return "Enter text.";
                var t = v.Text!.Trim();
                var max = d.MaxLength ?? 60;
                if (t.Length == 0 && !d.AllowEmpty) return $"{d.Label} is required.";
                if (t.Length > max) return $"Use at most {max} characters.";
                if (t.Length > 0 && d.Pattern is { } p && !p.IsMatch(t)) return d.PatternMessage ?? "Check the format.";
                break;
            }

            case SettingType.Time:
                if (v.Kind != ConfigValueKind.Text || !TimePattern().IsMatch(v.Text!)) return "Enter a time as HH:mm.";
                break;
            case SettingType.Boolean:
                if (v.Kind != ConfigValueKind.Boolean) return "Choose on or off.";
                break;
            case SettingType.Choice:
                if (d.Options is null || !d.Options.Contains(v)) return "Choose one of the listed options.";
                break;
            case SettingType.Money:
            {
                var lo = d.Min ?? 0;
                var hi = d.Max ?? 10_000_000;
                if (v.Kind != ConfigValueKind.Number || v.Number < lo || v.Number > hi)
                    return $"Enter an amount from {new Paise(lo)} to {new Paise(hi)}.";
                break;
            }

            case SettingType.Integer:
            case SettingType.Percent:
            {
                var lo = d.Min ?? 0;
                var hi = d.Max ?? 100_000;
                if (v.Kind != ConfigValueKind.Number || v.Number < lo || v.Number > hi)
                    return s.Type == SettingType.Percent
                        ? $"Use a percentage between {Num(lo / 100m)} and {Num(hi / 100m)}%."
                        : $"Use a whole number between {lo} and {hi}{(d.Suffix is { Length: > 0 } x ? " " + x : string.Empty)}.";
                break;
            }

            default:
                return NotConfigurable;
        }

        return d.Validate?.Invoke(v);
    }

    private static string Num(decimal d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    // ───────────── effective dates ─────────────
    /// <summary>Effective-from date of a change made on <paramref name="businessDate"/>; returns an error message instead when the request is invalid.</summary>
    public static (DateOnly Date, string? Error) EffectiveFrom(RegisteredSetting s, DateOnly businessDate, string? requested)
    {
        ArgumentNullException.ThrowIfNull(s);
        var req = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
        switch (s.Mode)
        {
            case EffectiveMode.Immediate:
                return req is not null && req != Wire(businessDate)
                    ? (businessDate, $"{s.Label} takes effect immediately; it cannot be dated.") : (businessDate, null);
            case EffectiveMode.NextDay:
            {
                var nd = businessDate.AddDays(1);
                return req is not null && req != Wire(nd)
                    ? (nd, $"{s.Label} takes effect from the next business day ({Wire(nd)}).") : (nd, null);
            }

            default:
            {
                if (req is null) return (businessDate, null);
                if (!DateOnly.TryParseExact(req, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return (businessDate, "Choose an effective date.");
                if (d < businessDate) return (businessDate, "Effective date cannot be before the current business date.");
                if (d > businessDate.AddDays(366)) return (businessDate, "Effective date can be at most one year ahead.");
                return (d, null);
            }
        }
    }

    public static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ───────────── dated values ─────────────
    /// <summary>Value in force on <paramref name="date"/>: the latest version with effective-from ≤ date (newest change wins on the same date), else the default.</summary>
    public static ConfigValue ValueOn(IEnumerable<DatedValue> versions, DateOnly date, ConfigValue fallback)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var v = versions.Where(x => x.EffectiveFrom <= date).OrderBy(x => x.EffectiveFrom).ThenBy(x => x.ChangeId).LastOrDefault();
        return v?.Value ?? fallback;
    }

    /// <summary>Future-dated versions (effective after the business date), oldest first.</summary>
    public static IReadOnlyList<DatedValue> Scheduled(IEnumerable<DatedValue> versions, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return versions.Where(x => x.EffectiveFrom > businessDate).OrderBy(x => x.EffectiveFrom).ThenBy(x => x.ChangeId).ToList();
    }

    // ───────────── permissions (DEC-058) ─────────────
    /// <summary>Who may change one setting: full rights all; Tech admin non-money only (request when an approver is named); others read-only.</summary>
    public static ChangeDecision Decide(RegisteredSetting s, ConfigAuthority a)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!s.Editable) return new(ChangeMode.Deny, s.Approval);
        switch (a)
        {
            case ConfigAuthority.Full:
                return new(ChangeMode.Allow, "Owner changes all groups (DEC-058).");
            case ConfigAuthority.Technical:
                if (s.Money) return new(ChangeMode.Deny, OwnerOnly);
                if (s.Approver != SettingApprover.None)
                    return new(ChangeMode.Request, $"Your change goes to the {(s.Approver == SettingApprover.Owner ? "Owner" : "Manager or Owner")} for approval.", s.Approver);
                return new(ChangeMode.Allow, "Tech admin changes non-money settings (DEC-058).");
            default:
                return new(ChangeMode.Deny, "Owner changes this. You can review it here.");
        }
    }

    /// <summary>Group-level access: read-only when the user can change none of its settings; the reason is shown (never hidden or redirected).</summary>
    public static GroupAccess Access(ConfigGroup g, IEnumerable<RegisteredSetting> settings, ConfigAuthority a)
    {
        ArgumentNullException.ThrowIfNull(g);
        var editable = settings.Where(s => s.Group.Id == g.Id && s.Editable).ToList();
        if (g.Money && a == ConfigAuthority.Technical) return new(true, OwnerOnly);
        if (editable.Count == 0)
            return new(true, g.Id == ConfigGroupIds.Users ? "Users and roles are changed in Access control." : "Nothing in this group is configurable here.");
        var ds = editable.Select(s => Decide(s, a)).ToList();
        return ds.All(d => d.Mode == ChangeMode.Deny) ? new(true, ds[0].Reason) : new(false, null);
    }

    /// <summary>Why this decider may not decide a request (null = may). Never your own request (PERM-SOD-001).</summary>
    public static string? DecideBlock(int requestedBy, SettingApprover approver, int decider, ConfigAuthority deciderAuthority, bool deciderHoldsApprove)
    {
        if (requestedBy == decider) return "You raised this request — another authorised user must decide it.";
        if (deciderAuthority == ConfigAuthority.Full) return null;
        if (approver == SettingApprover.Manager && deciderHoldsApprove) return null;
        return approver == SettingApprover.Manager ? "Only a Manager or the Owner can decide this request." : "Only the Owner can decide this request.";
    }

    // ───────────── search and summaries ─────────────
    public static string Normalize(string t) => Spaces().Replace(NonWord().Replace((t ?? string.Empty).ToLowerInvariant(), " "), " ").Trim();

    /// <summary>Every word must match the group (name, blurb, section) or a setting (label, key, help, unit).</summary>
    public static IReadOnlyList<(ConfigGroup Group, IReadOnlyList<RegisteredSetting> Settings, bool GroupMatch)> Search(IReadOnlyList<RegisteredSetting> all, string? query)
    {
        ArgumentNullException.ThrowIfNull(all);
        var terms = Normalize(query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return ConfigCatalog.Groups.Select(g => (g, (IReadOnlyList<RegisteredSetting>)[], true)).ToList();
        var hits = new List<(ConfigGroup, IReadOnlyList<RegisteredSetting>, bool)>();
        foreach (var g in ConfigCatalog.Groups)
        {
            var gt = Normalize(g.Label + " " + g.Blurb + " " + g.Section);
            var groupMatch = terms.All(gt.Contains);
            var defs = all.Where(s =>
            {
                if (s.Group.Id != g.Id) return false;
                var dt = Normalize($"{s.Label} {s.Key} {s.Definition.Help} {s.Definition.Unit}");
                return terms.Any(dt.Contains) && terms.All(t => dt.Contains(t, StringComparison.Ordinal) || gt.Contains(t, StringComparison.Ordinal));
            }).ToList();
            if (groupMatch || defs.Count > 0) hits.Add((g, defs, groupMatch));
        }

        return hits;
    }

    /// <summary>One-line live summary per group, e.g. "9 of 10 on · 6 core always on" or "12 settings · 2 changes pending".</summary>
    public static string Summary(ConfigGroup g, IEnumerable<RegisteredSetting> settings, int pending, int scheduledInGroup, int modulesOn)
    {
        ArgumentNullException.ThrowIfNull(g);
        if (g.Id == ConfigGroupIds.Modules) return $"{modulesOn} of {ConfigCatalog.Modules.Count} on · {ConfigCatalog.CoreModules.Count} core always on";
        var editable = settings.Count(s => s.Group.Id == g.Id && s.Editable);
        var parts = new List<string> { $"{editable} setting{(editable == 1 ? "" : "s")}" };
        if (g.Id == ConfigGroupIds.Money && scheduledInGroup > 0) parts.Add($"{scheduledInGroup} dated rate{(scheduledInGroup == 1 ? "" : "s")} ahead");
        if (pending > 0) parts.Add($"{pending} change{(pending == 1 ? "" : "s")} pending");
        return string.Join(" · ", parts);
    }

    /// <summary>"What it affects" lines shown before saving (prototype <c>cfgImpact</c>).</summary>
    public static IReadOnlyList<string> Impact(RegisteredSetting s, ConfigValue v, DateOnly businessDate, DateOnly effectiveFrom, ConfigValue current)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(v);
        var lines = new List<string> { s.Affects };
        if (effectiveFrom > businessDate) lines.Add($"Takes effect on {Wire(effectiveFrom)}; until then {Show(s, current)} stays in force.");
        if (s.Money && s.Dated) lines.Add("Posted records keep the value that applied on their date.");
        if (s.Key.StartsWith("module.", StringComparison.Ordinal) && ConfigCatalog.Module(s.Key["module.".Length..]) is { } m)
            lines.Add(v is { Kind: ConfigValueKind.Boolean, Flag: true }
                ? $"{m.Label} appears again in menus, tiles, search and phone tabs; its commands work again."
                : $"{m.Label} is hidden from menus, tiles, search, reminders and phone tabs, and its commands are refused. Its data and history are kept.");
        return lines;
    }
}
