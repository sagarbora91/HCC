// F7-R1 · in-repo PDF writer (RPT-ARCHITECTURE §4.3): PDF 1.4, standard Helvetica / Helvetica-Bold (WinAnsi; ₹ printed as "Rs."), text and rules
// only, uncompressed content streams, deterministic (CreationDate from the run record, no random /ID) so golden text extraction is stable.
// PDFsharp / the ETP PdfReportExporter may replace it later behind IReportRenderer (§10).
using System.Globalization;
using System.Text;

namespace Hcc.Reporting;

public static class MinimalPdfWriter
{
    public const double A4W = 595.28, A4H = 841.89, Margin = 36;

    /// <summary>One positioned text run or a horizontal rule on a page.</summary>
    public sealed record Op(double X, double Y, string? Text, bool Bold = false, double Size = 8, double RuleTo = 0, bool RightAlign = false,
        string? Raw = null); // F12-STUDY-PACK extension point: ready-made path operators (ChartBlock shapes), written as they are

    /// <summary>WinAnsi bytes of a text: ₹ → "Rs.", • and dashes / quotes to their 1252 codes, anything else outside Latin-1 → '?'.</summary>
    public static byte[] WinAnsi(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var o = new List<byte>(text.Length);
        foreach (var ch in text.Replace("₹", "Rs.", StringComparison.Ordinal))
        {
            o.Add(ch switch
            {
                '•' => 0x95, '–' => 0x96, '—' => 0x97, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94, '…' => 0x85,
                _ when ch < 0x80 || (ch >= 0xA0 && ch <= 0xFF) => (byte)ch,
                _ => (byte)'?',
            });
        }

        return [.. o];
    }

    /// <summary>Approximate Helvetica width (points) for right alignment: 0.5 em average, digits 0.556 em.</summary>
    public static double Width(string text, double size) => WinAnsi(text).Sum(b => b is >= (byte)'0' and <= (byte)'9' ? 0.556 : b == (byte)' ' ? 0.278 : 0.5) * size;

    private static string Lit(string text)
    {
        var sb = new StringBuilder("(");
        foreach (var b in WinAnsi(text))
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\') sb.Append('\\').Append((char)b);
            else if (b < 0x20 || b > 0x7E) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }

        return sb.Append(')').ToString();
    }

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static byte[] Write(IReadOnlyList<IReadOnlyList<Op>> pages, bool landscape, string title, DateTimeOffset created)
    {
        ArgumentNullException.ThrowIfNull(pages);
        double w = landscape ? A4H : A4W, h = landscape ? A4W : A4H;
        var objs = new List<string>();
        var n = Math.Max(1, pages.Count);
        // 1 catalog, 2 pages, 3 Helvetica, 4 Helvetica-Bold, 5 info, then page i: 6+2i page, 7+2i contents
        objs.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objs.Add($"<< /Type /Pages /Count {n} /Kids [{string.Join(' ', Enumerable.Range(0, n).Select(i => $"{6 + 2 * i} 0 R"))}] >>");
        objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        objs.Add($"<< /Title {Lit(title)} /Producer (HCC MinimalPdfWriter) /CreationDate (D:{created.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}Z) >>");
        for (var i = 0; i < n; i++)
        {
            var sb = new StringBuilder();
            foreach (var op in i < pages.Count ? pages[i] : [])
            {
                if (op.Raw is { } raw) { sb.Append(raw); continue; } // F12-STUDY-PACK
                if (op.Text is null) { sb.Append(CultureInfo.InvariantCulture, $"0.5 w {N(op.X)} {N(op.Y)} m {N(op.RuleTo)} {N(op.Y)} l S\n"); continue; }
                var x = op.RightAlign ? op.X - Width(op.Text, op.Size) : op.X;
                sb.Append(CultureInfo.InvariantCulture, $"BT /{(op.Bold ? "F2" : "F1")} {N(op.Size)} Tf {N(x)} {N(op.Y)} Td {Lit(op.Text)} Tj ET\n");
            }

            var content = sb.ToString();
            objs.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(w)} {N(h)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {7 + 2 * i} 0 R >>");
            objs.Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
        }

        using var ms = new MemoryStream();
        void Put(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        Put("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objs.Count; i++) { offsets.Add(ms.Position); Put($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = ms.Position;
        Put($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Put($"{o.ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n \n");
        Put($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}

/// <summary>Reads back the text runs and page count of a <see cref="MinimalPdfWriter"/> file (golden checks; tools/pdf-drift.mjs does the same in Node).</summary>
public static class PdfText
{
    public static (int Pages, IReadOnlyList<string> Lines) Extract(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var s = Encoding.Latin1.GetString(pdf);
        var pages = System.Text.RegularExpressions.Regex.Matches(s, "/Type /Page /").Count;
        var lines = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, @"Td \(((?:\\.|[^\\)])*)\) Tj"))
            lines.Add(Unescape(m.Groups[1].Value));
        return (pages, lines);
    }

    private static string Unescape(string t)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] != '\\') { sb.Append(t[i]); continue; }
            var nx = t[++i];
            if (nx is >= '0' and <= '7') { var b = Convert.ToByte(t.Substring(i, 3), 8); i += 2; sb.Append(b == 0x95 ? '•' : b == 0x96 ? '–' : b == 0x97 ? '—' : (char)b); }
            else sb.Append(nx);
        }

        return sb.ToString();
    }
}
