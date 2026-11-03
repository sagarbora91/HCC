// F7-R7 · document layouts (layout:'document', RPT-ARCHITECTURE §4.3 / F7-CONTRACT §4 batch 3), server twin of 03 Prototype/src/domain/reports/documents/model.ts:
// one DocumentModel per document, built once by a pure builder from the module's own read model (InvoiceView, FolioView, PayslipView, CheckView ...); the Lodge-look HTML
// (HCC letterhead, A16), the Excel / CSV and the PDF all read that model. ToReport() turns it into an engine ReportResult (sections) so XlsxRenderer / PdfRenderer /
// CsvRenderer render documents exactly like reports: same figures on screen, print and PDF by construction.
using System.Net;
using System.Text;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.Guests;

namespace Hcc.Reporting.Documents;

public sealed record DocTable(string Key, string? Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows);

/// <param name="Id">doc.&lt;slug&gt; (never renamed); the payslip keeps its catalogue id rpt.people.payslip.</param>
/// <param name="Pairs">Label / value facts in print order (the boxed grid).</param>
/// <param name="Money">The document carries hotel money.</param>
/// <param name="QrText">A plain string (hotel | number | date | total) — never an IRN.</param>
/// <param name="Lodge">Pre-built Lodge layout (registration card / day end), printed verbatim.</param>
public sealed record DocumentModel(string Id, string Title, string Number, string Property, IReadOnlyList<(string Label, string Value)> Pairs, IReadOnlyList<DocTable> Tables,
    IReadOnlyList<string> Notes, IReadOnlyList<string> Sign, bool Money, string Basis, string? QrText = null, LodgeLayout? Lodge = null);

public static class DocumentHtml
{
    public const string Css = LodgeLayouts.Css + ".lg-tab{width:100%;border-collapse:collapse;margin:4px 0 8px;font-size:10pt}.lg-tab th,.lg-tab td{border:1px solid black;padding:3px 5px;text-align:left}.lg-tab th{font-size:8.5pt;text-transform:uppercase;letter-spacing:.04em}"
        + ".lg-tab .n{text-align:right}.lg-tab tr.total td,.lg-tab tr.subtotal td{font-weight:bold}.lg-qr{font-size:8.5pt;margin-top:8px;word-break:break-all}";

    private static string E(string? t) => WebUtility.HtmlEncode(t ?? "");

    private static bool Num(ReportColumn c) => c.Type is ReportColumnType.Money or ReportColumnType.Int or ReportColumnType.Percent;

    private static void Table(StringBuilder sb, DocTable t)
    {
        if (t.Title is { } title) sb.Append("<div class=\"lg-sec\">").Append(E(title)).Append("</div>");
        sb.Append("<table class=\"lg-tab\"><thead><tr>");
        foreach (var c in t.Columns) sb.Append(Num(c) ? "<th class=\"n\">" : "<th>").Append(E(c.Label)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in t.Rows)
        {
            sb.Append(r.Kind == ReportRowKind.Row ? "<tr>" : $"<tr class=\"{r.Kind.ToString().ToLowerInvariant()}\">");
            foreach (var c in t.Columns) sb.Append(Num(c) ? "<td class=\"n\">" : "<td>").Append(E(CellText.Of(c, r.Cells.GetValueOrDefault(c.Key)))).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    /// <summary>The printable page body (Lodge look, HCC letterhead): the same markup as the prototype's docHtml.</summary>
    public static (string Title, string Body, string Css) Page(DocumentModel m)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (m.Lodge is { } l) return (l.Title, l.Body, l.Css);
        var sb = new StringBuilder();
        sb.Append($"<div class=\"lg-head\"><div class=\"brand\">{E(LodgeLayouts.Letterhead)}</div><h1>{E(m.Property)}</h1><div class=\"sub\">{E(m.Title)}{(m.Number.Length > 0 ? " · " + E(m.Number) : "")}</div></div>");
        if (m.Pairs.Count > 0)
        {
            sb.Append("<div class=\"lg-grid\">");
            foreach (var (k, v) in m.Pairs) sb.Append($"<div class=\"lg-cell\"><b>{E(k)}</b>{(v.Length > 0 ? E(v) : "&nbsp;")}</div>");
            sb.Append("</div>");
        }

        foreach (var t in m.Tables) Table(sb, t);
        foreach (var n in m.Notes) sb.Append($"<p class=\"lg-note\">{E(n)}</p>");
        if (m.QrText is { } qr) sb.Append($"<div class=\"lg-qr\">QR text: {E(qr)}</div>");
        if (m.Sign.Count > 0) sb.Append("<div class=\"lg-sign\">").AppendJoin("", m.Sign.Select(x => $"<div>{E(x)}</div>")).Append("</div>");
        return ($"{m.Title} {m.Number}".Trim(), sb.ToString(), Css);
    }

    /// <summary>A complete HTML page for the browser or the Android printer (printablePage twin).</summary>
    public static string FullPage(DocumentModel m)
    {
        var (title, body, css) = Page(m);
        return $"<!doctype html><html lang=\"en\"><head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{E(title)}</title><style>{css}</style>"
            + $"<style>html,body{{background:#fff!important}}body{{margin:0;padding:16px}}@page{{size:A4;margin:14mm}}</style></head><body>{body}</body></html>";
    }
}

public static class DocumentResults
{
    /// <summary>The engine result of a document: "Details" (label / value), every table, then the notes.</summary>
    public static ReportResult ToReport(DocumentModel m, DateTimeOffset runAt, string user)
    {
        ArgumentNullException.ThrowIfNull(m);
        var pairs = m.Lodge is { } l ? l.Pairs : m.Pairs;
        var tables = new List<DocTable> { new("details", "Details", [Doc.T("item", "Item"), Doc.T("value", "Value")], pairs.Select(p => Doc.Row(("item", p.Label), ("value", p.Value))).ToList()) };
        if (m.Lodge is null)
        {
            tables.AddRange(m.Tables);
            if (m.Notes.Count > 0) tables.Add(new("notes", "Notes", [Doc.T("note", "Note")], m.Notes.Select(n => Doc.Row(("note", n))).ToList()));
        }

        var sections = tables.Select(t => new ReportTable(t.Key, t.Title, t.Columns, t.Rows)).ToList();
        return new ReportResult(m.Id, m.Lodge?.Title ?? $"{m.Title} {m.Number}".Trim(), new BoundParams(new Dictionary<string, string>()), sections[0].Columns, sections[0].Rows,
            new ReportAbout(runAt, user, m.Basis, false, false), null, 0, 0, sections.Sum(t => t.Rows.Count), sections);
    }
}

public static class Doc
{
    public static ReportColumn T(string key, string label) => new(key, label, ReportColumnType.Text, false);
    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);
    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);
    public static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);
    public static ReportColumn P(string key, string label) => new(key, label, ReportColumnType.Percent, false);

    public static ReportRow Row(params (string Key, object? Value)[] cells) => Row(ReportRowKind.Row, cells);

    public static ReportRow Row(ReportRowKind kind, params (string Key, object? Value)[] cells) =>
        new(kind, cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal));
}
