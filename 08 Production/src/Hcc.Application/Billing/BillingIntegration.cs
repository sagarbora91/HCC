using Hcc.Application.Commands;
using Hcc.Application.Reservations;
using Hcc.Application.Stays;
using Hcc.Contracts;
using Hcc.Domain.Billing;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Application.Billing;

/// <summary>
/// Wave 1 integration: P3's <see cref="IStayFolioPort"/> served by the billing module. Check-in creates (or finds) the
/// reservation's folio; checkout runs <see cref="BillingService.SettleForCheckoutAsync"/> in the CheckoutStay transaction, which
/// also executes an approved company credit (DEC-041) so the credit and the departure commit or roll back together.
/// The actor is the command's own context (session principal), never the request body.
/// </summary>
public sealed class BillingStayFolioPort(IBillingStore store, BillingService billing) : IStayFolioPort
{
    public Task<long> EnsureFolioAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        return store.EnsureFolioAsync(tx, reservationId, ct);
    }

    public Task AssertCheckoutSettledAsync(ICommandTransaction tx, CheckoutFolioCheck check, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(check);
        var caller = new Caller(tx.Context.PropertyId, tx.Context.PrincipalId);
        return billing.SettleForCheckoutAsync(tx, caller, check.FolioId, check.Arrival, check.Departure, check.CompanyCreditApprovalId, ct);
    }
}

/// <summary>
/// Wave 1 integration: P4's <see cref="IStayNightSource"/>. Stay → reservation + folio from P3's <c>gst.stay</c>; the agreed
/// nightly rate and tax snapshot from P2's <see cref="IAgreedNightRateSource"/> (<c>inv.reservation_night</c>, taken at booking
/// or amend). A reservation without night snapshots (loaded before P2) falls back to its booked nightly rate at the dated tax rate.
/// </summary>
public sealed class StayNightSource(IBillingStore store, IAgreedNightRateSource agreed, ITaxRateSource taxRates) : IStayNightSource
{
    public async Task<StayNight> ResolveAsync(ICommandTransaction tx, int propertyId, long stayId, DateOnly serviceDate, CancellationToken ct)
    {
        var stay = await store.StayFolioAsync(tx, propertyId, stayId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.NOT_FOUND, "Stay not found.");
        var night = await agreed.GetAsync(tx, propertyId, stay.ReservationId, serviceDate, ct).ConfigureAwait(false);
        if (night is not null) return new StayNight(stay.FolioId, new Paise(night.RatePaise), night.TaxBps);
        var rate = await store.ReservationNightlyRateAsync(tx, stay.ReservationId, ct).ConfigureAwait(false)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This stay has no agreed rate for that night. Amend the reservation to price it.");
        return new StayNight(stay.FolioId, rate, taxRates.TaxBpsOn(propertyId, serviceDate));
    }
}

/// <summary>
/// Wave 1 integration: the reservation cancel/no-show guard (prototype CANCEL) over the billing folio figures: net payments
/// (payments − refunds) or a held deposit block cancellation.
/// </summary>
public sealed class BillingReservationMoney(IBillingStore store) : IReservationMoneyCheck
{
    public async Task<bool> HasUnsettledMoneyAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        var folioId = await store.FolioIdForReservationAsync(tx, reservationId, ct).ConfigureAwait(false);
        if (folioId is not { } f) return false;
        var figures = FolioStatement.Compute(await store.LinesAsync(tx, f, ct).ConfigureAwait(false), await store.ReceiptsAsync(tx, f, ct).ConfigureAwait(false));
        return figures.Paid != Paise.Zero || figures.Deposit != Paise.Zero;
    }
}
