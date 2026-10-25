using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>
/// SQL adapter for <see cref="IRoomBlockPort"/> and <see cref="IRoomBlockEditPort"/>. The room type's materialized count rows in the
/// window are range-locked (UPDLOCK, HOLDLOCK — no count row can be created in the window meanwhile), then the room's live room
/// nights are checked under lock, then sellable is adjusted and the block row written. Count rows created later compute sellable
/// from <c>inv.v_room_block_effective</c>. History (W3-8): a release or edit never touches nights before today's business date —
/// the block row keeps <c>released_on</c> and the past stays blocked; every create/edit/release is written to
/// <c>inv.room_block_history</c> with the actor and the UTC time from <paramref name="clock"/> (SYSUTCDATETIME when none).
/// </summary>
public sealed class SqlRoomBlockPort(SqlConnectionFactory connections, TimeProvider? clock = null) : IRoomBlockPort, IRoomBlockEditPort
{
    public async Task<long> PlaceAsync(ICommandTransaction tx, int propertyId, int roomId, DateOnly? from, DateOnly? to, string reason, string source,
        long? sourceRef, CancellationToken ct = default)
    {
        var r = Validate(from, to, reason, source);
        var s = SqlScope.Of(tx, connections);
        var id = await PlaceCoreAsync(s, tx, propertyId, roomId, from, to, r, source, sourceRef, ct).ConfigureAwait(false);
        await HistoryAsync(s, tx, id, RoomBlockEvents.Created, null, ct).ConfigureAwait(false);
        return id;
    }

    public async Task RemoveAsync(ICommandTransaction tx, int propertyId, long roomBlockId, CancellationToken ct = default)
    {
        var s = SqlScope.Of(tx, connections);
        var row = await LockActiveAsync(s, propertyId, roomBlockId, ct).ConfigureAwait(false);
        var bd = await BusinessDateAsync(s, propertyId, ct).ConfigureAwait(false);
        await ReleaseCoreAsync(s, tx, propertyId, row, bd, ct).ConfigureAwait(false);
        await HistoryAsync(s, tx, roomBlockId, RoomBlockEvents.Released, null, ct).ConfigureAwait(false);
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

    public async Task<long?> EditAsync(ICommandTransaction tx, int propertyId, long roomBlockId, DateOnly? from, DateOnly? to, string reason, CancellationToken ct = default)
    {
        var r = Validate(from, to, reason, "Inventory");
        var s = SqlScope.Of(tx, connections);
        var old = await LockActiveAsync(s, propertyId, roomBlockId, ct).ConfigureAwait(false);
        var bd = await BusinessDateAsync(s, propertyId, ct).ConfigureAwait(false);
        // Past nights are never rewritten: the new window starts no earlier than today.
        var newFrom = from is null || from.Value < bd ? bd : from.Value;
        await ReleaseCoreAsync(s, tx, propertyId, old, bd, ct).ConfigureAwait(false);
        if (to is { } t && t <= newFrom)
        {
            await HistoryAsync(s, tx, roomBlockId, RoomBlockEvents.Edited, null, ct, newFrom, to, r).ConfigureAwait(false);
            return null;
        }

        var id = await PlaceCoreAsync(s, tx, propertyId, old.RoomId, newFrom, to, r, old.Source, old.SourceRef, ct).ConfigureAwait(false);
        await using (var up = s.Command("UPDATE inv.room_block SET successor_block_id = @n WHERE room_block_id = @b;"))
        {
            up.Parameters.Big("@n", id);
            up.Parameters.Big("@b", roomBlockId);
            await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await HistoryAsync(s, tx, roomBlockId, RoomBlockEvents.Edited, id, ct, newFrom, to, r).ConfigureAwait(false);
        return id;
    }

    private static string Validate(DateOnly? from, DateOnly? to, string reason, string source)
    {
        if (from is { } f && to is { } t && t <= f) throw HccException.Validation("The block must end after it starts.");
        var r = (reason ?? "").Trim();
        if (r.Length is < 2 or > 200) throw HccException.Validation("Give a reason for the block.");
        if (source is not ("Inventory" or "Maintenance")) throw HccException.Validation("Unknown block source.");
        return r;
    }

    private sealed record BlockRow(int RoomId, DateOnly? From, DateOnly? To, string Reason, string Source, long? SourceRef, long Id);

    private static async Task<DateOnly> BusinessDateAsync(SqlScope s, int propertyId, CancellationToken ct)
    {
        await using var q = s.Command("SELECT business_date FROM inv.property WHERE property_id = @p;");
        q.Parameters.Int("@p", propertyId);
        return await q.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : throw InventoryErrors.NotFound("Property");
    }

    private static async Task<BlockRow> LockActiveAsync(SqlScope s, int propertyId, long roomBlockId, CancellationToken ct)
    {
        await using var q = s.Command("""
            SELECT b.room_id, b.from_night, b.to_night, b.reason, b.source, b.source_ref FROM inv.room_block b WITH (UPDLOCK, ROWLOCK) JOIN inv.room r ON r.room_id = b.room_id
            WHERE b.room_block_id = @b AND r.property_id = @p AND b.active = 1;
            """);
        q.Parameters.Big("@b", roomBlockId);
        q.Parameters.Int("@p", propertyId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw InventoryErrors.NotFound("Room block");
        return new BlockRow(rd.GetInt32(0), rd.GetDateOrNull(1), rd.GetDateOrNull(2), rd.GetString(3), rd.GetString(4), rd.GetInt64OrNull(5), roomBlockId);
    }

    private async Task<long> PlaceCoreAsync(SqlScope s, ICommandTransaction tx, int propertyId, int roomId, DateOnly? from, DateOnly? to, string reason,
        string source, long? sourceRef, CancellationToken ct)
    {
        var (type, roomNo) = await RoomAsync(s, propertyId, roomId, ct).ConfigureAwait(false);
        var deltas = await NightsAsync(s, type, roomId, from, to, null, -1, null, ct).ConfigureAwait(false);

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
        var bd = await BusinessDateAsync(s, propertyId, ct).ConfigureAwait(false);
        await using var ins = s.Command("""
            INSERT inv.room_block(room_id, from_night, to_night, reason, source, source_ref, created_on, created_utc) OUTPUT INSERTED.room_block_id
            VALUES (@room, @from, @to, @reason, @src, @ref, @bd, @now);
            """);
        ins.Parameters.Int("@room", roomId);
        ins.Parameters.NullableDate("@from", from);
        ins.Parameters.NullableDate("@to", to);
        ins.Parameters.Text("@reason", reason, 200);
        ins.Parameters.Add("@src", SqlDbType.VarChar, 20).Value = source;
        ins.Parameters.Big("@ref", sourceRef);
        ins.Parameters.Date("@bd", bd);
        ins.Parameters.Utc("@now", Now());
        return (long)(await ins.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    /// <summary>Restores sellable only for nights from today on (past nights stay blocked) and records the release date.</summary>
    private async Task ReleaseCoreAsync(SqlScope s, ICommandTransaction tx, int propertyId, BlockRow row, DateOnly bd, CancellationToken ct)
    {
        var (type, _) = await RoomAsync(s, propertyId, row.RoomId, ct).ConfigureAwait(false);
        var deltas = await NightsAsync(s, type, row.RoomId, row.From, row.To, row.Id, +1, bd, ct).ConfigureAwait(false);
        await deltas.ApplyAsync(s, propertyId, ct).ConfigureAwait(false);
        await using var up = s.Command("UPDATE inv.room_block SET active = 0, released_on = @bd, released_by = @u, released_utc = @now WHERE room_block_id = @b;");
        up.Parameters.Big("@b", row.Id);
        up.Parameters.Date("@bd", bd);
        up.Parameters.Int("@u", tx.Context.PrincipalId);
        up.Parameters.Utc("@now", Now());
        await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task HistoryAsync(SqlScope s, ICommandTransaction tx, long blockId, string evt, long? related, CancellationToken ct,
        DateOnly? newFrom = null, DateOnly? newTo = null, string? newReason = null)
    {
        // Edited rows show what the block was edited to; Created/Released rows show the block's own window.
        var edited = evt == RoomBlockEvents.Edited;
        await using var cmd = s.Command("""
            INSERT inv.room_block_history(room_block_id, room_id, event, effective_on, from_night, to_night, reason, source, source_ref, related_block_id, actor_id, event_utc)
            SELECT b.room_block_id, b.room_id, @evt,
              CASE WHEN @evt = 'Created' THEN b.created_on ELSE b.released_on END,
              CASE WHEN @edited = 1 THEN @nf ELSE b.from_night END, CASE WHEN @edited = 1 THEN @nt ELSE b.to_night END,
              CASE WHEN @edited = 1 THEN @nr ELSE b.reason END, b.source, b.source_ref, @rel, @actor, @now
            FROM inv.room_block b WHERE b.room_block_id = @b;
            """);
        cmd.Parameters.Big("@b", blockId);
        cmd.Parameters.Add("@evt", SqlDbType.VarChar, 10).Value = evt;
        cmd.Parameters.Add("@edited", SqlDbType.Bit).Value = edited;
        cmd.Parameters.NullableDate("@nf", newFrom);
        cmd.Parameters.NullableDate("@nt", newTo);
        cmd.Parameters.Text("@nr", newReason ?? "", 200);
        cmd.Parameters.Big("@rel", related);
        cmd.Parameters.Int("@actor", tx.Context.PrincipalId);
        cmd.Parameters.Utc("@now", Now());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private DateTime Now() => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static async Task<(int Type, string RoomNo)> RoomAsync(SqlScope s, int propertyId, int roomId, CancellationToken ct)
    {
        await using var q = s.Command("SELECT room_type_id, room_no FROM inv.room WHERE room_id = @r AND property_id = @p;");
        q.Parameters.Int("@r", roomId);
        q.Parameters.Int("@p", propertyId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw InventoryErrors.NotFound("Room");
        return (rd.GetInt32(0), rd.GetString(1));
    }

    /// <summary>
    /// Range-locks the type's count rows in the window (nights from <paramref name="minNight"/> only) and returns a sellable delta for
    /// nights no other block in effect covers.
    /// </summary>
    private static async Task<TypeNightDeltas> NightsAsync(SqlScope s, int type, int roomId, DateOnly? from, DateOnly? to, long? exceptBlock, int delta,
        DateOnly? minNight, CancellationToken ct)
    {
        await using var q = s.Command("""
            SELECT c.night,
              CASE WHEN EXISTS (SELECT 1 FROM inv.v_room_block_effective b WHERE b.room_id = @room AND (@except IS NULL OR b.room_block_id <> @except)
                AND (b.from_night IS NULL OR b.from_night <= c.night) AND (b.to_night IS NULL OR b.to_night > c.night)) THEN 1 ELSE 0 END
            FROM inv.type_night_count c WITH (UPDLOCK, HOLDLOCK)
            WHERE c.room_type_id = @t AND (@from IS NULL OR c.night >= @from) AND (@to IS NULL OR c.night < @to) AND (@min IS NULL OR c.night >= @min)
            ORDER BY c.night;
            """);
        q.Parameters.Int("@t", type);
        q.Parameters.Int("@room", roomId);
        q.Parameters.NullableDate("@from", from);
        q.Parameters.NullableDate("@to", to);
        q.Parameters.NullableDate("@min", minNight);
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
