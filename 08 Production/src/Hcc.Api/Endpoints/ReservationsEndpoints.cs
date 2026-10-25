using Hcc.Application.Billing;
using Hcc.Application.Reservations;
using Hcc.Contracts.Reservations;
using Hcc.Infrastructure.SqlServer.Reservations;

namespace Hcc.Api;

/// <summary>
/// Reservation endpoints (P2): POST /commands/CreateReservation, AmendReservation, CancelReservation, MarkNoShow, ConfirmHold;
/// GET /reservations/{id}. Every route requires a session; every command needs Idempotency-Key; versioned commands carry
/// expectedVersion in the body.
/// </summary>
public static class ReservationsEndpoints
{
    public static IServiceCollection AddReservationsModule(this IServiceCollection services)
    {
        services.AddSingleton<IReservationStore, SqlReservationStore>();
        services.AddSingleton<IAgreedNightRateSource, SqlAgreedNightRateSource>();
        services.AddSingleton<IReservationMoneyCheck, BillingReservationMoney>(); // billing owns folio figures
        services.AddSingleton<ReservationService>();
        return services;
    }

    public static RouteGroupBuilder MapReservationsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/CreateReservation", async (HttpContext http, ReservationService svc, CreateReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.CreateAsync(http.HccCommand("CreateReservation"), body, ct)));
        g.MapPost("/commands/AmendReservation", async (HttpContext http, ReservationService svc, AmendReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.AmendAsync(http.HccCommand("AmendReservation"), body, ct)));
        g.MapPost("/commands/CancelReservation", async (HttpContext http, ReservationService svc, CancelReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.CancelAsync(http.HccCommand("CancelReservation"), body, ct)));
        g.MapPost("/commands/MarkNoShow", async (HttpContext http, ReservationService svc, CancelReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.MarkNoShowAsync(http.HccCommand("MarkNoShow"), body, ct)));
        g.MapPost("/commands/ConfirmHold", async (HttpContext http, ReservationService svc, ConfirmHoldRequest body, CancellationToken ct) =>
            http.HccReply(await svc.ConfirmHoldAsync(http.HccCommand("ConfirmHold"), body, ct)));
        g.MapGet("/reservations/{id:long}", async (HttpContext http, ReservationService svc, long id, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await svc.GetAsync(property, principal, id, ct));
        });
        return api;
    }
}
