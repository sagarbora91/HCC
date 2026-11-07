// F12-RPT-PEOPLE · rpt.people.staff-pulse — anonymous staff pulse by department and quarter (switch meas.people.pulse). Owner and Manager only (PERM-HR-002).
// Department only: the query never reads a staff or user column (meas.pulse has none). Fewer than 5 answers in a department-quarter are suppressed.
using System.Data;
using System.Globalization;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.MeasPeople.PeopleSql;

namespace Hcc.Reporting.Reports.MeasPeople;

public sealed class StaffPulseReport(SqlPlatform platform, IAccessPolicy policy) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.people.staff-pulse";
    public const string Basis = "Anonymous pulse answers (four 1-5 scores and a 0-10 recommend score) by financial-year quarter and department; no staff, user or name is stored or shown. A department with fewer than 5 answers in a quarter is suppressed. Scores are averages; eNPS = promoters (9-10) minus detractors (0-6) in whole percent. Red: eNPS below 0.";
    public string MeasKey => "meas.people.pulse";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("quarter", "Quarter"), T("dept", "Department"), I("responses", "Responses"), T("workload", "Workload"), T("respect", "Respect"), T("tools", "Tools"), T("payFairness", "Pay fairness"),
        T("recommend", "Would recommend"), I("enps", "eNPS"), ExceptionCol,
    ];

    public ReportDefinition Definition { get; } = new(Id, "Staff pulse (optional)", "people", "P3", "MEAS", false, null, ["PERM-HR-002"], [], "PERM-RPT-003", ReportDateClass.FinancialYear,
        [new("fy", "Financial year", "fy", ReportDateClass.FinancialYear), new("quarter", "Quarter", "enum", null, ["Q1", "Q2", "Q3", "Q4"])], Columns, Basis, [ReportFormat.Screen, ReportFormat.Pdf], ReportPhone.Yes);

    private static string Avg(IEnumerable<int> v) { var l = v.ToList(); return (Math.Round(l.Sum() * 10.0 / l.Count, MidpointRounding.AwayFromZero) / 10).ToString("0.0", CultureInfo.InvariantCulture); }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        await OwnerManagerOnlyAsync(policy, caller, "The staff pulse", ct).ConfigureAwait(false);
        var fy = parameters.Values["fy"];
        var quarter = parameters.Values.GetValueOrDefault("quarter");
        var answers = new List<(string Quarter, string Dept, string DeptName, int Workload, int Respect, int Tools, int Pay, int Rec)>();
        long total;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM meas.pulse WHERE property_id = @p;", conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                total = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            await using (var cmd = new SqlCommand("""
                SELECT u.quarter_key, u.dept_code, d.name, u.workload, u.respect, u.tools, u.pay_fairness, u.recommend
                  FROM meas.pulse u JOIN hr.department d ON d.property_id = u.property_id AND d.dept_code = u.dept_code
                 WHERE u.property_id = @p AND u.quarter_key LIKE @fy + '-%' AND (@q = '' OR u.quarter_key LIKE '%-' + @q);
                """, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@fy", SqlDbType.VarChar, fy);
                Add(cmd, "@q", SqlDbType.VarChar, quarter ?? "");
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    answers.Add((rd.GetString(0).Trim(), rd.GetString(1), rd.GetString(2), rd.GetByte(3), rd.GetByte(4), rd.GetByte(5), rd.GetByte(6), rd.GetByte(7)));
            }
        }

        var rows = new List<ReportRow>();
        var suppressed = 0;
        foreach (var g in answers.GroupBy(a => (a.Quarter, a.Dept)).OrderBy(g => g.Key.Quarter + "|" + g.Key.Dept, StringComparer.Ordinal))
        {
            var v = g.ToList();
            if (v.Count < PeopleRules.PulseMinAnswers) { suppressed++; continue; }
            var e = PeopleRules.Enps(v.Select(x => x.Rec).ToList());
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["quarter"] = g.Key.Quarter, ["dept"] = v[0].DeptName, ["responses"] = (long)v.Count, ["workload"] = Avg(v.Select(x => x.Workload)), ["respect"] = Avg(v.Select(x => x.Respect)), ["tools"] = Avg(v.Select(x => x.Tools)),
                ["payFairness"] = Avg(v.Select(x => x.Pay)), ["recommend"] = Avg(v.Select(x => x.Rec)), ["enps"] = (long)e, ["exception"] = e < 0,
            }));
        }

        var note = total == 0 ? " Capture needed: hr.pulse (meas-cap-people). No pulse answer is captured yet."
            : suppressed > 0 ? $" {suppressed} department-quarter{(suppressed == 1 ? "" : "s")} suppressed (fewer than {PeopleRules.PulseMinAnswers} answers)." : "";
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, Basis + note));
    }
}
