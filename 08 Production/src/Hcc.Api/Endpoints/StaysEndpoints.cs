using Hcc.Application.Stays;
using Hcc.Contracts.Stays;
using Hcc.Infrastructure.SqlServer;
using Hcc.Application.Billing;
using Hcc.Infrastructure.SqlServer.Stays;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Stays endpoints (contracts §2–§3): <c>POST /api/v1/commands/{RegisterGuest|CheckIn|CheckoutStay}</c> with an
/// <c>Idempotency-Key</c> header, and <c>GET /api/v1/guests/{id}</c> (field-masked). Every route requires a session (P1);
/// the actor comes from the session, never from the body.
/// </summary>
public static class StaysEndpoints
{
    public static IServiceCollection AddHccStays(this IServiceCollection services)
    {
        services.AddSingleton<IStaysStore, SqlStaysStore>();
        services.AddSingleton<IStayFolioPort, BillingStayFolioPort>(); // P4 folio rules (checkout settlement, company credit)
        services.AddSingleton<IStaysReadStore>(sp => new SqlStaysReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<StaysCommands>();
        services.AddSingleton<StaysQueries>();
        return services;
    }

    public static RouteGroupBuilder MapStaysEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + StaysCommands.OpRegisterGuest, async (HttpContext http, RegisterGuestRequest body, StaysCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.RegisterGuestAsync(http.HccCommand(StaysCommands.OpRegisterGuest), body, ct)));
        g.MapPost("/commands/" + StaysCommands.OpCheckIn, async (HttpContext http, CheckInRequest body, StaysCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.CheckInAsync(http.HccCommand(StaysCommands.OpCheckIn), body, ct)));
        g.MapPost("/commands/" + StaysCommands.OpCheckoutStay, async (HttpContext http, CheckoutStayRequest body, StaysCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.CheckoutStayAsync(http.HccCommand(StaysCommands.OpCheckoutStay), body, ct)));
        g.MapGet("/guests/{guestId:long}", async (HttpContext http, long guestId, StaysQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetGuestAsync(property, principal, guestId, ct));
        });
        return api;
    }
}
