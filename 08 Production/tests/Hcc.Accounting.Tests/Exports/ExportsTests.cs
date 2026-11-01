using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Hcc.Api.Endpoints;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Reporting;

namespace Hcc.Accounting.Tests.Exports;

/// <summary>ACC-3 lane acc3-exports (contract §5): the OpenXML workbook, statement CSV / xlsx, the bulk statements zip and its party selection. No database.</summary>
public sealed class ExportsTests
{
    private static readonly DateOnly From = new(2026, 4, 1), To = new(2026, 4, 30);
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static StatementView Statement(string account, string name, long opening, params StatementRowView[] rows)
    {
        var d = rows.Sum(r => r.DebitPaise);
        var c = rows.Sum(r => r.CreditPaise);
        return new StatementView(account, name, From, To, opening, d, c, opening + d - c, rows);
    }

    private static StatementRowView Row(string no, string narration, long debit, long credit, long running)
        => new(new DateOnly(2026, 4, 10), no, "Sales", narration, "Room revenue", debit, credit, running);

    private static Dictionary<string, XDocument> Parts(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); return XDocument.Load(s); });
    }

    [Fact]
    public void Workbook_has_every_part_well_formed_with_bold_title_money_numbers_and_dates()
    {
        var t = StatementExports.StatementTable("HCC", Statement("1110/CMP-A", "Deccan Agro", 0, Row("SAL/1", "Room", 7080000, 0, 7080000)));
        var parts = Parts(StatementExports.Xlsx([t]));
        foreach (var p in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
            Assert.True(parts.ContainsKey(p), p);
        var sheet = parts["xl/worksheets/sheet1.xml"];
        var title = sheet.Descendants(Ns + "c").First(c => (string)c.Attribute("r")! == "A1");
        Assert.Equal("1", (string)title.Attribute("s")!); // bold
        Assert.Contains("Ledger statement · Deccan Agro · 2026-04-01 to 2026-04-30", title.Value);
        Assert.Equal("3", (string)sheet.Descendants(Ns + "c").First(c => (string)c.Attribute("r")! == "B3").Attribute("s")!); // From = date style
        Assert.Equal("46113", sheet.Descendants(Ns + "c").First(c => (string)c.Attribute("r")! == "B3").Element(Ns + "v")!.Value);
        Assert.Contains(sheet.Descendants(Ns + "c"), c => (string)c.Attribute("s")! == "2" && c.Element(Ns + "v")?.Value == "70800"); // ₹70,800.00 as a number
        Assert.Equal("7", (string)sheet.Descendants(Ns + "pane").Single().Attribute("ySplit")!);
        Assert.Contains("numFmtId=\"4\"", parts["xl/styles.xml"].ToString());
    }

    [Fact]
    public void Sheet_names_follow_excel_rules_and_are_unique()
    {
        var used = new List<string>();
        Assert.Equal("Profit and loss", OpenXmlExporter.SheetName("Profit and loss", used));
        Assert.Equal("Profit and loss 2", OpenXmlExporter.SheetName("profit and loss", used));
        Assert.Equal("History sheet", OpenXmlExporter.SheetName("History", used));
        var n = OpenXmlExporter.SheetName("a:b/c?d*e[f]" + new string('x', 40), used);
        Assert.True(n.Length <= 31);
        Assert.DoesNotContain(n, c => ":\/?*[]".Contains(c, StringComparison.Ordinal));
        Assert.Equal("A", OpenXmlExporter.ColumnName(0));
        Assert.Equal("AA", OpenXmlExporter.ColumnName(26));
    }

    [Fact]
    public void Workbook_bytes_are_stable_for_the_same_input()
    {
        var t = StatementExports.StatementTable("HCC", Statement("1100", "Guest ledger", 100, Row("SAL/1", "x", 5, 0, 105)));
        Assert.Equal(StatementExports.Xlsx([t]), StatementExports.Xlsx([t]));
    }

    [Fact]
    public void Csv_cells_are_formula_guarded_money_has_two_decimals_and_lines_are_crlf()
    {
        var st = Statement("2200/V-1", "=HYPERLINK(\"http://x\")", -50, Row("BIL/1", "+cmd|calc", 0, 125050, -125100));
        var csv = StatementExports.Csv(StatementExports.StatementTable("HCC", st));
        Assert.Contains("\r\n", csv);
        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.Contains("'+cmd|calc", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.Contains("1250.50", csv, StringComparison.Ordinal);
        Assert.Contains("1251.00,Cr", csv, StringComparison.Ordinal);
        Assert.All(csv.Split("\r\n"), l => Assert.False(l.StartsWith('=') || l.StartsWith('@')));
    }

    [Fact]
    public void Bulk_zip_holds_one_file_per_party_with_activity_named_to_the_contract()
    {
        var a = Statement("1110/CMP-A", "Deccan Agro", 0, Row("SAL/1", "Room", 7080000, 0, 7080000));
        var idle = Statement("1110/CMP-B", "Idle", 0);
        var b = Statement("2200/V-1", "Morning Foods", 0, Row("BIL/1", "Veg", 0, 2400000, -2400000));
        var (bytes, files) = StatementExports.StatementsZip("HCC", [a, idle, b], "csv");
        Assert.Equal(2, files.Count);
        Assert.Equal("HCC-statement-1110-CMP-A-Deccan-Agro-2026-04-01-2026-04-30.csv", files[0]);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(files, zip.Entries.Select(e => e.FullName).ToList());
        Assert.All(zip.Entries, e => Assert.Equal(e.Length, e.CompressedLength)); // store-only
        using var r = new StreamReader(zip.Entries[0].Open(), Encoding.UTF8);
        Assert.Contains("Deccan Agro", r.ReadToEnd(), StringComparison.Ordinal);
        var (xb, xf) = StatementExports.StatementsZip("HCC", [a], "xlsx");
        Assert.EndsWith(".xlsx", xf[0], StringComparison.Ordinal);
        using var xz = new ZipArchive(new MemoryStream(xb), ZipArchiveMode.Read);
        Assert.NotEmpty(Parts(Inner(xz.Entries[0])));
    }

    private static byte[] Inner(ZipArchiveEntry e) { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }

    [Theory]
    [InlineData("all", 5)]
    [InlineData("receivable", 3)]
    [InlineData("payable", 1)]
    public void Party_selection_filters_by_control(string which, int count)
    {
        var ids = new[] { "1110/CMP-1", "1115/GOIBIBO", "1120/UD-1", "2200/V-1", "1000/TILL", "4000", "1110" };
        Assert.Equal(count, AccountingExportEndpoints.SelectParties(ids, which).Count);
    }

    [Fact]
    public void Party_selection_refuses_an_unknown_filter()
        => Assert.Throws<HccException>(() => AccountingExportEndpoints.SelectParties(["1110/CMP-1"], "everyone"));

    [Fact]
    public void File_names_and_property_code_follow_the_contract()
    {
        Assert.Equal("HCC-profit-and-loss-2026-04-01-2026-04-30.xlsx", StatementExports.FileName("HCC", "profit-and-loss", From, To, "xlsx"));
        Assert.Equal("HCC-balance-sheet-2026-04-30.csv", StatementExports.FileName("HCC", "balance-sheet", To, To, ".csv"));
        Assert.Equal("HOTELEXAMPLE", StatementExports.PropertyCode("Hotel Example"));
        Assert.Equal("HCC", StatementExports.PropertyCode("  "));
    }
}
