using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Contracts.Billing;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Billing;

namespace Hcc.Api.Endpoints;

/// <summary>Billing endpoints: PostRoomNight, PostCharge, RecordReceipt, Request/Decide/ExecuteAdjustment, GetFolio. All require a session.</summary>
public static class BillingEndpoints
{
    /// <summary>DI for billing + payments (one line in Program.cs).</summary>
    public static IServiceCollection AddBillingModule(this IServiceCollection services)
    {
        services.AddTaxModule(); // DEC-069 GST master: THE IGstRateResolver (+ per-charge snapshots); replaced the fixed 10 % ITaxRateSource
        services.AddSingleton<IBillingStore>(sp => new SqlBillingStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IStayNightSource, StayNightSource>();      // P3 stay -> folio + P2 agreed nightly rate
        services.AddSingleton<IAccessScopeResolver>(sp => new SqlShiftScopeResolver(sp.GetRequiredService<SqlPlatform>().Connections)); // "shift" (S)
        services.AddSingleton<BillingService>();
        services.AddSingleton<CashierService>();
        services.AddSingleton<ICashDrawerPort>(sp => sp.GetRequiredService<CashierService>());
        services.AddSingleton<BillingQueries>();
        return services;
    }

    public static RouteGroupBuilder MapBillingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        Command<PostRoomNightRequest, ChargeResult>(g, "PostRoomNight", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().PostRoomNightAsync(c, k, r, ct));
        Command<PostChargeRequest, ChargeResult>(g, "PostCharge", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().PostChargeAsync(c, k, r, ct));
        Command<RecordReceiptRequest, ReceiptResult>(g, "RecordReceipt", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().RecordReceiptAsync(c, k, r, ct));
        Command<RequestAdjustmentRequest, AdjustmentResult>(g, "RequestAdjustment", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().RequestAdjustmentAsync(c, k, r, ct));
        Command<DecideApprovalRequest, ApprovalResult>(g, "DecideApproval", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().DecideApprovalAsync(c, k, r, ct));
        Command<ExecuteAdjustmentRequest, ApprovalResult>(g, "ExecuteAdjustment", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().ExecuteAdjustmentAsync(c, k, r, ct));
        g.MapGet("/folios/{folioId:long}", async (long folioId, HttpContext http, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetFolioAsync(http.HccCaller(), folioId, ct).ConfigureAwait(false)));
        g.MapGet("/folios/by-reservation/{reservationId:long}", async (long reservationId, HttpContext http, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetReservationFolioAsync(http.HccCaller(), reservationId, ct).ConfigureAwait(false))); // W4-3 sync layer
        return api;
    }

    /// <summary>The session's actor as a billing <see cref="Caller"/>.</summary>
    public static Caller HccCaller(this HttpContext http)
    {
        var (property, principal) = http.HccActor();
        return new Caller(property, principal);
    }

    /// <summary>POST /commands/{name} on a session-protected group: Idempotency-Key header required; replays carry <c>Idempotent-Replayed: true</c>.</summary>
    internal static void Command<TRequest, TResult>(RouteGroupBuilder group, string name,
        Func<IServiceProvider, Caller, string, TRequest, CancellationToken, Task<CommandResult<TResult>>> run)
    {
        group.MapPost("/commands/" + name, async (HttpContext http, TRequest? body, CancellationToken ct) =>
        {
            var caller = http.HccCaller();
            var key = http.Request.IdempotencyKey();
            if (body is null) throw HccException.Validation("The request body is required.");
            return http.HccReply(await run(http.RequestServices, caller, key, body, ct).ConfigureAwait(false));
        });
    }
}
