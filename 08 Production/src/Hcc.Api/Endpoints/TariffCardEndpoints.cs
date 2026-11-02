using Hcc.Application.Tariff;
using Hcc.Contracts.Tariff;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Tariff;

namespace Hcc.Api.Endpoints;

/// <summary>
/// F4-T1 tariff card (PERM-TRF-001…003): <c>POST /api/v1/commands/{SaveTariffCardDraft|ProposeTariffCard|PublishTariffCard|RejectTariffCard|DiscardTariffCard|SetTariffCardWeekend}</c>
/// and <c>GET /api/v1/tariff-card</c>.
/// </summary>
public static class TariffCardEndpoints
{
    public static IServiceCollection AddTariffCardModule(this IServiceCollection services)
    {
        services.AddSingleton<ITariffCardStore>(sp => new SqlTariffCardStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<TariffCardCommands>();
        services.AddSingleton<TariffCardQueries>();
        return services;
    }

    public static RouteGroupBuilder MapTariffCardEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + TariffCardCommands.OpSave, async (HttpContext http, SaveTariffCardDraftRequest body, TariffCardCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveDraftAsync(http.HccCommand(TariffCardCommands.OpSave), body, ct)));
        g.MapPost("/commands/" + TariffCardCommands.OpPropose, async (HttpContext http, TariffCardIdRequest body, TariffCardCommands c, CancellationToken ct) =>
            http.HccReply(await c.ProposeAsync(http.HccCommand(TariffCardCommands.OpPropose), body, ct)));
        g.MapPost("/commands/" + TariffCardCommands.OpPublish, async (HttpContext http, TariffCardIdRequest body, TariffCardCommands c, CancellationToken ct) =>
            http.HccReply(await c.PublishAsync(http.HccCommand(TariffCardCommands.OpPublish), body, ct)));
        g.MapPost("/commands/" + TariffCardCommands.OpReject, async (HttpContext http, RejectTariffCardRequest body, TariffCardCommands c, CancellationToken ct) =>
            http.HccReply(await c.RejectAsync(http.HccCommand(TariffCardCommands.OpReject), body, ct)));
        g.MapPost("/commands/" + TariffCardCommands.OpDiscard, async (HttpContext http, TariffCardIdRequest body, TariffCardCommands c, CancellationToken ct) =>
            http.HccReply(await c.DiscardAsync(http.HccCommand(TariffCardCommands.OpDiscard), body, ct)));
        g.MapPost("/commands/" + TariffCardCommands.OpWeekend, async (HttpContext http, SetTariffCardWeekendRequest body, TariffCardCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetWeekendAsync(http.HccCommand(TariffCardCommands.OpWeekend), body, ct)));
        g.MapGet("/tariff-card", async (HttpContext http, TariffCardQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.ListAsync(p, u, ct));
        });
        return api;
    }
}
