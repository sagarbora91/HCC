// F8-QW-NS · GET /reservations/no-show-due (read only; marking uses POST /commands/MarkNoShow).
using Hcc.Api.Endpoints;
using Hcc.Application.Reservations;

namespace Hcc.Api;

public static class NoShowEndpoints
{
    public static IServiceCollection AddNoShowModule(this IServiceCollection services)
    {
        services.AddSingleton<NoShowQueries>();
        return services;
    }

    public static RouteGroupBuilder MapNoShowEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/reservations/no-show-due", async (HttpContext http, NoShowQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.DueAsync(property, principal, ct));
        });
        return api;
    }
}
