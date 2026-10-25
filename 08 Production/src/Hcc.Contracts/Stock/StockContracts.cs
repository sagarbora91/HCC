namespace Hcc.Contracts.Stock;

// Wire contracts for Linen, laundry & stock (W2-8, prototype module 16). Items are addressed by their code (e.g. LIN-BS).
// Money (unit cost, stock value, laundry rate/charge, loss value) is null for everyone without PERM-RPT-002 (DEC-063).
// Housekeepers and supervisors therefore never receive a money field with a value.

public static class StockLocations
{
    public const string Store = "store", Floor1 = "floor1", Floor2 = "floor2", Floor3 = "floor3", Rooms = "rooms", Soiled = "soiled", Laundry = "laundry";
}

public static class StockKinds
{
    public const string Linen = "linen", Amenity = "amenity", Cleaning = "cleaning";
}

/// <summary>StockReceiveGrn K (simulated goods receipt into the Main store). Unit cost needs a money role.</summary>
public sealed record ReceiveGrnRequest(string Supplier, string? Ref, IReadOnlyList<GrnLineRequest> Lines);

public sealed record GrnLineRequest(string ItemCode, int Qty, long? UnitCostPaise = null);

public sealed record ReceiveGrnResult(long GrnId, string Ref, int Lines, string Detail);

/// <summary>StockIssue K: transfer between the Main store and floor pantries.</summary>
public sealed record IssueStockRequest(string ItemCode, int Qty, string From, string To);

/// <summary>StockUse K: supplies used from a floor pantry (not linen).</summary>
public sealed record UseStockRequest(string ItemCode, int Qty, string Floor, string? Room = null);

/// <summary>StockRoomChange K: fresh linen from a floor pantry into rooms; the same number of used pieces become soiled.</summary>
public sealed record RoomChangeRequest(string ItemCode, int Qty, string Floor, string? Room = null);

public sealed record StockMoveResult(long MoveId, string Detail);

public sealed record LaundryLineRequest(string ItemCode, int Qty);

public sealed record SendLaundryRequest(IReadOnlyList<LaundryLineRequest> Lines, string? Note = null);

public sealed record SendLaundryResult(long BatchId, string Vendor, int Pieces, string ExpectedVersion);

public sealed record ReceiveLaundryLine(string ItemCode, int Received, int Damaged = 0);

/// <summary>StockLaundryReceive K V: received clean to Main store; damaged and missing pieces go to the loss register.</summary>
public sealed record ReceiveLaundryRequest(long BatchId, string ExpectedVersion, IReadOnlyList<ReceiveLaundryLine> Lines, string? Note = null);

public sealed record ReceiveLaundryResult(long BatchId, string Status, int CleanPieces, int LossLines, long? ChargePaise);

/// <summary>StockRecordLoss K: write-off with a reason (Other needs a note).</summary>
public sealed record RecordLossRequest(string ItemCode, int Qty, string Loc, string Reason, string? Note = null);

public sealed record RecordLossResult(long LossId, string Detail);

/// <summary>StockCountSubmit K: physical count of a shelf location; <c>Counts</c> maps every item code to the counted quantity.</summary>
public sealed record SubmitCountRequest(string Loc, IReadOnlyDictionary<string, int> Counts, string? Note = null);

public sealed record SubmitCountResult(long CountId, string Status, int VarianceLines, string ExpectedVersion);

/// <summary>StockCountDecide K V: approve posts the variance, reject discards it. Another user than the counter, PERM-HK-004.</summary>
public sealed record DecideCountRequest(long CountId, string ExpectedVersion, bool Approve, string Reason);

public sealed record DecideCountResult(long CountId, string Status, int LossLines);

/// <summary>StockItemCreate K: a new stock item (PERM-HK-004). Unit cost and laundry rate need a money role.</summary>
public sealed record CreateStockItemRequest(string Code, string Name, string Kind, string Unit, int MinQty, int ReorderQty, int? StdX100 = null,
    long? UnitCostPaise = null, long? LaundryRatePaise = null, string? Supplier = null, IReadOnlyDictionary<string, int>? Par = null);

public sealed record StockItemWriteResult(string Code, string Detail, string Version);

/// <summary>StockItemSet K V: minimum, reorder quantity, par per room type (linen only) and standard use per room-night.</summary>
public sealed record SetStockItemRequest(string Code, string ExpectedVersion, int MinQty, int ReorderQty, int? StdX100 = null, IReadOnlyDictionary<string, int>? Par = null);

/// <summary>StockSettingsSet K (PERM-HK-004): par sets in circulation and the laundry vendor. Master configuration will own this later (DEC-057).</summary>
public sealed record SetStockSettingsRequest(int ParSets, string? VendorName, int TurnaroundDays);

public sealed record StockSettingsResult(int ParSets, string? VendorName, int TurnaroundDays);

public sealed record StockLevelView(string Loc, int Qty);

public sealed record StockItemView(
    string Code, string Name, string Kind, string Unit, int MinQty, int ReorderQty, int? StdX100,
    IReadOnlyDictionary<string, int> Par, IReadOnlyList<StockLevelView> Levels, int Clean, int Total, int ParRequired, int ParPerSet, int ParCoveragePercent,
    bool BelowMinimum, int SuggestedReorder, string? Supplier,
    long? UnitCostPaise, long? ValuePaise, long? LaundryRatePaise, string Version);

public sealed record StockItemsPage(int Page, int PageSize, int Total, IReadOnlyList<StockItemView> Rows, int ParSets, int AlertCount, int PiecesAtLaundry, int PendingCounts, long? TotalValuePaise);

public sealed record StockAlertView(string Code, string Name, int Clean, int MinQty, int Suggested);

public sealed record LaundryLineView(string ItemCode, string ItemName, int Sent, int? Received, int? Damaged, int Missing);

public sealed record LaundryBatchView(long BatchId, string Vendor, string SentDate, string Status, string? ReturnedDate, string? Note, IReadOnlyList<LaundryLineView> Lines, long? ChargePaise, string Version);

public sealed record LaundryBatchesPage(int Page, int PageSize, int Total, IReadOnlyList<LaundryBatchView> Rows, int PiecesOut);

public sealed record CountLineView(string ItemCode, string ItemName, int System, int Counted);

public sealed record CountView(long CountId, string BusinessDate, string Loc, string Status, int ActorId, int? ReviewerId, string? Reason, string? Note, IReadOnlyList<CountLineView> Lines, long? VarianceValuePaise, string Version);

public sealed record CountsPage(int Page, int PageSize, int Total, IReadOnlyList<CountView> Rows);

public sealed record LossView(long LossId, string BusinessDate, string ItemCode, string ItemName, string Loc, int Qty, string Reason, string? Note, int ActorId, long? ValuePaise, string? Ref);

public sealed record LossesPage(int Page, int PageSize, int Total, IReadOnlyList<LossView> Rows, long? TotalValuePaise);

public sealed record MoveView(long MoveId, string BusinessDate, string AtUtc, string Kind, string ItemCode, int Qty, string? From, string? To, string? Ref, string? Reason, int ActorId);

public sealed record MovesPage(int Page, int PageSize, int Total, IReadOnlyList<MoveView> Rows);

public sealed record ConsumptionRow(string Code, string Name, string Kind, int Used, decimal PerRoomNight, decimal Standard, int? VariancePercent, bool Flag);

public sealed record ConsumptionReport(string From, string To, int OccupiedRoomNights, IReadOnlyList<ConsumptionRow> Rows);
