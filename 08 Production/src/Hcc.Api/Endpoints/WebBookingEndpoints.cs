using Hcc.Application.Configuration;
using Hcc.Application.WebBooking;
using Hcc.Contracts;
using Hcc.Contracts.WebBooking;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.WebBooking;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Booking website (W3-2). PUBLIC, anonymous and rate-limited per client address (off until <c>Hcc:WebBooking:Enabled</c> is true):
/// <c>GET /api/v1/public/booking/quote</c>, <c>POST /api/v1/public/booking/requests</c> (Idempotency-Key), <c>GET /api/v1/public/booking/deposit/{token}</c>.
/// Staff (session): <c>GET /web-booking/requests[/{id}]</c> and <c>POST /commands/{ConfirmWebRequest|DeclineWebRequest|CreateWebDepositLink|MarkWebDepositPaid}</c>.
/// Master configuration › Modules on/off (DEC-057, A11-01): while "Direct booking site" is off the staff commands are refused (MODULE_OFF)
/// and the public routes answer as if the site were disabled ("Online booking is not available."); the staff queue stays readable.
/// </summary>
public static class WebBookingEndpoints
{
    private const string NotAvailable = "Online booking is not available.";

    public static IServiceCollection AddWebBookingModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var options = new WebBookingOptions();
        configuration?.GetSection("Hcc:WebBooking").Bind(options);
        services.AddSingleton(options);
        services.AddSingleton<IWebBookingStore, SqlWebBookingStore>();
        services.AddSingleton<PublicRateLimiter>();
        services.AddSingleton<WebBookingService>();
        return services;
    }

    private static IResult? Gate(HttpContext http, WebBookingOptions options, PublicRateLimiter limiter, string bucket, int limit, TimeSpan window)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!options.Enabled) return Results.NotFound(new ErrorResponse(nameof(HccErrorCode.NOT_FOUND), NotAvailable));
        if (limiter.TryAcquire(bucket, http.Connection.RemoteIpAddress, limit, window, out var retry)) return null;
        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Json(new ErrorResponse(nameof(HccErrorCode.UNAVAILABLE), "Too many requests. Please wait a moment and try again."), statusCode: StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// <see cref="Gate"/> (switch, rate limit), then Master configuration: while the public hotel's "Direct booking site" module is off
    /// (DEC-057, A11-01) the public routes answer exactly as when the site is disabled. Null = go on.
    /// </summary>
    private static async Task<IResult?> PublicGateAsync(HttpContext http, WebBookingOptions options, PublicRateLimiter limiter, string bucket, int limit, TimeSpan window)
    {
        if (Gate(http, options, limiter, bucket, limit, window) is { } refused) return refused;
        var site = await http.RequestServices.GetRequiredService<IWebBookingStore>().PublicPropertyAsync(http.RequestAborted).ConfigureAwait(false);
        if (site is { } s && !await http.RequestServices.GetRequiredService<IModuleGate>().IsOnAsync(s.PropertyId, ConfigModuleIds.Website, http.RequestAborted).ConfigureAwait(false))
            return Results.NotFound(new ErrorResponse(nameof(HccErrorCode.NOT_FOUND), NotAvailable));
        return null;
    }

    public static RouteGroupBuilder MapWebBookingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var pub = api.MapGroup("/public/booking");
        pub.MapGet("/quote", async (HttpContext http, WebBookingService svc, WebBookingOptions options, PublicRateLimiter limiter, CancellationToken ct,
            string arrival, string departure, int adults, int? children, string? roomType) =>
            await PublicGateAsync(http, options, limiter, "quote", options.QuotesPerMinute, TimeSpan.FromMinutes(1))
            ?? Results.Ok(await svc.QuoteAsync(arrival, departure, adults, children ?? 0, roomType, ct)));
        pub.MapPost("/requests", async (HttpContext http, SubmitWebRequest body, WebBookingService svc, WebBookingOptions options, PublicRateLimiter limiter, CancellationToken ct) =>
        {
            if (await PublicGateAsync(http, options, limiter, "request", options.RequestsPerTenMinutes, TimeSpan.FromMinutes(10)) is { } refused) return refused;
            var result = await svc.SubmitAsync(http.Request.IdempotencyKey(), body, ct);
            return http.HccReply(result);
        });
        pub.MapGet("/deposit/{token}", async (HttpContext http, string token, WebBookingService svc, WebBookingOptions options, PublicRateLimiter limiter, CancellationToken ct) =>
            await PublicGateAsync(http, options, limiter, "deposit", options.QuotesPerMinute, TimeSpan.FromMinutes(1)) ?? Results.Ok(await svc.DepositViewAsync(token, ct)));

        var g = api.MapHccSecured();
        var cmd = g.MapGroup(string.Empty).RequireHccModule(ConfigModuleIds.Website); // DEC-057 / A11-01: refused while the booking site is off; reads stay
        cmd.MapPost("/commands/ConfirmWebRequest", async (HttpContext http, ConfirmWebRequestRequest body, WebBookingService svc, CancellationToken ct) =>
            http.HccReply(await svc.ConfirmAsync(http.HccCommand("ConfirmWebRequest"), body, ct)));
        cmd.MapPost("/commands/DeclineWebRequest", async (HttpContext http, DeclineWebRequestRequest body, WebBookingService svc, CancellationToken ct) =>
            http.HccReply(await svc.DeclineAsync(http.HccCommand("DeclineWebRequest"), body, ct)));
        cmd.MapPost("/commands/CreateWebDepositLink", async (HttpContext http, CreateWebDepositLinkRequest body, WebBookingService svc, CancellationToken ct) =>
            http.HccReply(await svc.CreateDepositLinkAsync(http.HccCommand("CreateWebDepositLink"), body, ct)));
        cmd.MapPost("/commands/MarkWebDepositPaid", async (HttpContext http, MarkWebDepositPaidRequest body, WebBookingService svc, CancellationToken ct) =>
            http.HccReply(await svc.MarkDepositPaidAsync(http.HccCommand("MarkWebDepositPaid"), body, ct)));
        g.MapGet("/web-booking/requests", async (HttpContext http, WebBookingService svc, CancellationToken ct, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await svc.ListAsync(property, principal, status, ct));
        });
        g.MapGet("/web-booking/requests/{id:long}", async (HttpContext http, long id, WebBookingService svc, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await svc.GetAsync(property, principal, id, ct));
        });
        return api;
    }
}
