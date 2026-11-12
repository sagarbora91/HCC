// FX-RPT-C · rpt.money.expense-month — the month's counted expenses (Posted / Reversed originals and reversal records, capital purchases included, as the prototype's
// expMonthSummary and the server's tax feed) by head; GST paid and ITC allowed / disallowed are month figures on the total row (DEC-135). Server twin of built/money.ts
// `expenseMonth`. View and money gate PERM-RPT-002. Figures come from ExpenseScc.Summary over the same ledger the tax feed uses.
using Hcc.Application.Expenses;
using Hcc.Application.Billing;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Expenses;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.FxC;

public sealed class ExpenseMonthReport(IExpenseStore expenses) : IReportSelector
{
    public const string Id = "rpt.money.expense-month";

    public static readonly IReadOnlyList<ReportColumn> Columns =
        [T("head", "Head"), M("amount", "Amount"), I("entries", "Entries"), M("gstPaid", "GST paid"), M("itcAllowed", "ITC allowed"), M("itcDisallowed", "ITC disallowed")];

    public ReportDefinition Definition { get; } = new(Id, "Expenses month and tax feed", "money", "P2", "built", true, FxCSupport.Finance, [FxCSupport.Finance], [], "PERM-RPT-003", ReportDateClass.Month,
        [new("month", "Month", "month", ReportDateClass.Month)], Columns,
        "The month's counted expenses (the tax-feed figures of expMonthSummary), by head; GST paid and ITC allowed are month totals, shown on the total row.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    /// <summary>The report body (pure): one row per head (A to Z) and the total row.</summary>
    public static List<ReportRow> Rows(IEnumerable<LedgerRow> ledger, string month)
    {
        var rows = ledger.Where(r => r.Counts && r.Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) == month).ToList();
        var m = ExpenseScc.Summary(rows, month);
        var body = m.ByCategory.OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(k => FxCSupport.Row(ReportRowKind.Row, ("head", k.Key), ("amount", k.Value), ("entries", (long)rows.Count(r => r.Category == k.Key)), ("gstPaid", null), ("itcAllowed", null), ("itcDisallowed", null))).ToList();
        body.Add(FxCSupport.Row(ReportRowKind.Total, ("head", "Total"), ("amount", m.ExpensePaise), ("entries", (long)m.Count), ("gstPaid", m.GstPaidPaise), ("itcAllowed", m.GstItcPaise),
            ("itcDisallowed", m.GstPaidPaise - m.GstItcPaise)));
        return body;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var ledger = await expenses.LedgerAsync(null, caller.PropertyId, parameters.From!.Value, parameters.To!.Value, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(ledger, Param(parameters, "month")), FxCSupport.About(Definition, parameters));
    }
}
