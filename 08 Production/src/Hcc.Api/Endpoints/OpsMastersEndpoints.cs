using Hcc.Application.OpsMasters;
using Hcc.Contracts.OpsMasters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.OpsMasters;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Operations masters (MA-7): housekeeping task types and zones, complaint and lost-and-found categories.
/// Commands <c>POST /api/v1/commands/{Save|Retire|Reactivate|Delete}{HkTaskType|HkZone|ComplaintCategory|LostFoundCategory}</c> and
/// <c>Reorder{HkTaskTypes|HkZones|ComplaintCategories|LostFoundCategories}</c> (PERM-MST-001);
/// reads <c>GET /housekeeping/task-types</c>, <c>/housekeeping/zones</c>, <c>/guest-services/complaint-categories</c>,
/// <c>/guest-services/lost-found-categories</c> (any signed-in user) with <c>/history</c> and <c>/{id}/history</c> (PERM-MST-001).
/// </summary>
public static class OpsMastersEndpoints
{
    public static IServiceCollection AddOpsMastersModule(this IServiceCollection services)
    {
        services.AddSingleton<IOpsMastersStore, SqlOpsMastersStore>();
        services.AddSingleton<IOpsMastersReadStore>(sp => new SqlOpsMastersReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<Hcc.Application.Masters.IMasterUsage, SqlOpsMasterUsage>();
        services.AddSingleton<IHkTaskTypeSource, SqlHkTaskTypeSource>();
        services.AddSingleton<IGsCategorySource>(sp => new SqlGsCategorySource(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<OpsMastersCommands>();
        services.AddSingleton<OpsMastersQueries>();
        return services;
    }

    public static RouteGroupBuilder MapOpsMastersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();

        g.MapPost("/commands/" + OpsMasterDefs.HkTaskTypes.Save, async (HttpContext http, SaveHkTaskTypeRequest body, OpsMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveHkTaskTypeAsync(http.HccCommand(OpsMasterDefs.HkTaskTypes.Save), body, ct)));
        g.MapPost("/commands/" + OpsMasterDefs.HkZones.Save, async (HttpContext http, SaveHkZoneRequest body, OpsMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveHkZoneAsync(http.HccCommand(OpsMasterDefs.HkZones.Save), body, ct)));
        g.MapPost("/commands/" + OpsMasterDefs.ComplaintCategories.Save, async (HttpContext http, SaveComplaintCategoryRequest body, OpsMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveComplaintCategoryAsync(http.HccCommand(OpsMasterDefs.ComplaintCategories.Save), body, ct)));
        g.MapPost("/commands/" + OpsMasterDefs.LostFoundCategories.Save, async (HttpContext http, SaveLostFoundCategoryRequest body, OpsMastersCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveLostFoundCategoryAsync(http.HccCommand(OpsMasterDefs.LostFoundCategories.Save), body, ct)));

        foreach (var def in OpsMasterDefs.All)
        {
            var d = def; // captured per master
            g.MapPost("/commands/" + d.Retire, async (HttpContext http, OpsRowRequest body, OpsMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.RetireAsync(d, http.HccCommand(d.Retire), body, ct)));
            g.MapPost("/commands/" + d.Reactivate, async (HttpContext http, OpsRowRequest body, OpsMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.ReactivateAsync(d, http.HccCommand(d.Reactivate), body, ct)));
            g.MapPost("/commands/" + d.Delete, async (HttpContext http, OpsRowRequest body, OpsMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.DeleteAsync(d, http.HccCommand(d.Delete), body, ct)));
            g.MapPost("/commands/" + d.Reorder, async (HttpContext http, OpsReorderRequest body, OpsMastersCommands c, CancellationToken ct) =>
                http.HccReply(await c.ReorderAsync(d, http.HccCommand(d.Reorder), body, ct)));
        }

        MapRead(g, "/housekeeping/task-types", OpsMasterDefs.HkTaskTypes, (q, p, page, size, st, ct) => q.TaskTypesAsync(p, page, size, st, ct));
        MapRead(g, "/housekeeping/zones", OpsMasterDefs.HkZones, (q, p, page, size, st, ct) => q.ZonesAsync(p, page, size, st, ct));
        MapRead(g, "/guest-services/complaint-categories", OpsMasterDefs.ComplaintCategories, (q, p, page, size, st, ct) => q.ComplaintCategoriesAsync(p, page, size, st, ct));
        MapRead(g, "/guest-services/lost-found-categories", OpsMasterDefs.LostFoundCategories, (q, p, page, size, st, ct) => q.LostFoundCategoriesAsync(p, page, size, st, ct));
        return api;
    }

    private static void MapRead<T>(RouteGroupBuilder g, string path, OpsMasterDef def, Func<OpsMastersQueries, int, int?, int?, string?, CancellationToken, Task<OpsList<T>>> list)
    {
        g.MapGet(path, async (HttpContext http, OpsMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, _) = http.HccActor();
            return Results.Ok(await list(q, property, page, pageSize, status, ct));
        });
        g.MapGet(path + "/history", async (HttpContext http, OpsMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.HistoryAsync(def, property, principal, null, page, pageSize, ct));
        });
        g.MapGet(path + "/{id:int}/history", async (HttpContext http, OpsMastersQueries q, int id, CancellationToken ct, int? page = null, int? pageSize = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.HistoryAsync(def, property, principal, id, page, pageSize, ct));
        });
    }
}
