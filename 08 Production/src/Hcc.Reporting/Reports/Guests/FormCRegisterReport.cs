// F7-R3 · rpt.guests.form-c-register — Form C register (foreign guests; catalogue A13, C4.CFormReport). Server twin of guests/form-c-register.ts.
// Passport and visa numbers are masked in every format (passport: id_last4 through the engine mask; visa: last four here). "Form C submitted" = the Form C is
// recorded on the stay (HCC keeps no FRRO submission date yet, so "Submitted on" stays empty).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Guests;

public sealed class FormCRegisterReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.form-c-register";

    private static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    private static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        D("checkIn", "Check-in"), T("guest", "Guest", ReportMask.GuestName), T("nationality", "Nationality"), T("passportNo", "Passport no", ReportMask.IdNumber), T("passportIssue", "Passport issue date and place"),
        T("visa", "Visa no and type"), D("visaExpiry", "Visa expiry"), D("arrivedOn", "Arrived in India on"), T("arrivedFrom", "Arrived from"), T("nextDestination", "Next destination"), T("purpose", "Purpose"),
        T("room", "Room"), D("checkOut", "Check-out"), new("submitted", "Form C submitted", ReportColumnType.Bool, false), D("submittedOn", "Submitted on"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Form C register (foreign guests)", "guests", "P1", "built", false, null, ["PERM-RPT-001"], ["PERM-STY-001"], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("submitted", "Submitted", "enum", null, ["all", "yes", "no"], "all")],
        Columns, "Foreign guests by check-in date (range up to 92 days); submitted = Form C recorded on the stay. Passport and visa numbers are masked in every format.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Summary, false, ["A13", "C4.CFormReport"]);

    private const string Sql = """
        SELECT s.arrival, g.full_name, g.nationality, g.id_last4, fc.passport_issued, fc.passport_place, fc.visa_no, fc.visa_type, fc.visa_expiry, fc.arrival_in_india, fc.arrived_from, fc.next_destination,
               COALESCE(pv.name, r.purpose_code), rm.room_no, s.checked_out_date, CASE WHEN fc.stay_id IS NULL THEN 0 ELSE 1 END
          FROM gst.stay s
          JOIN inv.reservation r ON r.reservation_id = s.reservation_id
          JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN gst.stay_form_c fc ON fc.stay_id = s.stay_id
          LEFT JOIN mst.purpose_of_visit pv ON pv.property_id = r.property_id AND pv.code = r.purpose_code
          CROSS APPLY (SELECT TOP (1) sg.room_id FROM gst.stay_segment sg WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC) seg
          JOIN inv.room rm ON rm.room_id = seg.room_id
         WHERE s.property_id = @p AND s.arrival BETWEEN @from AND @to
           AND (fc.stay_id IS NOT NULL OR LOWER(LTRIM(RTRIM(g.nationality))) NOT IN (N'indian', N'india', N''))
           AND (@sub = 'all' OR (@sub = 'yes' AND fc.stay_id IS NOT NULL) OR (@sub = 'no' AND fc.stay_id IS NULL))
         ORDER BY s.arrival, rm.room_no;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var body = new List<ReportRow>();
        long todo = 0;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            GuestsSql.Add(cmd, "@sub", SqlDbType.VarChar, parameters.Values.GetValueOrDefault("submitted") ?? "all");
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var has = Convert.ToInt32(rd.GetValue(15)) == 1;
                if (!has) todo++;
                body.Add(new ReportRow(ReportRowKind.Row, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["checkIn"] = GuestsSql.Date(rd, 0), ["guest"] = GuestsSql.Str(rd, 1), ["nationality"] = GuestsSql.Str(rd, 2), ["passportNo"] = GuestsSql.Str(rd, 3),
                    ["passportIssue"] = has ? $"{GuestsSql.Iso(GuestsSql.Date(rd, 4))} · {rd.GetString(5)}" : "", ["visa"] = has ? $"{GuestsSql.VisaMasked(rd.GetString(6))} · {rd.GetString(7)}" : "",
                    ["visaExpiry"] = has ? GuestsSql.Date(rd, 8) : null, ["arrivedOn"] = has ? GuestsSql.Date(rd, 9) : null, ["arrivedFrom"] = GuestsSql.Str(rd, 10), ["nextDestination"] = GuestsSql.Str(rd, 11),
                    ["purpose"] = GuestsSql.Str(rd, 12), ["room"] = rd.GetString(13), ["checkOut"] = GuestsSql.DateN(rd, 14), ["submitted"] = has, ["submittedOn"] = null,
                }));
            }
        }

        var rows = new List<ReportRow> { new(ReportRowKind.Subtotal, new Dictionary<string, object?>(StringComparer.Ordinal) { ["nationality"] = $"To submit: {todo}" }) };
        rows.AddRange(body);
        rows.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?>(StringComparer.Ordinal) { ["nationality"] = $"{body.Count} foreign stay{(body.Count == 1 ? "" : "s")}" }));
        var about = new ReportAbout(DateTimeOffset.MinValue, "", Definition.DateBasis, false, parameters.Legacy, parameters.From, parameters.To);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, about);
    }
}
