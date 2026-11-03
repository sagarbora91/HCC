using Hcc.Application.Hall;
using Hcc.Infrastructure.SqlServer.Hall;

namespace Hcc.Api.Endpoints;

/// <summary>
/// F6 hall booking, lane H1 money endpoints (F6-CONTRACT §6): <c>POST /api/v1/hall/quotations/{id}/accept</c>,
/// <c>POST /api/v1/hall/events/{id}/advance|close|cancel|refund</c> and the same operations under <c>/commands/{op}</c>. Lane H3 adds the
/// reads (venues, calendar, enquiries, quotations, reports), the non-money commands and the RBAC seed.
/// </summary>
public static class HallEndpoints
{
    public static IServiceCollection AddHallModule(this IServiceCollection services)
    {
        services.AddSingleton<IHallBillingStore, SqlHallBillingStore>();
        services.AddSingleton<Hcc.Application.Invoices.IInvoiceSource, HallInvoiceSource>();
        services.AddSingleton<HallBillingCommands>();
        return services;
    }

    public static RouteGroupBuilder MapHallEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/hall/quotations/{id:long}/accept", async (HttpContext http, long id, AcceptQuotationRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.AcceptAsync(http.HccCommand(HallBillingCommands.OpAccept), body with { QuotationId = id }, ct)));
        g.MapPost("/hall/events/{id:long}/advance", async (HttpContext http, long id, HallAdvanceRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReceiveAdvanceAsync(http.HccCommand(HallBillingCommands.OpAdvance), body with { EventId = id }, ct)));
        g.MapPost("/hall/events/{id:long}/close", async (HttpContext http, long id, CloseHallEventRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.CloseAsync(http.HccCommand(HallBillingCommands.OpClose), body with { EventId = id }, ct)));
        g.MapPost("/hall/events/{id:long}/cancel", async (HttpContext http, long id, CancelHallEventRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.CancelAsync(http.HccCommand(HallBillingCommands.OpCancel), body with { EventId = id }, ct)));
        g.MapPost("/hall/events/{id:long}/refund", async (HttpContext http, long id, HallRefundRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.RefundAsync(http.HccCommand(HallBillingCommands.OpRefund), body with { EventId = id }, ct)));
        g.MapPost("/commands/" + HallBillingCommands.OpAccept, async (HttpContext http, AcceptQuotationRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.AcceptAsync(http.HccCommand(HallBillingCommands.OpAccept), body, ct)));
        g.MapPost("/commands/" + HallBillingCommands.OpAdvance, async (HttpContext http, HallAdvanceRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.ReceiveAdvanceAsync(http.HccCommand(HallBillingCommands.OpAdvance), body, ct)));
        g.MapPost("/commands/" + HallBillingCommands.OpClose, async (HttpContext http, CloseHallEventRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.CloseAsync(http.HccCommand(HallBillingCommands.OpClose), body, ct)));
        g.MapPost("/commands/" + HallBillingCommands.OpCancel, async (HttpContext http, CancelHallEventRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.CancelAsync(http.HccCommand(HallBillingCommands.OpCancel), body, ct)));
        g.MapPost("/commands/" + HallBillingCommands.OpRefund, async (HttpContext http, HallRefundRequest body, HallBillingCommands c, CancellationToken ct) =>
            http.HccReply(await c.RefundAsync(http.HccCommand(HallBillingCommands.OpRefund), body, ct)));
        return api;
    }
}
