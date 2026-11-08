namespace Hcc.Contracts.Stays;

// Wire contracts for the Stays module (contracts package §3). Dates are YYYY-MM-DD strings; versions are base64 rowversion.
// The actor always comes from the session, never from these bodies.

/// <summary>
/// An accompanying guest. DEC-193: each member has their own ID. <c>IdNumber</c> is write-only (validated per type, sealed per member, never
/// returned); <c>IdLast4</c> / <c>IdMasked</c> (XXXX-XXXX-1234 for PERM-GST-003, "Restricted" otherwise) come back on reads; <c>IdRequired</c>
/// (read-only) = the age band needs an ID (Adult / Senior); <c>IdOnFile</c> (read-only) = a full number is stored for this member.
/// </summary>
public sealed record GuestPartyDto(string Name, string AgeBand, string? Relation, string? IdType = null, string? IdNumber = null, string? IdLast4 = null,
    string? IdMasked = null, bool IdRequired = false, bool IdOnFile = false);

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
    string? IdNumber = null,
    string? Dob = null);   // W1a LG: optional date of birth yyyy-MM-dd (DEC-162 Q4)

/// <summary>
/// RegisterGuest K V. <c>ExpectedVersion</c> = the reservation's version. With <c>Guest</c> the profile is created (or the
/// profile <c>GuestId</c> updated, guarded by <c>GuestExpectedVersion</c>); without it the reservation's linked profile must
/// already satisfy the registration requirements.
/// </summary>
public sealed record RegisterGuestRequest(long ReservationId, string ExpectedVersion, GuestDraftDto? Guest = null, long? GuestId = null, string? GuestExpectedVersion = null);

public sealed record RegisterGuestResult(long ReservationId, long GuestId, string GuestVersion, bool Verified);

/// <summary>CheckIn K V. <c>ExpectedVersion</c> = the reservation's version.</summary>
public sealed record CheckInRequest(long ReservationId, string ExpectedVersion, string? PurposeCode = null, string? VehicleNo = null, string? VehicleType = null,
    string? CheckoutBasis = null, FormCDto? FormC = null, bool Desk = false);   // LF A3–A6: Desk = a front-desk form (applies the Owner's "purpose required" setting, like the prototype's desk:true)

public sealed record CheckInResult(long StayId, long ReservationId, long FolioId, int RoomId, string RoomNo, string BusinessDate, string StayVersion);

/// <summary>CheckoutStay K V. <c>ExpectedVersion</c> = the stay's version. Company credit is executed by Billing (P4).</summary>
public sealed record CheckoutStayRequest(long StayId, string ExpectedVersion, long? CompanyCreditApprovalId = null, string? LateCharge = null, string? LateReason = null, bool Desk = false,
    IReadOnlyList<long>? CreditApprovalIds = null);   // F8-INT: split stay = the company / guest credit approvals of its folios (each settles the folio it was requested on)   // LF A6: Desk = a front-desk form (the late check-out rule applies to it, like the prototype)

public sealed record CheckoutStayResult(long StayId, long ReservationId, int RoomId, string RoomNo, string RoomHk, string BusinessDate, long OutboxId, string StayVersion,
    IReadOnlyList<string>? InvoiceNos = null); // F8-INT: split stay = the folio invoices issued at checkout in letter order (null on a single-folio stay)

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
    string Origin = "manual",
    string? Dob = null,              // W1a LG: yyyy-MM-dd; null when unknown or when the reader may not see contact details
    long? LegacyGuestId = null);     // W1a LG: the old program's GuestID that produced this profile

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

// ── LF lf-desk (LF-CONTRACT §8) ──

/// <summary>Form C of a foreign guest's stay (per stay). The passport NUMBER is the guest's ID number and is not part of it. Dates yyyy-MM-dd; Sex M / F / X.</summary>
public sealed record FormCDto(string PassportPlace, string PassportIssued, string PassportExpiry, string VisaNo, string VisaType, string VisaPlace, string VisaIssued, string VisaExpiry,
    string ArrivedFrom, string ArrivalInIndia, int IntendedStayDays, string NextDestination, bool EmployedInIndia, string? EmployerOrBusiness, string AddressAbroad, string? ContactAbroad, string Sex);

/// <summary>PostLateCheckoutCharge K V: <c>Charge</c> = half-day (posts the charge) or waived (needs PERM-APR-001 and a Reason). Posted before CheckoutStay so the folio can be settled.</summary>
public sealed record LateCheckoutChargeRequest(long StayId, string ExpectedVersion, string Charge, string? Reason = null);

public sealed record LateCheckoutChargeResult(long StayId, string Decision, long AmountPaise, string StayVersion);

/// <summary>One row of GET /stays/form-c?from&amp;to (PERM-DOC-004): the stay, its guest and the Form C; the visa number is masked unless the reader holds the ID-details right.</summary>
public sealed record FormCRegisterRow(long StayId, string Booking, string Room, string Guest, string Nationality, string Arrival, string Departure, string Status, string? Dob, FormCDto? FormC, bool Missing);

public sealed record FormCRegister(string From, string To, IReadOnlyList<FormCRegisterRow> Rows);

/// <summary>SaveStayFormC K: store or replace the Form C of an in-house stay (the check-in request carries it too).</summary>
public sealed record SaveStayFormCRequest(long StayId, FormCDto FormC);
