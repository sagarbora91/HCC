using System.Net;
using Hcc.Application.Access;
using Hcc.Application.Configuration;
using Hcc.Application.Setup;
using Hcc.Contracts;
using Hcc.Contracts.Setup;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Setup;

namespace Hcc.Api.Endpoints;

/// <summary>
/// W3-b first-time hotel setup. No session (nobody exists yet), so both routes are refused unless the request comes from the
/// server PC itself (loopback — in production the plain-HTTP listener on 127.0.0.1:5080); the command is also refused once a
/// property exists. Replaces the manual <c>INSERT inv.property</c> + <c>/auth/bootstrap-owner</c> steps of the install guide.
/// </summary>
public static class SetupEndpoints
{
    public static IServiceCollection AddSetupModule(this IServiceCollection services)
    {
        services.AddSingleton<IPropertySetup>(sp => new SqlPropertySetup(sp.GetRequiredService<SqlPlatform>().Connections,
            sp.GetRequiredService<AccessModule>().Hasher, sp.GetRequiredService<AccessOptions>(), sp.GetRequiredService<ConfigurationService>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>True only for a loopback address (IPv4, IPv6 or IPv4-mapped IPv6). A LAN or unknown address is never the server PC.</summary>
    public static bool IsServerPc(IPAddress? remote)
    {
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        return IPAddress.IsLoopback(remote);
    }

    private static void RequireServerPc(HttpContext http)
    {
        if (!IsServerPc(http.Connection.RemoteIpAddress))
            throw new HccException(HccErrorCode.FORBIDDEN, "First-time setup is done on the server PC itself (http://127.0.0.1:5080).");
    }

    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var setup = api.MapGroup("/setup");
        setup.MapGet("/status", async (HttpContext http, IPropertySetup svc, CancellationToken ct) =>
        {
            RequireServerPc(http);
            return Results.Ok(await svc.StatusAsync(ct).ConfigureAwait(false));
        });
        setup.MapPost("/SetupProperty", async (SetupPropertyRequest body, HttpContext http, IPropertySetup svc, CancellationToken ct) =>
        {
            RequireServerPc(http);
            var key = http.Request.Headers[ApiConventions.IdempotencyKeyHeader].ToString();
            return Results.Ok(await svc.SetupAsync(key, body, ct).ConfigureAwait(false));
        });
        return api;
    }
}
