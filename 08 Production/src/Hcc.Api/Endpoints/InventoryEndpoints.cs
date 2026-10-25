using Hcc.Application.Inventory;
using Hcc.Contracts.Inventory;
using Hcc.Infrastructure.SqlServer.Inventory;

namespace Hcc.Api;

/// <summary>
/// Inventory endpoints (P2): GET /availability, POST /commands/QuoteStay, CreateGroupBlock, ReleaseGroupBlock. Every route
/// requires a session (P1); the actor comes from the session. GST rates come from the tax module's <c>IGstRateResolver</c> (DEC-069).
/// </summary>
public static class InventoryEndpoints
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddSingleton<IInventoryStore, SqlInventoryStore>();
        services.AddSingleton<IInventoryAllocator, SqlInventoryAllocator>();
        services.AddSingleton<IRoomBlockPort, SqlRoomBlockPort>();
        services.AddSingleton<InventoryService>();
        return services;
    }

    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/availability", async (HttpContext http, InventoryService inventory, string arrival, string departure,
            int adults, int? children, int? roomTypeId, string? ratePlanId, long? groupBlockId, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await inventory.GetAvailabilityAsync(property, principal,
                new AvailabilityQuery(arrival, departure, adults, children ?? 0, roomTypeId, ratePlanId, groupBlockId), ct));
        });
        // QuoteStay does not require an Idempotency-Key (contracts §3); without one a retry creates another quote.
        g.MapPost("/commands/QuoteStay", async (HttpContext http, InventoryService inventory, QuoteStayRequest body, CancellationToken ct) =>
            http.HccReply(await inventory.QuoteStayAsync(http.HccCommand("QuoteStay", keyRequired: false), body, ct)));
        g.MapPost("/commands/CreateGroupBlock", async (HttpContext http, InventoryService inventory, CreateGroupBlockRequest body, CancellationToken ct) =>
            http.HccReply(await inventory.CreateGroupBlockAsync(http.HccCommand("CreateGroupBlock"), body, ct)));
        g.MapPost("/commands/ReleaseGroupBlock", async (HttpContext http, InventoryService inventory, ReleaseGroupBlockRequest body, CancellationToken ct) =>
            http.HccReply(await inventory.ReleaseGroupBlockAsync(http.HccCommand("ReleaseGroupBlock"), body, ct)));
        return api;
    }
}
