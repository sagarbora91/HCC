using Hcc.Application.Commands;
using Hcc.Application.Invoices;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Contracts.Invoices;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Application.Billing;

/// <summary>
/// F8-INT · CheckoutStay of a split stay (SF-CONTRACT §4 CHECKOUT, §5). <see cref="SettleSplitAsync"/> returns null for a single-folio
/// reservation (I1: the caller keeps today's path). For a split stay it maps every named credit approval to the folio it was requested on,
/// then settles every live folio through <see cref="BillingService.SettleSplitForCheckoutAsync"/> (sf-domain). After the reservation is
/// Departed, <see cref="IssueFolioInvoicesAsync"/> issues one tax invoice per non-zero folio in letter order, inside the checkout transaction,
/// through the existing <see cref="FolioInvoiceSource"/> (one invoice per folio_id), buyer = the folio payer (B2B with its GSTIN).
/// </summary>
public sealed class SplitCheckoutPort(ISplitFolioStore split, IBillingStore store, BillingService billing, InvoiceService invoices) : IStaySplitCheckoutPort
{
    public async Task<IReadOnlyList<SplitCheckoutFolio>?> SettleSplitAsync(ICommandTransaction tx, CheckoutFolioCheck check, IReadOnlyList<long>? creditApprovalIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(check);
        var stay = await split.LockStayAsync(tx, tx.Context.PropertyId, check.ReservationId, ct).ConfigureAwait(false);
        var live = stay?.Snapshot.Folios.Where(f => !f.Removed).OrderBy(f => f.Seq).ToList() ?? [];
        if (live.Count <= 1) return null;

        // Each credit approval named at checkout settles the folio it was requested on (company folio / guest-credit folio).
        var byFolio = new Dictionary<long, long>();
        var ids = (creditApprovalIds ?? []).Concat(check.CompanyCreditApprovalId is { } single ? [single] : []).Distinct();
        foreach (var id in ids)
        {
            var peek = await store.PeekApprovalAsync(tx, id, ct).ConfigureAwait(false);
            if (peek is null || peek.PropertyId != tx.Context.PropertyId || peek.FolioId is not { } fid || live.All(f => f.FolioId != fid))
                throw new HccException(HccErrorCode.NOT_FOUND, "Credit approval not found on this stay's folios.");
            if (!byFolio.TryAdd(fid, id)) throw HccException.Validation("Name one credit approval per folio at checkout.");
        }

        var caller = new Caller(tx.Context.PropertyId, tx.Context.PrincipalId);
        var settled = await billing.SettleSplitForCheckoutAsync(tx, caller,
            live.Select(f => (f.FolioId, f.Seq, byFolio.TryGetValue(f.FolioId, out var a) ? a : (long?)null)).ToList(), check.Arrival, check.Departure, ct).ConfigureAwait(false);
        return settled.Select(x =>
        {
            var f = live.First(l => l.FolioId == x.FolioId);
            return new SplitCheckoutFolio(x.FolioId, x.Seq, x.Total.Value, f.PayerName, f.PayerGstin);
        }).ToList();
    }

    public async Task<IReadOnlyList<string>> IssueFolioInvoicesAsync(ICommandTransaction tx, IReadOnlyList<SplitCheckoutFolio> folios, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(folios);
        // Without a valid hotel GSTIN no tax invoice can be numbered (same rule as IssueInvoice); the checkout itself is not stopped.
        if (await invoices.SupplierProblemAsync(tx, tx.Context.PropertyId, ct).ConfigureAwait(false) is not null) return [];
        var numbers = new List<string>();
        foreach (var f in folios.Where(f => f.TotalPaise != 0).OrderBy(f => f.Seq))
        {
            var b2b = !string.IsNullOrWhiteSpace(f.PayerGstin);
            var issued = await invoices.IssueInTransactionAsync(tx, new IssueInvoiceRequest(FolioInvoiceSource.FolioKind, f.FolioId,
                b2b ? "B2B" : null, b2b ? f.PayerName : null, b2b ? f.PayerGstin : null), ct).ConfigureAwait(false);
            numbers.Add(issued.InvoiceNo);
        }
        return numbers;
    }
}
