using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.RoomMasters;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;
using Hcc.Domain.RoomMasters;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Masters;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.RoomMasters;

/// <summary>Shared SQL of the rooms-masters adapters (migration 2810).</summary>
internal static class RmsSql
{
    /// <summary>Floor code of inv.room alias r with mst.floor alias f left-joined on floor_id: the floor's code, else F&lt;hundreds digit&gt;.</summary>
    public const string FloorCode = $"COALESCE(f.code, CONCAT('F', {MstSql.RoomFloor}))";

    public const string TypeColumns = """
        t.room_type_id, t.code, COALESCE(t.name, t.code), COALESCE(t.max_adults, t.max_occupancy - t.max_children), t.max_children, t.extra_bed_allowed, t.bed_type,
        t.size_sqft, t.amenities_json, t.description, t.sort_order, t.active, t.retired_on, t.rack_rate_paise, COALESCE(t.updated_utc, '2026-01-01'), t.row_ver
        """;

    public const string RoomColumns = $"""
        r.room_id, r.room_no, r.floor_id, {FloorCode}, r.room_type_id, t.code, r.features_json, r.smoking, r.connecting_room_no, r.extension, r.notes, r.active, r.retired_on,
        r.retire_block_id, r.row_ver
        """;

    public const string RoomFrom = "inv.room r JOIN inv.room_type t ON t.room_type_id = r.room_type_id LEFT JOIN mst.floor f ON f.floor_id = r.floor_id";

    public const string AmenityColumns = "a.amenity_id, a.code, a.name, a.icon, a.show_on_website, a.sort_order, a.active, a.retired_on, a.row_ver";

    public const string OccColumns = "o.charge_id, o.code, o.name, o.room_type_id, t.code, o.child_age_from, o.child_age_to, o.sort_order, o.active, o.retired_on, o.row_ver";

    public static DateOnly? Date(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : DateOnly.FromDateTime(rd.GetDateTime(i));

    public static string? Str(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    public static RoomTypeRec Type(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), Convert.ToInt32(rd.GetValue(3), CultureInfo.InvariantCulture),
        rd.GetByte(4), rd.GetBoolean(5), Str(rd, 6), rd.IsDBNull(7) ? null : rd.GetInt32(7), RoomMastersRules.CodesFromJson(rd.GetString(8)), Str(rd, 9), rd.GetInt32(10),
        rd.GetBoolean(11), Date(rd, 12), rd.GetInt64(13), DateTime.SpecifyKind(rd.GetDateTime(14), DateTimeKind.Utc), (byte[])rd[15]);

    public static RoomRec Room(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetInt32(2), rd.GetString(3), rd.GetInt32(4), rd.GetString(5),
        RoomMastersRules.CodesFromJson(rd.GetString(6)), rd.GetBoolean(7), Str(rd, 8), Str(rd, 9), Str(rd, 10), rd.GetBoolean(11), Date(rd, 12), rd.IsDBNull(13) ? null : rd.GetInt64(13), (byte[])rd[14]);

    public static AmenityRec Amenity(SqlDataReader rd) => new(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), Str(rd, 3), rd.GetBoolean(4), rd.GetInt32(5), rd.GetBoolean(6), Date(rd, 7), (byte[])rd[8]);

    public static async Task<IReadOnlyList<OccupancyRec>> OccupancyAsync(SqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<(int Id, string Code, string Name, int Type, string TypeCode, int? From, int? To, int Sort, bool Active, DateOnly? Retired, byte[] Ver)>();
        var rates = new Dictionary<int, Dictionary<string, List<(DateOnly, long)>>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            rows.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetInt32(3), rd.GetString(4), rd.IsDBNull(5) ? null : rd.GetByte(5), rd.IsDBNull(6) ? null : rd.GetByte(6),
                rd.GetInt32(7), rd.GetBoolean(8), Date(rd, 9), (byte[])rd[10]));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt32(0);
            if (!rates.TryGetValue(id, out var byKind)) rates[id] = byKind = [];
            var kind = rd.GetString(1);
            if (!byKind.TryGetValue(kind, out var l)) byKind[kind] = l = [];
            l.Add((DateOnly.FromDateTime(rd.GetDateTime(2)), rd.GetInt64(3)));
        }

        return rows.Select(r => new OccupancyRec(r.Id, r.Code, r.Name, r.Type, r.TypeCode, r.From, r.To, r.Sort, r.Active, r.Retired, r.Ver,
            (rates.GetValueOrDefault(r.Id) ?? []).ToDictionary(kv => kv.Key, kv => (IReadOnlyList<(DateOnly From, long Paise)>)kv.Value.OrderBy(x => x.Item1).ToList()))).ToList();
    }

    public static string OccSql(string hint) => $"""
        SELECT {OccColumns} FROM inv.occupancy_charge o {hint} JOIN inv.room_type t ON t.room_type_id = o.room_type_id WHERE o.property_id = @p ORDER BY o.sort_order, o.code;
        SELECT x.charge_id, x.kind, x.effective_from, x.amount_paise FROM inv.occupancy_charge_rate x JOIN inv.occupancy_charge o ON o.charge_id = x.charge_id WHERE o.property_id = @p;
        """;
}

/// <summary>SQL adapter of <see cref="IRoomMastersStore"/> (inside the command transaction).</summary>
public sealed class SqlRoomMastersStore(SqlConnectionFactory connections) : IRoomMastersStore
{
    private static SqlCommand Cmd(ICommandTransaction tx, string sql) => MstSql.Sql(tx).Command(sql);

    private static async Task<List<T>> ListAsync<T>(SqlCommand cmd, Func<SqlDataReader, T> read, CancellationToken ct)
    {
        var list = new List<T>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(read(rd));
        return list;
    }

    // ── Room types ──
    public async Task<IReadOnlyList<RoomTypeRec>> LockRoomTypesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, $"SELECT {RmsSql.TypeColumns} FROM inv.room_type t WITH (UPDLOCK, HOLDLOCK) WHERE t.property_id = @p ORDER BY t.sort_order, t.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await ListAsync(cmd, RmsSql.Type, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(DateOnly From, long Paise)>> TariffsAsync(ICommandTransaction tx, int roomTypeId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, "SELECT effective_from, rate_paise FROM inv.room_type_tariff WITH (UPDLOCK, HOLDLOCK) WHERE room_type_id = @t ORDER BY effective_from;");
        cmd.P("@t", SqlDbType.Int, roomTypeId);
        return await ListAsync(cmd, rd => (DateOnly.FromDateTime(rd.GetDateTime(0)), rd.GetInt64(1)), ct).ConfigureAwait(false);
    }

    private static void BindType(SqlCommand cmd, RoomTypeRec t, int principalId, DateTime utc)
    {
        cmd.P("@c", SqlDbType.VarChar, t.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, t.Name, 60);
        cmd.P("@o", SqlDbType.TinyInt, (byte)(t.MaxAdults + t.MaxChildren));
        cmd.P("@a", SqlDbType.TinyInt, (byte)t.MaxAdults);
        cmd.P("@k", SqlDbType.TinyInt, (byte)t.MaxChildren);
        cmd.P("@x", SqlDbType.Bit, t.ExtraBedAllowed);
        cmd.P("@b", SqlDbType.NVarChar, t.BedType, 20);
        cmd.P("@z", SqlDbType.Int, t.SizeSqft);
        cmd.P("@am", SqlDbType.NVarChar, RoomMastersRules.CodesJson(t.Amenities), 1000);
        cmd.P("@d", SqlDbType.NVarChar, t.Description, 500);
        cmd.P("@s", SqlDbType.Int, t.SortOrder);
        cmd.P("@r", SqlDbType.BigInt, t.RackPaise);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
    }

    public async Task<int> InsertRoomTypeAsync(ICommandTransaction tx, int propertyId, RoomTypeRec row, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            INSERT inv.room_type(property_id, code, name, max_occupancy, max_adults, max_children, extra_bed_allowed, bed_type, size_sqft, amenities_json, description,
                                 sort_order, rack_rate_paise, active, updated_by, updated_utc)
            OUTPUT INSERTED.room_type_id VALUES (@p, @c, @n, @o, @a, @k, @x, @b, @z, @am, @d, @s, @r, 1, @u, @utc);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        BindType(cmd, row, principalId, utc);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateRoomTypeAsync(ICommandTransaction tx, RoomTypeRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            UPDATE inv.room_type SET code = @c, name = @n, max_occupancy = @o, max_adults = @a, max_children = @k, extra_bed_allowed = @x, bed_type = @b, size_sqft = @z,
                   amenities_json = @am, description = @d, sort_order = @s, rack_rate_paise = @r, updated_by = @u, updated_utc = @utc
             WHERE room_type_id = @id AND row_ver = @v;
            """);
        BindType(cmd, row, principalId, utc);
        cmd.P("@id", SqlDbType.Int, row.RoomTypeId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This room type");
    }

    public async Task ReplaceTariffsFromAsync(ICommandTransaction tx, int roomTypeId, DateOnly from, IReadOnlyList<(DateOnly From, long Paise)> rates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rates);
        await using (var del = Cmd(tx, "DELETE inv.room_type_tariff WHERE room_type_id = @t AND effective_from >= @f;"))
        {
            del.P("@t", SqlDbType.Int, roomTypeId);
            del.P("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var (d, paise) in rates)
        {
            await using var ins = Cmd(tx, "INSERT inv.room_type_tariff(room_type_id, effective_from, rate_paise) VALUES (@t, @f, @r);");
            ins.P("@t", SqlDbType.Int, roomTypeId);
            ins.P("@f", SqlDbType.Date, d.ToDateTime(TimeOnly.MinValue));
            ins.P("@r", SqlDbType.BigInt, paise);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<RoomTypeUsage> RoomTypeUsageAsync(ICommandTransaction tx, int roomTypeId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT (SELECT COUNT(*) FROM inv.room WHERE room_type_id = @t), (SELECT COUNT(*) FROM inv.room WHERE room_type_id = @t AND active = 1),
                   (SELECT COUNT(*) FROM inv.reservation WHERE room_type_id = @t);
            """);
        cmd.P("@t", SqlDbType.Int, roomTypeId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return new RoomTypeUsage(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2));
    }

    // ── Amenities ──
    public async Task<IReadOnlyList<AmenityRec>> LockAmenitiesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, $"SELECT {RmsSql.AmenityColumns} FROM inv.amenity a WITH (UPDLOCK, HOLDLOCK) WHERE a.property_id = @p ORDER BY a.sort_order, a.code;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await ListAsync(cmd, RmsSql.Amenity, ct).ConfigureAwait(false);
    }

    private static void BindAmenity(SqlCommand cmd, AmenityRec a, int principalId, DateTime utc)
    {
        cmd.P("@c", SqlDbType.VarChar, a.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, a.Name, 60);
        cmd.P("@i", SqlDbType.VarChar, a.Icon, 30);
        cmd.P("@w", SqlDbType.Bit, a.ShowOnWebsite);
        cmd.P("@s", SqlDbType.Int, a.SortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
    }

    public async Task<int> InsertAmenityAsync(ICommandTransaction tx, int propertyId, AmenityRec row, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            INSERT inv.amenity(property_id, code, name, icon, show_on_website, sort_order, active, updated_by, updated_utc) OUTPUT INSERTED.amenity_id
            VALUES (@p, @c, @n, @i, @w, @s, 1, @u, @utc);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        BindAmenity(cmd, row, principalId, utc);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateAmenityAsync(ICommandTransaction tx, AmenityRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var cmd = Cmd(tx, """
            UPDATE inv.amenity SET code = @c, name = @n, icon = @i, show_on_website = @w, sort_order = @s, updated_by = @u, updated_utc = @utc
             WHERE amenity_id = @id AND row_ver = @v;
            """);
        BindAmenity(cmd, row, principalId, utc);
        cmd.P("@id", SqlDbType.Int, row.AmenityId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This amenity");
    }

    public async Task<int> AmenityUsageAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT (SELECT COUNT(*) FROM inv.room_type WHERE property_id = @p AND amenities_json LIKE @k)
                 + (SELECT COUNT(*) FROM inv.room WHERE property_id = @p AND features_json LIKE @k);
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@k", SqlDbType.NVarChar, "%\"" + code + "\"%", 40);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    // ── Occupancy and extra charges ──
    public async Task<IReadOnlyList<OccupancyRec>> LockOccupancyChargesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, RmsSql.OccSql("WITH (UPDLOCK, HOLDLOCK)"));
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await RmsSql.OccupancyAsync(cmd, ct).ConfigureAwait(false);
    }

    private static void BindOcc(SqlCommand cmd, OccupancyRec o, int principalId, DateTime utc)
    {
        cmd.P("@c", SqlDbType.VarChar, o.Code, 20);
        cmd.P("@n", SqlDbType.NVarChar, o.Name, 60);
        cmd.P("@t", SqlDbType.Int, o.RoomTypeId);
        cmd.P("@af", SqlDbType.TinyInt, o.ChildAgeFrom is { } f ? (byte)f : null);
        cmd.P("@at", SqlDbType.TinyInt, o.ChildAgeTo is { } t ? (byte)t : null);
        cmd.P("@s", SqlDbType.Int, o.SortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
    }

    private static async Task RatesAsync(ICommandTransaction tx, int chargeId, OccupancyRec o, CancellationToken ct)
    {
        await using (var del = Cmd(tx, "DELETE inv.occupancy_charge_rate WHERE charge_id = @id;"))
        {
            del.P("@id", SqlDbType.Int, chargeId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var (kind, list) in o.Rates)
            foreach (var (d, paise) in list)
            {
                await using var ins = Cmd(tx, "INSERT inv.occupancy_charge_rate(charge_id, kind, effective_from, amount_paise) VALUES (@id, @k, @f, @a);");
                ins.P("@id", SqlDbType.Int, chargeId);
                ins.P("@k", SqlDbType.VarChar, kind, 5);
                ins.P("@f", SqlDbType.Date, d.ToDateTime(TimeOnly.MinValue));
                ins.P("@a", SqlDbType.BigInt, paise);
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
    }

    public async Task<int> InsertOccupancyChargeAsync(ICommandTransaction tx, int propertyId, OccupancyRec row, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        int id;
        await using (var cmd = Cmd(tx, """
            INSERT inv.occupancy_charge(property_id, code, name, room_type_id, child_age_from, child_age_to, sort_order, active, updated_by, updated_utc)
            OUTPUT INSERTED.charge_id VALUES (@p, @c, @n, @t, @af, @at, @s, 1, @u, @utc);
            """))
        {
            cmd.P("@p", SqlDbType.Int, propertyId);
            BindOcc(cmd, row, principalId, utc);
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        await RatesAsync(tx, id, row, ct).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateOccupancyChargeAsync(ICommandTransaction tx, OccupancyRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using (var cmd = Cmd(tx, """
            UPDATE inv.occupancy_charge SET code = @c, name = @n, room_type_id = @t, child_age_from = @af, child_age_to = @at, sort_order = @s, updated_by = @u, updated_utc = @utc
             WHERE charge_id = @id AND row_ver = @v;
            """))
        {
            BindOcc(cmd, row, principalId, utc);
            cmd.P("@id", SqlDbType.Int, row.ChargeId);
            cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
            SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This occupancy charge");
        }

        await RatesAsync(tx, row.ChargeId, row, ct).ConfigureAwait(false);
    }

    // ── Rooms ──
    public async Task<IReadOnlyList<RoomRec>> LockRoomsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, $"SELECT {RmsSql.RoomColumns} FROM inv.room r WITH (UPDLOCK, HOLDLOCK) JOIN inv.room_type t ON t.room_type_id = r.room_type_id LEFT JOIN mst.floor f ON f.floor_id = r.floor_id WHERE r.property_id = @p ORDER BY r.room_no;");
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await ListAsync(cmd, RmsSql.Room, ct).ConfigureAwait(false);
    }

    public async Task<int> InsertRoomAsync(ICommandTransaction tx, int propertyId, string roomNo, int floorId, int roomTypeId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        _ = principalId;
        _ = utc;
        await using var cmd = Cmd(tx, """
            DECLARE @n TABLE(night DATE PRIMARY KEY);
            INSERT @n SELECT night FROM inv.type_night_count WITH (UPDLOCK, HOLDLOCK) WHERE room_type_id = @t;
            INSERT inv.room(property_id, room_no, room_type_id, hk_status, floor_id) VALUES (@p, @r, @t, 'Ready', @f);
            DECLARE @id INT = CAST(SCOPE_IDENTITY() AS INT);
            UPDATE inv.type_night_count SET sellable = sellable + 1 WHERE room_type_id = @t AND night IN (SELECT night FROM @n);
            SELECT @id;
            """);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@t", SqlDbType.Int, roomTypeId);
        cmd.P("@r", SqlDbType.VarChar, roomNo, 10);
        cmd.P("@f", SqlDbType.Int, floorId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task UpdateRoomAsync(ICommandTransaction tx, RoomRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        _ = principalId;
        _ = utc;
        await using var cmd = Cmd(tx, """
            UPDATE inv.room SET room_no = @r, floor_id = @f, room_type_id = @t, features_json = @fe, smoking = @sm, connecting_room_no = @cn, extension = @x, notes = @no
             WHERE room_id = @id AND row_ver = @v;
            """);
        cmd.P("@r", SqlDbType.VarChar, row.RoomNo, 10);
        cmd.P("@f", SqlDbType.Int, row.FloorId);
        cmd.P("@t", SqlDbType.Int, row.RoomTypeId);
        cmd.P("@fe", SqlDbType.NVarChar, RoomMastersRules.CodesJson(row.Features), 1000);
        cmd.P("@sm", SqlDbType.Bit, row.Smoking);
        cmd.P("@cn", SqlDbType.VarChar, row.ConnectingRoomNo, 10);
        cmd.P("@x", SqlDbType.VarChar, row.Extension, 6);
        cmd.P("@no", SqlDbType.NVarChar, row.Notes, 200);
        cmd.P("@id", SqlDbType.Int, row.RoomId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This room");
    }

    /// <summary>Count rows of both types from <paramref name="from"/> on are range-locked; the room's unblocked nights move from the old to the
    /// new type (lock order of P2: room type ascending, night ascending — usp_apply_type_night_deltas sorts them).</summary>
    public async Task ChangeRoomTypeAsync(ICommandTransaction tx, int propertyId, int roomId, int oldTypeId, int newTypeId, DateOnly from, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        var deltas = new TypeNightDeltas();
        foreach (var (type, delta) in new[] { (oldTypeId, -1), (newTypeId, +1) }.OrderBy(x => x.Item1))
        {
            await using var q = s.Command("""
                SELECT c.night FROM inv.type_night_count c WITH (UPDLOCK, HOLDLOCK)
                 WHERE c.room_type_id = @t AND c.night >= @from
                   AND NOT EXISTS (SELECT 1 FROM inv.v_room_block_effective b WHERE b.room_id = @room
                                     AND (b.from_night IS NULL OR b.from_night <= c.night) AND (b.to_night IS NULL OR b.to_night > c.night))
                 ORDER BY c.night;
                """);
            q.Parameters.Int("@t", type);
            q.Parameters.Int("@room", roomId);
            q.Parameters.Date("@from", from);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var n = DateOnly.FromDateTime(rd.GetDateTime(0));
                deltas.Add(type, n, n.AddDays(1), sellable: delta);
            }
        }

        await InventorySql.Guard(async () => { await deltas.ApplyAsync(s, propertyId, ct).ConfigureAwait(false); return 0; }).ConfigureAwait(false);
    }

    public async Task<LiveNights> LiveNightsAsync(ICommandTransaction tx, int roomId, DateOnly from, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT rn.night, res.public_no FROM inv.room_night rn WITH (UPDLOCK, HOLDLOCK) JOIN inv.reservation res ON res.reservation_id = rn.reservation_id
             WHERE rn.room_id = @r AND rn.night >= @f ORDER BY rn.night;
            """);
        cmd.P("@r", SqlDbType.Int, roomId);
        cmd.P("@f", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        var rows = await ListAsync(cmd, rd => (Night: DateOnly.FromDateTime(rd.GetDateTime(0)), No: rd.GetString(1)), ct).ConfigureAwait(false);
        return new LiveNights(rows.Count, rows.Count == 0 ? null : rows[^1].Night, rows.Select(x => x.No).Distinct(StringComparer.Ordinal).ToList());
    }

    public async Task SetRoomActiveAsync(ICommandTransaction tx, int roomId, byte[] expectedVersion, bool active, DateOnly? retiredOn, long? retireBlockId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        _ = principalId;
        _ = utc;
        await using var cmd = Cmd(tx, "UPDATE inv.room SET active = @a, retired_on = @d, retire_block_id = @b WHERE room_id = @id AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@d", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@b", SqlDbType.BigInt, retireBlockId);
        cmd.P("@id", SqlDbType.Int, roomId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This room");
    }

    public async Task<int> RoomReferencesAsync(ICommandTransaction tx, int roomId, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            SELECT (SELECT COUNT(*) FROM inv.reservation WHERE room_id = @r) + (SELECT COUNT(*) FROM inv.room_night WHERE room_id = @r)
                 + (SELECT COUNT(*) FROM inv.room_block WHERE room_id = @r) + (SELECT COUNT(*) FROM ops.hk_task WHERE room_id = @r)
                 + (SELECT COUNT(*) FROM ops.room_block WHERE room_id = @r) + (SELECT COUNT(*) FROM gst.stay_segment WHERE room_id = @r);
            """);
        cmd.P("@r", SqlDbType.Int, roomId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task DeleteRoomAsync(ICommandTransaction tx, int propertyId, int roomId, int roomTypeId, byte[] expectedVersion, CancellationToken ct = default)
    {
        await using var cmd = Cmd(tx, """
            DECLARE @n TABLE(night DATE PRIMARY KEY);
            INSERT @n SELECT night FROM inv.type_night_count WITH (UPDLOCK, HOLDLOCK) WHERE room_type_id = @t;
            DELETE inv.room WHERE room_id = @id AND property_id = @p AND row_ver = @v;
            DECLARE @d INT = @@ROWCOUNT;
            IF @d = 1 UPDATE inv.type_night_count SET sellable = sellable - 1 WHERE room_type_id = @t AND night IN (SELECT night FROM @n);
            SELECT @d;
            """);
        cmd.P("@t", SqlDbType.Int, roomTypeId);
        cmd.P("@id", SqlDbType.Int, roomId);
        cmd.P("@p", SqlDbType.Int, propertyId);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        try { SqlValues.EnsureVersionMatched(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture), "This room"); }
        catch (SqlException e) when (e.Number == 547)
        {
            throw MastersRules.Fail(MasterReasons.InUse, "This room is used by other records — retire it instead of deleting.");
        }
    }

    // ── Shared list operations ──
    private static (string Table, string Id) Table(RoomMasterKind list) => list switch
    {
        RoomMasterKind.RoomType => ("inv.room_type", "room_type_id"),
        RoomMasterKind.Amenity => ("inv.amenity", "amenity_id"),
        RoomMasterKind.OccupancyCharge => ("inv.occupancy_charge", "charge_id"),
        _ => throw new ArgumentOutOfRangeException(nameof(list)),
    };

    public async Task SetActiveAsync(ICommandTransaction tx, RoomMasterKind list, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var (table, col) = Table(list);
        await using var cmd = Cmd(tx, $"UPDATE {table} SET active = @a, retired_on = @r, updated_by = @u, updated_utc = @utc WHERE {col} = @id AND row_ver = @v;");
        cmd.P("@a", SqlDbType.Bit, active);
        cmd.P("@r", SqlDbType.Date, retiredOn?.ToDateTime(TimeOnly.MinValue));
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        try { SqlValues.EnsureVersionMatched(await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), "This row"); }
        catch (SqlException e) when (e.Number is 2601 or 2627)
        {
            throw MastersRules.Fail(MasterReasons.Invalid, "Another active row already covers this room type — retire it first.");
        }
    }

    public async Task SetSortAsync(ICommandTransaction tx, RoomMasterKind list, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default)
    {
        var (table, col) = Table(list);
        await using var cmd = Cmd(tx, $"UPDATE {table} SET sort_order = @s, updated_by = @u, updated_utc = @utc WHERE {col} = @id;");
        cmd.P("@s", SqlDbType.Int, sortOrder);
        cmd.P("@u", SqlDbType.Int, principalId);
        cmd.P("@utc", SqlDbType.DateTime2, utc);
        cmd.P("@id", SqlDbType.Int, id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ICommandTransaction tx, RoomMasterKind list, int id, byte[] expectedVersion, CancellationToken ct = default)
    {
        var (table, col) = Table(list);
        var pre = list == RoomMasterKind.RoomType
            ? "DELETE inv.room_type_tariff WHERE room_type_id = @id; DELETE inv.type_night_count WHERE room_type_id = @id;"
            : "";
        await using var cmd = Cmd(tx, $"{pre} DELETE {table} WHERE {col} = @id AND row_ver = @v; SELECT CAST(@@ROWCOUNT AS INT);");
        cmd.P("@id", SqlDbType.Int, id);
        cmd.P("@v", SqlDbType.Binary, expectedVersion, 8);
        try { SqlValues.EnsureVersionMatched(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture), "This row"); }
        catch (SqlException e) when (e.Number == 547)
        {
            throw MastersRules.Fail(MasterReasons.InUse, "This row is used by other records (tariffs, channels, groups, charges) — retire it instead of deleting.");
        }
    }
}

/// <summary>Read side of the rooms masters (own connection).</summary>
public sealed class SqlRoomMastersReadStore(SqlConnectionFactory connections) : IRoomMastersReadStore
{
    public async Task<IReadOnlyList<(RoomTypeRec Type, int Rooms, IReadOnlyList<(DateOnly From, long Paise)> Tariffs)>> RoomTypesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {RmsSql.TypeColumns}, (SELECT COUNT(*) FROM inv.room r WHERE r.room_type_id = t.room_type_id) FROM inv.room_type t WHERE t.property_id = @p ORDER BY t.sort_order, t.code;
            SELECT x.room_type_id, x.effective_from, x.rate_paise FROM inv.room_type_tariff x JOIN inv.room_type t ON t.room_type_id = x.room_type_id WHERE t.property_id = @p ORDER BY x.effective_from;
            """, conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var types = new List<(RoomTypeRec, int)>();
        var tariffs = new Dictionary<int, List<(DateOnly, long)>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) types.Add((RmsSql.Type(rd), rd.GetInt32(16)));
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt32(0);
            if (!tariffs.TryGetValue(id, out var l)) tariffs[id] = l = [];
            l.Add((DateOnly.FromDateTime(rd.GetDateTime(1)), rd.GetInt64(2)));
        }

        return types.Select(x => (x.Item1, x.Item2, (IReadOnlyList<(DateOnly From, long Paise)>)(tariffs.GetValueOrDefault(x.Item1.RoomTypeId) ?? []))).ToList();
    }

    public async Task<IReadOnlyList<RoomRec>> RoomsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {RmsSql.RoomColumns} FROM {RmsSql.RoomFrom} WHERE r.property_id = @p ORDER BY r.room_no;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var list = new List<RoomRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(RmsSql.Room(rd));
        return list;
    }

    public async Task<IReadOnlyList<AmenityRec>> AmenitiesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {RmsSql.AmenityColumns} FROM inv.amenity a WHERE a.property_id = @p ORDER BY a.sort_order, a.code;", conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        var list = new List<AmenityRec>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(RmsSql.Amenity(rd));
        return list;
    }

    public async Task<IReadOnlyList<OccupancyRec>> OccupancyChargesAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command(RmsSql.OccSql(""), conn);
        cmd.P("@p", SqlDbType.Int, propertyId);
        return await RmsSql.OccupancyAsync(cmd, ct).ConfigureAwait(false);
    }
}
