using Hcc.Application.Masters;
using Hcc.Contracts.Masters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Masters;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Masters endpoints (MA-0): <c>POST /api/v1/commands/{SaveFloor|RetireFloor|ReactivateFloor|ReorderFloors|DeleteFloor}</c> and
/// <c>GET /api/v1/masters/floors</c>, <c>GET /api/v1/masters/floors/history</c>, <c>GET /api/v1/masters/floors/{id}/history</c>.
/// </summary>
public static class MastersEndpoints
{
    public static IServiceCollection AddMastersModule(this IServiceCollection services)
    {
        services.AddSingleton<IMastersStore, SqlMastersStore>();
        services.AddSingleton<IMastersReadStore>(sp => new SqlMastersReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IMasterUsage, SqlFloorUsage>();   // rooms per floor (inv.room.floor_id, MA-1; hundreds digit for rooms without one)
        services.AddSingleton<MastersCommands>();
        services.AddSingleton<MastersQueries>();
        return services;
    }

    public static RouteGroupBuilder MapMastersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + MastersCommands.OpSaveFloor, async (HttpContext http, SaveFloorRequest body, MastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveFloorAsync(http.HccCommand(MastersCommands.OpSaveFloor), body, ct)));
        g.MapPost("/commands/" + MastersCommands.OpRetireFloor, async (HttpContext http, RetireFloorRequest body, MastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.RetireFloorAsync(http.HccCommand(MastersCommands.OpRetireFloor), body, ct)));
        g.MapPost("/commands/" + MastersCommands.OpReactivateFloor, async (HttpContext http, ReactivateFloorRequest body, MastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReactivateFloorAsync(http.HccCommand(MastersCommands.OpReactivateFloor), body, ct)));
        g.MapPost("/commands/" + MastersCommands.OpReorderFloors, async (HttpContext http, ReorderFloorsRequest body, MastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReorderFloorsAsync(http.HccCommand(MastersCommands.OpReorderFloors), body, ct)));
        g.MapPost("/commands/" + MastersCommands.OpDeleteFloor, async (HttpContext http, DeleteFloorRequest body, MastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.DeleteFloorAsync(http.HccCommand(MastersCommands.OpDeleteFloor), body, ct)));

        g.MapGet("/masters/floors", async (HttpContext http, MastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.FloorsAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/floors/history", async (HttpContext http, MastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.FloorHistoryAsync(property, principal, null, page, pageSize, ct));
        });
        g.MapGet("/masters/floors/{id:int}/history", async (HttpContext http, MastersQueries q, int id, CancellationToken ct, int? page = null, int? pageSize = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.FloorHistoryAsync(property, principal, id, page, pageSize, ct));
        });
        return api;
    }
}
