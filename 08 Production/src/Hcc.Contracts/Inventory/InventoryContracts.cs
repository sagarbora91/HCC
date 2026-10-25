namespace Hcc.Contracts.Inventory;

/// <summary>PERM ids used by the inventory and reservation contracts (contracts §3, RBAC matrix §5).</summary>
public static class InventoryPermissions
{
    /// <summary>reservation.view — GetAvailability, reservation details.</summary>
    public const string ViewReservations = "PERM-RES-001";

    /// <summary>reservation.create / hold / confirm — QuoteStay, CreateReservation, ConfirmHold, group blocks.</summary>
    public const string CreateReservation = "PERM-RES-002";

    /// <summary>reservation.edit / change dates / move — AmendReservation.</summary>
    public const string EditReservation = "PERM-RES-003";

    /// <summary>reservation.cancel / no-show — CancelReservation, MarkNoShow.</summary>
    public const string CancelReservation = "PERM-RES-004";

    /// <summary>approval.decide — the approve permission that may override a stay restriction with a reason (DEC-053).</summary>
    public const string ApproveOverride = "PERM-APR-001";
}

/// <summary>Why a room cannot take a stay (contracts §3; strings as the prototype's <c>ResReason</c> plus GroupHold and RESTRICTED).</summary>
public static class AvailabilityReasons
{
    public const string Blocked = "Blocked";
    public const string OverCapacity = "Over capacity";
    public const string Occupied = "Occupied";
    public const string Booked = "Booked";
    public const string Held = "Held";
    public const string GroupHold = "GroupHold";
    public const string Restricted = "RESTRICTED";
}

/// <summary>A broken house stay restriction (A2.1): <c>MIN_STAY</c> or <c>CTA</c>, the restriction code and the user message.</summary>
public sealed record StayViolation(string Code, string Restriction, string Message);

/// <summary>GET /api/v1/availability query. <c>AsOf</c> (past business date, optional): out-of-service blocks as they stood on that date (room-block history).</summary>
public sealed record AvailabilityQuery(string Arrival, string Departure, int Adults, int Children = 0, int? RoomTypeId = null,
    string? RatePlanId = null, long? GroupBlockId = null, string? AsOf = null);

/// <param name="NightlyRatePaise">Tariff for the arrival night (BAR: dated tariff, else rack rate) — the rate a quote would start from (Wave 1 integration, P5 booking screens).</param>
public sealed record RoomAvailability(int RoomId, string RoomNo, int RoomTypeId, string RoomType, int Capacity, bool Available,
    IReadOnlyList<string> Reasons, string? HeldForBlock, long NightlyRatePaise);

public sealed record RoomTypeAvailability(int RoomTypeId, string Code, int MaxOccupancy, int AvailableRooms, IReadOnlyList<StayViolation> Violations);

public sealed record AvailabilityResult(string BusinessDate, string Arrival, string Departure, int Nights, int Party,
    IReadOnlyList<RoomTypeAvailability> Types, IReadOnlyList<RoomAvailability> Rooms);

/// <summary>POST /api/v1/commands/QuoteStay. Give a room or a room type; rate plan defaults to BAR.</summary>
public sealed record QuoteStayRequest(string Arrival, string Departure, int Adults, int Children = 0, int? RoomId = null,
    int? RoomTypeId = null, string? RatePlanId = null);

public sealed record QuoteNight(string Date, long RatePaise, int TaxBps, long TaxPaise);

public sealed record QuoteResult(long QuoteId, string Version, int RoomTypeId, int? RoomId, string Arrival, string Departure,
    int Adults, int Children, string RatePlanId, IReadOnlyList<QuoteNight> Nights, long NetPaise, long TaxPaise, long TotalPaise,
    string ExpiresAt, IReadOnlyList<StayViolation> Violations);

public sealed record GroupBlockAllotment(int RoomTypeId, int Rooms, long? RatePaise = null);

/// <summary>POST /api/v1/commands/CreateGroupBlock. Cut-off = last business date on which rooms may be picked up.</summary>
public sealed record CreateGroupBlockRequest(string Code, string Name, string Arrival, string Departure, string Cutoff,
    IReadOnlyList<GroupBlockAllotment> Allotments);

/// <summary>POST /api/v1/commands/ReleaseGroupBlock — releases every unpicked room and closes pickup.</summary>
public sealed record ReleaseGroupBlockRequest(long GroupBlockId, string ExpectedVersion);

public sealed record GroupBlockResult(long GroupBlockId, string Code, string Version, string Status);
