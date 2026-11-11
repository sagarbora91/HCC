// CU-RPT-A · rpt.money.expenses-cash — the closing pack's "Expenses & cash" tab: per business date the drawer opening, expected, counted and difference (cash book calculator) and the
// drawer-cash expenses, then the range tiles. Server twin of built/pack.ts `expensesCash`. Money gate PERM-RPT-002.
// Expenses are the counted non-capital records; "drawer" / "bank" / "credit" come from the payment-method master (settles to a till; any other non-credit method; credit kind).
// A date with no cash-book day (no shift, no movement, not closed, not the current date) shows a dash and blank cash figures, as the prototype does.
using Hcc.Application.Billing;
using Hcc.Application.Closing;
using Hcc.Application.Expenses;
using Hcc.Application.MoneyMasters;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Contracts.Closing;
using Hcc.Contracts.MoneyMasters;
using Hcc.Domain.Closing;
using Hcc.Domain.Errors;
using Hcc.Domain.Expenses;
using Hcc.Domain.MoneyMasters;
using static Hcc.Reporting.Reports.Money.MoneyRules;

namespace Hcc.Reporting.Reports.CuA;

public sealed class ExpensesCashReport(ICashBookSource source, IClosingStore closing, IExpenseStore expenses, IPaymentMethodSource methods) : IReportSelector
{
    public const string Id = "rpt.money.expenses-cash";

    public static readonly IReadOnlyList<ReportColumn> Columns =
    [
        Dt("date", "Business date"), T("day", "Day"), M("opening", "Opening float"), M("expected", "Expected cash"), M("counted", "Counted"), M("difference", "Difference"),
        M("cashExpenses", "Cash expenses"),
    ];

    public static readonly IReadOnlyList<ReportColumn> TileColumns = [T("tile", "Figure"), M("amount", "Amount")];

    public ReportDefinition Definition { get; } = new(Id, "Expenses and cash", "money", "P1", "built", true, CuASql.Finance, [CuASql.Finance], [], "PERM-RPT-003", ReportDateClass.Financial,
        [CuASql.From(ReportDateClass.Financial), CuASql.To(ReportDateClass.Financial)], Columns,
        "Expenses by business date, split by what they settle to; drawer opening, expected, counted and difference from the closing record (open day: the live shift). Cash in / out by receipt date.",
        [ReportFormat.Screen, ReportFormat.Xlsx, ReportFormat.Pdf, ReportFormat.Csv], ReportPhone.Yes);

    private static readonly string[] CashIn = ["payment", "deposit", "pos-sale"];
    private static readonly string[] CashOut = ["refund", "deposit-return", "pos-void"];

    /// <summary>The two blocks (pure): the day table with its total row, and the seven range tiles.</summary>
    public static (List<ReportRow> Days, List<ReportRow> Tiles) Blocks(DateOnly from, DateOnly to, CashBookRangeView view, IEnumerable<LedgerRow> ledger,
        IReadOnlyList<TenderMethod>? master, IEnumerable<CashBookMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(view);
        var counted = ledger.Where(e => e.Counts && !e.Capital).ToList();
        var cash = counted.Where(e => PaymentMethodBook.SettlesToTill(master, e.Method)).ToList();
        var credit = counted.Where(e => !PaymentMethodBook.SettlesToTill(master, e.Method) && PaymentMethodBook.KindOf(master, e.Method) == MoneyMasterValues.Credit).ToList();
        var taken = cash.Concat(credit).Select(e => e.ExpenseId).ToHashSet();
        var bank = counted.Where(e => !taken.Contains(e.ExpenseId)).ToList();
        var cashByDate = cash.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var rows = new List<ReportRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var day = view.Days.FirstOrDefault(x => x.BusinessDate == d);
            rows.Add(CuASql.Row(ReportRowKind.Row, ("date", d), ("day", day?.Status ?? "—"), ("opening", day?.OpeningPaise), ("expected", day?.ExpectedPaise), ("counted", day?.CountedPaise),
                ("difference", day?.DifferencePaise), ("cashExpenses", cashByDate.GetValueOrDefault(d))));
        }

        var cashTotal = cash.Sum(e => e.Amount);
        rows.Add(CuASql.Row(ReportRowKind.Total, ("date", "Total"), ("day", null), ("opening", null), ("expected", null), ("counted", null), ("difference", null), ("cashExpenses", cashTotal)));
        var mv = movements.Where(m => m.BusinessDate >= from && m.BusinessDate <= to).ToList();
        ReportRow Tile(string label, long amount) => CuASql.Row(ReportRowKind.Row, ("tile", label), ("amount", amount));
        var tiles = new List<ReportRow>
        {
            Tile("Drawer-cash expenses", cashTotal), Tile("UPI / bank expenses", bank.Sum(e => e.Amount)), Tile("Vendor-credit expenses (no drawer effect)", credit.Sum(e => e.Amount)),
            Tile("Reversals included above", counted.Where(e => e.IsReversal).Sum(e => e.Amount)), Tile("Posted expenses (net of reversals)", counted.Sum(e => e.Amount)),
            Tile("Cash into drawer (payments + deposits + POS sales)", mv.Where(m => CashIn.Contains(m.Kind)).Sum(m => m.Amount)),
            Tile("Cash out of drawer (refunds + returns + POS voids)", mv.Where(m => CashOut.Contains(m.Kind)).Sum(m => m.Amount)),
        };
        return (rows, tiles);
    }

    public async Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(parameters);
        var (from, to) = (parameters.From!.Value, parameters.To!.Value);
        var bd = await closing.BusinessDateAsync(null, caller.PropertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");
        var data = await source.LoadAsync(caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var view = CashBookCalculator.Compute(from, to, bd, data);
        var ledger = await expenses.LedgerAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var master = await methods.MethodsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var (days, tiles) = Blocks(from, to, view, ledger, master, data.Movements);
        return new ReportResult(Id, Definition.Title, parameters, Columns, days, About(Definition, parameters), null, 0, 0, 0,
            [new ReportTable("days", "By business date", Columns, days), new ReportTable("tiles", "Totals for the range", TileColumns, tiles)]);
    }
}
