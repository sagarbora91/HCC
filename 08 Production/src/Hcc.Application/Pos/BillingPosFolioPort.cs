using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;
using Hcc.Application.Tax;
using static Hcc.Application.Billing.BillingService;

namespace Hcc.Application.Pos;

/// <summary>
/// Billing's folio port for the POS (room posting): one <c>service</c> charge line on an in-house folio, with the tax snapshot, written through
/// <see cref="IBillingStore"/> inside the POS settle transaction (folio row locked, folio version touched so bound approvals go stale, DEC-041).
/// Like Billing's own lines it stores the per-charge GST record (category food, CGST / SGST, <c>tax.charge_gst</c>) and publishes
/// <c>ChargePosted</c>, the ONE event Accounting books a room-posted outlet bill from (it skips <c>PosBillSettled</c> with mode folio).
/// </summary>
public sealed class BillingPosFolioPort(IBillingStore store, IGstChargeSnapshots? gstSnapshots = null) : IPosFolioPort
{
    public async Task<PosFolioCharge> PostServiceChargeAsync(ICommandTransaction tx, int propertyId, long folioId, DateOnly businessDate, string label,
        Paise net, GstRate rate, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var f = await store.LockFolioAsync(tx, folioId, ct).ConfigureAwait(false);
        if (f is null || f.PropertyId != propertyId) throw new HccException(HccErrorCode.NOT_FOUND, "Folio not found.");
        if (f.ReservationStatus != "InHouse") throw Invalid("Only an in-house guest's folio can take a room charge.");
        ArgumentNullException.ThrowIfNull(rate);
        var line = new NewCharge(f.FolioId, ChargeKind.Service, businessDate, label, net.EnsureFieldRange(), rate.Bps, GstMath.LineTax(net, rate.Bps), null, null,
            tx.Context.PrincipalId, utc);
        var chargeId = await store.InsertChargeAsync(tx, line, ct).ConfigureAwait(false);
        if (gstSnapshots is not null)
        {
            var split = GstMath.Split(line.Tax);
            await gstSnapshots.RecordAsync(tx, new GstChargeSnapshot(chargeId, rate.Category, rate.Bps, rate.Itc, line.Tax, split.Cgst, split.Sgst, rate.Rule), ct).ConfigureAwait(false);
        }

        var version = await store.TouchFolioAsync(tx, f.FolioId, ct).ConfigureAwait(false);
        await tx.PublishAsync("ChargePosted", f.FolioId, new { folioId = f.FolioId, chargeId, kind = "service", serviceDate = businessDate, amountPaise = net.Value, taxPaise = line.Tax.Value }, ct).ConfigureAwait(false);
        return new PosFolioCharge(chargeId, version);
    }
}

/// <summary>Business-day close preflight over the POS store.</summary>
public sealed class PosCloseGate(IPosStore store, IPosTaxRates? taxRates = null) : IPosCloseGate
{
    public async Task<PosCloseList> ListAsync(int propertyId, DateOnly date, CancellationToken ct = default)
    {
        var bps = taxRates is null ? 0 : (await taxRates.FoodAsync(null, propertyId, date, ct).ConfigureAwait(false)).Bps;
        var open = (await store.OpenChecksAsync(propertyId, ct).ConfigureAwait(false)).Where(c => c.BusinessDate <= date).ToList();
        static string No(long id) => "POS-" + id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
        var checks = open.Select(c => new PosCloseItem(
            $"{c.Spot} · {No(c.OrderId)}{(c.Guest is null ? "" : " · " + c.Guest)} · {Hcc.Domain.Pos.PosRules.Totals([new Hcc.Domain.Pos.PosLineAmount(new Hcc.Domain.Money.Paise(c.NetPaise), 1)], bps).Total} open",
            "pos-check:" + c.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var voids = open.Where(c => c.PendingVoidId is not null).Select(c => new PosCloseItem(
            $"Void request · {c.Spot} · {No(c.OrderId)} · awaiting a different approver", "pos-void:" + c.PendingVoidId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        return new PosCloseList(checks, voids);
    }

    public Task<Contracts.Pos.PosCloseBlockers> BlockersAsync(int propertyId, CancellationToken ct = default) => store.BlockersAsync(propertyId, ct);
}
