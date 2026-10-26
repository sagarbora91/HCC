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
        nightly_rate_paise, group_block_id, notes, row_ver, company_name, company_gstin
        """;

    public async Task<ReservationRecord?> GetForUpdateAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default)
    {
        await using var q = SqlScope.Of(tx, connections).Command($"SELECT {Columns} FROM inv.reservation WITH (UPDLOCK, ROWLOCK) WHERE reservation_id = @r AND property_id = @p;");
        q.Parameters.Big("@r", reservationId);
        q.Parameters.Int("@p", propertyId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false) || rd.IsDBNull(4)) return null;
        return new ReservationRecord(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4), rd.GetInt32(5), rd.GetDate(6),
            rd.GetDate(7), rd.GetByte(8), rd.GetByte(9), rd.GetUtcOrNull(10), rd.GetInt64OrNull(11), rd.GetInt64OrNull(12), rd.GetStringOrNull(13), (byte[])rd[14], CompanyOf(rd.GetStringOrNull(15), rd.GetStringOrNull(16)));
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
              adults, children, phone, source, nightly_rate_paise, quote_id, group_block_id, restriction_override, notes, created_by, status_utc, company_name, company_gstin)
            OUTPUT INSERTED.reservation_id, INSERTED.public_no
            VALUES (@p, @no, @g, @a, @d, @st, @hold, @now, @room, @type, @ad, @ch, @phone, @src, @rate, @quote, @block, @ovr, @notes, @u, @now, @cname, @cgstin);
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
            ins.Parameters.Text("@cname", r.Company?.Name, 200);
            ins.Parameters.Add("@cgstin", SqlDbType.VarChar, 15).Value = (object?)r.Company?.Gstin ?? DBNull.Value;
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
        if (current.Allocation != change.Allocation) // a company-only amend keeps the room nights as they are
        {
            await InventorySql.Guard(async () =>
            {
                await SqlInventoryAllocator.ChangeAsync(s, propertyId, current.ReservationId, current.Allocation, change.Allocation, true, ct).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);
        }

        byte[] ver;
        await using (var up = s.Command("""
            UPDATE inv.reservation SET room_id = @room, room_type_id = @type, arrival = @a, departure = @d, nightly_rate_paise = @rate,
              quote_id = COALESCE(@quote, quote_id), notes = @notes, company_name = @cname, company_gstin = @cgstin
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
            up.Parameters.Text("@cname", change.Company?.Name, 200);
            up.Parameters.Add("@cgstin", SqlDbType.VarChar, 15).Value = (object?)change.Company?.Gstin ?? DBNull.Value;
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
              r.restriction_override, r.cancel_reason, r.notes, r.company_name, r.company_gstin
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
            Cancel = rd.GetStringOrNull(19), Notes = rd.GetStringOrNull(20), Company = CompanyOf(rd.GetStringOrNull(21), rd.GetStringOrNull(22)),
        };
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var nights = new List<ReservationNight>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            nights.Add(new ReservationNight(Domain.Dates.StayDates.ToWire(rd.GetDate(0)), rd.GetInt64(1), rd.GetInt32(2), rd.GetInt64(3)));
        var net = nights.Sum(n => n.RatePaise);
        var tax = nights.Sum(n => n.TaxPaise);
        return new ReservationDetails(head.Id, head.No, InventoryErrors.VersionToWire(head.Ver), status, head.Guest, head.Phone, head.RoomId, head.RoomNo,
            head.TypeId, head.Type, Domain.Dates.StayDates.ToWire(head.A), Domain.Dates.StayDates.ToWire(head.D), head.Ad, head.Ch, head.Src, head.Rate,
            holdUntil is null ? null : InventoryService.ToWireInstant(holdUntil.Value), head.Block, head.Ovr, head.Cancel, head.Notes, nights, net, tax, net + tax, head.Company);
    }

    /// <summary>Effective status as users see it: a lapsed hold reads Expired (same rule as <c>ReservationStatusMap.Effective</c>).</summary>
    private const string EffectiveStatus = "CASE WHEN r.status = 'Held' AND r.hold_until_utc IS NOT NULL AND r.hold_until_utc <= @now THEN 'Expired' ELSE r.status END";

    public async Task<ReservationList> ListAsync(int propertyId, ReservationListQuery query, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
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
            where.Add($"{EffectiveStatus} IN ({string.Join(", ", query.Statuses.Select((_, i) => "@s" + i))})");
        var text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim();
        if (text is not null)
            where.Add("(r.guest_name LIKE @t ESCAPE '!' OR r.public_no LIKE @t ESCAPE '!' OR r.phone LIKE @t ESCAPE '!' OR rm.room_no = @tx)");
        var from = $"FROM inv.reservation r JOIN inv.room rm ON rm.room_id = r.room_id JOIN inv.room_type t ON t.room_type_id = r.room_type_id WHERE {string.Join(" AND ", where)}";
        var sql = $"""
            DECLARE @bd DATE = (SELECT business_date FROM inv.property WHERE property_id = @p);
            SELECT @bd, COUNT(*) {from};
            CREATE TABLE #pg(ord INT IDENTITY PRIMARY KEY, reservation_id BIGINT NOT NULL);
            INSERT #pg(reservation_id) SELECT r.reservation_id {from} ORDER BY r.arrival DESC, r.reservation_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            SELECT r.reservation_id, r.public_no, r.row_ver, r.status, r.guest_name, r.phone, r.room_id, rm.room_no, r.room_type_id, t.code,
              r.arrival, r.departure, r.adults, r.children, r.source, r.nightly_rate_paise, r.hold_until_utc, r.group_block_id,
              r.restriction_override, r.cancel_reason, r.notes,
              COALESCE(s.guest_id, g.guest_id), CASE WHEN g.reservation_id IS NULL THEN 0 ELSE 1 END, s.stay_id, s.row_ver, s.status, s.checked_in_date, s.checked_out_date,
              r.company_name, r.company_gstin
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
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var q = connections.Command(sql, conn);
        q.Parameters.Int("@p", propertyId);
        q.Parameters.Utc("@now", nowUtc);
        q.Parameters.Int("@skip", (query.Page - 1) * query.PageSize);
        q.Parameters.Int("@take", query.PageSize);
        if (query.From is { } f) q.Parameters.Date("@from", f);
        if (query.To is { } to) q.Parameters.Date("@to", to);
        for (var i = 0; i < query.Statuses.Count; i++) q.Parameters.Text("@s" + i, query.Statuses[i], 20);
        if (text is not null)
        {
            var like = text.Replace("!", "!!", StringComparison.Ordinal).Replace("%", "!%", StringComparison.Ordinal).Replace("_", "!_", StringComparison.Ordinal).Replace("[", "![", StringComparison.Ordinal);
            q.Parameters.Text("@t", "%" + like + "%", 220);
            q.Parameters.Text("@tx", text, 10);
        }
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        var businessDate = Domain.Dates.StayDates.ToWire(rd.GetDate(0));
        var total = rd.GetInt32(1);
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var rows = new List<(ReservationListItem Item, long Id)>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var holdUntil = rd.GetUtcOrNull(16);
            var status = ReservationStatusMap.Effective(rd.GetString(3), holdUntil, nowUtc);
            var checkedIn = rd.GetDateOrNull(26);
            var checkedOut = rd.GetDateOrNull(27);
            rows.Add((new ReservationListItem(rd.GetInt64(0), rd.GetString(1), InventoryErrors.VersionToWire((byte[])rd[2]), status, rd.GetString(4), rd.GetStringOrNull(5),
                rd.GetInt32(6), rd.GetString(7), rd.GetInt32(8), rd.GetString(9), Domain.Dates.StayDates.ToWire(rd.GetDate(10)), Domain.Dates.StayDates.ToWire(rd.GetDate(11)),
                (int)rd.GetByte(12), (int)rd.GetByte(13), rd.GetString(14), rd.GetInt64OrNull(15),
                holdUntil is null ? null : InventoryService.ToWireInstant(holdUntil.Value), rd.GetInt64OrNull(17), rd.GetStringOrNull(18), rd.GetStringOrNull(19), rd.GetStringOrNull(20),
                rd.GetInt64OrNull(21), rd.GetInt32(22) == 1, rd.GetInt64OrNull(23), rd.IsDBNull(24) ? null : InventoryErrors.VersionToWire((byte[])rd[24]), rd.GetStringOrNull(25),
                checkedIn is null ? null : Domain.Dates.StayDates.ToWire(checkedIn.Value), checkedOut is null ? null : Domain.Dates.StayDates.ToWire(checkedOut.Value), [],
                CompanyOf(rd.GetStringOrNull(28), rd.GetStringOrNull(29))), rd.GetInt64(0)));
        }
        await rd.NextResultAsync(ct).ConfigureAwait(false);
        var segments = new Dictionary<long, List<ReservationStaySegment>>();
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = rd.GetInt64(0);
            if (!segments.TryGetValue(id, out var list)) segments[id] = list = [];
            var segTo = rd.GetDateOrNull(3);
            list.Add(new ReservationStaySegment(rd.GetString(1), Domain.Dates.StayDates.ToWire(rd.GetDate(2)), segTo is null ? null : Domain.Dates.StayDates.ToWire(segTo.Value)));
        }
        var items = rows.Select(x => segments.TryGetValue(x.Id, out var seg) ? x.Item with { Segments = seg } : x.Item).ToList();
        return new ReservationList(businessDate, total, query.Page, query.PageSize, items);
    }

    private static ReservationCompany? CompanyOf(string? name, string? gstin) => string.IsNullOrEmpty(name) ? null : new ReservationCompany(name, gstin);

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
