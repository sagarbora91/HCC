// F8-RPT-P2-A · rpt.guests.reservations-register — Reservations register (A37 purpose). Server twin of p2-a/reservations-register.ts (same column keys, rules, labels).
// Money: advance (deposit receipts) and quoted total (the quote's total when the booking came from a quote, else nightly rate x nights), gate PERM-RPT-002.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class ReservationsRegisterReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.reservations-register";

    public static readonly IReadOnlyList<string> StatusOptions = ["all", "held", "confirmed", "checked in", "checked out", "cancelled", "no-show", "expired"];

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("booking", "Booking"), D("created", "Created"), T("guest", "Guest", ReportMask.GuestName), D("arrival", "Arrival"), D("departure", "Departure"), T("roomType", "Room type"), I("rooms", "Rooms"),
        T("plan", "Plan"), T("source", "Source"), T("status", "Status"), M("advance", "Advance"), M("quoted", "Quoted total"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Reservations register", "guests", "P2", "built", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Forward,
        [new("from", "From", "date", ReportDateClass.Forward), new("to", "To", "date", ReportDateClass.Forward), new("status", "Status", "enum", null, StatusOptions, "all"), new("source", "Source", "text"), new("search", "Search", "text")],
        Columns, "Bookings by arrival date (range up to 92 days, up to 90 days ahead). Advance = deposits received on the booking. Quoted total = nightly rate x nights plus room GST at the rates in force on each night. Created = the date the booking was entered (blank when not recorded).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, ["A37"]);

    private const string Sql = """
        SELECT r.public_no, r.created_utc, COALESCE(g.full_name, r.guest_name), r.arrival, r.departure, COALESCE(rt.name, rt.code), mp.code, r.source, r.status,
               ISNULL((SELECT SUM(x.amount_paise) FROM pay.receipt x JOIN bill.folio f ON f.folio_id = x.folio_id WHERE f.reservation_id = r.reservation_id AND x.kind = 'deposit'), 0),
               COALESCE(q.total_paise, ISNULL(r.nightly_rate_paise, 0) * DATEDIFF(DAY, r.arrival, r.departure)), r.phone, g.phone, rm.room_no
          FROM inv.reservation r
          LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
          LEFT JOIN gst.guest g ON g.guest_id = reg.guest_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          LEFT JOIN inv.room_type rt ON rt.room_type_id = COALESCE(rm.room_type_id, r.room_type_id)
          LEFT JOIN bill.reservation_meal_plan rmp ON rmp.reservation_id = r.reservation_id
          LEFT JOIN bill.meal_plan mp ON mp.meal_plan_id = rmp.meal_plan_id
          LEFT JOIN bill.quote q ON q.quote_id = r.quote_id
         WHERE r.property_id = @p AND r.arrival BETWEEN @from AND @to;
        """;

    private sealed record Item(string Booking, string Created, string Guest, DateOnly Arrival, DateOnly Departure, string Type, string Plan, string Source, string Status, long Advance, long Quoted, string Phone, string Room);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        Range(clock, parameters, 92, 90);
        var v = parameters.Values;
        var st = v.GetValueOrDefault("status") ?? "all";
        var src = (v.GetValueOrDefault("source") ?? "").Trim();
        var q = (v.GetValueOrDefault("search") ?? "").Trim().ToLowerInvariant();
        var items = new List<Item>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var status = StatusOf(rd.GetString(8));
                var source = GuestsSql.Str(rd, 7);
                var booking = rd.GetString(0);
                var guest = GuestsSql.Str(rd, 2);
                var phone = GuestsSql.Str(rd, 11) + "|" + GuestsSql.Str(rd, 12);
                if (st != "all" && status != st) continue;
                if (src.Length > 0 && !string.Equals(source, src, StringComparison.OrdinalIgnoreCase)) continue;
                if (q.Length > 0 && !(booking + "|" + guest + "|" + phone).Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new Item(booking, GuestsSql.Iso(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(rd.GetDateTime(1), DateTimeKind.Utc), clock.Zone))), guest, GuestsSql.Date(rd, 3), GuestsSql.Date(rd, 4),
                    GuestsSql.Str(rd, 5), GuestsSql.Str(rd, 6), source, status, Convert.ToInt64(rd.GetValue(9)), Convert.ToInt64(rd.GetValue(10)), phone, GuestsSql.Str(rd, 13)));
            }
        }

        var sorted = items.OrderBy(i => i.Arrival).ThenBy(i => i.Room, Natural).ThenBy(i => i.Booking, StringComparer.Ordinal).ToList();
        var rows = sorted.Select(i =>
        {
            var c = Cells();
            c["booking"] = i.Booking; c["created"] = DateOnly.Parse(i.Created, System.Globalization.CultureInfo.InvariantCulture); c["guest"] = i.Guest; c["arrival"] = i.Arrival; c["departure"] = i.Departure; c["roomType"] = i.Type; c["rooms"] = 1L;
            c["plan"] = i.Plan; c["source"] = i.Source; c["status"] = Cap(i.Status); c["advance"] = i.Advance; c["quoted"] = i.Quoted;
            return Row(ReportRowKind.Row, c);
        }).ToList();
        var mix = string.Join(" · ", new[] { "held", "confirmed", "checked in", "checked out", "cancelled", "no-show", "expired" }
            .Select(x => (x, n: sorted.Count(i => i.Status == x))).Where(t => t.n > 0).Select(t => $"{Cap(t.x)} {t.n}"));
        var tc = Cells();
        tc["booking"] = $"Total · {Plural(sorted.Count, "booking")}"; tc["rooms"] = (long)sorted.Count; tc["status"] = mix; tc["advance"] = sorted.Sum(i => i.Advance); tc["quoted"] = sorted.Sum(i => i.Quoted);
        rows.Add(Row(ReportRowKind.Total, tc));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
