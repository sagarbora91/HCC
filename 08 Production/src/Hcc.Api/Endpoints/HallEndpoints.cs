using Hcc.Application.Hall;
using Hcc.Infrastructure.SqlServer;
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
        // F6-H3: enquiries, halls master, print log (commands) and the reads
        services.AddSingleton<IHallStore>(sp => new SqlHallStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<HallCommands>();
        services.AddSingleton<HallQueries>();
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

        // ── F6-H3: non-money commands ──
        g.MapPost("/hall/enquiries", async (HttpContext http, AddHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.AddEnquiryAsync(http.HccCommand(HallCommands.OpAddEnquiry), body, ct)));
        g.MapPost("/hall/enquiries/{id:long}/update", async (HttpContext http, long id, UpdateHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.UpdateEnquiryAsync(http.HccCommand(HallCommands.OpUpdateEnquiry), body with { EnquiryId = id }, ct)));
        g.MapPost("/hall/enquiries/{id:long}/lost", async (HttpContext http, long id, LoseHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.LoseEnquiryAsync(http.HccCommand(HallCommands.OpLoseEnquiry), body with { EnquiryId = id }, ct)));
        g.MapPost("/hall/enquiries/{id:long}/hold", async (HttpContext http, long id, HoldHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.HoldEnquiryAsync(http.HccCommand(HallCommands.OpHoldEnquiry), body with { EnquiryId = id }, ct)));
        g.MapPost("/hall/venues", async (HttpContext http, UpsertHallVenueRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.UpsertVenueAsync(http.HccCommand(HallCommands.OpUpsertVenue), body, ct)));
        g.MapPost("/hall/print-log", async (HttpContext http, LogHallPrintRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.LogPrintAsync(http.HccCommand(HallCommands.OpLogPrint), body, ct)));
        g.MapPost("/commands/" + HallCommands.OpAddEnquiry, async (HttpContext http, AddHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.AddEnquiryAsync(http.HccCommand(HallCommands.OpAddEnquiry), body, ct)));
        g.MapPost("/commands/" + HallCommands.OpUpdateEnquiry, async (HttpContext http, UpdateHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.UpdateEnquiryAsync(http.HccCommand(HallCommands.OpUpdateEnquiry), body, ct)));
        g.MapPost("/commands/" + HallCommands.OpLoseEnquiry, async (HttpContext http, LoseHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.LoseEnquiryAsync(http.HccCommand(HallCommands.OpLoseEnquiry), body, ct)));
        g.MapPost("/commands/" + HallCommands.OpHoldEnquiry, async (HttpContext http, HoldHallEnquiryRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.HoldEnquiryAsync(http.HccCommand(HallCommands.OpHoldEnquiry), body, ct)));
        g.MapPost("/commands/" + HallCommands.OpUpsertVenue, async (HttpContext http, UpsertHallVenueRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.UpsertVenueAsync(http.HccCommand(HallCommands.OpUpsertVenue), body, ct)));
        g.MapPost("/commands/" + HallCommands.OpLogPrint, async (HttpContext http, LogHallPrintRequest body, HallCommands c, CancellationToken ct) =>
            http.HccReply(await c.LogPrintAsync(http.HccCommand(HallCommands.OpLogPrint), body, ct)));

        // ── F6-H3: reads (the calendar and the function sheet carry no money; every other money field is null without the view of PERM-HAL-002, DEC-061) ──
        g.MapGet("/hall/venues", async (HttpContext http, HallQueries q, CancellationToken ct) => { var (p, u) = http.HccActor(); return Results.Ok(await q.VenuesAsync(p, u, ct)); });
        g.MapGet("/hall/menus", async (HttpContext http, HallQueries q, CancellationToken ct) => { var (p, u) = http.HccActor(); return Results.Ok(await q.MenusAsync(p, u, ct)); });
        g.MapGet("/hall/templates", async (HttpContext http, HallQueries q, CancellationToken ct) => { var (p, u) = http.HccActor(); return Results.Ok(await q.TncTemplatesAsync(p, u, ct)); });
        g.MapGet("/hall/lost-reasons", (HttpContext http, HallQueries q) => Results.Ok(q.LostReasons()));
        g.MapGet("/hall/calendar", async (HttpContext http, HallQueries q, CancellationToken ct, string from, string to, int? venue = null) =>
        {
            var (p, u) = http.HccActor();
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", out var f) || !DateOnly.TryParseExact(to, "yyyy-MM-dd", out var t)) throw Hcc.Domain.Errors.HccException.Validation("from and to: use dates like 2026-12-05.");
            return Results.Ok(await q.CalendarAsync(p, u, f, t, venue, ct));
        });
        g.MapGet("/hall/enquiries", async (HttpContext http, HallQueries q, CancellationToken ct, string? status = null) => { var (p, u) = http.HccActor(); return Results.Ok(await q.EnquiriesAsync(p, u, status, ct)); });
        g.MapGet("/hall/quotations", async (HttpContext http, HallQueries q, CancellationToken ct, string? status = null) => { var (p, u) = http.HccActor(); return Results.Ok(await q.QuotationsAsync(p, u, status, null, ct)); });
        g.MapGet("/hall/quotations/{id:long}", async (HttpContext http, long id, HallQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return (await q.QuotationsAsync(p, u, null, id, ct)).FirstOrDefault() is { } v ? Results.Ok(v) : throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.NOT_FOUND, "Quotation not found.");
        });
        g.MapGet("/hall/events", async (HttpContext http, HallQueries q, CancellationToken ct, string? status = null) => { var (p, u) = http.HccActor(); return Results.Ok(await q.EventsAsync(p, u, status, null, ct)); });
        g.MapGet("/hall/events/{id:long}", async (HttpContext http, long id, HallQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return (await q.EventsAsync(p, u, null, id, ct)).FirstOrDefault() is { } v ? Results.Ok(v) : throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.NOT_FOUND, "Hall event not found.");
        });
        g.MapGet("/hall/snapshot", async (HttpContext http, HallQueries q, CancellationToken ct) => { var (p, u) = http.HccActor(); return Results.Ok(await q.SnapshotAsync(p, u, ct)); });
        return api;
    }
}
