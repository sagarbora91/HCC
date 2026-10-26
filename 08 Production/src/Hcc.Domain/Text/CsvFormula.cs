using System.Text.RegularExpressions;

namespace Hcc.Domain.Text;

/// <summary>
/// CSV formula injection guard (OWASP "CSV Injection"; review F-17, A06-08). A spreadsheet runs a cell whose text starts with
/// <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return as a formula (<c>=HYPERLINK(…)</c>, DDE <c>=cmd|…</c>). Such text gets a
/// leading apostrophe, which Excel and LibreOffice show as plain text. A plain number (<c>-500</c>, <c>-1,250.50</c>, <c>+15</c>) is not a
/// formula and is left alone, so amounts keep their sign.
/// Used on the way out (voucher CSV) and on the way in (text columns of a masters import, <see cref="Import.ImportCellText"/>).
/// The prototype twin is <c>03 Prototype/src/domain/csv.ts</c> <c>csvSafe</c>.
/// </summary>
public static partial class CsvFormula
{
    /// <summary>First characters that make a spreadsheet treat the cell as a formula.</summary>
    public const string Triggers = "=+-@\t\r";

    [GeneratedRegex(@"^[+-]?\d[\d,]*(\.\d+)?$")]
    private static partial Regex Numeric();

    /// <summary>True when <paramref name="text"/> would run as a formula: it starts with a trigger and is not a plain number.</summary>
    public static bool IsRisky(string? text) =>
        !string.IsNullOrEmpty(text) && Triggers.Contains(text[0], StringComparison.Ordinal) && !Numeric().IsMatch(text);

    /// <summary>The text with a leading apostrophe when it is risky; unchanged otherwise (also when it already starts with the apostrophe).</summary>
    public static string Neutralise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return IsRisky(text) ? "'" + text : text;
    }
}
