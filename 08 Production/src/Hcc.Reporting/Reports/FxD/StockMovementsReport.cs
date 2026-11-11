// FX-RPT-D · rpt.stock.movements — Stock movements. Server twin of built/stock.ts `stockMovements`: every stock move (stk.move) in a business-date range, newest first, optionally only items
// whose name contains the text of the "item" parameter (case-insensitive). "When" = hotel-local time (IST) of the move. From / to show location labels; a goods receipt's from is the supplier,
// a use move's to is "used". No money column. View PERM-STK-001. CSV allowed.
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Reporting.Sql;
using static Hcc.Reporting.Reports.FxD.FxDSupport;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxD;

public sealed class StockMovementsReport(IStockReadStore store, IMoneyReportSource names) : IReportSelector
{
    public const string Id = "rpt.stock.movements";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [T("when", "When"), T("type", "Type"), T("item", "Item"), I("qty", "Qty"), T("from", "From"), T("to", "To"), T("ref", "Reference"), T("by", "By")];

    public ReportDefinition Definition { get; } = new(Id, "Stock movements", "stock", "P3", "built", false, null, [View], [], "PERM-RPT-003", ReportDateClass.Business,
        [From(), To(), new("item", "Item", "text")], Columns, "Every stock move in the range, newest first.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    public static List<ReportRow> Rows(IEnumerable<MoveRecord> moves, IReadOnlyDictionary<string, string> itemNames, IReadOnlyDictionary<int, string> users, string filter)
    {
        var q = filter.Trim();
        return [.. moves.Select(m => (Move: m, Name: itemNames.GetValueOrDefault(m.ItemCode, m.ItemCode)))
            .Where(x => q.Length == 0 || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Move.AtUtc).ThenByDescending(x => x.Move.MoveId)
            .Select(x => Row(ReportRowKind.Row, ("when", IstText(x.Move.AtUtc)), ("type", x.Move.Kind), ("item", x.Name), ("qty", (long)x.Move.Qty),
                ("from", string.IsNullOrEmpty(x.Move.From) ? null : Loc(x.Move.From)), ("to", string.IsNullOrEmpty(x.Move.To) ? null : Loc(x.Move.To)),
                ("ref", string.IsNullOrEmpty(x.Move.Ref) ? null : x.Move.Ref), ("by", Who(users, x.Move.ActorId))))];
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var moves = await store.MovesAsync(caller.PropertyId, null, parameters.From!.Value, parameters.To!.Value, ReportRunner.FileMax + 1, ct).ConfigureAwait(false);
        var items = await store.ItemsAsync(caller.PropertyId, ct).ConfigureAwait(false);
        var users = await names.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(moves, items.ToDictionary(i => i.Code, i => i.Name, StringComparer.Ordinal), users, Param(parameters, "item")), About(Definition, parameters));
    }
}
