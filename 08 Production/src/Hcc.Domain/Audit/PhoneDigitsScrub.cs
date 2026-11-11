using System.Text.RegularExpressions;

namespace Hcc.Domain.Audit;

/// <summary>
/// CU digits (V-CRM-11 follow-up): keeps raw phone numbers out of audit text and outbox payloads. A run of digits (any script, so Devanagari too) joined
/// by spaces, hyphens, plus signs, parentheses, or a dot / slash sitting directly between digits ("+91 (98765) 43-210", "98765.43210", "9876543210",
/// "०९८२०००५५५५") holding 10 or more digits is replaced by <c>***</c>; ISO dates (2026-11-11) and shorter numbers (amounts, ids, counts) are left alone.
/// <see cref="DigitRun"/> is the ONE digit-run definition: the AF2 customer-name mask (<c>CustomerCommands.SafeName</c>) uses it too, so the two cannot drift.
/// </summary>
public static class PhoneDigitsScrub
{
    private const int MinDigits = 10;

    /// <summary>A run of digits joined by spaces, hyphens, plus signs, parentheses, or a dot / slash directly between digits; never swallows a following ISO date; starts and ends on a digit (leading + and ( and a trailing ) are taken along).</summary>
    public static readonly Regex DigitRun = new(@"[+(]*\d(?:(?:[\s\-+()]*|[./])(?!\d{4}-\d{2}-\d{2}(?!\d))\d)*\)?", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // A date is matched first so "2026-11-11 2026-11-12" is never read as one 16-digit run.
    private static readonly Regex Pattern = new(@"(?<date>\d{4}-\d{2}-\d{2}(?!\d))|(?<run>" + DigitRun.ToString() + ")",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return Pattern.Replace(text, m => m.Groups["date"].Success || m.Value.Count(char.IsDigit) < MinDigits ? m.Value : "***");
    }

    /// <summary>True when the text still holds a run of 10 or more digits (used by tests and guards).</summary>
    public static bool HasPhoneDigits(string? text) => !string.IsNullOrEmpty(text) && Scrub(text) != text;
}
