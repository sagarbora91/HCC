// F12 MEAS · meas-settings (MEAS-CONTRACT §1.5): server mirror of the prototype's measOn. Same rule as the prototype:
// optional module on AND (core OR (pack on AND item on)). Values come from the same cfg.value rows through ISettingsReader.
using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Application.Configuration;

/// <summary>Measurement switches (module / pack / item). Mirror of <see cref="IModuleGate"/>; property ids are int as in IModuleGate.</summary>
public interface IMeasurementGate
{
    Task<bool> IsOnAsync(int propertyId, string key, CancellationToken ct = default);

    /// <summary>Throws FORBIDDEN with <c>details.reason</c> = MEAS_OFF and <c>details.key</c> while the item is off.</summary>
    Task EnsureOnAsync(int propertyId, string key, CancellationToken ct = default);
}

/// <summary>A report selector that belongs to a measurement item: <see cref="Hcc.Application.Reporting.Rpt.IReportSelector"/>s implement it and
/// ReportRunner hides / refuses them through <see cref="IMeasurementGate"/> (F7 ReportDefinition stays unchanged).</summary>
public interface IMeasuredReport
{
    string MeasKey { get; }
}

/// <summary>Route metadata of <c>RequireHccMeasurement(key)</c> so tests can list the gated routes.</summary>
public sealed record HccMeasGateMetadata(string Key);

public sealed class MeasurementGate(ISettingsReader reader, IModuleGate modules, SettingsRegistry? registry = null) : IMeasurementGate
{
    private async Task<bool> FlagAsync(int propertyId, string key, CancellationToken ct)
    {
        // ConfigCatalog lists the 'measure' group (f12-int), so every key is registered; a registry without the measure provider (tests) reads on.
        if (registry is not null && registry.Find(key) is null) return true;
        var v = await reader.GetAsync(propertyId, key, null, ct).ConfigureAwait(false);
        return v.Kind != Hcc.Domain.Configuration.ConfigValueKind.Boolean || v.Flag;
    }

    public async Task<bool> IsOnAsync(int propertyId, string key, CancellationToken ct = default)
    {
        var item = MeasurementCatalogue.Item(key) ?? throw new InvalidOperationException($"'{key}' is not a measurement key.");
        if (item.Module is { } m && !await modules.IsOnAsync(propertyId, m, ct).ConfigureAwait(false)) return false;
        if (MeasurementCatalogue.IsCore(item, await modules.IsOnAsync(propertyId, MeasurementCatalogue.CreditModule, ct).ConfigureAwait(false))) return true;
        if (!await FlagAsync(propertyId, MeasurementCatalogue.PackKey(item.Pack), ct).ConfigureAwait(false)) return false;
        return await FlagAsync(propertyId, key, ct).ConfigureAwait(false);
    }

    public async Task EnsureOnAsync(int propertyId, string key, CancellationToken ct = default)
    {
        if (await IsOnAsync(propertyId, key, ct).ConfigureAwait(false)) return;
        throw Refusal(key);
    }

    /// <summary>FORBIDDEN (HccErrorCode has no MEAS_OFF yet; integration item) with details.reason MEAS_OFF and details.key.</summary>
    public static HccException Refusal(string key)
    {
        var item = MeasurementCatalogue.Item(key);
        return new HccException(HccErrorCode.FORBIDDEN, $"MEAS_OFF: {item?.Label ?? key} is switched off in Master configuration › Measurement reports. Its data is kept.",
            new Dictionary<string, JsonElement>
            {
                ["reason"] = JsonSerializer.SerializeToElement(MeasurementCatalogue.OffReason),
                ["key"] = JsonSerializer.SerializeToElement(key),
            });
    }
}
