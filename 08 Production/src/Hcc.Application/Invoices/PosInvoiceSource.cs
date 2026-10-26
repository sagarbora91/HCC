using System.Globalization;
using Hcc.Application.Commands;
using Hcc.Application.Pos;
using Hcc.Contracts;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Tax;

namespace Hcc.Application.Invoices;

/// <summary>
/// DEC-072 (audit A03a-01): a settled outlet POS bill — walk-in restaurant, or room service paid at the outlet — as an invoice source, over
/// the POS store's public port only (<see cref="IPosStore"/>; no POS table is written). Source id = <c>pos.bill.bill_id</c>; series RST
/// (<c>RST/&lt;FY&gt;/&lt;7 digits&gt;</c>, own gap-free series per FY). The invoice carries ONE line, the bill as settled: taxable = the bill net, the
/// bill's rate, tax and stored CGST / SGST (migration 1201) — never recomputed, because the POS taxes the bill net once (PosRules.Totals) and
/// per-item tax could differ by a paisa; the items are listed in the line description. Category food → SAC 996331 (restaurant service).
/// A bill charged to a room folio (mode folio) is refused: its folio line is invoiced on the guest's room invoice (INV) after checkout.
/// A credit note is never requested for a POS invoice: voiding the bill (same business day) issues it, and Accounting already reverses the
/// sale from PosBillVoided, so <see cref="PostCreditAsync"/> books nothing (the invoice and credit note are documents, not vouchers).
/// Lock order: the check (order) row first, as every POS command.
/// </summary>
public sealed class PosInvoiceSource(IPosStore pos) : IInvoiceSource
{
    public const string PosKind = "pos";

    public string Kind => PosKind;

    public bool CreditNoteRequests => false;

    public string CreditNoteRefusal =>
        "A restaurant invoice is credited by voiding its POS bill on the day it was settled (Restaurant › the bill › Void bill); the credit note is issued with the void.";

    private static HccException NotFound() => new(HccErrorCode.NOT_FOUND, "POS bill not found.");

    private async Task<(OrderRow Order, BillRow Bill)> LockBillAsync(ICommandTransaction tx, int propertyId, long billId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var peek = await pos.BillByIdAsync(tx, propertyId, billId, ct).ConfigureAwait(false) ?? throw NotFound();
        var order = await pos.LockOrderAsync(tx, propertyId, peek.OrderId, ct).ConfigureAwait(false) ?? throw NotFound();
        var bill = await pos.BillForOrderAsync(tx, order.OrderId, ct).ConfigureAwait(false) ?? throw NotFound();
        return (order, bill);
    }

    public async Task LockAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct) =>
        _ = await LockBillAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false);

    public async Task<InvoiceSourceDocument> LoadForIssueAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct)
    {
        var (o, b) = await LockBillAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false);
        if (b.Mode != "outlet")
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "This bill was charged to a room folio: it is on the guest's room tax invoice after checkout, not in the restaurant series.");
        if (b.Status != "Settled") throw new HccException(HccErrorCode.INVALID_TRANSITION, "This POS bill was voided; there is nothing to invoice.");
        var items = (await pos.LinesAsync(tx, o.OrderId, ct).ConfigureAwait(false)).Where(l => !l.Voided)
            .Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Name} ×{l.Qty}"));
        var spot = o.TableNo is not null ? "Table " + o.TableNo : "Room " + o.RoomNo;
        var description = $"Restaurant service · {o.OutletName} · {spot} · {string.Join(", ", items)}";
        if (description.Length > 200) description = description[..199] + "…";
        var line = new SourceLine(b.BillId, "restaurant", b.BusinessDate, description, GstCategory.Food, b.Net, b.TaxBps, b.Tax, b.Cgst, b.Sgst);
        return new InvoiceSourceDocument(PosKind, b.BillId, b.BillNo, o.Guest, null, null, [line], InvoiceDocType.RestaurantInvoice);
    }

    public async Task<IReadOnlyList<long?>> PostCreditAsync(ICommandTransaction tx, int propertyId, long sourceId, string creditNoteNo, string reason, DateOnly date,
        IReadOnlyList<CreditPosting> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var (_, b) = await LockBillAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false);
        // Only the void reaches here (requests are refused): the bill is already void and PosBillVoided reverses the sale in the books.
        if (b.Status != "Void") throw new InvalidOperationException("A POS invoice is credited only when its bill is voided.");
        return lines.Select(_ => (long?)null).ToList();
    }
}

/// <summary>
/// DEC-072: the POS side's way into the invoice-issue path (<see cref="IPosInvoicePort"/>), inside the settling or voiding command's
/// transaction: same numbering, document, audit and outbox as IssueInvoice / credit notes. The settle needs no PERM-INV-002 of its own —
/// the invoice is part of the settlement the caller was allowed to make (PERM-PAY-001), as the folio port posts a charge for the settle.
/// </summary>
public sealed class PosInvoicePort(InvoiceService invoices) : IPosInvoicePort
{
    public async Task<PosInvoiceOutcome> IssueAsync(ICommandTransaction tx, long billId, PosBuyer buyer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(buyer);
        // As at checkout (W4 D1): no tax invoice is numbered without a valid hotel GSTIN, and the sale itself is not refused. The bill keeps
        // no RST number; IssueInvoice {sourceKind: pos, sourceId: billId} issues it once the GSTIN is recorded.
        if (await invoices.SupplierProblemAsync(tx, tx.Context.PropertyId, ct).ConfigureAwait(false) is { } problem)
        {
            await tx.AuditAsync("POS_INVOICE_NOT_ISSUED", "pos-bill:" + billId.ToString(CultureInfo.InvariantCulture), problem, ct).ConfigureAwait(false);
            return new PosInvoiceOutcome(null, null, problem);
        }

        var b2b = buyer.Gstin is not null;
        var r = await invoices.IssueInTransactionAsync(tx, new IssueInvoiceRequest(PosInvoiceSource.PosKind, billId, b2b ? "B2B" : "B2C", buyer.Name, buyer.Gstin,
            buyer.Address), ct).ConfigureAwait(false);
        return new PosInvoiceOutcome(r.InvoiceId, r.InvoiceNo, null);
    }

    public async Task<string?> CreditVoidAsync(ICommandTransaction tx, long billId, int requestedBy, string reason, CancellationToken ct) =>
        (await invoices.CreditOnSourceReversalAsync(tx, PosInvoiceSource.PosKind, billId, requestedBy, reason, ct).ConfigureAwait(false))?.CreditNoteNo;
}
