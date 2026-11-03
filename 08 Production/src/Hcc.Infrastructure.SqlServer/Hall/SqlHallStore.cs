using System.Data;
using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Hall;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Hall;

/// <summary>SQL adapter of <see cref="IHallStore"/> (F6 H3: enquiries, halls master, print log 4348, and the reads of migrations 4300 / 4320).</summary>
public sealed class SqlHallStore(SqlConnectionFactory connections) : IHallStore
{
    private static SqlCommand Cmd(ICommandTransaction tx, string sql) =>
        (tx as SqlCommandTransaction ?? throw new InvalidOperationException("The hall store needs the SQL command transaction.")).Command(sql);

    private static void P(SqlCommand c, string n, SqlDbType t, object? v) => c.Parameters.Add(n, t).Value = v ?? DBNull.Value;

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static long? L(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    private static int? I(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static DateOnly Day(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));

    private static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string[] Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<DateOnly> Dates(string s) => Split(s).Select(x => DateOnly.ParseExact(x, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();

    private async Task<T> ReadAsync<T>(Func<SqlConnection, Task<T>> f, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        return await f(conn).ConfigureAwait(false);
    }

    private static SqlCommand Q(SqlConnection c, string sql, int propertyId)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return cmd;
    }

    // ── writes ──
    public async Task<long> InsertEnquiryAsync(ICommandTransaction tx, NewHallEnquiry e, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var cmd = Cmd(tx, """
            INSERT hall.enquiry(property_id, guest_id, company_id, name, phone, event_type, dates, slot, pax, venue_id, source, follow_up, note, status, created_by, created_utc)
            OUTPUT INSERTED.enquiry_id VALUES (@p, @g, @co, @n, @ph, @t, @d, @s, @x, @v, @src, @f, @note, 'OPEN', @by, @u);
            """);
        P(cmd, "@p", SqlDbType.Int, e.PropertyId);
        P(cmd, "@g", SqlDbType.BigInt, e.GuestId);
        P(cmd, "@co", SqlDbType.BigInt, e.CompanyId);
        P(cmd, "@n", SqlDbType.NVarChar, e.Name);
        P(cmd, "@ph", SqlDbType.VarChar, e.Phone);
        P(cmd, "@t", SqlDbType.VarChar, e.EventType);
        P(cmd, "@d", SqlDbType.VarChar, string.Join(',', e.Dates.Select(Wire)));
        P(cmd, "@s", SqlDbType.VarChar, e.Slot);
        P(cmd, "@x", SqlDbType.Int, e.Pax);
        P(cmd, "@v", SqlDbType.Int, e.VenueId);
        P(cmd, "@src", SqlDbType.VarChar, e.Source);
        P(cmd, "@f", SqlDbType.Date, e.FollowUp.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@note", SqlDbType.NVarChar, e.Note);
        P(cmd, "@by", SqlDbType.Int, e.By);
        P(cmd, "@u", SqlDbType.DateTime2, e.Utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<HallEnquiryRow?> LockEnquiryAsync(ICommandTransaction tx, int propertyId, long enquiryId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT enquiry_id, name, event_type, dates, slot, pax, venue_id, follow_up, note, status, lost_reason_id, lost_note, soft_venue_id, soft_until_utc, version
            FROM hall.enquiry WITH (UPDLOCK, ROWLOCK) WHERE enquiry_id = @e AND property_id = @p;
            """);
        P(cmd, "@e", SqlDbType.BigInt, enquiryId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new HallEnquiryRow(r.GetInt64(0), r.GetString(1), r.GetString(2), Dates(r.GetString(3)), r.GetString(4), r.GetInt32(5), I(r, 6), Day(r, 7), S(r, 8), r.GetString(9), S(r, 10), S(r, 11),
            I(r, 12), r.IsDBNull(13) ? null : DateOnly.FromDateTime(r.GetDateTime(13)), r.GetInt32(14));
    }

    public async Task UpdateEnquiryAsync(ICommandTransaction tx, HallEnquiryRow e, int? by, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(e);
        await using var cmd = Cmd(tx, """
            UPDATE hall.enquiry SET dates = @d, slot = @s, pax = @x, venue_id = @v, follow_up = @f, note = @note, status = @st, lost_reason_id = @lr, lost_note = @ln,
                   soft_venue_id = @sv, soft_until_utc = @su, version = version + 1
            WHERE enquiry_id = @e AND version = @ver;
            """);
        P(cmd, "@e", SqlDbType.BigInt, e.EnquiryId);
        P(cmd, "@ver", SqlDbType.Int, e.Version);
        P(cmd, "@d", SqlDbType.VarChar, string.Join(',', e.Dates.Select(Wire)));
        P(cmd, "@s", SqlDbType.VarChar, e.Slot);
        P(cmd, "@x", SqlDbType.Int, e.Pax);
        P(cmd, "@v", SqlDbType.Int, e.VenueId);
        P(cmd, "@f", SqlDbType.Date, e.FollowUp.ToDateTime(TimeOnly.MinValue));
        P(cmd, "@note", SqlDbType.NVarChar, e.Note);
        P(cmd, "@st", SqlDbType.VarChar, e.Status);
        P(cmd, "@lr", SqlDbType.VarChar, e.LostReasonId);
        P(cmd, "@ln", SqlDbType.NVarChar, e.LostNote);
        P(cmd, "@sv", SqlDbType.Int, e.SoftVenueId);
        P(cmd, "@su", SqlDbType.DateTime2, e.SoftUntil?.ToDateTime(TimeOnly.MinValue));
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1) throw new HccException(HccErrorCode.STALE_VERSION, "This enquiry was changed by someone else. Reload and try again.");
        _ = by;
    }

    public async Task<IReadOnlyList<(long EnquiryId, int VenueId, string Slot, IReadOnlyList<DateOnly> Dates)>> SoftHoldsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<DateOnly> dates,
        DateOnly businessDate, long exceptEnquiry, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, """
            SELECT enquiry_id, soft_venue_id, slot, dates FROM hall.enquiry
            WHERE property_id = @p AND enquiry_id <> @e AND soft_venue_id IS NOT NULL AND CAST(soft_until_utc AS DATE) >= @bd AND status IN ('OPEN','QUOTED');
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@e", SqlDbType.BigInt, exceptEnquiry);
        P(cmd, "@bd", SqlDbType.Date, businessDate.ToDateTime(TimeOnly.MinValue));
        var list = new List<(long, int, string, IReadOnlyList<DateOnly>)>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add((r.GetInt64(0), r.GetInt32(1), r.GetString(2), Dates(r.GetString(3))));
        return list;
    }

    public async Task<bool> VenueActiveAsync(ICommandTransaction tx, int propertyId, int venueId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM hall.venue WHERE property_id = @p AND venue_id = @v AND active = 1;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@v", SqlDbType.Int, venueId);
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0;
    }

    public async Task<string?> VenueCodeAsync(ICommandTransaction tx, int propertyId, int venueId, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT code FROM hall.venue WHERE property_id = @p AND venue_id = @v AND combined_of IS NULL;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@v", SqlDbType.Int, venueId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    public async Task<int> ConfirmedAheadAsync(ICommandTransaction tx, int propertyId, int venueId, DateOnly from, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "SELECT COUNT(*) FROM hall.event WHERE property_id = @p AND venue_id = @v AND status = 'CONFIRMED' AND event_date >= @d;");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@v", SqlDbType.Int, venueId);
        P(cmd, "@d", SqlDbType.Date, from.ToDateTime(TimeOnly.MinValue));
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    public async Task<(int VenueId, int Version)> UpsertVenueAsync(ICommandTransaction tx, int propertyId, UpsertHallVenueRequest v, IReadOnlyList<string> combinedCodes, int by, DateTime utc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(v);
        int id, version;
        var cap = v.Capacity ?? new Dictionary<string, int>();
        int? C(string k) => cap.TryGetValue(k, out var n) ? n : null;
        try
        {
            if (v.VenueId is { } vid)
            {
                await using var cmd = Cmd(tx, """
                    UPDATE hall.venue SET code = @code, name = @n, kind = @k, cap_theatre = @c1, cap_round = @c2, cap_ushape = @c3, cap_floating = @c4, cap_classroom = @c5, min_plates = @mp,
                           gst_class = @g, combined_of = @co, active = @a, version = version + 1, updated_by = @by, updated_utc = @u
                    OUTPUT INSERTED.venue_id, INSERTED.version
                    WHERE venue_id = @v AND property_id = @p AND (@ver IS NULL OR version = @ver);
                    """);
                Fill(cmd);
                P(cmd, "@v", SqlDbType.Int, vid);
                P(cmd, "@ver", SqlDbType.Int, v.ExpectedVersion);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await r.ReadAsync(ct).ConfigureAwait(false))
                    throw new HccException(v.ExpectedVersion is null ? HccErrorCode.NOT_FOUND : HccErrorCode.STALE_VERSION, v.ExpectedVersion is null ? "Hall not found." : "This hall was changed by someone else. Reload and try again.");
                (id, version) = (r.GetInt32(0), r.GetInt32(1));
            }
            else
            {
                await using var cmd = Cmd(tx, """
                    INSERT hall.venue(property_id, code, name, kind, cap_theatre, cap_round, cap_ushape, cap_floating, cap_classroom, min_plates, gst_class, combined_of, active, updated_by, updated_utc)
                    OUTPUT INSERTED.venue_id, INSERTED.version VALUES (@p, @code, @n, @k, @c1, @c2, @c3, @c4, @c5, @mp, @g, @co, @a, @by, @u);
                    """);
                Fill(cmd);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await r.ReadAsync(ct).ConfigureAwait(false);
                (id, version) = (r.GetInt32(0), r.GetInt32(1));
            }
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw HccException.Validation("Another hall already has this name or code.");
        }

        await using (var del = Cmd(tx, "DELETE FROM hall.venue_slot WHERE venue_id = @v;"))
        {
            P(del, "@v", SqlDbType.Int, id);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var s in v.Slots)
        {
            await using var ins = Cmd(tx, "INSERT hall.venue_slot(venue_id, slot, time_from, time_to, rate_paise, min_plates) VALUES (@v, @s, @f, @t, @r, @m);");
            P(ins, "@v", SqlDbType.Int, id);
            P(ins, "@s", SqlDbType.VarChar, s.Slot);
            P(ins, "@f", SqlDbType.Char, s.From);
            P(ins, "@t", SqlDbType.Char, s.To);
            P(ins, "@r", SqlDbType.BigInt, s.RatePaise);
            P(ins, "@m", SqlDbType.Int, s.MinPlates);
            await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return (id, version);

        void Fill(SqlCommand cmd)
        {
            P(cmd, "@p", SqlDbType.Int, propertyId);
            P(cmd, "@code", SqlDbType.VarChar, v.Code);
            P(cmd, "@n", SqlDbType.NVarChar, v.Name);
            P(cmd, "@k", SqlDbType.VarChar, v.Kind);
            P(cmd, "@c1", SqlDbType.Int, C("THEATRE"));
            P(cmd, "@c2", SqlDbType.Int, C("ROUND"));
            P(cmd, "@c3", SqlDbType.Int, C("USHAPE"));
            P(cmd, "@c4", SqlDbType.Int, C("FLOATING"));
            P(cmd, "@c5", SqlDbType.Int, C("CLASSROOM"));
            P(cmd, "@mp", SqlDbType.Int, v.MinPlates);
            P(cmd, "@g", SqlDbType.VarChar, string.IsNullOrWhiteSpace(v.GstClass) ? "HALL_RENT" : v.GstClass);
            P(cmd, "@co", SqlDbType.VarChar, combinedCodes.Count == 0 ? null : string.Join(',', combinedCodes));
            P(cmd, "@a", SqlDbType.Bit, v.Active);
            P(cmd, "@by", SqlDbType.Int, by);
            P(cmd, "@u", SqlDbType.DateTime2, utc);
        }
    }

    public async Task InsertPrintLogAsync(ICommandTransaction tx, int propertyId, string docKind, long refId, int? version, bool withPrices, int by, DateTime utc, CancellationToken ct)
    {
        await using var cmd = Cmd(tx, "INSERT hall.print_log(property_id, doc_kind, ref_id, version, with_prices, printed_by, printed_utc) VALUES (@p, @k, @r, @v, @w, @by, @u);");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@k", SqlDbType.VarChar, docKind);
        P(cmd, "@r", SqlDbType.BigInt, refId);
        P(cmd, "@v", SqlDbType.Int, version);
        P(cmd, "@w", SqlDbType.Bit, withPrices);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@u", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── reads ──
    public Task<IReadOnlyList<HallVenueView>> VenuesAsync(int propertyId, bool seesMoney, CancellationToken ct) => ReadAsync<IReadOnlyList<HallVenueView>>(async conn =>
    {
        var raw = new List<(int Id, string Code, string Name, string Kind, bool Active, int Version, string? Combined, int? Mp, string Gst, Dictionary<string, int> Cap)>();
        await using (var cmd = Q(conn, "SELECT venue_id, code, name, kind, active, version, combined_of, min_plates, gst_class, cap_theatre, cap_round, cap_ushape, cap_floating, cap_classroom FROM hall.venue WHERE property_id = @p ORDER BY venue_id;", propertyId))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            string[] setups = ["THEATRE", "ROUND", "USHAPE", "FLOATING", "CLASSROOM"];
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var cap = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < setups.Length; i++) if (!r.IsDBNull(9 + i)) cap[setups[i]] = r.GetInt32(9 + i);
                raw.Add((r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetInt32(5), S(r, 6), I(r, 7), r.GetString(8), cap));
            }
        }

        var slots = new Dictionary<int, List<HallSlotView>>();
        await using (var cmd = Q(conn, "SELECT s.venue_id, s.slot, s.time_from, s.time_to, s.rate_paise, s.min_plates FROM hall.venue_slot s JOIN hall.venue v ON v.venue_id = s.venue_id WHERE v.property_id = @p ORDER BY s.venue_id, s.slot;", propertyId))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!slots.TryGetValue(r.GetInt32(0), out var l)) slots[r.GetInt32(0)] = l = [];
                l.Add(new HallSlotView(r.GetString(1), r.GetString(2), r.GetString(3), seesMoney ? r.GetInt64(4) : null, I(r, 5)));
            }

        var byCode = raw.ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal);
        return raw.Select(x => new HallVenueView(x.Id, x.Code, x.Name, x.Kind, x.Active, x.Version, x.Cap,
            x.Combined is null ? [] : Split(x.Combined).Where(byCode.ContainsKey).Select(c => byCode[c]).ToList(), x.Mp, x.Gst, slots.GetValueOrDefault(x.Id) ?? [])).ToList();
    }, ct);

    public Task<IReadOnlyList<HallEnquiryView>> EnquiriesAsync(int propertyId, string? status, CancellationToken ct) => ReadAsync<IReadOnlyList<HallEnquiryView>>(async conn =>
    {
        await using var cmd = Q(conn, """
            SELECT enquiry_id, name, phone, event_type, dates, slot, pax, venue_id, source, follow_up, note, status, lost_reason_id, lost_note, soft_venue_id, soft_until_utc, version, guest_id, company_id, created_utc
            FROM hall.enquiry WHERE property_id = @p AND (@s IS NULL OR status = @s) ORDER BY enquiry_id;
            """, propertyId);
        P(cmd, "@s", SqlDbType.VarChar, status);
        var list = new List<HallEnquiryView>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new HallEnquiryView(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Split(r.GetString(4)), r.GetString(5), r.GetInt32(6), I(r, 7), r.GetString(8), Wire(Day(r, 9)), S(r, 10),
                r.GetString(11), S(r, 12), S(r, 13), I(r, 14), r.IsDBNull(15) ? null : Wire(DateOnly.FromDateTime(r.GetDateTime(15))), r.GetInt32(16), L(r, 17), L(r, 18), Utc(r.GetDateTime(19))));
        return list;
    }, ct);

    public Task<IReadOnlyList<HallCalendarRowView>> CalendarAsync(int propertyId, DateOnly from, DateOnly to, int? venueId, CancellationToken ct) => ReadAsync<IReadOnlyList<HallCalendarRowView>>(async conn =>
    {
        DateOnly bd;
        await using (var b = Q(conn, "SELECT business_date FROM inv.property WHERE property_id = @p;", propertyId)) bd = DateOnly.FromDateTime((DateTime)(await b.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? DateTime.UtcNow.Date));
        var rows = new List<HallCalendarRowView>();
        bool In(DateOnly d) => d >= from && d <= to;
        await using (var cmd = Q(conn, "SELECT event_id, venue_id, dates, slot, status, customer_name, event_type FROM hall.event WHERE property_id = @p AND status <> 'CANCELLED';", propertyId))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                foreach (var d in Dates(r.GetString(2)).Where(In))
                    if (venueId is null || venueId == r.GetInt32(1))
                        rows.Add(new HallCalendarRowView(Wire(d), r.GetInt32(1), r.GetString(3), "EVENT", "HEV-" + r.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                            $"{r.GetString(5)} · {r.GetString(6)}", r.GetString(4) == "BILLED" ? "Event day" : "Confirmed"));
        await using (var cmd = Q(conn, "SELECT enquiry_id, soft_venue_id, dates, slot, name, event_type FROM hall.enquiry WHERE property_id = @p AND soft_venue_id IS NOT NULL AND CAST(soft_until_utc AS DATE) >= @bd AND status IN ('OPEN','QUOTED');", propertyId))
        {
            P(cmd, "@bd", SqlDbType.Date, bd.ToDateTime(TimeOnly.MinValue));
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                foreach (var d in Dates(r.GetString(2)).Where(In))
                    if (venueId is null || venueId == r.GetInt32(1))
                        rows.Add(new HallCalendarRowView(Wire(d), r.GetInt32(1), r.GetString(3), "SOFT_HOLD", "ENQ-" + r.GetInt64(0).ToString(CultureInfo.InvariantCulture), $"{r.GetString(4)} · {r.GetString(5)}", "Enquiry hold"));
        }

        return rows.OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.VenueId).ThenBy(x => Array.IndexOf(["MORNING", "AFTERNOON", "EVENING", "FULL_DAY"], x.Slot)).ToList();
    }, ct);

    private static async Task<Dictionary<long, List<HallLineView>>> LinesAsync(SqlConnection conn, int propertyId, string sql, bool seesMoney, bool eventLines, CancellationToken ct)
    {
        var map = new Dictionary<long, List<HallLineView>>();
        await using var cmd = Q(conn, sql, propertyId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = r.GetInt64(0);
            if (!map.TryGetValue(key, out var l)) map[key] = l = [];
            l.Add(new HallLineView(r.GetInt32(1), r.GetString(2), S(r, 3), r.GetString(4), r.GetInt32(5), r.GetString(6), seesMoney ? r.GetInt64(7) : null, seesMoney ? r.GetInt64(8) : null,
                seesMoney ? r.GetInt64(9) : null, r.GetString(10), eventLines ? I(r, 11) : null, eventLines && r.GetBoolean(12)));
        }

        return map;
    }

    private static async Task<Dictionary<long, List<HallAdvanceView>>> AdvanceMapAsync(SqlConnection conn, int propertyId, string sql, bool paid, CancellationToken ct)
    {
        var map = new Dictionary<long, List<HallAdvanceView>>();
        await using var cmd = Q(conn, sql, propertyId);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var key = r.GetInt64(0);
            if (!map.TryGetValue(key, out var l)) map[key] = l = [];
            l.Add(new HallAdvanceView(r.GetInt32(1), r.GetString(2), I(r, 3), L(r, 4), r.GetString(5), paid ? r.GetInt64(6) : null));
        }

        return map;
    }

    public Task<IReadOnlyList<HallQuotationView>> QuotationsAsync(int propertyId, string? status, long? quotationId, CancellationToken ct) => ReadAsync<IReadOnlyList<HallQuotationView>>(async conn =>
    {
        var lines = await LinesAsync(conn, propertyId, """
            SELECT l.version_id, l.seq, l.kind, l.ref_id, l.descr, l.qty, l.unit, l.rate_paise, l.list_rate_paise, l.discount_paise, l.gst_class FROM hall.quotation_line l
            JOIN hall.quotation_version v ON v.version_id = l.version_id JOIN hall.quotation q ON q.quotation_id = v.quotation_id WHERE q.property_id = @p ORDER BY l.version_id, l.seq;
            """, true, false, ct).ConfigureAwait(false);
        var adv = await AdvanceMapAsync(conn, propertyId, """
            SELECT a.version_id, a.seq, a.label, a.pct_bps, a.amount_paise, a.due FROM hall.quotation_advance a
            JOIN hall.quotation_version v ON v.version_id = a.version_id JOIN hall.quotation q ON q.quotation_id = v.quotation_id WHERE q.property_id = @p ORDER BY a.version_id, a.seq;
            """, false, ct).ConfigureAwait(false);
        var heads = new List<(long Id, string Number, string Fy, long Enq, int Cur, string Name, string Phone, long? Co)>();
        var versions = new Dictionary<long, List<HallQuotationVersionView>>();
        await using (var cmd = Q(conn, "SELECT quotation_id, number, fy, enquiry_id, current_version, customer_name, customer_phone, company_id FROM hall.quotation WHERE property_id = @p AND (@q IS NULL OR quotation_id = @q) ORDER BY quotation_id;", propertyId))
        {
            P(cmd, "@q", SqlDbType.BigInt, quotationId);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) heads.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt32(4), r.GetString(5), r.GetString(6), L(r, 7)));
        }

        await using (var cmd = Q(conn, """
            SELECT v.version_id, v.quotation_id, v.v, v.status, v.event_type, v.dates, v.slot, v.venue_id, v.setup, v.pax, v.guaranteed_pax, v.valid_until, v.created_utc, v.sent_utc, v.override_reason
            FROM hall.quotation_version v JOIN hall.quotation q ON q.quotation_id = v.quotation_id WHERE q.property_id = @p ORDER BY v.quotation_id, v.v;
            """, propertyId))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var vid = r.GetInt64(0);
                if (!versions.TryGetValue(r.GetInt64(1), out var l)) versions[r.GetInt64(1)] = l = [];
                l.Add(new HallQuotationVersionView(r.GetInt32(2), r.GetString(3), r.GetString(4), Split(r.GetString(5)), r.GetString(6), r.GetInt32(7), r.GetString(8), r.GetInt32(9), r.GetInt32(10), Wire(Day(r, 11)),
                    Utc(r.GetDateTime(12)), r.IsDBNull(13) ? null : Utc(r.GetDateTime(13)), S(r, 14), lines.GetValueOrDefault(vid) ?? [], adv.GetValueOrDefault(vid) ?? []));
            }

        return heads.Select(h => new HallQuotationView(h.Id, h.Number, h.Fy, h.Enq, h.Cur, h.Name, h.Phone, h.Co, versions.GetValueOrDefault(h.Id) ?? []))
            .Where(q => status is null || q.Versions.FirstOrDefault(v => v.V == q.CurrentVersion)?.Status == status).ToList();
    }, ct);

    public Task<IReadOnlyList<HallEventView>> EventsAsync(int propertyId, string? status, long? eventId, bool seesMoney, CancellationToken ct) => ReadAsync<IReadOnlyList<HallEventView>>(async conn =>
    {
        var lines = await LinesAsync(conn, propertyId, """
            SELECT l.event_id, l.seq, l.kind, l.ref_id, l.descr, l.qty, l.unit, l.rate_paise, l.list_rate_paise, l.discount_paise, l.gst_class, l.tax_bps, l.is_extra FROM hall.event_line l
            JOIN hall.event e ON e.event_id = l.event_id WHERE e.property_id = @p ORDER BY l.event_id, l.seq;
            """, seesMoney, true, ct).ConfigureAwait(false);
        var adv = await AdvanceMapAsync(conn, propertyId, """
            SELECT a.event_id, a.seq, a.label, a.pct_bps, a.amount_paise, a.due, a.paid_paise FROM hall.event_advance a JOIN hall.event e ON e.event_id = a.event_id WHERE e.property_id = @p ORDER BY a.event_id, a.seq;
            """, true, ct).ConfigureAwait(false);
        var receipts = new Dictionary<long, List<HallReceiptView>>();
        if (seesMoney)
            await using (var cmd = Q(conn, """
                SELECT x.event_id, x.receipt_id, x.business_date, x.kind, x.tender, x.company_id, x.udhaar, x.reference, x.amount_paise, x.at_utc FROM hall.event_receipt x
                JOIN hall.event e ON e.event_id = x.event_id WHERE e.property_id = @p ORDER BY x.event_id, x.receipt_id;
                """, propertyId))
            await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (!receipts.TryGetValue(r.GetInt64(0), out var l)) receipts[r.GetInt64(0)] = l = [];
                    l.Add(new HallReceiptView(r.GetInt64(1), Wire(Day(r, 2)), r.GetString(3), S(r, 4), L(r, 5), r.GetBoolean(6), S(r, 7), r.GetInt64(8), Utc(r.GetDateTime(9))));
                }

        var list = new List<HallEventView>();
        await using var q = Q(conn, """
            SELECT event_id, quotation_id, version, venue_id, event_date, dates, slot, event_type, status, guaranteed_pax, actual_pax, customer_name, customer_phone, company_id, block_id,
                   invoice_no, invoice_date, total_paise, taxable_paise, cgst_paise, sgst_paise, advance_applied_paise, balance_paise, settle_mode, cancel_date, cancel_kept_paise, cancel_refund_paise,
                   refund_state, cancel_reason, billed_pax, created_utc
            FROM hall.event WHERE property_id = @p AND (@s IS NULL OR status = @s) AND (@e IS NULL OR event_id = @e) ORDER BY event_id;
            """, propertyId);
        P(q, "@s", SqlDbType.VarChar, status);
        P(q, "@e", SqlDbType.BigInt, eventId);
        await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            long? M(int i) => seesMoney ? L(rd, i) : null;
            var id = rd.GetInt64(0);
            list.Add(new HallEventView(id, rd.GetInt64(1), rd.GetInt32(2), rd.GetInt32(3), Wire(Day(rd, 4)), Split(rd.GetString(5)), rd.GetString(6), rd.GetString(7), rd.GetString(8), rd.GetInt32(9), I(rd, 10),
                rd.GetString(11), rd.GetString(12), L(rd, 13), L(rd, 14), seesMoney ? S(rd, 15) : null, rd.IsDBNull(16) ? null : Wire(Day(rd, 16)), M(17), M(18), M(19), M(20), M(21), M(22), S(rd, 23),
                rd.IsDBNull(24) ? null : Wire(Day(rd, 24)), M(25), M(26), S(rd, 27), seesMoney ? S(rd, 28) : null, I(rd, 29), lines.GetValueOrDefault(id) ?? [], adv.GetValueOrDefault(id) ?? [],
                receipts.GetValueOrDefault(id) ?? [], Utc(rd.GetDateTime(30))));
        }

        return list;
    }, ct);
}
