namespace Hcc.Domain.FrontDeskMasters;

/// <summary>MA-11: one booking-source row (mst.booking_source) as the CreateReservation check reads it.</summary>
public sealed record BookingSourceRow(string Code, string Name, string Segment, string? Channel, bool Active);
