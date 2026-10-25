using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Domain.Inventory;
using Hcc.Domain.Stays;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Infrastructure.SqlServer.Sql;

namespace Hcc.Infrastructure.SqlServer.Stays;

/// <summary>SQL adapter of <see cref="IStayChangeStore"/> (0002 inventory/billing tables, 0200 reservation details, 0300 stays, 0301 history).</summary>
public sealed class SqlStayChangeStore : IStayChangeStore
{
    public async Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(P3Sql.BusinessDateSql);
        return await P3Sql.BusinessDateAsync(cmd, propertyId, ct).ConfigureAwait(false);
    }

    public async Task<StayChangeContext?> LockStayAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT s.stay_id, s.property_id, s.reservation_id, s.folio_id, s.status, s.arrival, s.departure, s.row_ver,
                   seg.room_id, seg.seq, r.room_type_id, r.adults, r.children, r.nightly_rate_paise, r.group_block_id
            FROM gst.stay s WITH (UPDLOCK, ROWLOCK)
            JOIN inv.reservation r WITH (UPDLOCK, ROWLOCK) ON r.reservation_id = s.reservation_id
            CROSS APPLY (SELECT TOP (1) room_id, seq FROM gst.stay_segment WHERE stay_id = s.stay_id ORDER BY seq DESC) seg
            WHERE s.stay_id = @s AND s.property_id = @p;
            """);
        cmd.Add("@s", SqlDbType.BigInt, stayId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new StayChangeContext(rd.GetInt64(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetInt64(3), rd.GetString(4), P3Sql.Date(rd, 5), P3Sql.Date(rd, 6), (byte[])rd[7],
            rd.GetInt32(8), (int)rd.GetByte(9), rd.IsDBNull(10) ? 0 : rd.GetInt32(10), (int)rd.GetByte(11), (int)rd.GetByte(12),
            rd.IsDBNull(13) ? null : rd.GetInt64(13), rd.IsDBNull(14) ? null : rd.GetInt64(14));
    }

    public async Task<ChangeRoom?> LockRoomAsync(ICommandTransaction tx, int propertyId, int roomId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT r.room_id, r.room_no, r.room_type_id, t.max_occupancy, r.hk_status,
                   CASE WHEN EXISTS (SELECT 1 FROM gst.stay_segment s WITH (UPDLOCK, HOLDLOCK) WHERE s.room_id = r.room_id AND s.to_date IS NULL) THEN 1 ELSE 0 END
            FROM inv.room r WITH (UPDLOCK, ROWLOCK) JOIN inv.room_type t ON t.room_type_id = r.room_type_id
            WHERE r.room_id = @r AND r.property_id = @p;
            """);
        cmd.Add("@r", SqlDbType.Int, roomId);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new ChangeRoom(rd.GetInt32(0), rd.GetString(1), rd.GetInt32(2), (int)rd.GetByte(3), rd.GetString(4), rd.GetInt32(5) == 1);
    }

    public async Task<bool> RoomBlockedDuringAsync(ICommandTransaction tx, int roomId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command(SqlRoomBlockReader.BlockedDuringSql);
        return await SqlRoomBlockReader.BlockedDuringAsync(cmd, roomId, from, to, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoomNightKey>> RoomNightsAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT n.room_id, r.room_type_id, n.night FROM inv.room_night n WITH (UPDLOCK)
            JOIN inv.room r ON r.room_id = n.room_id WHERE n.reservation_id = @r ORDER BY n.night;
            """);
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        var list = new List<RoomNightKey>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(new RoomNightKey(rd.GetInt32(0), rd.GetInt32(1), P3Sql.Date(rd, 2)));
        return list;
    }

    public async Task<IReadOnlySet<DateOnly>> PostedNightsAsync(ICommandTransaction tx, long folioId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("""
            SELECT c.service_date FROM bill.charge c
            WHERE c.folio_id = @f AND c.kind = 'room' AND c.reverses_charge_id IS NULL
              AND NOT EXISTS (SELECT 1 FROM bill.charge x WHERE x.reverses_charge_id = c.charge_id);
            """);
        cmd.Add("@f", SqlDbType.BigInt, folioId);
        var set = new HashSet<DateOnly>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) set.Add(P3Sql.Date(rd, 0));
        return set;
    }

    public async Task<long?> LastNightRateAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("SELECT TOP (1) rate_paise FROM inv.reservation_night WHERE reservation_id = @r ORDER BY night DESC;");
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is long v ? v : null;
    }

    public async Task DeleteNightPricesFromAsync(ICommandTransaction tx, long reservationId, DateOnly from, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("DELETE inv.reservation_night WHERE reservation_id = @r AND night >= @d;");
        cmd.Add("@r", SqlDbType.BigInt, reservationId);
        cmd.Add("@d", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task WriteNightPricesAsync(ICommandTransaction tx, long reservationId, IReadOnlyList<PricedNight> nights, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nights);
        foreach (var n in nights)
        {
            await using var cmd = P3Sql.Sql(tx).Command("""
                DELETE inv.reservation_night WHERE reservation_id = @r AND night = @n;
                INSERT inv.reservation_night(reservation_id, night, rate_paise, tax_bps, tax_paise) VALUES (@r, @n, @rate, @bps, @tax);
                """);
            cmd.Add("@r", SqlDbType.BigInt, reservationId);
            cmd.Add("@n", SqlDbType.Date, n.Date.ToDateTime(TimeOnly.MinValue));
            cmd.Add("@rate", SqlDbType.BigInt, n.RatePaise);
            cmd.Add("@bps", SqlDbType.Int, n.TaxBps);
            cmd.Add("@tax", SqlDbType.BigInt, n.TaxPaise);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<byte[]> ApplyMoveAsync(ICommandTransaction tx, StayChangeContext s, ChangeRoom toRoom, DateOnly businessDate, long? newRatePaise, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(toRoom);
        await using var cmd = P3Sql.Sql(tx).Command("""
            UPDATE gst.stay_segment SET to_date = @bd WHERE stay_id = @s AND seq = @seq AND to_date IS NULL;
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date) VALUES (@s, @seq + 1, @room, @bd);
            UPDATE inv.reservation SET room_id = @room, room_type_id = @type, nightly_rate_paise = COALESCE(@rate, nightly_rate_paise) WHERE reservation_id = @res;
            UPDATE gst.stay SET departure = departure OUTPUT INSERTED.row_ver WHERE stay_id = @s;
            """);
        cmd.Add("@s", SqlDbType.BigInt, s.StayId);
        cmd.Add("@seq", SqlDbType.TinyInt, (byte)s.CurrentSegmentSeq);
        cmd.Add("@room", SqlDbType.Int, toRoom.RoomId);
        cmd.Add("@type", SqlDbType.Int, toRoom.RoomTypeId);
        cmd.Add("@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@rate", SqlDbType.BigInt, newRatePaise);
        cmd.Add("@res", SqlDbType.BigInt, s.ReservationId);
        return (byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<byte[]> ApplyDeparturesAsync(ICommandTransaction tx, StayChangeContext s, DateOnly newDeparture, long? newRatePaise, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(s);
        await using var cmd = P3Sql.Sql(tx).Command("""
            UPDATE inv.reservation SET departure = @d, nightly_rate_paise = COALESCE(@rate, nightly_rate_paise) WHERE reservation_id = @res;
            UPDATE gst.stay SET departure = @d OUTPUT INSERTED.row_ver WHERE stay_id = @s;
            """);
        cmd.Add("@d", SqlDbType.Date, newDeparture.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@rate", SqlDbType.BigInt, newRatePaise);
        cmd.Add("@res", SqlDbType.BigInt, s.ReservationId);
        cmd.Add("@s", SqlDbType.BigInt, s.StayId);
        return (byte[])(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task SetRoomHkAsync(ICommandTransaction tx, int roomId, string hk, CancellationToken ct = default)
    {
        await using var cmd = P3Sql.Sql(tx).Command("UPDATE inv.room SET hk_status = @hk WHERE room_id = @r;");
        cmd.Add("@hk", SqlDbType.VarChar, hk, 12);
        cmd.Add("@r", SqlDbType.Int, roomId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertMoveTaskAsync(ICommandTransaction tx, int propertyId, int roomId, long stayId, int segmentSeq, string note, DateOnly businessDate, DateTime utc, CancellationToken ct = default)
    {
        var key = $"hk:move:{stayId.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{segmentSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        // Locked check-then-insert (contracts §4 rule 8); the filtered unique index on causal_key is the backstop.
        await using var chk = P3Sql.Sql(tx).Command("SELECT task_id FROM ops.hk_task WITH (UPDLOCK, HOLDLOCK) WHERE causal_key = @ck;");
        chk.Add("@ck", SqlDbType.VarChar, key, 80);
        if (await chk.ExecuteScalarAsync(ct).ConfigureAwait(false) is long existing) return existing;
        return await SqlHousekeepingStore.InsertTaskAsync(P3Sql.Sql(tx).Command(SqlHousekeepingStore.InsertTaskSql), propertyId, roomId, "move", note, stayId, key, businessDate, utc, ct).ConfigureAwait(false);
    }

    public async Task InsertChangeAsync(ICommandTransaction tx, int propertyId, StayChangeLog log, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        await using var cmd = P3Sql.Sql(tx).Command("""
            INSERT gst.stay_change(property_id, stay_id, kind, business_date, from_room_id, to_room_id, old_departure, new_departure, rate_decision,
              old_rate_paise, new_rate_paise, rate_loss_paise, reason, created_by, created_utc)
            VALUES (@p, @s, @k, @bd, @from, @to, @od, @nd, @rd, @or, @nr, @loss, @reason, @u, @utc);
            """);
        cmd.Add("@p", SqlDbType.Int, propertyId);
        cmd.Add("@s", SqlDbType.BigInt, log.StayId);
        cmd.Add("@k", SqlDbType.VarChar, log.Kind, 8);
        cmd.Add("@bd", SqlDbType.Date, log.BusinessDate.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@from", SqlDbType.Int, log.FromRoomId);
        cmd.Add("@to", SqlDbType.Int, log.ToRoomId);
        cmd.Add("@od", SqlDbType.Date, log.OldDeparture.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@nd", SqlDbType.Date, log.NewDeparture.ToDateTime(TimeOnly.MinValue));
        cmd.Add("@rd", SqlDbType.VarChar, log.RateDecision, 6);
        cmd.Add("@or", SqlDbType.BigInt, log.OldRatePaise);
        cmd.Add("@nr", SqlDbType.BigInt, log.NewRatePaise);
        cmd.Add("@loss", SqlDbType.BigInt, log.RateLossPaise);
        cmd.Add("@reason", SqlDbType.NVarChar, log.Reason.Length == 0 ? null : log.Reason, 200);
        cmd.Add("@u", SqlDbType.Int, principalId);
        cmd.Add("@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
