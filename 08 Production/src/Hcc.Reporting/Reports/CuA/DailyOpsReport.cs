// CU-RPT-A · rpt.guests.daily-ops — the closing pack's "Daily operations" tab. Server twin of built/pack.ts `dailyOps`. Money gate PERM-RPT-002.
// Every figure comes from the daily pack calculator the closing snapshot uses (DailyPackCalculator over IReportSource); expenses = counted, non-capital expense records by business date
// (Posted / Reversed originals plus their negative reversal records), as the prototype's reportExpenses.
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Expenses;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Reporting;
using Hcc.Domain.Errors;
using Hcc.Domain.Reporting;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.CuA;

public sealed class DailyOpsReport(IReportSource source, IClosingStore closing, IExpenseStore expenses) : IReportSelector
{
    public const string Id = "rpt.guests.daily-ops";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        Dt("date", "Business date"), T("status", "Day status"), I("arrivals", "Arrivals"), I("departures", "Departures"), I("occupied", "Occupied room-nights"),
        M("netRevenue", "Net revenue"), M("netReceipts", "Net receipts"), M("expenses", "Expenses"),
    ];

    public ReportDefinition Definition { get; } = new(Id, "Daily operations", "guests", "P1", "built", true, CuASql.Finance, [CuASql.Finance], [], "PERM-RPT-003", ReportDateClass.Financial,
        [CuASql.From(ReportDateClass.Financial), CuASql.To(ReportDateClass.Financial)], Columns,
        "Charges by service date, receipts by receipt date, expenses by business date (the closing report pack, tab Daily operations). Total row = the tab tiles.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes, false, []);

    /// <summary>Day status as the pack shows it: Closed, Open (the current business date), Future, else a dash.</summary>
    public static string Status(DateOnly d, DateOnly businessDate, IReadOnlySet<DateOnly> closed) =>
        closed.Contains(d) ? "Closed" : d == businessDate ? "Open" : d > businessDate ? "Future" : "—";

    /// <summary>Counted, non-capital expense amounts per business date (reversal records are negative and offset on their own date).</summary>
    public static Dictionary<DateOnly, long> ExpensesByDate(IEnumerable<Hcc.Domain.Expenses.LedgerRow> ledger) =>
        ledger.Where(e => e.Counts && !e.Capital).GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

    /// <summary>The report body (pure).</summary>
    public static List<ReportRow> Rows(DailyReport pack, IReadOnlyDictionary<DateOnly, long> expenseByDate, DateOnly businessDate, IReadOnlySet<DateOnly> closed)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var rows = new List<ReportRow>();
        long arr = 0, dep = 0, occ = 0, rev = 0, rec = 0, exp = 0;
        foreach (var d in pack.Days)
        {
            var e = expenseByDate.GetValueOrDefault(d.Date);
            rows.Add(CuASql.Row(ReportRowKind.Row, ("date", d.Date), ("status", Status(d.Date, businessDate, closed)), ("arrivals", (long)d.Arrivals), ("departures", (long)d.Departures),
                ("occupied", (long)d.Occupied), ("netRevenue", d.NetRevenuePaise), ("netReceipts", d.NetReceiptsPaise), ("expenses", e)));
            arr += d.Arrivals; dep += d.Departures; occ += d.Occupied; rev += d.NetRevenuePaise; rec += d.NetReceiptsPaise; exp += e;
        }

        rows.Add(CuASql.Row(ReportRowKind.Total, ("date", "Total"), ("status", null), ("arrivals", arr), ("departures", dep), ("occupied", occ), ("netRevenue", rev), ("netReceipts", rec), ("expenses", exp)));
        return rows;
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var bd = await closing.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var data = await source.LoadAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var pack = DailyPackCalculator.Compute(data, from, to);
        var closed = (await closing.ClosedDatesAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false)).ToHashSet();
        var ledger = await expenses.LedgerAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        return new ReportResult(Id, Definition.Title, parameters, Columns, Rows(pack, ExpensesByDate(ledger), bd, closed), About(Definition, parameters));
    }
}
