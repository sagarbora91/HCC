namespace Hcc.Application.Maintenance;

/// <summary>Maintenance settings in Master configuration: <c>mnt.pmLeadDays</c> is registered by the hub (group "Housekeeping and maintenance"; default 3 days = <see cref="Hcc.Domain.Maintenance.MntRules.PmHorizonDays"/>).</summary>
public static class MaintenanceSettings
{
    public const string PmLeadDaysKey = "mnt.pmLeadDays";
}
