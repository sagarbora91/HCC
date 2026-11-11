// FX-RPT-C · rpt.money.closing-records — one row per closed business date of a financial year, newest first, from the stored closing record (cls.closing_snapshot, the current
// record of the date; one replaced by a re-close is history and not listed). Server twin of built/money.ts `closingRecords`. View PERM-CLS-001, money gate PERM-RPT-002.
// Cash difference = the stored record's figures.drawer.differencePaise (the sum over every closed shift of counted - expected, what the production client shows), 0 when absent. Corrections = how many times the Owner reopened the date.
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxC;

public sealed class ClosingRecordsReport(SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.money.closing-records";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [Dt("date", "Business date"), T("closedBy", "Closed by"), T("closedAt", "Closed at"), M("netRevenue", "Net revenue"), M("netReceipts", "Net receipts"), M("cashDifference", "Cash difference"), I("corrections", "Corrections")];

    public ReportDefinition Definition { get; } = new(Id, "Earlier closing records", "money", "P2", "built", true, FxCSupport.Finance, ["PERM-CLS-001"], [], "PERM-RPT-003", ReportDateClass.FinancialYear,
        [new("fy", "Financial year", "fy", ReportDateClass.FinancialYear)], Columns,
        "One row per closed business date, newest first, from the stored closing record. Corrections = how many times the Owner reopened that date.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var names = await FxCSupport.NamesAsync(platform, caller.PropertyId, ct).ConfigureAwait(false);
        var rows = new List<ReportRow>();
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            SELECT s.business_date, s.closed_by, s.closed_utc, s.net_revenue_paise, s.net_receipts_paise, ISNULL(CAST(JSON_VALUE(s.record_json, '$.figures.drawer.differencePaise') AS BIGINT), 0),
                   (SELECT COUNT(*) FROM cls.day_reopen r WHERE r.property_id = s.property_id AND r.business_date = s.business_date)
              FROM cls.closing_snapshot s
             WHERE s.property_id = @p AND s.superseded_utc IS NULL AND s.business_date BETWEEN @from AND @to
             ORDER BY s.business_date DESC;
            """, conn);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = caller.PropertyId;
        cmd.Parameters.Add("@from", SqlDbType.Date).Value = parameters.From!.Value.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@to", SqlDbType.Date).Value = parameters.To!.Value.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var difference = rd.GetInt64(5);
            rows.Add(FxCSupport.Row(ReportRowKind.Row, ("date", DateOnly.FromDateTime(rd.GetDateTime(0))), ("closedBy", FxCSupport.NameOf(names, rd.GetInt32(1))), ("closedAt", IstText(rd.GetDateTime(2))),
                ("netRevenue", rd.GetInt64(3)), ("netReceipts", rd.GetInt64(4)), ("cashDifference", difference), ("corrections", (long)rd.GetInt32(6))));
        }

        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, FxCSupport.About(Definition, parameters));
    }
}
