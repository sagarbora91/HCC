// F12-RPT-GUEST · rpt.guests.desk-timing — Arrival and departure experience (gst.stay instants, ops.key_job, bill.charge / pay.receipt times; switch meas.guest.desk-timing). Twin of meas-guest/desk-timing.ts.
// Arrival-to-key = check-in instant to the first finished key job of the stay; bill-to-checkout = the last charge or receipt in the 60 minutes before check-out. Whole minutes, lower median.
using Hcc.Application.Billing;
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasGuest;

public sealed class DeskTimingReport(SqlPlatform platform, ISettingsReader? settings = null) : MeasGuestBase(platform, settings)
{
    public const string Id = "rpt.guests.desk-timing";
    public const int BillWindowMinutes = 60;

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MeasGuestRules.T("hour", "Hour"), MeasGuestRules.T("deskUser", "Desk user"), MeasGuestRules.I("arrivals", "Arrivals"), MeasGuestRules.I("arrivalToKey", "Median minutes arrival-to-key"),
        MeasGuestRules.I("billToCheckout", "Median minutes bill-to-checkout"), MeasGuestRules.I("queuePeak", "Queue peak"),
    ];

    public override string MeasKey => "meas.guest.desk-timing";

    public override ReportDefinition Definition { get; } = new(Id, "Arrival and departure experience", "guests", "P2", "MEAS", false, null, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.Business,
        [new("from", "From", "date", ReportDateClass.Business), new("to", "To", "date", ReportDateClass.Business), new("deskUser", "Desk user", "user")],
        Columns, "Check-ins and check-outs by the hour they happened (range up to 92 days), one row per hour and desk user. Arrival-to-key = check-in to the first finished key job; bill-to-checkout = last bill action to the check-out; medians are whole minutes (lower median); queue peak = most check-ins waiting for a key at the same time.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No);

    private sealed record Ev(bool In, string User, DateTime At, long? Minutes, DateTime? End);

    private const string InSql = """
        SELECT st.checked_in_utc, COALESCE(u.display_name, CAST(st.checked_in_by AS NVARCHAR(20))),
               (SELECT MIN(j.finished_utc) FROM ops.key_job j WHERE j.reservation_id = st.reservation_id AND j.state = 'done' AND j.kind NOT IN ('cancel', 'read') AND j.finished_utc >= st.checked_in_utc),
               st.checked_in_by, u.login_name
          FROM gst.stay st LEFT JOIN plat.app_user u ON u.user_id = st.checked_in_by
         WHERE st.property_id = @p AND st.checked_in_date BETWEEN @from AND @to;
        """;

    private const string OutSql = """
        SELECT st.checked_out_utc, COALESCE(u.display_name, CAST(st.checked_out_by AS NVARCHAR(20))),
               (SELECT MAX(x.t) FROM (SELECT c.posted_utc AS t FROM bill.charge c WHERE c.folio_id = st.folio_id
                                      UNION ALL SELECT r.recorded_utc FROM pay.receipt r WHERE r.folio_id = st.folio_id AND r.recorded_utc IS NOT NULL) x
                 WHERE x.t <= st.checked_out_utc AND x.t >= DATEADD(MINUTE, -60, st.checked_out_utc)),
               st.checked_out_by, u.login_name
          FROM gst.stay st LEFT JOIN plat.app_user u ON u.user_id = st.checked_out_by
         WHERE st.property_id = @p AND st.status = 'CheckedOut' AND st.checked_out_date BETWEEN @from AND @to;
        """;

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await GuestsSql.ClockAsync(Platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 92, clock.BusinessDate);
        var user = Param(parameters, "deskUser");
        var ev = new List<Ev>();
        await using (var conn = await Platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            foreach (var inbound in new[] { true, false })
            {
                await using var cmd = new SqlCommand(inbound ? InSql : OutSql, conn);
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@from", SqlDbType.Date, GuestsSql.D(parameters.From!.Value));
                Add(cmd, "@to", SqlDbType.Date, GuestsSql.D(parameters.To!.Value));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    var uid = rd.IsDBNull(3) ? "" : Convert.ToString(rd.GetValue(3), CultureInfo.InvariantCulture)!;
                    var login = rd.IsDBNull(4) ? "" : rd.GetString(4);
                    if (user.Length > 0 && uid != user && !string.Equals(login, user, StringComparison.OrdinalIgnoreCase)) continue;
                    var at = rd.GetDateTime(0);
                    DateTime? other = rd.IsDBNull(2) ? null : rd.GetDateTime(2);
                    long? minutes = other is null ? null : (long)Math.Round(Math.Abs((at - other.Value).TotalMinutes), MidpointRounding.AwayFromZero);
                    ev.Add(new Ev(inbound, rd.GetString(1), at, minutes, inbound ? other : null));
                }
            }
        }

        // MEAS2-B (M1): red row = median above the check-in / check-out standard (meas.target.checkinMinutes 8 / checkoutMinutes 6).
        var inT = await TargetAsync(caller.PropertyId, "meas.target.checkinMinutes", 8, ct).ConfigureAwait(false);
        var outT = await TargetAsync(caller.PropertyId, "meas.target.checkoutMinutes", 6, ct).ConfigureAwait(false);
        var rows = ev.GroupBy(e => LocalOf(clock, e.At).ToString("HH", CultureInfo.InvariantCulture) + ":00\0" + e.User).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var parts = g.Key.Split('\0');
            var ins = g.Where(e => e.In).ToList();
            var iv = ins.Select(e => (Start: e.At, End: e.End ?? e.At)).ToList();
            var peak = iv.Count == 0 ? 0 : iv.Max(a => iv.Count(b => b.Start <= a.Start && (a.Start < b.End || b.Start == a.Start)));
            var toKey = MeasGuestRules.LowerMedian(ins.Where(e => e.Minutes is not null).Select(e => e.Minutes!.Value));
            var toOut = MeasGuestRules.LowerMedian(g.Where(e => !e.In && e.Minutes is not null).Select(e => e.Minutes!.Value));
            return Row(red: MeasGuestRules.DeskRed(toKey, toOut, inT, outT), cells: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["hour"] = parts[0], ["deskUser"] = parts[1], ["arrivals"] = (long)ins.Count,
                ["arrivalToKey"] = toKey,
                ["billToCheckout"] = toOut,
                ["queuePeak"] = (long)peak,
            });
        }).ToList();
        return Result(parameters, rows);
    }
}
