using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.Application.Invoices;

/// <summary>
/// The stay folio as an invoice source, over Billing's public ports only (<see cref="IBillingStore"/>, <see cref="IGstChargeSnapshots"/>):
/// no Billing file is changed. Issue needs a checked-out (Departed) folio with balance 0 — the prototype allocated the number at checkout,
/// which required exactly that. A credit note posts one negative folio line per credit line (kind correction, at the invoice line's
/// stored rate, CGST/SGST snapshot in <c>tax.charge_gst</c>) and publishes Billing's <c>ChargePosted</c> event, the hook Accounting already
/// consumes (contra revenue "line:adjustment", 4910 Refunds, reversals and credit notes). The folio balance turns into a credit the
/// guest can be refunded through Billing's refund flow (prototype "A settled departed stay requires a credit note before a refund").
/// </summary>
public sealed class FolioInvoiceSource(IBillingStore billing, IGstChargeSnapshots gst, IInvoiceStore store, TimeProvider clock) : IInvoiceSource
{
    public const string FolioKind = "folio";

    public string Kind => FolioKind;

    private async Task<FolioHeader> LockFolioAsync(ICommandTransaction tx, int propertyId, long folioId, CancellationToken ct)
    {
        var f = await billing.LockFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        if (f is null || f.PropertyId != propertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        return f;
    }

    public Task LockAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct) => LockFolioAsync(tx, propertyId, sourceId, ct);

    public async Task<InvoiceSourceDocument> LoadForIssueAsync(ICommandTransaction tx, int propertyId, long sourceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var f = await LockFolioAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false);
        if (f.ReservationStatus != "Departed")
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Check the guest out first: the tax invoice is issued for a settled, checked-out folio.");
        var lines = await billing.LinesAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        var figures = FolioStatement.Compute(lines, await billing.ReceiptsAsync(tx, f.FolioId, ct).ConfigureAwait(false));
        if (figures.Balance != Paise.Zero) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Settle the folio exactly before issuing the tax invoice.");
        var party = await store.FolioPartyAsync(tx, f.FolioId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        var source = new List<SourceLine>(lines.Count);
        foreach (var l in lines)
        {
            var snap = await gst.FindAsync(tx, l.ChargeId, ct).ConfigureAwait(false);
            var category = snap?.Category ?? (l.Kind is ChargeKind.Room or ChargeKind.Discount ? GstCategory.Room : GstCategories.OfLabel(l.Label));
            var description = l.Kind == ChargeKind.Room && party.RoomNo is { } room ? $"{l.Label} · Room {room}" : l.Label;
            source.Add(new SourceLine(l.ChargeId, BillingCodes.Code(l.Kind), l.ServiceDate, description, category, l.Amount, l.TaxBps, l.Tax, snap?.Cgst, snap?.Sgst));
        }

        return new InvoiceSourceDocument(FolioKind, f.FolioId, party.PublicNo, party.GuestName, party.Arrival, party.Departure, source);
    }

    public async Task<IReadOnlyList<long?>> PostCreditAsync(ICommandTransaction tx, int propertyId, long sourceId, string creditNoteNo, string reason, DateOnly date,
        IReadOnlyList<CreditPosting> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(lines);
        var f = await LockFolioAsync(tx, propertyId, sourceId, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = new List<long?>(lines.Count);
        foreach (var c in lines)
        {
            var label = $"Credit note {creditNoteNo} · {reason}";
            if (label.Length > 200) label = label[..200];
            var amount = -c.Taxable;
            var tax = -c.Tax;
            if (MoneyMath.TaxOf(amount, c.Bps) != tax) throw new InvalidOperationException("Credit posting tax does not follow the line rate.");
            var id = await billing.InsertChargeAsync(tx, new NewCharge(f.FolioId, ChargeKind.Correction, date, label, amount, c.Bps, tax, null, null, tx.Context.PrincipalId, now), ct).ConfigureAwait(false);
            var original = c.SourceLineId is { } sid ? await gst.FindAsync(tx, sid, ct).ConfigureAwait(false) : null;
            var split = GstMath.Split(tax);
            await gst.RecordAsync(tx, new GstChargeSnapshot(id, c.Category, c.Bps, original?.Itc ?? false, tax, split.Cgst, split.Sgst,
                "Credit note " + creditNoteNo + (c.SourceLineId is { } s2 ? " · charge " + s2.ToString(CultureInfo.InvariantCulture) : "")), ct).ConfigureAwait(false);
            await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId = id, kind = "correction", serviceDate = date, amountPaise = amount.Value, taxPaise = tax.Value }, ct).ConfigureAwait(false);
            ids.Add(id);
        }

        await billing.TouchFolioAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        return ids;
    }
}
