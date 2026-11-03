// F7-R3 · rpt.guests.movement-register — Arrivals and departures register (catalogue A30). Server twin of 03 Prototype/src/domain/reports/guests/movement-register.ts
// (same column keys, row rules and labels). Money: late check-out charge and balance due (gate PERM-RPT-002).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Guests;

public sealed class MovementRegisterReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.movement-register";

    private static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        new("date", "Date", ReportColumnType.Date, false), T("booking", "Booking"), T("guest", "Guest", ReportMask.GuestName), T("room", "Room"), T("roomType", "Room type"),
        new("pax", "Pax", ReportColumnType.Int, false), new("nights", "Nights", ReportColumnType.Int, false), T("source", "Source"), T("status", "Status"), T("checkInTime", "Check-in time"),
        T("checkOutTime", "Check-out time"), T("basis", "Check-out basis"), new("lateCharge", "Late check-out charge", ReportColumnType.Money, true), T("purpose", "Purpose of visit"),
        T("vehicle", "Vehicle no"), new("balanceDue", "Balance due", ReportColumnType.Money, true),
    ];

    public static readonly IReadOnlyList<string> StatusOptions = ["all", "booked", "checked in", "checked out", "cancelled", "no-show"];

    public ReportDefinition Definition { get; } = new(Id, "Arrivals and departures register", "guests", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Forward,
        [new("type", "Type", "enum", null, ["both", "arrivals", "departures"], "both"), new("from", "From", "date", ReportDateClass.Forward), new("to", "To", "date", ReportDateClass.Forward),
         new("status", "Status", "enum", null, StatusOptions, "all"), new("roomType", "Room type", "text"), new("source", "Source", "text")],
        Columns, "Arrivals by arrival date, departures by departure date (booked date until the guest leaves); range up to 92 days, up to 90 days ahead. Late check-out charge and balance due include GST.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Summary, false, ["A30"]);

    private const string Sql = $"""
        SELECT x.kind, x.mdate, r.public_no, COALESCE(g.full_name, r.guest_name), rm.room_no, COALESCE(rt.name, rt.code), r.adults + r.children, DATEDIFF(DAY, r.arrival, r.departure), r.source, r.status,
               s.checked_in_utc, s.checked_out_utc, r.checkout_basis, COALESCE(pv.name, r.purpose_code), r.vehicle_no,
               {GuestsSql.LateChargeExpr}, {GuestsSql.BalanceExpr}, rm.room_id
          FROM (SELECT reservation_id, arrival AS mdate, 'arrival' AS kind FROM inv.reservation WHERE property_id = @p AND @want <> 'departures' AND arrival BETWEEN @from AND @to
                UNION ALL
                SELECT reservation_id, departure, 'departure' FROM inv.reservation WHERE property_id = @p AND @want <> 'arrivals' AND departure BETWEEN @from AND @to AND status IN ('Confirmed','InHouse','Departed')) x
          JOIN inv.reservation r ON r.reservation_id = x.reservation_id
          LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
          LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
          LEFT JOIN gst.guest g ON g.guest_id = COALESCE(s.guest_id, reg.guest_id)
          LEFT JOIN inv.room rm ON rm.room_id = COALESCE((SELECT TOP (1) sg.room_id FROM gst.stay_segment sg WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC), r.room_id)
          LEFT JOIN inv.room_type rt ON rt.room_type_id = COALESCE(rm.room_type_id, r.room_type_id)
          LEFT JOIN mst.purpose_of_visit pv ON pv.property_id = r.property_id AND pv.code = r.purpose_code
          LEFT JOIN bill.folio f ON f.reservation_id = r.reservation_id
         ORDER BY x.mdate, rm.room_no, x.kind;
        """;

    private sealed record Item(DateOnly Date, string Booking, string Guest, string Room, string RoomType, long Pax, long Nights, string Source, string Status, string In, string Out,
        string Basis, long Late, string Purpose, string Vehicle, long Balance);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        GuestsSql.RangeGuard(from, to, 92, clock.BusinessDate, 90);
        var v = parameters.Values;
        var want = v.GetValueOrDefault("type") ?? "both";
        var st = v.GetValueOrDefault("status") ?? "all";
        var rt = (v.GetValueOrDefault("roomType") ?? "").Trim();
        var src = (v.GetValueOrDefault("source") ?? "").Trim();
        var items = new List<Item>();
        {
            await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@want", SqlDbType.VarChar, want);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to));
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var status = GuestsSql.MovementStatus(rd.GetString(9));
                var type = GuestsSql.Str(rd, 5);
                var source = GuestsSql.Str(rd, 8);
                if (st != "all" && status != st) continue;
                if (rt.Length > 0 && !string.Equals(type, rt, StringComparison.OrdinalIgnoreCase)) continue;
                if (src.Length > 0 && !string.Equals(source, src, StringComparison.OrdinalIgnoreCase)) continue;
                var inUtc = rd.IsDBNull(10) ? (DateTime?)null : rd.GetDateTime(10);
                var outUtc = rd.IsDBNull(11) ? (DateTime?)null : rd.GetDateTime(11);
                items.Add(new Item(GuestsSql.Date(rd, 1), rd.GetString(2), GuestsSql.Str(rd, 3), GuestsSql.Str(rd, 4), type, Convert.ToInt64(rd.GetValue(6)), Convert.ToInt64(rd.GetValue(7)), source,
                    char.ToUpperInvariant(status[0]) + status[1..], GuestsSql.Local(clock, inUtc), GuestsSql.Local(clock, outUtc), rd.GetString(12).Trim() == "24h" ? "24-hour" : "Fixed time",
                    Convert.ToInt64(rd.GetValue(15)), GuestsSql.Str(rd, 13), GuestsSql.Str(rd, 14), Convert.ToInt64(rd.GetValue(16))));
            }
        }

        var rows = new List<ReportRow>();
        DateOnly? day = null;
        long dRooms = 0, dPax = 0, dLate = 0, dBal = 0, tRooms = 0, tPax = 0, tLate = 0, tBal = 0;
        static string Rooms(long n) => $"{n} room{(n == 1 ? "" : "s")}";
        void Flush()
        {
            if (day is null) return;
            rows.Add(new ReportRow(ReportRowKind.Subtotal, new Dictionary<string, object?>(StringComparer.Ordinal)
            { ["date"] = day, ["booking"] = $"Day total · {Rooms(dRooms)}", ["pax"] = dPax, ["lateCharge"] = dLate, ["balanceDue"] = dBal }, GuestsSql.Iso(day.Value)));
        }

        foreach (var i in items)
        {
            if (i.Date != day) { Flush(); day = i.Date; dRooms = dPax = dLate = dBal = 0; }
            rows.Add(new ReportRow(ReportRowKind.Row, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["date"] = i.Date, ["booking"] = i.Booking, ["guest"] = i.Guest, ["room"] = i.Room, ["roomType"] = i.RoomType, ["pax"] = i.Pax, ["nights"] = i.Nights, ["source"] = i.Source,
                ["status"] = i.Status, ["checkInTime"] = i.In, ["checkOutTime"] = i.Out, ["basis"] = i.Basis, ["lateCharge"] = i.Late, ["purpose"] = i.Purpose, ["vehicle"] = i.Vehicle, ["balanceDue"] = i.Balance,
            }, GuestsSql.Iso(i.Date)));
            dRooms++; dPax += i.Pax; dLate += i.Late; dBal += i.Balance; tRooms++; tPax += i.Pax; tLate += i.Late; tBal += i.Balance;
        }

        Flush();
        rows.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?>(StringComparer.Ordinal) { ["booking"] = $"Total · {Rooms(tRooms)}", ["pax"] = tPax, ["lateCharge"] = tLate, ["balanceDue"] = tBal }));
        var about = new ReportAbout(DateTimeOffset.MinValue, "", Definition.DateBasis, false, parameters.Legacy, parameters.From, parameters.To);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, about);
    }
}
