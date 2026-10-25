using System.Globalization;
using System.Security.Claims;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Stays;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Stays endpoints (contracts §2–§3): <c>POST /api/v1/commands/{RegisterGuest|CheckIn|CheckoutStay}</c> with an
/// <c>Idempotency-Key</c> header, and <c>GET /api/v1/guests/{id}</c> (field-masked). The actor comes from the session
/// (P1): claims <see cref="ClaimTypes.NameIdentifier"/> = principal id and <see cref="P3Session.PropertyClaim"/> = property id.
/// </summary>
public static class StaysEndpoints
{
    public static IServiceCollection AddHccStays(this IServiceCollection services)
    {
        services.TryAddSingleton<IAccessPolicy, DenyAllAccessPolicy>(); // fail closed until P1 registers the real policy
        services.AddSingleton<IStaysStore, SqlStaysStore>();
        services.AddSingleton<IStayFolioPort, Sql0002StayFolioPort>(); // P4 replaces with its folio service
        services.AddSingleton<IStaysReadStore>(sp => new SqlStaysReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<StaysCommands>();
        services.AddSingleton<StaysQueries>();
        return services;
    }

    public static RouteGroupBuilder MapStaysEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapPost("/commands/" + StaysCommands.OpRegisterGuest, async (HttpContext http, RegisterGuestRequest body, StaysCommands cmds, CancellationToken ct) =>
            P3Session.Reply(http, await cmds.RegisterGuestAsync(P3Session.Context(http, StaysCommands.OpRegisterGuest), body, ct)));
        api.MapPost("/commands/" + StaysCommands.OpCheckIn, async (HttpContext http, CheckInRequest body, StaysCommands cmds, CancellationToken ct) =>
            P3Session.Reply(http, await cmds.CheckInAsync(P3Session.Context(http, StaysCommands.OpCheckIn), body, ct)));
        api.MapPost("/commands/" + StaysCommands.OpCheckoutStay, async (HttpContext http, CheckoutStayRequest body, StaysCommands cmds, CancellationToken ct) =>
            P3Session.Reply(http, await cmds.CheckoutStayAsync(P3Session.Context(http, StaysCommands.OpCheckoutStay), body, ct)));
        api.MapGet("/guests/{guestId:long}", async (HttpContext http, long guestId, StaysQueries q, CancellationToken ct) =>
        {
            var (property, principal) = P3Session.Actor(http);
            return Results.Ok(await q.GetGuestAsync(property, principal, guestId, ct));
        });
        return api;
    }
}

/// <summary>Session → actor for the P3 endpoints, until P1's session middleware provides a shared accessor.</summary>
public static class P3Session
{
    public const string PropertyClaim = "hcc:property";
    public const string ReplayedHeader = "Idempotency-Replayed";

    public static (int PropertyId, int PrincipalId) Actor(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var user = http.User;
        if (int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var principal)
            && int.TryParse(user.FindFirstValue(PropertyClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var property))
            return (property, principal);
        throw new HccException(HccErrorCode.FORBIDDEN, "Sign in to continue.");
    }

    public static CommandContext Context(HttpContext http, string operation)
    {
        var (property, principal) = Actor(http);
        var key = http.Request.Headers[ApiConventions.IdempotencyKeyHeader].ToString();
        var ctx = new CommandContext(property, principal, operation, key);
        ctx.Validate();
        return ctx;
    }

    public static IResult Reply<T>(HttpContext http, CommandResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Replayed) http.Response.Headers[ReplayedHeader] = "true";
        return Results.Ok(result.Value);
    }
}

/// <summary>Fail-closed placeholder: registered only when no real <see cref="IAccessPolicy"/> exists (P1 provides it).</summary>
public sealed class DenyAllAccessPolicy : IAccessPolicy
{
    public Task RequireAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, long? amountPaise = null, CancellationToken ct = default)
        => throw new HccException(HccErrorCode.FORBIDDEN, "Access is not configured on this server.");

    public Task<bool> HasAsync(int propertyId, int principalId, string permissionId, AccessScope? scope = null, CancellationToken ct = default) => Task.FromResult(false);
}
