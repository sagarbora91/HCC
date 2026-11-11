using System.Text.RegularExpressions;

namespace Hcc.Domain.Audit;

/// <summary>
/// CU digits (V-CRM-11 follow-up): keeps raw phone numbers out of audit text and outbox payloads. A run of digits joined by spaces, hyphens, plus signs or
/// parentheses ("+91 (98765) 43-210", "9876543210") holding 10 or more digits is replaced by <c>***</c>; ISO dates (2026-11-11) and shorter numbers
/// (amounts, ids, counts) are left alone. Same idea as the AF2 customer-name mask, but safe to run over a whole sentence.
/// </summary>
public static class PhoneDigitsScrub
{
    private const int MinDigits = 10;

    // A date is matched first so "2026-11-11 2026-11-12" is never read as one 16-digit run.
    private static readonly Regex Pattern = new(@"(?<date>\d{4}-\d{2}-\d{2}(?!\d))|(?<run>[+(]*\d(?:[\s\-()]*\d)*\)?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return Pattern.Replace(text, m => m.Groups["date"].Success || m.Value.Count(char.IsAsciiDigit) < MinDigits ? m.Value : "***");
    }

    /// <summary>True when the text still holds a run of 10 or more digits (used by tests and guards).</summary>
    public static bool HasPhoneDigits(string? text) => !string.IsNullOrEmpty(text) && Scrub(text) != text;
}
