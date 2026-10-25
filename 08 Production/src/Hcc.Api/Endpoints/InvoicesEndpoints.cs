using Hcc.Application.Invoices;
using Hcc.Contracts.Invoices;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Invoices;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Tax invoices and credit notes (W3-6): <c>POST /api/v1/commands/{IssueInvoice|RequestCreditNote|DecideCreditNote}</c> (Idempotency-Key) and
/// <c>GET /api/v1/invoices?from=&amp;to=</c> (register), <c>/invoices/gstr1?from=&amp;to=</c>, <c>/invoices/{id}</c>, <c>/invoices/{id}/document?format=html|text</c>,
/// <c>/credit-notes/{id}</c>, <c>/credit-notes/{id}/document</c>, <c>/credit-note-requests[?status=]</c>. Every route requires a session.
/// </summary>
public static class InvoicesEndpoints
{
    public static IServiceCollection AddInvoicesModule(this IServiceCollection services)
    {
        services.AddSingleton<IInvoiceStore>(sp => new SqlInvoiceStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IInvoiceSource, FolioInvoiceSource>();   // W3-4 adds its group master-folio source as another IInvoiceSource
        services.AddSingleton<IInvoiceDocumentRenderer, SimpleInvoiceRenderer>(); // ETP-REUSE: ETP PDF exporter behind the same port
        services.AddSingleton<InvoiceService>();
        services.AddSingleton<InvoiceQueries>();
        return services;
    }

    public static RouteGroupBuilder MapInvoicesEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + InvoiceService.OpIssue, async (HttpContext http, IssueInvoiceRequest body, InvoiceService s, CancellationToken ct) =>
            http.HccReply(await s.IssueInvoiceAsync(http.HccCommand(InvoiceService.OpIssue), body, ct)));
        g.MapPost("/commands/" + InvoiceService.OpRequestCreditNote, async (HttpContext http, RequestCreditNoteRequest body, InvoiceService s, CancellationToken ct) =>
            http.HccReply(await s.RequestCreditNoteAsync(http.HccCommand(InvoiceService.OpRequestCreditNote), body, ct)));
        g.MapPost("/commands/" + InvoiceService.OpDecideCreditNote, async (HttpContext http, DecideCreditNoteRequest body, InvoiceService s, CancellationToken ct) =>
            http.HccReply(await s.DecideCreditNoteAsync(http.HccCommand(InvoiceService.OpDecideCreditNote), body, ct)));

        g.MapGet("/invoices", async (HttpContext http, InvoiceQueries q, DateOnly from, DateOnly to, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.RegisterAsync(p, u, from, to, ct));
        });
        g.MapGet("/invoices/gstr1", async (HttpContext http, InvoiceQueries q, DateOnly from, DateOnly to, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.Gstr1Async(p, u, from, to, ct));
        });
        g.MapGet("/invoices/{invoiceId:long}", async (long invoiceId, HttpContext http, InvoiceQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetInvoiceAsync(p, u, invoiceId, ct));
        });
        g.MapGet("/invoices/{invoiceId:long}/document", async (long invoiceId, HttpContext http, InvoiceQueries q, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var d = await q.RenderInvoiceAsync(p, u, invoiceId, format, ct);
            return Results.Content(d.Content, d.ContentType);
        });
        g.MapGet("/credit-notes/{creditNoteId:long}", async (long creditNoteId, HttpContext http, InvoiceQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetCreditNoteAsync(p, u, creditNoteId, ct));
        });
        g.MapGet("/credit-notes/{creditNoteId:long}/document", async (long creditNoteId, HttpContext http, InvoiceQueries q, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var d = await q.RenderCreditNoteAsync(p, u, creditNoteId, format, ct);
            return Results.Content(d.Content, d.ContentType);
        });
        g.MapGet("/credit-note-requests", async (HttpContext http, InvoiceQueries q, CancellationToken ct, string? status = null) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.RequestsAsync(p, u, status, ct));
        });
        return api;
    }
}
