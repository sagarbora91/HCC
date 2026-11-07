// F12-RPT-PEOPLE · rpt.people.attrition — attrition and tenure (catalogue §3.10, switch meas.people.attrition). Server twin of meas-people/attrition.ts.
using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.MeasPeople.PeopleSql;

namespace Hcc.Reporting.Reports.MeasPeople;

public sealed class AttritionReport(SqlPlatform platform, ISettingsReader? settings = null) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.people.attrition";
    public const string Basis = "Joiners and leavers by joining / exit date inside the financial year (exit date = the recorded exit reason's date, else the staff record's). Tenure = months from joining to exit, averaged. 12-month attrition % = leavers in the 12 months ending at the report end / average of opening and closing headcount. Vacancy days = days from each leaver's exit to the next joiner of the department (or the report end). Red: attrition above the annual limit (target 30 %).";
    public string MeasKey => "meas.people.attrition";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("dept", "Department"), I("joiners", "Joiners"), I("leavers", "Leavers"), T("exitReasons", "Exit reasons"), T("tenure", "Tenure at exit"), P("attritionPct", "12-month attrition %"),
        I("vacancyDays", "Vacancy days"), ExceptionCol,
    ];

    public ReportDefinition Definition { get; } = new(Id, "Attrition and tenure", "people", "P2", "MEAS", false, null, ["PERM-HR-001"], [], "PERM-RPT-003", ReportDateClass.FinancialYear,
        [new("fy", "Financial year", "fy", ReportDateClass.FinancialYear), new("dept", "Department", "text")], Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No);

    private sealed record Person(string Id, string Dept, DateOnly Joined, DateOnly? Exit, string? Reason);

    private static double MonthsBetween(DateOnly a, DateOnly b) => Math.Max(0, Math.Round((b.DayNumber - a.DayNumber) / 30.4375 * 10, MidpointRounding.AwayFromZero) / 10);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var from = parameters.From!.Value;
        var to = parameters.To!.Value;
        var w0 = to.AddDays(-364);
        var limit = await TargetAsync(settings, caller.PropertyId, "meas.target.attrition", ct).ConfigureAwait(false);
        var depts = new List<(string Code, string Name)>();
        var people = new List<Person>();
        var anyReason = false;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand("SELECT dept_code, name FROM hr.department WHERE property_id = @p ORDER BY dept_code;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) depts.Add((rd.GetString(0), rd.GetString(1)));
            }

            await using (var cmd = new SqlCommand("""
                SELECT s.staff_id, s.dept_code, s.joined, COALESCE(x.exit_date, s.exited_on), x.reason
                  FROM hr.staff s LEFT JOIN meas.staff_exit x ON x.property_id = s.property_id AND x.staff_id = s.staff_id WHERE s.property_id = @p;
                """, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    people.Add(new Person(Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture)!, rd.GetString(1), DateOnly.FromDateTime(rd.GetDateTime(2)),
                        rd.IsDBNull(3) ? null : DateOnly.FromDateTime(rd.GetDateTime(3)), rd.IsDBNull(4) ? null : rd.GetString(4)));
            }

            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM meas.staff_exit WHERE property_id = @p;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                anyReason = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
            }
        }

        var filter = parameters.Values.GetValueOrDefault("dept");
        var rows = new List<ReportRow>();
        foreach (var d in depts.Where(d => PeopleRules.DeptMatches(d.Code, d.Name, filter)))
        {
            var staff = people.Where(p => p.Dept == d.Code).ToList();
            if (staff.Count == 0) continue;
            var joined = staff.Count(p => p.Joined >= from && p.Joined <= to);
            var left = staff.Where(p => p.Exit is { } e && e >= from && e <= to).ToList();
            var left12 = staff.Count(p => p.Exit is { } e && e >= w0 && e <= to);
            var reasons = left.GroupBy(p => p.Reason ?? "Not recorded").OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} x{g.Count()}");
            var tenure = left.Count > 0 ? (left.Average(p => MonthsBetween(p.Joined, p.Exit!.Value))).ToString("0.0", CultureInfo.InvariantCulture) + " months" : "";
            long vac = 0;
            foreach (var p in left)
            {
                var e = p.Exit!.Value;
                var next = staff.Where(j => j.Joined > e).Select(j => j.Joined).Order().Cast<DateOnly?>().FirstOrDefault();
                var end = next is { } n && n <= to ? n : to;
                vac += Math.Max(0, end.DayNumber - e.DayNumber);
            }

            long Head(DateOnly day) => staff.Count(p => p.Joined <= day && (p.Exit is null || p.Exit > day));
            long open = Head(w0), close = Head(to);
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["dept"] = d.Name, ["joiners"] = (long)joined, ["leavers"] = (long)left.Count, ["exitReasons"] = string.Join("; ", reasons), ["tenure"] = tenure,
                ["attritionPct"] = PeopleRules.Pct1(left12 * 2L, open + close), ["vacancyDays"] = vac, ["exception"] = PeopleRules.AttritionRed(left12, open, close, limit),
            }));
        }

        var basis = anyReason ? Basis : $"{Basis} Capture needed: exit reason (staff.exitReason list, meas-cap-people). No exit reason is recorded yet; leavers show as \"Not recorded\".";
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, basis));
    }
}
