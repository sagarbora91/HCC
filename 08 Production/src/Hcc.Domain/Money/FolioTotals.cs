namespace Hcc.Domain.Money;

/// <summary>Receipt kinds (prototype <c>Receipt.kind</c>; SQL <c>pay.receipt.kind</c>).</summary>
public enum ReceiptKind
{
    Payment,
    Deposit,
    DepositReturn,
    Refund,
    CompanyCredit,
    DepositApply, // ACC-2 flows (contract §4, A6a): the deposit held applied to the folio at check-out
    OtaCredit,    // ACC-2 flows (contract §4, A13): the folio balance moved to the OTA's receivable at check-out
}

public sealed record FolioLine(Paise Amount, int TaxBps);

public sealed record FolioReceipt(ReceiptKind Kind, Paise Amount);

public sealed record FolioTotalsResult(Paise Net, Paise Tax, Paise Total, Paise Paid, Paise Deposit, Paise Balance);

/// <summary>
/// Folio totals ported from the prototype's <c>totals()</c> (<c>03 Prototype/src/domain/selectors.ts</c>):
/// tax per line at its snapshot rate; deposits are held separately and never reduce the balance; company credit moves
/// the balance to a receivable (Financial Rulebook §2). Owner since Wave 1 integration: Billing (P4; wrapped by
/// <c>Hcc.Domain.Billing.FolioStatement</c>). The file stays here (namespace <c>Hcc.Domain.Money</c>) to avoid churn.
/// </summary>
public static class FolioTotals
{
    public static FolioTotalsResult Compute(IEnumerable<FolioLine> lines, IEnumerable<FolioReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(receipts);
        var net = Paise.Zero;
        var tax = Paise.Zero;
        foreach (var l in lines)
        {
            net += l.Amount;
            tax += MoneyMath.TaxOf(l.Amount, l.TaxBps);
        }

        Paise paid = Paise.Zero, deposit = Paise.Zero, credit = Paise.Zero;
        foreach (var r in receipts)
        {
            switch (r.Kind)
            {
                case ReceiptKind.Payment: paid += r.Amount; break;
                case ReceiptKind.Refund: paid -= r.Amount; break;
                case ReceiptKind.Deposit: deposit += r.Amount; break;
                case ReceiptKind.DepositReturn: deposit -= r.Amount; break;
                case ReceiptKind.CompanyCredit: credit += r.Amount; break;
                case ReceiptKind.OtaCredit: credit += r.Amount; break;                          // ACC-2 flows: like company credit
                case ReceiptKind.DepositApply: deposit -= r.Amount; credit += r.Amount; break;  // ACC-2 flows: leaves the deposit, settles the balance
                default: throw new ArgumentOutOfRangeException(nameof(receipts), r.Kind, "Unknown receipt kind.");
            }
        }

        var total = net + tax;
        return new FolioTotalsResult(net, tax, total, paid, deposit, total - paid - credit);
    }

    /// <summary>Parses the prototype/SQL receipt kind spelling (payment, deposit, deposit-return, refund, company-credit).</summary>
    public static ReceiptKind ParseKind(string kind) => kind switch
    {
        "payment" => ReceiptKind.Payment,
        "deposit" => ReceiptKind.Deposit,
        "deposit-return" => ReceiptKind.DepositReturn,
        "refund" => ReceiptKind.Refund,
        "company-credit" => ReceiptKind.CompanyCredit,
        "deposit-apply" => ReceiptKind.DepositApply,
        "ota-credit" => ReceiptKind.OtaCredit,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown receipt kind."),
    };

    /// <summary>The prototype/SQL spelling of a receipt kind.</summary>
    public static string KindCode(ReceiptKind kind) => kind switch
    {
        ReceiptKind.Payment => "payment",
        ReceiptKind.Deposit => "deposit",
        ReceiptKind.DepositReturn => "deposit-return",
        ReceiptKind.Refund => "refund",
        ReceiptKind.CompanyCredit => "company-credit",
        ReceiptKind.DepositApply => "deposit-apply",
        ReceiptKind.OtaCredit => "ota-credit",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown receipt kind."),
    };
}
