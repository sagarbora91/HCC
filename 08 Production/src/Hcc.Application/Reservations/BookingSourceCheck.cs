using Hcc.Application.Commands;

namespace Hcc.Application.Reservations;

/// <summary>
/// MA-11 (MA-5 gap): CreateReservation refuses a booking source whose row in the Booking sources master (mst.booking_source) is retired
/// (MASTER_RETIRED), as the prototype BOOK does. A source text that matches no row (a new OTA channel, a free-text source) and an empty list
/// are allowed — the master registers OTA channels and companies itself. Runs inside the booking transaction.
/// </summary>
public interface IBookingSourceCheck
{
    Task EnsureUsableAsync(ICommandTransaction tx, int propertyId, string source, CancellationToken ct = default);
}
