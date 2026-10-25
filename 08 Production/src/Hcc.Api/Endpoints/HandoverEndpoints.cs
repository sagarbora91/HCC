using Hcc.Application.Handover;
using Hcc.Contracts.Handover;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Handover;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Shift handover endpoints: <c>POST /api/v1/commands/{SaveHandoverTemplate|ToggleHandoverTemplate|TickChecklistItem|SubmitChecklistRun|
/// ReopenChecklistRun|SubmitHandoverNote|AcknowledgeHandoverNote|CompleteCarryItem}</c> and <c>GET /api/v1/handover/{checklists|notes|carry|templates|dashboard}</c>.
/// </summary>
public static class HandoverEndpoints
{
    public static IServiceCollection AddHandoverModule(this IServiceCollection services)
    {
        services.AddSingleton<IHandoverStore, SqlHandoverStore>();
        services.AddSingleton<IHandoverReadStore>(sp => new SqlHandoverReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IHandoverPrincipals>(sp => new SqlHandoverPrincipals(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<HandoverCommands>();
        services.AddSingleton<HandoverQueries>();
        return services;
    }

    public static RouteGroupBuilder MapHandoverEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + HandoverCommands.OpSaveTemplate, async (HttpContext http, SaveHandoverTemplateRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveTemplateAsync(http.HccCommand(HandoverCommands.OpSaveTemplate), body, ct)));
        g.MapPost("/commands/" + HandoverCommands.OpToggleTemplate, async (HttpContext http, ToggleHandoverTemplateRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.ToggleTemplateAsync(http.HccCommand(HandoverCommands.OpToggleTemplate), body, ct)));
        g.MapPost("/commands/" + HandoverCommands.OpTick, async (HttpContext http, TickChecklistItemRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.TickAsync(http.HccCommand(HandoverCommands.OpTick), body, ct)));
        g.MapPost("/commands/" + HandoverCommands.OpSubmitRun, async (HttpContext http, SubmitChecklistRunRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.SubmitRunAsync(http.HccCommand(HandoverCommands.OpSubmitRun), body, ct)));
        g.MapPost("/commands/" + HandoverCommands.OpReopenRun, async (HttpContext http, ReopenChecklistRunRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReopenRunAsync(http.HccCommand(HandoverCommands.OpReopenRun), body, ct)));
        g.MapPost("/commands/" + HandoverCommands.OpSubmitNote, async (HttpContext http, SubmitHandoverNoteRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.SubmitNoteAsync(http.HccCommand(HandoverCommands.OpSubmitNote), body, ct)));
        g.MapPost("/commands/" + HandoverCommands.OpAckNote, async (HttpContext http, AcknowledgeHandoverNoteRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.AcknowledgeNoteAsync(http.HccCommand(HandoverCommands.OpAckNote), body, ct)));
        g.MapPost("/commands/" + HandoverCommands.OpCarryDone, async (HttpContext http, CompleteCarryItemRequest body, HandoverCommands c, CancellationToken ct) =>
            http.HccReply(await c.CompleteCarryAsync(http.HccCommand(HandoverCommands.OpCarryDone), body, ct)));

        g.MapGet("/handover/checklists", async (HttpContext http, HandoverQueries q, CancellationToken ct, string? date = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.MyChecklistsAsync(property, principal, date, ct));
        });
        g.MapGet("/handover/notes", async (HttpContext http, HandoverQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.NotesAsync(property, principal, ct));
        });
        g.MapGet("/handover/carry", async (HttpContext http, HandoverQueries q, CancellationToken ct, string? desk = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.OpenCarryAsync(property, principal, desk, ct));
        });
        g.MapGet("/handover/templates", async (HttpContext http, HandoverQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.TemplatesAsync(property, principal, ct));
        });
        g.MapGet("/handover/dashboard", async (HttpContext http, HandoverQueries q, CancellationToken ct, string? date = null, string? by = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.DashboardAsync(property, principal, date, by, ct));
        });
        return api;
    }
}
