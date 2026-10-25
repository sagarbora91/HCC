using System.Globalization;
using System.Net;
using System.Text;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Invoices;

/// <summary>
/// Plain HTML / text rendering of invoices and credit notes (test and LAN-print adapter).
/// ETP-REUSE: replace with the ETP PDF/Excel document exporters (Hcc.Reporting) behind <see cref="IInvoiceDocumentRenderer"/> when V:\ETP is
/// available (ETP_Reuse_Pending.md row 37). Every value is HTML-encoded; nothing is fetched from outside.
/// </summary>
public sealed class SimpleInvoiceRenderer : IInvoiceDocumentRenderer
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string Rupees(Paise p) => (p.Value < 0 ? "-" : "") + "₹" + (Math.Abs(p.Value) / 100m).ToString("#,##0.00", India);

    public RenderedDocument Render(InvoiceDocumentModel document, DocumentFormat format)
    {
        ArgumentNullException.ThrowIfNull(document);
        return format == DocumentFormat.Text ? new RenderedDocument("text/plain; charset=utf-8", Text(document)) : new RenderedDocument("text/html; charset=utf-8", Html(document));
    }

    private static string Party(DocumentParty p) =>
        string.Join(" · ", new[] { p.Name, p.Address, p.Gstin is null ? null : "GSTIN " + p.Gstin, p.State }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string Text(InvoiceDocumentModel d)
    {
        var sb = new StringBuilder();
        var nl = "\n";
        sb.Append(d.Title.ToUpperInvariant()).Append(nl);
        sb.Append("No. ").Append(d.Number).Append(" · Date ").Append(d.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)).Append(nl);
        sb.Append("Supplier: ").Append(Party(d.Supplier)).Append(nl);
        sb.Append("Recipient: ").Append(Party(d.Recipient)).Append(nl);
        sb.Append("Place of supply: ").Append(d.PlaceOfSupply).Append(d.InterState ? " (inter-state, IGST)" : " (intra-state, CGST + SGST)").Append(nl);
        if (d.Reference is not null) sb.Append("Reference: ").Append(d.Reference).Append(nl);
        if (d.AgainstInvoice is not null) sb.Append("Against invoice: ").Append(d.AgainstInvoice).Append(nl);
        if (d.Reason is not null) sb.Append("Reason: ").Append(d.Reason).Append(nl);
        foreach (var l in d.Lines)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{l.Description} | SAC {l.Sac} | {Rupees(l.Taxable)} | {GstMath.Percent(l.Bps)}");
            sb.Append(d.InterState ? $" | IGST {Rupees(l.Igst)}" : $" | CGST {Rupees(l.Cgst)} | SGST {Rupees(l.Sgst)}").Append(nl);
        }

        sb.Append("Taxable value: ").Append(Rupees(d.Figures.Taxable)).Append(nl);
        if (d.InterState) sb.Append("IGST: ").Append(Rupees(d.Figures.Igst)).Append(nl);
        else sb.Append("CGST: ").Append(Rupees(d.Figures.Cgst)).Append(" · SGST: ").Append(Rupees(d.Figures.Sgst)).Append(nl);
        sb.Append("Total: ").Append(Rupees(d.Figures.Total)).Append(nl);
        sb.Append(d.AmountWords).Append(nl);
        sb.Append(d.Footer).Append(nl);
        return sb.ToString();
    }

    private static string Html(InvoiceDocumentModel d)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>").Append(E(d.Title + " " + d.Number)).Append("</title>");
        sb.Append("<style>body{font-family:sans-serif;color:#2B1D16}table{border-collapse:collapse;width:100%}td,th{border:1px solid #6B5547;padding:4px;text-align:right}td:first-child,th:first-child{text-align:left}</style></head><body>");
        sb.Append("<h1>").Append(E(d.Title)).Append("</h1>");
        sb.Append("<p>No. <strong>").Append(E(d.Number)).Append("</strong> · Date ").Append(E(d.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture))).Append("</p>");
        sb.Append("<p>Supplier: ").Append(E(Party(d.Supplier))).Append("</p><p>Recipient: ").Append(E(Party(d.Recipient))).Append("</p>");
        sb.Append("<p>Place of supply: ").Append(E(d.PlaceOfSupply)).Append(d.InterState ? " (inter-state, IGST)" : " (intra-state, CGST + SGST)").Append("</p>");
        if (d.Reference is not null) sb.Append("<p>Reference: ").Append(E(d.Reference)).Append("</p>");
        if (d.AgainstInvoice is not null) sb.Append("<p>Against invoice: ").Append(E(d.AgainstInvoice)).Append("</p>");
        if (d.Reason is not null) sb.Append("<p>Reason: ").Append(E(d.Reason)).Append("</p>");
        sb.Append(d.InterState
            ? "<table><thead><tr><th>Description</th><th>SAC</th><th>Taxable</th><th>Rate</th><th>IGST</th></tr></thead><tbody>"
            : "<table><thead><tr><th>Description</th><th>SAC</th><th>Taxable</th><th>Rate</th><th>CGST</th><th>SGST</th></tr></thead><tbody>");
        foreach (var l in d.Lines)
        {
            sb.Append("<tr><td>").Append(E(l.Description)).Append("</td><td>").Append(E(l.Sac)).Append("</td><td>").Append(E(Rupees(l.Taxable)))
                .Append("</td><td>").Append(E(GstMath.Percent(l.Bps))).Append("</td>");
            if (d.InterState) sb.Append("<td>").Append(E(Rupees(l.Igst))).Append("</td>");
            else sb.Append("<td>").Append(E(Rupees(l.Cgst))).Append("</td><td>").Append(E(Rupees(l.Sgst))).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<p>Taxable value ").Append(E(Rupees(d.Figures.Taxable)));
        sb.Append(d.InterState ? " · IGST " + E(Rupees(d.Figures.Igst)) : " · CGST " + E(Rupees(d.Figures.Cgst)) + " · SGST " + E(Rupees(d.Figures.Sgst)));
        sb.Append(" · <strong>Total ").Append(E(Rupees(d.Figures.Total))).Append("</strong></p>");
        sb.Append("<p>").Append(E(d.AmountWords)).Append("</p><p>").Append(E(d.Footer)).Append("</p></body></html>");
        return sb.ToString();
    }
}
