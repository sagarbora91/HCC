using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Application.Reservations;
using Hcc.Contracts.Reservations;
using Hcc.Infrastructure.SqlServer.Billing;
using Hcc.Infrastructure.SqlServer.Reservations;
using Hcc.Stays.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Stays.Tests.Sql;

/// <summary>
/// Wave D5 (the list-speed leftover of A09-18 / Wave C4): the bookings list the app loads (<c>GET /reservations?view=range</c>,
/// departure after the business date − 180 days, pages of 500) and the cashier's folio list (<c>GET /cashier/folios</c>) used to scan
/// every booking the hotel ever had. <see cref="SqlReservationStore.ListSql"/> now seeks the window on <c>IX_reservation_departure</c>
/// and <see cref="SqlBillingStore.DeskFoliosSql"/> finds its three groups through their own indexes (migration 4030). These tests prove
/// both return the same rows as the statements shipped at 125713d, result set for result set and column for column, and that their
/// cost no longer follows the history.
/// </summary>
[Trait("Category", "Sql")]
public partial class DeskListQuerySqlTests : IAsyncLifetime
{
    /// <summary>The cashier folio list as shipped at 125713d, kept only to prove the rewrite returns the same rows.</summary>
    private const string LegacyDeskFoliosSql = """
        DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
        SELECT @bd;
        SELECT r.reservation_id, r.public_no, r.status, r.guest_name, COALESCE(cur.room_id, r.room_id), COALESCE(cur.room_no, rm.room_no), t.code,
               r.arrival, r.departure, r.adults, r.children, r.source, r.nightly_rate_paise, s.stay_id, f.folio_id, r.company_name, r.company_gstin
        FROM inv.reservation r
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          LEFT JOIN inv.room_type t ON t.room_type_id = r.room_type_id
          LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
          LEFT JOIN bill.folio f ON f.reservation_id = r.reservation_id
          OUTER APPLY (SELECT TOP (1) sg.room_id, sr.room_no FROM gst.stay_segment sg JOIN inv.room sr ON sr.room_id = sg.room_id
                       WHERE sg.stay_id = s.stay_id AND sg.to_date IS NULL) cur
        WHERE r.property_id = @p
          AND (r.status = 'InHouse'
               OR (r.status = 'Confirmed' AND r.arrival <= @bd AND r.departure > @bd)
               OR (r.status = 'Departed' AND s.checked_out_date = @bd))
        ORDER BY COALESCE(cur.room_no, rm.room_no), r.reservation_id;
        """;

    /// <summary>The bookings list statement as SqlReservationStore built it at 125713d (same parameters as today).</summary>
    private static string LegacyListSql(ReservationListQuery query)
    {
        var where = new List<string> { "r.property_id = @p" };
        switch (query.View)
        {
            case ReservationViews.Arrivals:
                where.Add("r.status IN ('Held', 'Confirmed') AND r.arrival <= @bd AND r.departure > @bd");
                break;
            case ReservationViews.InHouse:
                where.Add("r.status = 'InHouse'");
                break;
            case ReservationViews.Departures:
                where.Add("r.status = 'InHouse' AND r.departure <= @bd");
                break;
            case ReservationViews.Active:
                where.Add("r.status IN ('Held', 'Confirmed', 'InHouse')");
                break;
        }
        if (query.From is not null) where.Add("r.departure > @from");
        if (query.To is not null) where.Add("r.arrival < @to");
        if (query.Statuses.Count > 0)
            where.Add($"CASE WHEN r.status = 'Held' AND r.hold_until_utc IS NOT NULL AND r.hold_until_utc <= @now THEN 'Expired' ELSE r.status END IN ({string.Join(", ", query.Statuses.Select((_, i) => "@s" + i))})");
        if (!string.IsNullOrWhiteSpace(query.Text))
            where.Add("(r.guest_name LIKE @t ESCAPE '!' OR r.public_no LIKE @t ESCAPE '!' OR r.phone LIKE @t ESCAPE '!' OR rm.room_no = @tx)");
        var from = $"FROM inv.reservation r JOIN inv.room rm ON rm.room_id = r.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE {string.Join(" AND ", where)}";
        return $"""
            DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
            SELECT @bd, COUNT(*) {from};
            CREATE TABLE #pg(ord INT IDENTITY PRIMARY KEY, reservation_id BIGINT NOT NULL);
            INSERT #pg(reservation_id) SELECT r.reservation_id {from} ORDER BY r.arrival DESC, r.reservation_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            SELECT r.reservation_id, r.public_no, r.row_ver, r.status, r.guest_name, r.phone, r.room_id, rm.room_no, r.room_type_id, t.code,
              r.arrival, r.departure, r.adults, r.children, r.source, r.nightly_rate_paise, r.hold_until_utc, r.group_block_id,
              r.restriction_override, r.cancel_reason, r.notes,
              COALESCE(s.guest_id, g.guest_id), CASE WHEN g.reservation_id IS NULL THEN 0 ELSE 1 END, s.stay_id, s.row_ver, s.status, s.checked_in_date, s.checked_out_date,
              r.company_name, r.company_gstin, r.company_id
            FROM #pg pg JOIN inv.reservation r ON r.reservation_id = pg.reservation_id
              JOIN inv.room rm ON rm.room_id = r.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id
              LEFT JOIN gst.registration g ON g.reservation_id = r.reservation_id
              LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
            ORDER BY pg.ord;
            SELECT s.reservation_id, rm.room_no, sg.from_date, sg.to_date
            FROM #pg pg JOIN gst.stay s ON s.reservation_id = pg.reservation_id
              JOIN gst.stay_segment sg ON sg.stay_id = s.stay_id JOIN inv.room rm ON rm.room_id = sg.room_id
            ORDER BY s.reservation_id, sg.seq;
            DROP TABLE #pg;
            """;
    }

    /// <summary>Page reads of <c>inv.reservation</c> that the count and page statements of one full bookings load (every page of the
    /// 180-day window, as the app asks for it) may take on the three-year fixture. The new statements seek the window (about 40 per
    /// page); the old ones scan the table twice per page (several thousand), so the margin is wide both ways.</summary>
    private const long WindowReadsCeiling = 600;

    /// <summary>Page reads the cashier folio list may take on the history fixture (the old statement scans two whole tables).</summary>
    private const long FolioReadsCeiling = 400;

    private static readonly DateOnly BusinessDate = new(2026, 10, 24);

    /// <summary>The window the app loads (sync/reservations.ts BOOKINGS_WINDOW_DAYS = 180).</summary>
    private static readonly DateOnly WindowFrom = BusinessDate.AddDays(-180);

    private static readonly DateTime Now = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private P3World _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await P3World.CreateAsync(Ct);
        // The BASE seed books without a room (a P2 stand-in); the bookings list only lists bookings with a room, so give them theirs.
        await ExecAsync($"""
            UPDATE r SET room_id = n.room_id, room_type_id = rm.room_type_id
            FROM inv.reservation r CROSS APPLY (SELECT TOP (1) x.room_id FROM inv.room_night x WHERE x.reservation_id = r.reservation_id ORDER BY x.night) n
              JOIN inv.room rm ON rm.room_id = n.room_id
            WHERE r.room_id IS NULL;
            """);
    }

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    [GeneratedRegex(@"logical reads (\d+)")]
    private static partial Regex LogicalReads();

    [GeneratedRegex(@"^Table '([^']+)'\. Scan count \d+, logical reads (\d+)")]
    private static partial Regex TableReads();

    private int Room(string no) => _w.Seed.Rooms[no];

    private Task ExecAsync(FormattableString sql) => _w.Db.ExecAsync(FormattableString.Invariant(sql), Ct);

    private async Task<long> IdAsync(FormattableString sql) => Convert.ToInt64(await _w.Db.ScalarAsync(FormattableString.Invariant(sql), Ct), CultureInfo.InvariantCulture);

    private static string Q(string? v) => v is null ? "NULL" : "N'" + v.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>A booking in <paramref name="room"/> (property 1 unless given) with its room nights; extra columns optional.</summary>
    private async Task<long> BookingAsync(string no, string guest, string status, string arrival, string departure, string? room,
        int? propertyId = null, int? roomId = null, string? holdUntil = null, string? phone = null, string? company = null, string? gstin = null)
    {
        var p = propertyId ?? _w.PropertyId;
        var rid = roomId ?? (room is null ? null : Room(room));
        var id = await IdAsync($"""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, hold_until_utc, phone, company_name, company_gstin, nightly_rate_paise, source)
            OUTPUT INSERTED.reservation_id
            SELECT {p}, '{no}', {Q(guest)}, '{arrival}', '{departure}', '{status}', {(rid is null ? "NULL" : rid.Value.ToString(CultureInfo.InvariantCulture))},
                   {(rid is null ? "NULL" : $"(SELECT room_type_id FROM inv.room WHERE room_id = {rid})")}, {(holdUntil is null ? "NULL" : "'" + holdUntil + "'")},
                   {Q(phone)}, {Q(company)}, {Q(gstin)}, 250000, 'Direct';
            """);
        if (rid is { } r && status is "Held" or "Confirmed" or "InHouse")
        {
            var sb = new StringBuilder();
            for (var n = DateOnly.Parse(arrival, CultureInfo.InvariantCulture); n < DateOnly.Parse(departure, CultureInfo.InvariantCulture); n = n.AddDays(1))
                sb.AppendLine(CultureInfo.InvariantCulture, $"INSERT inv.room_night(room_id, night, reservation_id) VALUES ({r}, '{n:yyyy-MM-dd}', {id});");
            await _w.Db.ExecAsync(sb.ToString(), Ct);
        }

        return id;
    }

    /// <summary>A registered stay with its folio and room segments (to_date NULL = open); checked out on <paramref name="checkedOut"/> when given.</summary>
    private async Task StayAsync(long reservationId, string arrival, string departure, string? checkedOut, params (int Room, string From, string? To)[] segments)
    {
        var p = await IdAsync($"SELECT property_id FROM inv.reservation WHERE reservation_id = {reservationId};");
        var stayId = await IdAsync($"""
            INSERT gst.guest(property_id, full_name, phone, email, address, nationality, id_type, id_last4, verified, origin, created_utc)
            VALUES ({p}, N'List guest {reservationId} (fictional)', '+91 98200 66{reservationId % 1000:000}', 'list{reservationId}@example.test', N'Latur (fictional)', N'Indian', N'Passport (demo)', 'L{reservationId % 1000:000}', 1, 'manual', '2026-09-01T09:00:00');
            DECLARE @g BIGINT = SCOPE_IDENTITY();
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by) VALUES ({reservationId}, @g, '{arrival}T09:00:00', {Users.Reception});
            INSERT bill.folio(reservation_id) VALUES ({reservationId});
            DECLARE @f BIGINT = SCOPE_IDENTITY();
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
            OUTPUT INSERTED.stay_id
            VALUES ({p}, {reservationId}, @g, @f, '{(checkedOut is null ? "InHouse" : "CheckedOut")}', '{arrival}', '{departure}', '{arrival}', '{arrival}T09:00:00', {Users.Reception},
                    {(checkedOut is null ? "NULL" : "'" + checkedOut + "'")}, {(checkedOut is null ? "NULL" : "'" + checkedOut + "T10:00:00'")}, {(checkedOut is null ? "NULL" : Users.Reception.ToString(CultureInfo.InvariantCulture))});
            """);
        byte seq = 0;
        foreach (var (room, from, to) in segments)
            await ExecAsync($"INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date) VALUES ({stayId}, {++seq}, {room}, '{from}', {(to is null ? "NULL" : "'" + to + "'")});");
    }

    private async Task<SqlConnection> OpenAsync() => await _w.Db.Platform.Connections.OpenAsync(Ct);

    private static Action<SqlParameterCollection> ListParams(int propertyId, ReservationListQuery q) => ps => SqlReservationStore.BindList(ps, propertyId, q, Now);

    private static Action<SqlParameterCollection> FolioParams(int propertyId) => ps => ps.Add("@p", SqlDbType.Int).Value = propertyId;

    /// <summary>Every result set, every column of every row, in order (row versions as base64, dates as yyyy-MM-dd, NULL spelled out).</summary>
    private async Task<List<List<string>>> RowsAsync(string sql, Action<SqlParameterCollection> bind)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        bind(cmd.Parameters);
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        var sets = new List<List<string>>();
        do
        {
            var rows = new List<string>();
            while (await rd.ReadAsync(Ct))
            {
                var cells = new string[rd.FieldCount];
                for (var i = 0; i < rd.FieldCount; i++)
                {
                    cells[i] = rd.IsDBNull(i) ? "NULL" : rd.GetValue(i) switch
                    {
                        byte[] b => Convert.ToBase64String(b),
                        DateTime d => d.ToString(d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "O", CultureInfo.InvariantCulture),
                        var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
                    };
                }

                rows.Add(string.Join(" | ", cells));
            }

            sets.Add(rows);
        }
        while (await rd.NextResultAsync(Ct));
        return sets;
    }

    /// <summary>Logical page reads (SET STATISTICS IO, warm cache) in all and per table in statement order, the actual plan XML of one
    /// run of the statement, and the elapsed time of a further run without statistics.</summary>
    private async Task<(long Reads, string Plan, double Ms, List<(string Table, long Reads)> Tables)> MeasureAsync(string sql, Action<SqlParameterCollection> bind)
    {
        await using var conn = await OpenAsync();
        var messages = new List<string>();
        conn.InfoMessage += (_, e) => { foreach (SqlError x in e.Errors) messages.Add(x.Message); };
        async Task<string> RunAsync()
        {
            await using var cmd = new SqlCommand(sql, conn);
            bind(cmd.Parameters);
            var plan = new StringBuilder();
            await using var rd = await cmd.ExecuteReaderAsync(Ct);
            do
            {
                var isPlan = rd.FieldCount == 1 && rd.GetName(0).Contains("Showplan", StringComparison.Ordinal);
                while (await rd.ReadAsync(Ct)) if (isPlan) plan.Append(rd.GetString(0));
            }
            while (await rd.NextResultAsync(Ct));
            return plan.ToString();
        }

        await RunAsync();
        await using (var on = new SqlCommand("SET STATISTICS IO ON; SET STATISTICS XML ON;", conn)) await on.ExecuteNonQueryAsync(Ct);
        messages.Clear();
        var plan = await RunAsync();
        await using (var off = new SqlCommand("SET STATISTICS IO OFF; SET STATISTICS XML OFF;", conn)) await off.ExecuteNonQueryAsync(Ct);
        var reads = messages.SelectMany(m => LogicalReads().Matches(m)).Sum(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        var tables = messages.Select(m => TableReads().Match(m)).Where(m => m.Success)
            .Select(m => (m.Groups[1].Value, long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await RunAsync();
        return (reads, plan, watch.Elapsed.TotalMilliseconds, tables);
    }

    /// <summary>One bookings load as the app does it: every page of 500 of the 180-day window (sync/reservations.ts loadBookings).
    /// <c>Window</c> = the reads of <c>inv.reservation</c> by the count and the page statements (the first two statements that read
    /// it in each batch), the part that used to scan the whole table.</summary>
    private async Task<(long Reads, long Window, string Plan, double Ms, int Pages)> MeasureLoadAsync(Func<ReservationListQuery, string> sql)
    {
        var total = await _w.Db.CountAsync(FormattableString.Invariant($"SELECT COUNT(*) FROM inv.reservation r JOIN inv.room rm ON rm.room_id = r.room_id WHERE r.property_id = {_w.PropertyId} AND r.departure > '{WindowFrom:yyyy-MM-dd}';"), Ct);
        var pages = Math.Max(1, (total + 499) / 500);
        long reads = 0, window = 0;
        double ms = 0;
        var plans = new StringBuilder();
        for (var page = 1; page <= pages; page++)
        {
            var q = new ReservationListQuery(ReservationViews.Range, WindowFrom, null, [], null, page, 500);
            var (r, plan, t, tables) = await MeasureAsync(sql(q), ListParams(_w.PropertyId, q));
            reads += r;
            window += tables.Where(x => x.Table == "reservation").Take(2).Sum(x => x.Reads);
            ms += t;
            plans.Append(plan);
        }

        return (reads, window, plans.ToString(), ms, pages);
    }

    /// <summary>Bookings and stays for every case both lists distinguish (on top of the BASE seed: 8 in house, 2 arriving today).</summary>
    private async Task<int> CasesAsync()
    {
        // Departed: long before the window (not listed), one leaving exactly on the window's first day (not listed: departure must be
        // after it), one the day after (listed), one in the window with a guest who moved rooms, and one who left today.
        var old = await BookingAsync("OLD-105", "Left last year (fictional)", "Departed", "2025-09-01", "2025-09-03", "105");
        await StayAsync(old, "2025-09-01", "2025-09-03", "2025-09-03", (Room("105"), "2025-09-01", "2025-09-03"));
        var edge = await BookingAsync("EDGE-106", "Left on the window's first day (fictional)", "Departed", "2026-04-25", WindowFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "106");
        await StayAsync(edge, "2026-04-25", "2026-04-27", "2026-04-27", (Room("106"), "2026-04-25", "2026-04-27"));
        var inside = await BookingAsync("IN-106", "Left the day after (fictional)", "Departed", "2026-04-26", "2026-04-28", "106", phone: "+91 98200 55501");
        await StayAsync(inside, "2026-04-26", "2026-04-28", "2026-04-28", (Room("106"), "2026-04-26", "2026-04-28"));
        var moved = await BookingAsync("MOVED-107", "Moved and left (fictional)", "Departed", "2026-09-10", "2026-09-14", "107");
        await StayAsync(moved, "2026-09-10", "2026-09-14", "2026-09-14", (Room("107"), "2026-09-10", "2026-09-11"), (Room("108"), "2026-09-11", "2026-09-14"));
        var today = await BookingAsync("TODAY-203", "Left today (fictional)", "Departed", "2026-10-21", "2026-10-24", "203", company: "Fictional Travels", gstin: "27ZZFIC0009F1Z5");
        await StayAsync(today, "2026-10-21", "2026-10-24", "2026-10-24", (Room("203"), "2026-10-21", "2026-10-24"));
        var early = await BookingAsync("EARLY-204", "Left today, booked till tomorrow (fictional)", "Departed", "2026-10-22", "2026-10-25", "204");
        await StayAsync(early, "2026-10-22", "2026-10-25", "2026-10-24", (Room("204"), "2026-10-22", "2026-10-24"));
        var yesterday = await BookingAsync("YDAY-205", "Left yesterday (fictional)", "Departed", "2026-10-20", "2026-10-23", "205");
        await StayAsync(yesterday, "2026-10-20", "2026-10-23", "2026-10-23", (Room("205"), "2026-10-20", "2026-10-23"));
        await BookingAsync("NOSTAY-208", "Departed without a stay row (fictional)", "Departed", "2026-10-22", "2026-10-24", "208");

        // In house: a long stay that arrived before the window, a guest who moved (open segment in the new room), an in-house booking
        // without a stay row (no folio yet) and one without a room.
        var longStay = await BookingAsync("LONG-303", "Long stay (fictional)", "InHouse", "2026-04-01", "2026-11-30", "303");
        await StayAsync(longStay, "2026-04-01", "2026-11-30", null, (Room("303"), "2026-04-01", null));
        var mover = await BookingAsync("MOVER-304", "Moved in house (fictional)", "InHouse", "2026-10-22", "2026-10-27", "304");
        await StayAsync(mover, "2026-10-22", "2026-10-27", null, (Room("304"), "2026-10-22", "2026-10-23"), (Room("305"), "2026-10-23", null));
        await BookingAsync("NOSTAY-306", "In house, no stay yet (fictional)", "InHouse", "2026-10-24", "2026-10-25", "306");
        await BookingAsync("NOROOM-1", "In house, no room (fictional)", "InHouse", "2026-10-24", "2026-10-25", null);

        // Arrivals and future: a stale confirmed booking still in the dates (listed by the cashier), a stale one already past its
        // departure (not), ties on arrival, a far one, holds (lapsed = Expired, live), a no-show and a cancellation.
        await BookingAsync("STALE-307", "Stale confirmed (fictional)", "Confirmed", "2026-10-22", "2026-10-26", "307");
        await BookingAsync("PAST-307", "Confirmed, dates passed (fictional)", "Confirmed", "2026-10-20", "2026-10-22", "307");
        await BookingAsync("TIE-1", "Tie first (fictional)", "Confirmed", "2026-11-10", "2026-11-12", "105");
        await BookingAsync("TIE-2", "Tie second (fictional)", "Confirmed", "2026-11-10", "2026-11-12", "106", company: "Deccan Pharma Distributors");
        await BookingAsync("FAR-208", "Far future (fictional)", "Confirmed", "2027-03-05", "2027-03-07", "208");
        await BookingAsync("HELD-OLD", "Lapsed hold (fictional)", "Held", "2026-10-24", "2026-10-26", "107", holdUntil: "2026-10-24T06:00:00");
        await BookingAsync("HELD-LIVE", "Live hold (fictional)", "Held", "2026-10-30", "2026-11-01", "108", holdUntil: "2026-10-25T06:00:00");
        await BookingAsync("NOSHOW-1", "No-show guest (fictional)", "NoShow", "2026-10-23", "2026-10-24", "305");
        await BookingAsync("CXL-1", "Cancelled guest (fictional)", "Cancelled", "2026-11-02", "2026-11-04", "306");

        // A second property with its own room, in-house guest and departure today: neither list mixes the two.
        var p2 = (int)await IdAsync($"INSERT inv.property(code, name, time_zone, business_date) OUTPUT INSERTED.property_id VALUES ('SECOND', N'Second Residency (fictional)', 'Asia/Kolkata', '2026-10-24');");
        await ExecAsync($"INSERT inv.room_type(property_id, code, max_occupancy) VALUES ({p2}, 'Classic', 2);");
        var r2 = (int)await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id SELECT {p2}, '101', room_type_id FROM inv.room_type WHERE property_id = {p2};");
        var r2b = (int)await IdAsync($"INSERT inv.room(property_id, room_no, room_type_id) OUTPUT INSERTED.room_id SELECT {p2}, '102', room_type_id FROM inv.room_type WHERE property_id = {p2};");
        var p2In = await BookingAsync("P2-IN", "Second property guest (fictional)", "InHouse", "2026-10-23", "2026-10-25", null, p2, r2);
        await StayAsync(p2In, "2026-10-23", "2026-10-25", null, (r2, "2026-10-23", null));
        var p2Out = await BookingAsync("P2-OUT", "Second property, left today (fictional)", "Departed", "2026-10-22", "2026-10-24", null, p2, r2b);
        await StayAsync(p2Out, "2026-10-22", "2026-10-24", "2026-10-24", (r2b, "2026-10-22", "2026-10-24"));
        return p2;
    }

    [Fact]
    public async Task Bookings_list_and_cashier_folio_list_return_the_same_rows_as_the_old_statements_for_every_view_filter_and_page()
    {
        var p2 = await CasesAsync();
        ReservationListQuery Q(string view, DateOnly? from = null, DateOnly? to = null, string[]? statuses = null, string? text = null, int page = 1, int size = 500) =>
            new(view, from, to, statuses ?? [], text, page, size);
        var queries = new List<ReservationListQuery>
        {
            Q(ReservationViews.Range, WindowFrom),                                  // what the app loads
            Q(ReservationViews.Range, WindowFrom, BusinessDate.AddDays(30)),
            Q(ReservationViews.Range, WindowFrom, statuses: ["Expired", "Confirmed"]),
            Q(ReservationViews.Range, WindowFrom, text: "fictional"),
            Q(ReservationViews.Range, WindowFrom, text: "105"),
            Q(ReservationViews.Range, WindowFrom, text: "55501"),
            Q(ReservationViews.Range),                                              // no dates: every booking
            Q(ReservationViews.Arrivals), Q(ReservationViews.InHouse), Q(ReservationViews.Departures), Q(ReservationViews.Active),
            Q(ReservationViews.Range, BusinessDate.AddDays(400)),                   // an empty window
        };
        for (var page = 1; page <= 9; page++) queries.Add(Q(ReservationViews.Range, WindowFrom, page: page, size: 4)); // pages of 4, past the end too

        foreach (var p in new[] { _w.PropertyId, p2 })
        {
            foreach (var q in queries)
            {
                var legacy = await RowsAsync(LegacyListSql(q), ListParams(p, q));
                Assert.Equal(legacy, await RowsAsync(SqlReservationStore.ListSql(q), ListParams(p, q)));
            }

            Assert.Equal(await RowsAsync(LegacyDeskFoliosSql, FolioParams(p)), await RowsAsync(SqlBillingStore.DeskFoliosSql, FolioParams(p)));
        }

        // The fixture really exercises the cases (read through the stores, as the endpoints do).
        var store = new SqlReservationStore(_w.Db.Platform.Connections);
        var window = await store.ListAsync(_w.PropertyId, Q(ReservationViews.Range, WindowFrom), Now, Ct);
        var listed = window.Items.Select(i => i.PublicNo).ToHashSet();
        Assert.Equal(window.Total, window.Items.Count);
        Assert.Contains("IN-106", listed);
        Assert.Contains("LONG-303", listed);
        Assert.Contains("HCC-1041", listed);
        Assert.DoesNotContain("EDGE-106", listed);
        Assert.DoesNotContain("OLD-105", listed);
        Assert.DoesNotContain("NOROOM-1", listed);
        Assert.DoesNotContain("P2-IN", listed);
        Assert.Equal(["TIE-2", "TIE-1"], window.Items.Where(i => i.PublicNo.StartsWith("TIE-", StringComparison.Ordinal)).Select(i => i.PublicNo));
        Assert.Equal("Expired", window.Items.Single(i => i.PublicNo == "HELD-OLD").Status);
        Assert.Equal(["107", "108"], window.Items.Single(i => i.PublicNo == "MOVED-107").Segments.Select(s => s.RoomNo));

        var (_, desk) = await new SqlBillingStore(_w.Db.Platform.Connections).ListDeskFoliosAsync(_w.PropertyId, Ct);
        var cashier = desk.Select(r => r.PublicNo).ToList();
        foreach (var no in new[] { "HCC-1041", "HCC-1049", "TODAY-203", "EARLY-204", "LONG-303", "MOVER-304", "NOSTAY-306", "NOROOM-1", "STALE-307" })
            Assert.Contains(no, cashier);
        foreach (var no in new[] { "YDAY-205", "NOSTAY-208", "PAST-307", "TIE-1", "HELD-OLD", "NOSHOW-1", "CXL-1", "P2-IN", "P2-OUT" })
            Assert.DoesNotContain(no, cashier);
        Assert.Equal("305", desk.Single(r => r.PublicNo == "MOVER-304").RoomNo);
        Assert.Equal(["P2-IN", "P2-OUT"], (await new SqlBillingStore(_w.Db.Platform.Connections).ListDeskFoliosAsync(p2, Ct)).Rows.Select(r => r.PublicNo).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Departed two-night stays on every room of property 1 from <paramref name="start"/>, <paramref name="perRoom"/> each,
    /// with their registration, folio, stay and closed segment (one shared fictional guest).</summary>
    private async Task HistoryAsync(string prefix, string start, int perRoom)
    {
        var p = _w.PropertyId;
        await ExecAsync($"""
            DECLARE @g BIGINT = (SELECT MIN(guest_id) FROM gst.guest WHERE email = 'history@example.test');
            IF @g IS NULL
            BEGIN
              INSERT gst.guest(property_id, full_name, phone, email, address, nationality, id_type, id_last4, verified, origin, created_utc)
              VALUES ({p}, N'History guest (fictional)', '+91 98200 99999', 'history@example.test', N'Latur (fictional)', N'Indian', N'Passport (demo)', 'H999', 1, 'manual', '2023-10-01T09:00:00');
              SET @g = SCOPE_IDENTITY();
            END;
            WITH n AS (SELECT TOP ({perRoom}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, nightly_rate_paise, source)
            SELECT {p}, CONCAT('{prefix}-', r.room_no, '-', n.k), N'History guest (fictional)', DATEADD(day, 2 * n.k, '{start}'), DATEADD(day, 2 * n.k + 2, '{start}'),
                   'Departed', r.room_id, r.room_type_id, 250000, 'Direct'
            FROM inv.room r CROSS JOIN n WHERE r.property_id = {p};
            INSERT gst.registration(reservation_id, guest_id, registered_utc, registered_by)
            SELECT reservation_id, @g, DATEADD(hour, 9, CAST(arrival AS DATETIME2(3))), {Users.Reception} FROM inv.reservation WHERE public_no LIKE '{prefix}-%';
            INSERT bill.folio(reservation_id) SELECT reservation_id FROM inv.reservation WHERE public_no LIKE '{prefix}-%';
            INSERT gst.stay(property_id, reservation_id, guest_id, folio_id, status, arrival, departure, checked_in_date, checked_in_utc, checked_in_by, checked_out_date, checked_out_utc, checked_out_by)
            SELECT {p}, r.reservation_id, @g, f.folio_id, 'CheckedOut', r.arrival, r.departure, r.arrival, DATEADD(hour, 9, CAST(r.arrival AS DATETIME2(3))), {Users.Reception},
                   r.departure, DATEADD(hour, 10, CAST(r.departure AS DATETIME2(3))), {Users.Reception}
            FROM inv.reservation r JOIN bill.folio f ON f.reservation_id = r.reservation_id WHERE r.public_no LIKE '{prefix}-%';
            INSERT gst.stay_segment(stay_id, seq, room_id, from_date, to_date)
            SELECT s.stay_id, 1, r.room_id, s.arrival, s.departure FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id WHERE r.public_no LIKE '{prefix}-%';
            UPDATE STATISTICS inv.reservation;
            UPDATE STATISTICS gst.stay;
            UPDATE STATISTICS gst.registration;
            UPDATE STATISTICS gst.stay_segment;
            UPDATE STATISTICS bill.folio;
            """);
    }

    private static string MigrationSql(string file)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Hcc.slnx"))) return File.ReadAllText(Path.Combine(d.FullName, "database", "migrations", file));
        throw new InvalidOperationException("Hcc.slnx not found above the test output folder.");
    }

    private sealed record Probe(long ListReads, long WindowReads, double ListMs, int Pages, string ListPlan, long FolioReads, double FolioMs, string FolioPlan);

    private async Task<Probe> ProbeAsync(Func<ReservationListQuery, string> listSql, string folioSql)
    {
        var list = await MeasureLoadAsync(listSql);
        var folio = await MeasureAsync(folioSql, FolioParams(_w.PropertyId));
        return new Probe(list.Reads, list.Window, list.Ms, list.Pages, list.Plan, folio.Reads, folio.Ms, folio.Plan);
    }

    private Task DropIndexesAsync() =>
        ExecAsync($"DROP INDEX IX_reservation_departure ON inv.reservation; DROP INDEX IX_reservation_inhouse ON inv.reservation; DROP INDEX IX_stay_checked_out ON gst.stay;");

    [Fact]
    public async Task Bookings_window_and_cashier_folio_list_use_their_indexes_and_stay_flat_with_three_years_of_history()
    {
        // One year of departed two-night stays on every room (each with its registration, folio, stay and segment; the last 180
        // days of them inside the app's window) and 72 future bookings; then two more years before it (about 13,000 bookings).
        var p = _w.PropertyId;
        await HistoryAsync("HA", "2025-10-24", 182);
        await ExecAsync($"""
            WITH j AS (SELECT n FROM (VALUES (0), (1), (2)) v(n))
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, room_id, room_type_id, nightly_rate_paise, source)
            SELECT {p}, CONCAT('FY-', r.room_no, '-', j.n), N'Future guest (fictional)', DATEADD(day, 20 * j.n, '2026-12-01'), DATEADD(day, 20 * j.n + 2, '2026-12-01'),
                   'Confirmed', r.room_id, r.room_type_id, 250000, 'Direct'
            FROM inv.room r CROSS JOIN j WHERE r.property_id = {p};
            UPDATE STATISTICS inv.reservation;
            """);

        // After = the new statements with migration 4030; before = the 125713d statements without the 4030 indexes (schema 3951).
        var after1 = await ProbeAsync(SqlReservationStore.ListSql, SqlBillingStore.DeskFoliosSql);
        await DropIndexesAsync();
        var before1 = await ProbeAsync(LegacyListSql, LegacyDeskFoliosSql);
        await _w.Db.ExecAsync(MigrationSql("4030_desk_list_indexes.sql"), Ct);

        await HistoryAsync("HB", "2023-10-24", 365);
        Assert.Equal(24 * 547, await _w.Db.CountAsync("SELECT COUNT(*) FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id WHERE r.public_no LIKE 'H[AB]-%';", Ct));
        var after3 = await ProbeAsync(SqlReservationStore.ListSql, SqlBillingStore.DeskFoliosSql);
        var legacyWithIndexes = await ProbeAsync(LegacyListSql, LegacyDeskFoliosSql);
        var q = new ReservationListQuery(ReservationViews.Range, WindowFrom, null, [], null, 1, 500);
        Assert.Equal(await RowsAsync(LegacyListSql(q), ListParams(p, q)), await RowsAsync(SqlReservationStore.ListSql(q), ListParams(p, q)));
        Assert.Equal(await RowsAsync(LegacyDeskFoliosSql, FolioParams(p)), await RowsAsync(SqlBillingStore.DeskFoliosSql, FolioParams(p)));
        await DropIndexesAsync();
        var before3 = await ProbeAsync(LegacyListSql, LegacyDeskFoliosSql);
        static string Ms(double ms) => ms.ToString("0.0", CultureInfo.InvariantCulture);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Bookings load ({after3.Pages} pages of 500), all reads / reads of inv.reservation by the count and page statements / ms: "
            + $"1 year before {before1.ListReads} / {before1.WindowReads} / {Ms(before1.ListMs)}, after {after1.ListReads} / {after1.WindowReads} / {Ms(after1.ListMs)}; "
            + $"3 years before {before3.ListReads} / {before3.WindowReads} / {Ms(before3.ListMs)}, after {after3.ListReads} / {after3.WindowReads} / {Ms(after3.ListMs)} "
            + $"(old statement with the 4030 indexes {legacyWithIndexes.ListReads} / {legacyWithIndexes.WindowReads}; window ceiling {WindowReadsCeiling}). "
            + $"Cashier folio list, reads / ms: 1 year before {before1.FolioReads} / {Ms(before1.FolioMs)}, after {after1.FolioReads} / {Ms(after1.FolioMs)}; "
            + $"3 years before {before3.FolioReads} / {Ms(before3.FolioMs)}, after {after3.FolioReads} / {Ms(after3.FolioMs)} (old statement with the indexes {legacyWithIndexes.FolioReads}; ceiling {FolioReadsCeiling}). "
            + $"{await _w.Db.CountAsync("SELECT COUNT(*) FROM inv.reservation;", Ct)} bookings.");

        // Bookings list: the count and the page seek the window, so they read the same with one year or three years behind them; the
        // old statements scan the whole table twice per page. The page's own rows (500 bookings with their stay, registration and
        // room segments) are joined by the optimizer's cheapest choice, a scan while the tables are small and one seek per row after
        // that, so they cost at most what 500 seeks cost and stop growing; the whole load stays below the old one.
        Assert.Contains("[IX_reservation_departure]", after3.ListPlan, StringComparison.Ordinal);
        Assert.True(after3.WindowReads <= after1.WindowReads + 2 * after3.Pages, $"The count and page statements read {after1.WindowReads} pages of inv.reservation on one year and {after3.WindowReads} on three."); // at most one more index level
        Assert.True(after3.WindowReads < WindowReadsCeiling, $"The count and page statements read {after3.WindowReads} pages of inv.reservation (ceiling {WindowReadsCeiling}); before {before3.WindowReads}.");
        Assert.True(before3.WindowReads > WindowReadsCeiling, $"The old count and page statements read only {before3.WindowReads} pages; the fixture no longer shows the history cost.");
        Assert.True(before3.WindowReads > before1.WindowReads * 2, $"The old count and page statements grew only from {before1.WindowReads} to {before3.WindowReads} pages.");
        Assert.True(after1.ListReads * 3 < before1.ListReads, $"The bookings load read {after1.ListReads} pages on one year against {before1.ListReads} before.");
        Assert.True(after3.ListReads < before3.ListReads, $"The bookings load read {after3.ListReads} pages on three years against {before3.ListReads} before.");

        // Cashier folio list: flat; the old one grows with every booking and stay.
        Assert.Contains("[IX_reservation_inhouse]", after3.FolioPlan, StringComparison.Ordinal);
        Assert.Contains("[IX_stay_checked_out]", after3.FolioPlan, StringComparison.Ordinal);
        Assert.True(after3.FolioReads < FolioReadsCeiling, $"The cashier folio list read {after3.FolioReads} pages (ceiling {FolioReadsCeiling}); before it read {before3.FolioReads}.");
        Assert.True(before3.FolioReads > FolioReadsCeiling, $"The old cashier folio list read only {before3.FolioReads} pages; the fixture no longer shows the history cost.");
        Assert.True(after3.FolioReads - after1.FolioReads < 40, $"The cashier folio list grew from {after1.FolioReads} to {after3.FolioReads} pages with two more years of history.");
        Assert.True(before3.FolioReads - before1.FolioReads > 200, $"The old cashier folio list grew only from {before1.FolioReads} to {before3.FolioReads} pages.");
    }
}
