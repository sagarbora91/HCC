using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;

namespace Hcc.Application.Yield;

/// <summary>
/// Yield settings in Master configuration (DEC-057), registered by this module (group "Rooms and rate plans", Owner-only like the rest of the group).
/// Only the plain setting lives here; the pricing rules (rate plans, seasons, day-of-week adjustments, guardrails, rounding, restrictions) stay in
/// Yield's own tables because they have their own editor, versions and audit.
/// </summary>
public static class YieldSettings
{
    public const string ProposalHoursKey = "yld.proposalHours";

    public const int DefaultProposalHours = 24;

    public static ISettingsProvider Provider { get; } = new SettingsProvider("yield", ConfigGroupIds.Rooms,
    [
        new(ProposalHoursKey, "Tariff publish proposal lifetime", SettingType.Integer)
        {
            Min = 1, Max = 168, Default = ConfigValue.Of(DefaultProposalHours), Suffix = "h", Unit = "hours", Effective = EffectiveMode.Date, Dated = true,
            Help = "A manager's tariff publish request lapses after this long if the Owner has not decided it.",
            Affects = "Proposals raised from the effective date; pending proposals keep their expiry.",
        },
    ]);
}
