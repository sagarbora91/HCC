// Adapted from ETP Etp.Reporting.Reporting/OpenXmlReportExporter.cs (SaagarCC-V6-ETP-Source-and-Report-Engine-605002f, DEC-054; provenance in
// third-party/source-reuse.json, entry ACC3-EXPORTS-OPENXML). CHANGES: ETP builds the workbook with the DocumentFormat.OpenXml package; HCC keeps
// "no new NuGet package" (Directory.Packages.props is minimal), so the same OOXML parts (workbook, styles, worksheet with frozen pane, inline strings,
// numbers as numbers, a #,##0.00 money format, bold title / header cells, per-column widths) are written directly with System.IO.Compression. The
// layout rules are ETP's (title row, period, header row frozen, totals row bold, money as numbers); the one-sheet ETP metadata block is replaced by
// a caller-supplied title block, and the workbook may hold several sheets. Mirrors the prototype's src/app/xlsx.ts cell for cell.
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>Integer paise written as the rupee number with <c>#,##0.00</c>.</summary>
public readonly record struct XlsxMoney(long Paise);

/// <summary>Bold text (title rows, column headers).</summary>
public readonly record struct XlsxBold(string Text);

/// <summary>One worksheet: rows of cells (null / string / long / int / decimal / <see cref="XlsxMoney"/> / <see cref="XlsxBold"/> / <see cref="DateOnly"/> /
/// double as a percent fraction via <see cref="XlsxPercent"/>), the number of rows frozen above the body and optional column widths.</summary>
public sealed record XlsxSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows, int FreezeRows = 0, IReadOnlyList<double>? Widths = null,
    Func<int, string, XlsxSheetParts>? Parts = null); // F12-STUDY-PACK extension point: extra parts (native charts), called with the 1-based sheet index and final name

// F12-STUDY-PACK · extension point types (mirror of the prototype XSheetExt): `Tail` is written after sheetData, `Rels` are the sheet's
// relationships, `Files` are added to the package (an Override when ContentType is set).
public sealed record XlsxRel(string Id, string Type, string Target);

public sealed record XlsxPart(string Name, string Xml, string? ContentType);

public sealed record XlsxSheetParts(string Tail, IReadOnlyList<XlsxRel> Rels, IReadOnlyList<XlsxPart> Files);
// F12-STUDY-PACK end

/// <summary>A fraction (0.494 = 49.4 %) shown with <c>0.0%</c>.</summary>
public readonly record struct XlsxPercent(double Fraction);

public static class OpenXmlExporter
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // cellXfs: 0 text · 1 bold · 2 money #,##0.00 · 3 date · 4 integer · 5 percent
    private const int Text = 0, Bold = 1, Money = 2, Date = 3, Int = 4, Pct = 5;
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Excel sheet name: ≤ 31 chars, none of <c>: \ / ? * [ ]</c>, unique (case-insensitive), never "History".</summary>
    public static string SheetName(string name, ICollection<string> used)
    {
        var b = new string((name ?? "").Select(c => ":\\/?*[]".Contains(c, StringComparison.Ordinal) ? ' ' : c).ToArray()).Trim().Trim('\'').Trim();
        if (b.Length > 31) b = b[..31].Trim().TrimEnd('\'');
        if (b.Length == 0) b = "Sheet";
        if (b.Equals("history", StringComparison.OrdinalIgnoreCase)) b = "History sheet";
        var n = b;
        for (var i = 2; used.Any(u => u.Equals(n, StringComparison.OrdinalIgnoreCase)); i++)
        {
            var suffix = " " + i.ToString(CultureInfo.InvariantCulture);
            n = (b.Length + suffix.Length > 31 ? b[..(31 - suffix.Length)] : b) + suffix;
        }

        used.Add(n);
        return n;
    }

    /// <summary>The workbook as .xlsx bytes (at least one sheet).</summary>
    public static byte[] Build(IReadOnlyList<XlsxSheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        var used = new List<string>();
        var named = (sheets.Count == 0 ? [new XlsxSheet("Sheet1", [])] : sheets).Select(s => (Name: SheetName(s.Name, used), Sheet: s)).ToList();
        var parts = named.Select((s, i) => s.Sheet.Parts?.Invoke(i + 1, s.Name)).ToList(); // F12-STUDY-PACK extension point
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Put(zip, "[Content_Types].xml", w =>
            {
                w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Default(w, "xml", "application/xml");
                Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                for (var i = 1; i <= named.Count; i++) Override(w, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                foreach (var f in parts.SelectMany(p => p?.Files ?? []).Where(f => f.ContentType is not null)) Override(w, "/" + f.Name, f.ContentType!); // F12-STUDY-PACK
                w.WriteEndElement();
            });
            Put(zip, "_rels/.rels", w =>
            {
                w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                Relationship(w, "rId1", Rel + "/officeDocument", "xl/workbook.xml");
                w.WriteEndElement();
            });
            Put(zip, "xl/workbook.xml", w =>
            {
                w.WriteStartElement("workbook", Main);
                w.WriteAttributeString("xmlns", "r", null, Rel);
                w.WriteStartElement("sheets");
                for (var i = 0; i < named.Count; i++)
                {
                    w.WriteStartElement("sheet");
                    w.WriteAttributeString("name", named[i].Name);
                    w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("r", "id", Rel, "rId" + (i + 1).ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                }

                w.WriteEndElement();
                w.WriteEndElement();
            });
            Put(zip, "xl/_rels/workbook.xml.rels", w =>
            {
                w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                for (var i = 1; i <= named.Count; i++) Relationship(w, "rId" + i.ToString(CultureInfo.InvariantCulture), Rel + "/worksheet", $"worksheets/sheet{i}.xml");
                Relationship(w, "rId" + (named.Count + 1).ToString(CultureInfo.InvariantCulture), Rel + "/styles", "styles.xml");
                w.WriteEndElement();
            });
            Put(zip, "xl/styles.xml", WriteStyles);
            for (var i = 0; i < named.Count; i++)
            {
                var sheet = named[i].Sheet;
                var tail = parts[i]?.Tail;
                Put(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, sheet, tail));
            }

            // F12-STUDY-PACK: sheet relationships and extra parts of the extension point
            for (var i = 0; i < named.Count; i++)
            {
                if (parts[i] is not { } p) continue;
                if (p.Rels.Count > 0)
                    Put(zip, $"xl/worksheets/_rels/sheet{i + 1}.xml.rels", w =>
                    {
                        w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                        foreach (var r in p.Rels) Relationship(w, r.Id, r.Type, r.Target);
                        w.WriteEndElement();
                    });
                foreach (var f in p.Files) PutRaw(zip, f.Name, f.Xml);
            }
            // F12-STUDY-PACK end
        }

        return ms.ToArray();
    }

    private static void Put(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero); // byte-stable output
        using var s = entry.Open();
        using var w = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        w.WriteStartDocument(true);
        write(w);
        w.WriteEndDocument();
    }

    // F12-STUDY-PACK: a ready-made XML part (chart / drawing), byte-stable like Put.
    private static void PutRaw(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var s = entry.Open();
        var b = new UTF8Encoding(false).GetBytes(xml);
        s.Write(b, 0, b.Length);
    }

    private static void Default(XmlWriter w, string ext, string type)
    {
        w.WriteStartElement("Default");
        w.WriteAttributeString("Extension", ext);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Override(XmlWriter w, string part, string type)
    {
        w.WriteStartElement("Override");
        w.WriteAttributeString("PartName", part);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Relationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship");
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }

    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", Main);
        w.WriteRaw("<numFmts count=\"2\"><numFmt numFmtId=\"165\" formatCode=\"yyyy\\-mm\\-dd\"/><numFmt numFmtId=\"166\" formatCode=\"0.0%\"/></numFmts>");
        w.WriteRaw("<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>");
        w.WriteRaw("<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>");
        w.WriteRaw("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        w.WriteRaw("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        w.WriteRaw("<cellXfs count=\"6\">"
            + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
            + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
            + "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
            + "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
            + "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
            + "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>");
        w.WriteEndElement();
    }

    private static void WriteSheet(XmlWriter w, XlsxSheet sheet, string? tail = null)
    {
        w.WriteStartElement("worksheet", Main);
        if (sheet.FreezeRows > 0)
        {
            w.WriteStartElement("sheetViews");
            w.WriteStartElement("sheetView");
            w.WriteAttributeString("workbookViewId", "0");
            w.WriteStartElement("pane");
            w.WriteAttributeString("ySplit", sheet.FreezeRows.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("topLeftCell", "A" + (sheet.FreezeRows + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("activePane", "bottomLeft");
            w.WriteAttributeString("state", "frozen");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        if (sheet.Widths is { Count: > 0 })
        {
            w.WriteStartElement("cols");
            for (var i = 0; i < sheet.Widths.Count; i++)
            {
                w.WriteStartElement("col");
                var n = (i + 1).ToString(CultureInfo.InvariantCulture);
                w.WriteAttributeString("min", n);
                w.WriteAttributeString("max", n);
                w.WriteAttributeString("width", sheet.Widths[i].ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        w.WriteStartElement("sheetData");
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            w.WriteStartElement("row");
            w.WriteAttributeString("r", (r + 1).ToString(CultureInfo.InvariantCulture));
            var row = sheet.Rows[r];
            for (var c = 0; c < row.Count; c++) WriteCell(w, row[c], ColumnName(c) + (r + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
        }

        w.WriteEndElement();
        if (tail is { Length: > 0 }) w.WriteRaw(tail); // F12-STUDY-PACK: after sheetData (e.g. <drawing r:id/>)
        w.WriteEndElement();
    }

    private static void WriteCell(XmlWriter w, object? value, string reference)
    {
        switch (value)
        {
            case null: return;
            case string { Length: 0 }: return;
            case string s: Inline(w, reference, s, Text); return;
            case XlsxBold b: if (b.Text.Length > 0) Inline(w, reference, b.Text, Bold); return;
            case XlsxMoney m: Number(w, reference, ((decimal)m.Paise / 100m).ToString(CultureInfo.InvariantCulture), Money); return;
            case DateOnly d: Number(w, reference, (d.DayNumber - new DateOnly(1899, 12, 30).DayNumber).ToString(CultureInfo.InvariantCulture), Date); return;
            case int or long: Number(w, reference, Convert.ToString(value, CultureInfo.InvariantCulture)!, Int); return;
            case decimal dec: Number(w, reference, dec.ToString(CultureInfo.InvariantCulture), Money); return;
            case XlsxPercent p: Number(w, reference, p.Fraction.ToString("R", CultureInfo.InvariantCulture), Pct); return;
            default: Inline(w, reference, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", Text); return;
        }
    }

    private static void Inline(XmlWriter w, string reference, string text, int style)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("r", reference);
        w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("t", "inlineStr");
        w.WriteStartElement("is");
        w.WriteStartElement("t");
        w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString(StripControl(text));
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Number(XmlWriter w, string reference, string value, int style)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("r", reference);
        w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        w.WriteElementString("v", value);
        w.WriteEndElement();
    }

    private static string StripControl(string t) => new(t.Where(c => c >= ' ' || c is '\t' or '\n' or '\r').ToArray());

    public static string ColumnName(int zeroBased)
    {
        var value = zeroBased + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }

        return result;
    }
}
