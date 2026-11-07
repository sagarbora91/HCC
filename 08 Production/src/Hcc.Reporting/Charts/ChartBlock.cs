// F12 MEAS · meas-study-pack · ChartBlock, server twin of 03 Prototype/src/domain/reports/charts/chart.ts (MEAS-CONTRACT §9.4).
// A chart is ONE result block: a ReportTable whose first column key is "chart.<kind>" (category labels) and whose other numeric columns are the
// series (money = long paise, counts = long, percent = double with one decimal). Being an ordinary block it passes the runner untouched: rule M
// drops a money series with its column; the table prints in every format, and the chart is drawn FROM those same cells, so Excel = PDF = screen.
// Excel: native DrawingML chart parts through the OpenXmlExporter XlsxSheet.Parts hook. PDF: bars / line / stacks as PDF path operators through
// the MinimalPdfWriter Op.Raw hook, drawn in the right half of the table beside the rows they belong to (one bar per row). No package (no PDFsharp:
// Hcc.Reporting references none).
using System.Globalization;
using System.Net;
using System.Text;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer.Reporting;

namespace Hcc.Reporting.Charts;

public enum ChartKind { Bar, Line, Stacked, Heat }

public sealed record ChartSeries(string Key, string Label, ReportColumnType Type, IReadOnlyList<double?> Values);

/// <summary>What a selector hands over: kind, labels, series values (money in paise, counts, percent). <paramref name="Legacy"/> marks legacy points.</summary>
public sealed record ChartBlock(string Id, string Title, ChartKind Kind, IReadOnlyList<string> Labels, IReadOnlyList<ChartSeries> Series, string LabelTitle = "Period", IReadOnlyList<bool>? Legacy = null);

public static class ChartBlocks
{
    public static string LabelKey(ChartKind k) => "chart." + k.ToString().ToLowerInvariant();

    private static object? Cell(ReportColumnType t, double? v) => v is not { } d ? null : t == ReportColumnType.Percent ? d : (object)(long)Math.Round(d);

    /// <summary>The block as a runner section (key <c>chart:&lt;id&gt;</c>); legacy points carry Group "legacy".</summary>
    public static ReportTable Section(ChartBlock c)
    {
        ArgumentNullException.ThrowIfNull(c);
        foreach (var s in c.Series)
            if (s.Values.Count != c.Labels.Count) throw new ArgumentException($"ChartBlock {c.Id}: series {s.Key} has {s.Values.Count} values for {c.Labels.Count} labels");
        var lk = LabelKey(c.Kind);
        var cols = new List<ReportColumn> { new(lk, c.LabelTitle, ReportColumnType.Text, false) };
        cols.AddRange(c.Series.Select(s => new ReportColumn(s.Key, s.Label, s.Type, s.Type == ReportColumnType.Money)));
        var rows = c.Labels.Select((l, i) =>
        {
            var cells = new Dictionary<string, object?>(StringComparer.Ordinal) { [lk] = l };
            foreach (var s in c.Series) cells[s.Key] = Cell(s.Type, s.Values[i]);
            return new ReportRow(ReportRowKind.Row, cells, c.Legacy is { } lg && i < lg.Count && lg[i] ? "legacy" : null);
        }).ToList();
        return new ReportTable("chart:" + c.Id, c.Title, cols, rows);
    }

    /// <summary>Decodes a table back into its chart, or null when it is not a chart. Only Row rows are points.</summary>
    public static ChartBlock? Of(string? key, string? title, IReadOnlyList<ReportColumn> columns, IReadOnlyList<ReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0 || !columns[0].Key.StartsWith("chart.", StringComparison.Ordinal)) return null;
        if (!Enum.TryParse<ChartKind>(columns[0].Key[6..], true, out var kind)) return null;
        var pts = rows.Where(r => r.Kind == ReportRowKind.Row).ToList();
        var lk = columns[0].Key;
        var series = columns.Skip(1).Where(c => c.Type is ReportColumnType.Money or ReportColumnType.Int or ReportColumnType.Percent)
            .Select(c => new ChartSeries(c.Key, c.Label, c.Type, pts.Select(r => r.Cells.GetValueOrDefault(c.Key) is { } v and not string ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : (double?)null).ToList())).ToList();
        return new ChartBlock((key ?? "").StartsWith("chart:", StringComparison.Ordinal) ? key![6..] : kind.ToString().ToLowerInvariant(), title ?? "", kind,
            pts.Select(r => Convert.ToString(r.Cells.GetValueOrDefault(lk), CultureInfo.InvariantCulture) ?? "").ToList(), series, columns[0].Label, pts.Select(r => r.Group == "legacy").ToList());
    }

    public static ChartBlock? Of(ReportTable t) => t is null ? null : Of(t.Key, t.Title, t.Columns, t.Rows);

    /// <summary>The drawn value: money in rupees, counts and percent as they are. Excel caches percent as a fraction (the cell's format).</summary>
    public static double? Value(ReportColumnType t, double? v) => v is { } d ? t == ReportColumnType.Money ? d / 100.0 : d : null;

    // ── Excel ──
    private const string C = "http://schemas.openxmlformats.org/drawingml/2006/chart", A = "http://schemas.openxmlformats.org/drawingml/2006/main",
        R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    private static string E(string t) => WebUtility.HtmlEncode(t);

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Chart XML for a sheet whose row 1 is the header and rows 2… the points (XlsxRenderer's layout). Same XML as the prototype chartXml.</summary>
    public static string ChartXml(ChartBlock c, string sheet)
    {
        ArgumentNullException.ThrowIfNull(c);
        var n = c.Labels.Count;
        var last = (n + 1).ToString(CultureInfo.InvariantCulture);
        var q = E("'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'");
        var cat = $"<c:cat><c:strRef><c:f>{q}!$A$2:$A${last}</c:f><c:strCache><c:ptCount val=\"{n}\"/>{string.Concat(c.Labels.Select((l, i) => $"<c:pt idx=\"{i}\"><c:v>{E(l)}</c:v></c:pt>"))}</c:strCache></c:strRef></c:cat>";
        var line = c.Kind == ChartKind.Line;
        var ser = string.Concat(c.Series.Select((s, si) =>
        {
            var col = OpenXmlExporter.ColumnName(si + 1);
            var fmt = s.Type == ReportColumnType.Money ? "#,##0.00" : s.Type == ReportColumnType.Percent ? "0.0%" : "0";
            var cached = string.Concat(s.Values.Select((v, i) => v is not { } d ? "" : $"<c:pt idx=\"{i}\"><c:v>{Num(s.Type == ReportColumnType.Percent ? (double)((decimal)d / 100m) : Value(s.Type, d)!.Value)}</c:v></c:pt>"));
            return $"<c:ser><c:idx val=\"{si}\"/><c:order val=\"{si}\"/><c:tx><c:strRef><c:f>{q}!${col}$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>{E(s.Label)}</c:v></c:pt></c:strCache></c:strRef></c:tx>"
                + (line ? "<c:marker><c:symbol val=\"circle\"/></c:marker>" : "") + cat
                + $"<c:val><c:numRef><c:f>{q}!${col}$2:${col}${last}</c:f><c:numCache><c:formatCode>{fmt}</c:formatCode><c:ptCount val=\"{n}\"/>{cached}</c:numCache></c:numRef></c:val>" + (line ? "<c:smooth val=\"0\"/>" : "") + "</c:ser>";
        }));
        const string axes = "<c:axId val=\"500001\"/><c:axId val=\"500002\"/>";
        var plot = line ? $"<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>{ser}<c:marker val=\"1\"/>{axes}</c:lineChart>"
            : $"<c:barChart><c:barDir val=\"col\"/><c:grouping val=\"{(c.Kind == ChartKind.Stacked ? "stacked" : "clustered")}\"/><c:varyColors val=\"0\"/>{ser}<c:gapWidth val=\"80\"/>{(c.Kind == ChartKind.Stacked ? "<c:overlap val=\"100\"/>" : "")}{axes}</c:barChart>";
        var title = E(c.Title.Length > 0 ? c.Title : c.Id);
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><c:chartSpace xmlns:c=\"{C}\" xmlns:a=\"{A}\" xmlns:r=\"{R}\"><c:roundedCorners val=\"0\"/><c:chart><c:title><c:tx><c:rich><a:bodyPr/><a:p><a:r><a:t>{title}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/><c:plotArea><c:layout/>{plot}<c:catAx><c:axId val=\"500001\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:crossAx val=\"500002\"/></c:catAx><c:valAx><c:axId val=\"500002\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:majorGridlines/><c:crossAx val=\"500001\"/></c:valAx></c:plotArea><c:legend><c:legendPos val=\"b\"/></c:legend><c:plotVisOnly val=\"1\"/></c:chart></c:chartSpace>";
    }

    /// <summary>Hook used by XlsxRenderer: a chart table's sheet gets the drawing + chart parts (heat maps: the table is the map, no part).</summary>
    public static XlsxSheet Decorate(ReportTable t, XlsxSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (Of(t) is not { } c || c.Kind == ChartKind.Heat) return sheet;
        return sheet with
        {
            Parts = (index, name) =>
            {
                var i = index.ToString(CultureInfo.InvariantCulture);
                int cols = c.Series.Count + 1, rows = c.Labels.Count + 1;
                var drawing = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"{Xdr}\" xmlns:a=\"{A}\"><xdr:twoCellAnchor><xdr:from><xdr:col>{cols + 1}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>{cols + 10}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{Math.Max(rows, 18)}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to><xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"{E(c.Title.Length > 0 ? c.Title : c.Id)}\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"{C}\"><c:chart xmlns:c=\"{C}\" xmlns:r=\"{R}\" r:id=\"rId1\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>";
                return new XlsxSheetParts($"<drawing xmlns:r=\"{R}\" r:id=\"rIdChart1\"/>",
                    [new XlsxRel("rIdChart1", R + "/drawing", $"../drawings/drawing{i}.xml")],
                    [new XlsxPart($"xl/drawings/drawing{i}.xml", drawing, "application/vnd.openxmlformats-officedocument.drawing+xml"),
                     new XlsxPart($"xl/drawings/_rels/drawing{i}.xml.rels", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"{R}/chart\" Target=\"../charts/chart{i}.xml\"/></Relationships>", null),
                     new XlsxPart($"xl/charts/chart{i}.xml", ChartXml(c, name), "application/vnd.openxmlformats-officedocument.drawingml.chart+xml")]);
            },
        };
    }

    // ── PDF ──
    /// <summary>Right edge of a chart table's text columns: the chart takes the right half of the line.</summary>
    public static double TableRight(PrintTable t, double x0, double x1) => Of(null, t?.Title, t!.Columns, t.Rows) is { Kind: not ChartKind.Heat } ? x0 + (x1 - x0) / 2 : x1;

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static readonly string[] Fill = ["0.478 0.122 0.169", "0.420 0.333 0.278", "0.690 0.553 0.341", "0.169 0.114 0.086"]; // Maroon & Sand (print is light)

    /// <summary>One drawn mark (bar or point) of the PDF chart: the tests read these back.</summary>
    public sealed record Mark(string Series, int Index, double Value, double X, double Y, double W, double H);

    /// <summary>Marks for a chart table laid out from <paramref name="firstRowY"/> (baseline of row 0) at <paramref name="rowStep"/> points per row,
    /// in [xa, xb]. Bars: length = value / max × width (rupees for money); stacked: segments end to end; line: one point per row.</summary>
    public static IReadOnlyList<Mark> Marks(ChartBlock c, double xa, double xb, double firstRowY, double rowStep)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vals = c.Series.Select(s => s.Values.Select(v => Value(s.Type, v) ?? 0).ToList()).ToList();
        var max = c.Kind == ChartKind.Stacked
            ? Enumerable.Range(0, c.Labels.Count).Select(i => vals.Sum(v => Math.Max(0, v[i]))).DefaultIfEmpty(0).Max()
            : vals.SelectMany(v => v).DefaultIfEmpty(0).Max();
        var w = xb - xa;
        double Len(double v) => max > 0 ? Math.Max(0, v) / max * w : 0;
        var marks = new List<Mark>();
        var k = Math.Max(1, c.Series.Count);
        for (var i = 0; i < c.Labels.Count; i++)
        {
            var y = firstRowY - rowStep * i;
            var x = xa;
            for (var si = 0; si < c.Series.Count; si++)
            {
                var v = vals[si][i];
                switch (c.Kind)
                {
                    case ChartKind.Stacked: marks.Add(new(c.Series[si].Key, i, v, x, y - 1, Len(v), 7)); x += Len(v); break;
                    case ChartKind.Line: marks.Add(new(c.Series[si].Key, i, v, xa + Len(v), y + 2.5, 0, 0)); break;
                    default: var h = 7.0 / k; marks.Add(new(c.Series[si].Key, i, v, xa, y - 1 + h * (k - 1 - si), Len(v), h)); break;
                }
            }
        }

        return marks;
    }

    /// <summary>Hook used by PdfRenderer.Layout: the raw PDF operators drawing the chart of a chart table (empty for any other table).</summary>
    public static IReadOnlyList<MinimalPdfWriter.Op> PdfOps(PrintTable t, double x0, double x1, double firstRowY, double rowStep)
    {
        ArgumentNullException.ThrowIfNull(t);
        if (Of(null, t.Title, t.Columns, t.Rows) is not { Kind: not ChartKind.Heat } c) return [];
        double xa = TableRight(t, x0, x1) + 8, xb = x1;
        var sb = new StringBuilder("q 0.4 w\n");
        var marks = Marks(c, xa, xb, firstRowY, rowStep);
        var idx = c.Series.Select((s, i) => (s.Key, i)).ToDictionary(p => p.Key, p => p.i, StringComparer.Ordinal);
        if (c.Kind == ChartKind.Line)
        {
            foreach (var g in marks.GroupBy(m => m.Series))
            {
                sb.Append(CultureInfo.InvariantCulture, $"{Fill[idx[g.Key] % Fill.Length]} RG ");
                var first = true;
                foreach (var m in g) { sb.Append(CultureInfo.InvariantCulture, $"{N(m.X)} {N(m.Y)} {(first ? "m" : "l")} "); first = false; }
                sb.Append("S\n");
                foreach (var m in g) sb.Append(CultureInfo.InvariantCulture, $"{Fill[idx[g.Key] % Fill.Length]} rg {N(m.X - 1.5)} {N(m.Y - 1.5)} 3 3 re f\n");
            }
        }
        else
        {
            foreach (var m in marks) sb.Append(CultureInfo.InvariantCulture, $"{Fill[idx[m.Series] % Fill.Length]} rg {N(m.X)} {N(m.Y)} {N(m.W)} {N(m.H)} re f\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"0.6 G {N(xa)} {N(firstRowY + 9)} m {N(xa)} {N(firstRowY - rowStep * (c.Labels.Count - 1) - 2)} l S\nQ\n");
        return [new MinimalPdfWriter.Op(0, 0, null, Raw: sb.ToString())];
    }
}
