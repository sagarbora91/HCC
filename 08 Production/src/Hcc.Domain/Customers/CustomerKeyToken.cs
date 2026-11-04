using System.Security.Cryptography;
using System.Text;

namespace Hcc.Domain.Customers;

/// <summary>
/// AF-DEC-3 (PR-01): the customer key that leaves the server. The internal key is the mobile number's last 10 digits (or <c>n:</c> + name), so it must never
/// be sent as is; the wire carries <c>ck_</c> + the first 22 characters of the base64url HMAC-SHA256 of the key under a per-property secret.
/// Stored rows (aliases, tags, notes, preferences, legacy rollups) keep the raw key; only the wire changes.
/// </summary>
public static class CustomerKeyToken
{
    public const string Prefix = "ck_";

    private const int BodyLength = 22, TotalLength = 25;

    public static string Make(ReadOnlySpan<byte> secret, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var mac = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(key));
        var b64 = Convert.ToBase64String(mac).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return Prefix + b64[..BodyLength];
    }

    public static bool IsToken(string? value) =>
        value is { Length: TotalLength } && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.AsSpan(Prefix.Length).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") < 0;
}
