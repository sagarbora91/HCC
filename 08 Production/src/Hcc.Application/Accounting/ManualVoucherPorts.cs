using System.Globalization;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;

namespace Hcc.Application.Accounting;

// ACC-2 integration · the manual-voucher API (acc2-manual, contract §2) behind the ports the gst lane (IGstManualVouchers: GST payment,
// statutory remittance) and the tds lane (ITdsRemittancePoster: TDS remittance) wrote against. Each saves its template voucher inside the
// caller's own command transaction through ManualVoucherService.SaveInTransactionAsync (every §2 guard, the cash guard, Owner approved at once,
// Accountant pending). The tender line is posted on the party the payment method settles to (1030/<bank>): a control ledger alone is refused.

/// <summary>Both ports over <see cref="ManualVoucherService"/> (registered in AddAccountingManualModule).</summary>
public sealed class ManualVoucherPorts(ManualVoucherService manual, IManualVoucherStore store) : IGstManualVouchers, ITdsRemittancePoster
{
    private async Task<string> TenderAsync(ICommandTransaction tx, int propertyId, string account, string? method, CancellationToken ct)
    {
        if (PartyLedgers.IsParty(account) || string.IsNullOrWhiteSpace(method) || account is not ("1000" or "1030")) return account;
        var party = await store.TenderPartyAsync(tx, propertyId, method, ct).ConfigureAwait(false);
        return party is null ? account : account + "/" + party;
    }

    public async Task<GstManualVoucherRef> SaveAsync(ICommandTransaction tx, Caller caller, GstManualVoucherDraft draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(draft);
        var lines = new List<ManualLineRequest>();
        foreach (var (account, dr, cr) in draft.Lines)
            lines.Add(new ManualLineRequest(await TenderAsync(tx, caller.PropertyId, account, draft.Tender, ct).ConfigureAwait(false), dr, cr));
        var r = await manual.SaveInTransactionAsync(tx, caller, new SaveManualVoucherRequest(null, draft.Date, draft.Type, draft.Narration, draft.Reference, draft.Template, lines), ct)
            .ConfigureAwait(false);
        return new GstManualVoucherRef(r.VoucherId.ToString(CultureInfo.InvariantCulture), r.Status);
    }

    public async Task<int> PendingAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct)
        => (await store.ListAsync(tx, propertyId, ct).ConfigureAwait(false)).Count(v => v.Status == ManualVoucherRules.Pending && v.Date >= from && v.Date <= to);

    public async Task<long> PaidForAsync(ICommandTransaction tx, int propertyId, string month, CancellationToken ct)
        => (await store.ListAsync(tx, propertyId, ct).ConfigureAwait(false))
            .Where(v => v.Template == "gst-payment" && v.Status is ManualVoucherRules.Approved or ManualVoucherRules.Pending && v.VoidOf is null && v.VoidedBy is null
                && v.Narration.StartsWith("GST paid for " + month + " ", StringComparison.Ordinal))
            .Sum(v => v.Lines.Where(l => l.Account == GstSetOff.LedgerCash).Sum(l => l.Debit));

    public async Task<string?> PostAsync(ICommandTransaction tx, Caller caller, TdsRemittanceVoucher voucher, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(voucher);
        var bank = await TenderAsync(tx, caller.PropertyId, "1030", voucher.Tender, ct).ConfigureAwait(false);
        var r = await manual.SaveInTransactionAsync(tx, caller, new SaveManualVoucherRequest(null, voucher.Date, "Payment", voucher.Narration, voucher.Reference, voucher.Template,
            [new ManualLineRequest(voucher.PayableLedger, voucher.AmountPaise, 0), new ManualLineRequest(bank, 0, voucher.AmountPaise)]), ct).ConfigureAwait(false);
        return r.VoucherId.ToString(CultureInfo.InvariantCulture);
    }
}
