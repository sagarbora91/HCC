// F7-R7 · document layouts (Lodge look + HCC letterhead, A16): the legal and desk documents of batch 3 as a printable page or a PDF. Permissions are those of the read they are
// built from (invoice / credit note PERM-INV-001, payslip PERM-HR-006 or own, bill and KOT PERM-POS-001), so a document never widens a role; every print writes one export-log row.
//   GET /documents/invoice/{invoiceId}?format=html|pdf        GET /documents/credit-note/{creditNoteId}?format=
//   GET /documents/payslip/{runId}/{staffId}?format=          GET /documents/bill/{orderId}?format=      GET /documents/kot/{orderId}/{kotId}?format=
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Handover;
using Hcc.Application.Invoices;
using Hcc.Application.Payroll;
using Hcc.Application.Pos;
using Hcc.Application.Tariff;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Documents;
using Hcc.Reporting.Reports.Guests;

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
        g.MapGet("/documents/folio/{folioId:long}", async (HttpContext http, long folioId, BillingQueries q, DocumentSql sql, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var c = http.HccCaller();
            var v = await q.GetFolioAsync(c, folioId, ct).ConfigureAwait(false); // PERM-FOL-001
            var h = await sql.FolioHeaderAsync(c, folioId, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.Folio(v, h, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["folio"] = folioId.ToString() }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/receipt/{folioId:long}/{receiptId:long}", async (HttpContext http, long folioId, long receiptId, BillingQueries q, DocumentSql sql, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var c = http.HccCaller();
            var v = await q.GetFolioAsync(c, folioId, ct).ConfigureAwait(false);
            var h = await sql.FolioHeaderAsync(c, folioId, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.Receipt(v, receiptId, h, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["folio"] = folioId.ToString(), ["receipt"] = receiptId.ToString() }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/quotation/{bookingNo}", async (HttpContext http, string bookingNo, DocumentSql sql, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var qi = await sql.QuotationAsync(http.HccCaller(), bookingNo, ct).ConfigureAwait(false); // PERM-RES-001
            return File(await d.RenderAsync(p, u, DocumentBuilders.Quotation(qi, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["booking"] = bookingNo }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/rate-card", async (HttpContext http, TariffCardQueries q, DocumentService d, CancellationToken ct, string? version = null, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var list = await q.ListAsync(p, u, ct).ConfigureAwait(false); // PERM-TRF-001
            var card = (version is null ? list.Versions.Where(v => v.Status == "published").OrderByDescending(v => v.EffectiveFrom).FirstOrDefault() : list.Versions.FirstOrDefault(v => v.Code == version))
                ?? throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.NOT_FOUND, "No published tariff card yet.");
            return File(await d.RenderAsync(p, u, DocumentBuilders.RateCard(card, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["version"] = card.Code }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/handover-pack", async (HttpContext http, HandoverQueries q, IAccessPolicy policy, DocumentService d, CancellationToken ct, string? date = null, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            var notes = await q.NotesAsync(p, u, ct).ConfigureAwait(false); // the read filters by desk and role
            var day = date ?? notes.Notes.Select(n => n.Date).DefaultIfEmpty("").Max()!;
            var cash = await policy.HasAsync(p, u, "PERM-HND-004", null, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.HandoverPack(notes.Notes, day, cash, await d.PropertyNameAsync(p, ct).ConfigureAwait(false)), format, new Dictionary<string, string> { ["date"] = day }, ct).ConfigureAwait(false));
        });
        g.MapGet("/documents/registration-card/{bookingNo}", async (HttpContext http, string bookingNo, IAccessPolicy policy, SqlPlatform platform, DocumentService d, CancellationToken ct, string? format = null) =>
        {
            var (p, u) = http.HccActor();
            if (!await policy.HasAsync(p, u, "PERM-STY-001", null, ct).ConfigureAwait(false) && !await policy.HasAsync(p, u, "PERM-RPT-001", null, ct).ConfigureAwait(false))
                throw new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.FORBIDDEN, "You cannot print registration cards.");
            var l = await LodgeLayouts.RegistrationCardAsync(platform, http.HccCaller(), bookingNo, ct).ConfigureAwait(false); // ID number masked, no photos
            var name = await d.PropertyNameAsync(p, ct).ConfigureAwait(false);
            return File(await d.RenderAsync(p, u, DocumentBuilders.FromLodge(l, "doc.registration-card", "Guest registration card", bookingNo, name, false), format, new Dictionary<string, string> { ["booking"] = bookingNo }, ct).ConfigureAwait(false));
        });
        return api;
    }

    private static IResult File(Hcc.Application.Reporting.Rpt.RenderedFile f) => Results.File(f.Bytes, f.ContentType, f.FileName);
}
