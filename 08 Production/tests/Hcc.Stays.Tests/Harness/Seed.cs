using System.Globalization;
using System.Text;

namespace Hcc.Stays.Tests.Harness;

/// <summary>
/// Loads the prototype BASE fixture (from the golden export) into SQL: property at business date 2026-10-24, 24 rooms with
/// readiness, room 308 blocked, 10 reservations with room nights, registrations + stays + folios for the 8 in-house guests,
/// and the fixture's housekeeping tasks. Fictional data only. Billing rows (charges/receipts) are written directly here as a
/// stand-in for P4's commands.
/// </summary>
public sealed class Seed
{
    private int _publicNo = 2000;

    private Seed(P3Database db) => Db = db;

    public P3Database Db { get; }

    public int PropertyId { get; private set; }

    public Dictionary<string, int> Rooms { get; } = [];

    public Dictionary<string, long> Reservations { get; } = [];

    public Dictionary<string, long> Stays { get; } = [];

    public Dictionary<string, long> Tasks { get; } = [];

    private static string S(string? v) => v is null ? "NULL" : "N'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Status(string prototype) => prototype switch { "In house" => "InHouse", "To do" => "ToDo", "In progress" => "InProgress", _ => prototype };

    public static async Task<Seed> BaseAsync(P3Database db, GoldenBase g, CancellationToken ct = default)
    {
        var seed = new Seed(db);
        seed.PropertyId = Convert.ToInt32(await db.ScalarAsync($"""
            INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('DEMO', N'HCC Demo Residency', 'Asia/Kolkata', '{g.BusinessDate}');
            """, ct), CultureInfo.InvariantCulture);
        var p = seed.PropertyId;
        await db.ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({p}, 'Classic', 2), ({p}, 'Deluxe', 3), ({p}, 'Suite', 4);", ct);
        var sb = new StringBuilder();
        foreach (var r in g.Rooms)
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room(property_id, room_no, room_type_id, hk_status) SELECT {p}, '{r.RoomNo}', room_type_id, '{r.Hk}' FROM inv.room_type WHERE property_id = {p} AND code = '{r.Type}';");
        await db.ExecAsync(sb.ToString(), ct);
        await using (var conn = await db.Platform.Connections.OpenAsync(ct))
        await using (var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT room_no, room_id FROM inv.room WHERE property_id = {p};", conn))
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct)) seed.Rooms[rd.GetString(0)] = rd.GetInt32(1);
        }

        foreach (var r in g.Rooms.Where(r => r.Blocked))
            await db.ExecAsync($"INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc) VALUES ({p}, {seed.Rooms[r.RoomNo]}, {D(r.BlockFrom)}, {D(r.BlockTo)}, {S(r.BlockReason ?? "Out of service")}, 'manual', {Users.Supervisor}, '2026-10-24T08:00:00');", ct);

        var i = 0;
        foreach (var b in g.Bookings)
        {
            i++;
            var resId = await seed.ReservationAsync(b.Room, b.Arrival, b.Departure, b.Guest, Status(b.Status), b.Id, ct);
            seed.Reservations[b.Id] = resId;
            if (!b.Registered) continue;
            var guestId = Convert.ToInt64(await db.ScalarAsync($"""
                INSERT gst.guest(property_id, full_name, phone, email, address, nationality, id_type, id_last4, verified, origin, created_utc)
                OUTPUT INSERTED.guest_id VALUES ({p}, {S(b.Guest)}, '+91 98200 0{1040 + i}', 'guest{i}@example.test', N'{i} Demo Street, Latur (fictional)', N'Indian', N'Passport (demo)', 'D{100 + i}', 1, 'manual', '2026-10-23T09:00:00');
                """, ct), CultureInfo.InvariantCulture);
            await db.ExecAsync($"INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES ({resId}, {guestId}, '2026-10-23T09:00:00', {Users.Reception});", ct);
            if (b.Status != "In house") continue;
            var stayId = Convert.ToInt64(await db.ScalarAsync($"""
                INSERT bill.folio(reservation_id) VALUES ({resId});
                DECLARE @f BIGINT = SCOPE_IDENTITY();
                INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by)
                OUTPUT INSERTED.stay_id VALUES ({p}, {resId}, {guestId}, @f, 'InHouse', '{b.Arrival}', '{b.Departure}', '{b.Arrival}', '{b.Arrival}T09:00:00', {Users.Reception});
                """, ct), CultureInfo.InvariantCulture);
            await db.ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date) VALUES ({stayId}, 1, {seed.Rooms[b.Room]}, '{b.Arrival}');", ct);
            seed.Stays[b.Id] = stayId;
        }

        foreach (var t in g.Tasks)
        {
            var mask = t.Status == "Inspection" ? 7 : 0;
            var started = t.Status is "In progress" or "Inspection" ? "'2026-10-24T07:30:00'" : "NULL";
            seed.Tasks[t.Id] = Convert.ToInt64(await db.ScalarAsync($"""
                INSERT ops.hk_task(property_id, room_id, kind, status, assignee_id, checklist_mask, business_date, created_utc, started_utc)
                OUTPUT INSERTED.task_id VALUES ({p}, {seed.Rooms[t.Room]}, '{t.Kind}', '{Status(t.Status)}', {Users.Of(t.Assignee)}, {mask}, '{g.BusinessDate}', '2026-10-24T07:00:00', {started});
                """, ct), CultureInfo.InvariantCulture);
        }

        return seed;
    }

    private static string D(string? date) => date is null ? "NULL" : $"'{date}'";

    /// <summary>A reservation with one room night per night of [arrival, departure) on the room (P2 stand-in).</summary>
    public async Task<long> ReservationAsync(string roomNo, string arrival, string departure, string guest, string status = "Confirmed", string? publicNo = null, CancellationToken ct = default)
    {
        publicNo ??= "HCC-" + (++_publicNo).ToString(CultureInfo.InvariantCulture);
        var id = Convert.ToInt64(await Db.ScalarAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id
            VALUES ({PropertyId}, '{publicNo}', {S(guest)}, '{arrival}', '{departure}', '{status}');
            """, ct), CultureInfo.InvariantCulture);
        var a = DateOnly.Parse(arrival, CultureInfo.InvariantCulture);
        var d = DateOnly.Parse(departure, CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        for (var n = a; n < d; n = n.AddDays(1))
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room_night(room_id, night, reservation_id) VALUES ({Rooms[roomNo]}, '{n:yyyy-MM-dd}', {id});");
        await Db.ExecAsync(sb.ToString(), ct);
        return id;
    }

    private async Task<string> VersionAsync(string sql, CancellationToken ct) => Convert.ToBase64String((byte[])(await Db.ScalarAsync(sql, ct))!);

    public Task<string> ReservationVersionAsync(long id, CancellationToken ct = default) => VersionAsync($"SELECT row_ver FROM inv.reservation WHERE reservation_id = {id};", ct);

    public Task<string> StayVersionAsync(long id, CancellationToken ct = default) => VersionAsync($"SELECT row_ver FROM gst.stay WHERE stay_id = {id};", ct);

    public Task<string> TaskVersionAsync(long id, CancellationToken ct = default) => VersionAsync($"SELECT row_ver FROM ops.hk_task WHERE task_id = {id};", ct);

    public Task<string> BlockVersionAsync(int roomId, CancellationToken ct = default) => VersionAsync($"SELECT row_ver FROM ops.room_block WHERE room_id = {roomId} AND released_utc IS NULL;", ct);

    public async Task<long> FolioOfAsync(long reservationId, CancellationToken ct = default) =>
        Convert.ToInt64(await Db.ScalarAsync($"SELECT folio_id FROM bill.folio WHERE reservation_id = {reservationId};", ct), CultureInfo.InvariantCulture);

    /// <summary>P4 stand-in: charges as the prototype posted them (amount + tax snapshot).</summary>
    public async Task PostLinesAsync(long folioId, IEnumerable<GoldenLine> lines, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        foreach (var l in lines)
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT bill.charge(folio_id, kind, service_date, label, amount_paise, tax_bps, tax_paise) VALUES ({folioId}, '{l.Kind}', '{l.Date}', N'{l.Kind} {l.Date}', {l.AmountPaise}, {l.TaxBps}, {l.AmountPaise * l.TaxBps / 10000});");
        await Db.ExecAsync(sb.ToString(), ct);
    }

    /// <summary>P4 stand-in: a receipt row.</summary>
    public Task ReceiptAsync(long folioId, string kind, string method, long amountPaise, CancellationToken ct = default) =>
        Db.ExecAsync($"INSERT pay.receipt(folio_id, kind, method, amount_paise, business_date) VALUES ({folioId}, '{kind}', '{method}', {amountPaise}, '2026-10-24');", ct);
}
