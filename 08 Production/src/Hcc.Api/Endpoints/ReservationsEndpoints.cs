using Hcc.Application.Reservations;
using Hcc.Contracts.Reservations;
using Hcc.Infrastructure.SqlServer.Reservations;

namespace Hcc.Api;

/// <summary>
/// Reservation endpoints (P2): POST /commands/CreateReservation, AmendReservation, CancelReservation, MarkNoShow, ConfirmHold;
/// GET /reservations/{id}. Every command needs Idempotency-Key; versioned commands carry expectedVersion in the body.
/// </summary>
public static class ReservationsEndpoints
{
    public static IServiceCollection AddReservationsModule(this IServiceCollection services)
    {
        services.AddSingleton<IReservationStore, SqlReservationStore>();
        services.AddSingleton<IAgreedNightRateSource, SqlAgreedNightRateSource>();
        services.AddSingleton<ReservationService>();
        return services;
    }

    public static RouteGroupBuilder MapReservationsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapPost("/commands/CreateReservation", async (HttpContext http, IRequestActorResolver actors, ReservationService svc, CreateReservationRequest body, CancellationToken ct) =>
            InventoryEndpoints.Respond(http, await svc.CreateAsync(InventoryEndpoints.Context(http, actors, "CreateReservation"), body, ct)));
        api.MapPost("/commands/AmendReservation", async (HttpContext http, IRequestActorResolver actors, ReservationService svc, AmendReservationRequest body, CancellationToken ct) =>
            InventoryEndpoints.Respond(http, await svc.AmendAsync(InventoryEndpoints.Context(http, actors, "AmendReservation"), body, ct)));
        api.MapPost("/commands/CancelReservation", async (HttpContext http, IRequestActorResolver actors, ReservationService svc, CancelReservationRequest body, CancellationToken ct) =>
            InventoryEndpoints.Respond(http, await svc.CancelAsync(InventoryEndpoints.Context(http, actors, "CancelReservation"), body, ct)));
        api.MapPost("/commands/MarkNoShow", async (HttpContext http, IRequestActorResolver actors, ReservationService svc, CancelReservationRequest body, CancellationToken ct) =>
            InventoryEndpoints.Respond(http, await svc.MarkNoShowAsync(InventoryEndpoints.Context(http, actors, "MarkNoShow"), body, ct)));
        api.MapPost("/commands/ConfirmHold", async (HttpContext http, IRequestActorResolver actors, ReservationService svc, ConfirmHoldRequest body, CancellationToken ct) =>
            InventoryEndpoints.Respond(http, await svc.ConfirmHoldAsync(InventoryEndpoints.Context(http, actors, "ConfirmHold"), body, ct)));
        api.MapGet("/reservations/{id:long}", async (HttpContext http, IRequestActorResolver actors, ReservationService svc, long id, CancellationToken ct) =>
        {
            var a = actors.Resolve(http);
            return Results.Ok(await svc.GetAsync(a.PropertyId, a.PrincipalId, id, ct));
        });
        return api;
    }
}
