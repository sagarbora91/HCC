using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Application.Reservations;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Inventory;
using Hcc.Domain.Reservations;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Inventory;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Reservations;

/// <summary>
/// SQL adapter for <see cref="IReservationStore"/>. Lock order: reservation row (UPDLOCK) → group block → type counts → room
/// nights (via <see cref="SqlInventoryAllocator"/>). Every write is version-checked against the row read under lock.
/// </summary>
public sealed class SqlReservationStore(SqlConnectionFactory connections) : IReservationStore
{
    private const string Columns = """
        reservation_id, public_no, status, guest_name, room_id, room_type_id, arrival, departure, adults, children, hold_until_utc,
        nightly_rate_paise, group_block_id, notes, row_ver
        """;

    public async Task<ReservationRecord?> GetForUpdateAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default)
    {
        await using var q = SqlScope.Of(tx, connections).Command($"SELECT {Columns} FROM inv.reservation WITH (UPDLOCK, ROWLOCK) WHERE reservation_id = @r AND property_id = @p;");
        q.Parameters.Big("@r", reservationId);
        q.Parameters.Int("@p", propertyId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false) || rd.IsDBNull(4)) return null;
        return new ReservationRecord(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), rd.GetInt32(5), rd.GetDate(6),
            rd.GetDate(7), rd.GetByte(8), rd.GetByte(9), rd.GetUtcOrNull(10), rd.GetInt64OrNull(11), rd.GetInt64OrNull(12), rd.GetStringOrNull(13), (byte[])rd[14]);
    }

    public async Task<SavedReservation> InsertAsync(ICommandTransaction tx, NewReservation r, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        var s = SqlScope.Of(tx, connections);
        long id;
        string publicNo;
        await using (var ins = s.Command("""
            DECLARE @no VARCHAR(20) = CONCAT('HCC-', NEXT VALUE FOR inv.seq_reservation_no);
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, hold_until_utc, created_utc, room_id, room_type_id,
              adults, children, phone, source, nightly_rate_paise, quote_id, group_block_id, restriction_override, notes, created_by, status_utc)
            OUTPUT INSERTED.reservation_id, INSERTED.public_no
            VALUES (@p, @no, @g, @a, @d, @st, @hold, @now, @room, @type, @ad, @ch, @phone, @src, @rate, @quote, @block, @ovr, @notes, @u, @now);
            """))
        {
            ins.Parameters.Int("@p", r.PropertyId);
            ins.Parameters.Text("@g", r.GuestName, 200);
            ins.Parameters.Date("@a", r.Allocation.Arrival);
            ins.Parameters.Date("@d", r.Allocation.Departure);
            ins.Parameters.Add("@st", SqlDbType.VarChar, 12).Value = r.Status;
            ins.Parameters.Utc("@hold", r.HoldUntilUtc);
            ins.Parameters.Utc("@now", r.NowUtc);
            ins.Parameters.Int("@room", r.Allocation.RoomId);
            ins.Parameters.Int("@type", r.Allocation.RoomTypeId);
            ins.Parameters.Add("@ad", SqlDbType.TinyInt).Value = (byte)r.Adults;
            ins.Parameters.Add("@ch", SqlDbType.TinyInt).Value = (byte)r.Children;
            ins.Parameters.Add("@phone", SqlDbType.VarChar, 20).Value = (object?)r.Phone ?? DBNull.Value;
            ins.Parameters.Add("@src", SqlDbType.VarChar, 40).Value = r.Source;
            ins.Parameters.Big("@rate", r.Nights.Count > 0 ? r.Nights[0].RatePaise : null);
            ins.Parameters.Big("@quote", r.QuoteId);
            ins.Parameters.Big("@block", r.Allocation.GroupBlockId);
            ins.Parameters.Text("@ovr", r.RestrictionOverride, 400);
            ins.Parameters.Text("@notes", r.Notes, 400);
            ins.Parameters.Int("@u", r.PrincipalId);
            await using var rd = await ins.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (id, publicNo) = (rd.GetInt64(0), rd.GetString(1));
        }

        await InventorySql.Guard(async () =>
        {
            await SqlInventoryAllocator.ChangeAsync(s, r.PropertyId, id, null, r.Allocation, true, ct).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
        await WriteNightsAsync(s, id, r.Nights, ct).ConfigureAwait(false);
        return new SavedReservation(id, publicNo, await VersionAsync(s, id, ct).ConfigureAwait(false));
    }

    public async Task<SavedReservation> ChangeAsync(ICommandTransaction tx, int propertyId, ReservationRecord current, ReservationChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(change);
        var s = SqlScope.Of(tx, connections);
        await InventorySql.Guard(async () =>
        {
            await SqlInventoryAllocator.ChangeAsync(s, propertyId, current.ReservationId, current.Allocation, change.Allocation, true, ct).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);
        byte[] ver;
        await using (var up = s.Command("""
            UPDATE inv.reservation SET room_id = @room, room_type_id = @type, arrival = @a, departure = @d, nightly_rate_paise = @rate,
              quote_id = COALESCE(@quote, quote_id), notes = @notes
            OUTPUT INSERTED.row_ver WHERE reservation_id = @r AND row_ver = @v;
            """))
        {
            up.Parameters.Int("@room", change.Allocation.RoomId);
            up.Parameters.Int("@type", change.Allocation.RoomTypeId);
            up.Parameters.Date("@a", change.Allocation.Arrival);
            up.Parameters.Date("@d", change.Allocation.Departure);
            up.Parameters.Big("@rate", change.NightlyRatePaise);
            up.Parameters.Big("@quote", change.QuoteId);
            up.Parameters.Text("@notes", change.Notes, 400);
            up.Parameters.Big("@r", current.ReservationId);
            up.Parameters.Version("@v", current.RowVersion);
            var o = await up.ExecuteScalarAsync(ct).ConfigureAwait(false);
            SqlValues.EnsureVersionMatched(o is null ? 0 : 1, "This reservation");
            ver = (byte[])o!;
        }

        await using (var del = s.Command("DELETE inv.reservation_night WHERE reservation_id = @r;"))
        {
            del.Parameters.Big("@r", current.ReservationId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await WriteNightsAsync(s, current.ReservationId, change.Nights, ct).ConfigureAwait(false);
        return new SavedReservation(current.ReservationId, current.PublicNo, ver);
    }

    public async Task<SavedReservation> SetStatusAsync(ICommandTransaction tx, int propertyId, ReservationRecord current, string status, string? reason,
        DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        var s = SqlScope.Of(tx, connections);
        if (status is ReservationStatuses.Cancelled or ReservationStatuses.NoShow or ReservationStatuses.Expired)
        {
            await InventorySql.Guard(async () =>
            {
                await SqlInventoryAllocator.ChangeAsync(s, propertyId, current.ReservationId, current.Allocation, null, true, ct).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);
        }

        await using var up = s.Command("""
            UPDATE inv.reservation SET status = @st, status_utc = @now, cancel_reason = COALESCE(@reason, cancel_reason),
              hold_until_utc = CASE WHEN @st = 'Expired' THEN hold_until_utc ELSE NULL END
            OUTPUT INSERTED.row_ver WHERE reservation_id = @r AND row_ver = @v;
            """);
        up.Parameters.Add("@st", SqlDbType.VarChar, 12).Value = status;
        up.Parameters.Utc("@now", nowUtc);
        up.Parameters.Text("@reason", reason, 200);
        up.Parameters.Big("@r", current.ReservationId);
        up.Parameters.Version("@v", current.RowVersion);
        var o = await up.ExecuteScalarAsync(ct).ConfigureAwait(false);
        SqlValues.EnsureVersionMatched(o is null ? 0 : 1, "This reservation");
        return new SavedReservation(current.ReservationId, current.PublicNo, (byte[])o!);
    }

    public async Task<ReservationDetails?> GetDetailsAsync(int propertyId, long reservationId, DateTime nowUtc, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command("""
            SELECT r.reservation_id, r.public_no, r.row_ver, r.status, r.guest_name, r.phone, r.room_id, rm.room_no, r.room_type_id, t.code,
              r.arrival, r.departure, r.adults, r.children, r.source, r.nightly_rate_paise, r.hold_until_utc, r.group_block_id,
              r.restriction_override, r.cancel_reason, r.notes
            FROM inv.reservation r JOIN inv.room rm ON rm.room_id = r.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id
            WHERE r.reservation_id = @r AND r.property_id = @p;
            SELECT night, rate_paise, tax_bps, tax_paise FROM inv.reservation_night WHERE reservation_id = @r ORDER BY night;
            """, conn);
        q.Parameters.Big("@r", reservationId);
        q.Parameters.Int("@p", propertyId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        var holdUntil = rd.GetUtcOrNull(16);
        var status = ReservationStatusMap.Effective(rd.GetString(3), holdUntil, nowUtc);
        var head = new
        {
            Id = rd.GetInt64(0), No = rd.GetString(1), Ver = (byte[])rd[2], Guest = rd.GetString(4), Phone = rd.GetStringOrNull(5), RoomId = rd.GetInt32(6),
            RoomNo = rd.GetString(7), TypeId = rd.GetInt32(8), Type = rd.GetString(9), A = rd.GetDate(10), D = rd.GetDate(11), Ad = (int)rd.GetByte(12),
            Ch = (int)rd.GetByte(13), Src = rd.GetString(14), Rate = rd.GetInt64OrNull(15), Block = rd.GetInt64OrNull(17), Ovr = rd.GetStringOrNull(18),
            Cancel = rd.GetStringOrNull(19), Notes = rd.GetStringOrNull(20),
        };
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var nights = new List<ReservationNight>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            nights.Add(new ReservationNight(Domain.Dates.StayDates.ToWire(rd.GetDate(0)), rd.GetInt64(1), rd.GetInt32(2), rd.GetInt64(3)));
        var net = nights.Sum(n => n.RatePaise);
        var tax = nights.Sum(n => n.TaxPaise);
        return new ReservationDetails(head.Id, head.No, InventoryErrors.VersionToWire(head.Ver), status, head.Guest, head.Phone, head.RoomId, head.RoomNo,
            head.TypeId, head.Type, Domain.Dates.StayDates.ToWire(head.A), Domain.Dates.StayDates.ToWire(head.D), head.Ad, head.Ch, head.Src, head.Rate,
            holdUntil is null ? null : InventoryService.ToWireInstant(holdUntil.Value), head.Block, head.Ovr, head.Cancel, head.Notes, nights, net, tax, net + tax);
    }

    private static async Task WriteNightsAsync(SqlScope s, long reservationId, IReadOnlyList<PricedNight> nights, CancellationToken ct)
    {
        foreach (var n in nights)
        {
            await using var ins = s.Command("INSERT inv.reservation_night(reservation_id, night, rate_paise, tax_bps, tax_paise) VALUES (@r, @n, @rate, @b, @x);");
            ins.Parameters.Big("@r", reservationId);
            ins.Parameters.Date("@n", n.Date);
            ins.Parameters.Big("@rate", n.RatePaise);
            ins.Parameters.Int("@b", n.TaxBps);
            ins.Parameters.Big("@x", n.TaxPaise);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> VersionAsync(SqlScope s, long reservationId, CancellationToken ct)
    {
        await using var v = s.Command("SELECT row_ver FROM inv.reservation WHERE reservation_id = @r;");
        v.Parameters.Big("@r", reservationId);
        return (byte[])(await v.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
