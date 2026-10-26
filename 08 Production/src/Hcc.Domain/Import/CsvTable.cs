using System.Text;

namespace Hcc.Domain.Import;

/// <summary>One data record; <see cref="Row"/> is the spreadsheet row number (header = row 1, first data row = 2).</summary>
public sealed record CsvRow(int Row, IReadOnlyList<string> Cells);

/// <summary>A parsed CSV file: header cells and data records (fully blank records skipped).</summary>
public sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<CsvRow> Rows, char Delimiter);

/// <summary>
/// RFC 4180 reader tolerant of what Excel "Save as CSV" writes: UTF-8 with or without BOM, CRLF or LF, quoted cells with doubled
/// quotes and line breaks, and the delimiter of the header line (comma; semicolon or tab when the header has those and no comma).
/// </summary>
public static class CsvParser
{
    public const int MaxChars = 2_000_000;
    public const int MaxRows = 5_000;

    /// <summary>Parses the text; a structural problem (unterminated quote, too large) is returned as <paramref name="error"/>.</summary>
    public static CsvTable? TryParse(string? text, out string? error)
    {
        error = null;
        var s = text ?? string.Empty;
        if (s.Length > 0 && s[0] == '﻿') s = s[1..];
        if (s.Length > MaxChars)
        {
            error = $"The file is too large (at most {MaxChars:N0} characters). Split it into smaller files.";
            return null;
        }

        if (s.Trim().Length == 0)
        {
            error = "The file is empty.";
            return null;
        }

        var delimiter = DetectDelimiter(s);
        var records = new List<(int Line, List<string> Cells)>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;
        var cellStarted = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < s.Length && s[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else quoted = false;
                }
                else
                {
                    if (c == '\n') line++;
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"' && !cellStarted)
            {
                quoted = true;
                cellStarted = true;
            }
            else if (c == delimiter)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                cellStarted = false;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                cells.Add(cell.ToString());
                cell.Clear();
                cellStarted = false;
                records.Add((recordLine, cells));
                cells = [];
                line++;
                recordLine = line;
            }
            else
            {
                cell.Append(c);
                cellStarted = true;
            }
        }

        if (quoted)
        {
            error = $"A quoted cell starting on line {recordLine} is never closed (missing \").";
            return null;
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            records.Add((recordLine, cells));
        }

        records.RemoveAll(r => r.Cells.All(x => x.Trim().Length == 0));
        if (records.Count == 0)
        {
            error = "The file is empty.";
            return null;
        }

        var headers = records[0].Cells.Select(h => h.Trim()).ToList();
        var rows = new List<CsvRow>();
        for (var r = 1; r < records.Count; r++) rows.Add(new CsvRow(records[r].Line, records[r].Cells));
        if (rows.Count > MaxRows)
        {
            error = $"The file has {rows.Count:N0} rows; at most {MaxRows:N0} rows per file.";
            return null;
        }

        return new CsvTable(headers, rows, delimiter);
    }

    private static char DetectDelimiter(string s)
    {
        var end = s.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? s : s[..end];
        if (header.Contains(',', StringComparison.Ordinal)) return ',';
        if (header.Contains(';', StringComparison.Ordinal)) return ';';
        if (header.Contains('\t', StringComparison.Ordinal)) return '\t';
        return ',';
    }

    /// <summary>Header comparison key: lower-case letters and digits only ("Room No" = "roomNo" = "room_no").</summary>
    public static string Key(string? header)
    {
        var sb = new StringBuilder();
        foreach (var c in header ?? string.Empty)
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
