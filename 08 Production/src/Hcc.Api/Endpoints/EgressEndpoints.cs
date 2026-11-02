using System.Diagnostics;
using Hcc.Application.Access;
using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.Egress;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Egress;

namespace Hcc.Api.Endpoints;

/// <summary>PERM ids of the egress gate (migration 4733).</summary>
public static class EgressPermissions
{
    /// <summary>Change the Internet switches (Owner); enforced by Master configuration (group "internet" is Owner-only).</summary>
    public const string Change = "PERM-NET-001";

    /// <summary>Read the Egress block (Owner, Manager).</summary>
    public const string Read = "PERM-NET-002";
}

/// <summary>
/// F5-E1 egress gate wiring (EGRESS-CONTRACT §1, §1.1, §1.2, §5): the <c>net.*</c> settings (Master configuration › Internet, Owner only,
/// default OFF), the settings source of the policy, the SQL store, the replay worker and <c>GET /api/v1/admin/system-health/egress</c> (PERM-NET-002).
/// </summary>
public static class EgressEndpoints
{
    private static SettingDefinition Switch(string key, string label, string help) => new(key, label, SettingType.Boolean)
    {
        Default = ConfigValue.Of(false), Unit = "on / off", Help = help, Approver = SettingApprover.Owner, Money = true,
        Affects = "Outbound internet calls of this feature, immediately (≤ 5 s). OFF = queued work is held, not lost.",
    };

    /// <summary>The Internet group (EGRESS-CONTRACT §1.1). Owner only (money-flagged group), reason required (ConfigurationService).</summary>
    public static ISettingsProvider Provider { get; } = new SettingsProvider("egress", ConfigGroupIds.Internet,
    [
        Switch(EgressKeys.Master, "Internet (master switch)", "OFF = HCC makes no internet call at all, whatever the switches below say. Default OFF at go-live (A28)."),
        Switch(EgressKeys.Of(EgressIntegration.Messaging), "Guest messaging (WhatsApp / SMS)", "Lets messages leave for the gateway. OFF = messages queue as held."),
        Switch(EgressKeys.Of(EgressIntegration.Ota), "OTA channel manager", "Lets OTA bookings come in and rates / inventory go out."),
        Switch(EgressKeys.Of(EgressIntegration.Ocr), "ID scan (OCR)", "Lets ID photos be read by the OCR service. OFF = enter the ID by hand."),
        Switch(EgressKeys.Of(EgressIntegration.Payments), "Payment links", "Lets deposit payment links be created. OFF = record the deposit by hand (DEC-102)."),
        Switch(EgressKeys.Of(EgressIntegration.Email), "E-mail", "Lets e-mails and scheduled reports be sent."),
        Switch(EgressKeys.Of(EgressIntegration.UpdateCheck), "Update check", "Lets HCC ask for a newer version."),
        new(EgressKeys.Window, "Allowed time window", SettingType.Text)
        {
            Default = ConfigValue.Of(string.Empty), AllowEmpty = true, MaxLength = 120, Approver = SettingApprover.Owner, Money = true,
            Unit = "{\"from\":\"HH:mm\",\"to\":\"HH:mm\",\"days\":[0-6]} or empty",
            Help = "Hotel local time; from inclusive, to exclusive; may cross midnight; 0 = Sunday. Empty = no window (the switches alone decide). Outside the window the internet is OFF.",
            Validate = v => v.Kind == ConfigValueKind.Text ? EgressRules.WindowProblem(v.Text) : null,
            Affects = "When outbound internet calls are allowed.",
        },
    ]);

    public static IServiceCollection AddEgressModule(this IServiceCollection services)
    {
        services.AddSingleton(Provider);
        services.AddSingleton(sp => new SqlEgressStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IEgressOutboxStore>(sp => sp.GetRequiredService<SqlEgressStore>());
        services.AddSingleton<IEgressAudit>(sp => sp.GetRequiredService<SqlEgressStore>());
        services.AddSingleton<IEgressHealthStore>(sp => sp.GetRequiredService<SqlEgressStore>());
        services.AddSingleton<IEgressSettingsSource, ConfigEgressSettingsSource>();
        services.AddHccEgress();
        services.AddSingleton<EgressOutbox>();
        services.AddSingleton<EgressHealth>();
        services.AddHostedService<EgressOutboxWorker>();
        return services;
    }

    public static RouteGroupBuilder MapEgressEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/admin/system-health/egress", async (HttpContext http, EgressHealth health, IAccessPolicy policy, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var (property, principal) = http.HccActor();
            await policy.RequireAsync(property, principal, EgressPermissions.Read, null, null, ct);
            var owner = await policy.HasAsync(property, principal, EgressPermissions.Change, null, ct);
            return Results.Ok(await health.ReadAsync(owner, ct));
        });
        return api;
    }
}

/// <summary>
/// Reads <c>net.*</c> from Master configuration for the (single) property. Anything that is not a stored boolean is null (= unreadable, OFF).
/// Last change = the newest active change of any <c>net.*</c> key.
/// </summary>
public sealed class ConfigEgressSettingsSource(ISettingsReader settings, IConfigurationReadStore reads, SqlPlatform platform) : IEgressSettingsSource
{
    private int? _property;

    public TimeZoneInfo TimeZone { get; } = Kolkata();

    private static TimeZoneInfo Kolkata()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "India Standard Time");
        }
    }

    private async Task<int> PropertyAsync(CancellationToken ct)
    {
        if (_property is { } p) return p;
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = platform.Connections.Command("SELECT TOP (1) property_id FROM inv.property ORDER BY property_id;", conn);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("No property yet.");
        _property = (int)v;
        return _property.Value;
    }

    public async Task<EgressSettings> ReadAsync(CancellationToken ct = default)
    {
        var property = await PropertyAsync(ct).ConfigureAwait(false);
        static bool? Flag(ConfigValue v) => v.Kind == ConfigValueKind.Boolean ? v.Flag : null;
        var master = Flag(await settings.GetAsync(property, EgressKeys.Master, null, ct).ConfigureAwait(false));
        var switches = new Dictionary<EgressIntegration, bool?>();
        foreach (var i in EgressKeys.All) switches[i] = Flag(await settings.GetAsync(property, EgressKeys.Of(i), null, ct).ConfigureAwait(false));
        var w = await settings.GetAsync(property, EgressKeys.Window, null, ct).ConfigureAwait(false);
        string? window = w.Kind switch { ConfigValueKind.Text => w.Text, ConfigValueKind.Null => null, _ => "{invalid" };
        var changes = (await reads.ActiveChangesAsync(property, null, ct).ConfigureAwait(false))
            .Where(c => c.Key.StartsWith("net.", StringComparison.Ordinal)).OrderByDescending(c => c.ChangedUtc).FirstOrDefault();
        return new EgressSettings(master, switches, window,
            changes is null ? null : new DateTimeOffset(DateTime.SpecifyKind(changes.ChangedUtc, DateTimeKind.Utc)), changes?.ChangedByName, changes?.Reason);
    }
}

/// <summary>
/// EGRESS-CONTRACT §1.2 replay hook on the existing outbox worker base (<see cref="OutboxPollingWorker"/>, every 2 s): ON → held items back to
/// pending in order; OFF → pending items held. Also records the firewall state the API wants and, on Windows, starts the on-demand task
/// <c>\HCC\ToggleEgress</c> when it changed (task missing / failing = System health "firewall rule not applied"; the software gate still holds).
/// </summary>
public sealed class EgressOutboxWorker(EgressOutbox outbox, IEgressPolicy policy, IEgressHealthStore health, TimeProvider clock, ILogger<EgressOutboxWorker> logger)
    : OutboxPollingWorker(logger, "Internet egress hold / replay")
{
    protected override async Task PollAsync(CancellationToken ct)
    {
        await outbox.TickAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var wanted = policy.Current(now).MasterOn ? "On" : "Off";
        if (await health.SetWantedFirewallAsync(wanted, now, ct).ConfigureAwait(false) && OperatingSystem.IsWindows()) StartToggleTask();
    }

    private static void StartToggleTask()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/Run /TN \"\\HCC\\ToggleEgress\"") { UseShellExecute = false, CreateNoWindow = true });
        }
#pragma warning disable CA1031 // task missing: System health shows "firewall rule not applied"; the software gate holds.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
