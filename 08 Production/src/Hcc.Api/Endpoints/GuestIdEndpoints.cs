using Hcc.Application.GuestIds;
using Hcc.Contracts.GuestIds;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.GuestIds;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Guest ID numbers (DEC-068): <c>POST /api/v1/commands/RevealGuestId</c> (ID-view permission, audited) and
/// <c>POST /api/v1/commands/RekeyGuestIds</c> (Owner). Keys: <c>Hcc:GuestIdKeys:Current</c> + <c>Hcc:GuestIdKeys:Keys:&lt;id&gt;</c>
/// (DPAPI-wrapped; see the GuestIds README). Without a configured key the module is not registered: RegisterGuest then refuses full
/// numbers (UNAVAILABLE) instead of storing them in clear, and the reveal routes answer UNAVAILABLE.
/// </summary>
public static class GuestIdEndpoints
{
    public static IServiceCollection AddGuestIdModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Hcc:GuestIdKeys");
        var current = section["Current"];
        if (string.IsNullOrWhiteSpace(current)) return services;
        var keys = section.GetSection("Keys").GetChildren().ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
        var ring = new DpapiGuestIdKeyRing(current, keys, section.GetValue<bool>("AllowPlainKeys"));
        services.AddSingleton<IGuestIdKeyRing>(ring);
        services.AddSingleton<GuestIdProtector>();
        services.AddSingleton<IGuestIdStore>(sp => new SqlGuestIdStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<GuestIdService>();
        return services;
    }

    public static RouteGroupBuilder MapGuestIdEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + GuestIdService.OpReveal, async (HttpContext http, RevealGuestIdRequest body, IServiceProvider sp, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return http.HccReply(await Service(sp).RevealAsync(http.HccCommand(GuestIdService.OpReveal), body, ct));
        });
        g.MapPost("/commands/" + GuestIdService.OpRekey, async (HttpContext http, RekeyGuestIdsRequest body, IServiceProvider sp, CancellationToken ct) =>
            http.HccReply(await Service(sp).RekeyAsync(http.HccCommand(GuestIdService.OpRekey), body, ct)));
        return api;
    }

    private static GuestIdService Service(IServiceProvider sp) => sp.GetService<GuestIdService>()
        ?? throw Hcc.Domain.Errors.HccException.NotConfigured("guest-id", "Guest ID encryption is not configured on this server.");
}
