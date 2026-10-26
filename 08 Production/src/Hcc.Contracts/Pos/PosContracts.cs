namespace Hcc.Contracts.Pos;

// Wire contracts for the restaurant POS & room service (module 23). Money = integer paise; dates YYYY-MM-DD; versions = base64 rowversion.

/// <summary>POST /api/v1/commands/CreateOutlet (K). Kind <c>table</c> (with <c>Tables</c>) or <c>room</c> (room service).</summary>
public sealed record CreateOutletRequest(string Code, string Name, string Kind, IReadOnlyList<string>? Tables);

/// <summary>POST /api/v1/commands/SaveMenuItem (K). <c>ItemId</c> null creates; otherwise updates (then <c>ExpectedVersion</c> is required).</summary>
public sealed record SaveMenuItemRequest(int? ItemId, string Code, string Name, string Category, long PricePaise, bool Veg, IReadOnlyList<int> OutletIds,
    string? ExpectedVersion);

/// <summary>POST /api/v1/commands/SetMenuItemAvailability (K). false = "86" (unavailable).</summary>
public sealed record SetMenuItemAvailabilityRequest(int ItemId, bool Available);

public sealed record MenuItemResult(int ItemId, bool Available, string Version);

public sealed record OutletResult(int OutletId, string Code, string Version);

/// <summary>POST /api/v1/commands/OpenCheck (K). Table outlets take <c>Table</c>; room service takes <c>RoomNo</c> of an in-house room.</summary>
public sealed record OpenCheckRequest(int OutletId, string? Table, string? RoomNo, int? Covers, string? Guest);

/// <summary>POST /api/v1/commands/AddCheckItem (K). Unsent lines of the same item merge.</summary>
public sealed record AddCheckItemRequest(long OrderId, int ItemId, int? Qty, string? ExpectedVersion);

/// <summary>POST /api/v1/commands/SetCheckItemQty (K). Qty 0 removes an unsent line.</summary>
public sealed record SetCheckItemQtyRequest(long OrderId, long LineId, int Qty, string? ExpectedVersion);

/// <summary>POST /api/v1/commands/SendKot (K). Sends every unsent line.</summary>
public sealed record SendKotRequest(long OrderId, string? ExpectedVersion);

/// <summary>POST /api/v1/commands/AdvanceKot (K). Status Ready or Served, forward only.</summary>
public sealed record AdvanceKotRequest(long OrderId, long KotId, string Status);

/// <summary>
/// POST /api/v1/commands/SettleCheck (K V). Mode <c>outlet</c> (Tender Cash/UPI/Card; UPI/Card need a reference; Cash needs <c>TillId</c> with an
/// open shift, DEC-052) or <c>folio</c> (one service line on the in-house guest's folio; <c>StayId</c> defaults to the check's stay).
/// </summary>
public sealed record SettleCheckRequest(long OrderId, string Mode, string? Tender, string? Reference, int? TillId, long? StayId, string ExpectedVersion);

/// <summary>POST /api/v1/commands/VoidCheckItem (K). Only items already sent to the kitchen. Above the limit this raises a request.</summary>
public sealed record VoidCheckItemRequest(long OrderId, long LineId, string Reason, string? ExpectedVersion);

/// <summary>POST /api/v1/commands/VoidBill (K). An open check, or a settled outlet bill of the current business date.</summary>
public sealed record VoidBillRequest(long OrderId, string Reason, string? ExpectedVersion);

/// <summary>POST /api/v1/commands/DecideVoid (K). A different user with approval rights decides a pending void.</summary>
public sealed record DecideVoidRequest(long VoidRequestId, bool Approve, string? Reason);

public sealed record CheckResult(long OrderId, string Status, string Version, long NetPaise, int TaxBps, long TaxPaise, long TotalPaise, int Items);

public sealed record KotResult(long OrderId, long KotId, int KotNo, string Status, string Version);

public sealed record SettleResult(long OrderId, long BillId, string BillNo, string Mode, long NetPaise, int TaxBps, long TaxPaise, long TotalPaise,
    long? ReceiptId, long? ChargeId, long? CashMovementId, string Version);

/// <summary>Outcome <c>Voided</c> or <c>PendingApproval</c> (then <c>VoidRequestId</c> is set and the check is locked).</summary>
public sealed record VoidResult(long OrderId, string Outcome, long? VoidRequestId, string Version);

public sealed record DecideVoidResult(long VoidRequestId, string Status, long OrderId, string OrderVersion);

// ── Queries ──

public sealed record LineView(long LineId, int ItemId, string Name, long PricePaise, int Qty, long ValuePaise, long? KotId, string Status,
    bool Voided, string? VoidReason);

public sealed record KotView(long KotId, int KotNo, string Label, string Status, DateTime CreatedUtc, DateTime? ReadyUtc, DateTime? ServedUtc, int? CreatedBy = null);

public sealed record BillView(long BillId, string BillNo, string Mode, string? Tender, string? Reference, long NetPaise, int TaxBps, long TaxPaise,
    long TotalPaise, DateOnly BusinessDate, string Status, DateTime? SettledUtc = null, int? SettledBy = null, long? ReservationId = null);

public sealed record CheckView(long OrderId, int OutletId, string Outlet, string Spot, string? Guest, int Covers, DateOnly BusinessDate, string Status,
    string Version, long? PendingVoidId, IReadOnlyList<LineView> Lines, IReadOnlyList<KotView> Kots, long NetPaise, int TaxBps, long TaxPaise,
    long TotalPaise, int Items, BillView? Bill, DateTime? OpenedUtc = null, int? OpenedBy = null, string? VoidReason = null, DateTime? VoidedUtc = null,
    long? ReservationId = null);

public sealed record OpenCheckSummary(long OrderId, string Outlet, string Spot, string? Guest, DateOnly BusinessDate, int Items, long NetPaise,
    bool KotPending, long? PendingVoidId);

public sealed record MenuItemView(int ItemId, string Code, string Name, string Category, long PricePaise, bool Veg, bool Available,
    IReadOnlyList<int> OutletIds, string Version);

public sealed record OutletView(int OutletId, string Code, string Name, string Kind, IReadOnlyList<string> Tables);

public sealed record KitchenItem(long OrderId, long KotId, int KotNo, string Label, string Spot, string Status, DateTime CreatedUtc,
    IReadOnlyList<LineView> Lines);

public sealed record DayOutletSummary(int OutletId, string Name, int Checks, long NetPaise, long TaxPaise, long GrossPaise, int OpenChecks);

public sealed record TopItem(string Name, int Qty, long ValuePaise);

/// <summary>Outlet day summary (prototype <c>posDaySummary</c>): settled checks by settlement date; open checks as of now.</summary>
public sealed record DaySummaryView(DateOnly Date, int Checks, int Covers, long NetPaise, long TaxPaise, long GrossPaise, long CashPaise, long UpiPaise,
    long CardPaise, long RoomFolioPaise, long OutletCashPaise, int VoidCount, long VoidValuePaise, int OpenCount, long OpenValuePaise,
    IReadOnlyList<DayOutletSummary> ByOutlet, IReadOnlyList<TopItem> Top, int PendingVoids);

/// <summary>What stops a business-day close (the closing module's preflight reads this): open checks and pending void requests.</summary>
public sealed record PosCloseBlockers(int OpenChecks, int PendingVoids)
{
    public bool Blocked => OpenChecks > 0 || PendingVoids > 0;
}

/// <summary>A pending void request (check locked until decided): the approvals queue and the check panel read this. <c>RequestedBy</c> is the user id.</summary>
public sealed record VoidRequestView(long VoidRequestId, long OrderId, string Spot, string Scope, long? LineId, long AmountPaise, string Reason, int RequestedBy,
    DateTime ExpiresUtc, string OrderVersion);

/// <summary>An in-house guest the outlet can charge to (room service targets, "post to room"): stay, folio, reservation (= the web client's booking id), room.</summary>
public sealed record PosStayView(long StayId, long FolioId, long ReservationId, string RoomNo, string Guest);

/// <summary>The shared front-desk till (DEC-052) and its open shift, so the outlet can take cash. <c>ShiftOpen</c> false = no cash can be taken.</summary>
public sealed record PosTillView(int? TillId, string? Code, long? ShiftId, bool ShiftOpen);

