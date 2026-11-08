using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Billing;

namespace Hcc.Api.Endpoints;

/// <summary>
/// F8 split folio endpoints (DEC-152): POST /api/v1/commands/{OpenFolio|SetFolioRouting|RemoveFolio} (billing, PERM-FOL-002) over
/// <see cref="SplitFolioService"/>; GET /api/v1/folios/by-reservation/{reservationId}/summary (PERM-FOL-001) = one <see cref="FolioSummaryDto"/> per folio.
/// (The contract's GET /folios/{reservationId} collides with the existing GET /folios/{folioId}, hence the longer path.)
/// </summary>
public static class SplitFolioEndpoints
{
    public static IServiceCollection AddSplitFolioModule(this IServiceCollection services)
    {
        services.AddSingleton(sp => new SqlSplitFolioStore(sp.GetRequiredService<SqlPlatform>().Connections, sp.GetRequiredService<IBillingStore>(),
            sp.GetRequiredService<Application.Groups.ICompanyCreditGuard>()));
        services.AddSingleton<ISplitFolioStore>(sp => sp.GetRequiredService<SqlSplitFolioStore>());
        services.AddSingleton<ISplitFolioReader>(sp => sp.GetRequiredService<SqlSplitFolioStore>());
        services.AddSingleton<SplitFolioService>();
        return services;
    }

    public static RouteGroupBuilder MapSplitFolioEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + SplitFolioService.OpOpen, async (HttpContext http, OpenFolioRequest body, SplitFolioService s, CancellationToken ct)
            => http.HccReply(await s.OpenAsync(http.HccCommand(SplitFolioService.OpOpen), body, ct).ConfigureAwait(false)));
        g.MapPost("/commands/" + SplitFolioService.OpRoute, async (HttpContext http, SetFolioRoutingRequest body, SplitFolioService s, CancellationToken ct)
            => http.HccReply(await s.SetRoutingAsync(http.HccCommand(SplitFolioService.OpRoute), body, ct).ConfigureAwait(false)));
        g.MapPost("/commands/" + SplitFolioService.OpRemove, async (HttpContext http, RemoveFolioRequest body, SplitFolioService s, CancellationToken ct)
            => http.HccReply(await s.RemoveAsync(http.HccCommand(SplitFolioService.OpRemove), body, ct).ConfigureAwait(false)));
        g.MapGet("/folios/by-reservation/{reservationId:long}/summary", async (long reservationId, HttpContext http, IAccessPolicy policy, ISplitFolioReader reader, CancellationToken ct) =>
        {
            var c = http.HccCaller();
            await policy.RequireAsync(c.PropertyId, c.PrincipalId, BillingPermissions.FolioView, ct: ct).ConfigureAwait(false);
            var rows = await reader.SummaryAsync(c.PropertyId, reservationId, ct).ConfigureAwait(false)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "Reservation not found.");
            return Results.Ok(rows);
        });
        return api;
    }
}
