using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;

namespace Hcc.Application.Stays;

/// <summary>
/// CTL-01 (consolidated audit; AUDITFIX-PLAN §2 Q4 default (a)): the Owner-only threshold in Master configuration › Rooms and rate plans above which a
/// rate reduction measured against the stay's AGREED rate needs the Owner. Default 15 % = the Manager's PERM-RATE-001 limit below the tariff (DEC-063
/// role matrix); the stricter of the two rules applies. Read inside the command transaction by <see cref="StayRateCommands"/>; mirrored by the prototype
/// <c>domain/streams/stay-rate.ts</c> (<c>STR_THRESHOLD_KEY</c>).
/// </summary>
public static class RateControlSettings
{
    public const string ThresholdKey = "rate.agreedReductionBps";

    public const int DefaultThresholdBps = 1500;

    public static ISettingsProvider Provider { get; } = new SettingsProvider("rate-control", ConfigGroupIds.Rooms,
    [
        new(ThresholdKey, "Rate reduction below the agreed rate that needs the Owner", SettingType.Percent)
        {
            Min = 0, Max = 10_000, Default = ConfigValue.Of(DefaultThresholdBps), Money = true, Unit = "% below the agreed rate",
            Help = "A Manager may lower an in-house stay's rate by at most this much below the rate agreed on the booking (and never below the PERM-RATE-001 limit under the tariff). Beyond it the Owner decides; every such change is listed in the owner digest.",
            Affects = "Rate changes requested or decided from now on; applied changes and pending requests are not re-evaluated.",
        },
    ]);

    /// <summary>The threshold in force (basis points, 0–10000); the default when unset or the reader is absent.</summary>
    public static int Read(ConfigValue v) =>
        v.Kind == ConfigValueKind.Number && v.Number is >= 0 and <= 10_000 ? (int)v.Number : DefaultThresholdBps;
}
