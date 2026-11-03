// F7-R6 · rpt.acc.pl — profit and loss (ACC-3 wired). Adapter over PlQueries.PlAsync: ICAI vertical, or the Tally-group view. Golden April 2026 profit before tax 1,40,400 rupees.
using Hcc.Application.Accounting;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts.Accounting;
using static Hcc.Reporting.Reports.Acc.AccShared;
using static Hcc.Reporting.Reports.Tax.TaxSql;

namespace Hcc.Reporting.Reports.Acc;

public sealed class PlReport(AccQueries q) : IReportSelector
{
    public const string Id = "rpt.acc.pl";

    public static readonly IReadOnlyList<ReportColumn> Columns = [T("code", "Code"), T("line", "Line"), T("group", "Group"), M("amount", "Amount")];

    public ReportDefinition Definition { get; } = new(Id, "Profit and loss", "acc", "P2", "ACC-3", true, OwnerAccountant, [OwnerAccountant], [], "PERM-RPT-003", ReportDateClass.Financial,
        [Range(), new("tallyView", "Tally-group view", "bool", null, null, "false")], Columns,
        "Period vouchers of the range (the opening entry is never P&L). Income = credits less debits, expenses = debits less credits. Provisional while opening balances are pending or the range mixes locked and open days.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Summary);

    private static ReportRow L(PlLineView l) => Row(ReportRowKind.Row, ("code", l.Code), ("line", l.Name), ("group", l.Group), ("amount", l.Paise));

    private static ReportRow Sub(string line, long amount) => Row(ReportRowKind.Subtotal, ("line", line), ("amount", amount));

    /// <summary>The report body from the query view (pure; the tests feed it views).</summary>
    public static List<ReportRow> Rows(PlView x, bool tallyView, IReadOnlyList<ReportRow> stamp)
    {
        var rows = new List<ReportRow>(stamp);
        if (tallyView)
            rows.AddRange(x.ByTallyGroup.Select(g => Row(ReportRowKind.Row, ("code", g.Group), ("line", g.Name), ("group", ""), ("amount", g.Paise))));
        else
        {
            rows.Add(Row(ReportRowKind.Subtotal, ("line", "Revenue from operations")));
            rows.AddRange(x.Revenue.Lines.Select(L));
            rows.Add(Sub("Total revenue", x.Revenue.Total));
            rows.Add(Row(ReportRowKind.Subtotal, ("line", "Other income")));
            rows.AddRange(x.OtherIncome.Lines.Select(L));
            rows.Add(Sub("Total other income", x.OtherIncome.Total));
            rows.Add(Row(ReportRowKind.Subtotal, ("line", "Expenses")));
            foreach (var g in new[] { x.Expenses.Consumption, x.Expenses.Employee, x.Expenses.Finance, x.Expenses.Depreciation, x.Expenses.Other })
            {
                rows.Add(Row(ReportRowKind.Subtotal, ("line", g.Name)));
                rows.AddRange(g.Lines.Select(L));
                rows.Add(Sub("Total " + g.Name.ToLowerInvariant(), g.Total));
            }

            rows.Add(Sub("Total expenses", x.Expenses.Total));
        }

        rows.AddRange((x.Warnings ?? []).Select(w => Row(ReportRowKind.Row, ("line", "Warning: " + w))));
        rows.Add(Row(ReportRowKind.Total, ("line", "Profit before tax"), ("amount", x.ProfitBeforeTax)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var x = await q.Pl().PlAsync(caller, from, to, ct).ConfigureAwait(false);
        var stamp = await StampAsync(q, caller, from, to, "line", ct).ConfigureAwait(false);
        var rows = Rows(x, parameters.Values.GetValueOrDefault("tallyView") == "true", stamp);
        return new ReportResult(Id, Definition.Title, parameters, Columns, rows, About(Definition, parameters, x.Provisional || stamp.Count > 0));
    }
}
