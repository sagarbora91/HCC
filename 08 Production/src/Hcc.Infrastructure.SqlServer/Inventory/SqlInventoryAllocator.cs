using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>
/// SQL implementation of <see cref="IInventoryAllocator"/> (contracts §4 rules 1–2, spike C1–C4). Lock order, always:
/// group-block rows (id asc) → <c>inv.type_night_count</c> (type asc, night asc; all touched keys, zero deltas included) →
/// <c>inv.room_night</c> (delete own rows, locked check, insert ascending). Block allotment changes and room-night changes are
/// written only after the counts, so a count row ensured inside this transaction never reflects this transaction's own change.
/// </summary>
public sealed class SqlInventoryAllocator(SqlConnectionFactory connections) : IInventoryAllocator
{
    public Task ChangeAsync(ICommandTransaction tx, int propertyId, long reservationId, StayAllocation? from, StayAllocation? to,
        bool returnToBlock = true, CancellationToken ct = default)
        => InventorySql.Guard(async () =>
        {
            await ChangeAsync(SqlScope.Of(tx, connections), propertyId, reservationId, from, to, returnToBlock, ct).ConfigureAwait(false);
            return 0;
        });

    public static async Task ChangeAsync(SqlScope scope, int propertyId, long reservationId, StayAllocation? from, StayAllocation? to,
        bool returnToBlock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (from is null && to is null) return;

        // 1. Group-block rows under update locks (ascending id), read before any change.
        var blockIds = new[] { from?.GroupBlockId, to?.GroupBlockId }.Where(x => x is not null).Select(x => x!.Value).Distinct().Order().ToList();
        var blocks = new Dictionary<long, (DateOnly Arrival, DateOnly Departure, bool Open)>();
        foreach (var id in blockIds)
        {
            await using var q = scope.Command("""
                SELECT g.arrival, g.departure, CASE WHEN g.status = 'Open' AND g.cutoff >= p.business_date THEN 1 ELSE 0 END
                FROM inv.group_block g WITH (UPDLOCK, ROWLOCK) JOIN inv.property p ON p.property_id = g.property_id
                WHERE g.group_block_id = @b AND g.property_id = @p;
                SELECT room_type_id FROM inv.group_block_type WITH (UPDLOCK, ROWLOCK) WHERE group_block_id = @b ORDER BY room_type_id;
                """);
            q.Parameters.Big("@b", id);
            q.Parameters.Int("@p", propertyId);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw InventoryErrors.NotFound("Group block");
            blocks[id] = (rd.GetDate(0), rd.GetDate(1), rd.GetInt32(2) == 1);
        }

        var sameBlockType = from?.GroupBlockId is { } fb && fb == to?.GroupBlockId && from.RoomTypeId == to.RoomTypeId;
        var deltas = new TypeNightDeltas();
        if (from is not null)
        {
            deltas.Add(from.RoomTypeId, from.Arrival, from.Departure, sold: -1);
            if (from.GroupBlockId is { } b && returnToBlock && blocks[b].Open && !sameBlockType)
                deltas.Add(from.RoomTypeId, blocks[b].Arrival, blocks[b].Departure, held: +1);
        }

        if (to is not null)
        {
            deltas.Add(to.RoomTypeId, to.Arrival, to.Departure, sold: +1);
            if (to.GroupBlockId is { } b && !sameBlockType)
            {
                if (!blocks[b].Open) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Pickup is closed for this group block.");
                deltas.Add(to.RoomTypeId, blocks[b].Arrival, blocks[b].Departure, held: -1);
            }
        }

        // Every key a writer touches is locked even when its net delta is zero (two writers on one room-night always meet here).
        if (from is not null) deltas.Add(from.RoomTypeId, from.Arrival, from.Departure);
        await deltas.ApplyAsync(scope, propertyId, ct).ConfigureAwait(false);

        // 2. Room nights.
        if (from is not null)
        {
            await using var del = scope.Command("DELETE inv.room_night WHERE reservation_id = @r;");
            del.Parameters.Big("@r", reservationId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (to is not null)
        {
            await using var ins = scope.Command($"""
                IF EXISTS (SELECT 1 FROM inv.room_night WITH (UPDLOCK, HOLDLOCK) WHERE room_id = @room AND night >= @a AND night < @d)
                  THROW {InventorySql.RoomTaken}, 'ROOM_TAKEN', 1;
                DECLARE @n DATE = @a;
                WHILE @n < @d
                BEGIN
                  INSERT inv.room_night(room_id, night, reservation_id) VALUES (@room, @n, @r);
                  SET @n = DATEADD(day, 1, @n);
                END
                """);
            ins.Parameters.Int("@room", to.RoomId);
            ins.Parameters.Date("@a", to.Arrival);
            ins.Parameters.Date("@d", to.Departure);
            ins.Parameters.Big("@r", reservationId);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // 3. Block allotment (after the counts).
        if (sameBlockType) return;
        if (to?.GroupBlockId is { } tb)
        {
            await using var up = scope.Command("UPDATE inv.group_block_type SET picked = picked + 1 WHERE group_block_id = @b AND room_type_id = @t AND picked < allotted;");
            up.Parameters.Big("@b", tb);
            up.Parameters.Int("@t", to.RoomTypeId);
            if (await up.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
                throw InventoryErrors.NotAvailable("No unpicked rooms of this type are left in the group block.");
        }

        if (from?.GroupBlockId is { } fbk && returnToBlock && blocks[fbk].Open)
        {
            await using var down = scope.Command("UPDATE inv.group_block_type SET picked = picked - 1 WHERE group_block_id = @b AND room_type_id = @t AND picked > 0;");
            down.Parameters.Big("@b", fbk);
            down.Parameters.Int("@t", from.RoomTypeId);
            await down.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}
