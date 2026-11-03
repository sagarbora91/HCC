// F7-R3 · rpt.guests.rate-changes — Rate change detail (catalogue A36; gst.stay_rate_change, LF A1). Server twin of guests/rate-changes.ts.
// The report's own permission is the money gate PERM-RPT-002 (rate columns are money; the Cashier's own-shift grant does not reach a property-wide report).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Guests;

public sealed class RateChangesReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.rate-changes";

    private static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    private static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    private static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("changedAt", "Changed at"), T("room", "Room"), T("booking", "Booking"), T("guest", "Guest", ReportMask.GuestName), M("oldRate", "Old rate"), M("newRate", "New rate"),
        I("oldGuests", "Old guests"), I("newGuests", "New guests"), T("reason", "Reason"), T("authorisedBy", "Authorised by"), T("changedBy", "Changed by"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Rate change detail", "guests", "P1", "built", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("room", "Room", "room"), new("changedBy", "Changed by", "user")],
        Columns, "Changes by the business date they were made on (range up to 92 days). Rates are per night; guests = adults + children. Footer: count and net change in rate (new - old).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, ["A36"]);

    private const string Sql = """
        SELECT c.changed_utc, rm.room_no, r.public_no, COALESCE(g.full_name, r.guest_name), c.old_rate_paise, c.new_rate_paise, c.old_people + c.old_children, c.new_people + c.new_children, c.reason,
               COALESCE(ua.display_name, CAST(COALESCE(c.approved_by, c.changed_by) AS NVARCHAR(20))), COALESCE(uc.display_name, CAST(c.changed_by AS NVARCHAR(20))), c.change_id
          FROM gst.stay_rate_change c
          JOIN inv.reservation r ON r.reservation_id = c.reservation_id
          JOIN inv.room rm ON rm.room_id = c.room_id
          LEFT JOIN gst.stay s ON s.stay_id = c.stay_id
          LEFT JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN plat.app_user uc ON uc.user_id = c.changed_by
          LEFT JOIN plat.app_user ua ON ua.user_id = COALESCE(c.approved_by, c.changed_by)
         WHERE c.property_id = @p AND c.business_date BETWEEN @from AND @to
           AND (@room = '' OR rm.room_no = @room)
           AND (@by = '' OR CAST(c.changed_by AS NVARCHAR(20)) = @by OR LOWER(uc.login_name) = LOWER(@by))
         ORDER BY c.changed_utc, c.change_id;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var rows = new List<ReportRow>();
        long net = 0;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(Sql, conn);
            GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
            GuestsSql.Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
            GuestsSql.Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
            GuestsSql.Add(cmd, "@room", SqlDbType.VarChar, parameters.Values.GetValueOrDefault("room") ?? "");
            GuestsSql.Add(cmd, "@by", SqlDbType.NVarChar, parameters.Values.GetValueOrDefault("changedBy") ?? "");
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                long oldRate = rd.GetInt64(4), newRate = rd.GetInt64(5);
                net += newRate - oldRate;
                rows.Add(new ReportRow(ReportRowKind.Row, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["changedAt"] = GuestsSql.Local(clock, rd.GetDateTime(0)), ["room"] = rd.GetString(1), ["booking"] = rd.GetString(2), ["guest"] = GuestsSql.Str(rd, 3), ["oldRate"] = oldRate, ["newRate"] = newRate,
                    ["oldGuests"] = Convert.ToInt64(rd.GetValue(6)), ["newGuests"] = Convert.ToInt64(rd.GetValue(7)), ["reason"] = rd.GetString(8), ["authorisedBy"] = GuestsSql.Str(rd, 9), ["changedBy"] = GuestsSql.Str(rd, 10),
                }));
            }
        }

        var n = rows.Count;
        rows.Add(new ReportRow(ReportRowKind.Total, new Dictionary<string, object?>(StringComparer.Ordinal)
        { ["changedAt"] = $"Total · {n} change{(n == 1 ? "" : "s")}", ["newRate"] = net, ["reason"] = "Net change in rate (new - old)" }));
        var about = new ReportAbout(DateTimeOffset.MinValue, "", Definition.DateBasis, false, parameters.Legacy, parameters.From, parameters.To);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, about);
    }
}
