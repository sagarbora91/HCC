// F8-RPT-P2-A · rpt.guests.userwise-activity — User-wise check-in and check-out (A33). Server twin of p2-a/userwise-activity.ts. No money. Permission PERM-ADM-004.
// Source: the stay record, which stores who checked the guest in / out and when (the same fact as the audit entries). The range binds BOTH actions.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.P2A.P2A;

namespace Hcc.Reporting.Reports.P2A;

public sealed class UserwiseActivityReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.guests.userwise-activity";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("user", "User"), T("at", "Date and time"), T("action", "Action"), T("booking", "Booking"), T("room", "Room"), T("guest", "Guest", ReportMask.GuestName), T("billNo", "Bill no")];

    public ReportDefinition Definition { get; } = new(Id, "User-wise check-in and check-out", "guests", "P2", "built", false, null, ["PERM-ADM-004"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("user", "User", "user"), new("action", "Action", "enum", null, ["both", "check-in", "check-out"], "both")],
        Columns, "Check-in and check-out entries of the audit trail by the business date they were made on (range up to 92 days); the range applies to both actions.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.No, false, ["A33"]);

    private static string Select(string kind, string by, string utc, string date) => $"""
        SELECT COALESCE(u.display_name, CAST(s.{by} AS NVARCHAR(20))), s.{utc}, '{kind}', r.public_no,
               COALESCE((SELECT TOP (1) rm2.room_no FROM gst.stay_segment sg JOIN inv.room rm2 ON rm2.room_id = sg.room_id WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC), rm.room_no, ''),
               COALESCE(g.full_name, r.guest_name), {GuestsSql.BillNoExpr}, s.{by}, u.login_name
          FROM gst.stay s
          JOIN inv.reservation r ON r.reservation_id = s.reservation_id
          JOIN gst.guest g ON g.guest_id = s.guest_id
          LEFT JOIN inv.room rm ON rm.room_id = r.room_id
          LEFT JOIN bill.folio f ON f.folio_id = s.folio_id
          LEFT JOIN plat.app_user u ON u.user_id = s.{by}
         WHERE s.property_id = @p AND s.{utc} IS NOT NULL AND s.{date} BETWEEN @from AND @to
        """;

    private static readonly string Sql = Select("Check-in", "checked_in_by", "checked_in_utc", "checked_in_date") + "\nUNION ALL\n" + Select("Check-out", "checked_out_by", "checked_out_utc", "checked_out_date") + ";";

    private sealed record Item(string User, string At, string Action, string Booking, string Room, string Guest, string Bill);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        Range(clock, parameters, 92);
        var want = parameters.Values.GetValueOrDefault("action") ?? "both";
        var who = (parameters.Values.GetValueOrDefault("user") ?? "").Trim();
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
                var action = rd.GetString(2);
                if ((action == "Check-in" && want == "check-out") || (action == "Check-out" && want == "check-in")) continue;
                if (who.Length > 0 && GuestsSql.Str(rd, 7) != who && !string.Equals(GuestsSql.Str(rd, 8), who, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new Item(GuestsSql.Str(rd, 0), GuestsSql.Local(clock, rd.GetDateTime(1)), action, rd.GetString(3), GuestsSql.Str(rd, 4), GuestsSql.Str(rd, 5), GuestsSql.Str(rd, 6)));
            }
        }

        var sorted = items.OrderBy(i => i.User, StringComparer.CurrentCulture).ThenBy(i => i.At, StringComparer.Ordinal).ThenBy(i => i.Booking, StringComparer.Ordinal).ToList();
        var rows = new List<ReportRow>();
        string cur = "";
        long ci = 0, co = 0, ti = 0, to = 0;
        static string Txt(long i, long o) => $"{Plural(i, "check-in")} · {Plural(o, "check-out")}";
        void Flush()
        {
            if (cur.Length == 0) return;
            var c = Cells(); c["user"] = cur; c["booking"] = Txt(ci, co);
            rows.Add(Row(ReportRowKind.Subtotal, c, cur));
        }

        foreach (var i in sorted)
        {
            if (i.User != cur) { Flush(); cur = i.User; ci = co = 0; }
            var c = Cells();
            c["user"] = i.User; c["at"] = i.At; c["action"] = i.Action; c["booking"] = i.Booking; c["room"] = i.Room; c["guest"] = i.Guest; c["billNo"] = i.Bill;
            rows.Add(Row(ReportRowKind.Row, c, i.User));
            if (i.Action == "Check-in") { ci++; ti++; } else { co++; to++; }
        }

        Flush();
        var t = Cells(); t["user"] = "Total"; t["booking"] = Txt(ti, to);
        rows.Add(Row(ReportRowKind.Total, t));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters));
    }
}
