using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Application.Groups;

/// <summary>
/// <see cref="IGroupFolioPort"/> over Billing's <see cref="IBillingStore"/> (the POS room-posting port is the precedent): the folio row is
/// locked, the transfer is an append-only <c>company-credit</c> receipt (tender "Company credit", reference "Group master …"), the folio
/// version moves (approvals bound to the old version go stale, DEC-041) and <c>ReceiptRecorded</c> is published so Accounting books
/// Dr company receivables · Cr guest ledger exactly as for an approved company credit. Billing's own files are unchanged.
/// </summary>
public sealed class BillingGroupFolioPort(IBillingStore store) : IGroupFolioPort
{
    public async Task<GuestFolioState> LockFolioAsync(ICommandTransaction tx, int propertyId, long folioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var f = await store.LockFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        if (f is null || f.PropertyId != propertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        return new GuestFolioState(f.FolioId, f.ReservationId, f.ReservationStatus, f.IsOpen, f.Version);
    }

    public async Task<long> TransferToMasterAsync(ICommandTransaction tx, long folioId, Paise amount, DateOnly businessDate, string reference, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var receipt = new NewReceipt(folioId, ReceiptKind.CompanyCredit, Tenders.CompanyCredit, amount.EnsureReceiptAmount(), null, businessDate, null, null,
            reference.Length <= 120 ? reference : reference[..120], null, tx.Context.PrincipalId, utc);
        var id = await store.InsertReceiptAsync(tx, receipt, ct).ConfigureAwait(false);
        await store.TouchFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        await tx.AuditAsync("RECEIPT_RECORDED", "folio:" + folioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"company-credit · {Tenders.CompanyCredit} · {amount} · receipt {id} · {receipt.Reference}", ct).ConfigureAwait(false);
        await tx.PublishAsync("ReceiptRecorded", folioId, new { folioId, receiptId = id, kind = FolioTotals.KindCode(ReceiptKind.CompanyCredit), tender = Tenders.CompanyCredit,
            amountPaise = amount.Value, businessDate }, ct).ConfigureAwait(false);
        return id;
    }
}
