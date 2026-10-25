namespace Hcc.Domain.Money;

public sealed record DatedReceipt(DateOnly Date, string Method, ReceiptKind Kind, Paise Amount);

public sealed record OutletReceipt(DateOnly Date, string Method, Paise Amount);

public enum ExpenseStatus
{
    Pending,
    Posted,
    Reversed,
    Rejected,
}

public sealed record CashExpense(DateOnly Date, ExpenseStatus Status, string Method, Paise Amount);

/// <summary>
/// Collections and expected drawer cash, ported from the prototype's <c>metrics().collections</c> and <c>drawer()</c>
/// (<c>03 Prototype/src/domain/selectors.ts</c>; Financial Rulebook §4; shared till DEC-052). P0 kernel used by the golden
/// tests; ownership moves to P4 (cashier) / P6 (closing), which must keep these figures.
/// </summary>
public static class CashFigures
{
    public const string Cash = "Cash";

    /// <summary>Payments minus refunds on the business date, all tenders. Deposits are not collections.</summary>
    public static Paise Collections(IEnumerable<DatedReceipt> receipts, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        var sum = Paise.Zero;
        foreach (var r in receipts.Where(r => r.Date == businessDate))
        {
            if (r.Kind == ReceiptKind.Payment) sum += r.Amount;
            else if (r.Kind == ReceiptKind.Refund) sum -= r.Amount;
        }

        return sum;
    }

    /// <summary>
    /// Opening float + cash payments/deposits − other cash receipt kinds (refunds, deposit returns) + outlet (restaurant)
    /// cash net of voids − posted or reversed cash expenses, all on the business date.
    /// </summary>
    public static Paise ExpectedDrawer(Paise openingFloat, DateOnly businessDate, IEnumerable<DatedReceipt> folioReceipts,
        IEnumerable<OutletReceipt> outletReceipts, IEnumerable<CashExpense> expenses)
    {
        ArgumentNullException.ThrowIfNull(folioReceipts);
        ArgumentNullException.ThrowIfNull(outletReceipts);
        ArgumentNullException.ThrowIfNull(expenses);
        var total = openingFloat;
        foreach (var r in folioReceipts.Where(r => r.Date == businessDate && r.Method == Cash))
            total += r.Kind is ReceiptKind.Payment or ReceiptKind.Deposit ? r.Amount : -r.Amount;
        foreach (var o in outletReceipts.Where(o => o.Date == businessDate && o.Method == Cash))
            total += o.Amount;
        foreach (var e in expenses.Where(e => e.Date == businessDate && e.Method == Cash && e.Status is ExpenseStatus.Posted or ExpenseStatus.Reversed))
            total -= e.Amount;
        return total;
    }
}
