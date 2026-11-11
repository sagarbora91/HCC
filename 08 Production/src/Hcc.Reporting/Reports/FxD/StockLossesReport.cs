// FX-RPT-D · rpt.stock.losses — Losses. Server twin of built/stock.ts `stockLosses`: recorded losses in a business-date range, oldest first, value at the standard cost when recorded
// (stk.loss.value_paise). Total row (quantity and value) only when there are rows. View PERM-STK-001; value needs PERM-STK-004.
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Reporting.Sql;
using static Hcc.Reporting.Reports.FxD.FxDSupport;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxD;

public sealed class StockLossesReport(IStockReadStore store, IMoneyReportSource names) : IReportSelector
{
    public const string Id = "rpt.stock.losses";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [Dt("date", "Date"), T("item", "Item"), T("location", "Location"), I("qty", "Qty"), T("reason", "Reason"), M("value", "Value"), T("by", "By")];

    public ReportDefinition Definition { get; } = new(Id, "Losses", "stock", "P3", "built", true, Gate, [View], [], "PERM-RPT-003", ReportDateClass.Business,
        [From(), To()], Columns, "Recorded losses by date, oldest first; value at the standard cost when recorded.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public static List<ReportRow> Rows(IEnumerable<LossRecord> losses, IReadOnlyDictionary<int, string> users)
    {
        var l = losses.OrderBy(x => x.BusinessDate).ThenBy(x => x.LossId).ToList();
        var rows = l.Select(x => Row(ReportRowKind.Row, ("date", x.BusinessDate), ("item", x.ItemName), ("location", Loc(x.Loc)), ("qty", (long)x.Qty), ("reason", x.Reason),
            ("value", x.ValuePaise), ("by", Who(users, x.ActorId)))).ToList();
        if (rows.Count > 0)
            rows.Add(Row(ReportRowKind.Total, ("date", "Total"), ("item", null), ("location", null), ("qty", (long)l.Sum(x => x.Qty)), ("reason", null), ("value", l.Sum(x => x.ValuePaise)), ("by", null)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var losses = await store.LossesAsync(caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        var users = await names.NamesAsync(caller.PropertyId, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(losses, users), About(Definition, parameters));
    }
}
