using System.Text.Json;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Configuration;
using Hcc.Contracts.Configuration;
using Hcc.Domain.Errors;

namespace Hcc.Application.Egress;

/// <summary>Body of <c>POST /commands/SetNetSetting</c>. <c>ExpectedVersion</c> is the setting's row version from the last load (as SetSetting); null = the setting was never saved.</summary>
public sealed record SetNetSettingRequest(string Setting, JsonElement? Value, string? Reason, string? ExpectedVersion = null);

/// <summary>
/// AF-NETSET (audit item 23): the Owner's Internet switch as one named command. EGRESS-CONTRACT §1.1: the <c>net.*</c> settings are dated,
/// Owner-only (PERM-NET-001), reason required and audited with the Configuration history. This class only adds the explicit permission, the key list and
/// the reason / value shape; the write itself is the EXISTING configuration-change path (<see cref="ConfigurationService.SetSettingAsync"/>: same table,
/// change history with reason, audit <c>SETTING_SET</c>, outbox <c>SettingChanged</c>, idempotency). It never touches <c>ops.egress_firewall_state</c>:
/// the wanted firewall state is written by <c>EgressOutboxWorker</c> from the policy (EGRESS-CONTRACT §4).
/// </summary>
public sealed class NetSettingCommands(ConfigurationService configuration, IAccessPolicy policy)
{
    public const string Operation = "SetNetSetting";

    /// <summary>PERM-NET-001 (migration 4733): change the Internet switches. Owner only.</summary>
    public const string ChangePermission = "PERM-NET-001";

    public const int ReasonMin = 3;
    public const int ReasonMax = 120;

    /// <summary>The keys of EGRESS-CONTRACT §1.1 (a test pins this list to <c>EgressKeys</c>).</summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "net.internetEnabled", "net.messaging", "net.ota", "net.ocr", "net.payments", "net.email", "net.updateCheck", "net.window",
    ];

    public const string WindowKey = "net.window";

    public async Task<CommandResult<SetSettingResult>> SetAsync(CommandContext ctx, SetNetSettingRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, ChangePermission, null, null, ct).ConfigureAwait(false);
        var key = (req.Setting ?? string.Empty).Trim();
        if (!Keys.Contains(key, StringComparer.Ordinal)) throw HccException.Validation("Unknown internet setting.");
        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length is < ReasonMin or > ReasonMax) throw HccException.Validation($"Give a reason for changing an internet switch ({ReasonMin}–{ReasonMax} characters).");
        var value = Shape(key, req.Value);
        // ExpectedVersion exactly as SetSetting: the row version from the last load, null = never saved. Kept as sent so a retry with the same key is the same request.
        return await configuration.SetSettingAsync(ctx, new SetSettingRequest(key, value, null, reason, req.ExpectedVersion), ct).ConfigureAwait(false);
    }

    /// <summary>Switches take a boolean; the window is stored as its JSON text (an object or null / "" = no window).</summary>
    private static JsonElement Shape(string key, JsonElement? raw)
    {
        var v = raw ?? JsonSerializer.SerializeToElement<string?>(null);
        if (key != WindowKey)
        {
            if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw HccException.Validation("Choose on or off.");
            return v;
        }

        switch (v.ValueKind)
        {
            case JsonValueKind.Null:
                return JsonSerializer.SerializeToElement(string.Empty);
            case JsonValueKind.String:
                return JsonSerializer.SerializeToElement((v.GetString() ?? string.Empty).Trim());
            case JsonValueKind.Object:
                return JsonSerializer.SerializeToElement(v.GetRawText());
            default:
                throw HccException.Validation("Window: {\"from\":\"HH:mm\",\"to\":\"HH:mm\",\"days\":[0-6]} (0 = Sunday), or empty.");
        }
    }
}
