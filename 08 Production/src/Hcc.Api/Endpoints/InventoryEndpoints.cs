using System.Globalization;
using System.Security.Claims;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Inventory;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hcc.Api;

/// <summary>The signed-in actor of a request (contracts §1: from the server session, never from the body).</summary>
public sealed record RequestActor(int PropertyId, int PrincipalId);

/// <summary>
/// STUB for P1: resolves the actor from the session. P1 (access) replaces the default registration with its session
/// implementation; the default reads the claims <c>hcc:property</c> and <c>hcc:principal</c> and denies otherwise.
/// </summary>
public interface IRequestActorResolver
{
    RequestActor Resolve(HttpContext http);
}

public sealed class ClaimsRequestActorResolver : IRequestActorResolver
{
    public const string PropertyClaim = "hcc:property";
    public const string PrincipalClaim = "hcc:principal";

    public RequestActor Resolve(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var user = http.User;
        if (int.TryParse(user.FindFirstValue(PropertyClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0
            && int.TryParse(user.FindFirstValue(PrincipalClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var u) && u > 0)
            return new RequestActor(p, u);
        throw new HccException(HccErrorCode.FORBIDDEN, "Sign in to continue.");
    }
}

/// <summary>Inventory endpoints (P2): GET /availability, POST /commands/QuoteStay, CreateGroupBlock, ReleaseGroupBlock.</summary>
public static class InventoryEndpoints
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddSingleton<IInventoryStore, SqlInventoryStore>();
        services.AddSingleton<IInventoryAllocator, SqlInventoryAllocator>();
        services.AddSingleton<IRoomBlockPort, SqlRoomBlockPort>();
        services.TryAddSingleton<ITaxRateSource>(new ConfiguredTaxRateSource());
        services.TryAddSingleton<IRequestActorResolver, ClaimsRequestActorResolver>();
        services.AddSingleton<InventoryService>();
        return services;
    }

    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet("/availability", async (HttpContext http, IRequestActorResolver actors, InventoryService inventory, string arrival, string departure,
            int adults, int? children, int? roomTypeId, string? ratePlanId, long? groupBlockId, CancellationToken ct) =>
        {
            var a = actors.Resolve(http);
            return Results.Ok(await inventory.GetAvailabilityAsync(a.PropertyId, a.PrincipalId,
                new AvailabilityQuery(arrival, departure, adults, children ?? 0, roomTypeId, ratePlanId, groupBlockId), ct));
        });
        api.MapPost("/commands/QuoteStay", async (HttpContext http, IRequestActorResolver actors, InventoryService inventory, QuoteStayRequest body, CancellationToken ct) =>
            Respond(http, await inventory.QuoteStayAsync(Context(http, actors, "QuoteStay", keyRequired: false), body, ct)));
        api.MapPost("/commands/CreateGroupBlock", async (HttpContext http, IRequestActorResolver actors, InventoryService inventory, CreateGroupBlockRequest body, CancellationToken ct) =>
            Respond(http, await inventory.CreateGroupBlockAsync(Context(http, actors, "CreateGroupBlock"), body, ct)));
        api.MapPost("/commands/ReleaseGroupBlock", async (HttpContext http, IRequestActorResolver actors, InventoryService inventory, ReleaseGroupBlockRequest body, CancellationToken ct) =>
            Respond(http, await inventory.ReleaseGroupBlockAsync(Context(http, actors, "ReleaseGroupBlock"), body, ct)));
        return api;
    }

    /// <summary>
    /// Command context from the session actor and the Idempotency-Key header. QuoteStay does not require a key (contracts §3);
    /// without one the server uses a fresh key, so a retry creates another quote.
    /// </summary>
    public static CommandContext Context(HttpContext http, IRequestActorResolver actors, string operation, bool keyRequired = true)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(actors);
        var actor = actors.Resolve(http);
        var key = http.Request.Headers[ApiConventions.IdempotencyKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (keyRequired) throw HccException.Validation($"{ApiConventions.IdempotencyKeyHeader} header is required.");
            key = "auto-" + Guid.NewGuid().ToString("N");
        }

        var ctx = new CommandContext(actor.PropertyId, actor.PrincipalId, operation, key);
        ctx.Validate();
        return ctx;
    }

    public static IResult Respond<T>(HttpContext http, CommandResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Replayed) http.Response.Headers["Idempotent-Replayed"] = "true";
        return Results.Ok(result.Value);
    }
}
