using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using static Hcc.Application.Billing.BillingService;

namespace Hcc.Application.Pos;

/// <summary>
/// Billing's folio port for the POS (room posting): one <c>service</c> charge line on an in-house folio, with the tax snapshot, written through
/// <see cref="IBillingStore"/> inside the POS settle transaction (folio row locked, folio version touched so bound approvals go stale, DEC-041).
/// </summary>
public sealed class BillingPosFolioPort(IBillingStore store) : IPosFolioPort
{
    public async Task<PosFolioCharge> PostServiceChargeAsync(ICommandTransaction tx, int propertyId, long folioId, DateOnly businessDate, string label,
        Paise net, int taxBps, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var f = await store.LockFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        if (f is null || f.PropertyId != propertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        if (f.ReservationStatus != "InHouse") throw Invalid("Only an in-house guest's folio can take a room charge.");
        var line = new NewCharge(f.FolioId, ChargeKind.Service, businessDate, label, net.EnsureFieldRange(), taxBps, MoneyMath.TaxOf(net, taxBps), null, null,
            tx.Context.PrincipalId, utc);
        var chargeId = await store.InsertChargeAsync(tx, line, ct).ConfigureAwait(false);
        var version = await store.TouchFolioAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        return new PosFolioCharge(chargeId, version);
    }
}

/// <summary>Business-day close preflight over the POS store.</summary>
public sealed class PosCloseGate(IPosStore store) : IPosCloseGate
{
    public Task<Contracts.Pos.PosCloseBlockers> BlockersAsync(int propertyId, CancellationToken ct = default) => store.BlockersAsync(propertyId, ct);
}
