using Hcc.Application.Import;
using Hcc.Contracts.Import;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Import;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Masters &amp; opening balances import (W4-13): <c>POST /api/v1/commands/{UploadImportBatch|CommitImportBatch}</c> (Idempotency-Key) and
/// <c>GET /api/v1/import/masters</c> (templates + setup state), <c>/import/batches[?limit=]</c>, <c>/import/batches/{id}</c> (fresh validation + preview),
/// <c>/import/opening-balances</c>, <c>/import/ledgers</c>.
/// </summary>
public static class ImportEndpoints
{
    public static IServiceCollection AddImportModule(this IServiceCollection services)
    {
        services.AddSingleton<IImportStore>(sp => new SqlImportStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IImportInventoryWriter, SqlImportInventoryWriter>();
        services.AddSingleton<IImportAccessWriter, SqlImportAccessWriter>();
        services.AddSingleton<ImportService>();
        services.AddSingleton<IOpeningBalanceSource>(sp => sp.GetRequiredService<ImportService>());
        return services;
    }

    public static RouteGroupBuilder MapImportEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + ImportService.OpUpload, async (HttpContext http, UploadImportBatchRequest body, ImportService s, CancellationToken ct) =>
            http.HccReply(await s.UploadAsync(http.HccCommand(ImportService.OpUpload), body, ct)));
        g.MapPost("/commands/" + ImportService.OpCommit, async (HttpContext http, CommitImportBatchRequest body, ImportService s, CancellationToken ct) =>
            http.HccReply(await s.CommitAsync(http.HccCommand(ImportService.OpCommit), body, ct)));
        g.MapGet("/import/masters", async (HttpContext http, ImportService s, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await s.MastersAsync(property, principal, ct));
        });
        g.MapGet("/import/batches", async (HttpContext http, ImportService s, CancellationToken ct, int limit = 50) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await s.ListAsync(property, principal, limit, ct));
        });
        g.MapGet("/import/batches/{batchId:long}", async (HttpContext http, ImportService s, long batchId, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await s.GetBatchAsync(property, principal, batchId, ct));
        });
        g.MapGet("/import/opening-balances", async (HttpContext http, ImportService s, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await s.OpeningBalancesAsync(property, principal, ct));
        });
        g.MapGet("/import/ledgers", async (HttpContext http, ImportService s, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await s.LedgersAsync(property, principal, ct));
        });
        return api;
    }
}
