// CU-RPT-B · rpt.rest.day-summary — Outlet day summary. Server twin of the prototype posDaySummary report (built/rest.ts): settled checks by settlement date,
// open checks as of now. Figures come from IPosStore.DaySummaryAsync (the same query as GET /pos/day-summary); the settlement block groups settled bills by
// payment-method name (POS methods of the master first, then the room-folio postings, then any other name found). Money gate PERM-POS-004 (Owner, Manager, Accountant).
using System.Data;
using Hcc.Application.Billing;
using Hcc.Application.Pos;
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Money;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.CuB;

public sealed class RestDaySummaryReport(IPosStore store, IPosTaxRates taxRates, SqlPlatform platform) : IReportSelector
{
    public const string Id = "rpt.rest.day-summary";
    public const string RoomFolioLabel = "Posted to room folios";

    public static readonly IReadOnlyList<ReportColumn> OutletCols =
    [
        MoneyRules.T("outlet", "Outlet"), MoneyRules.I("bills", "Bills"), MoneyRules.M("net", "Net"), MoneyRules.M("tax", "Tax"), MoneyRules.M("total", "Total"), MoneyRules.I("open", "Open checks"),
    ];

    private static readonly IReadOnlyList<ReportColumn> SettleCols = [MoneyRules.T("settlement", "Settlement"), MoneyRules.M("amount", "Amount")];
    private static readonly IReadOnlyList<ReportColumn> TopCols = [MoneyRules.T("item", "Top item"), MoneyRules.I("qty", "Item qty"), MoneyRules.M("value", "Item value")];
    private static readonly IReadOnlyList<ReportColumn> OpenCols = [MoneyRules.T("open", "Open checks"), MoneyRules.I("count", "Checks"), MoneyRules.M("value", "Value")];

    public ReportDefinition Definition { get; } = new(Id, "Outlet day summary", "rest", "P1", "built", true, "PERM-POS-004", ["PERM-POS-004"], [], "PERM-RPT-003",
        ReportDateClass.Day, [new("from", "Business date", "date", ReportDateClass.Day)], OutletCols,
        "Settled checks by settlement date; open checks as of now. Posted to room folios = checks settled to a stay (a charge, not a receipt).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    private const string TenderSql = """
        SELECT tender, SUM(total_paise) FROM pos.bill WHERE property_id = @p AND status = 'Settled' AND business_date = @d GROUP BY tender;
        """;

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var date = parameters.From!.Value;
        var today = await store.PropertyBusinessDateAsync(caller.PropertyId, ct).ConfigureAwait(false) ?? date;
        var bps = (await taxRates.FoodAsync(null, caller.PropertyId, today, ct).ConfigureAwait(false)).Bps;
        var d = await store.DaySummaryAsync(caller.PropertyId, date, bps, ct).ConfigureAwait(false);

        var outlets = d.ByOutlet.Select(o => CuBSupport.Row(ReportRowKind.Row, ("outlet", o.Name), ("bills", (long)o.Checks), ("net", o.NetPaise), ("tax", o.TaxPaise), ("total", o.GrossPaise),
            ("open", (long)o.OpenChecks))).ToList();
        outlets.Add(CuBSupport.Row(ReportRowKind.Total, ("outlet", "All outlets"), ("bills", (long)d.Checks), ("net", d.NetPaise), ("tax", d.TaxPaise), ("total", d.GrossPaise), ("open", (long)d.OpenCount)));

        var byTender = new Dictionary<string, long>(StringComparer.Ordinal);
        long folio = 0;
        await using (var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false))
        {
            await using var cmd = new SqlCommand(TenderSql, conn);
            cmd.Parameters.Add("@p", SqlDbType.Int).Value = caller.PropertyId;
            cmd.Parameters.Add("@d", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                if (rd.IsDBNull(0)) folio += rd.GetInt64(1);
                else byTender[rd.GetString(0)] = byTender.GetValueOrDefault(rd.GetString(0)) + rd.GetInt64(1);
            }
        }

        var methods = await CuBSupport.MethodNamesAsync(platform, caller.PropertyId, CuBSupport.Place.Pos, ct).ConfigureAwait(false);
        if (methods.Count == 0) methods = ["Cash", "UPI", "Card"];
        var settle = methods.Select(m => CuBSupport.Row(ReportRowKind.Row, ("settlement", m), ("amount", byTender.GetValueOrDefault(m)))).ToList();
        settle.Add(CuBSupport.Row(ReportRowKind.Row, ("settlement", RoomFolioLabel), ("amount", folio)));
        settle.AddRange(byTender.Where(k => !methods.Contains(k.Key)).OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => CuBSupport.Row(ReportRowKind.Row, ("settlement", k.Key), ("amount", k.Value))));

        var top = d.Top.Select(t => CuBSupport.Row(ReportRowKind.Row, ("item", t.Name), ("qty", (long)t.Qty), ("value", t.ValuePaise))).ToList();
        var open = new List<ReportRow>
        {
            CuBSupport.Row(ReportRowKind.Row, ("open", "Open checks now"), ("count", (long)d.OpenCount), ("value", d.OpenValuePaise)),
            CuBSupport.Row(ReportRowKind.Row, ("open", "Voided lines today"), ("count", (long)d.VoidCount), ("value", d.VoidValuePaise)),
        };
        var a = new ReportTable("outlets", "By outlet", OutletCols, outlets);
        return new ReportResult(Id, Definition.Title, parameters, a.Columns, a.Rows, CuBSupport.About(Definition, parameters),
            Sections: [a, new("settlement", "By settlement", SettleCols, settle), new("top", "Top items", TopCols, top), new("open", "Open checks", OpenCols, open)]);
    }
}
