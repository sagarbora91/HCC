using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.GuestIds;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.GuestIds;

/// <summary>SQL adapter of <see cref="IGuestIdStore"/> over <c>gst.guest_id_secret</c> (0601). Only sealed blobs ever reach SQL.</summary>
public sealed class SqlGuestIdStore(SqlConnectionFactory connections) : IGuestIdStore
{
    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The guest ID store needs the SQL command transaction.");

    public async Task<bool> HasNumberAsync(ICommandTransaction tx, long guestId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT COUNT(*) FROM gst.guest_id_secret WHERE guest_id = @g;");
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task UpsertAsync(ICommandTransaction tx, long guestId, SealedGuestId sealedId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sealedId);
        // Locked check-then-write (contracts §4 rule 8): the guest row is already locked by the caller.
        await using var cmd = Sql(tx).Command("""
            IF EXISTS (SELECT 1 FROM gst.guest_id_secret WITH (UPDLOCK, HOLDLOCK) WHERE guest_id = @g)
              UPDATE gst.guest_id_secret SET key_id = @k, sealed = @s, updated_utc = @utc WHERE guest_id = @g;
            ELSE
              INSERT gst.guest_id_secret(guest_id, key_id, sealed, created_utc, updated_utc) VALUES (@g, @k, @s, @utc, @utc);
            """);
        cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 40).Value = sealedId.KeyId;
        cmd.Parameters.Add("@s", SqlDbType.VarBinary, 64).Value = sealedId.Sealed;
        cmd.Parameters.Add("@utc", SqlDbType.DateTime2).Value = utc;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private const string ReadSql = """
        SELECT g.id_type, g.id_last4, s.key_id, s.sealed
        FROM gst.guest g LEFT JOIN gst.guest_id_secret s ON s.guest_id = g.guest_id
        WHERE g.guest_id = @g AND g.property_id = @p;
        """;

    public async Task<(string? IdType, string? Last4, SealedGuestId? Sealed)?> ReadAsync(ICommandTransaction? tx, int propertyId, long guestId, CancellationToken ct = default)
    {
        SqlConnection? own = null;
        try
        {
            SqlCommand cmd;
            if (tx is not null) cmd = Sql(tx).Command(ReadSql);
            else
            {
                own = await connections.OpenAsync(ct).ConfigureAwait(false);
                cmd = connections.Command(ReadSql, own);
            }

            await using (cmd)
            {
                cmd.Parameters.Add("@g", SqlDbType.BigInt).Value = guestId;
                cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
                var type = rd.IsDBNull(0) ? null : rd.GetString(0);
                var last4 = rd.IsDBNull(1) ? null : rd.GetString(1);
                var sealedId = rd.IsDBNull(2) ? null : new SealedGuestId(rd.GetString(2), (byte[])rd[3]);
                return (type, last4, sealedId);
            }
        }
        finally
        {
            if (own is not null) await own.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<(long GuestId, SealedGuestId Sealed)>> LockStaleAsync(ICommandTransaction tx, int propertyId, string currentKeyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT s.guest_id, s.key_id, s.sealed FROM gst.guest_id_secret s WITH (UPDLOCK, HOLDLOCK)
            JOIN gst.guest g ON g.guest_id = s.guest_id
            WHERE g.property_id = @p AND s.key_id <> @k ORDER BY s.guest_id;
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 40).Value = currentKeyId;
        var list = new List<(long, SealedGuestId)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((rd.GetInt64(0), new SealedGuestId(rd.GetString(1), (byte[])rd[2])));
        return list;
    }
}
