using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;

namespace Hcc.Application.Housekeeping;

/// <summary>
/// Housekeeping's settings in Master configuration (DEC-057), registered by this module through <see cref="ISettingsProvider"/>
/// (group "Housekeeping and maintenance"). <see cref="HousekeepingCommands"/> reads <see cref="InspectionKey"/> through
/// <see cref="ISettingsReader"/> inside the command transaction; <see cref="HousekeepingOptions"/> is only the fallback without configuration.
/// </summary>
public static class HousekeepingSettings
{
    public const string InspectionKey = "inspection";

    public static ISettingsProvider Provider { get; } = new SettingsProvider("housekeeping", ConfigGroupIds.Housekeeping,
    [
        new(InspectionKey, "Supervisor inspection after cleaning", SettingType.Boolean)
        {
            Unit = "on / off", Default = ConfigValue.Of(true), Approver = SettingApprover.Manager, Help = "When off, a completed task makes the room Ready directly.",
            Affects = "Cleaning completed after the change; tasks already awaiting inspection are unaffected.",
        },
    ]);
}
