using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Documents;
using Hcc.Application.GuestIds;
using Hcc.Application.Legacy;
using Hcc.Contracts;
using Hcc.Contracts.Legacy;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Legacy;

using Hcc.Domain.Configuration;

namespace Hcc.Api.Endpoints;

/// <summary>
/// W1a LG legacy importer (LG-HISTORY-CONTRACT §5, §10): <c>POST /api/v1/commands/legacy.import</c> (Owner, PERM-LGC-001; the idempotency key is
/// computed by the server from folder + parts + dry run + totals.json, a header key is not needed) and <c>GET /api/v1/legacy/runs[?limit=]</c>.
/// Needs the guest-ID key (ID numbers and images are sealed): without it the module is not registered and the routes answer UNAVAILABLE.
/// </summary>
public static class LegacyEndpoints
{
    public static IServiceCollection AddLegacyModule(this IServiceCollection services)
    {
        services.AddSingleton<ILegacyStore>(sp => new SqlLegacyStore(sp.GetRequiredService<SqlPlatform>().Connections));
        if (!services.Any(d => d.ServiceType == typeof(GuestIdProtector))) return services;
        services.AddSingleton(sp => new LegacyImportService(sp.GetRequiredService<SqlPlatform>().Commands, sp.GetRequiredService<IAccessPolicy>(), sp.GetRequiredService<ILegacyStore>(),
            sp.GetRequiredService<IGuestIdStore>(), sp.GetRequiredService<GuestIdProtector>(), sp.GetRequiredService<IDocumentStore>(), sp.GetRequiredService<IDocumentImageStore>(),
            sp.GetRequiredService<IOpeningEntryStore>(), sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    private static LegacyImportService Service(HttpContext http) =>
        http.RequestServices.GetService<LegacyImportService>()
        ?? throw new HccException(HccErrorCode.UNAVAILABLE, "The legacy importer needs the guest-ID key on this server (Hcc:GuestIdKeys).");

    public static RouteGroupBuilder MapLegacyEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var cmd = g.MapGroup(string.Empty).RequireHccFeature(FeatureIds.LegacyImport); // ADM-1 (DEC-214): refused while the sub-function is off; reads stay on g
        cmd.MapPost("/commands/" + LegacyImportService.OpImport, async (HttpContext http, LegacyImportRequest body, CancellationToken ct) =>
            http.HccReply(await Service(http).ImportAsync(http.HccCommand(LegacyImportService.OpImport, keyRequired: false), body, ct)));
        g.MapGet("/legacy/runs", async (HttpContext http, CancellationToken ct, int limit = 50) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await Service(http).RunsAsync(property, principal, limit, ct));
        });
        return api;
    }
}
