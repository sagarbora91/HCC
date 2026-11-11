// CU-RPT-B · rpt.stock.stock-sheet — Stock by location. Server twin of the prototype stock sheet (built/stock.ts): the stock held now — pieces per location, total,
// unit cost and value (total × standard cost). View PERM-STK-001 (quantities); unit cost and value need PERM-STK-004 and are removed without it (rule M).
// Items in code order (the stock module's list order). Point in time: no date parameter.
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Domain.Stock;
using Hcc.Reporting.Reports.Money;

namespace Hcc.Reporting.Reports.CuB;

public sealed class StockSheetReport(IStockReadStore store) : IReportSelector
{
    public const string Id = "rpt.stock.stock-sheet";

    /// <summary>Short column labels of the locations (prototype INV_LOCS short), in the stock module order.</summary>
    private static readonly IReadOnlyDictionary<string, string> LocLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["store"] = "Store", ["floor1"] = "F1", ["floor2"] = "F2", ["floor3"] = "F3", ["rooms"] = "Rooms", ["soiled"] = "Soiled", ["laundry"] = "Laundry",
    };

    private static readonly IReadOnlyDictionary<string, string> KindLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["linen"] = "Linen", ["amenity"] = "Amenities", ["cleaning"] = "Cleaning supplies",
    };

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        MoneyRules.T("item", "Item"), MoneyRules.T("kind", "Kind"), MoneyRules.T("unit", "Unit"),
        .. StockRules.AllLocations.Select(l => MoneyRules.I(l, LocLabels[l])),
        MoneyRules.I("total", "Total"), MoneyRules.M("cost", "Unit cost"), MoneyRules.M("value", "Value"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Stock by location", "stock", "P1", "built", true, "PERM-STK-004", ["PERM-STK-001"], [], "PERM-RPT-003",
        null, [], Columns, "The stock held now: pieces per location, total, unit cost and value (total × standard cost).",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var items = await store.ItemsAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var rows = new List<ReportRow>();
        long all = 0;
        foreach (var i in items)
        {
            var total = StockRules.Total(i.Stock);
            var value = total * i.CostPaise;
            all += value;
            var cells = new List<(string, object?)> { ("item", i.Name), ("kind", KindLabels.GetValueOrDefault(i.Kind, i.Kind)), ("unit", i.Unit) };
            cells.AddRange(StockRules.AllLocations.Select(l => (l, (object?)(long)StockRules.Qty(i.Stock, l))));
            cells.Add(("total", (long)total));
            cells.Add(("cost", i.CostPaise));
            cells.Add(("value", value));
            rows.Add(CuBSupport.Row(ReportRowKind.Row, [.. cells]));
        }

        var t = new List<(string, object?)> { ("item", "Total value"), ("kind", null), ("unit", null) };
        t.AddRange(StockRules.AllLocations.Select(l => (l, (object?)null)));
        t.Add(("total", null));
        t.Add(("cost", null));
        t.Add(("value", all));
        rows.Add(CuBSupport.Row(ReportRowKind.Total, [.. t]));
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, CuBSupport.About(Definition, parameters));
    }
}
