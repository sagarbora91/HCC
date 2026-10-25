namespace Hcc.Contracts.Reservations;

/// <summary>Reservation status on the wire (SQL <c>inv.reservation.status</c>; prototype "In house"/"No show" are InHouse/NoShow).</summary>
public static class ReservationStatuses
{
    public const string Held = "Held";
    public const string Expired = "Expired";
    public const string Confirmed = "Confirmed";
    public const string InHouse = "InHouse";
    public const string Departed = "Departed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
}

public sealed record GuestInput(string Name, string? Phone = null);

/// <summary>
/// POST /api/v1/commands/CreateReservation (Idempotency-Key). <c>GroupBlockId</c> is the explicit block context for a
/// group pickup (no global hold exemption). <c>RestrictionOverride</c> = reason; needs the approve permission (DEC-053).
/// </summary>
public sealed record CreateReservationRequest(long QuoteId, string QuoteVersion, GuestInput Guest, int RoomId, bool Hold = false,
    string? RestrictionOverride = null, long? GroupBlockId = null, string? Source = null, string? Notes = null);

public sealed record ReservationResult(long ReservationId, string PublicNo, string Version, string Status, string? HoldUntil);

/// <summary>Price decision when dates or room change: keep the agreed nightly rate, or requote (QuoteId + QuoteVersion).</summary>
public static class PriceDecisions
{
    public const string Keep = "keep";
    public const string Requote = "requote";
}

/// <summary>POST /api/v1/commands/AmendReservation (Idempotency-Key, expectedVersion). All-or-nothing re-allocation.</summary>
public sealed record AmendReservationRequest(long ReservationId, string ExpectedVersion, string Arrival, string Departure, int RoomId,
    string PriceDecision = PriceDecisions.Keep, long? QuoteId = null, string? QuoteVersion = null, string? RestrictionOverride = null);

/// <summary>POST /api/v1/commands/CancelReservation or /MarkNoShow (Idempotency-Key, expectedVersion).</summary>
public sealed record CancelReservationRequest(long ReservationId, string ExpectedVersion, string? Reason = null);

/// <summary>POST /api/v1/commands/ConfirmHold — Held → Confirmed while the hold is running.</summary>
public sealed record ConfirmHoldRequest(long ReservationId, string ExpectedVersion);

public sealed record ReservationNight(string Date, long RatePaise, int TaxBps, long TaxPaise);

/// <summary>GET /api/v1/reservations/{id}.</summary>
public sealed record ReservationDetails(long ReservationId, string PublicNo, string Version, string Status, string GuestName, string? Phone,
    int RoomId, string RoomNo, int RoomTypeId, string RoomType, string Arrival, string Departure, int Adults, int Children, string Source,
    long? NightlyRatePaise, string? HoldUntil, long? GroupBlockId, string? RestrictionOverride, string? CancelReason, string? Notes,
    IReadOnlyList<ReservationNight> Nights, long NetPaise, long TaxPaise, long TotalPaise);
