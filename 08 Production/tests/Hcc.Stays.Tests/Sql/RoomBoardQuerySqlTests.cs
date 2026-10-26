using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Hcc.Stays.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Stays.Tests.Sql;

/// <summary>
/// A09-18 (Wave C4): the room board statement. The next arrival per room used to be a per-room TOP (1) that walked every room
/// night the room ever had; <see cref="SqlHousekeepingReadStore.BoardSql"/> reads the Confirmed reservations once from the
/// filtered index IX_reservation_confirmed (migration 3930). These tests prove it returns the same rows as the old statement,
/// column for column, and that its cost no longer follows the history.
/// </summary>
[Trait("Category", "Sql")]
public partial class RoomBoardQuerySqlTests : IAsyncLifetime
{
    /// <summary>The board statement as shipped at 70c2713, kept only to prove the rewrite returns the same rows.</summary>
    private const string LegacyBoardSql = """
        SELECT r.room_id, r.room_no, t.code, r.hk_status, r.row_ver,
               b.block_id, b.room_id, b.from_date, b.to_date, b.reason, b.source, b.row_ver,
               cur.guest_name, cur.arrival, cur.departure,
               nxt.guest_name, nxt.arrival, nxt.departure
        FROM inv.room r
        JOIN inv.room_type t ON t.room_type_id = r.room_type_id
        LEFT JOIN ops.room_block b ON b.room_id = r.room_id AND b.released_utc IS NULL
        OUTER APPLY (SELECT TOP (1) res.guest_name, s.arrival, s.departure
                     FROM gst.stay_segment seg JOIN gst.stay s ON s.stay_id = seg.stay_id JOIN inv.reservation res ON res.reservation_id = s.reservation_id
                     WHERE seg.room_id = r.room_id AND seg.to_date IS NULL) cur
        OUTER APPLY (SELECT TOP (1) res.guest_name, res.arrival, res.departure
                     FROM inv.reservation res
                     WHERE res.status = 'Confirmed' AND EXISTS (SELECT 1 FROM inv.room_night n WHERE n.reservation_id = res.reservation_id AND n.room_id = r.room_id)
                     ORDER BY res.arrival, res.reservation_id) nxt
        WHERE r.property_id = @p
        ORDER BY r.room_no;
        """;

    /// <summary>Page reads the new statement may take on the history fixture (24 rooms, two years of past bookings, 72 future
    /// ones). It takes about 500; the old statement takes several thousand (its plan varies), so the margin is wide both ways.</summary>
    private const long ReadsCeiling = 2_000;

    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await P3World.CreateAsync(Ct);

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    [GeneratedRegex(@"logical reads (\d+)")]
    private static partial Regex LogicalReads();

    private int Room(string no) => _w.Seed.Rooms[no];

    private Task ExecAsync(FormattableString sql) => _w.Db.ExecAsync(FormattableString.Invariant(sql), Ct);

    private async Task<long> IdAsync(FormattableString sql) => Convert.ToInt64(await _w.Db.ScalarAsync(FormattableString.Invariant(sql), Ct), CultureInfo.InvariantCulture);

    /// <summary>A reservation (property 1 unless given) with one room night per entry of <paramref name="nights"/> as (room id, night).</summary>
    private async Task<long> BookingAsync(string no, string guest, string status, string arrival, string departure, (int Room, string Night)[] nights, int? propertyId = null)
    {
        var id = await IdAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status) OUTPUT INSERTED.reservation_id
            VALUES ({propertyId ?? _w.PropertyId}, '{no}', N'{guest}', '{arrival}', '{departure}', '{status}');
            """);
        var sb = new StringBuilder();
        foreach (var (room, night) in nights)
            sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room_night(room_id, night, reservation_id) VALUES ({room}, '{night}', {id});");
        if (sb.Length > 0) await _w.Db.ExecAsync(sb.ToString(), Ct);
        return id;
    }

    private static (int, string)[] Nights(int room, string from, int count) =>
        Enumerable.Range(0, count).Select(i => (room, DateOnly.Parse(from, CultureInfo.InvariantCulture).AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToArray();

    /// <summary>A stay with its room segments (to_date NULL = the open segment); CheckedOut when every segment is closed.</summary>
    private async Task StayAsync(long reservationId, string arrival, string departure, params (int Room, string From, string? To)[] segments)
    {
        var open = segments.Any(s => s.To is null);
        var stayId = await IdAsync($"""
            INSERT gst.guest(property_id, full_name, phone, email, address, nationality, id_type, id_last4, verified, origin, created_utc)
            VALUES ({_w.PropertyId}, N'Board guest {reservationId} (fictional)', '+91 98200 77{reservationId % 1000:000}', 'board{reservationId}@example.test', N'Latur (fictional)', N'Indian', N'Passport (demo)', 'B{reservationId % 1000:000}', 1, 'manual', '2026-09-01T09:00:00');
            DECLARE @g BIGINT = SCOPE_IDENTITY();
            INSERT bill.folio(reservation_id) VALUES ({reservationId});
            DECLARE @f BIGINT = SCOPE_IDENTITY();
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
            OUTPUT INSERTED.stay_id
            VALUES ({_w.PropertyId}, {reservationId}, @g, @f, '{(open ? "InHouse" : "CheckedOut")}', '{arrival}', '{departure}', '{arrival}', '{arrival}T09:00:00', {Users.Reception},
                    {(open ? "NULL" : "'" + departure + "'")}, {(open ? "NULL" : "'" + departure + "T10:00:00'")}, {(open ? "NULL" : Users.Reception.ToString(CultureInfo.InvariantCulture))});
            """);
        byte seq = 0;
        foreach (var (room, from, to) in segments)
            await ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES ({stayId}, {++seq}, {room}, '{from}', {(to is null ? "NULL" : "'" + to + "'")});");
    }

    private async Task<SqlConnection> OpenAsync() => await _w.Db.Platform.Connections.OpenAsync(Ct);

    private static SqlCommand Board(string sql, SqlConnection conn, int propertyId)
    {
        var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return cmd;
    }

    /// <summary>Every column of every row, in order (row versions as base64, dates as yyyy-MM-dd, NULL spelled out).</summary>
    private async Task<List<string>> RowsAsync(string sql, int propertyId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Board(sql, conn, propertyId);
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await rd.ReadAsync(Ct))
        {
            var cells = new string[rd.FieldCount];
            for (var i = 0; i < rd.FieldCount; i++)
            {
                cells[i] = rd.IsDBNull(i) ? "NULL" : rd.GetValue(i) switch
                {
                    byte[] b => Convert.ToBase64String(b),
                    DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
                };
            }

            rows.Add(string.Join(" | ", cells));
        }

        return rows;
    }

    /// <summary>Logical page reads (SET STATISTICS IO, warm cache) and the actual plan XML of one board read.</summary>
    private async Task<(long Reads, string Plan)> MeasureAsync(string sql)
    {
        await using var conn = await OpenAsync();
        var messages = new List<string>();
        conn.InfoMessage += (_, e) => { foreach (SqlError x in e.Errors) messages.Add(x.Message); };
        await using (var warm = Board(sql, conn, _w.PropertyId))
        await using (var rd = await warm.ExecuteReaderAsync(Ct))
            while (await rd.ReadAsync(Ct)) { }
        await using (var on = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS XML ON;", conn)) await on.ExecuteNonQueryAsync(Ct);
        var plan = new StringBuilder();
        messages.Clear();
        await using (var cmd = Board(sql, conn, _w.PropertyId))
        await using (var rd = await cmd.ExecuteReaderAsync(Ct))
        {
            do
            {
                var isPlan = rd.FieldCount == 1 && rd.GetName(0).Contains("Showplan", StringComparison.Ordinal);
                while (await rd.ReadAsync(Ct)) if (isPlan) plan.Append(rd.GetString(0));
            }
            while (await rd.NextResultAsync(Ct));
        }

        await using (var off = new SqlCommand("SET STATISTICS IO OFF; SET STATISTICS XML OFF;", conn)) await off.ExecuteNonQueryAsync(Ct);
        var reads = messages.SelectMany(m => LogicalReads().Matches(m)).Sum(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        return (reads, plan.ToString());
    }

    [Fact]
    public async Task Board_returns_the_same_rows_as_the_old_statement_for_past_current_and_future_stays_blocks_and_holds()
    {
        // History: departed stays (closed segments) and their room nights on several rooms.
        foreach (var (no, k) in new[] { ("101", 0), ("105", 1), ("106", 2), ("203", 3), ("205", 4), ("304", 5) })
        {
            var d = 1 + k * 3;
            var arrival = $"2026-09-{d:00}";
            var departure = $"2026-09-{d + 2:00}";
            var id = await BookingAsync($"HIST-{no}", $"Departed guest {no} (fictional)", "Departed", arrival, departure, Nights(Room(no), arrival, 2));
            await StayAsync(id, arrival, departure, (Room(no), arrival, departure));
        }

        // Current: a guest who moved from 106 to 105 (closed segment on 106, open on 105) — 106 has no current guest.
        var moved = await BookingAsync("MOVE-105", "Moved guest (fictional)", "InHouse", "2026-10-22", "2026-10-27",
            [(Room("106"), "2026-10-22"), .. Nights(Room("105"), "2026-10-23", 4)]);
        await StayAsync(moved, "2026-10-22", "2026-10-27", (Room("106"), "2026-10-22", "2026-10-23"), (Room("105"), "2026-10-23", null));

        // Future: ties on arrival broken by id, an earlier arrival booked later, one booking split over two rooms, a stale
        // Confirmed booking (arrival in the past) the board still shows, a Held booking and a no-show with nights (not shown),
        // a cancellation without nights, and a booking right after the current guest of 101 leaves.
        await BookingAsync("TIE-1", "Tie first (fictional)", "Confirmed", "2026-11-10", "2026-11-14", Nights(Room("105"), "2026-11-12", 2));
        await BookingAsync("TIE-2", "Tie second (fictional)", "Confirmed", "2026-11-10", "2026-11-12", Nights(Room("105"), "2026-11-10", 2));
        await BookingAsync("LATE-106", "Booked first, arrives later (fictional)", "Confirmed", "2026-11-05", "2026-11-07", Nights(Room("106"), "2026-11-05", 2));
        await BookingAsync("EARLY-106", "Booked later, arrives first (fictional)", "Confirmed", "2026-11-01", "2026-11-03", Nights(Room("106"), "2026-11-01", 2));
        await BookingAsync("SPLIT", "Split over two rooms (fictional)", "Confirmed", "2026-11-20", "2026-11-23",
            [(Room("107"), "2026-11-20"), (Room("108"), "2026-11-21"), (Room("108"), "2026-11-22")]);
        await BookingAsync("LATER-108", "After the split (fictional)", "Confirmed", "2026-12-01", "2026-12-03", Nights(Room("108"), "2026-12-01", 2));
        await BookingAsync("HELD-203", "Held guest (fictional)", "Held", "2026-10-28", "2026-10-30", Nights(Room("203"), "2026-10-28", 2));
        await BookingAsync("CONF-203", "Confirmed after a hold (fictional)", "Confirmed", "2026-11-15", "2026-11-17", Nights(Room("203"), "2026-11-15", 2));
        await BookingAsync("NOSHOW-204", "No-show guest (fictional)", "NoShow", "2026-10-24", "2026-10-25", Nights(Room("204"), "2026-10-24", 1));
        await BookingAsync("STALE-205", "Stale confirmed (fictional)", "Confirmed", "2026-10-20", "2026-10-22", Nights(Room("205"), "2026-10-20", 2));
        await BookingAsync("CXL-303", "Cancelled guest (fictional)", "Cancelled", "2026-11-02", "2026-11-04", []);
        await BookingAsync("NEXT-101", "Next guest of 101 (fictional)", "Confirmed", "2026-10-26", "2026-10-28", Nights(Room("101"), "2026-10-26", 2));
        await BookingAsync("FAR-208", "Far future (fictional)", "Confirmed", "2027-01-05", "2027-01-07", Nights(Room("208"), "2027-01-05", 2));
        await BookingAsync("NEAR-208", "Near future (fictional)", "Confirmed", "2026-12-01", "2026-12-02", Nights(Room("208"), "2026-12-01", 1));

        // Blocks: 308 (BASE, open-ended); a dated block on 304 with a released one before it; a released block on 106 only.
        await ExecAsync($"""
            INSERT ops.room_block(property_id, room_id, from_date, to_date, reason, source, created_by, created_utc, released_utc, released_by) VALUES
              ({_w.PropertyId}, {Room("304")}, '2026-08-01', '2026-08-03', N'Old leak (fictional)', 'manual', {Users.Supervisor}, '2026-07-30T08:00:00', '2026-08-03T08:00:00', {Users.Supervisor}),
              ({_w.PropertyId}, {Room("304")}, '2026-11-01', '2026-11-03', N'Repaint (fictional)', 'maintenance', {Users.Supervisor}, '2026-10-24T08:00:00', NULL, NULL),
              ({_w.PropertyId}, {Room("106")}, '2026-09-10', '2026-09-12', N'Old AC repair (fictional)', 'manual', {Users.Supervisor}, '2026-09-09T08:00:00', '2026-09-12T08:00:00', {Users.Supervisor});
            """);

        // A second property with its own room and booking: neither board sees the other's rooms or bookings.
        var p2 = (int)await IdAsync($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('SECOND', N'Second Residency (fictional)', 'Asia/Kolkata', '2026-10-24');");
        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({p2}, 'Classic', 2);");
        var r2 = (int)await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id SELECT {p2}, '101', room_type_id FROM inv.room_type WHERE property_id = {p2};");
        await BookingAsync("P2-NEXT", "Second property guest (fictional)", "Confirmed", "2026-11-01", "2026-11-02", Nights(r2, "2026-11-01", 1), p2);

        foreach (var p in new[] { _w.PropertyId, p2 })
        {
            var legacy = await RowsAsync(LegacyBoardSql, p);
            var current = await RowsAsync(SqlHousekeepingReadStore.BoardSql, p);
            Assert.Equal(legacy, current);
        }

        // The fixture really exercises every case (read through the store, as the board endpoint does).
        var board = (await _w.HkReads.BoardAsync(_w.PropertyId, Ct)).ToDictionary(r => r.RoomNo);
        Assert.Equal(24, board.Count);
        Assert.Equal(("Tie first (fictional)", "2026-11-10"), (board["105"].Next!.Name, board["105"].Next!.Arrival));
        Assert.Equal("Moved guest (fictional)", board["105"].Current!.Name);
        Assert.Null(board["106"].Current);
        Assert.Equal("Booked later, arrives first (fictional)", board["106"].Next!.Name);
        Assert.Equal("Split over two rooms (fictional)", board["107"].Next!.Name);
        Assert.Equal("Split over two rooms (fictional)", board["108"].Next!.Name);
        Assert.Equal("Confirmed after a hold (fictional)", board["203"].Next!.Name);
        Assert.Null(board["204"].Next);
        Assert.Equal("Stale confirmed (fictional)", board["205"].Next!.Name);
        Assert.Equal(("Ananya Rao", "Next guest of 101 (fictional)"), (board["101"].Current!.Name, board["101"].Next!.Name));
        Assert.Equal("Near future (fictional)", board["208"].Next!.Name);
        Assert.Equal("Ishaan Bose", board["103"].Next!.Name);
        Assert.Null(board["303"].Next);
        Assert.Equal(("Repaint (fictional)", "maintenance"), (board["304"].Block!.Reason, board["304"].Block!.Source));
        Assert.Null(board["106"].Block);
        Assert.NotNull(board["308"].Block);
        var second = Assert.Single(await _w.HkReads.BoardAsync(p2, Ct));
        Assert.Equal("Second property guest (fictional)", second.Next!.Name);
    }

    [Fact]
    public async Task Board_reads_use_the_confirmed_index_and_stay_flat_with_two_years_of_history()
    {
        // Two years of departed one-night bookings on every room (17,520 bookings and room nights) and 72 future ones.
        var p = _w.PropertyId;
        await ExecAsync($"""
            WITH n AS (SELECT TOP (730) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            SELECT {p}, CONCAT('HY-', r.room_no, '-', n.k), N'History guest (fictional)', DATEADD(day, n.k, '2024-06-01'), DATEADD(day, n.k + 1, '2024-06-01'), 'Departed'
            FROM inv.room r CROSS JOIN n WHERE r.property_id = {p};
            WITH j AS (SELECT n FROM (VALUES (0), (1), (2)) v(n))
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status)
            SELECT {p}, CONCAT('FY-', r.room_no, '-', j.n), N'Future guest (fictional)', DATEADD(day, 20 * j.n, '2026-12-01'), DATEADD(day, 20 * j.n + 2, '2026-12-01'), 'Confirmed'
            FROM inv.room r CROSS JOIN j WHERE r.property_id = {p};
            INSERT inv.room_night(room_id, night, reservation_id)
            SELECT r.room_id, DATEADD(day, d.n, x.arrival), x.reservation_id
            FROM inv.reservation x JOIN inv.room r ON r.property_id = x.property_id AND r.room_no = PARSENAME(REPLACE(x.public_no, '-', '.'), 2)
            CROSS JOIN (VALUES (0), (1)) d(n)
            WHERE (x.public_no LIKE 'HY-%' OR x.public_no LIKE 'FY-%') AND DATEADD(day, d.n, x.arrival) < x.departure;
            UPDATE STATISTICS inv.reservation;
            UPDATE STATISTICS inv.room_night;
            """);
        Assert.Equal(17_520 + 72 * 2, await _w.Db.CountAsync("SELECT COUNT(*) FROM inv.room_night WHERE reservation_id IN (SELECT reservation_id FROM inv.reservation WHERE public_no LIKE 'HY-%' OR public_no LIKE 'FY-%');", Ct));

        var (reads, plan) = await MeasureAsync(SqlHousekeepingReadStore.BoardSql);
        var (legacyReads, _) = await MeasureAsync(LegacyBoardSql);
        TestContext.Current.TestOutputHelper?.WriteLine($"Room board page reads on the history fixture: new statement {reads}, old statement {legacyReads} (ceiling {ReadsCeiling}).");
        Assert.Equal(await RowsAsync(LegacyBoardSql, p), await RowsAsync(SqlHousekeepingReadStore.BoardSql, p));
        Assert.Contains("[IX_reservation_confirmed]", plan, StringComparison.Ordinal);
        Assert.True(reads < ReadsCeiling, $"The board read {reads} pages (ceiling {ReadsCeiling}); the old statement read {legacyReads}.");
        Assert.True(legacyReads > ReadsCeiling, $"The old statement read only {legacyReads} pages; the fixture no longer shows the history cost.");
    }
}
