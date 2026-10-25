using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>
/// SQL adapter for <see cref="IRoomBlockPort"/>. The room type's materialized count rows in the window are range-locked
/// (UPDLOCK, HOLDLOCK — no count row can be created in the window meanwhile), then the room's live room nights are checked
/// under lock, then sellable is adjusted and the block row written. Count rows created later compute sellable from the blocks.
/// </summary>
public sealed class SqlRoomBlockPort(SqlConnectionFactory connections) : IRoomBlockPort
{
    public async Task<long> PlaceAsync(ICommandTransaction tx, int propertyId, int roomId, DateOnly? from, DateOnly? to, string reason, string source,
        long? sourceRef, CancellationToken ct = default)
    {
        if (from is { } f && to is { } t && t <= f) throw HccException.Validation("The block must end after it starts.");
        var r = (reason ?? "").Trim();
        if (r.Length is < 2 or > 200) throw HccException.Validation("Give a reason for the block.");
        if (source is not ("Inventory" or "Maintenance")) throw HccException.Validation("Unknown block source.");
        var s = SqlScope.Of(tx, connections);
        var (type, roomNo) = await RoomAsync(s, propertyId, roomId, ct).ConfigureAwait(false);
        var deltas = await NightsAsync(s, type, roomId, from, to, null, -1, ct).ConfigureAwait(false);

        await using (var q = s.Command("""
            SELECT COUNT(*) FROM inv.room_night rn WITH (UPDLOCK, HOLDLOCK)
            WHERE rn.room_id = @room AND (@from IS NULL OR rn.night >= @from) AND (@to IS NULL OR rn.night < @to)
              AND rn.night >= (SELECT business_date FROM inv.property WHERE property_id = @p);
            """))
        {
            q.Parameters.Int("@room", roomId);
            q.Parameters.NullableDate("@from", from);
            q.Parameters.NullableDate("@to", to);
            q.Parameters.Int("@p", propertyId);
            if ((int)(await q.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0)
                throw new HccException(HccErrorCode.NOT_AVAILABLE, $"Room {roomNo} has bookings in that window. Move them before blocking the room.");
        }

        await InventorySql.Guard(async () => { await deltas.ApplyAsync(s, propertyId, ct).ConfigureAwait(false); return 0; }).ConfigureAwait(false);
        await using var ins = s.Command("""
            INSERT inv.room_block(room_id, from_night, to_night, reason, source, source_ref) OUTPUT INSERTED.room_block_id
            VALUES (@room, @from, @to, @reason, @src, @ref);
            """);
        ins.Parameters.Int("@room", roomId);
        ins.Parameters.NullableDate("@from", from);
        ins.Parameters.NullableDate("@to", to);
        ins.Parameters.Text("@reason", r, 200);
        ins.Parameters.Add("@src", SqlDbType.VarChar, 20).Value = source;
        ins.Parameters.Big("@ref", sourceRef);
        return (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task RemoveAsync(ICommandTransaction tx, int propertyId, long roomBlockId, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        int roomId;
        DateOnly? from, to;
        await using (var q = s.Command("""
            SELECT b.room_id, b.from_night, b.to_night FROM inv.room_block b WITH (UPDLOCK, ROWLOCK) JOIN inv.room r ON r.room_id = b.room_id
            WHERE b.room_block_id = @b AND r.property_id = @p AND b.active = 1;
            """))
        {
            q.Parameters.Big("@b", roomBlockId);
            q.Parameters.Int("@p", propertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw InventoryErrors.NotFound("Room block");
            (roomId, from, to) = (rd.GetInt32(0), rd.GetDateOrNull(1), rd.GetDateOrNull(2));
        }

        var (type, _) = await RoomAsync(s, propertyId, roomId, ct).ConfigureAwait(false);
        var deltas = await NightsAsync(s, type, roomId, from, to, roomBlockId, +1, ct).ConfigureAwait(false);
        await deltas.ApplyAsync(s, propertyId, ct).ConfigureAwait(false);
        await using var up = s.Command("UPDATE inv.room_block SET active = 0 WHERE room_block_id = @b;");
        up.Parameters.Big("@b", roomBlockId);
        await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveBySourceAsync(ICommandTransaction tx, int propertyId, string source, long sourceRef, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        long? id;
        await using (var q = s.Command("""
            SELECT TOP (1) b.room_block_id FROM inv.room_block b WITH (UPDLOCK, ROWLOCK) JOIN inv.room r ON r.room_id = b.room_id
            WHERE b.source = @src AND b.source_ref = @ref AND b.active = 1 AND r.property_id = @p;
            """))
        {
            q.Parameters.Add("@src", SqlDbType.VarChar, 20).Value = source;
            q.Parameters.Big("@ref", sourceRef);
            q.Parameters.Int("@p", propertyId);
            id = await q.ExecuteScalarAsync(ct).ConfigureAwait(false) as long?;
        }

        if (id is { } blockId) await RemoveAsync(tx, propertyId, blockId, ct).ConfigureAwait(false);
    }

    private static async Task<(int Type, string RoomNo)> RoomAsync(SqlScope s, int propertyId, int roomId, CancellationToken ct)
    {
        await using var q = s.Command("SELECT room_type_id, room_no FROM inv.room WHERE room_id = @r AND property_id = @p;");
        q.Parameters.Int("@r", roomId);
        q.Parameters.Int("@p", propertyId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw InventoryErrors.NotFound("Room");
        return (rd.GetInt32(0), rd.GetString(1));
    }

    /// <summary>Range-locks the type's count rows in the window and returns a sellable delta for nights no other active block covers.</summary>
    private static async Task<TypeNightDeltas> NightsAsync(SqlScope s, int type, int roomId, DateOnly? from, DateOnly? to, long? exceptBlock, int delta, CancellationToken ct)
    {
        await using var q = s.Command("""
            SELECT c.night,
              CASE WHEN EXISTS (SELECT 1 FROM inv.room_block b WHERE b.room_id = @room AND b.active = 1 AND (@except IS NULL OR b.room_block_id <> @except)
                AND (b.from_night IS NULL OR b.from_night <= c.night) AND (b.to_night IS NULL OR b.to_night > c.night)) THEN 1 ELSE 0 END
            FROM inv.type_night_count c WITH (UPDLOCK, HOLDLOCK)
            WHERE c.room_type_id = @t AND (@from IS NULL OR c.night >= @from) AND (@to IS NULL OR c.night < @to)
            ORDER BY c.night;
            """);
        q.Parameters.Int("@t", type);
        q.Parameters.Int("@room", roomId);
        q.Parameters.NullableDate("@from", from);
        q.Parameters.NullableDate("@to", to);
        q.Parameters.Big("@except", exceptBlock);
        var deltas = new TypeNightDeltas();
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            if (rd.GetInt32(1) == 1) continue;
            var n = rd.GetDate(0);
            deltas.Add(type, n, n.AddDays(1), sellable: delta);
        }

        return deltas;
    }
}
