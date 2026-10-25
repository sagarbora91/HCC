using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Application.Stays;

/// <summary>Wire <c>expectedVersion</c> (base64 of an 8-byte SQL rowversion) ↔ bytes, for the P3 modules.</summary>
public static class VersionToken
{
    public static byte[] Parse(string? token, string field = "expectedVersion")
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(token ?? string.Empty);
        }
        catch (FormatException)
        {
            throw HccException.Validation($"{field} is not a valid version token.");
        }

        if (bytes.Length != 8) throw HccException.Validation($"{field} is not a valid version token.");
        return bytes;
    }

    public static string ToWire(byte[] version) => Convert.ToBase64String(version);

    /// <summary>STALE_VERSION when the record's current version differs from the client's.</summary>
    public static void EnsureMatches(byte[] current, byte[] expected, string what)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!current.AsSpan().SequenceEqual(expected))
            throw new HccException(HccErrorCode.STALE_VERSION, $"{what} was changed by someone else. Reload and try again.");
    }
}
