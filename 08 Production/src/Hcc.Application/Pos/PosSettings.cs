using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Domain.Pos;

namespace Hcc.Application.Pos;

/// <summary>
/// POS settings in Master configuration (DEC-057), registered by this module through <see cref="ISettingsProvider"/> (group "Restaurant and POS").
/// The void limit is a money setting: Owner-only to change, read-only for the Tech admin (DEC-058). Dated: a change applies from its effective date.
/// </summary>
public static class PosSettings
{
    public const string VoidLimitKey = "pos.voidLimit";

    public static ISettingsProvider Provider { get; } = new SettingsProvider("pos", ConfigGroupIds.Pos,
    [
        new(VoidLimitKey, "Void limit without a second approver", SettingType.Money)
        {
            Money = true, Min = 0, Max = 10_000_000, Default = ConfigValue.Of(PosRules.VoidLimitPaise), Unit = "₹ per void", Effective = EffectiveMode.Date, Dated = true,
            Help = "A void above this value needs a different user with approval rights.",
            Affects = "Voids requested from the effective date; requests already pending are not re-evaluated.",
        },
    ]);
}
