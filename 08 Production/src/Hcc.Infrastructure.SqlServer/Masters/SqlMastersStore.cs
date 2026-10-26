using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Masters;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Masters;

/// <summary>Shared SQL of the masters adapters (mst.floor, mst.master_history; migration 2800).</summary>
internal static class MstSql
{
    /// <summary>Floor number of inv.room alias r: hundreds digit of an all-digit number of 3+ characters, else 1 (MastersRules.FloorOfRoom).</summary>
    public const string RoomFloor = "IIF(r.room_no NOT LIKE '%[^0-9]%' AND LEN(r.room_no) >= 3, IIF(CAST(r.room_no AS BIGINT) / 100 < 1, 1, CAST(r.room_no AS BIGINT) / 100), 1)";

    /// <summary>MA-1: floor code of inv.room alias r with mst.floor alias rf left-joined on r.floor_id (hundreds digit when the room has none).</summary>
    public const string RoomFloorCode = $"COALESCE(rf.code, CONCAT('F', {RoomFloor}))";

    public const string FloorColumns = "f.floor_id, f.code, f.name, f.sort_order, f.active, f.retired_on, f.updated_utc, f.row_ver";

    public static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("Masters SQL stores need the SQL command transaction.");

    public static void P(this SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    public static FloorRec Floor(SqlDataReader rd, int o = 0) => new(rd.GetInt32(o), rd.GetString(o + 1), rd.GetString(o + 2), rd.GetInt32(o + 3), rd.GetBoolean(o + 4),
        rd.IsDBNull(o + 5) ? null : DateOnly.FromDateTime(rd.GetDateTime(o + 5)), DateTime.SpecifyKind(rd.GetDateTime(o + 6), DateTimeKind.Utc), (byte[])rd[o + 7]);
}

/// <summary>SQL adapter of <see cref="IMastersStore"/>. Rows are locked with UPDLOCK (HOLDLOCK on the property's set for code uniqueness).</summary>
public sealed class SqlMastersStore : IMastersStore
{
    public async Task<FloorRec?> LockFloorAsync(ICommandTransaction tx, int propertyId, int floorId, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command($"SELECT {MstSql.FloorColumns} FROM mst.floor f WITH (UPDLOCK, ROWLOCK) WHERE f.property_id = @p AND f.floor_id = @f;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@f", SqlDbType.Int, floorId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? MstSql.Floor(rd) : null;
    }

    public async Task<IReadOnlyList<FloorRec>> LockFloorsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command($"SELECT {MstSql.FloorColumns} FROM mst.floor f WITH (UPDLOCK, HOLDLOCK) WHERE f.property_id = @p ORDER BY f.sort_order, f.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        var list = new List<FloorRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(MstSql.Floor(rd));
        return list;
    }

    public async Task<int> InsertFloorAsync(ICommandTransaction tx, int propertyId, string code, string name, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("""
            INSERT mst.floor(property_id, code, name, sort_order, active, updated_by, updated_utc) OUTPUT INSERTED.floor_id VALUES (@p, @c, @n, @s, 1, @u, @utc);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@c", SqlDbType.VarChar, code, 20);
        cmd.P("@n", SqlDbType.NVarChar, name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateFloorAsync(ICommandTransaction tx, int floorId, byte[] expectedVersion, string code, string name, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("UPDATE mst.floor SET code = @c, name = @n, sort_order = @s, updated_by = @u, updated_utc = @utc WHERE floor_id = @f AND row_ver = @v;");
        cmd.P("@c", SqlDbType.VarChar, code, 20);
        cmd.P("@n", SqlDbType.NVarChar, name, 60);
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@f", SqlDbType.Int, floorId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This floor");
    }

    public async Task SetFloorActiveAsync(ICommandTransaction tx, int floorId, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("UPDATE mst.floor SET active = @a, retired_on = @r, updated_by = @u, updated_utc = @utc WHERE floor_id = @f AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@r", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@f", SqlDbType.Int, floorId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This floor");
    }

    public async Task SetFloorSortAsync(ICommandTransaction tx, int floorId, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("UPDATE mst.floor SET sort_order = @s, updated_by = @u, updated_utc = @utc WHERE floor_id = @f;");
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@f", SqlDbType.Int, floorId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteFloorAsync(ICommandTransaction tx, int floorId, byte[] expectedVersion, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("DELETE mst.floor WHERE floor_id = @f AND row_ver = @v;");
        cmd.P("@f", SqlDbType.Int, floorId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This floor");
    }

    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
    }

    public async Task AddHistoryAsync(ICommandTransaction tx, int propertyId, string master, long rowId, string? code, string field, string? oldValue, string? newValue, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command("""
            INSERT mst.master_history(property_id, master, row_id, code, field, old_value, new_value, changed_by, changed_utc) VALUES (@p, @m, @r, @c, @f, @o, @n, @u, @utc);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@m", SqlDbType.VarChar, master, 30);
        cmd.P("@r", SqlDbType.BigInt, rowId);
        cmd.P("@c", SqlDbType.VarChar, code, 20);
        cmd.P("@f", SqlDbType.VarChar, field, 40);
        cmd.P("@o", SqlDbType.NVarChar, Clip(oldValue), 200);
        cmd.P("@n", SqlDbType.NVarChar, Clip(newValue), 200);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string? Clip(string? v) => v is { Length: > 200 } ? v[..200] : v;
}

/// <summary>Read side of the masters module (own connection).</summary>
public sealed class SqlMastersReadStore(SqlConnectionFactory connections) : IMastersReadStore
{
    public async Task<IReadOnlyList<(FloorRec Floor, int Rooms)>> FloorsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {MstSql.FloorColumns}, ISNULL(n.rooms, 0)
              FROM mst.floor f
              LEFT JOIN (SELECT {MstSql.RoomFloorCode} AS code, COUNT(*) AS rooms FROM inv.room r LEFT JOIN mst.floor rf ON rf.floor_id = r.floor_id
                          WHERE r.property_id = @p GROUP BY {MstSql.RoomFloorCode}) n
                ON n.code = f.code
             WHERE f.property_id = @p ORDER BY f.sort_order, f.code;
            """, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var list = new List<(FloorRec, int)>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add((MstSql.Floor(rd), rd.GetInt32(8)));
        return list;
    }

    public async Task<(int Total, IReadOnlyList<MasterHistoryView> Items)> HistoryAsync(int propertyId, string master, long? rowId, int skip, int take, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT COUNT(*) FROM mst.master_history h WHERE h.property_id = @p AND h.master = @m AND (@r IS NULL OR h.row_id = @r);
            SELECT h.history_id, h.row_id, h.code, h.field, h.old_value, h.new_value, h.changed_by, u.display_name, h.changed_utc
              FROM mst.master_history h LEFT JOIN plat.app_user u ON u.user_id = h.changed_by
             WHERE h.property_id = @p AND h.master = @m AND (@r IS NULL OR h.row_id = @r)
             ORDER BY h.history_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@m", SqlDbType.VarChar, master, 30);
        cmd.P("@r", SqlDbType.BigInt, rowId);
        cmd.P("@skip", SqlDbType.Int, skip);
        cmd.P("@take", SqlDbType.Int, take);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var total = rd.GetInt32(0);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var items = new List<MasterHistoryView>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            items.Add(new MasterHistoryView(rd.GetInt64(0), rd.GetInt64(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4),
                rd.IsDBNull(5) ? null : rd.GetString(5), rd.GetInt32(6), rd.IsDBNull(7) ? null : rd.GetString(7),
                DateTime.SpecifyKind(rd.GetDateTime(8), DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)));
        return (total, items);
    }
}

/// <summary>
/// In-use provider of the Floors master: the rooms on the floor — inv.room.floor_id (MA-1, migration 2810), or for a room without one the
/// floor coded F&lt;hundreds digit of its number&gt;.
/// </summary>
public sealed class SqlFloorUsage : IMasterUsage
{
    public IReadOnlyCollection<string> Masters { get; } = [MasterIds.Floors];

    public async Task<MasterUsageCount> CountAsync(ICommandTransaction tx, int propertyId, string master, long rowId, CancellationToken ct = default)
    {
        await using var cmd = MstSql.Sql(tx).Command($"""
            SELECT COUNT(*) FROM inv.room r JOIN mst.floor f ON f.floor_id = @f AND f.property_id = r.property_id LEFT JOIN mst.floor rf ON rf.floor_id = r.floor_id
             WHERE r.property_id = @p AND f.code = {MstSql.RoomFloorCode};
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@f", SqlDbType.Int, (int)rowId);
        var n = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return n == 0 ? MasterUsageCount.None : new MasterUsageCount(n, ["Rooms"]);
    }
}
