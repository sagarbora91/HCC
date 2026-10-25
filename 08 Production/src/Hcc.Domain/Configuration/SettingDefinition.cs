using System.Text.RegularExpressions;

namespace Hcc.Domain.Configuration;

/// <summary>Value type of a setting (prototype <c>CfgType</c>). Money = paise; percent = basis points (100 = 1 %).</summary>
public enum SettingType
{
    Text,
    Time,
    Money,
    Boolean,
    Integer,
    Percent,
    Choice,
    Fixed,
}

/// <summary>Immediate = now · Date = from a business date you choose (dated value) · NextDay = from the next business day.</summary>
public enum EffectiveMode
{
    Immediate,
    Date,
    NextDay,
}

/// <summary>Who approves a change made by a non-Owner (Tech admin). The Owner's own change never needs approval (DEC-058).</summary>
public enum SettingApprover
{
    None,
    Manager,
    Owner,
}

/// <summary>
/// What a module declares for one setting (prototype <c>CfgProviderDef</c>): only <see cref="Key"/>, <see cref="Label"/> and
/// <see cref="Type"/> are needed; everything else has a safe default resolved by <see cref="RegisteredSetting.From"/>.
/// Money/tax/statutory values should set <see cref="Dated"/> (effective-from dates; posted records never move).
/// </summary>
public sealed record SettingDefinition(string Key, string Label, SettingType Type)
{
    public string Unit { get; init; } = string.Empty;

    public string Help { get; init; } = string.Empty;

    public ConfigValue Default { get; init; } = ConfigValue.Null;

    public long? Min { get; init; }

    public long? Max { get; init; }

    public int? MaxLength { get; init; }

    public bool AllowEmpty { get; init; }

    public IReadOnlyList<ConfigValue>? Options { get; init; }

    public Regex? Pattern { get; init; }

    public string? PatternMessage { get; init; }

    public string? Suffix { get; init; }

    public EffectiveMode? Effective { get; init; }

    public bool? Dated { get; init; }

    public SettingApprover Approver { get; init; } = SettingApprover.None;

    /// <summary>Money/tax/payroll/compliance/approval-limit setting: Owner-only (DEC-058). Null = the group's flag.</summary>
    public bool? Money { get; init; }

    /// <summary>Never shown in full, never exported, never in audit text.</summary>
    public bool Secret { get; init; }

    /// <summary>True = value masked for everyone without full configuration rights.</summary>
    public bool OwnerOnlyVisible { get; init; }

    public string? Affects { get; init; }

    /// <summary>Statutory/tax values: "Illustrative — CA to confirm for a hotel".</summary>
    public bool Illustrative { get; init; }

    public bool Editable { get; init; } = true;

    public string? FixedReason { get; init; }

    /// <summary>Display text of a fixed (read-only) setting.</summary>
    public string? FixedText { get; init; }

    public Func<ConfigValue, string?>? Validate { get; init; }

    public Func<ConfigValue, string>? Show { get; init; }
}

/// <summary>A definition normalized into the registry (prototype <c>CfgSettingDef</c> = <c>normalize(provider, def)</c>).</summary>
public sealed record RegisteredSetting(
    SettingDefinition Definition,
    ConfigGroup Group,
    string Source,
    SettingType Type,
    bool Editable,
    bool Money,
    EffectiveMode Mode,
    bool Dated,
    bool OwnerOnlyVisible,
    string Approval,
    string EffectiveText,
    string Affects)
{
    public const string DefaultAffects = "Shown on the related screens; existing records are not changed.";

    public string Key => Definition.Key;

    public string Label => Definition.Label;

    public SettingApprover Approver => Definition.Approver;

    public bool Secret => Definition.Secret;

    public ConfigValue Default => Definition.Default;

    public static string EffectiveTextOf(EffectiveMode m) => m switch
    {
        EffectiveMode.Date => "From a business date you choose (dated value)",
        EffectiveMode.NextDay => "From the next business day",
        _ => "Immediately",
    };

    public static RegisteredSetting From(string source, ConfigGroup group, SettingDefinition d)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(d);
        var fixedSetting = d.Type == SettingType.Fixed || !d.Editable;
        var mode = d.Effective ?? (d.Dated == true ? EffectiveMode.Date : EffectiveMode.Immediate);
        var money = d.Money ?? group.Money;
        var approval = fixedSetting
            ? d.FixedReason ?? "Not configurable here"
            : (money ? "Owner only (DEC-058)" : "Owner or Tech admin (DEC-058)")
              + (d.Approver == SettingApprover.None ? string.Empty
                  : $" · Tech admin changes need {(d.Approver == SettingApprover.Owner ? "Owner" : "Manager or Owner")} approval");
        return new RegisteredSetting(d, group, source, fixedSetting ? SettingType.Fixed : d.Type, !fixedSetting, money, mode,
            d.Dated ?? mode == EffectiveMode.Date, d.Secret || d.OwnerOnlyVisible, approval, fixedSetting ? "—" : EffectiveTextOf(mode),
            d.Affects ?? DefaultAffects);
    }
}
