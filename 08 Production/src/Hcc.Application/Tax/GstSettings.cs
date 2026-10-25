using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Domain.Tax;

namespace Hcc.Application.Tax;

/// <summary>
/// The GST master's group in Master configuration ("Money and tax", DEC-057/DEC-069), registered by the Tax module through
/// <see cref="ISettingsProvider"/>. The values themselves are DATED versions in <c>tax.gst_setting_version</c>, changed only by the Owner
/// through <see cref="GstMasterService"/> (one source of truth), so the hub lists every GST key read-only with that pointer.
/// </summary>
public static class GstSettings
{
    public static ISettingsProvider Provider { get; } = new SettingsProvider("gst-master", ConfigGroupIds.Money,
        GstSeed.Settings.Select(d => new SettingDefinition(d.Key, d.Label, SettingType.Fixed)
        {
            FixedText = "Dated GST master · " + d.Unit,
            FixedReason = "Owner only, dated versions — edited in Money and tax → GST master (tax.gst_setting_version). " + GstSeed.Illustrative + ".",
            Editable = false,
            Help = d.Help,
        }).ToList());
}
