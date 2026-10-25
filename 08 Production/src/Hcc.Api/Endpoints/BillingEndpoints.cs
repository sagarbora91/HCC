using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Payments;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Billing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Resolves the signed-in caller from the server session (contracts §1). P1 owns sessions; until it is merged the default
/// resolver denies every request (fail closed). Requested change: a shared session accessor from P1 replaces this.
/// </summary>
public interface ISessionCallerResolver
{
    Caller Resolve(HttpContext context);
}

public sealed class NoSessionCallerResolver : ISessionCallerResolver
{
    public Caller Resolve(HttpContext context) => throw new HccException(HccErrorCode.FORBIDDEN, "Sign in first (staff sessions arrive with the access module).");
}

/// <summary>Fail-closed policy used only when no real <see cref="IAccessPolicy"/> (P1) is registered.</summary>
public sealed class DenyAllAccessPolicy : IAccessPolicy
{
    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
        => throw new HccException(HccErrorCode.FORBIDDEN, $"Access denied: {permissionId} is required.");

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>Billing endpoints: PostRoomNight, PostCharge, RecordReceipt, Request/Decide/ExecuteAdjustment, GetFolio.</summary>
public static class BillingEndpoints
{
    /// <summary>DI for billing + payments (one line in Program.cs). Real access policy / stays / session resolver replace the fallbacks.</summary>
    public static IServiceCollection AddBillingModule(this IServiceCollection services)
    {
        services.TryAddSingleton<IAccessPolicy, DenyAllAccessPolicy>();
        services.TryAddSingleton<ISessionCallerResolver, NoSessionCallerResolver>();
        services.TryAddSingleton<IStayNightSource, UnwiredStayNightSource>();
        services.TryAddSingleton<ITaxRateSource>(new FixedTaxRateSource());
        services.AddSingleton<IBillingStore>(sp => new SqlBillingStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<BillingService>();
        services.AddSingleton<CashierService>();
        services.AddSingleton<ICashDrawerPort>(sp => sp.GetRequiredService<CashierService>());
        services.AddSingleton<BillingQueries>();
        return services;
    }

    public static RouteGroupBuilder MapBillingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        Command<PostRoomNightRequest, ChargeResult>(api, "PostRoomNight", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().PostRoomNightAsync(c, k, r, ct));
        Command<PostChargeRequest, ChargeResult>(api, "PostCharge", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().PostChargeAsync(c, k, r, ct));
        Command<RecordReceiptRequest, ReceiptResult>(api, "RecordReceipt", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().RecordReceiptAsync(c, k, r, ct));
        Command<RequestAdjustmentRequest, AdjustmentResult>(api, "RequestAdjustment", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().RequestAdjustmentAsync(c, k, r, ct));
        Command<DecideApprovalRequest, ApprovalResult>(api, "DecideApproval", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().DecideApprovalAsync(c, k, r, ct));
        Command<ExecuteAdjustmentRequest, ApprovalResult>(api, "ExecuteAdjustment", (sp, c, k, r, ct) => sp.GetRequiredService<BillingService>().ExecuteAdjustmentAsync(c, k, r, ct));
        api.MapGet("/folios/{folioId:long}", async (long folioId, HttpContext http, ISessionCallerResolver session, BillingQueries q, CancellationToken ct)
            => Results.Ok(await q.GetFolioAsync(session.Resolve(http), folioId, ct).ConfigureAwait(false)));
        return api;
    }

    /// <summary>POST /commands/{name}: Idempotency-Key header required; replays answer with the stored result and header <c>Idempotent-Replayed: true</c>.</summary>
    internal static void Command<TRequest, TResult>(RouteGroupBuilder api, string name,
        Func<IServiceProvider, Caller, string, TRequest, CancellationToken, Task<CommandResult<TResult>>> run)
    {
        api.MapPost("/commands/" + name, async (HttpContext http, TRequest? body, ISessionCallerResolver session, CancellationToken ct) =>
        {
            var caller = session.Resolve(http);
            var key = http.Request.Headers[ApiConventions.IdempotencyKeyHeader].ToString();
            if (string.IsNullOrWhiteSpace(key)) throw HccException.Validation($"{ApiConventions.IdempotencyKeyHeader} header is required.");
            if (body is null) throw HccException.Validation("The request body is required.");
            var result = await run(http.RequestServices, caller, key, body, ct).ConfigureAwait(false);
            if (result.Replayed) http.Response.Headers["Idempotent-Replayed"] = "true";
            return Results.Ok(result.Value);
        });
    }
}
