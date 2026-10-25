using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.WebBooking;

/// <summary>
/// Booking-website rules ported from the prototype (<c>chnWebSearchError</c>, <c>CHN_WEB_REQUEST</c> field checks: name of at least 2
/// characters, phone 7–16 digits, optional e-mail, notes up to 200) plus the production-only public-surface rules (honeypot, caps,
/// deposit share, link tokens).
/// </summary>
public static partial class WebBookingRules
{
    public const int MaxNotes = 200;
    public const int MaxPendingPerPhone = 3;
    public const int DefaultDepositBps = 3000;
    public static readonly TimeSpan DefaultDepositLife = TimeSpan.FromHours(24);

    [GeneratedRegex(@"^\+?[\d\s-]{7,16}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRun();

    public static string Guest(string? name)
    {
        var n = Clean(name, 200);
        if (n.Length < 2) throw HccException.Validation("Enter your name.");
        return n;
    }

    public static string Phone(string? phone)
    {
        var p = (phone ?? "").Trim();
        if (!PhonePattern().IsMatch(p) || p.Count(char.IsDigit) < 7) throw HccException.Validation("Enter a valid phone number (7–16 digits).");
        return p;
    }

    public static string? Email(string? email)
    {
        var e = (email ?? "").Trim();
        if (e.Length == 0) return null;
        if (e.Length > 200 || !EmailPattern().IsMatch(e)) throw HccException.Validation("Enter a valid email address or leave it blank.");
        return e;
    }

    public static string? Notes(string? notes)
    {
        var n = Clean(notes, MaxNotes);
        return n.Length == 0 ? null : n;
    }

    /// <summary>The honeypot input must stay empty; a filled one is an automated form fill.</summary>
    public static void EnsureHuman(string? honeypot)
    {
        if (!string.IsNullOrWhiteSpace(honeypot)) throw HccException.Validation("The request could not be accepted.");
    }

    public static string Reason(string? reason)
    {
        var r = Clean(reason, 200);
        if (r.Length < 3) throw HccException.Validation("Give a reason for declining the request (at least 3 characters).");
        return r;
    }

    /// <summary>Control characters removed, whitespace runs collapsed, clipped to <paramref name="max"/>.</summary>
    public static string Clean(string? text, int max)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? "").Trim()) sb.Append(char.IsControl(ch) ? ' ' : ch);
        var s = WhitespaceRun().Replace(sb.ToString(), " ").Trim();
        return s.Length > max ? s[..max] : s;
    }

    public static string PublicNo(long sequence) => "WEB-" + sequence.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>Last four digits visible, the rest masked, for queue lists.</summary>
    public static string MaskPhone(string phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? new string('•', digits.Length) : new string('•', digits.Length - 4) + digits[^4..];
    }

    /// <summary>Deposit share of the quoted total, half away from zero (paise), at least 1 paisa.</summary>
    public static Paise DefaultDeposit(Paise total, int bps)
    {
        if (bps is < 1 or > 10000) throw HccException.Validation("Deposit share must be between 0.01 % and 100 %.");
        var amount = MoneyMath.PercentOf(total, bps);
        return amount.Value < 1 ? new Paise(1) : amount;
    }

    public static Paise DepositAmount(long? requested, Paise total, int defaultBps)
    {
        if (total.Value <= 0) throw HccException.Validation("There is nothing to take a deposit against.");
        if (requested is null) return DefaultDeposit(total, defaultBps);
        var amount = Paise.FromWire(requested.Value);
        if (amount.Value <= 0) throw HccException.Validation("Deposit must be more than zero.");
        if (amount > total) throw HccException.Validation("Deposit cannot be more than the booking total.");
        return amount;
    }

    /// <summary>A fresh unguessable link token: 32 random bytes, base64url. Only <see cref="TokenHash"/> is stored.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] TokenHash(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 100) throw HccException.Validation("The payment link is not valid.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Server-derived idempotency key for one step of a multi-command flow (80 characters at most).</summary>
    public static string StepKey(string key, string step)
    {
        ArgumentNullException.ThrowIfNull(key);
        var k = key + ":" + step;
        if (k.Length <= 80) return k;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ":" + step;
    }
}
