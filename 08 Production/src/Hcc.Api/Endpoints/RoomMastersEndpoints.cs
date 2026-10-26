using Hcc.Application.Commands;
using Hcc.Application.RoomMasters;
using Hcc.Contracts.RoomMasters;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.RoomMasters;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Rooms masters endpoints (MA-1): <c>POST /api/v1/commands/{SaveRoomType|RetireRoomType|ReactivateRoomType|ReorderRoomTypes|DeleteRoomType|
/// SaveAmenity|RetireAmenity|ReactivateAmenity|ReorderAmenities|DeleteAmenity|SaveOccupancyCharge|RetireOccupancyCharge|ReactivateOccupancyCharge|
/// ReorderOccupancyCharges|DeleteOccupancyCharge|AddRooms|SaveRoom|RenumberRoom|RetireRoom|ReactivateRoom|DeleteRoom}</c> and
/// <c>GET /api/v1/masters/{room-types|amenities|occupancy-charges|rooms}</c> (+ <c>/history</c>, <c>/{id}/history</c>).
/// </summary>
public static class RoomMastersEndpoints
{
    public static IServiceCollection AddRoomMastersModule(this IServiceCollection services)
    {
        services.AddSingleton<IRoomMastersStore>(sp => new SqlRoomMastersStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IRoomMastersReadStore>(sp => new SqlRoomMastersReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<RoomMastersCommands>();
        services.AddSingleton<RoomMastersQueries>();
        return services;
    }

    private static void Post<TReq, TRes>(RouteGroupBuilder g, string op, Func<RoomMastersCommands, CommandContext, TReq, CancellationToken, Task<CommandResult<TRes>>> run) =>
        g.MapPost("/commands/" + op, async (HttpContext http, TReq body, RoomMastersCommands c, CancellationToken ct) => http.HccReply(await run(c, http.HccCommand(op), body, ct)));

    public static RouteGroupBuilder MapRoomMastersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        Post<SaveRoomTypeRequest, RoomTypeView>(g, RoomMastersCommands.OpSaveRoomType, (c, x, b, ct) => c.SaveRoomTypeAsync(x, b, ct));
        Post<RoomTypeIdRequest, StatusResult>(g, RoomMastersCommands.OpRetireRoomType, (c, x, b, ct) => c.RetireRoomTypeAsync(x, b, ct));
        Post<RoomTypeIdRequest, StatusResult>(g, RoomMastersCommands.OpReactivateRoomType, (c, x, b, ct) => c.ReactivateRoomTypeAsync(x, b, ct));
        Post<ReorderRoomTypesRequest, StatusResult>(g, RoomMastersCommands.OpReorderRoomTypes, (c, x, b, ct) => c.ReorderRoomTypesAsync(x, b, ct));
        Post<RoomTypeIdRequest, DeleteMasterResult>(g, RoomMastersCommands.OpDeleteRoomType, (c, x, b, ct) => c.DeleteRoomTypeAsync(x, b, ct));
        Post<SaveAmenityRequest, AmenityView>(g, RoomMastersCommands.OpSaveAmenity, (c, x, b, ct) => c.SaveAmenityAsync(x, b, ct));
        Post<AmenityIdRequest, StatusResult>(g, RoomMastersCommands.OpRetireAmenity, (c, x, b, ct) => c.RetireAmenityAsync(x, b, ct));
        Post<AmenityIdRequest, StatusResult>(g, RoomMastersCommands.OpReactivateAmenity, (c, x, b, ct) => c.ReactivateAmenityAsync(x, b, ct));
        Post<ReorderAmenitiesRequest, StatusResult>(g, RoomMastersCommands.OpReorderAmenities, (c, x, b, ct) => c.ReorderAmenitiesAsync(x, b, ct));
        Post<AmenityIdRequest, DeleteMasterResult>(g, RoomMastersCommands.OpDeleteAmenity, (c, x, b, ct) => c.DeleteAmenityAsync(x, b, ct));
        Post<SaveOccupancyChargeRequest, OccupancyChargeView>(g, RoomMastersCommands.OpSaveOccupancyCharge, (c, x, b, ct) => c.SaveOccupancyChargeAsync(x, b, ct));
        Post<OccupancyChargeIdRequest, StatusResult>(g, RoomMastersCommands.OpRetireOccupancyCharge, (c, x, b, ct) => c.RetireOccupancyChargeAsync(x, b, ct));
        Post<OccupancyChargeIdRequest, StatusResult>(g, RoomMastersCommands.OpReactivateOccupancyCharge, (c, x, b, ct) => c.ReactivateOccupancyChargeAsync(x, b, ct));
        Post<ReorderOccupancyChargesRequest, StatusResult>(g, RoomMastersCommands.OpReorderOccupancyCharges, (c, x, b, ct) => c.ReorderOccupancyChargesAsync(x, b, ct));
        Post<OccupancyChargeIdRequest, DeleteMasterResult>(g, RoomMastersCommands.OpDeleteOccupancyCharge, (c, x, b, ct) => c.DeleteOccupancyChargeAsync(x, b, ct));
        Post<AddRoomsRequest, AddRoomsResult>(g, RoomMastersCommands.OpAddRooms, (c, x, b, ct) => c.AddRoomsAsync(x, b, ct));
        Post<SaveRoomRequest, RoomMasterView>(g, RoomMastersCommands.OpSaveRoom, (c, x, b, ct) => c.SaveRoomAsync(x, b, ct));
        Post<RenumberRoomRequest, RoomMasterView>(g, RoomMastersCommands.OpRenumberRoom, (c, x, b, ct) => c.RenumberRoomAsync(x, b, ct));
        Post<RoomIdRequest, RoomMasterView>(g, RoomMastersCommands.OpRetireRoom, (c, x, b, ct) => c.RetireRoomAsync(x, b, ct));
        Post<RoomIdRequest, RoomMasterView>(g, RoomMastersCommands.OpReactivateRoom, (c, x, b, ct) => c.ReactivateRoomAsync(x, b, ct));
        Post<RoomIdRequest, DeleteMasterResult>(g, RoomMastersCommands.OpDeleteRoom, (c, x, b, ct) => c.DeleteRoomAsync(x, b, ct));

        g.MapGet("/masters/room-types", async (HttpContext http, RoomMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.RoomTypesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/amenities", async (HttpContext http, RoomMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.AmenitiesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/occupancy-charges", async (HttpContext http, RoomMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.OccupancyChargesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/masters/rooms", async (HttpContext http, RoomMastersQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.RoomsAsync(property, principal, ct));
        });
        foreach (var (path, master) in new[] { ("room-types", RoomMasterIds.RoomTypes), ("amenities", RoomMasterIds.Amenities), ("occupancy-charges", RoomMasterIds.OccupancyCharges), ("rooms", RoomMasterIds.Rooms) })
        {
            g.MapGet($"/masters/{path}/history", async (HttpContext http, RoomMastersQueries q, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, null, page, pageSize, ct));
            });
            g.MapGet($"/masters/{path}/{{id:long}}/history", async (HttpContext http, RoomMastersQueries q, long id, CancellationToken ct, int? page = null, int? pageSize = null) =>
            {
                var (property, principal) = http.HccActor();
                return Results.Ok(await q.HistoryAsync(property, principal, master, id, page, pageSize, ct));
            });
        }

        return api;
    }
}
