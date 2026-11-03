// F7-R1 · three-way equality (screen = Excel = PDF text / print), CSV neutralisation, PDF golden (text + page count), file names.
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting.Tests;

public sealed class RendererTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly RenderContext Ctx = new("HPE", "Test Hotel", ReportFormat.Xlsx);

    private static Task<ReportResult> Demo(int rows = 2) =>
        Rig.Runner(null, new DemoSelector(rows)).RunAsync(Rig.C(1), "rpt.admin.engine-test", new Dictionary<string, string?> { ["from"] = "2026-10-20", ["to"] = "2026-10-24" }, new RunOptions(All: true), Ct);

    /// <summary>Sheet XML → rows (inline strings or numbers; gaps filled with null).</summary>
    public static List<List<object?>> SheetCells(byte[] xlsx, int sheet)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        using var rd = new StreamReader(zip.GetEntry($"xl/worksheets/sheet{sheet}.xml")!.Open());
        var xml = rd.ReadToEnd();
        var rows = new List<List<object?>>();
        foreach (Match rm in Regex.Matches(xml, "<row [^>]*>(.*?)</row>", RegexOptions.Singleline))
        {
            var row = new List<object?>();
            foreach (Match cm in Regex.Matches(rm.Groups[1].Value, "<c r=\"([A-Z]+)\\d+\"[^>]*?(?:/>|>(.*?)</c>)", RegexOptions.Singleline))
            {
                var col = cm.Groups[1].Value.Aggregate(0, (n, ch) => n * 26 + ch - 'A' + 1) - 1;
                while (row.Count < col) row.Add(null);
                var inner = cm.Groups[2].Value;
                var t = Regex.Match(inner, "<t[^>]*>(.*?)</t>", RegexOptions.Singleline);
                var v = Regex.Match(inner, "<v>(.*?)</v>");
                row.Add(t.Success ? System.Net.WebUtility.HtmlDecode(t.Groups[1].Value) : v.Success ? decimal.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture) : null);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object? ExcelOf(ReportColumn c, object? v) => v switch
    {
        null or "" => null,
        string s => s,
        _ when c.Type == ReportColumnType.Money => Convert.ToDecimal(v, CultureInfo.InvariantCulture) / 100m,
        _ when c.Type == ReportColumnType.Percent => Convert.ToDecimal(v, CultureInfo.InvariantCulture) / 100m,
        _ => Convert.ToDecimal(v, CultureInfo.InvariantCulture),
    };

    /// <summary>Asserts screen = Excel = PDF text = print text for a result (RPT-ARCHITECTURE §9).</summary>
    internal static void ThreeWay(ReportResult r)
    {
        var tables = ReportRunner.Tables(r);
        var xlsx = new XlsxRenderer().Render(r, Ctx).Bytes;
        for (var i = 0; i < tables.Count; i++)
        {
            var t = tables[i];
            var cells = SheetCells(xlsx, i + 1);
            Assert.Equal(t.Columns.Select(c => (object?)c.Label), cells[0]);
            Assert.Equal(t.Rows.Count + 1, cells.Count);
            for (var j = 0; j < t.Rows.Count; j++)
            {
                var want = t.Columns.Select(c => ExcelOf(c, t.Rows[j].Cells.GetValueOrDefault(c.Key))).ToList();
                var got = cells[j + 1];
                while (got.Count < want.Count) got.Add(null);
                Assert.Equal(want, got.Take(want.Count));
            }
        }

        var model = PrintModelBuilder.Build(r, Ctx.PropertyName);
        var text = PrintModelBuilder.Text(model).Split('\n').Where(l => l != "\f" && !Regex.IsMatch(l, "^Page \\d+ of \\d+$")).ToList();
        var want2 = tables.SelectMany(t => t.Rows.Select(row => string.Join('\t', t.Columns.Select(c => CellText.Of(c, row.Cells.GetValueOrDefault(c.Key)))))).ToList();
        Assert.Equal(want2, text.Where(l => !tables.Any(t => l == string.Join('\t', t.Columns.Select(c => c.Label)))));
        var (pages, lines) = PdfText.Extract(new PdfRenderer().Render(r, Ctx).Bytes);
        Assert.Equal(model.Pages.Count, pages);
        var pdfCells = lines.ToList();
        var k = 0;
        foreach (var t in tables)
            foreach (var row in t.Rows)
                foreach (var c in t.Columns)
                {
                    var s = CellText.Of(c, row.Cells.GetValueOrDefault(c.Key)).Replace("₹", "Rs.", StringComparison.Ordinal);
                    if (s.Length == 0) continue;
                    var at = pdfCells.IndexOf(s, k);
                    Assert.True(at >= 0, $"PDF lacks '{s}' after line {k}");
                    k = at + 1;
                }
    }

    [Fact]
    public async Task Screen_Excel_PDF_and_print_carry_the_same_figures()
    {
        ThreeWay(await Demo());
        ThreeWay(await Demo(120));
    }

    [Fact]
    public async Task Csv_neutralises_formula_text_money_in_rupees_and_never_holds_a_full_id()
    {
        var r = await Demo();
        var csv = Encoding.UTF8.GetString(new CsvRenderer().Render(r, Ctx).Bytes);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv, StringComparison.Ordinal);
        Assert.Contains("1000.01", csv, StringComparison.Ordinal);
        foreach (var bytes in new[] { new CsvRenderer().Render(r, Ctx).Bytes, new PrintHtmlRenderer().Render(r, Ctx).Bytes, new PdfRenderer().Render(r, Ctx).Bytes })
            Assert.DoesNotContain("5678 9012", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_is_deterministic_landscape_over_eight_columns_and_names_follow_the_rule()
    {
        var r = await Demo();
        Assert.Equal(new PdfRenderer().Render(r, Ctx).Bytes, new PdfRenderer().Render(r, Ctx).Bytes);
        Assert.False(PrintModelBuilder.Build(r, "P").Landscape);
        Assert.Equal("HPE-rpt.admin.engine-test-2026-10-20-2026-10-24.pdf", ReportFiles.FileName("HPE", r, "pdf"));
        Assert.Equal("₹1,23,45,678.90", CellText.Rupees(1234567890));
        Assert.Equal("-₹1,000", CellText.Rupees(-100000));
        var html = Encoding.UTF8.GetString(new PrintHtmlRenderer().Render(r, Ctx).Bytes);
        Assert.Contains("Page 1 of 1", html, StringComparison.Ordinal);
        Assert.Contains("Test basis", html, StringComparison.Ordinal);
    }

    /// <summary>PDF golden: text runs + page count of the demo report against Goldens/rpt.admin.engine-test.pdf.txt (tools/pdf-drift.mjs checks the
    /// committed .pdf the same way). HCC_ACCEPT_GOLDENS=&lt;path to this test folder&gt; rewrites both.</summary>
    [Fact]
    public async Task Pdf_golden_text_and_page_count_do_not_drift()
    {
        var bytes = new PdfRenderer().Render(await Demo(60), Ctx).Bytes;
        var (pages, lines) = PdfText.Extract(bytes);
        var text = $"pages: {pages}\n" + string.Join('\n', lines) + "\n";
        if (Environment.GetEnvironmentVariable("HCC_ACCEPT_GOLDENS") is { Length: > 0 } dir)
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "Goldens", "rpt.admin.engine-test.pdf"), bytes, Ct);
            await File.WriteAllTextAsync(Path.Combine(dir, "Goldens", "rpt.admin.engine-test.pdf.txt"), text, Ct);
        }

        Assert.Equal((await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Goldens", "rpt.admin.engine-test.pdf.txt"), Ct)).ReplaceLineEndings("\n"), text);
    }
}
