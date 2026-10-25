namespace Hcc.Contracts.Stays;

// Wire contracts for the Stays module (contracts package §3). Dates are YYYY-MM-DD strings; versions are base64 rowversion.
// The actor always comes from the session, never from these bodies.

public sealed record GuestPartyDto(string Name, string AgeBand, string? Relation);

/// <summary>Registration fields a receptionist may enter. <c>IdLast4</c> = last 4 characters of the ID only — never the full number.</summary>
public sealed record GuestDraftDto(
    string? Name,
    string? Phone,
    string? Email = null,
    string? Address = null,
    string? Nationality = null,
    string? IdType = null,
    string? IdLast4 = null,
    string? Company = null,
    string? Gstin = null,
    string? Preferences = null,
    string? Notes = null,
    IReadOnlyList<GuestPartyDto>? Party = null);

/// <summary>
/// RegisterGuest K V. <c>ExpectedVersion</c> = the reservation's version. With <c>Guest</c> the profile is created (or the
/// profile <c>GuestId</c> updated, guarded by <c>GuestExpectedVersion</c>); without it the reservation's linked profile must
/// already satisfy the registration requirements.
/// </summary>
public sealed record RegisterGuestRequest(long ReservationId, string ExpectedVersion, GuestDraftDto? Guest = null, long? GuestId = null, string? GuestExpectedVersion = null);

public sealed record RegisterGuestResult(long ReservationId, long GuestId, string GuestVersion, bool Verified);

/// <summary>CheckIn K V. <c>ExpectedVersion</c> = the reservation's version.</summary>
public sealed record CheckInRequest(long ReservationId, string ExpectedVersion);

public sealed record CheckInResult(long StayId, long ReservationId, long FolioId, int RoomId, string RoomNo, string BusinessDate, string StayVersion);

/// <summary>CheckoutStay K V. <c>ExpectedVersion</c> = the stay's version. Company credit is executed by Billing (P4).</summary>
public sealed record CheckoutStayRequest(long StayId, string ExpectedVersion, long? CompanyCreditApprovalId = null);

public sealed record CheckoutStayResult(long StayId, long ReservationId, int RoomId, string RoomNo, string RoomHk, string BusinessDate, long OutboxId, string StayVersion);

/// <summary>Guest profile as the reader may see it (field masks applied server-side; <c>Masked</c> = ID restricted).</summary>
public sealed record GuestView(
    long GuestId,
    string Name,
    string Phone,
    string? Email,
    string? Address,
    string Nationality,
    string? IdType,
    string? IdLast4,
    string? Company,
    string? Gstin,
    IReadOnlyList<GuestPartyDto> Party,
    bool Verified,
    bool Masked,
    string Version);

/// <summary>Outbox payload of <c>StayDeparted</c> (consumed by housekeeping: exactly one checkout clean per stay).</summary>
public sealed record StayDepartedEvent(long StayId, long ReservationId, int PropertyId, int RoomId, string BusinessDate);

public static class StaysEvents
{
    public const string StayDeparted = "StayDeparted";
}
