// FX-RPT-D · rpt.stock.counts — Stock counts. Server twin of built/stock.ts `stockCounts`: each count (newest date first) with the lines where the count differs from the system quantity;
// the variance value (Σ over all lines of (counted − system) × unit cost) is shown on the first line of the count; a count without variance gets one "No variance" line with value 0.
// Count reference = "CNT-" + id. View PERM-STK-001; value needs PERM-STK-004.
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Reporting.Sql;
using static Hcc.Reporting.Reports.FxD.FxDSupport;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxD;

public sealed class StockCountsReport(IStockReadStore store, IMoneyReportSource names) : IReportSelector
{
    public const string Id = "rpt.stock.counts";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("count", "Count"), Dt("date", "Date"), T("location", "Location"), T("status", "Status"), T("item", "Item"), I("system", "System"), I("counted", "Counted"),
        I("variance", "Variance"), M("value", "Variance value"), T("by", "Counted by"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Stock counts", "stock", "P3", "built", true, Gate, [View], [], "PERM-RPT-003", null, [], Columns,
        "Each count with the lines where the count differs from the system quantity (invCountVariance); the variance value is shown on the first line of the count.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public static List<ReportRow> Rows(IEnumerable<CountRecord> counts, IReadOnlyDictionary<string, long> costByCode, IReadOnlyDictionary<int, string> users)
    {
        var rows = new List<ReportRow>();
        foreach (var c in counts.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.CountId))
        {
            var head = ("count", (object?)("CNT-" + c.CountId.ToString(CultureInfo.InvariantCulture)));
            var diff = c.Lines.Where(l => l.Counted != l.System).ToList();
            var total = c.Lines.Sum(l => (l.Counted - l.System) * costByCode.GetValueOrDefault(l.ItemCode));
            if (diff.Count == 0)
            {
                rows.Add(Row(ReportRowKind.Row, head, ("date", c.BusinessDate), ("location", Loc(c.Loc)), ("status", c.Status), ("item", "No variance"), ("system", null), ("counted", null),
                    ("variance", null), ("value", 0L), ("by", Who(users, c.ActorId))));
                continue;
            }

            for (var i = 0; i < diff.Count; i++)
            {
                var l = diff[i];
                rows.Add(Row(ReportRowKind.Row, head, ("date", c.BusinessDate), ("location", Loc(c.Loc)), ("status", c.Status), ("item", l.ItemName), ("system", (long)l.System), ("counted", (long)l.Counted),
                    ("variance", (long)(l.Counted - l.System)), ("value", i == 0 ? total : null), ("by", Who(users, c.ActorId))));
            }
        }

        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var counts = await store.CountsAsync(caller.PropertyId, null, ct).ConfigureAwait(false);
        var items = await store.ItemsAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var users = await names.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(counts, items.ToDictionary(i => i.Code, i => i.CostPaise, StringComparer.Ordinal), users), About(Definition, parameters));
    }
}
