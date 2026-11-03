// F7-R6 · rpt.acc.cash-ratio — share of receipts and payments made in cash, flagged above 5 % (ACC-3 wired). Adapter over PlQueries.CashRatioAsync.
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class CashRatioReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.cash-ratio";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("measure", "Measure"), M("cash", "Cash"), M("total", "Total"), P("share", "Share"), B("flagged", "Flagged")];

    public ReportDefinition Definition { get; } = new(Id, "Cash ratio", "acc", "P2", "ACC-3", true, OwnerAccountant, [OwnerAccountant], [], "PERM-RPT-003", ReportDateClass.FinancialYear,
        [new("fy", "Financial year", "fy", ReportDateClass.FinancialYear)], Columns,
        "Receipt and Payment vouchers of the financial year: the money side on cash and bank ledgers; cash = 1000 and 1005. Flagged above 5 %.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf], ReportPhone.Summary);

    /// <summary>The report body from the query view (pure; the tests feed it views).</summary>
    public static List<ReportRow> Rows(CashRatioView c, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp)
        {
            Row(ReportRowKind.Row, ("measure", "Receipts"), ("cash", c.CashReceipts), ("total", c.TotalReceipts), ("share", BpsToPct(c.ReceiptsPct)), ("flagged", c.ReceiptsPct > PlReports.CashRatioLimitBps)),
            Row(ReportRowKind.Row, ("measure", "Payments"), ("cash", c.CashPayments), ("total", c.TotalPayments), ("share", BpsToPct(c.PaymentsPct)), ("flagged", c.PaymentsPct > PlReports.CashRatioLimitBps)),
        };
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var c = await q.Pl().CashRatioAsync(caller, parameters.Values["fy"], ct).ConfigureAwait(false);
        var (fyFrom, fyTo) = PlReports.FyRange(c.Fy);
        var stamp = await StampAsync(q, caller, fyFrom, fyTo, "measure", ct).ConfigureAwait(false);
        var rows = Rows(c, stamp);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, stamp.Count > 0));
    }
}
