// F7-R7 · document layouts (Lodge look + HCC letterhead, A16): the legal and desk documents of batch 3 as a printable page or a PDF. Permissions are those of the read they are
// built from (invoice / credit note PERM-INV-001, payslip PERM-HR-006 or own, bill and KOT PERM-POS-001), so a document never widens a role; every print writes one export-log row.
//   GET /documents/invoice/{invoiceId}?format=html|pdf        GET /documents/credit-note/{creditNoteId}?format=
//   GET /documents/payslip/{runId}/{staffId}?format=          GET /documents/bill/{orderId}?format=      GET /documents/kot/{orderId}/{kotId}?format=
using Hcc.Application.Invoices;
using Hcc.Application.Payroll;
using Hcc.Application.Pos;
using Hcc.Reporting.Documents;

namespace Hcc.Api.Endpoints;

public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/documents/invoice/{invoiceId:long}", async (HttpContext http, long invoiceId, InvoiceQueries q, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var v = await q.GetInvoiceAsync(p, u, invoiceId, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.Invoice(v, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["invoice"] = invoiceId.ToString() }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/credit-note/{creditNoteId:long}", async (HttpContext http, long creditNoteId, InvoiceQueries q, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var v = await q.GetCreditNoteAsync(p, u, creditNoteId, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.CreditNote(v, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["creditNote"] = creditNoteId.ToString() }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/payslip/{runId:long}/{staffId:long}", async (HttpContext http, long runId, long staffId, PayrollQueries q, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var v = await q.GetPayslipAsync(p, u, runId, staffId, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.Payslip(v, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["run"] = runId.ToString(), ["staff"] = staffId.ToString() }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/bill/{orderId:long}", async (HttpContext http, long orderId, PosQueries q, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var v = await q.GetCheckAsync(http.PosCaller(), orderId, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.OutletBill(v, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["order"] = orderId.ToString() }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/kot/{orderId:long}/{kotId:long}", async (HttpContext http, long orderId, long kotId, PosQueries q, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var v = await q.GetCheckAsync(http.PosCaller(), orderId, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.Kot(v, kotId, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["order"] = orderId.ToString(), ["kot"] = kotId.ToString() }, ct).ConfigureAwait(false));
        });
        return api;
    }

    private static IResult File(Hcc.Application.Reporting.Rpt.RenderedFile f) => Results.File(f.Bytes, f.ContentType, f.FileName);
}
