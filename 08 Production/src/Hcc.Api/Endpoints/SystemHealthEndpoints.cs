using Hcc.Api.Hosting;
using Hcc.Application.Health;
using Hcc.Contracts.Health;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Health;

namespace Hcc.Api.Endpoints;

/// <summary>
/// System health (Wave C3, audit A09-05 + fix wave A "Left open"): <c>GET /api/v1/admin/system-health</c> and
/// <c>POST /api/v1/commands/RetryParkedEvent</c>, both PERM-SYS-001 (Owner, Tech admin).
/// </summary>
public static class SystemHealthEndpoints
{
    public static IServiceCollection AddSystemHealthModule(this IServiceCollection services)
    {
        services.AddSingleton<ISystemHealthStore>(sp => new SqlSystemHealthStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IOperatorAlertStatus, EventLogAlertStatus>();
        services.AddSingleton<SystemHealthService>();
        return services;
    }

    public static RouteGroupBuilder MapSystemHealthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/admin/system-health", async (HttpContext http, SystemHealthService s, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var (property, principal) = http.HccActor();
            return Results.Ok(await s.HealthAsync(property, principal, ct));
        });
        g.MapPost("/commands/" + SystemHealthService.OpRetryParked, async (HttpContext http, RetryParkedEventRequest body, SystemHealthService s, CancellationToken ct) =>
            http.HccReply(await s.RetryParkedAsync(http.HccCommand(SystemHealthService.OpRetryParked), body, ct)));
        return api;
    }
}

/// <summary>"on" when the Event Log source HCC is registered on this PC, "off" when not, "unavailable" off Windows.</summary>
public sealed class EventLogAlertStatus : IOperatorAlertStatus
{
    public string EventLog() => !OperatingSystem.IsWindows() ? "unavailable" : WindowsEventLogAlertSink.SourceRegistered() ? "on" : "off";
}
