namespace Hcc.Contracts.Stays;

// Wire contracts for the Stays module (contracts package §3). Dates are YYYY-MM-DD strings; versions are base64 rowversion.
// The actor always comes from the session, never from these bodies.

public sealed record GuestPartyDto(string Name, string AgeBand, string? Relation);

/// <summary>
/// Registration fields a receptionist may enter. <c>IdNumber</c> = the full ID number (DEC-068; write-only: validated per type,
/// encrypted at rest, never returned except by the audited RevealGuestId command). <c>IdLast4</c> is accepted alone only for
/// profiles from before DEC-068; with a number it is derived from it. Blank number on an update keeps the stored one.
/// </summary>
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
    IReadOnlyList<GuestPartyDto>? Party = null,
    string? IdNumber = null);

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

/// <summary>
/// Guest profile as the reader may see it (field masks applied server-side; <c>Masked</c> = ID restricted). Never carries the full ID
/// number: <c>IdMasked</c> = XXXX-XXXX-1234 for holders of the ID-view permission ("Restricted" otherwise), <c>IdOnFile</c> = a full
/// number is stored, <c>CanRevealId</c> = the reader may call RevealGuestId (audited).
/// </summary>
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
    string Version,
    string IdMasked = "",
    bool IdOnFile = false,
    bool CanRevealId = false,
    string? Preferences = null,
    string? Notes = null,
    string Origin = "manual");

/// <summary>
/// SaveGuestProfile (W4-1): create (<c>GuestId</c> null) or edit a guest profile without touching a reservation (prototype GUEST_SAVE with no
/// booking link). <c>GuestExpectedVersion</c> is required to edit. <c>Verified</c> = the front desk checked the ID.
/// </summary>
public sealed record SaveGuestProfileRequest(GuestDraftDto Guest, bool Verified = false, long? GuestId = null, string? GuestExpectedVersion = null);

public sealed record SaveGuestProfileResult(long GuestId, string GuestVersion, bool Verified, bool Created);

/// <summary>Page of GET /guests (field-masked like GET /guests/{id}).</summary>
public sealed record GuestListPage(int Total, int Page, int PageSize, IReadOnlyList<GuestView> Items);

/// <summary>Outbox payload of <c>StayDeparted</c> (consumed by housekeeping: exactly one checkout clean per stay).</summary>
public sealed record StayDepartedEvent(long StayId, long ReservationId, int PropertyId, int RoomId, string BusinessDate);

public static class StaysEvents
{
    public const string StayDeparted = "StayDeparted";
}
