using System.Globalization;
using System.IO.Compression;
using System.Text;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Text;

namespace Hcc.Infrastructure.SqlServer.Reporting;

/// <summary>
/// ACC-3 exports (contract §5): the same row model as the prototype's <c>accounting-exports.ts</c> — a title block, a bold header row and the body — rendered
/// as an Excel sheet (<see cref="OpenXmlExporter"/>) or as CSV (CRLF, <see cref="CsvFormula.Neutralise"/> on every text cell), plus the bulk per-party
/// statements zip. File names <c>&lt;property code&gt;-&lt;report&gt;-&lt;from&gt;-&lt;to&gt;.&lt;ext&gt;</c>.
/// </summary>
public static class StatementExports
{
    /// <summary>A table: sheet name, rows (cells as for <see cref="XlsxSheet"/>), rows to freeze, widths.</summary>
    public sealed record Table(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows, int Freeze, IReadOnlyList<double>? Widths = null)
    {
        public XlsxSheet Sheet => new(Name, Rows, Freeze, Widths);
    }

    public static string FileName(string prop, string report, DateOnly from, DateOnly to, string ext)
    {
        var parts = new List<string> { Clean(prop, "HCC"), Clean(report, "report"), Iso(from) };
        if (to != from) parts.Add(Iso(to));
        return string.Join('-', parts) + "." + ext.TrimStart('.');
    }

    /// <summary>The property code that prefixes file names: the letters and digits of the property name, ≤ 12; "HCC" when empty.</summary>
    public static string PropertyCode(string? propertyName)
    {
        var c = new string((propertyName ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());
        return c.Length == 0 ? "HCC" : c.ToUpperInvariant()[..Math.Min(12, c.Length)];
    }

    public static Table StatementTable(string prop, StatementView st)
    {
        static object?[] Bal(long n) => [n == 0 ? new XlsxMoney(0) : new XlsxMoney(Math.Abs(n)), n > 0 ? "Dr" : n < 0 ? "Cr" : ""];
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { new XlsxBold($"Ledger statement · {st.Name} · {Iso(st.From)} to {Iso(st.To)}") },
            new object?[] { "Property", prop },
            new object?[] { "From", st.From },
            new object?[] { "To", st.To },
            new object?[] { "Note", "Ledger " + st.Account },
            Array.Empty<object?>(),
            new object?[] { H("Date"), H("Voucher No"), H("Voucher Type"), H("Particulars"), H("Narration"), H("Debit"), H("Credit"), H("Balance"), H("Dr/Cr") },
            Row(st.From, "", "", "Opening balance", "", "", "", Bal(st.OpeningPaise)),
        };
        foreach (var r in st.Rows)
            rows.Add(Row(r.Date, r.No, r.Type, r.Counter, r.Narration, r.DebitPaise != 0 ? new XlsxMoney(r.DebitPaise) : "", r.CreditPaise != 0 ? new XlsxMoney(r.CreditPaise) : "", Bal(r.RunningPaise)));
        rows.Add(["", "", "", "Total for the period", "", new XlsxMoney(st.DebitPaise), new XlsxMoney(st.CreditPaise), "", ""]);
        rows.Add(Row(st.To, "", "", "Closing balance", "", "", "", Bal(st.ClosingPaise)));
        return new Table(st.Name, rows, 7, [12, 20, 14, 34, 40, 14, 14, 16, 6]);
    }

    private static XlsxBold H(string t) => new(t);

    private static object?[] Row(object? a, object? b, object? c, object? d, object? e, object? f, object? g, object?[] balance) => [a, b, c, d, e, f, g, .. balance];

    public static byte[] Xlsx(IEnumerable<Table> tables) => OpenXmlExporter.Build([.. tables.Select(t => t.Sheet)]);

    /// <summary>CSV of one table: text cells formula-guarded, money with two decimals, dates ISO.</summary>
    public static string Csv(Table t) => string.Join("\r\n", t.Rows.Select(r => string.Join(',', r.Select(c => VoucherExport.CsvCell(CellText(c))))));

    public static string CellText(object? c) => c switch
    {
        null => "",
        string s => s,
        XlsxBold b => b.Text,
        XlsxMoney m => ((decimal)m.Paise / 100m).ToString("0.00", CultureInfo.InvariantCulture),
        DateOnly d => Iso(d),
        XlsxPercent p => (p.Fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
        _ => Convert.ToString(c, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>One file per party statement (CSV or xlsx), parties with neither an opening balance nor a movement left out, a zip.</summary>
    public static (byte[] Bytes, IReadOnlyList<string> Files) StatementsZip(string prop, IEnumerable<StatementView> statements, string format)
    {
        var xlsx = string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);
        var files = new List<string>();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var st in statements)
            {
                if (st.OpeningPaise == 0 && st.Rows.Count == 0) continue;
                var slug = new string(st.Name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
                if (slug.Length > 40) slug = slug[..40].Trim('-');
                var name = FileName(prop, "statement-" + st.Account.Replace('/', '-') + (slug.Length > 0 ? "-" + slug : ""), st.From, st.To, xlsx ? "xlsx" : "csv");
                for (var i = 2; files.Contains(name, StringComparer.OrdinalIgnoreCase); i++) name = Path.GetFileNameWithoutExtension(name) + $" ({i})" + Path.GetExtension(name);
                files.Add(name);
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression); // store-only, as the prototype's zip.ts
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var s = entry.Open();
                var t = StatementTable(prop, st);
                var bytes = xlsx ? Xlsx([t]) : new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(t))).ToArray();
                s.Write(bytes);
            }
        }

        return (ms.ToArray(), files);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Clean(string t, string fallback)
    {
        var c = new string((t ?? "").Select(x => char.IsAsciiLetterOrDigit(x) || x == '.' ? x : '-').ToArray()).Trim('-');
        return c.Length == 0 ? fallback : c;
    }
}
