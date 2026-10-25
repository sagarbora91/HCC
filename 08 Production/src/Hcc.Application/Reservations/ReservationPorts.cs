using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Inventory;

namespace Hcc.Application.Reservations;

public sealed record ReservationRecord(long ReservationId, string PublicNo, string Status, string GuestName, int RoomId, int RoomTypeId,
    DateOnly Arrival, DateOnly Departure, int Adults, int Children, DateTime? HoldUntilUtc, long? NightlyRatePaise, long? GroupBlockId,
    string? Notes, byte[] RowVersion)
{
    public StayAllocation Allocation => new(RoomId, RoomTypeId, Arrival, Departure, GroupBlockId);
}

public sealed record NewReservation(int PropertyId, int PrincipalId, string GuestName, string? Phone, StayAllocation Allocation,
    int Adults, int Children, string Status, DateTime? HoldUntilUtc, string Source, long? QuoteId, IReadOnlyList<PricedNight> Nights,
    string? RestrictionOverride, string? Notes, DateTime NowUtc);

public sealed record ReservationChange(StayAllocation Allocation, long? NightlyRatePaise, long? QuoteId, IReadOnlyList<PricedNight> Nights,
    string? Notes);

public sealed record SavedReservation(long ReservationId, string PublicNo, byte[] RowVersion);

/// <summary>Reservation persistence (SQL adapter in Hcc.Infrastructure.SqlServer/Reservations). Writers take the command transaction.</summary>
public interface IReservationStore
{
    /// <summary>Reads the reservation under an update lock (serializes competing commands on the same reservation).</summary>
    Task<ReservationRecord?> GetForUpdateAsync(ICommandTransaction tx, int propertyId, long reservationId, CancellationToken ct = default);

    /// <summary>Inserts the reservation, its nightly price snapshot, and allocates the room nights (via <see cref="IInventoryAllocator"/>).</summary>
    Task<SavedReservation> InsertAsync(ICommandTransaction tx, NewReservation reservation, CancellationToken ct = default);

    /// <summary>Re-allocates (amend) and replaces the nightly snapshot; version-checked against <paramref name="current"/>.</summary>
    Task<SavedReservation> ChangeAsync(ICommandTransaction tx, int propertyId, ReservationRecord current, ReservationChange change, CancellationToken ct = default);

    /// <summary>Sets a new status; Cancelled/NoShow/Expired release the room nights (and return a group pickup to an open block).</summary>
    Task<SavedReservation> SetStatusAsync(ICommandTransaction tx, int propertyId, ReservationRecord current, string status, string? reason,
        DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Net payments (payment − refund) or a deposit balance on the reservation's folio, if any (prototype CANCEL guard).
    /// Reads 0002 <c>bill.folio</c>/<c>pay.receipt</c> only; P4 may replace it with its folio service.
    /// </summary>
    Task<bool> HasUnsettledMoneyAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default);

    Task<ReservationDetails?> GetDetailsAsync(int propertyId, long reservationId, DateTime nowUtc, CancellationToken ct = default);
}
