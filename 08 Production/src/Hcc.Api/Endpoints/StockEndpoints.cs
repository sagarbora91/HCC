using Hcc.Application.Stock;
using Hcc.Contracts.Stock;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Stock;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Linen, laundry &amp; stock (W2-8): <c>POST /api/v1/commands/Stock*</c> (Idempotency-Key required) and
/// <c>GET /api/v1/stock/{items|settings|alerts|laundry|counts|losses|moves|consumption}</c>. Every route needs a session; money fields are
/// null for callers without PERM-RPT-002.
/// </summary>
public static class StockEndpoints
{
    public static IServiceCollection AddHccStock(this IServiceCollection services)
    {
        services.AddSingleton<IStockStore, SqlStockStore>();
        services.AddSingleton<IStockReadStore>(sp => new SqlStockReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<StockCommands>();
        services.AddSingleton<StockQueries>();
        return services;
    }

    public static RouteGroupBuilder MapStockEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        var cmd = g.MapGroup(string.Empty).RequireHccModule(ConfigModuleIds.Inventory); // DEC-057 / A11-01: refused while Linen & stock is off; reads stay
        cmd.MapPost("/commands/" + StockCommands.OpReceiveGrn, async (HttpContext http, ReceiveGrnRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReceiveGrnAsync(http.HccCommand(StockCommands.OpReceiveGrn), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpIssue, async (HttpContext http, IssueStockRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.IssueAsync(http.HccCommand(StockCommands.OpIssue), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpUse, async (HttpContext http, UseStockRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.UseAsync(http.HccCommand(StockCommands.OpUse), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpRoomChange, async (HttpContext http, RoomChangeRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.RoomChangeAsync(http.HccCommand(StockCommands.OpRoomChange), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpLaundrySend, async (HttpContext http, SendLaundryRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.SendLaundryAsync(http.HccCommand(StockCommands.OpLaundrySend), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpLaundryReceive, async (HttpContext http, ReceiveLaundryRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReceiveLaundryAsync(http.HccCommand(StockCommands.OpLaundryReceive), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpRecordLoss, async (HttpContext http, RecordLossRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.RecordLossAsync(http.HccCommand(StockCommands.OpRecordLoss), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpCountSubmit, async (HttpContext http, SubmitCountRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.SubmitCountAsync(http.HccCommand(StockCommands.OpCountSubmit), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpCountDecide, async (HttpContext http, DecideCountRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.DecideCountAsync(http.HccCommand(StockCommands.OpCountDecide), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpItemCreate, async (HttpContext http, CreateStockItemRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.CreateItemAsync(http.HccCommand(StockCommands.OpItemCreate), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpItemSet, async (HttpContext http, SetStockItemRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetItemAsync(http.HccCommand(StockCommands.OpItemSet), body, ct)));
        cmd.MapPost("/commands/" + StockCommands.OpSettingsSet, async (HttpContext http, SetStockSettingsRequest body, StockCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetSettingsAsync(http.HccCommand(StockCommands.OpSettingsSet), body, ct)));

        g.MapGet("/stock/items", async (HttpContext http, StockQueries q, CancellationToken ct, int page = 1, int pageSize = 50, string? kind = null, bool belowMinimum = false) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ItemsAsync(property, principal, page, pageSize, kind, belowMinimum, ct));
        });
        g.MapGet("/stock/settings", async (HttpContext http, StockQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.SettingsAsync(property, principal, ct));
        });
        g.MapGet("/stock/alerts", async (HttpContext http, StockQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.AlertsAsync(property, principal, ct));
        });
        g.MapGet("/stock/laundry", async (HttpContext http, StockQueries q, CancellationToken ct, int page = 1, int pageSize = 50, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.BatchesAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/stock/counts", async (HttpContext http, StockQueries q, CancellationToken ct, int page = 1, int pageSize = 50, string? status = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.CountsAsync(property, principal, page, pageSize, status, ct));
        });
        g.MapGet("/stock/losses", async (HttpContext http, StockQueries q, CancellationToken ct, int page = 1, int pageSize = 50, string? from = null, string? to = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.LossesAsync(property, principal, page, pageSize, from, to, ct));
        });
        g.MapGet("/stock/moves", async (HttpContext http, StockQueries q, CancellationToken ct, int page = 1, int pageSize = 50, string? item = null, string? from = null, string? to = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.MovesAsync(property, principal, page, pageSize, item, from, to, ct));
        });
        g.MapGet("/stock/consumption", async (HttpContext http, StockQueries q, string from, string to, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ConsumptionAsync(property, principal, from, to, ct));
        });
        return api;
    }
}
