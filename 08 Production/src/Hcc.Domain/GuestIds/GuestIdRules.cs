using System.Text;
using System.Text.RegularExpressions;

namespace Hcc.Domain.GuestIds;

// DEC-068 · full guest ID numbers. Ported from 03 Prototype/src/domain/streams/guests.ts (guestIdKind … guestIdFormat).
// LEGAL RISK (DEC-068): UIDAI regulations expect hotels to keep only a masked Aadhaar; storing the full number can be an offence
// under the Aadhaar Act 2016. Stored by owner decision — CA/lawyer to confirm before production. Format checks only: nothing is
// verified against any registry. The full number must never appear in audit text, logs, outbox events, exports or messages —
// every message below is fixed text and never echoes the input.

public enum GuestIdKind
{
    Aadhaar,
    Passport,
    DrivingLicence,
    VoterId,
    Pan,
    Other,
}

public static partial class GuestIdRules
{
    public const string Restricted = "Restricted";

    /// <summary>ID kind from the free-text type (prototype: prefix match, "(demo)" suffixes allowed).</summary>
    public static GuestIdKind KindOf(string? type)
    {
        var t = (type ?? string.Empty).Trim().ToLowerInvariant();
        return t.StartsWith("aadhaar", StringComparison.Ordinal) ? GuestIdKind.Aadhaar
            : t.StartsWith("passport", StringComparison.Ordinal) ? GuestIdKind.Passport
            : t.StartsWith("driving", StringComparison.Ordinal) ? GuestIdKind.DrivingLicence
            : t.StartsWith("voter", StringComparison.Ordinal) ? GuestIdKind.VoterId
            : t.StartsWith("pan", StringComparison.Ordinal) ? GuestIdKind.Pan
            : GuestIdKind.Other;
    }

    /// <summary>Upper-case, spaces and hyphens removed.</summary>
    public static string Normalise(string? value)
    {
        var sb = new StringBuilder();
        foreach (var c in value ?? string.Empty)
            if (!char.IsWhiteSpace(c) && c != '-') sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    private static readonly int[,] D =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, { 1, 2, 3, 4, 0, 6, 7, 8, 9, 5 }, { 2, 3, 4, 0, 1, 7, 8, 9, 5, 6 }, { 3, 4, 0, 1, 2, 8, 9, 5, 6, 7 },
        { 4, 0, 1, 2, 3, 9, 5, 6, 7, 8 }, { 5, 9, 8, 7, 6, 0, 4, 3, 2, 1 }, { 6, 5, 9, 8, 7, 1, 0, 4, 3, 2 }, { 7, 6, 5, 9, 8, 2, 1, 0, 4, 3 },
        { 8, 7, 6, 5, 9, 3, 2, 1, 0, 4 }, { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 },
    };

    private static readonly int[,] P =
    {
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, { 1, 5, 7, 6, 2, 8, 3, 0, 9, 4 }, { 5, 8, 0, 3, 7, 9, 6, 1, 4, 2 }, { 8, 9, 1, 6, 0, 4, 3, 5, 2, 7 },
        { 9, 4, 5, 3, 1, 2, 6, 8, 7, 0 }, { 4, 2, 8, 6, 5, 7, 3, 9, 0, 1 }, { 2, 7, 9, 3, 8, 0, 6, 4, 1, 5 }, { 7, 0, 4, 6, 9, 1, 3, 2, 5, 8 },
    };

    private static readonly int[] Inv = [0, 4, 3, 2, 1, 5, 6, 7, 8, 9];

    /// <summary>Verhoeff check of a digit string (checksum digit last).</summary>
    public static bool Verhoeff(string digits)
    {
        if (string.IsNullOrEmpty(digits) || !digits.All(char.IsAsciiDigit)) return false;
        var c = 0;
        for (var i = 0; i < digits.Length; i++) c = D[c, P[i % 8, digits[digits.Length - 1 - i] - '0']];
        return c == 0;
    }

    /// <summary>Verhoeff check digit for a digit string without its checksum.</summary>
    public static int VerhoeffDigit(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        var c = 0;
        for (var i = 0; i < digits.Length; i++) c = D[c, P[(i + 1) % 8, digits[digits.Length - 1 - i] - '0']];
        return Inv[c];
    }

    [GeneratedRegex(@"^\d{12}$")] private static partial Regex Aadhaar();
    [GeneratedRegex(@"^[A-Z]\d{7}$")] private static partial Regex Passport();
    [GeneratedRegex(@"^[A-Z]{2}\d{13}$")] private static partial Regex Licence();
    [GeneratedRegex(@"^[A-Z]{3}\d{7}$")] private static partial Regex Voter();
    [GeneratedRegex(@"^[A-Z]{5}\d{4}[A-Z]$")] private static partial Regex Pan();
    [GeneratedRegex(@"^[A-Z0-9]{4,20}$")] private static partial Regex OtherId();

    /// <summary>Error text for a number against its type; empty when valid or blank (prototype <c>guestIdNumberError</c>, same wording).</summary>
    public static string Error(string? type, string? raw)
    {
        var n = Normalise(raw);
        if (n.Length == 0) return string.Empty;
        return KindOf(type) switch
        {
            GuestIdKind.Aadhaar => !Aadhaar().IsMatch(n) ? "Aadhaar must be exactly 12 digits."
                : Verhoeff(n) ? string.Empty : "This Aadhaar number fails the checksum — check the digits.",
            GuestIdKind.Passport => Passport().IsMatch(n) ? string.Empty : "Passport number is 1 letter followed by 7 digits (e.g. Z1234567).",
            GuestIdKind.DrivingLicence => Licence().IsMatch(n) ? string.Empty : "Driving licence is 2 letters, then 13 digits (e.g. MH1220110012345).",
            GuestIdKind.VoterId => Voter().IsMatch(n) ? string.Empty : "Voter ID (EPIC) is 3 letters followed by 7 digits.",
            GuestIdKind.Pan => Pan().IsMatch(n) ? string.Empty : "PAN is 5 letters, 4 digits, 1 letter (e.g. ABCDE1234F).",
            _ => OtherId().IsMatch(n) ? string.Empty : "ID number is 4 to 20 letters or digits.",
        };
    }

    public static string Last4(string? number)
    {
        var n = Normalise(number);
        return n.Length <= 4 ? n : n[^4..];
    }

    /// <summary>Masked form shown everywhere: XXXX-XXXX-1234 (last 4 only); empty when nothing is on file.</summary>
    public static string Mask(string? last4)
    {
        var l = (last4 ?? string.Empty).Trim().ToUpperInvariant();
        return l.Length == 0 ? string.Empty : "XXXX-XXXX-" + (l.Length > 4 ? l[^4..] : l);
    }

    /// <summary>Readable grouping of a full number, only for the deliberate audited reveal (Aadhaar in groups of 4).</summary>
    public static string Format(string? type, string number)
    {
        ArgumentNullException.ThrowIfNull(number);
        if (KindOf(type) != GuestIdKind.Aadhaar || !Aadhaar().IsMatch(number)) return number;
        return string.Join(' ', number[..4], number[4..8], number[8..]);
    }
}
