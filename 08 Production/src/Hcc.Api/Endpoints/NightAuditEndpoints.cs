using Hcc.Application.Closing;

namespace Hcc.Api.Endpoints;

/// <summary>F8-QW-NA: GET /api/v1/closing/night-audit?date= (PERM-CLS-001). Show only: the lines add no blocker to the close.</summary>
public static class NightAuditEndpoints
{
    public static IServiceCollection AddNightAuditModule(this IServiceCollection services)
    {
        services.AddSingleton(sp => new NightAuditService(sp.GetRequiredService<ClosingService>()));
        return services;
    }

    public static RouteGroupBuilder MapNightAuditEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/closing/night-audit", async (HttpContext http, NightAuditService s, DateOnly? date, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(http.HccCaller(), date, ct).ConfigureAwait(false)));
        return api;
    }
}
