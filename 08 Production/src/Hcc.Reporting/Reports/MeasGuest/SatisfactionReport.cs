// F12-RPT-GUEST · rpt.guests.satisfaction — Guest satisfaction and NPS (meas.survey; switch meas.guest.survey). Twin of meas-guest/satisfaction.ts.
// meas.survey has no identity column; the verbatim lists comments of surveys with consent only.
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasGuest;

public sealed class SatisfactionReport(SqlPlatform platform, ISettingsReader? settings = null) : MeasGuestBase(platform, settings)
{
    public const string Id = "rpt.guests.satisfaction";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MeasGuestRules.T("month", "Month"), MeasGuestRules.I("responses", "Responses"), MeasGuestRules.P("responseRate", "Response rate"), MeasGuestRules.T("overall", "Overall"),
        MeasGuestRules.T("cleanliness", "Cleanliness"), MeasGuestRules.T("staff", "Staff"), MeasGuestRules.T("value", "Value"), MeasGuestRules.T("breakfast", "Breakfast"),
        MeasGuestRules.I("nps", "NPS"), MeasGuestRules.T("verbatim", "Verbatim"),
    ];

    public override string MeasKey => "meas.guest.survey";

    public override ReportDefinition Definition { get; } = new(Id, "Guest satisfaction and NPS", "guests", "P1", "MEAS", false, null, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month), new("roomType", "Room type", "text"), new("channel", "Channel", "text")],
        Columns, "One row for the month by the survey date (guest identity is never shown). Scores are averages of 1-5; NPS = % scoring 9-10 minus % scoring 0-6 on the recommend question; response rate = surveys / stays that left in the month. Verbatim lists comments of guests who gave consent.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private sealed record Survey(int Overall, int Cleanliness, int Staff, int Value, int? Breakfast, int Recommend, string? Comment, bool Consent);

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(Platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        var month = Param(parameters, "month");
        if (month.Length == 0) month = GuestsSql.Iso(clock.BusinessDate)[..7];
        string rt = Param(parameters, "roomType"), ch = Param(parameters, "channel");
        var list = new List<Survey>();
        long left;
        await using (var conn = await Platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand("""
                SELECT at_utc, overall, cleanliness, staff, value_score, breakfast, recommend, comment, consent
                  FROM meas.survey
                 WHERE property_id = @p AND at_utc >= DATEADD(DAY, -2, @from) AND at_utc < DATEADD(DAY, 33, @from)
                   AND (@rt = '' OR room_type = @rt) AND (@ch = '' OR channel = @ch)
                 ORDER BY at_utc, survey_id;
                """, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@from", SqlDbType.DateTime2, DateTime.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture));
                Add(cmd, "@rt", SqlDbType.NVarChar, rt);
                Add(cmd, "@ch", SqlDbType.NVarChar, ch);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (LocalOf(clock, rd.GetDateTime(0)).ToString("yyyy-MM", CultureInfo.InvariantCulture) != month) continue;
                    list.Add(new Survey(rd.GetByte(1), rd.GetByte(2), rd.GetByte(3), rd.GetByte(4), rd.IsDBNull(5) ? null : rd.GetByte(5), rd.GetByte(6), rd.IsDBNull(7) ? null : rd.GetString(7), rd.GetBoolean(8)));
                }
            }

            if (list.Count == 0) return Result(parameters, [], "gsv.survey (meas-cap-guest)");
            await using var c2 = new SqlCommand("""
                SELECT COUNT(*) FROM gst.stay st
                  JOIN inv.reservation r ON r.reservation_id = st.reservation_id
                  LEFT JOIN inv.room rm ON rm.room_id = r.room_id
                  LEFT JOIN inv.room_type t ON t.room_type_id = COALESCE(r.room_type_id, rm.room_type_id)
                 WHERE st.property_id = @p AND st.status = 'CheckedOut' AND st.checked_out_date BETWEEN @from AND @to
                   AND (@rt = '' OR t.code = @rt) AND (@ch = '' OR r.source = @ch);
                """, conn);
            Add(c2, "@p", SqlDbType.Int, caller.PropertyId);
            Add(c2, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            Add(c2, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            Add(c2, "@rt", SqlDbType.NVarChar, rt);
            Add(c2, "@ch", SqlDbType.NVarChar, ch);
            left = Convert.ToInt64(await c2.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        var bf = list.Where(v => v.Breakfast is not null).ToList();
        var verbatim = string.Join(" | ", list.Where(v => v.Consent && !string.IsNullOrEmpty(v.Comment)).Take(5).Select(v => v.Comment!.Trim()));
        var n = list.Count;
        return Result(parameters,
        [
            Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["month"] = month, ["responses"] = (long)n, ["responseRate"] = MeasGuestRules.Pct1(n, left),
                ["overall"] = MeasGuestRules.Avg1(list.Sum(v => v.Overall), n), ["cleanliness"] = MeasGuestRules.Avg1(list.Sum(v => v.Cleanliness), n),
                ["staff"] = MeasGuestRules.Avg1(list.Sum(v => v.Staff), n), ["value"] = MeasGuestRules.Avg1(list.Sum(v => v.Value), n),
                ["breakfast"] = MeasGuestRules.Avg1(bf.Sum(v => v.Breakfast!.Value), bf.Count), ["nps"] = MeasGuestRules.Nps(list.Select(v => v.Recommend).ToList()), ["verbatim"] = verbatim,
            }),
        ]);
    }
}
