// F7-R1 · PDF-or-print model (RPT-ARCHITECTURE §4.3) and the one text form of a cell. Same paging and text as the prototype print.ts.
using System.Globalization;
using System.Text;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting;

public sealed record PrintTable(string? Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows);

public sealed record PrintPage(IReadOnlyList<PrintTable> Tables);

public sealed record PrintHeader(string Property, string Title, IReadOnlyList<KeyValuePair<string, string>> Params, DateTimeOffset RunAt, string User);

public sealed record PrintModel(bool Landscape, PrintHeader Header, IReadOnlyList<PrintPage> Pages, string Basis, bool Provisional);

public static class CellText
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <summary>Run time as India time text (yyyy-MM-dd HH:mm IST).</summary>
    public static string RunAt(DateTimeOffset at) => at.ToOffset(Ist).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " IST";

    /// <summary>₹ with Indian grouping; whole rupees without decimals (as the prototype's money()).</summary>
    public static string Rupees(long paise)
    {
        var neg = paise < 0;
        var a = Math.Abs(paise);
        var rupees = (a / 100).ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (rupees.Length > 3)
        {
            var head = rupees[..^3];
            for (var i = 0; i < head.Length; i++)
            {
                sb.Append(head[i]);
                if ((head.Length - i - 1) % 2 == 0 && i < head.Length - 1) sb.Append(',');
            }

            sb.Append(',').Append(rupees[^3..]);
        }
        else sb.Append(rupees);

        if (a % 100 != 0) sb.Append('.').Append((a % 100).ToString("00", CultureInfo.InvariantCulture));
        return (neg ? "-₹" : "₹") + sb;
    }

    public static string Of(ReportColumn c, object? v)
    {
        ArgumentNullException.ThrowIfNull(c);
        return v switch
        {
            null => "",
            string s => s,
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool b => b ? "Yes" : "No",
            _ when c.Type == ReportColumnType.Money => Rupees(Convert.ToInt64(v, CultureInfo.InvariantCulture)),
            _ when c.Type == ReportColumnType.Percent => Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture) + "%",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
        };
    }
}

public static class PrintModelBuilder
{
    public const int RowsPortrait = 40;
    public const int RowsLandscape = 28;

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal) { ["from"] = "From", ["to"] = "To", ["month"] = "Month", ["fy"] = "Financial year", ["asOf"] = "As of" };

    public static PrintModel Build(ReportResult r, string property)
    {
        ArgumentNullException.ThrowIfNull(r);
        var tables = ReportRunner.Tables(r);
        var landscape = tables.Max(t => t.Columns.Count) > 8;
        var per = landscape ? RowsLandscape : RowsPortrait;
        var pages = new List<PrintPage>();
        var cur = new List<PrintTable>();
        var used = 0;
        foreach (var t in tables)
        {
            int i = 0; var placed = false;
            while (!placed || i < t.Rows.Count)
            {
                if (per - used <= 3 && cur.Count > 0) { pages.Add(new PrintPage(cur)); cur = []; used = 0; }
                var chunk = t.Rows.Skip(i).Take(Math.Max(1, per - used - 1)).ToList();
                cur.Add(new PrintTable(t.Title, t.Columns, chunk));
                used += chunk.Count + 2; i += chunk.Count; placed = true;
                if (i < t.Rows.Count) { pages.Add(new PrintPage(cur)); cur = []; used = 0; }
            }
        }

        if (cur.Count > 0 || pages.Count == 0) pages.Add(new PrintPage(cur));
        var ps = r.Params.Values.Select(kv => new KeyValuePair<string, string>(Labels.GetValueOrDefault(kv.Key, kv.Key), kv.Value)).ToList();
        return new PrintModel(landscape, new PrintHeader(property, r.Title, ps, r.About.RunAt, r.About.User), pages, r.About.DateBasis, r.About.Provisional);
    }

    /// <summary>Plain text (golden harness): one line per row, tab-separated; pages split by form feeds. Same as the prototype printText.</summary>
    public static string Text(PrintModel m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return string.Join("\n\f\n", m.Pages.Select((p, i) => string.Join('\n',
            new[] { $"Page {i + 1} of {m.Pages.Count}" }.Concat(p.Tables.SelectMany(t =>
                new[] { string.Join('\t', t.Columns.Select(c => c.Label)) }.Concat(t.Rows.Select(row => string.Join('\t', t.Columns.Select(c => CellText.Of(c, row.Cells.GetValueOrDefault(c.Key)))))))))));
    }
}
