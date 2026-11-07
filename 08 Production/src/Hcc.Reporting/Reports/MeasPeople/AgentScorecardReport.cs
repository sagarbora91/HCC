// F12-RPT-PEOPLE · rpt.people.agent-scorecard — performance by desk user (catalogue §3.10, switch meas.people.scorecard). Owner and Manager only. Server twin of agent-scorecard.ts.
// Sources: gst.stay (check-in / check-out user), gst.stay_rate_change, bill.approval (approved discount requests), pay.shift (cash counted by), ops.guest_complaint (owner),
// meas.turnaway (regrets), meas.survey (desk entries). Upsell captures and comps have no actor column on the server yet: 0 (see MEAS-RPT-PEOPLE-DONE.md).
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

#pragma warning disable CS0649
public sealed class AgentScorecardReport(SqlPlatform platform, IAccessPolicy policy) : IReportSelector, IMeasuredReport
{
    public const string Id = "rpt.people.agent-scorecard";
    public const string Basis = "Per signed-in user for the month. Check-ins / check-outs / upsell captures from the command audit log; rate edits from the stay rate-change log; discounts from approved discount requests by the requester (reason % = requests with a reason); comps from approved comp requests (DEC-266 not built yet: 0 until it is); complaints owned; cash variances = absolute cash difference of the day closings the user counted; regrets = turn-aways logged; survey score = average overall score of desk-entered surveys. Upsell per arrival = upsell captures / check-ins. Red: discounts (Rs) or cash variances (Rs) above the team's upper quartile (needs at least 4 users with activity). Owner and Manager only.";
    public string MeasKey => "meas.people.scorecard";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("user", "User"), I("checkins", "Check-ins"), I("checkouts", "Check-outs"), I("rateEdits", "Rate edits"), I("discountCount", "Discounts (count)"), M("discountAmount", "Discounts (Rs)"),
        P("discountReasonPct", "Discounts with a reason (%)"), I("comps", "Comps requested"), T("upsellPerArrival", "Upsell per arrival"), I("complaints", "Complaints attributed"),
        M("cashVariance", "Cash variances (Rs)"), I("regrets", "Regrets logged"), T("surveyScore", "Survey score"), ExceptionCol,
    ];

    public ReportDefinition Definition { get; } = new(Id, "Performance by desk user", "people", "P2", "MEAS", true, "PERM-RPT-002", ["PERM-RPT-002"], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month), new("user", "Desk user", "user")], Columns, Basis, [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.No);

    private sealed class Figures
    {
        public long Checkins, Checkouts, RateEdits, DiscountCount, DiscountAmount, WithReason, Comps, Upsells, Complaints, CashVariance, Regrets, SurveyTotal, SurveyCount;

        public bool Active => Checkins + Checkouts + RateEdits + DiscountCount + DiscountAmount + WithReason + Comps + Upsells + Complaints + CashVariance + Regrets + SurveyTotal + SurveyCount > 0;
    }

    private const string Sql = """
        SELECT 'in', checked_in_by, 1, 0 FROM gst.stay WHERE property_id = @p AND checked_in_date BETWEEN @from AND @to
        UNION ALL SELECT 'out', checked_out_by, 1, 0 FROM gst.stay WHERE property_id = @p AND checked_out_by IS NOT NULL AND checked_out_date BETWEEN @from AND @to
        UNION ALL SELECT 'rate', changed_by, 1, 0 FROM gst.stay_rate_change WHERE property_id = @p AND business_date BETWEEN @from AND @to
        UNION ALL SELECT 'disc', requested_by, CASE WHEN LEN(LTRIM(RTRIM(reason))) > 0 THEN 1 ELSE 0 END, amount_paise FROM bill.approval
                   WHERE property_id = @p AND action = 'discount' AND status IN ('Approved', 'Executed') AND CAST(decided_utc AS DATE) BETWEEN @from AND @to
        UNION ALL SELECT 'cash', submitted_by, 1, ABS(counted_paise - expected_paise) FROM pay.shift sh JOIN pay.till t ON t.till_id = sh.till_id
                   WHERE t.property_id = @p AND sh.status = 'Closed' AND sh.submitted_by IS NOT NULL AND sh.business_date BETWEEN @from AND @to
        UNION ALL SELECT 'cmp', owner_id, 1, 0 FROM ops.guest_complaint WHERE property_id = @p AND CAST(created_utc AS DATE) BETWEEN @from AND @to
        UNION ALL SELECT 'reg', recorded_by, 1, 0 FROM meas.turnaway WHERE property_id = @p AND business_date BETWEEN @from AND @to
        UNION ALL SELECT 'sur', entered_by, 1, overall FROM meas.survey WHERE property_id = @p AND via = 'desk' AND entered_by IS NOT NULL AND CAST(at_utc AS DATE) BETWEEN @from AND @to;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        await OwnerManagerOnlyAsync(policy, caller, "The performance scorecard", ct).ConfigureAwait(false);
        var by = new Dictionary<int, Figures>();
        var names = new Dictionary<int, (string Login, string Name)>();
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using (var cmd = new SqlCommand(Sql, conn))
            {
                Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
                Add(cmd, "@from", SqlDbType.Date, D(parameters.From!.Value));
                Add(cmd, "@to", SqlDbType.Date, D(parameters.To!.Value));
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (rd.IsDBNull(1)) continue;
                    var uid = Convert.ToInt32(rd.GetValue(1), CultureInfo.InvariantCulture);
                    if (!by.TryGetValue(uid, out var f)) by[uid] = f = new Figures();
                    long flag = Convert.ToInt64(rd.GetValue(2), CultureInfo.InvariantCulture), amt = Convert.ToInt64(rd.GetValue(3), CultureInfo.InvariantCulture);
                    switch (rd.GetString(0))
                    {
                        case "in": f.Checkins++; break;
                        case "out": f.Checkouts++; break;
                        case "rate": f.RateEdits++; break;
                        case "disc": f.DiscountCount++; f.DiscountAmount += amt; f.WithReason += flag; break;
                        case "cash": f.CashVariance += amt; break;
                        case "cmp": f.Complaints++; break;
                        case "reg": f.Regrets++; break;
                        case "sur": f.SurveyTotal += amt; f.SurveyCount++; break;
                    }
                }
            }

            await using (var cmd = new SqlCommand("SELECT user_id, login_name, display_name FROM plat.app_user;", conn))
            {
                await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rd.ReadAsync(ct).ConfigureAwait(false)) names[rd.GetInt32(0)] = (rd.GetString(1), rd.GetString(2));
            }
        }

        var userFilter = parameters.Values.GetValueOrDefault("user");
        var team = by.Values.Where(f => f.Active).ToList();
        long[] Col(Func<Figures, long> g) => team.Select(g).ToArray();
        var rows = new List<ReportRow>();
        foreach (var (uid, f) in by.OrderBy(kv => kv.Key))
        {
            if (!f.Active) continue;
            names.TryGetValue(uid, out var nm);
            if (!string.IsNullOrEmpty(userFilter) && !string.Equals(userFilter, uid.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) && !string.Equals(userFilter, nm.Login, StringComparison.OrdinalIgnoreCase)) continue;
            var red = PeopleRules.AboveUpperQuartile(Col(x => x.DiscountAmount), f.DiscountAmount) || PeopleRules.AboveUpperQuartile(Col(x => x.CashVariance), f.CashVariance);
            rows.Add(Row(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["user"] = nm.Name ?? uid.ToString(CultureInfo.InvariantCulture), ["checkins"] = f.Checkins, ["checkouts"] = f.Checkouts, ["rateEdits"] = f.RateEdits, ["discountCount"] = f.DiscountCount,
                ["discountAmount"] = f.DiscountAmount, ["discountReasonPct"] = PeopleRules.Pct1(f.WithReason, f.DiscountCount), ["comps"] = f.Comps,
                ["upsellPerArrival"] = f.Checkins > 0 ? (f.Upsells / (double)f.Checkins).ToString("0.00", CultureInfo.InvariantCulture) : "0.00", ["complaints"] = f.Complaints, ["cashVariance"] = f.CashVariance,
                ["regrets"] = f.Regrets, ["surveyScore"] = f.SurveyCount > 0 ? (Math.Round(f.SurveyTotal * 10.0 / f.SurveyCount, MidpointRounding.AwayFromZero) / 10).ToString("0.0", CultureInfo.InvariantCulture) : "", ["exception"] = red,
            }));
        }

        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, Basis));
    }
}
