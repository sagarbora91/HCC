// F7-R6 · rpt.acc.balance-sheet — owner's funds, liabilities and assets at a date (ACC-3 wired). Adapter over PlQueries.BalanceSheetAsync. An HCC view, NOT the
// statutory statement. Golden 30 April 2026: both sides 79,27,900 rupees (rulebook §8.5).
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class BalanceSheetReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.balance-sheet";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("block", "Block"), T("line", "Line"), M("amount", "Amount")];

    public ReportDefinition Definition { get; } = new(Id, "Balance sheet", "acc", "P3", "ACC-3", true, OwnerAccountant, [OwnerAccountant], [], "PERM-RPT-003", ReportDateClass.AsOf,
        [new("asOf", "As on", "date", ReportDateClass.AsOf)], Columns,
        "Every voucher up to and including the date (opening entry and locked snapshots included). Owner's funds = capital + profit of the year to date - drawings. An HCC view, not the statutory statement.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Summary);

    private static IEnumerable<ReportRow> Block(BsBlockView b) =>
        b.Lines.Select(l => Row(ReportRowKind.Row, ("block", b.Name), ("line", l.Name), ("amount", l.Paise)))
            .Append(Row(ReportRowKind.Subtotal, ("block", b.Name), ("line", "Total " + b.Name.ToLowerInvariant()), ("amount", b.Total)));

    /// <summary>The report body from the query view (pure; the tests feed it views).</summary>
    public static List<ReportRow> Rows(BalanceSheetView b, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp) { Row(ReportRowKind.Subtotal, ("block", "Note"), ("line", "An HCC view of the books — not the statutory statement")) };
        rows.AddRange(Block(b.Liabilities.OwnersFunds));
        rows.AddRange(Block(b.Liabilities.NonCurrent));
        rows.AddRange(Block(b.Liabilities.Current));
        rows.Add(Row(ReportRowKind.Subtotal, ("block", "Liabilities"), ("line", "Total liabilities"), ("amount", b.Liabilities.Total)));
        rows.AddRange(Block(b.Assets.NonCurrent));
        rows.AddRange(Block(b.Assets.Current));
        rows.Add(Row(ReportRowKind.Subtotal, ("block", "Assets"), ("line", "Total assets"), ("amount", b.Assets.Total)));
        rows.Add(Row(ReportRowKind.Row, ("block", "Check"), ("line", "Suspense (9999)"), ("amount", b.Suspense)));
        rows.Add(Row(ReportRowKind.Row, ("block", "Check"), ("line", "Opening-balance difference (1400)"), ("amount", b.OpeningDifference)));
        rows.Add(Row(ReportRowKind.Total, ("block", b.Balanced ? "Balanced" : "Not balanced"), ("line", "Difference (assets - liabilities)"), ("amount", b.Assets.Total - b.Liabilities.Total)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var asOf = parameters.To!.Value;
        var b = await q.Pl().BalanceSheetAsync(caller, asOf, ct).ConfigureAwait(false);
        var stamp = await StampAsync(q, caller, Fy(b.AsOf), b.AsOf, "line", ct).ConfigureAwait(false);
        var rows = Rows(b, stamp);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, b.Provisional || stamp.Count > 0));
    }
}
