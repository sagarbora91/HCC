using Hcc.Domain.Money;

namespace Hcc.Domain.Billing;

/// <summary>One posted folio line with its tax snapshot as stored (contracts §4 rule 5).</summary>
public sealed record PostedLine(long ChargeId, ChargeKind Kind, DateOnly ServiceDate, string Label, Paise Amount, int TaxBps, Paise Tax, long? ReversesChargeId);

/// <summary>One receipt row as stored (append-only; refunds link to their parent payment).</summary>
public sealed record PostedReceipt(long ReceiptId, ReceiptKind Kind, string Tender, Paise Amount, DateOnly BusinessDate, long? RefundOf, string Reference);

public sealed record FolioFigures(Paise Net, Paise Tax, Paise Total, Paise Paid, Paise Deposit, Paise CompanyCredit, Paise Balance);

/// <summary>
/// Folio totals for the billing module. Wraps the P0 kernel <see cref="FolioTotals"/> (prototype <c>totals()</c>) so the
/// figures stay identical, and cross-checks that the stored per-line tax snapshots add up to the same tax (a stored
/// snapshot that disagrees with its own rate and amount is a data fault, never silently re-rounded).
/// </summary>
public static class FolioStatement
{
    public static FolioFigures Compute(IReadOnlyCollection<PostedLine> lines, IReadOnlyCollection<PostedReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(receipts);
        var t = FolioTotals.Compute(lines.Select(l => new FolioLine(l.Amount, l.TaxBps)), receipts.Select(r => new FolioReceipt(r.Kind, r.Amount)));
        var storedTax = lines.Aggregate(Paise.Zero, (a, l) => a + l.Tax);
        if (storedTax != t.Tax)
            throw new InvalidOperationException($"Stored tax snapshots ({storedTax.Value}) disagree with the line rates ({t.Tax.Value}).");
        var credit = receipts.Where(r => r.Kind == ReceiptKind.CompanyCredit).Aggregate(Paise.Zero, (a, r) => a + r.Amount);
        return new FolioFigures(t.Net, t.Tax, t.Total, t.Paid, t.Deposit, credit, t.Balance);
    }

    /// <summary>Amount already refunded against one payment receipt.</summary>
    public static Paise RefundedAgainst(IEnumerable<PostedReceipt> receipts, long receiptId)
        => receipts.Where(r => r.Kind == ReceiptKind.Refund && r.RefundOf == receiptId).Aggregate(Paise.Zero, (a, r) => a + r.Amount);

    /// <summary>Room-night label as in the prototype (<c>'Room night · ' + date</c>).</summary>
    public static string RoomNightLabel(DateOnly serviceDate) => "Room night · " + serviceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Daily receipts (Financial Rulebook §6): wraps the P0 kernel <see cref="CashFigures.Collections"/>.</summary>
public static class DailyReceipts
{
    public static Paise Collections(IEnumerable<PostedReceipt> receipts, DateOnly businessDate)
        => CashFigures.Collections(receipts.Select(r => new DatedReceipt(r.BusinessDate, r.Tender, r.Kind, r.Amount)), businessDate);
}
