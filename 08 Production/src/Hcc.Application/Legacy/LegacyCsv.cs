using System.Globalization;
using System.Text;
using Hcc.Domain.Errors;

namespace Hcc.Application.Legacy;

/// <summary>
/// A CSV file of the run folder (contract §4: UTF-8, header row, RFC 4180 quoting, '\n' or "\r\n"). Columns are read by name, so the
/// importer does not depend on the column order. Values are trimmed; an empty value is null.
/// </summary>
public sealed class LegacyCsv
{
    private readonly Dictionary<string, int> _index;

    private LegacyCsv(string name, IReadOnlyList<string> header, IReadOnlyList<string[]> rows)
    {
        Name = name;
        Header = header;
        Rows = rows;
        _index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++) _index.TryAdd(header[i], i);
    }

    public string Name { get; }

    public IReadOnlyList<string> Header { get; }

    public IReadOnlyList<string[]> Rows { get; }

    public bool Has(string column) => _index.ContainsKey(column);

    public string? Get(string[] row, string column)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!_index.TryGetValue(column, out var i) || i >= row.Length) return null;
        var v = row[i].Trim();
        return v.Length == 0 ? null : v;
    }

    public string Req(string[] row, string column, int line) =>
        Get(row, column) ?? throw HccException.Validation($"{Name} row {line}: '{column}' is empty.");

    public long? Long(string[] row, string column, int line)
    {
        var v = Get(row, column);
        if (v is null) return null;
        return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : throw HccException.Validation($"{Name} row {line}: '{column}' is not a whole number.");
    }

    public long LongReq(string[] row, string column, int line) => Long(row, column, line) ?? throw HccException.Validation($"{Name} row {line}: '{column}' is empty.");

    public int? Int(string[] row, string column, int line) => Long(row, column, line) is { } x ? checked((int)x) : null;

    public bool Bool(string[] row, string column, int line) => Get(row, column)?.ToLowerInvariant() switch
    {
        null or "0" or "false" or "no" or "n" => false,
        "1" or "true" or "yes" or "y" => true,
        _ => throw HccException.Validation($"{Name} row {line}: '{column}' is not 0 / 1."),
    };

    public DateOnly? Date(string[] row, string column, int line)
    {
        var v = Get(row, column);
        if (v is null) return null;
        return DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
            : throw HccException.Validation($"{Name} row {line}: '{column}' is not a date (yyyy-mm-dd).");
    }

    public DateTime? Utc(string[] row, string column, int line)
    {
        var v = Get(row, column);
        if (v is null) return null;
        return DateTime.TryParseExact(v, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", "yyyy-MM-dd HH:mm:ss"], CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : throw HccException.Validation($"{Name} row {line}: '{column}' is not a UTC time (yyyy-mm-ddTHH:MM:SSZ).");
    }

    /// <summary>Only the header row (for the ID-column check of every CSV in the folder).</summary>
    public static IReadOnlyList<string> ReadHeader(string path)
    {
        using var r = new StreamReader(path, Encoding.UTF8, true);
        var line = r.ReadLine() ?? string.Empty;
        return Split(line + "\n").FirstOrDefault() ?? [];
    }

    public static LegacyCsv Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var rows = Split(text);
        var name = Path.GetFileName(path);
        if (rows.Count == 0) return new LegacyCsv(name, [], []);
        var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
        return new LegacyCsv(name, header, rows.Skip(1).Where(r => !(r.Length == 1 && r[0].Length == 0)).ToList());
    }

    public static LegacyCsv Parse(string name, string text)
    {
        var rows = Split(text);
        if (rows.Count == 0) return new LegacyCsv(name, [], []);
        return new LegacyCsv(name, rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList(), rows.Skip(1).Where(r => !(r.Length == 1 && r[0].Length == 0)).ToList());
    }

    private static List<string[]> Split(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else quoted = false;
                }
                else sb.Append(c);

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    row.Add(sb.ToString());
                    sb.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(sb.ToString());
                    sb.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                    any = false;
                    break;
                default:
                    sb.Append(c);
                    any = true;
                    break;
            }
        }

        if (any || sb.Length > 0)
        {
            row.Add(sb.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }
}
