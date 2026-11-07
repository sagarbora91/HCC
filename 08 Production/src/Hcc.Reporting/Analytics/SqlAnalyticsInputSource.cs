// F12 MEAS · f12-int-an · the production analytics input: live HCC stays / bills / receipts + the imported legacy history (lgc.*) + the rooms master in ONE AnInput
// for every study selector (study-a positional, study-b initialisers). Mirrors 03 Prototype/src/domain/analytics/legacy-input.ts (buildAnInput) for the live rows
// and 'Existing Software/Reports/beyond_lib.py' (the workbook rules) for the lgc rows:
//   · halls never enter (live halls are hall-module bookings, never inv.reservation; legacy rooms CON / CONAC or room type CONF are dropped from stays, a bill
//     on a hall room is dropped);
//   · duplicate stays never enter (lgc.stay.status = 'Duplicate');
//   · one AnStay per room (a legacy booking with several booked rooms gives one AnStay per distinct room; live = one reservation, its current room);
//   · live bill date = departure when Departed, else arrival; legacy bill date = lgc.bill.bill_date (the legacy bill was printed at departure);
//   · room 211 = Suite; every lgc row carries Legacy = true; LegacyUntil 2026-10-30 (a legacy stay still open at the backup / cutover is cut at that date so it
//     never overlaps HCC live nights);
//   · money = integer paise straight from SQL (no double): live amounts are the folio's lines (a reversal and its original net to zero), charged nights = live
//     room lines (kind = 'room', neither reversing nor reversed); legacy room revenue = room_charge_paise, other = extra + laundry + other, GST = the four GST
//     columns, total = net_paise, paid = net − balance (DEC-265: the open legacy balance is a bad debt, never an opening receivable).
//   · DEC-266: the explicit complimentary stay type is not built; no table carries the flag yet, so Complimentary = false (the zero-rate proxies apply).
// Cached per property and as-of date for 60 seconds (the management pack runs 21 study reports in one request).
using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Analytics;

public sealed class SqlAnalyticsInputSource(SqlPlatform platform, TimeProvider? clock = null) : IAnalyticsInputSource, Reports.MeasStudyB.IAnalyticsInputSource
{
    public const string LegacyUntil = "2026-10-30";
    public const string CovidFrom = "2020-03-01";
    public const string CovidTo = "2021-03-31";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly HashSet<string> Halls = new(StringComparer.OrdinalIgnoreCase) { "CON", "CONAC" };
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<(int, string), (DateTimeOffset At, AnInput Input, BeyondExtras Extras)> _cache = [];

    // ── IAnalyticsInputSource (study-a) ──
    public async Task<AnInput> LoadAsync(DateOnly asOf, CancellationToken ct = default) => await LoadAsync(await SinglePropertyAsync(ct).ConfigureAwait(false), asOf, ct).ConfigureAwait(false);

    public async Task<AnInput> LoadAsync(int propertyId, DateOnly asOf, CancellationToken ct = default) => (await LoadBothAsync(propertyId, asOf, ct).ConfigureAwait(false)).Input;

    // ── MeasStudyB.IAnalyticsInputSource (study-b): as of the business date, as the prototype (s.date) ──
    async Task<(AnInput Input, BeyondExtras Extras)> Reports.MeasStudyB.IAnalyticsInputSource.LoadAsync(Caller caller, BoundParams parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var b = await BusinessDateAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return await LoadBothAsync(caller.PropertyId, b, ct).ConfigureAwait(false);
    }

    public async Task<(AnInput Input, BeyondExtras Extras)> LoadBothAsync(int propertyId, DateOnly asOf, CancellationToken ct = default)
    {
        var business = await BusinessDateAsync(propertyId, ct).ConfigureAwait(false);
        if (asOf > business) asOf = business; // the prototype's AsOf is the business date; never past it
        var key = (propertyId, Iso(asOf));
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var hit) && now - hit.At < Ttl) return (hit.Input, hit.Extras);
        }
        var built = await BuildAsync(propertyId, asOf, ct).ConfigureAwait(false);
        lock (_gate)
        {
            foreach (var k in _cache.Where(kv => now - kv.Value.At >= Ttl).Select(kv => kv.Key).ToList()) _cache.Remove(k);
            _cache[key] = (now, built.Input, built.Extras);
        }
        return built;
    }

    private async Task<int> SinglePropertyAsync(CancellationToken ct)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT MIN(property_id) FROM inv.property;", conn);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is int p ? p : 1;
    }

    private async Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT business_date FROM inv.property WHERE property_id = @p;", conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is DateTime d ? DateOnly.FromDateTime(d) : DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string S(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "";

    private static string? Sn(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);

    private static long L(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);

    private static long? Ln(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);

    private static int Nights(string from, string to) =>
        Math.Max(1, DateOnly.ParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - DateOnly.ParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber);

    /// <summary>Live and legacy room-type text → the study type names (Suite / Premium / Deluxe / Twin), else the text as stored.</summary>
    public static string TypeName(string code) => code.Trim().ToUpperInvariant() switch
    {
        "SUITE" => "Suite", "PREMIUM" => "Premium", "DELUXE" => "Deluxe", "TWIN" => "Twin", "CONF" => "Conference Hall", _ => code.Trim(),
    };

    public static bool IsHall(string room, string? type) =>
        Halls.Contains(room.Trim()) || (type is { } t && (t.Trim().Equals("CONF", StringComparison.OrdinalIgnoreCase) || t.Contains("hall", StringComparison.OrdinalIgnoreCase) || t.Contains("conference", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Live reservation status → the prototype booking status text the selectors classify (Slept / NotSlept).</summary>
    public static string LiveStatus(string s) => s switch
    {
        "InHouse" => "In house", "Departed" => "Departed", "Cancelled" => "Cancelled", "NoShow" => "No show", "Held" => "Held", "Expired" => "Expired", _ => "Confirmed",
    };

    /// <summary>Booking source → (source, channel): live "OTA · Booking.com" and legacy "OTA:AGODA" are OTA with their channel; the workbooks name AGODA Agoda and MMT MakeMyTrip.</summary>
    public static (string Source, string? Channel) SourceOf(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return ("Direct", null);
        if (s.StartsWith("OTA · ", StringComparison.Ordinal)) return ("OTA", s[6..].Trim());
        if (s.StartsWith("OTA:", StringComparison.OrdinalIgnoreCase))
        {
            var c = s[4..].Trim();
            return ("OTA", c.Contains("AGODA", StringComparison.OrdinalIgnoreCase) ? "Agoda" : c.Contains("MMT", StringComparison.OrdinalIgnoreCase) ? "MakeMyTrip" : c.Length == 0 ? "OTA" : c);
        }
        return (s, null);
    }

    private static string Method(string m) => m.ToUpperInvariant() switch { "CASH" => "Cash", "UPI" => "UPI", "CARD" => "Card", "BANK" => "Bank transfer", _ => "Other" };

    private static (string? City, string? State) Place(string? address)
    {
        var parts = (address ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? (parts[^2], parts[^1]) : (null, null);
    }

    private const string LiveStaySql = """
        SELECT r.public_no, r.guest_name, r.phone, s.guest_id, r.arrival, r.departure, r.status, r.nightly_rate_paise, r.source, r.company_name, COALESCE(pv.name, r.purpose_code),
               COALESCE(seg.room_no, rm.room_no), r.adults + r.children, f.folio_id, g.nationality, g.address, g.phone, g.id_last4
          FROM inv.reservation r
          LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
          LEFT JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN bill.folio f ON f.reservation_id = r.reservation_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          LEFT JOIN mst.purpose_of_visit pv ON pv.property_id = r.property_id AND pv.code = r.purpose_code
          OUTER APPLY (SELECT TOP (1) x.room_no FROM gst.stay_segment sg JOIN inv.room x ON x.room_id = sg.room_id WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC) seg
         WHERE r.property_id = @p
         ORDER BY r.arrival, r.public_no;
        """;

    // Amounts: every line of the folio (a reversal nets its original to zero); charged nights: live room lines only.
    private const string LiveChargeSql = """
        SELECT c.folio_id,
               SUM(CASE WHEN c.kind = 'room' THEN c.amount_paise ELSE 0 END),
               SUM(CASE WHEN c.kind = 'food' THEN c.amount_paise ELSE 0 END),
               SUM(CASE WHEN c.kind IN ('service','correction') THEN c.amount_paise ELSE 0 END),
               SUM(CASE WHEN c.kind = 'discount' THEN c.amount_paise ELSE 0 END),
               SUM(c.tax_paise),
               SUM(CASE WHEN c.kind = 'room' AND c.reverses_charge_id IS NULL AND c.reversed_by_charge_id IS NULL THEN 1 ELSE 0 END),
               COUNT(*)
          FROM bill.charge c JOIN bill.folio f ON f.folio_id = c.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
         WHERE r.property_id = @p
         GROUP BY c.folio_id;
        """;

    private const string LiveReceiptSql = """
        SELECT x.receipt_id, x.folio_id, x.kind, x.method, x.amount_paise, x.business_date
          FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id JOIN inv.reservation r ON r.reservation_id = f.reservation_id
         WHERE r.property_id = @p
         ORDER BY x.business_date, x.receipt_id;
        """;

    private const string RoomSql = """
        SELECT rm.room_no, COALESCE(rt.name, rt.code), rt.code, rm.retired_on
          FROM inv.room rm JOIN inv.room_type rt ON rt.room_type_id = rm.room_type_id
         WHERE rm.property_id = @p
         ORDER BY rm.room_no;
        """;

    private const string LegacyStaySql = """
        SELECT ls.stay_id, ls.legacy_fy, ls.legacy_booking_id, ls.customer_key, gm.guest_id, ls.arrival_date, ls.departure_date, ls.expected_departure_date, ls.nights, ls.room_no,
               ls.purpose, ls.source_tag, ls.status, ls.meal_plan, ls.adults + ls.children
          FROM lgc.stay ls
          OUTER APPLY (SELECT TOP (1) m.guest_id FROM lgc.guest_map m WHERE m.property_id = ls.property_id AND m.legacy_guest_id = ls.legacy_guest_id AND m.guest_id IS NOT NULL ORDER BY m.guest_id) gm
         WHERE ls.property_id = @p AND ls.status <> 'Duplicate'
         ORDER BY ls.arrival_date, ls.legacy_fy, ls.legacy_booking_id;
        """;

    private const string LegacySegmentSql = """
        SELECT sg.stay_id, sg.room_no, sg.room_type_code, sg.rate_paise
          FROM lgc.room_segment sg JOIN lgc.stay ls ON ls.stay_id = sg.stay_id
         WHERE ls.property_id = @p AND sg.kind = 'booked' AND sg.room_no <> ''
         ORDER BY sg.stay_id;
        """;

    private const string LegacyBillSql = """
        SELECT b.bill_id, b.stay_id, b.bill_date, b.room_no, b.nights, b.room_charge_paise, b.extra_paise, b.fb_paise, b.laundry_paise, b.other_paise, b.discount_paise,
               b.room_gst_paise + b.fb_gst_paise + b.laundry_gst_paise + b.other_gst_paise, b.net_paise, b.balance_paise, b.paid_flag, b.advance_paise
          FROM lgc.bill b
         WHERE b.property_id = @p AND b.cancelled = 0
         ORDER BY b.bill_date, b.bill_id;
        """;

    private const string LegacyReceiptSql = """
        SELECT x.receipt_id, x.bill_id, x.stay_id, x.kind, x.method, x.amount_paise, x.received_date
          FROM lgc.receipt x
         WHERE x.property_id = @p AND x.kind IN ('advance','payment')
         ORDER BY x.received_date, x.receipt_id;
        """;

    /// <summary>Receipts linked to a legacy bill by its bill id OR its stay id (each row once), for the workbook's receivables reconciliation.</summary>
    private const string LegacyReceiptLinkSql = """
        SELECT x.receipt_id, x.bill_id, x.stay_id, x.kind, x.amount_paise, x.received_date
          FROM lgc.receipt x
         WHERE x.property_id = @p AND x.kind IN ('advance','payment','refund');
        """;

    private const string ExpenseSql = "SELECT business_date, category, amount_paise FROM ops.expense WHERE property_id = @p AND status = 'Posted' ORDER BY business_date, expense_id;";

    private const string AuditSql = "SELECT LEFT(action, 40), COUNT(*) FROM plat.audit_event WHERE property_id = @p GROUP BY LEFT(action, 40);";

    private sealed record LegacyStay(long Id, string Fy, long Booking, string GuestKey, string Arrival, string To, int Nights, string RoomNo, string? Purpose, string? Source,
        string Status, string? MealPlan, int People);

    private async Task<(AnInput Input, BeyondExtras Extras)> BuildAsync(int propertyId, DateOnly asOf, CancellationToken ct)
    {
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        async Task Read(string sql, Action<SqlDataReader> row)
        {
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) row(rd);
        }

        // ── rooms master (live) ──
        var live = new List<(string No, string Type, string Code, string? RetiredOn)>();
        await Read(RoomSql, r => live.Add((S(r, 0), S(r, 1), S(r, 2), r.IsDBNull(3) ? null : Iso(r.GetDateTime(3))))).ConfigureAwait(false);

        // ── legacy stays, one per booked room ──
        var lstays = new List<LegacyStay>();
        await Read(LegacyStaySql, r =>
        {
            var arrival = Iso(r.GetDateTime(5));
            var nights = (int)Math.Max(1, L(r, 8));
            var to = !r.IsDBNull(6) ? Iso(r.GetDateTime(6)) : LegacyCore.AddDays(arrival, nights);
            var status = S(r, 12) == "CheckedOut" ? "Departed" : "In house";
            if (status != "Departed" && string.CompareOrdinal(to, LegacyCore.AddDays(LegacyUntil, 1)) > 0)
            {
                // open at the backup / cutover: the legacy history stops at LegacyUntil, HCC live carries the rest
                to = string.CompareOrdinal(arrival, LegacyUntil) > 0 ? arrival : LegacyCore.AddDays(LegacyUntil, 1);
                nights = Math.Max(1, Nights(arrival, to));
            }
            var guestKey = !r.IsDBNull(4) ? "G" + L(r, 4).ToString(CultureInfo.InvariantCulture) : S(r, 3);
            lstays.Add(new LegacyStay(L(r, 0), S(r, 1), L(r, 2), guestKey, arrival, to, nights, S(r, 9), Sn(r, 10), Sn(r, 11), status, Sn(r, 13), (int)L(r, 14)));
        }).ConfigureAwait(false);
        var segs = new Dictionary<long, List<(string Room, string? Type, long? Rate)>>();
        await Read(LegacySegmentSql, r =>
        {
            var id = L(r, 0);
            if (!segs.TryGetValue(id, out var l)) segs[id] = l = [];
            l.Add((S(r, 1).Trim(), Sn(r, 2), Ln(r, 3)));
        }).ConfigureAwait(false);

        var segType = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in segs.Values)
            foreach (var s in l)
                if (s.Type is { Length: > 0 } t) segType.TryAdd(s.Room, t);
        var liveType = live.ToDictionary(x => x.No, x => x.Type, StringComparer.Ordinal);
        string? TypeOf(string room) => liveType.TryGetValue(room, out var t) ? t : segType.GetValueOrDefault(room);

        var stays = new List<AnStay>();
        var stayIdByRoom = new Dictionary<(long, string), string>();
        var firstStayId = new Dictionary<long, string>();
        var firstSale = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ls in lstays)
        {
            var booked = segs.GetValueOrDefault(ls.Id) ?? [];
            var rooms = booked.Select(b => b.Room).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (rooms.Count == 0) rooms = ls.RoomNo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var baseId = "L" + ls.Fy + "-" + ls.Booking.ToString(CultureInfo.InvariantCulture);
            var (src, ch) = SourceOf(ls.Source);
            foreach (var room in rooms)
            {
                if (IsHall(room, TypeOf(room))) continue;
                var id = firstStayId.ContainsKey(ls.Id) ? baseId + "/" + room : baseId;
                firstStayId.TryAdd(ls.Id, id);
                stayIdByRoom[(ls.Id, room)] = id;
                var rate = booked.FirstOrDefault(b => b.Room == room).Rate ?? 0;
                stays.Add(new AnStay(id, ls.GuestKey, room, ls.Arrival, ls.To, ls.Nights, rate, src, ch, null, ls.Purpose, ls.Status, false, true)
                    { MealPlan = ls.MealPlan, People = ls.People });
                // in service from the MONTH of the first sale (Occupancy workbook: rooms counted from the month of their first-ever sale)
                if (!firstSale.TryGetValue(room, out var f) || string.CompareOrdinal(ls.Arrival, f) < 0) firstSale[room] = ls.Arrival[..8] + "01";
            }
        }

        // ── legacy bills (guest rooms only) and receipts ──
        var bills = new List<AnBill>();
        var billOfStay = new Dictionary<long, string>();
        // DEC-265 receivables, the workbook's rule (build_receivables_report.py): a bill flagged unpaid (paid_flag N) owes max(balance - receipts after
        // the bill date, 0) when the stored balance is positive, else max(net - max(advance, payments - refunds), 0), over the receipts linked by the
        // bill OR its stay (each receipt once). Every other bill is paid in full. Paid = net - due.
        var links = new List<(long Id, long? Bill, long? Stay, string Kind, long Amount, string Date)>();
        await Read(LegacyReceiptLinkSql, r => links.Add((L(r, 0), Ln(r, 1), Ln(r, 2), S(r, 3), L(r, 4), Iso(r.GetDateTime(5))))).ConfigureAwait(false);
        var byBill = links.Where(x => x.Bill is not null).ToLookup(x => x.Bill!.Value);
        var byStay = links.Where(x => x.Stay is not null).ToLookup(x => x.Stay!.Value);
        long LegacyDue(long billId, long? stayId, string billDate, string paidFlag, long net, long advance, long balance)
        {
            if (paidFlag.Trim() != "N") return 0;
            var rs = byBill[billId].Concat(stayId is { } sid ? byStay[sid] : []).DistinctBy(x => x.Id).ToList();
            long pay = 0, refund = 0, later = 0;
            foreach (var x in rs)
            {
                if (x.Kind is "payment" or "advance") { pay += x.Amount; if (string.CompareOrdinal(x.Date, billDate) > 0) later += x.Amount; }
                else if (x.Kind == "refund") refund += x.Amount;
            }
            return balance > 0 ? Math.Max(balance - later, 0) : Math.Max(net - Math.Max(advance, pay - refund), 0);
        }
        await Read(LegacyBillSql, r =>
        {
            var rooms = S(r, 3).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rooms.Length == 0 || rooms.Any(x => Halls.Contains(x))) return;
            var id = "LB" + L(r, 0).ToString(CultureInfo.InvariantCulture);
            var stayKey = Ln(r, 1);
            var stay = stayKey is { } sk ? (stayIdByRoom.GetValueOrDefault((sk, rooms[0])) ?? firstStayId.GetValueOrDefault(sk) ?? "L-stay-" + sk.ToString(CultureInfo.InvariantCulture)) : "";
            if (stayKey is { } k2) billOfStay.TryAdd(k2, id);
            long net = L(r, 12), balance = L(r, 13);
            bills.Add(new AnBill(id, stay, Iso(r.GetDateTime(2)), L(r, 5), L(r, 7), L(r, 6) + L(r, 8) + L(r, 9), L(r, 10), L(r, 11), net, (int)(L(r, 4) * rooms.Length),
                Math.Max(0, net - Math.Min(Math.Max(0, net), LegacyDue(L(r, 0), stayKey, Iso(r.GetDateTime(2)), S(r, 14), net, L(r, 15), balance))), true));
        }).ConfigureAwait(false);
        var receipts = new List<AnReceipt>();
        await Read(LegacyReceiptSql, r =>
        {
            var bill = Ln(r, 1) is { } b ? "LB" + b.ToString(CultureInfo.InvariantCulture) : Ln(r, 2) is { } s ? billOfStay.GetValueOrDefault(s) ?? "" : "";
            receipts.Add(new AnReceipt("LR" + L(r, 0).ToString(CultureInfo.InvariantCulture), bill, Iso(r.GetDateTime(6)), Method(S(r, 4)), L(r, 5), true));
        }).ConfigureAwait(false);

        // ── live: reservations, folio totals, receipts ──
        var charges = new Dictionary<long, (long Room, long Fnb, long Other, long Disc, long Tax, int RoomLines, int Lines)>();
        await Read(LiveChargeSql, r => charges[L(r, 0)] = (L(r, 1), L(r, 2), L(r, 3), L(r, 4), L(r, 5), (int)L(r, 6), (int)L(r, 7))).ConfigureAwait(false);
        var pays = new Dictionary<long, List<(long Id, string Kind, string Method, long Amount, string Date)>>();
        await Read(LiveReceiptSql, r =>
        {
            var f = L(r, 1);
            if (!pays.TryGetValue(f, out var l)) pays[f] = l = [];
            l.Add((L(r, 0), S(r, 2), S(r, 3), L(r, 4), Iso(r.GetDateTime(5))));
        }).ConfigureAwait(false);
        await Read(LiveStaySql, r =>
        {
            var no = S(r, 0);
            string from = Iso(r.GetDateTime(4)), to = Iso(r.GetDateTime(5));
            var status = LiveStatus(S(r, 6));
            var guestKey = !r.IsDBNull(3) ? "G" + L(r, 3).ToString(CultureInfo.InvariantCulture) : S(r, 2) is { Length: > 0 } ph ? ph : S(r, 1).Trim().ToLowerInvariant();
            var (src, ch) = SourceOf(S(r, 8));
            var (city, state) = Place(Sn(r, 15));
            stays.Add(new AnStay(no, guestKey, S(r, 11), from, to, Nights(from, to), L(r, 7), src, ch, Sn(r, 9) is { Length: > 0 } co ? co : null, Sn(r, 10), status, false, false)
            {
                People = (int)L(r, 12), Nationality = Sn(r, 14), City = city, State = state, PhoneCaptured = !r.IsDBNull(2) || !r.IsDBNull(16), IdCaptured = !r.IsDBNull(17),
            });
            if (r.IsDBNull(13)) return;
            var folio = L(r, 13);
            var c = charges.GetValueOrDefault(folio);
            if (status is not ("In house" or "Departed") && c.Lines == 0) return;
            var net = c.Room + c.Fnb + c.Other + c.Disc;
            long paid = 0, credit = 0;
            foreach (var p in pays.GetValueOrDefault(folio) ?? [])
            {
                if (p.Kind is "payment" or "deposit-apply") paid += p.Amount;
                else if (p.Kind == "refund") paid -= p.Amount;
                else if (p.Kind is "company-credit" or "ota-credit" or "guest-credit") credit += p.Amount;
                if (p.Kind is "payment" or "deposit-apply") receipts.Add(new AnReceipt(p.Id.ToString(CultureInfo.InvariantCulture), "B-" + no, p.Date, p.Method, p.Amount, false));
            }
            bills.Add(new AnBill("B-" + no, no, status == "Departed" ? to : from, c.Room, c.Fnb, c.Other, -c.Disc, c.Tax, net + c.Tax, c.RoomLines, Math.Min(paid + credit, net + c.Tax), false));
        }).ConfigureAwait(false);

        // ── rooms: live master (halls out) + legacy-only rooms; in service from the first legacy sale when the room has legacy history ──
        var anRooms = new List<AnRoom>();
        foreach (var x in live)
        {
            if (IsHall(x.No, x.Type) || IsHall(x.No, x.Code)) continue;
            var suite = x.No == "211" || x.Type.Contains("suite", StringComparison.OrdinalIgnoreCase) || x.Code.Contains("suite", StringComparison.OrdinalIgnoreCase);
            anRooms.Add(new AnRoom(x.No, suite ? "Suite" : TypeName(x.Type), firstSale.GetValueOrDefault(x.No) ?? "2000-01-01", x.RetiredOn, suite));
        }
        foreach (var (room, first) in firstSale.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (liveType.ContainsKey(room)) continue;
            var t = segType.GetValueOrDefault(room) ?? "";
            var suite = room == "211" || t.Equals("SUITE", StringComparison.OrdinalIgnoreCase);
            anRooms.Add(new AnRoom(room, suite ? "Suite" : TypeName(t), first, null, suite));
        }

        // ── study-b extras the server can supply (expenses, audit counts); hall events and licences come from their modules later ──
        var expenses = new List<ExpenseFact>();
        await Read(ExpenseSql, r => expenses.Add(new ExpenseFact(Iso(r.GetDateTime(0)), S(r, 1), L(r, 2)))).ConfigureAwait(false);
        var audit = new List<AuditFact>();
        await Read(AuditSql, r => audit.Add(new AuditFact(S(r, 0), (int)L(r, 1)))).ConfigureAwait(false);
        audit.Sort((a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Action, b.Action));

        var input = new AnInput(anRooms, stays, bills, receipts, Iso(asOf), LegacyUntil, CovidFrom, CovidTo);
        return (input, new BeyondExtras { Expenses = expenses, Audit = audit });
    }
}
