namespace Hcc.Contracts.WebBooking;

/// <summary>PERM ids of the booking-website module (migration 2001).</summary>
public static class WebBookingPermissions
{
    /// <summary>webbooking.review — queue, confirm, decline, deposit link (Owner, Manager, Reception).</summary>
    public const string Review = "PERM-WEB-001";
}

/// <summary>Web request status on the wire. <c>Confirming</c> = a staff member has claimed it and is creating the reservation.</summary>
public static class WebRequestStatuses
{
    public const string Pending = "Pending";
    public const string Confirming = "Confirming";
    public const string Confirmed = "Confirmed";
    public const string Declined = "Declined";
}

/// <summary>Deposit link status on the wire (<c>Expired</c> is derived from the expiry time).</summary>
public static class DepositLinkStatuses
{
    public const string Open = "Open";
    public const string Paid = "Paid";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
}

// ── Public (anonymous) surface: nothing here carries staff data, staff ids, folio or reservation numbers ──

public sealed record PublicQuoteNight(string Date, long RatePaise, int TaxBps, long TaxPaise);

/// <param name="Bookable">False when sold out, over capacity or a house restriction applies; <c>Reason</c> says why (never which room or guest).</param>
public sealed record PublicRoomTypeQuote(int RoomTypeId, string RoomType, int MaxOccupancy, int RoomsLeft, bool Bookable, string? Reason,
    long NightlyRatePaise, int Nights, long NetPaise, long TaxPaise, long TotalPaise, IReadOnlyList<PublicQuoteNight> NightRates);

/// <summary>GET /api/v1/public/booking/quote?arrival&amp;departure&amp;adults&amp;children[&amp;roomType].</summary>
public sealed record PublicQuoteResult(string Hotel, string Arrival, string Departure, int Nights, int Adults, int Children,
    IReadOnlyList<PublicRoomTypeQuote> RoomTypes, string Note);

/// <summary>POST /api/v1/public/booking/requests (Idempotency-Key). <c>Website</c> is a honeypot field: real visitors leave it empty.</summary>
public sealed record SubmitWebRequest(string RoomType, string Arrival, string Departure, int Adults, int Children, string Guest, string Phone,
    string? Email = null, string? Notes = null, string? Website = null);

/// <summary>What the guest sees after submitting: a reference and the quote; nothing is held and no payment is taken.</summary>
public sealed record WebRequestReceipt(string RequestNo, string Status, string RoomType, string Arrival, string Departure, int Adults, int Children,
    long NetPaise, long TaxPaise, long TotalPaise, string Message);

/// <summary>GET /api/v1/public/booking/deposit/{token} — what the simulated payment page shows.</summary>
public sealed record PublicDepositView(string Hotel, string RequestNo, string Status, long AmountPaise, string ExpiresAt, string Arrival,
    string Departure, string RoomType);

// ── Staff surface ──

/// <summary>Row of the staff queue. Phone is masked in lists; the single-request view gives staff the full contact.</summary>
public sealed record WebRequestSummary(long RequestId, string RequestNo, string Status, string Guest, string PhoneMasked, string RoomType, string Arrival,
    string Departure, int Adults, int Children, long TotalPaise, string CreatedAt, string? ReservationNo, string? Reason, string Version);

public sealed record DepositLinkView(long LinkId, string Status, long AmountPaise, string ExpiresAt, string? PaidAt, string? PaidRef, long? ReceiptId);

public sealed record WebRequestDetail(long RequestId, string RequestNo, string Status, string Guest, string Phone, string? Email, string? Notes, int RoomTypeId,
    string RoomType, string Arrival, string Departure, int Adults, int Children, long NetPaise, long TaxPaise, long TotalPaise,
    IReadOnlyList<PublicQuoteNight> QuotedNights, string CreatedAt, long? ReservationId, string? ReservationNo, string? Reason, int? DecidedBy,
    string Version, IReadOnlyList<DepositLinkView> DepositLinks);

/// <summary>GET /api/v1/web-booking/requests?status=…</summary>
public sealed record WebRequestList(IReadOnlyList<WebRequestSummary> Requests, int Pending);

/// <summary>POST /api/v1/commands/ConfirmWebRequest (Idempotency-Key).</summary>
public sealed record ConfirmWebRequestRequest(long RequestId);

/// <param name="PriceChanged">The tariff or tax rate moved since the guest's quote; the reservation carries today's agreed price.</param>
public sealed record ConfirmWebRequestResult(long RequestId, string RequestNo, string Status, long ReservationId, string ReservationNo, string ReservationVersion,
    long QuotedTotalPaise, long BookedTotalPaise, bool PriceChanged);

/// <summary>POST /api/v1/commands/DeclineWebRequest (Idempotency-Key). Reason is required (3–200 characters).</summary>
public sealed record DeclineWebRequestRequest(long RequestId, string Reason);

public sealed record WebRequestDecision(long RequestId, string RequestNo, string Status, string Version);

/// <summary>POST /api/v1/commands/CreateWebDepositLink (Idempotency-Key). Amount defaults to the configured share of the total.</summary>
public sealed record CreateWebDepositLinkRequest(long RequestId, long? AmountPaise = null);

/// <summary>The token is returned ONCE here; only its hash is stored. <c>Url</c> is the simulated payment page path.</summary>
public sealed record WebDepositLinkResult(long LinkId, long RequestId, string Token, string Url, long AmountPaise, string ExpiresAt, string Status);

/// <summary>POST /api/v1/commands/MarkWebDepositPaid (Idempotency-Key) — the simulated gateway callback, entered by staff.</summary>
public sealed record MarkWebDepositPaidRequest(string Token, string GatewayRef);

public sealed record WebDepositPaid(long LinkId, long RequestId, long ReceiptId, long FolioId, long AmountPaise, string Status);
