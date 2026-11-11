// FX-RPT-D · rpt.stock.par — Par and minimums. Server twin of built/stock.ts `stockPar`: linen items that have a par by room type — pieces per set of rooms (Σ rooms of the type × par),
// required for the configured number of par sets, held now (all locations), coverage % (half up, 100 when nothing is required), the reorder minimum and the clean stock. No money column.
// View PERM-STK-001. An item counts as "with a par" when it has at least one par row (the server stores no empty par).
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Domain.Stock;
using static Hcc.Reporting.Reports.FxD.FxDSupport;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxD;

public sealed class StockParReport(IStockReadStore store) : IReportSelector
{
    public const string Id = "rpt.stock.par";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("item", "Linen item"), I("perSet", "Per set"), I("required", "Required (sets)"), I("held", "Held"), I("coverage", "Coverage %"), I("min", "Minimum"), I("clean", "Clean stock"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Par and minimums", "stock", "P3", "built", false, null, [View], [], "PERM-RPT-003", null, [], Columns,
        "Linen items with a par by room type: pieces per set of rooms, required for the configured number of sets, held now, coverage and the reorder minimum.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public static List<ReportRow> Rows(IEnumerable<StockItemRecord> items, IReadOnlyDictionary<string, int> roomsByType, int parSets) =>
        [.. items.Where(i => i.Kind == "linen" && i.Par.Count > 0).Select(i =>
        {
            var required = StockRules.ParRequired(i.Par, roomsByType, parSets);
            var held = StockRules.Total(i.Stock);
            return Row(ReportRowKind.Row, ("item", i.Name), ("perSet", (long)StockRules.ParPerSet(i.Par, roomsByType)), ("required", (long)required), ("held", (long)held),
                ("coverage", (long)StockRules.ParCoveragePercent(held, required)), ("min", (long)i.MinQty), ("clean", (long)StockRules.Clean(i.Stock)));
        })];

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var items = await store.ItemsAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var rooms = await store.RoomsByTypeAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var settings = await store.SettingsAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(items, rooms, settings.ParSets), About(Definition, parameters));
    }
}
