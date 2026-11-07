// F7-R1 · the renderers (RPT-ARCHITECTURE §4): one masked ReportResult in, a file out. A renderer never queries, re-totals or re-rounds.
using System.Globalization;
using System.Net;
using System.Text;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Text;
using Hcc.Infrastructure.SqlServer.Reporting;

namespace Hcc.Reporting;

public static class ReportFiles
{
    /// <summary><c>&lt;property code&gt;-&lt;report id&gt;-&lt;from&gt;-&lt;to&gt;.&lt;ext&gt;</c></summary>
    public static string FileName(string code, ReportResult r, string ext)
    {
        ArgumentNullException.ThrowIfNull(r);
        var v = r.Params.Values;
        var first = v.GetValueOrDefault("from") ?? v.GetValueOrDefault("month") ?? v.GetValueOrDefault("fy") ?? v.GetValueOrDefault("asOf");
        var parts = new[] { code, r.Id, first, v.GetValueOrDefault("to") }.Where(x => !string.IsNullOrEmpty(x));
        var name = string.Join('-', parts);
        return new string(name.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' ? ch : '_').ToArray()) + "." + ext;
    }
}

public sealed class XlsxRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Xlsx;

    public static object? Cell(ReportColumn c, object? v) => v switch
    {
        null => null,
        string s => s,
        DateOnly d => d,
        bool b => b ? "Yes" : "No",
        _ when c.Type == ReportColumnType.Money => new XlsxMoney(Convert.ToInt64(v, CultureInfo.InvariantCulture)),
        _ when c.Type == ReportColumnType.Percent => new XlsxPercent((double)(Convert.ToDecimal(v, CultureInfo.InvariantCulture) / 100m)), // F7-R4: decimal division, so 57.1 writes 0.571 (not 0.5710000000000001)
        _ => v,
    };

    public RenderedFile Render(ReportResult result, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        var sheets = ReportRunner.Tables(result).Select(t => Charts.ChartBlocks.Decorate(t, new XlsxSheet(t.Title ?? result.Title, // F12-STUDY-PACK hook: native chart parts
            [t.Columns.Select(c => (object?)new XlsxBold(c.Label)).ToList(), .. t.Rows.Select(r => (IReadOnlyList<object?>)t.Columns.Select(c => Cell(c, r.Cells.GetValueOrDefault(c.Key))).ToList())],
            1, t.Columns.Select(c => c.Type == ReportColumnType.Text ? 26.0 : 16.0).ToList()))).ToList();
        var a = result.About;
        var about = new List<IReadOnlyList<object?>> { new object?[] { new XlsxBold("Item"), new XlsxBold("Value") }, new object?[] { "Report", $"{result.Title} · {result.Id}" }, new object?[] { "Property", context.PropertyName } };
        about.AddRange(result.Params.Values.Select(kv => (IReadOnlyList<object?>)new object?[] { kv.Key, kv.Value }));
        about.Add(new object?[] { "Date basis", a.DateBasis });
        about.Add(new object?[] { "Run at", CellText.RunAt(a.RunAt) });
        about.Add(new object?[] { "Run by", a.User });
        about.Add(new object?[] { "Provisional", a.Provisional ? "Yes — the day is not closed" : "No" });
        if (a.Legacy) about.Add(new object?[] { "Source", "Legacy data (old Lodge software)" });
        if (result.Removed is { Count: > 0 } rm) about.Add(new object?[] { "Money columns removed", string.Join(", ", rm) });
        sheets.Add(new XlsxSheet("About", about, 1, [28, 70]));
        return new RenderedFile(ReportFiles.FileName(context.PropertyCode, result, "xlsx"), OpenXmlExporter.Mime, OpenXmlExporter.Build(sheets));
    }
}

public sealed class CsvRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Csv;

    private static string Q(string t)
    {
        var s = CsvFormula.Neutralise(t);
        return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
    }

    public RenderedFile Render(ReportResult result, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        var sb = new StringBuilder();
        foreach (var t in ReportRunner.Tables(result))
        {
            if (t.Title is { } title) sb.Append(Q(title)).Append("\r\n");
            sb.Append(string.Join(',', t.Columns.Select(c => Q(c.Label)))).Append("\r\n");
            foreach (var r in t.Rows)
                sb.Append(string.Join(',', t.Columns.Select(c =>
                {
                    var v = r.Cells.GetValueOrDefault(c.Key);
                    return Q(c.Type == ReportColumnType.Money && v is long or int ? (Convert.ToInt64(v, CultureInfo.InvariantCulture) / 100m).ToString("0.00", CultureInfo.InvariantCulture) : CellText.Of(c, v));
                }))).Append("\r\n");
            sb.Append("\r\n");
        }

        return new RenderedFile(ReportFiles.FileName(context.PropertyCode, result, "csv"), "text/csv; charset=utf-8", new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
    }
}

public sealed class PdfRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Pdf;

    public static IReadOnlyList<IReadOnlyList<MinimalPdfWriter.Op>> Layout(PrintModel m)
    {
        ArgumentNullException.ThrowIfNull(m);
        double w = m.Landscape ? MinimalPdfWriter.A4H : MinimalPdfWriter.A4W, h = m.Landscape ? MinimalPdfWriter.A4W : MinimalPdfWriter.A4H, x0 = MinimalPdfWriter.Margin, x1 = w - MinimalPdfWriter.Margin;
        var pages = new List<IReadOnlyList<MinimalPdfWriter.Op>>();
        for (var pi = 0; pi < m.Pages.Count; pi++)
        {
            var ops = new List<MinimalPdfWriter.Op>();
            var y = h - MinimalPdfWriter.Margin;
            if (pi == 0)
            {
                ops.Add(new(x0, y, m.Header.Property, true, 10)); y -= 16;
                ops.Add(new(x0, y, m.Header.Title, true, 13)); y -= 14;
                ops.Add(new(x0, y, string.Join(" · ", m.Header.Params.Select(p => $"{p.Key}: {p.Value}")), false, 8)); y -= 11;
                ops.Add(new(x0, y, $"Run {CellText.RunAt(m.Header.RunAt)} by {m.Header.User}{(m.Provisional ? " · PROVISIONAL (day not closed)" : "")}", false, 8)); y -= 16;
            }

            foreach (var t in m.Pages[pi].Tables)
            {
                if (t.Title is { } title) { ops.Add(new(x0, y, title, true, 10)); y -= 13; }
                ops.AddRange(Charts.ChartBlocks.PdfOps(t, x0, x1, y - 13, 11)); // F12-STUDY-PACK hook: a chart table draws its chart in the right half
                var cw = (Charts.ChartBlocks.TableRight(t, x0, x1) - x0) / Math.Max(1, t.Columns.Count);
                bool Num(ReportColumn c) => c.Type is ReportColumnType.Money or ReportColumnType.Int or ReportColumnType.Percent;
                double X(int i, ReportColumn c) => Num(c) ? x0 + cw * (i + 1) - 2 : x0 + cw * i;
                for (var i = 0; i < t.Columns.Count; i++) ops.Add(new(X(i, t.Columns[i]), y, t.Columns[i].Label, true, 7, 0, Num(t.Columns[i])));
                y -= 3; ops.Add(new(x0, y, null, RuleTo: x1)); y -= 10;
                foreach (var r in t.Rows)
                {
                    for (var i = 0; i < t.Columns.Count; i++)
                    {
                        var c = t.Columns[i];
                        var text = CellText.Of(c, r.Cells.GetValueOrDefault(c.Key));
                        if (text.Length > 0) ops.Add(new(X(i, c), y, text, r.Kind != ReportRowKind.Row, 7, 0, Num(c)));
                    }

                    y -= 11;
                }

                y -= 8;
            }

            ops.Add(new(x0, MinimalPdfWriter.Margin - 12, $"{m.Basis} · Page {pi + 1} of {m.Pages.Count}", false, 7));
            pages.Add(ops);
        }

        return pages;
    }

    public RenderedFile Render(ReportResult result, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        var m = PrintModelBuilder.Build(result, context.PropertyName);
        return new RenderedFile(ReportFiles.FileName(context.PropertyCode, result, "pdf"), "application/pdf", MinimalPdfWriter.Write(Layout(m), m.Landscape, result.Title, result.About.RunAt));
    }
}

public sealed class PrintHtmlRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Print;

    public RenderedFile Render(ReportResult result, RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        var m = PrintModelBuilder.Build(result, context.PropertyName);
        static string E(string t) => WebUtility.HtmlEncode(t);
        var sb = new StringBuilder($"<!doctype html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>{E(m.Header.Title)}</title><style>@page{{size:A4 {(m.Landscape ? "landscape" : "portrait")}}}.rpt-page{{break-after:page}}table{{border-collapse:collapse;width:100%;font-size:10pt}}th,td{{border-bottom:1px solid #999;padding:2px 4px;text-align:left}}.n{{text-align:right}}tr.total td,tr.subtotal td{{font-weight:bold}}</style></head><body>");
        for (var i = 0; i < m.Pages.Count; i++)
        {
            sb.Append("<section class=\"rpt-page\">");
            if (i == 0) sb.Append($"<header><div>{E(m.Header.Property)}</div><h2>{E(m.Header.Title)}</h2><div>{E(string.Join(" · ", m.Header.Params.Select(p => $"{p.Key}: {p.Value}")))}</div><div>Run {E(CellText.RunAt(m.Header.RunAt))} by {E(m.Header.User)}{(m.Provisional ? " · PROVISIONAL (day not closed)" : "")}</div></header>");
            foreach (var t in m.Pages[i].Tables)
            {
                if (t.Title is { } title) sb.Append($"<h3>{E(title)}</h3>");
                sb.Append("<table><thead><tr>").Append(string.Concat(t.Columns.Select(c => $"<th>{E(c.Label)}</th>"))).Append("</tr></thead><tbody>");
                foreach (var r in t.Rows)
                    sb.Append(r.Kind == ReportRowKind.Row ? "<tr>" : $"<tr class=\"{r.Kind.ToString().ToLowerInvariant()}\">")
                      .Append(string.Concat(t.Columns.Select(c => $"<td>{E(CellText.Of(c, r.Cells.GetValueOrDefault(c.Key)))}</td>"))).Append("</tr>");
                sb.Append("</tbody></table>");
            }

            sb.Append($"<footer>{E(m.Basis)} · Page {i + 1} of {m.Pages.Count}</footer></section>");
        }

        sb.Append("</body></html>");
        return new RenderedFile(ReportFiles.FileName(context.PropertyCode, result, "html"), "text/html; charset=utf-8", Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
