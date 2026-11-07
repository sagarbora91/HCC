// F12-RPT-GUEST · rpt.guests.reputation-index — Online reputation (meas.review_month; switch meas.guest.reputation). Twin of meas-guest/reputation-index.ts.
using Hcc.Application.Billing;
using System.Data;
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.MeasGuest;

public sealed class ReputationIndexReport(SqlPlatform platform, ISettingsReader? settings = null) : MeasGuestBase(platform, settings)
{
    public const string Id = "rpt.guests.reputation-index";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MeasGuestRules.T("month", "Month"), MeasGuestRules.T("platform", "Platform"), MeasGuestRules.I("reviews", "Reviews received"), MeasGuestRules.T("avgScore", "Average score"),
        MeasGuestRules.T("score12m", "12-month score"), MeasGuestRules.P("responseRate", "Response rate"), MeasGuestRules.T("themes", "Themes"),
    ];

    public override string MeasKey => "meas.guest.reputation";

    public override ReportDefinition Definition { get; } = new(Id, "Online reputation", "guests", "P2", "MEAS", false, null, ["PERM-RPT-001"], [], "PERM-RPT-003", ReportDateClass.FinancialYear,
        [new("fy", "Financial year", "fy", ReportDateClass.FinancialYear), new("platform", "Platform", "enum", null, ["google", "agoda", "makemytrip", "booking", "other"])],
        Columns, "One row per month and platform for the financial year (April to March). Scores 0-5 with two decimals. Response rate = reviews answered / reviews received.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public override async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        string from = parameters.From!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture), to = parameters.To!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture), platform = Param(parameters, "platform");
        var rows = new List<ReportRow>();
        // MEAS2-B (M1): the month before the range is read too, so the first month's red rule can compare with the previous month of the same platform.
        var avgOf = new Dictionary<string, decimal>(StringComparer.Ordinal);
        await using var conn = await Platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            SELECT month, platform, review_count, avg_score, score_12m, responded, themes
              FROM meas.review_month
             WHERE property_id = @p AND month BETWEEN @prev AND @to AND (@platform = '' OR platform = @platform)
             ORDER BY month, platform;
            """, conn);
        Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        Add(cmd, "@prev", SqlDbType.Char, MeasGuestRules.PrevMonth(from));
        Add(cmd, "@to", SqlDbType.Char, to);
        Add(cmd, "@platform", SqlDbType.VarChar, platform);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            int count = rd.GetInt32(2), responded = rd.GetInt32(5);
            string month = rd.GetString(0).Trim(), plat = rd.GetString(1);
            var avg = rd.GetDecimal(3);
            avgOf[month + "\0" + plat] = avg;
            if (string.CompareOrdinal(month, from) < 0) continue; // the previous month only feeds the rule
            decimal? prev = avgOf.TryGetValue(MeasGuestRules.PrevMonth(month) + "\0" + plat, out var pa) ? pa : null;
            rows.Add(Row(red: MeasGuestRules.ReputationRed(avg, prev, count, responded), cells: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["month"] = month, ["platform"] = plat, ["reviews"] = (long)count,
                ["avgScore"] = rd.GetDecimal(3).ToString("0.00", CultureInfo.InvariantCulture), ["score12m"] = rd.IsDBNull(4) ? null : rd.GetDecimal(4).ToString("0.00", CultureInfo.InvariantCulture),
                ["responseRate"] = MeasGuestRules.Pct1(responded, count), ["themes"] = rd.IsDBNull(6) ? "" : rd.GetString(6),
            }));
        }

        return Result(parameters, rows, "gsv.review_month (meas-cap-guest)");
    }
}
