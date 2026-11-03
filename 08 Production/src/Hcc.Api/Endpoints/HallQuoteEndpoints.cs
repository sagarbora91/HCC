using Hcc.Application.Hall;
using Hcc.Infrastructure.SqlServer.Hall;

namespace Hcc.Api.Endpoints;

/// <summary>
/// F6 hall booking, lane H2 quotation + T&amp;C endpoints (F6-CONTRACT §6): <c>POST /api/v1/hall/quotations</c>,
/// <c>POST /api/v1/hall/quotations/{id}/edit|revise|send|override|approve-discount|lost|cancel</c>, <c>POST /api/v1/hall/quotations/expire</c>,
/// <c>POST /api/v1/hall/templates</c>, and the same operations under <c>/commands/{op}</c>. Accept is lane H1's (HallEndpoints); reads are lane H3's.
/// </summary>
public static class HallQuoteEndpoints
{
    public static IServiceCollection AddHallQuoteModule(this IServiceCollection services)
    {
        services.AddSingleton<IHallQuoteStore, SqlHallQuoteStore>();
        services.AddSingleton<HallQuoteCommands>();
        return services;
    }

    public static RouteGroupBuilder MapHallQuoteEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/hall/quotations", async (HttpContext http, CreateQuotationRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.CreateAsync(http.HccCommand(HallQuoteCommands.OpCreate), body, ct)));
        g.MapPost("/hall/quotations/{id:long}/edit", async (HttpContext http, long id, EditQuotationRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.EditAsync(http.HccCommand(HallQuoteCommands.OpEdit), body with { QuotationId = id }, ct)));
        Action(g, "revise", HallQuoteCommands.OpRevise, (c, x, b, ct) => c.ReviseAsync(x, b, ct));
        Action(g, "send", HallQuoteCommands.OpSend, (c, x, b, ct) => c.SendAsync(x, b, ct));
        Action(g, "override", HallQuoteCommands.OpOverride, (c, x, b, ct) => c.OverrideAsync(x, b, ct));
        Action(g, "approve-discount", HallQuoteCommands.OpApprove, (c, x, b, ct) => c.ApproveDiscountAsync(x, b, ct));
        Action(g, "lost", HallQuoteCommands.OpLost, (c, x, b, ct) => c.LoseAsync(x, b, ct));
        Action(g, "cancel", HallQuoteCommands.OpCancel, (c, x, b, ct) => c.CancelAsync(x, b, ct));
        g.MapPost("/hall/quotations/expire", async (HttpContext http, ExpireQuotationsRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.ExpireAsync(http.HccCommand(HallQuoteCommands.OpExpire), body, ct)));
        g.MapPost("/hall/templates", async (HttpContext http, SaveTncTemplateRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveTemplateAsync(http.HccCommand(HallQuoteCommands.OpTemplate), body, ct)));
        g.MapPost("/commands/" + HallQuoteCommands.OpCreate, async (HttpContext http, CreateQuotationRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.CreateAsync(http.HccCommand(HallQuoteCommands.OpCreate), body, ct)));
        g.MapPost("/commands/" + HallQuoteCommands.OpEdit, async (HttpContext http, EditQuotationRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.EditAsync(http.HccCommand(HallQuoteCommands.OpEdit), body, ct)));
        g.MapPost("/commands/" + HallQuoteCommands.OpExpire, async (HttpContext http, ExpireQuotationsRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.ExpireAsync(http.HccCommand(HallQuoteCommands.OpExpire), body, ct)));
        g.MapPost("/commands/" + HallQuoteCommands.OpTemplate, async (HttpContext http, SaveTncTemplateRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await c.SaveTemplateAsync(http.HccCommand(HallQuoteCommands.OpTemplate), body, ct)));
        return api;
    }

    private static void Action(RouteGroupBuilder g, string verb, string op,
        Func<HallQuoteCommands, Hcc.Application.Commands.CommandContext, QuotationActionRequest, CancellationToken, Task<Hcc.Application.Commands.CommandResult<QuotationResult>>> run)
    {
        g.MapPost("/hall/quotations/{id:long}/" + verb, async (HttpContext http, long id, QuotationActionRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await run(c, http.HccCommand(op), body with { QuotationId = id }, ct)));
        g.MapPost("/commands/" + op, async (HttpContext http, QuotationActionRequest body, HallQuoteCommands c, CancellationToken ct) =>
            http.HccReply(await run(c, http.HccCommand(op), body, ct)));
    }
}
