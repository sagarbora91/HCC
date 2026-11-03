// F7-R3 · rpt.guests.guest-register — Guest register (statutory; catalogue C1.GuestRegister, C4.CheckInReport). Server twin of guests/guest-register.ts.
// Money flag n. ID numbers masked in every format (only id_last4 is read; the engine masks it to XXXX-XXXX-1234). Legacy stays (lgc.stay) are listed for ranges
// before 2026-04-01 with the guest's details from the matched HCC profile when there is one (DEC-162).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Guests;

public sealed class GuestRegisterReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.guest-register";

    private static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    private static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("regNo", "Registration no"), T("checkIn", "Check-in"), T("guest", "Guest", ReportMask.GuestName), T("nationality", "Nationality"), T("city", "City"), T("phone", "Phone"),
        T("idType", "ID type"), T("idNumber", "ID number", ReportMask.IdNumber), T("room", "Room"), I("adults", "Adults"), I("children", "Children"), T("purpose", "Purpose of visit"),
        T("vehicle", "Vehicle no"), T("comingFrom", "Coming from"), T("goingTo", "Going to"), T("checkOut", "Check-out"), T("billNo", "Bill no"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Guest register (statutory)", "guests", "P1", "built", false, null, ["PERM-RPT-001"], ["PERM-STY-001"], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("status", "Status", "enum", null, ["all", "in house", "checked out"], "all")],
        Columns, "Stays by check-in business date (range up to 92 days); a stay still in house shows no check-out. ID numbers are masked in every format.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No, false, ["C1.GuestRegister", "C4.CheckInReport", "C4.PhotoAddressProof"]);

    private const string Sql = $"""
        SELECT r.public_no, s.checked_in_utc, g.full_name, g.nationality, g.address, g.phone, g.id_type, g.id_last4, rm.room_no, r.adults, r.children, COALESCE(pv.name, r.purpose_code),
               r.vehicle_no, fc.arrived_from, fc.next_destination, s.checked_out_utc, {GuestsSql.BillNoExpr}, s.status
          FROM gst.stay s
          JOIN inv.reservation r ON r.reservation_id = s.reservation_id
          JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN bill.folio f ON f.folio_id = s.folio_id
          LEFT JOIN gst.stay_form_c fc ON fc.stay_id = s.stay_id
          LEFT JOIN mst.purpose_of_visit pv ON pv.property_id = r.property_id AND pv.code = r.purpose_code
          CROSS APPLY (SELECT TOP (1) sg.room_id FROM gst.stay_segment sg WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC) seg
          JOIN inv.room rm ON rm.room_id = seg.room_id
         WHERE s.property_id = @p AND s.arrival BETWEEN @from AND @to AND (@st = 'all' OR (@st = 'in house' AND s.status = 'InHouse') OR (@st = 'checked out' AND s.status = 'CheckedOut'))
         ORDER BY r.public_no;
        """;

    private const string LegacySql = """
        SELECT CONCAT('L', ls.legacy_fy, '-', ls.legacy_booking_id), ls.arrival_utc, COALESCE(g.full_name, ls.guest_name_snapshot), g.nationality, g.address, g.phone, g.id_type, g.id_last4, ls.room_no,
               ls.adults, ls.children, ls.purpose, ls.departure_utc, ls.legacy_bill_no
          FROM lgc.stay ls
          LEFT JOIN lgc.guest_map gm ON gm.property_id = ls.property_id AND gm.legacy_guest_id = ls.legacy_guest_id
          LEFT JOIN gst.guest g ON g.guest_id = gm.guest_id
         WHERE ls.property_id = @p AND ls.arrival_date BETWEEN @from AND @to AND ls.status <> 'Duplicate'
           AND (@st = 'all' OR (@st = 'in house' AND ls.status <> 'CheckedOut') OR (@st = 'checked out' AND ls.status = 'CheckedOut'))
         ORDER BY ls.legacy_fy, ls.legacy_booking_id;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        GuestsSql.RangeGuard(from, to, 92, clock.BusinessDate);
        var st = parameters.Values.GetValueOrDefault("status") ?? "all";
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        if (from < ParamBinder.LegacyBefore)
        {
            await using var lc = new SqlCommand(LegacySql, conn);
            GuestsSql.Add(lc, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(lc, "@from", SqlDbType.Date, GuestsSql.D(from));
            GuestsSql.Add(lc, "@to", SqlDbType.Date, GuestsSql.D(to));
            GuestsSql.Add(lc, "@st", SqlDbType.VarChar, st);
            await using var lr = await lc.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await lr.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new ReportRow(ReportRowKind.Row, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["regNo"] = lr.GetString(0), ["checkIn"] = GuestsSql.Local(clock, lr.GetDateTime(1)), ["guest"] = GuestsSql.Str(lr, 2), ["nationality"] = GuestsSql.Str(lr, 3),
                    ["city"] = GuestsSql.CityOfAddress(GuestsSql.Str(lr, 4)), ["phone"] = GuestsSql.Str(lr, 5), ["idType"] = GuestsSql.Str(lr, 6), ["idNumber"] = GuestsSql.Str(lr, 7),
                    ["room"] = GuestsSql.Str(lr, 8), ["adults"] = Convert.ToInt64(lr.GetValue(9)), ["children"] = Convert.ToInt64(lr.GetValue(10)), ["purpose"] = GuestsSql.Str(lr, 11),
                    ["vehicle"] = "", ["comingFrom"] = "", ["goingTo"] = "", ["checkOut"] = lr.IsDBNull(12) ? "" : GuestsSql.Local(clock, lr.GetDateTime(12)), ["billNo"] = lr.IsDBNull(13) ? "" : "L-" + Convert.ToString(lr.GetValue(13), System.Globalization.CultureInfo.InvariantCulture),
                }));
        }

        if (to >= ParamBinder.LegacyBefore)
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(from < ParamBinder.LegacyBefore ? ParamBinder.LegacyBefore : from));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(to));
            GuestsSql.Add(cmd, "@st", SqlDbType.VarChar, st);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new ReportRow(ReportRowKind.Row, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["regNo"] = rd.GetString(0), ["checkIn"] = GuestsSql.Local(clock, rd.GetDateTime(1)), ["guest"] = GuestsSql.Str(rd, 2), ["nationality"] = GuestsSql.Str(rd, 3),
                    ["city"] = GuestsSql.CityOfAddress(GuestsSql.Str(rd, 4)), ["phone"] = GuestsSql.Str(rd, 5), ["idType"] = GuestsSql.Str(rd, 6), ["idNumber"] = GuestsSql.Str(rd, 7),
                    ["room"] = rd.GetString(8), ["adults"] = Convert.ToInt64(rd.GetValue(9)), ["children"] = Convert.ToInt64(rd.GetValue(10)), ["purpose"] = GuestsSql.Str(rd, 11),
                    ["vehicle"] = GuestsSql.Str(rd, 12), ["comingFrom"] = GuestsSql.Str(rd, 13), ["goingTo"] = GuestsSql.Str(rd, 14),
                    ["checkOut"] = rd.IsDBNull(15) ? "" : GuestsSql.Local(clock, rd.GetDateTime(15)), ["billNo"] = GuestsSql.Str(rd, 16),
                }));
        }

        var n = rows.Count;
        rows.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?>(StringComparer.Ordinal) { ["regNo"] = $"{n} stay{(n == 1 ? "" : "s")}" }));
        var about = new ReportAbout(DateTimeOffset.MinValue, "", Definition.DateBasis, false, parameters.Legacy, parameters.From, parameters.To);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, about);
    }
}
