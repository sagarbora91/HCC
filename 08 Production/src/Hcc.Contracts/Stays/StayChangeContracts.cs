namespace Hcc.Contracts.Stays;

// Wire contracts for changes to an in-house stay (W3-7): room move, extend, shorten, early departure. Dates are YYYY-MM-DD;
// versions are base64 rowversion; money is integer paise. The actor always comes from the session.

/// <summary>Rate handling of a change: <c>keep</c> (default: the agreed nightly tariff is preserved, prototype MOVE) or <c>change</c> to a new nightly rate for the un-posted nights.</summary>
public static class RateDecisions
{
    public const string Keep = "keep";
    public const string Change = "change";
}

/// <summary>
/// MoveRoom K V (PERM-STY-002). <c>ExpectedVersion</c> = the stay's version. The target room (same or different type) must be vacant,
/// ready, unblocked and free for the rest of the stay. A rate <c>change</c> below the agreed tariff gives revenue up and needs
/// the approval permission (Manager within its limit, Owner).
/// </summary>
public sealed record MoveRoomRequest(long StayId, string ExpectedVersion, int ToRoomId, string Reason, string? RateDecision = null, long? NewNightlyRatePaise = null);

public sealed record MoveRoomResult(long StayId, long ReservationId, int FromRoomId, string FromRoomNo, int ToRoomId, string ToRoomNo, string FromRoomHk,
    long HousekeepingTaskId, string BusinessDate, long NightlyRatePaise, long RateLossPaise, string StayVersion, long OutboxId);

/// <summary>ExtendStay K V (PERM-STY-002): a later departure; the extra nights need availability (allocator).</summary>
public sealed record ExtendStayRequest(long StayId, string ExpectedVersion, string NewDeparture, string? Reason = null, string? RateDecision = null, long? NewNightlyRatePaise = null);

/// <summary>ShortenStay K V (PERM-STY-002): an earlier departure after tonight; nights already posted cannot be released.</summary>
public sealed record ShortenStayRequest(long StayId, string ExpectedVersion, string NewDeparture, string Reason);

/// <summary>EarlyDeparture K V (PERM-STY-002): the guest leaves today; the remaining nights are released. CheckoutStay follows.</summary>
public sealed record EarlyDepartureRequest(long StayId, string ExpectedVersion, string Reason);

/// <summary>Result of ExtendStay / ShortenStay / EarlyDeparture. <c>NightsChanged</c> = nights added (extend) or released (shorten, early).</summary>
public sealed record ChangeStayDatesResult(long StayId, long ReservationId, int RoomId, string RoomNo, string OldDeparture, string NewDeparture,
    int NightsChanged, long NightlyRatePaise, long RateLossPaise, string BusinessDate, string StayVersion, long OutboxId);

/// <summary>Outbox payloads (consumed by messaging, housekeeping, reports).</summary>
public sealed record StayRoomMovedEvent(long StayId, long ReservationId, int PropertyId, int FromRoomId, int ToRoomId, string BusinessDate);

public sealed record StayDatesChangedEvent(long StayId, long ReservationId, int PropertyId, int RoomId, string Kind, string OldDeparture, string NewDeparture, string BusinessDate);

public static class StayChangeEvents
{
    public const string StayRoomMoved = "StayRoomMoved";
    public const string StayExtended = "StayExtended";
    public const string StayShortened = "StayShortened";
}
