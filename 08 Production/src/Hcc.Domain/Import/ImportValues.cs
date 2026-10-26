using System.Globalization;
using Hcc.Contracts;

namespace Hcc.Domain.Import;

/// <summary>Cell parsers for the import files (owner-friendly formats; strict results).</summary>
public static class ImportValues
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "d.M.yyyy"];

    /// <summary>
    /// Rupees with up to 2 decimals → paise ("4200", "4,200.50", "₹ 1,25,000", "Rs. 99.5"). More decimals are rounded half away from
    /// zero to the paisa (<paramref name="rounded"/> = true, reported as a warning). Negative amounts and more than ₹10 crore are refused.
    /// </summary>
    public static bool TryRupees(string? text, out long paise, out bool rounded, out string? error)
    {
        paise = 0;
        rounded = false;
        error = null;
        var t = (text ?? string.Empty).Trim();
        foreach (var prefix in new[] { "₹", "INR", "Rs.", "Rs", "rs.", "rs" })
            if (t.StartsWith(prefix, StringComparison.Ordinal))
            {
                t = t[prefix.Length..].Trim();
                break;
            }

        t = t.Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (t.Length == 0)
        {
            error = "Enter an amount in rupees, for example 4200 or 4200.50.";
            return false;
        }

        if (t.StartsWith('-') || t.StartsWith('('))
        {
            error = "Amounts cannot be negative here.";
            return false;
        }

        if (!t.All(c => char.IsAsciiDigit(c) || c == '.') || t.Count(c => c == '.') > 1
            || !decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rupees))
        {
            error = $"'{text?.Trim()}' is not an amount in rupees (use digits, an optional decimal point and up to 2 decimals).";
            return false;
        }

        var exact = rupees * 100m;
        if (exact > ApiConventions.MaxPaisePerField)
        {
            error = "The amount is above ₹10 crore.";
            return false;
        }

        var p = decimal.Round(exact, 0, MidpointRounding.AwayFromZero);
        rounded = p != exact;
        paise = (long)p;
        return true;
    }

    /// <summary>YYYY-MM-DD (preferred) or Indian day-first dates (25-10-2026, 25/10/2026).</summary>
    public static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static bool TryInt(string? text, out int value) =>
        int.TryParse((text ?? string.Empty).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    /// <summary>A percentage such as "15", "+15", "-10.5", "12.5%" → basis points, half away from zero.</summary>
    public static bool TryPercentBps(string? text, out int bps)
    {
        bps = 0;
        var t = (text ?? string.Empty).Trim().TrimEnd('%').Trim();
        if (t.StartsWith('+')) t = t[1..];
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var pct)) return false;
        if (pct < -1000 || pct > 1000) return false;
        bps = (int)decimal.Round(pct * 100m, 0, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>A list cell: values separated by ';' or '|' (the comma is the column separator), trimmed, blanks dropped.</summary>
    public static IReadOnlyList<string> List(string? text) =>
        (text ?? string.Empty).Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
