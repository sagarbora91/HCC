using Hcc.Api.Endpoints;
using Hcc.Domain.Configuration;
using Hcc.Application.Billing;
using Hcc.Application.Reservations;
using Hcc.Contracts.Reservations;
using Hcc.Infrastructure.SqlServer.Reservations;

namespace Hcc.Api;

/// <summary>
/// Reservation endpoints (P2): POST /commands/CreateReservation, AmendReservation, SetReservationCompany (DEC-076), CancelReservation, MarkNoShow, ConfirmHold;
/// GET /reservations/{id}; GET /reservations?view&from&to&status&q&page&pageSize (W4-1 front-desk list). Every route requires a session; every command needs Idempotency-Key; versioned commands carry
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
        var cmd = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.Reservations); // ADM-1: CreateReservation stays on g (walk-in for today is exempt inside ReservationService)
        g.MapPost("/commands/CreateReservation", async (HttpContext http, ReservationService svc, CreateReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.CreateAsync(http.HccCommand("CreateReservation"), body, ct)));
        cmd.MapPost("/commands/AmendReservation", async (HttpContext http, ReservationService svc, AmendReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.AmendAsync(http.HccCommand("AmendReservation"), body, ct)));
        cmd.MapPost("/commands/SetReservationCompany", async (HttpContext http, ReservationService svc, SetReservationCompanyRequest body, CancellationToken ct) =>
            http.HccReply(await svc.SetCompanyAsync(http.HccCommand("SetReservationCompany"), body, ct)));
        cmd.MapPost("/commands/CancelReservation", async (HttpContext http, ReservationService svc, CancelReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.CancelAsync(http.HccCommand("CancelReservation"), body, ct)));
        cmd.MapPost("/commands/MarkNoShow", async (HttpContext http, ReservationService svc, CancelReservationRequest body, CancellationToken ct) =>
            http.HccReply(await svc.MarkNoShowAsync(http.HccCommand("MarkNoShow"), body, ct)));
        cmd.MapPost("/commands/ConfirmHold", async (HttpContext http, ReservationService svc, ConfirmHoldRequest body, CancellationToken ct) =>
            http.HccReply(await svc.ConfirmHoldAsync(http.HccCommand("ConfirmHold"), body, ct)));
        g.MapGet("/reservations", async (HttpContext http, ReservationService svc, CancellationToken ct, string? view = null, string? from = null, string? to = null,
            string? status = null, string? q = null, int page = 1, int pageSize = 200) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await svc.ListAsync(property, principal, view, from, to, status, q, page, pageSize, ct));
        });
        g.MapGet("/reservations/{id:long}", async (HttpContext http, ReservationService svc, long id, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await svc.GetAsync(property, principal, id, ct));
        });
        return api;
    }
}
