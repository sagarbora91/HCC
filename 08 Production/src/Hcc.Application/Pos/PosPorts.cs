using Hcc.Application.Commands;
using Hcc.Contracts.Pos;
using Hcc.Domain.Money;

namespace Hcc.Application.Pos;

/// <summary>
/// PERM ids used by the POS. The RBAC matrix has no POS rows; the prototype's coarse model maps onto existing ids with the same roles
/// (<c>billing</c> → PERM-FOL-002, <c>payments</c> → PERM-PAY-001, <c>approve</c> → PERM-APR-001), so no seed change is needed (DEC-063).
/// </summary>
public static class PosPermissions
{
    /// <summary>Open checks, items, KOTs, kitchen status, 86 an item, post a check to a room, void within the limit (prototype <c>billing</c>).</summary>
    public const string Operate = "PERM-FOL-002";

    /// <summary>Settle at the outlet (Cash/UPI/Card) and void a settled outlet bill (prototype <c>payments</c>); own-shift scope for REC/CSH.</summary>
    public const string Settle = "PERM-PAY-001";

    /// <summary>Decide a void above the limit (prototype <c>approve</c>); the Manager's limit applies to the void value.</summary>
    public const string VoidDecide = "PERM-APR-001";

    /// <summary>Outlet and menu setup (price, outlets, new items).</summary>
    public const string Setup = "PERM-ADM-003";

    /// <summary>Shift scope kind for the own-shift grant (same as billing).</summary>
    public const string ShiftScope = "shift";
}

/// <summary>The signed-in caller (session principal).</summary>
public sealed record PosCaller(int PropertyId, int PrincipalId);

/// <summary>
/// The ONE place the POS asks for its restaurant / room-service tax rate: the GST master (DEC-069) through <see cref="GstMasterPosTaxRates"/>
/// (food 5 % without ITC, or 18 % with ITC when the hotel declared specified premises for the financial year).
/// </summary>
public interface IPosTaxRates
{
    /// <summary>The GST rate for restaurant / room-service food on <paramref name="date"/> (the business date); pass the command transaction when there is one.</summary>
    Task<Hcc.Domain.Tax.GstRate> FoodAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct = default);
}

/// <summary>POS food tax from C's GST master (<see cref="Hcc.Application.Tax.IGstRateResolver"/>, category food incl. specified premises).</summary>
public sealed class GstMasterPosTaxRates(Hcc.Application.Tax.IGstRateResolver gst) : IPosTaxRates
{
    public Task<Hcc.Domain.Tax.GstRate> FoodAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct = default)
        => gst.ChargeAsync(tx, propertyId, date, Hcc.Domain.Tax.GstCategory.Food, ct);
}

/// <summary>Result of posting a bill to a room folio.</summary>
public sealed record PosFolioCharge(long ChargeId, string FolioVersion);

/// <summary>
/// Room-posting port toward Billing (folio). Implemented by <see cref="BillingPosFolioPort"/> over <c>IBillingStore</c>; runs inside the
/// settling transaction, so the bill and the folio line commit or roll back together.
/// </summary>
public interface IPosFolioPort
{
    /// <summary>One service line (net + tax snapshot) on an in-house folio. INVALID_TRANSITION when the folio is not in house.</summary>
    Task<PosFolioCharge> PostServiceChargeAsync(ICommandTransaction tx, int propertyId, long folioId, DateOnly businessDate, string label, Paise net,
        Hcc.Domain.Tax.GstRate rate, DateTime utc, CancellationToken ct = default);
}

/// <summary>Business-day close preflight input (P6 closing reads this): open checks and pending void requests block the close.</summary>
public interface IPosCloseGate
{
    Task<PosCloseBlockers> BlockersAsync(int propertyId, CancellationToken ct = default);

    /// <summary>The blocker list for closing <paramref name="date"/>: every check still open (opened on or before that date) with its table or room, number, guest and bill total, and every pending void request.</summary>
    Task<PosCloseList> ListAsync(int propertyId, DateOnly date, CancellationToken ct = default);
}

/// <summary>One thing POS holds open: a label for people ("Table T2 · POS-0003 · ₹1,680.00 open") and a reference for the page.</summary>
public sealed record PosCloseItem(string Label, string Ref);

public sealed record PosCloseList(IReadOnlyList<PosCloseItem> OpenChecks, IReadOnlyList<PosCloseItem> PendingVoids);

// ── Store rows ──

public sealed record OutletRow(int OutletId, int PropertyId, string Code, string Name, string Kind, string Version);

public sealed record MenuItemRow(int ItemId, int PropertyId, string Code, string Name, string Category, Paise Price, bool Veg, bool Available,
    IReadOnlyList<int> OutletIds, string Version);

public sealed record StayRoomRow(long StayId, long FolioId, int RoomId, string RoomNo, string Guest, string Status);

public sealed record OrderRow(long OrderId, int PropertyId, int OutletId, string OutletName, string OutletKind, string? TableNo, int? RoomId, string? RoomNo,
    long? StayId, string? Guest, int Covers, DateOnly BusinessDate, string Status, long? PendingVoidId, int Rev, string Version);

public sealed record LineRow(long LineId, long OrderId, int ItemId, string Name, Paise Price, int Qty, long? KotId, bool Voided, string? VoidReason);

public sealed record KotRow(long KotId, long OrderId, int OutletId, int KotNo, string Status, DateTime CreatedUtc, DateTime? ReadyUtc, DateTime? ServedUtc);

public sealed record BillRow(long BillId, long OrderId, string BillNo, string Mode, string? Tender, string? Reference, Paise Net, int TaxBps, Paise Tax,
    Paise Total, DateOnly BusinessDate, int? TillId, long? ShiftId, long? FolioId, long? ChargeId, string Status);

public sealed record NewBill(long OrderId, int PropertyId, string BillNo, string Mode, string? Tender, string? Reference, Paise Net, int TaxBps, Paise Tax,
    Paise Total, DateOnly BusinessDate, DateTime SettledUtc, int SettledBy, int? TillId, long? ShiftId, long? CashMovementId, long? StayId, long? FolioId,
    long? ChargeId, Paise Cgst = default, Paise Sgst = default);

public sealed record NewReceipt(long BillId, long OrderId, int OutletId, string Kind, string Tender, Paise Amount, Paise Net, Paise Tax, int TaxBps,
    string Reference, DateOnly BusinessDate, int PrincipalId, DateTime RecordedUtc);

public sealed record VoidRequestRow(long VoidId, int PropertyId, long OrderId, string Scope, long? LineId, Paise Amount, string Reason, string Status,
    int OrderRev, int RequestedBy, DateTime ExpiresUtc, string Version);

public sealed record NewVoidRequest(int PropertyId, long OrderId, string Scope, long? LineId, Paise Amount, string Reason, int OrderRev, int RequestedBy,
    DateTime RequestedUtc, DateTime ExpiresUtc);

/// <summary>
/// Persistence for the POS. Methods taking an <see cref="ICommandTransaction"/> run inside the command transaction.
/// Lock order: check → bill counter → (till/shift inside the drawer port) → void request.
/// </summary>
public interface IPosStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    // setup
    Task<bool> OutletCodeExistsAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct);

    Task<OutletRow> InsertOutletAsync(ICommandTransaction tx, int propertyId, string code, string name, string kind, IReadOnlyList<string> tables, DateTime utc, CancellationToken ct);

    Task<OutletRow?> OutletAsync(ICommandTransaction tx, int propertyId, int outletId, CancellationToken ct);

    Task<bool> OutletHasTableAsync(ICommandTransaction tx, int outletId, string table, CancellationToken ct);

    Task<MenuItemRow?> MenuItemAsync(ICommandTransaction tx, int propertyId, int itemId, bool lockRow, CancellationToken ct);

    Task<bool> MenuCodeExistsAsync(ICommandTransaction tx, int propertyId, string code, int? exceptItemId, CancellationToken ct);

    Task<int> CountOutletsAsync(ICommandTransaction tx, int propertyId, IReadOnlyList<int> outletIds, CancellationToken ct);

    Task<MenuItemRow> InsertMenuItemAsync(ICommandTransaction tx, int propertyId, string code, string name, string category, Paise price, bool veg,
        IReadOnlyList<int> outletIds, CancellationToken ct);

    Task<MenuItemRow> UpdateMenuItemAsync(ICommandTransaction tx, int itemId, byte[] expectedVersion, string code, string name, string category, Paise price,
        bool veg, IReadOnlyList<int> outletIds, CancellationToken ct);

    Task<string> SetAvailabilityAsync(ICommandTransaction tx, int itemId, bool available, CancellationToken ct);

    // in-house guests (read from the stays tables)
    Task<StayRoomRow?> InHouseByRoomAsync(ICommandTransaction tx, int propertyId, string roomNo, CancellationToken ct);

    Task<StayRoomRow?> StayByIdAsync(ICommandTransaction tx, int propertyId, long stayId, CancellationToken ct);

    // checks
    Task<long> InsertOrderAsync(ICommandTransaction tx, int propertyId, int outletId, string? table, int? roomId, long? stayId, string? guest, int covers,
        DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct);

    /// <summary>The check under UPDLOCK; null when unknown at the property.</summary>
    Task<OrderRow?> LockOrderAsync(ICommandTransaction tx, int propertyId, long orderId, CancellationToken ct);

    /// <summary>Open check on a table / room of an outlet, if any (for a clear message before the unique index).</summary>
    Task<bool> OpenCheckExistsAsync(ICommandTransaction tx, int outletId, string? table, int? roomId, CancellationToken ct);

    /// <summary>Advances the check's revision/rowversion and returns the new wire version.</summary>
    Task<string> TouchOrderAsync(ICommandTransaction tx, long orderId, CancellationToken ct);

    Task SetOrderStatusAsync(ICommandTransaction tx, long orderId, string status, string? voidReason, int? voidedBy, DateTime? voidedUtc, long? stayId, CancellationToken ct);

    Task SetPendingVoidAsync(ICommandTransaction tx, long orderId, long? voidId, CancellationToken ct);

    Task<IReadOnlyList<LineRow>> LinesAsync(ICommandTransaction tx, long orderId, CancellationToken ct);

    Task<long> InsertLineAsync(ICommandTransaction tx, long orderId, MenuItemRow item, int qty, CancellationToken ct);

    Task SetLineQtyAsync(ICommandTransaction tx, long lineId, int qty, CancellationToken ct);

    Task DeleteLineAsync(ICommandTransaction tx, long lineId, CancellationToken ct);

    Task VoidLinesAsync(ICommandTransaction tx, IReadOnlyList<long> lineIds, string reason, int by, DateOnly date, long? voidRequestId, CancellationToken ct);

    // kitchen
    Task<(long KotId, int KotNo)> InsertKotAsync(ICommandTransaction tx, OrderRow order, DateOnly date, int principalId, DateTime utc, CancellationToken ct);

    Task AssignKotAsync(ICommandTransaction tx, long kotId, IReadOnlyList<long> lineIds, CancellationToken ct);

    Task<KotRow?> LockKotAsync(ICommandTransaction tx, long orderId, long kotId, CancellationToken ct);

    Task UpdateKotStatusAsync(ICommandTransaction tx, long kotId, string status, DateTime utc, CancellationToken ct);

    // bills
    Task<int> NextBillSequenceAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<long> InsertBillAsync(ICommandTransaction tx, NewBill bill, CancellationToken ct);

    Task<BillRow?> BillForOrderAsync(ICommandTransaction tx, long orderId, CancellationToken ct);

    Task MarkBillVoidAsync(ICommandTransaction tx, long billId, string reason, int by, DateTime utc, CancellationToken ct);

    Task<long> InsertReceiptAsync(ICommandTransaction tx, NewReceipt receipt, CancellationToken ct);

    // void requests
    Task<(long VoidId, string Version)> InsertVoidRequestAsync(ICommandTransaction tx, NewVoidRequest request, CancellationToken ct);

    Task<VoidRequestRow?> PeekVoidRequestAsync(ICommandTransaction tx, int propertyId, long voidId, CancellationToken ct);

    Task<VoidRequestRow?> LockVoidRequestAsync(ICommandTransaction tx, long voidId, CancellationToken ct);

    Task<string> DecideVoidRequestAsync(ICommandTransaction tx, long voidId, string expectedVersion, string status, int decidedBy, DateTime utc,
        string? decisionReason, CancellationToken ct);

    // queries (own connection)
    Task<CheckView?> ReadCheckAsync(int propertyId, long orderId, int taxBps, CancellationToken ct);

    Task<IReadOnlyList<OpenCheckSummary>> OpenChecksAsync(int propertyId, CancellationToken ct);

    Task<IReadOnlyList<OutletView>> OutletsAsync(int propertyId, CancellationToken ct);

    Task<IReadOnlyList<MenuItemView>> MenuAsync(int propertyId, int? outletId, CancellationToken ct);

    Task<IReadOnlyList<KitchenItem>> KitchenAsync(int propertyId, CancellationToken ct);

    Task<DaySummaryView> DaySummaryAsync(int propertyId, DateOnly date, int taxBps, CancellationToken ct);

    Task<DateOnly?> PropertyBusinessDateAsync(int propertyId, CancellationToken ct);

    Task<PosCloseBlockers> BlockersAsync(int propertyId, CancellationToken ct);

    /// <summary>Checks that left the open state (settled or void) with a bill on, or opened on, <paramref name="date"/>, newest first (at most <paramref name="take"/>).</summary>
    Task<IReadOnlyList<long>> ClosedCheckIdsAsync(int propertyId, DateOnly date, int take, CancellationToken ct);

    /// <summary>Pending void requests, oldest first.</summary>
    Task<IReadOnlyList<VoidRequestView>> PendingVoidsAsync(int propertyId, CancellationToken ct);

    /// <summary>In-house stays with their current room, by room number.</summary>
    Task<IReadOnlyList<PosStayView>> InHouseStaysAsync(int propertyId, CancellationToken ct);

    /// <summary>The property's shared till and its open shift (null ids when no till exists yet).</summary>
    Task<PosTillView> TillAsync(int propertyId, CancellationToken ct);
}
