// F12-RPT-PEOPLE · rpt.people.training — training hours and certifications (catalogue §3.10, switch meas.people.training). Server twin of meas-people/training.ts.
using System.Data;
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.MeasPeople.PeopleSql;

namespace Hcc.Reporting.Reports.MeasPeople;

public sealed class TrainingReport(SqlPlatform platform) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.people.training";
    public const string Basis = "Training records up to the as-of date. Days to expiry = valid-to minus the as-of date (negative = expired). Hours per head this quarter = that person's training hours in the financial-year quarter of the as-of date. Mandatory certificates: fire safety, first aid, POSH, and FSSAI handler for the Kitchen. Red: a mandatory certificate expired (and not renewed by a later valid record) or missing altogether (listed as \"(missing)\").";
    public string MeasKey => "meas.people.training";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("staff", "Staff"), T("course", "Course"), D("date", "Date"), T("hours", "Hours"), T("certificateNo", "Certificate no"), D("validTo", "Valid to"), I("daysToExpiry", "Days to expiry"),
        T("quarterHours", "Hours per head this quarter"), ExceptionCol,
    ];

    public ReportDefinition Definition { get; } = new(Id, "Training hours and certifications", "people", "P2", "MEAS", false, null, ["PERM-HR-001"], [], "PERM-RPT-003", ReportDateClass.AsOf,
        [new("asOf", "As of", "date", ReportDateClass.AsOf), new("dept", "Department", "text")], Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var asOf = parameters.To!.Value;
        var dept = parameters.Values.GetValueOrDefault("dept");
        var staff = new Dictionary<string, (string Name, string Dept, string DeptName, bool Active, DateOnly Joined, DateOnly? Exited)>(StringComparer.Ordinal);
        var list = new List<TrainingRec>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand("""
                SELECT s.staff_id, s.name, s.dept_code, d.name, s.active, s.joined, s.exited_on
                  FROM hr.staff s JOIN hr.department d ON d.property_id = s.property_id AND d.dept_code = s.dept_code WHERE s.property_id = @p;
                """, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    staff[Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture)!] = (rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetBoolean(4), DateOnly.FromDateTime(rd.GetDateTime(5)), rd.IsDBNull(6) ? null : DateOnly.FromDateTime(rd.GetDateTime(6)));
            }

            await using (var cmd = new SqlCommand("""
                SELECT training_id, staff_id, course, trained_on, hours, certificate_no, valid_to, mandatory FROM meas.training WHERE property_id = @p AND trained_on <= @asOf ORDER BY trained_on, training_id;
                """, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@asOf", SqlDbType.Date, D(asOf));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    list.Add(new TrainingRec(Convert.ToString(rd.GetValue(0), CultureInfo.InvariantCulture)!, Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture)!, rd.GetString(2),
                        DateOnly.FromDateTime(rd.GetDateTime(3)), (double)rd.GetDecimal(4), rd.IsDBNull(5) ? null : rd.GetString(5), rd.IsDBNull(6) ? null : DateOnly.FromDateTime(rd.GetDateTime(6)), rd.GetBoolean(7)));
            }
        }

        var rows = new List<ReportRow>();
        if (list.Count > 0)
        {
            var q = QuarterKey(asOf);
            var qHours = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var t in list.Where(t => QuarterKey(t.Date) == q)) qHours[t.Staff] = qHours.GetValueOrDefault(t.Staff) + (long)Math.Round(t.Hours * 100);
            string QH(string id) => (qHours.GetValueOrDefault(id) / 100.0).ToString("0.0", CultureInfo.InvariantCulture);
            foreach (var t in list)
            {
                if (!staff.TryGetValue(t.Staff, out var st) || !PeopleRules.DeptMatches(st.Dept, st.DeptName, dept)) continue;
                var mand = t.Mandatory || PeopleRules.MandatoryKey(t.Course) is not null;
                rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["staff"] = st.Name, ["course"] = t.Course, ["date"] = t.Date, ["hours"] = t.Hours.ToString("0.0", CultureInfo.InvariantCulture), ["certificateNo"] = t.CertificateNo ?? "", ["validTo"] = t.ValidTo,
                    ["daysToExpiry"] = t.ValidTo is { } v ? (long)(v.DayNumber - asOf.DayNumber) : null, ["quarterHours"] = QH(t.Staff), ["exception"] = mand && PeopleRules.CertificateExpired(list, t, asOf),
                }));
            }

            foreach (var (id, st) in staff.Where(kv => kv.Value.Active && kv.Value.Exited is null && kv.Value.Joined <= asOf && PeopleRules.DeptMatches(kv.Value.Dept, kv.Value.DeptName, dept)))
                foreach (var m in PeopleRules.MandatoryCourses)
                {
                    if (m.Dept is not null && !string.Equals(m.Dept, st.Dept, StringComparison.Ordinal)) continue;
                    if (list.Any(t => t.Staff == id && t.Course.Contains(m.Match, StringComparison.OrdinalIgnoreCase))) continue;
                    rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["staff"] = st.Name, ["course"] = m.Label + " (missing)", ["date"] = null, ["hours"] = "0.0", ["certificateNo"] = "", ["validTo"] = null, ["daysToExpiry"] = null, ["quarterHours"] = QH(id), ["exception"] = true,
                    }));
                }
        }

        var basis = list.Count == 0 ? $"{Basis} Capture needed: hr.training (meas-cap-people). No training record is captured yet." : Basis;
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, basis));
    }
}
