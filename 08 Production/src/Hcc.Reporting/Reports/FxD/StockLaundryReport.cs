// FX-RPT-D · rpt.stock.laundry — Laundry batches. Server twin of built/stock.ts `stockLaundry`: every batch, newest sent first; received, damaged and missing stay blank while
// the batch is out; the charge is the one computed when the batch came back. Batch reference = "LB-" + id (as the goods-out move). View PERM-STK-001; charge needs PERM-STK-004.
using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Application.Stock;
using Hcc.Domain.Stock;
using static Hcc.Reporting.Reports.FxD.FxDSupport;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxD;

public sealed class StockLaundryReport(IStockReadStore store) : IReportSelector
{
    public const string Id = "rpt.stock.laundry";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        T("batch", "Batch"), T("vendor", "Vendor"), Dt("sent", "Sent"), T("status", "Status"), I("pieces", "Pieces sent"), I("received", "Received"), I("damaged", "Damaged"),
        I("missing", "Missing"), M("charge", "Laundry charge"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Laundry batches", "stock", "P3", "built", true, Gate, [View], [], "PERM-RPT-003", null, [], Columns,
        "Every laundry batch, newest first. Received, damaged and missing are blank while the batch is still out; the charge is the one computed when the batch came back.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Yes);

    public static List<ReportRow> Rows(IEnumerable<LaundryBatchRecord> batches) =>
        [.. batches.OrderByDescending(b => b.SentDate).ThenByDescending(b => b.BatchId).Select(b =>
        {
            var back = b.Status != "Out";
            return Row(ReportRowKind.Row, ("batch", "LB-" + b.BatchId.ToString(CultureInfo.InvariantCulture)), ("vendor", b.Vendor), ("sent", b.SentDate), ("status", b.Status),
                ("pieces", (long)b.Lines.Sum(l => l.Sent)), ("received", back ? (long)b.Lines.Sum(l => l.Received ?? 0) : null), ("damaged", back ? (long)b.Lines.Sum(l => l.Damaged ?? 0) : null),
                ("missing", back ? (long)b.Lines.Sum(l => StockRules.Missing(l.Sent, l.Received, l.Damaged)) : null), ("charge", b.ChargePaise));
        })];

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var batches = await store.BatchesAsync(caller.PropertyId, null, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(batches), About(Definition, parameters));
    }
}
