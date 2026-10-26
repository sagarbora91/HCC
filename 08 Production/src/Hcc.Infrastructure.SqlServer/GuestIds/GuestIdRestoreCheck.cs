using System.Globalization;
using System.Security.Cryptography;
using Hcc.Application.GuestIds;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.GuestIds;

/// <summary>Outcome of <see cref="GuestIdRestoreCheck"/>: <see cref="Message"/> is the one line printed (never an ID number).</summary>
public sealed record GuestIdCheckResult(GuestIdCheckStatus Status, int Opened, string Message)
{
    /// <summary>True when the check passed or there was nothing to check (a database without sealed ID numbers).</summary>
    public bool Ok => Status != GuestIdCheckStatus.Failed;
}

public enum GuestIdCheckStatus
{
    Ok,
    NoGuestIds,
    Failed,
}

/// <summary>
/// A07-03 / A09-07: proves that the guest ID numbers of a (restored) database open with the key installed on this PC, through the
/// server's own <see cref="GuestIdProtector.Open"/> path. Read-only: for every key id found in <c>gst.guest_id_secret</c> it opens the
/// newest sealed row (so a key still needed by older rows after a rotation is checked too) and throws the plain text away at once.
/// The result names key ids and counts only, never a number or its last 4. Used by <c>Hcc.Api.exe verify-guest-id</c> (restore drill).
/// </summary>
public static class GuestIdRestoreCheck
{
    public const string Prefix = "Guest ID check: ";

    private const string ImportHint = "Install the key from its offline copy on the keys USB stick: New-HccGuestIdKey.ps1 -ImportOfflineCopy <file> (INSTALL.md section 6b).";

    private const string NewestPerKeySql = """
        SELECT x.key_id, x.guest_id, x.property_id, x.sealed, x.n FROM (
          SELECT s.key_id, s.guest_id, g.property_id, s.sealed,
                 COUNT(*) OVER (PARTITION BY s.key_id) AS n,
                 ROW_NUMBER() OVER (PARTITION BY s.key_id ORDER BY s.updated_utc DESC, s.guest_id DESC) AS rn
          FROM gst.guest_id_secret s JOIN gst.guest g ON g.guest_id = s.guest_id) x
        WHERE x.rn = 1 ORDER BY x.key_id;
        """;

    /// <param name="connections">The database to check (the restore drill's scratch copy, or the live database).</param>
    /// <param name="ringFactory">Loads the key ring as the server does; null = no key configured. Called only when sealed rows exist.</param>
    public static async Task<GuestIdCheckResult> RunAsync(SqlConnectionFactory connections, Func<IGuestIdKeyRing?> ringFactory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(ringFactory);
        var newest = new List<(string KeyId, long GuestId, int PropertyId, byte[] Sealed, int Count)>();
        try
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connections.Command(NewestPerKeySql, conn);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                newest.Add((rd.GetString(0), rd.GetInt64(1), rd.GetInt32(2), (byte[])rd[3], rd.GetInt32(4)));
        }
        catch (Exception ex) when (ex is SqlException or HccException)
        {
            var db = new SqlConnectionStringBuilder(connections.Options.ConnectionString).InitialCatalog;
            var detail = ex.InnerException is SqlException inner ? inner.Message : ex.Message;
            return Failed(0, $"the guest ID numbers in database '{db}' could not be read ({detail}).");
        }

        if (newest.Count == 0) return new GuestIdCheckResult(GuestIdCheckStatus.NoGuestIds, 0, Prefix + "no guest IDs to check (the database holds no sealed ID numbers).");
        var total = newest.Sum(r => r.Count);

        IGuestIdKeyRing? ring;
        try
        {
            ring = ringFactory();
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or PlatformNotSupportedException)
        {
            return Failed(0, $"the guest ID key configured on this PC cannot be loaded ({ex.Message}). {ImportHint}");
        }

        if (ring is null)
            return Failed(0, $"the database holds {Count(total)} sealed ID number(s) but no guest ID key is configured on this server (Hcc:GuestIdKeys). {ImportHint}");

        var protector = new GuestIdProtector(ring);
        var problems = new List<string>();
        var opened = 0;
        foreach (var row in newest)
        {
            if (ring.Key(row.KeyId) is null)
            {
                problems.Add($"{Count(row.Count)} ID number(s) are sealed with key '{row.KeyId}', which is not installed on this server.");
                continue;
            }

            try
            {
                // The plain text is discarded at once: only "it opened" is kept.
                _ = protector.Open(row.PropertyId, row.GuestId, new SealedGuestId(row.KeyId, row.Sealed)).Length;
                opened++;
            }
            catch (HccException ex)
            {
                problems.Add(ex.Message);
            }
        }

        if (problems.Count > 0) return Failed(opened, string.Join(" ", problems) + " " + ImportHint);
        var message = opened == 1
            ? Prefix + "OK (1 ID opened)"
            : Prefix + $"OK ({Count(opened)} IDs opened, one per key: {string.Join(", ", newest.Select(r => r.KeyId))})";
        return new GuestIdCheckResult(GuestIdCheckStatus.Ok, opened, message);
    }

    // One line: SQL Server joins several errors with line breaks, and the drill reads the last "Guest ID check:" line.
    private static GuestIdCheckResult Failed(int opened, string why)
        => new(GuestIdCheckStatus.Failed, opened, Prefix + "FAILED - " + string.Join(' ', why.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);
}
