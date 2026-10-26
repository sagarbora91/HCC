namespace Hcc.Contracts.Billing;

// Wire contracts for billing and cashier (contracts package §3). Money = integer paise; dates YYYY-MM-DD; versions = base64 rowversion.

/// <summary>POST /api/v1/commands/PostRoomNight (K). One canonical room charge per folio and service date; a repeat returns it.</summary>
public sealed record PostRoomNightRequest(long StayId, DateOnly ServiceDate);

/// <summary>
/// POST /api/v1/commands/PostCharge (K). Service charge on today's business date at the GST master rate in force (DEC-069).
/// <c>GstCategory</c> = food | laundry | banquet | other; when omitted it is guessed from the label (prototype gstCategoryOfLabel).
/// </summary>
public sealed record PostChargeRequest(long FolioId, string Label, long AmountPaise, string? GstCategory = null);

public sealed record ChargeResult(long ChargeId, long FolioId, string Kind, DateOnly ServiceDate, string Label, long AmountPaise, int TaxBps,
    long TaxPaise, bool Created, string FolioVersion);

/// <summary>
/// POST /api/v1/commands/RecordReceipt (K). Kind payment | deposit | deposit-return; cash needs an open shift on <c>TillId</c>.
/// Advance before check-in: a Confirmed booking has no folio until check-in, so send <c>FolioId</c> 0 (or omit it) with
/// <c>ReservationId</c>; the reservation's folio is opened in the same transaction (it is the one check-in then finds) and the
/// receipt is recorded on it. With a <c>FolioId</c> the reservation id is ignored.
/// </summary>
public sealed record RecordReceiptRequest(long FolioId, string Kind, string Tender, long AmountPaise, string? Reference, int? TillId, long? ReservationId = null);

public sealed record ReceiptResult(long ReceiptId, long FolioId, string Kind, string Tender, long AmountPaise, DateOnly BusinessDate, long? ShiftId,
    long? RefundOf, string FolioVersion);

/// <summary>
/// POST /api/v1/commands/RequestAdjustment (K V). Action discount | refund | credit | correction. <c>TargetId</c> = receipt (refund)
/// or charge (correction); <c>ExpectedVersion</c> = the folio version the request is bound to (DEC-041).
/// </summary>
public sealed record RequestAdjustmentRequest(string Action, long FolioId, long? TargetId, long AmountPaise, string Reason, string ExpectedVersion);

/// <summary>Outcome Posted (a discount within the requester's limit) or PendingApproval.</summary>
public sealed record AdjustmentResult(string Outcome, long? ApprovalId, string? ApprovalVersion, long? ChargeId, string FolioVersion);

/// <summary>POST /api/v1/commands/DecideApproval (K V). <c>ExpectedVersion</c> = the approval's version.</summary>
public sealed record DecideApprovalRequest(long ApprovalId, bool Approve, string? Reason, string ExpectedVersion);

/// <summary>POST /api/v1/commands/ExecuteAdjustment (K V): an approved refund (tender/till for cash) or company credit.</summary>
public sealed record ExecuteAdjustmentRequest(long ApprovalId, string ExpectedVersion, string? Tender, string? Reference, int? TillId);

public sealed record ApprovalResult(long ApprovalId, string Action, string Status, long AmountPaise, string Version, long? ResultId, string? FolioVersion,
    string? ShiftVersion);

/// <summary>POST /api/v1/commands/CreateTill (K). DEC-052: one shared front-desk till per property.</summary>
public sealed record CreateTillRequest(string Code, string Name);

public sealed record TillResult(int TillId, string Code, string Name, string Version);

/// <summary>POST /api/v1/commands/OpenShift (K). The float is carried from the previous counted close (DEC-035); only a till's first shift takes one.</summary>
public sealed record OpenShiftRequest(int TillId, long? OpeningFloatPaise);

/// <summary>POST /api/v1/commands/SubmitShiftCount (K V). Notes = count per note denomination (₹500…₹10); coins in paise.</summary>
public sealed record SubmitShiftCountRequest(long ShiftId, long CountedPaise, IReadOnlyDictionary<string, int>? Notes, long? CoinsPaise, string? Reason,
    string ExpectedVersion);

public sealed record ShiftResult(long ShiftId, int TillId, DateOnly BusinessDate, string Status, long OpeningFloatPaise, long? ExpectedPaise,
    long? CountedPaise, long? ApprovalId, string Version);

// ── Queries ──

public sealed record FolioLineView(long ChargeId, string Kind, DateOnly ServiceDate, string Label, long AmountPaise, int TaxBps, long TaxPaise,
    long? ReversesChargeId, bool Reversed);

public sealed record FolioReceiptView(long ReceiptId, string Kind, string Tender, long AmountPaise, DateOnly BusinessDate, string Reference,
    long? RefundOf, long RefundedPaise, long RefundablePaise);

public sealed record ApprovalView(long ApprovalId, string Action, string TargetKind, long TargetId, long AmountPaise, string Status, string Reason,
    int RequestedBy, int? DecidedBy, DateTime ExpiresUtc, long? ResultId, string Version);

public sealed record FolioTotalsView(long NetPaise, long TaxPaise, long TotalPaise, long PaidPaise, long DepositPaise, long CompanyCreditPaise,
    long BalancePaise);

/// <summary>GET /api/v1/folios/{folioId} (PERM-FOL-001).</summary>
public sealed record FolioView(long FolioId, long ReservationId, string ReservationStatus, DateOnly BusinessDate, string Version,
    IReadOnlyList<FolioLineView> Lines, IReadOnlyList<FolioReceiptView> Receipts, IReadOnlyList<ApprovalView> Approvals, FolioTotalsView Totals);

public sealed record ExpectedCashView(long OpeningPaise, long PaymentsPaise, long DepositsPaise, long RefundsPaise, long ReturnsPaise,
    long ExpensesPaise, long OtherPaise, long RestaurantPaise, long TransfersPaise, long ExpectedPaise, bool Consistent);

public sealed record CashMovementView(long MovementId, string Kind, string SourceType, long SourceId, long AmountPaise, string Label, string Reference,
    DateOnly BusinessDate);

/// <summary>GET /api/v1/shifts/{shiftId} (PERM-SHF-001).</summary>
public sealed record ShiftView(ShiftResult Shift, ExpectedCashView Expected, IReadOnlyList<CashMovementView> Movements);

/// <summary>GET /api/v1/cashier/collections?date= (PERM-RPT-002): payments − refunds by receipt date; deposits shown separately.</summary>
public sealed record CollectionsView(DateOnly BusinessDate, long CollectionsPaise, IReadOnlyDictionary<string, long> ByTenderPaise,
    long DepositsReceivedPaise, long DepositsReturnedPaise);

// ── W4-3 (sync layer) reads ──

/// <summary>
/// GET /api/v1/folios/by-reservation/{reservationId} (PERM-FOL-001): the reservation's folio (null until check-in opens one) and its stay
/// (id + version for CheckoutStay; null before check-in). NOT_FOUND when the reservation is not on this property.
/// </summary>
public sealed record ReservationFolioView(long ReservationId, long? StayId, string? StayVersion, FolioView? Folio);

/// <summary>One shift on the cash desk with the count reason, who closed it and its cash-difference approval (if any).</summary>
public sealed record CashDeskShiftView(ShiftResult Shift, int? SubmittedBy, string? DifferenceReason, int? ClosedBy, ApprovalView? Approval);

/// <summary>A till with its active (Open / Submitted) shift and expected cash, and its last closed shift.</summary>
public sealed record CashDeskTillView(TillResult Till, CashDeskShiftView? Active, ExpectedCashView? Expected, CashDeskShiftView? LastClosed);

/// <summary>GET /api/v1/cashier/desk (PERM-SHF-001, any shift): every till of the property (DEC-052: normally one shared till), by till id.</summary>
public sealed record CashDeskView(DateOnly BusinessDate, IReadOnlyList<CashDeskTillView> Tills);

/// <summary>
/// One booking on the cashier's folio list (D5). Only what billing needs: no phone, e-mail, address or ID fields (the Cashier's guest
/// view is masked, PERM-GST-001 M). <c>RoomNo</c> is the current room of an in-house stay (after a move), else the booked room.
/// <c>FolioId</c> is null for a confirmed arrival without a receipt yet (the first advance opens the folio).
/// </summary>
public sealed record DeskFolioItem(long ReservationId, string PublicNo, string Status, string GuestName, int? RoomId, string? RoomNo, string? RoomType,
    string Arrival, string Departure, int Adults, int Children, string Source, long? NightlyRatePaise, long? StayId, long? FolioId, string? FolioVersion,
    long BalancePaise, string? CompanyName, string? CompanyGstin);

/// <summary>
/// GET /api/v1/cashier/folios (PERM-FOL-001, property scope): the cashier's working list without the reservations read (PERM-RES-001):
/// every in-house stay, today's confirmed arrivals (advances) and today's departures, by room number.
/// </summary>
public sealed record DeskFolioListView(DateOnly BusinessDate, IReadOnlyList<DeskFolioItem> Items);
