// F12 MEAS · rpt.rooms.hk-productivity — Attendant productivity and turnaround (switch meas.hk.productivity). Server twin of meas-hk/hk-productivity.ts.
// Red row: pass rate under 95 %; median departure room over 45 minutes; a same-day re-let not inspected before arrival.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;
using static Hcc.Reporting.Reports.MeasHk.MeasHkRules;

namespace Hcc.Reporting.Reports.MeasHk;

public sealed class HkProductivityReport(SqlPlatform platform) : MeasHkReportBase(platform)
{
    public const string Id = "rpt.rooms.hk-productivity";
    public static readonly TimeOnly ReletCheckIn = new(12, 0); // property check-in time when the stay has no recorded instant

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("attendant", "Attendant"), T("shift", "Shift"), I("departures", "Departure rooms"), I("stayovers", "Stayover rooms"), I("minutesPerRoom", "Minutes per room"),
        I("vacatedToInspected", "Vacated-to-inspected minutes"), P("passPct", "Inspection pass %"), T("rejections", "Rejections by reason"), T("relet", "Same-day re-let compliance"),
    ];

    private const string Basis = "Housekeeping tasks by the business date they were made for (range up to 31 days). Shift by the hour the task started (Morning 06:00, Afternoon 14:00, Night 22:00). Minutes per room = start to finish. Vacated-to-inspected = task created at departure to inspection. Re-let = a guest arriving the same day in the cleaned room; compliant when passed before arrival. Red: pass rate under 95 %, median departure room over 45 minutes, or a re-let not inspected before arrival.";

    public override string MeasKey => "meas.hk.productivity";

    public override ReportDefinition Definition { get; } = new(Id, "Attendant productivity and turnaround", "rooms", "P1", "MEAS", false, null, ["PERM-HK-001"], [], "PERM-RPT-003", ReportDateClass.Short,
        [new("from", "From", "date", ReportDateClass.Short), new("to", "To", "date", ReportDateClass.Short), new("attendant", "Attendant", "user"), new("shift", "Shift", "enum", null, ["Morning", "Afternoon", "Night"])],
        Columns, Basis, Std, ReportPhone.Yes);

    private sealed record Task(string Attendant, string Name, string Kind, DateOnly Date, string Room, DateTime? Created, DateTime? Started, DateTime? Finished, DateTime? Inspected, string Result, string Reject, string Shift);

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var clock = await ClockAsync(caller, ct).ConfigureAwait(false);
        GuestsSql.RangeGuard(parameters.From, parameters.To, 31, clock.BusinessDate);
        var captured = await QueryAsync("SELECT COUNT(*) FROM meas.task_times WHERE property_id = @p;", caller.PropertyId, null, ct).ConfigureAwait(false);
        if (L(captured[0][0]) == 0) return Done(parameters, Columns, [], CaptureNote(Basis, "task start, finish and inspection times (meas-cap-hk)"));
        var data = await QueryAsync("""
            SELECT CAST(t.assignee_id AS NVARCHAR(20)), COALESCE(u.display_name, CAST(t.assignee_id AS NVARCHAR(20)), N''), t.kind, t.business_date, rm.room_no, t.created_utc,
                   COALESCE(m.started_utc, t.started_utc), COALESCE(m.finished_utc, t.completed_utc), COALESCE(m.inspected_utc, t.inspected_utc), m.inspect_result, COALESCE(m.reject_reason, t.reject_reason)
              FROM ops.hk_task t JOIN inv.room rm ON rm.room_id = t.room_id
              LEFT JOIN meas.task_times m ON m.property_id = t.property_id AND m.task_no = CAST(t.task_id AS VARCHAR(60))
              LEFT JOIN plat.app_user u ON u.user_id = t.assignee_id
             WHERE t.property_id = @p AND t.kind IN ('checkout','stayover') AND t.business_date BETWEEN @from AND @to AND (@by = '' OR CAST(t.assignee_id AS NVARCHAR(20)) = @by);
            """, caller.PropertyId, c => { BindRange(c, parameters); GuestsSql.Add(c, "@by", SqlDbType.NVarChar, P(parameters, "attendant")); }, ct).ConfigureAwait(false);
        var arrivals = await QueryAsync("""
            SELECT rm.room_no, r.arrival FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id = n.reservation_id JOIN inv.room rm ON rm.room_id = n.room_id
             WHERE r.property_id = @p AND n.night = r.arrival AND r.arrival BETWEEN @from AND @to AND r.status IN ('Confirmed','InHouse','Departed');
            """, caller.PropertyId, c => BindRange(c, parameters), ct).ConfigureAwait(false);
        var arriving = arrivals.Select(a => (Room: S(a[0]), Date: Dt(a[1]))).ToHashSet();
        var shiftWanted = P(parameters, "shift");
        var tasks = data.Select(r =>
        {
            var started = Utc(r[6]);
            DateTime? local = started is { } s ? TimeZoneInfo.ConvertTimeFromUtc(s, clock.Zone) : null;
            return new Task(S(r[0]), S(r[1]), S(r[2]), Dt(r[3]), S(r[4]), Utc(r[5]), started, Utc(r[7]), Utc(r[8]), S(r[9]), S(r[10]), ShiftOf(local?.Hour));
        }).Where(t => shiftWanted == "" || t.Shift == shiftWanted).ToList();

        var order = new[] { "Morning", "Afternoon", "Night" };
        var rows = new List<ReportRow>();
        foreach (var g in tasks.GroupBy(t => (t.Attendant, t.Shift)).OrderBy(g => g.First().Name, StringComparer.Ordinal).ThenBy(g => Array.IndexOf(order, g.Key.Shift)))
        {
            var all = g.ToList();
            var dep = all.Where(t => t.Kind == "checkout").ToList();
            var stay = all.Where(t => t.Kind == "stayover").ToList();
            var mins = all.Select(Mins).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            var depMins = dep.Select(Mins).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            var vac = dep.Where(t => t.Created is not null && t.Inspected is not null).Select(t => Math.Round((t.Inspected!.Value - t.Created!.Value).TotalMinutes)).Where(x => x >= 0).ToList();
            var inspected = all.Where(t => t.Result != "").ToList();
            var pass = inspected.Count(t => t.Result == "pass");
            var reasons = inspected.Where(t => t.Result == "fail").GroupBy(t => t.Reject == "" ? "No reason" : t.Reject).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal);
            int relets = 0, ok = 0;
            foreach (var t in dep)
            {
                if (!arriving.Contains((t.Room, t.Date))) continue;
                relets++;
                var arrivalUtc = TimeZoneInfo.ConvertTimeToUtc(t.Date.ToDateTime(ReletCheckIn), clock.Zone);
                if (t.Result == "pass" && t.Inspected is { } i && i <= arrivalUtc) ok++;
            }

            var mean = Mean(mins);
            var vm = Median(vac);
            var red = PassRateRed(pass, inspected.Count) || MedianDepartureRed(Median(depMins)) || ok < relets;
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["attendant"] = all[0].Name, ["shift"] = g.Key.Shift, ["departures"] = (long)dep.Count, ["stayovers"] = (long)stay.Count,
                ["minutesPerRoom"] = mean is null ? null : (long)Math.Round(mean.Value, MidpointRounding.AwayFromZero),
                ["vacatedToInspected"] = vm is null ? null : (long)Math.Round(vm.Value, MidpointRounding.AwayFromZero),
                ["passPct"] = inspected.Count > 0 ? R1(pass * 100.0 / inspected.Count) : null,
                ["rejections"] = string.Join("; ", reasons.Select(x => $"{x.Key} ×{x.Count()}")), ["relet"] = relets > 0 ? $"{ok} of {relets}" : "-",
            }, red));
        }

        return Done(parameters, Columns, rows);
    }

    private static double? Mins(Task t) => t.Started is { } a && t.Finished is { } b && b >= a ? Math.Round((b - a).TotalMinutes) : null;
}
