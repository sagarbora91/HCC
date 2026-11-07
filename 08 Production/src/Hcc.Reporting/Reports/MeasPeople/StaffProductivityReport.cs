// F12-RPT-PEOPLE · rpt.people.staff-productivity — staff per occupied room, overtime, absenteeism (catalogue §3.10, switch meas.people.productivity).
// Server twin of meas-people/staff-productivity.ts. Hours and days only: the query never reads a pay column (hr.staff monthly / daily are not selected).
// Server simplification (MEAS-RPT-PEOPLE-DONE.md): scheduled days = days with a mark (or an unmarked rostered working day up to the report end); approved leave and paid holidays are not netted.
using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.MeasPeople.PeopleSql;

namespace Hcc.Reporting.Reports.MeasPeople;

public sealed class StaffProductivityReport(SqlPlatform platform, ISettingsReader? settings = null) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.people.staff-productivity";
    public const string Basis = "Rostered hours = shift length on every rostered day outside weekly offs and full-day leave. OT % = overtime minutes / rostered minutes. Absenteeism % = absent days (half-day = 1/2) / scheduled days. Occupied room-night = a stay that slept in a room that night. Low-forecast night = occupancy below the occupancy target; the need on those nights = rooms sold x the month's staff-hours per occupied room-night. Red: OT above the OT target, absenteeism above the absenteeism target, or rostered hours on low nights above that need by 20 %. Hours and days only: no pay amount.";
    public string MeasKey => "meas.people.productivity";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("dept", "Department"), I("headcount", "Headcount"), T("hoursPerRoomNight", "Staff-hours per occupied room-night"), P("otPct", "OT %"), P("absentPct", "Absenteeism %"),
        I("lateMarks", "Late marks"), T("lowNightHours", "Rostered hours on low-forecast nights"), ExceptionCol,
    ];

    public ReportDefinition Definition { get; } = new(Id, "Staff per occupied room, overtime, absenteeism", "people", "P2", "MEAS", false, null, ["PERM-HR-001"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month), new("dept", "Department", "text")], Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private sealed record Person(long Id, string Dept, string DeptName, string DefaultShift, int WeeklyOff, DateOnly Joined, DateOnly? Exited);

    private sealed class Acc
    {
        public long Head, Late, RosterMin, OtMin, Sched, AbsentHalves, LowMin;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var otTarget = await TargetAsync(settings, caller.PropertyId, "meas.target.ot", ct).ConfigureAwait(false);
        var absTarget = await TargetAsync(settings, caller.PropertyId, "meas.target.absenteeism", ct).ConfigureAwait(false);
        var occTarget = await TargetAsync(settings, caller.PropertyId, "meas.target.occupancy", ct).ConfigureAwait(false);
        var people = new List<Person>();
        var marks = new Dictionary<(long, DateOnly), (string Status, int Ot)>();
        var roster = new Dictionary<(long, DateOnly), string>();
        var stays = new List<(DateOnly Arrival, DateOnly Departure)>();
        long sellable;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand("""
                SELECT s.staff_id, s.dept_code, d.name, s.default_shift, s.weekly_off, s.joined, s.exited_on
                  FROM hr.staff s JOIN hr.department d ON d.property_id = s.property_id AND d.dept_code = s.dept_code
                 WHERE s.property_id = @p AND s.joined <= @to AND (s.exited_on IS NULL OR s.exited_on >= @from) ORDER BY d.dept_code, s.staff_id;
                """, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@from", SqlDbType.Date, D(from));
                Add(cmd, "@to", SqlDbType.Date, D(to));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    people.Add(new Person(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3).Trim(), rd.GetByte(4), DateOnly.FromDateTime(rd.GetDateTime(5)), rd.IsDBNull(6) ? null : DateOnly.FromDateTime(rd.GetDateTime(6))));
            }

            await using (var cmd = new SqlCommand("SELECT a.staff_id, a.work_date, a.status, a.ot_minutes FROM hr.attendance a JOIN hr.staff s ON s.staff_id = a.staff_id WHERE s.property_id = @p AND a.work_date BETWEEN @from AND @to;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@from", SqlDbType.Date, D(from));
                Add(cmd, "@to", SqlDbType.Date, D(to));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) marks[(rd.GetInt64(0), DateOnly.FromDateTime(rd.GetDateTime(1)))] = (rd.GetString(2).Trim(), rd.GetInt16(3));
            }

            await using (var cmd = new SqlCommand("SELECT r.staff_id, r.work_date, r.shift_code FROM hr.roster r JOIN hr.staff s ON s.staff_id = r.staff_id WHERE s.property_id = @p AND r.work_date BETWEEN @from AND @to;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@from", SqlDbType.Date, D(from));
                Add(cmd, "@to", SqlDbType.Date, D(to));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) roster[(rd.GetInt64(0), DateOnly.FromDateTime(rd.GetDateTime(1)))] = rd.GetString(2).Trim();
            }

            await using (var cmd = new SqlCommand("SELECT arrival, departure FROM gst.stay WHERE property_id = @p AND arrival <= @to AND departure > @from;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@from", SqlDbType.Date, D(from));
                Add(cmd, "@to", SqlDbType.Date, D(to));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) stays.Add((DateOnly.FromDateTime(rd.GetDateTime(0)), DateOnly.FromDateTime(rd.GetDateTime(1))));
            }

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM inv.room WHERE property_id = @p;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                sellable = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
        }

        var dates = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) dates.Add(d);
        var occ = dates.ToDictionary(d => d, d => (long)stays.Count(s => s.Arrival <= d && s.Departure > d));
        var occRoomNights = occ.Values.Sum();
        var low = dates.Where(d => sellable > 0 && occ[d] * 100 < occTarget * sellable).ToHashSet();
        var lowRooms = low.Sum(d => occ[d]);

        var perDept = new SortedDictionary<string, (string Name, Acc A)>(StringComparer.Ordinal);
        foreach (var p in people)
        {
            if (!perDept.TryGetValue(p.Dept, out var e)) perDept[p.Dept] = e = (p.DeptName, new Acc());
            var a = e.A;
            a.Head++;
            foreach (var d in dates)
            {
                if (d < p.Joined || (p.Exited is { } ex && d > ex)) continue;
                var code = roster.TryGetValue((p.Id, d), out var o) ? o : (int)d.DayOfWeek == p.WeeklyOff ? "OFF" : p.DefaultShift;
                var has = marks.TryGetValue((p.Id, d), out var mk);
                if (has)
                {
                    a.OtMin += mk.Ot;
                    if (mk.Status == "L") a.Late++;
                    if (mk.Status is "P" or "L" or "H" or "A") a.Sched++;
                    if (mk.Status == "A") a.AbsentHalves += 2;
                    if (mk.Status == "H") a.AbsentHalves += 1;
                }
                else if (code != "OFF") a.Sched++;
                if (code == "OFF") continue;
                var m = PeopleRules.ShiftMinutesOf(code);
                a.RosterMin += m;
                if (low.Contains(d)) a.LowMin += m;
            }
        }

        var filter = parameters.Values.GetValueOrDefault("dept");
        var rows = new List<ReportRow>();
        var tot = new Acc();
        foreach (var (code, (name, a)) in perDept)
        {
            if (!PeopleRules.DeptMatches(code, name, filter)) continue;
            rows.Add(Line(name, a, occRoomNights, lowRooms, otTarget, absTarget, ReportRowKind.Row));
            tot.Head += a.Head; tot.Late += a.Late; tot.RosterMin += a.RosterMin; tot.OtMin += a.OtMin; tot.Sched += a.Sched; tot.AbsentHalves += a.AbsentHalves; tot.LowMin += a.LowMin;
        }

        if (rows.Count > 0) rows.Add(Line("All departments", tot, occRoomNights, lowRooms, otTarget, absTarget, ReportRowKind.Total));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, Basis));
    }

    private static ReportRow Line(string name, Acc a, long occRoomNights, long lowRooms, long otTarget, long absTarget, ReportRowKind kind)
    {
        var r = PeopleRules.ProductivityRule(new ProdFigures(a.RosterMin, a.OtMin, a.Sched, a.AbsentHalves, occRoomNights, a.LowMin, lowRooms), otTarget, absTarget);
        return Row(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["dept"] = name, ["headcount"] = a.Head, ["hoursPerRoomNight"] = occRoomNights > 0 ? (a.RosterMin / 60.0 / occRoomNights).ToString("0.00", CultureInfo.InvariantCulture) : "0.00",
            ["otPct"] = PeopleRules.Pct1(a.OtMin, a.RosterMin), ["absentPct"] = PeopleRules.Pct1(a.AbsentHalves, 2 * a.Sched), ["lateMarks"] = a.Late, ["lowNightHours"] = PeopleRules.HoursText(a.LowMin),
            ["exception"] = r.Ot || r.Absent || r.OverRostered,
        }, kind);
    }
}
