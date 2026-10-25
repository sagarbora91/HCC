using System.Text.RegularExpressions;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Sql;

/// <summary>Adapter-boundary conversions between SQL values and domain types.</summary>
public static class SqlValues
{
    /// <summary>BIGINT paise → <see cref="Paise"/> with the ±₹10 crore / safe-integer check (contracts §4 rule 3).</summary>
    public static Paise ReadPaise(SqlDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return Paise.FromWire(reader.GetInt64(ordinal));
    }

    /// <summary>SQL ROWVERSION (8 bytes) → base64 <c>expectedVersion</c> for the wire.</summary>
    public static string RowVersionToWire(byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        if (rowVersion.Length != 8) throw new ArgumentException("A rowversion is 8 bytes.", nameof(rowVersion));
        return Convert.ToBase64String(rowVersion);
    }

    /// <summary>Wire <c>expectedVersion</c> → 8-byte rowversion; malformed values are VALIDATION errors.</summary>
    public static byte[] RowVersionFromWire(string? expectedVersion)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(expectedVersion ?? string.Empty);
        }
        catch (FormatException)
        {
            throw HccException.Validation("expectedVersion is not a valid version token.");
        }

        if (bytes.Length != 8) throw HccException.Validation("expectedVersion is not a valid version token.");
        return bytes;
    }

    /// <summary>Throws STALE_VERSION when a versioned UPDATE affected no rows (spike T04).</summary>
    public static void EnsureVersionMatched(int rowsAffected, string what)
    {
        if (rowsAffected == 0) throw new HccException(HccErrorCode.STALE_VERSION, $"{what} was changed by someone else. Reload and try again.");
    }
}

/// <summary>Splits a T-SQL script into batches on lines consisting only of <c>GO</c> (sqlcmd convention).</summary>
public static partial class SqlBatches
{
    [GeneratedRegex(@"^\s*GO\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GoLine();

    public static IReadOnlyList<string> Split(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return GoLine().Split(script.Replace("\r\n", "\n", StringComparison.Ordinal))
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();
    }
}

/// <summary>Maps SQL Server errors to HCC error codes at the adapter boundary.</summary>
public static class SqlErrorTranslator
{
    public const int DuplicateKey = 2627;
    public const int DuplicateIndexKey = 2601;
    public const int Deadlock = 1205;
    public const int LockTimeout = 1222;

    public static bool IsDuplicateKey(SqlException ex) => ex is not null && (ex.Number == DuplicateKey || ex.Number == DuplicateIndexKey);

    /// <summary>
    /// Translates a SqlException: duplicate keys on exclusive inventory → NOT_AVAILABLE (lost race, spike C1/C4);
    /// deadlock/lock timeout/connection failures → UNAVAILABLE; anything else is rethrown by the caller.
    /// </summary>
    public static HccException? Translate(SqlException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex.Number switch
        {
            DuplicateKey or DuplicateIndexKey => new HccException(HccErrorCode.NOT_AVAILABLE, "Not available: someone else took it first.", null, ex),
            Deadlock or LockTimeout => new HccException(HccErrorCode.UNAVAILABLE, "The database was busy. Try again.", null, ex),
            -2 or 53 or 233 or 10054 or 10060 or 4060 => new HccException(HccErrorCode.UNAVAILABLE, "The database is not reachable.", null, ex),
            _ => null,
        };
    }
}
