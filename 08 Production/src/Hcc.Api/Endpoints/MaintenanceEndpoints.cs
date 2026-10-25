using Hcc.Application.Access;
using Hcc.Application.Maintenance;
using Hcc.Contracts.Maintenance;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Maintenance;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Maintenance endpoints (W2-7): <c>POST /api/v1/commands/{CreateWorkOrder|ConvertRepairToWorkOrder|AssignWorkOrder|StartWorkOrder|HoldWorkOrder|
/// AddWorkOrderPart|CompleteWorkOrder|VerifyWorkOrder|CancelWorkOrder|BlockWorkOrderRoom|GeneratePmWorkOrders|SavePmPlan|SaveMaintenanceAsset|
/// SaveMaintenanceVendor|SaveAmc|ReceiveSpare}</c> with an Idempotency-Key header, and <c>GET /api/v1/maintenance/{work-orders|work-orders/{id}|registers|amcs|report}</c>.
/// Money is returned only to finance readers (DEC-063 G12).
/// </summary>
public static class MaintenanceEndpoints
{
    public static IServiceCollection AddHccMaintenance(this IServiceCollection services)
    {
        services.AddSingleton<IMaintenanceStore, SqlMaintenanceStore>();
        services.AddSingleton<IMaintenanceReadStore>(sp => new SqlMaintenanceReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        // Assigned-scope resolver for P1's policy (scope kind "workOrder").
        services.AddSingleton<IAccessScopeResolver>(sp => new SqlMaintenanceScopeResolver(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<MaintenanceCommands>();
        services.AddSingleton<MaintenanceQueries>();
        return services;
    }

    public static RouteGroupBuilder MapMaintenanceEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + MaintenanceCommands.OpCreate, async (HttpContext http, CreateWorkOrderRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.CreateAsync(http.HccCommand(MaintenanceCommands.OpCreate), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpConvertRepair, async (HttpContext http, ConvertRepairRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.ConvertRepairAsync(http.HccCommand(MaintenanceCommands.OpConvertRepair), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpAssign, async (HttpContext http, AssignWorkOrderRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.AssignAsync(http.HccCommand(MaintenanceCommands.OpAssign), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpStart, async (HttpContext http, StartWorkOrderRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.StartAsync(http.HccCommand(MaintenanceCommands.OpStart), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpHold, async (HttpContext http, HoldWorkOrderRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.HoldAsync(http.HccCommand(MaintenanceCommands.OpHold), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpPart, async (HttpContext http, AddWorkOrderPartRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.AddPartAsync(http.HccCommand(MaintenanceCommands.OpPart), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpComplete, async (HttpContext http, CompleteWorkOrderRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.CompleteAsync(http.HccCommand(MaintenanceCommands.OpComplete), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpVerify, async (HttpContext http, VerifyWorkOrderRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.VerifyAsync(http.HccCommand(MaintenanceCommands.OpVerify), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpCancel, async (HttpContext http, CancelWorkOrderRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.CancelAsync(http.HccCommand(MaintenanceCommands.OpCancel), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpBlockRoom, async (HttpContext http, BlockWorkOrderRoomRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.BlockRoomAsync(http.HccCommand(MaintenanceCommands.OpBlockRoom), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpGeneratePm, async (HttpContext http, GeneratePmWorkOrdersRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.GeneratePmAsync(http.HccCommand(MaintenanceCommands.OpGeneratePm), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpSavePlan, async (HttpContext http, SavePmPlanRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SavePlanAsync(http.HccCommand(MaintenanceCommands.OpSavePlan), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpSaveAsset, async (HttpContext http, SaveAssetRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveAssetAsync(http.HccCommand(MaintenanceCommands.OpSaveAsset), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpSaveVendor, async (HttpContext http, SaveVendorRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveVendorAsync(http.HccCommand(MaintenanceCommands.OpSaveVendor), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpSaveAmc, async (HttpContext http, SaveAmcRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveAmcAsync(http.HccCommand(MaintenanceCommands.OpSaveAmc), body, ct)));
        g.MapPost("/commands/" + MaintenanceCommands.OpReceiveSpare, async (HttpContext http, ReceiveSpareRequest body, MaintenanceCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReceiveSpareAsync(http.HccCommand(MaintenanceCommands.OpReceiveSpare), body, ct)));

        g.MapGet("/maintenance/work-orders", async (HttpContext http, MaintenanceQueries q, CancellationToken ct, int page = 1, int pageSize = 50, string? status = null, string? priority = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListWorkOrdersAsync(property, principal, page, pageSize, status, priority, ct));
        });
        g.MapGet("/maintenance/work-orders/{id:long}", async (HttpContext http, long id, MaintenanceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.GetWorkOrderAsync(property, principal, id, ct));
        });
        g.MapGet("/maintenance/registers", async (HttpContext http, MaintenanceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.RegistersAsync(property, principal, ct));
        });
        g.MapGet("/maintenance/amcs", async (HttpContext http, MaintenanceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.AmcsAsync(property, principal, ct));
        });
        g.MapGet("/maintenance/report", async (HttpContext http, MaintenanceQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ReportAsync(property, principal, ct));
        });
        return api;
    }
}
