using Hcc.Application.Inventory;
using Hcc.Domain.Inventory;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>
/// SQL adapter for <see cref="IRoomBlockHistoryReader"/> (W3-8). As of business date D a block is visible when it was created on or
/// before D, and its window is clipped to its release date only when the release took effect on or before D — so a release or edit made
/// after D never changes what D showed.
/// </summary>
public sealed class SqlRoomBlockHistoryReader(SqlConnectionFactory connections) : IRoomBlockHistoryReader
{
    // Effective window as of @asof (to_eff NULL = open-ended).
    private const string AsOfWindows = """
        SELECT b.room_block_id, b.room_id, b.from_night, b.reason,
          CASE WHEN b.released_on IS NOT NULL AND b.released_on <= @asof AND (b.to_night IS NULL OR b.to_night > b.released_on) THEN b.released_on ELSE b.to_night END AS to_eff
        FROM inv.room_block b JOIN inv.room r ON r.room_id = b.room_id
        WHERE r.property_id = @p AND b.created_on <= @asof
        """;

    public async Task<IReadOnlyList<RoomBlockHistoryEntry>> ListAsync(int propertyId, int? roomId, long? roomBlockId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT h.history_id, h.room_block_id, h.room_id, h.event, h.effective_on, h.from_night, h.to_night, h.reason, h.source, h.source_ref,
                   h.related_block_id, h.actor_id, h.event_utc
            FROM inv.room_block_history h JOIN inv.room r ON r.room_id = h.room_id
            WHERE r.property_id = @p AND (@room IS NULL OR h.room_id = @room) AND (@block IS NULL OR h.room_block_id = @block)
            ORDER BY h.history_id;
            """, conn);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Int("@room", roomId);
        q.Parameters.Big("@block", roomBlockId);
        var list = new List<RoomBlockHistoryEntry>();
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RoomBlockHistoryEntry(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt32(2), rd.GetString(3), rd.GetDate(4), rd.GetDateOrNull(5), rd.GetDateOrNull(6),
                rd.GetString(7), rd.GetString(8), rd.GetInt64OrNull(9), rd.GetInt64OrNull(10), rd.IsDBNull(11) ? null : rd.GetInt32(11), DateTime.SpecifyKind(rd.GetDateTime(12), DateTimeKind.Utc)));
        return list;
    }

    public async Task<IReadOnlyList<RoomBlockWindow>> WindowsAsOfAsync(int propertyId, DateOnly asOf, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command($"""
            SELECT room_id, from_night, to_eff, reason FROM ({AsOfWindows}) w
            WHERE (w.from_night IS NULL OR w.to_eff IS NULL OR w.from_night < w.to_eff) AND (w.to_eff IS NULL OR @from < w.to_eff) AND (w.from_night IS NULL OR @to > w.from_night);
            """, conn);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Date("@asof", asOf);
        q.Parameters.Date("@from", from);
        q.Parameters.Date("@to", to);
        var list = new List<RoomBlockWindow>();
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RoomBlockWindow(rd.GetInt32(0), rd.GetDateOrNull(1), rd.GetDateOrNull(2), rd.GetString(3)));
        return list;
    }

    public async Task<IReadOnlyList<RoomStatusEntry>> RoomStatusAsync(int propertyId, DateOnly asOf, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command($"""
            SELECT r.room_id, r.room_no, t.code,
              (SELECT TOP (1) w.room_block_id FROM ({AsOfWindows}) w WHERE w.room_id = r.room_id
                 AND (w.from_night IS NULL OR w.from_night <= @asof) AND (w.to_eff IS NULL OR w.to_eff > @asof) ORDER BY w.room_block_id DESC) AS block_id,
              (SELECT TOP (1) w.reason FROM ({AsOfWindows}) w WHERE w.room_id = r.room_id
                 AND (w.from_night IS NULL OR w.from_night <= @asof) AND (w.to_eff IS NULL OR w.to_eff > @asof) ORDER BY w.room_block_id DESC) AS reason,
              CASE WHEN EXISTS (SELECT 1 FROM inv.room_night rn WHERE rn.room_id = r.room_id AND rn.night = @asof) THEN 1 ELSE 0 END AS occupied
            FROM inv.room r JOIN inv.room_type t ON t.room_type_id = r.room_type_id
            WHERE r.property_id = @p
            ORDER BY r.room_no;
            """, conn);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Date("@asof", asOf);
        var list = new List<RoomStatusEntry>();
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RoomStatusEntry(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), !rd.IsDBNull(3), rd.GetStringOrNull(4), rd.GetInt64OrNull(3), rd.GetInt32(5) == 1));
        return list;
    }
}
