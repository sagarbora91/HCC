// FX-RPT-D · rpt.stock.reorder — Reorder alerts. Server twin of built/stock.ts `stockReorder`: items whose clean stock (main store + floor pantries) is below the minimum, now.
// Suggested = max(reorder quantity, 2 × minimum − clean). Most short first (clean − minimum), then name. View PERM-STK-001; unit cost needs PERM-STK-004 (rule M).
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Domain.Stock;
using static Hcc.Reporting.Reports.FxD.FxDSupport;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxD;

public sealed class StockReorderReport(IStockReadStore store) : IReportSelector
{
    public const string Id = "rpt.stock.reorder";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [T("item", "Item"), T("kind", "Kind"), I("clean", "Clean stock"), I("min", "Minimum"), I("suggest", "Suggested order"), M("cost", "Unit cost")];

    public ReportDefinition Definition { get; } = new(Id, "Reorder alerts", "stock", "P2", "built", true, Gate, [View], [], "PERM-RPT-003", null, [], Columns,
        "Items whose clean stock (store + floor pantries) is below the minimum, now. Suggested = the larger of the reorder quantity and twice the minimum less clean stock.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    /// <summary>The alert rows from the items (pure).</summary>
    public static List<ReportRow> Rows(IEnumerable<StockItemRecord> items) =>
        [.. items.Select(i => (Item: i, Clean: StockRules.Clean(i.Stock))).Where(x => StockRules.BelowMinimum(x.Clean, x.Item.MinQty))
            .OrderBy(x => x.Clean - x.Item.MinQty).ThenBy(x => x.Item.Name, StringComparer.InvariantCulture)
            .Select(x => Row(ReportRowKind.Row, ("item", x.Item.Name), ("kind", Kind(x.Item.Kind)), ("clean", (long)x.Clean), ("min", (long)x.Item.MinQty),
                ("suggest", (long)StockRules.SuggestedReorder(x.Clean, x.Item.MinQty, x.Item.ReorderQty)), ("cost", x.Item.CostPaise)))];

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var items = await store.ItemsAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(items), About(Definition, parameters));
    }
}
