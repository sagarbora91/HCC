using Hcc.Application.Commands;

namespace Hcc.Application.Stock;

/// <summary>
/// PERM ids of Linen &amp; stock: the module's own set from role matrix v2 (DEC-079, migration 3700) instead of the borrowed housekeeping
/// and finance-report ids. Money (unit cost, value, laundry rate and charge, loss value) rides only on <see cref="Money"/>, which the Cashier,
/// Supervisor and Housekeeper do not hold (D3, DEC-061).
/// </summary>
public static class StockPermissions
{
    /// <summary>Read the module without money (levels, laundry, counts): Owner, Manager, Supervisor, Accountant (view), Housekeeper (assigned).</summary>
    public const string View = "PERM-STK-001";

    /// <summary>Work with stock: issue, use, room change, laundry, losses, counts (assigned-scope housekeepers included).</summary>
    public const string Work = "PERM-STK-002";

    /// <summary>Supervise: receive goods, decide counts (never your own), create items.</summary>
    public const string Supervise = "PERM-STK-003";

    /// <summary>See and enter money (unit cost, stock value, laundry rates and charges): Owner, Manager, Accountant.</summary>
    public const string Money = "PERM-STK-004";

    /// <summary>Configure item levels, par sets and the laundry vendor (rates also need <see cref="Money"/>).</summary>
    public const string Configure = "PERM-STK-005";
}

public sealed record StockItemRecord(
    int ItemId, string Code, string Name, string Kind, string Unit, long CostPaise, int MinQty, int ReorderQty, int? StdX100,
    long? LaundryRatePaise, string? Supplier, IReadOnlyDictionary<string, int> Par, IReadOnlyDictionary<string, int> Stock, byte[] Version);

public sealed record StockSettingsRecord(int ParSets, string? VendorName, int TurnaroundDays)
{
    public static StockSettingsRecord Default { get; } = new(3, null, 1);
}

public sealed record LaundryLineRecord(int ItemId, string ItemCode, string ItemName, int Sent, int? Received, int? Damaged);

public sealed record LaundryBatchRecord(long BatchId, string Vendor, DateOnly SentDate, string Status, DateOnly? ReturnedDate, string? Note, long? ChargePaise,
    IReadOnlyList<LaundryLineRecord> Lines, byte[] Version);

public sealed record CountLineRecord(int ItemId, string ItemCode, string ItemName, int System, int Counted);

public sealed record CountRecord(long CountId, DateOnly BusinessDate, string Loc, string Status, int ActorId, int? ReviewerId, string? Reason, string? Note,
    IReadOnlyList<CountLineRecord> Lines, byte[] Version);

public sealed record LossRecord(long LossId, DateOnly BusinessDate, string ItemCode, string ItemName, string Loc, int Qty, string Reason, string? Note, int ActorId, long ValuePaise, string? Ref);

public sealed record MoveRecord(long MoveId, DateOnly BusinessDate, DateTime AtUtc, string Kind, string ItemCode, int Qty, string? From, string? To, string? Ref, string? Reason, int ActorId);

/// <summary>Live occupied room-nights and imported history for one date (the rule picks live, then history).</summary>
public sealed record OccupancyDay(DateOnly Date, int Live, int History);

/// <summary>Quantity used per item in a period: amenities/cleaning from <c>use</c> moves, linen from <c>room-change</c> moves.</summary>
public sealed record ConsumptionUsed(string ItemCode, int Used);

/// <summary>Write side (inside the command transaction). Every quantity change happens under the item's row lock.</summary>
public interface IStockStore
{
    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>The item with its quantities, locked (UPDLOCK) until commit; null when absent. Lock several items in ascending code order.</summary>
    Task<StockItemRecord?> LockItemAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default);

    Task AdjustAsync(ICommandTransaction tx, int itemId, string loc, int delta, CancellationToken ct = default);

    Task SetCostAsync(ICommandTransaction tx, int itemId, long costPaise, CancellationToken ct = default);

    Task<long> InsertMoveAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, DateTime utc, string kind, int itemId, int qty,
        string? from, string? to, string? reference, string? reason, int actorId, CancellationToken ct = default);

    Task<(long GrnId, string Ref)> InsertGrnAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, string supplier, string? reference, int actorId, DateTime utc, CancellationToken ct = default);

    Task InsertGrnLineAsync(ICommandTransaction tx, long grnId, int itemId, int qty, long costPaise, CancellationToken ct = default);

    Task<long> InsertLossAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, int itemId, string loc, int qty, string reason, string? note,
        int actorId, long valuePaise, string? reference, DateTime utc, CancellationToken ct = default);

    Task<StockSettingsRecord> SettingsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task UpsertSettingsAsync(ICommandTransaction tx, int propertyId, StockSettingsRecord settings, CancellationToken ct = default);

    Task<(long BatchId, byte[] Version)> InsertBatchAsync(ICommandTransaction tx, int propertyId, string vendor, DateOnly sentDate, DateTime utc, int actorId, string? note, CancellationToken ct = default);

    Task InsertBatchLineAsync(ICommandTransaction tx, long batchId, int itemId, int sent, CancellationToken ct = default);

    Task<LaundryBatchRecord?> LockBatchAsync(ICommandTransaction tx, int propertyId, long batchId, CancellationToken ct = default);

    Task SetBatchLineReturnAsync(ICommandTransaction tx, long batchId, int itemId, int received, int damaged, CancellationToken ct = default);

    Task CompleteBatchAsync(ICommandTransaction tx, long batchId, byte[] expectedVersion, string status, DateOnly returned, int receivedBy, string? note, long charge, CancellationToken ct = default);

    /// <summary>All items with the quantity at one location (read inside the transaction; the count snapshot).</summary>
    Task<IReadOnlyList<(int ItemId, string Code, string Name, int Qty)>> LevelsAtAsync(ICommandTransaction tx, int propertyId, string loc, CancellationToken ct = default);

    Task<bool> HasPendingCountAsync(ICommandTransaction tx, int propertyId, string loc, CancellationToken ct = default);

    Task<(long CountId, byte[] Version)> InsertCountAsync(ICommandTransaction tx, int propertyId, DateOnly businessDate, DateTime utc, string loc, string status, int actorId, string? note,
        IReadOnlyList<(int ItemId, int System, int Counted)> lines, CancellationToken ct = default);

    Task<CountRecord?> LockCountAsync(ICommandTransaction tx, int propertyId, long countId, CancellationToken ct = default);

    Task DecideCountAsync(ICommandTransaction tx, long countId, byte[] expectedVersion, string status, int reviewerId, string reason, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, int>> RoomTypeIdsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Inserts the item with zero stock at every location. False when the code already exists (checked under a lock).</summary>
    Task<(bool Created, int ItemId, byte[] Version)> InsertItemAsync(ICommandTransaction tx, int propertyId, string code, string name, string kind, string unit,
        long costPaise, int minQty, int reorderQty, int? stdX100, long? laundryRatePaise, string? supplier, DateTime utc, CancellationToken ct = default);

    /// <summary>Updates the levels and, when <paramref name="par"/> is not null, replaces the par rows (room type id → pieces). Versioned.</summary>
    Task<byte[]> UpdateItemAsync(ICommandTransaction tx, int itemId, byte[] expectedVersion, int minQty, int reorderQty, int? stdX100,
        IReadOnlyDictionary<int, int>? par, CancellationToken ct = default);
}

/// <summary>Read side (own connections).</summary>
public interface IStockReadStore
{
    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<StockItemRecord>> ItemsAsync(int propertyId, CancellationToken ct = default);

    /// <summary>Room count per room type code.</summary>
    Task<IReadOnlyDictionary<string, int>> RoomsByTypeAsync(int propertyId, CancellationToken ct = default);

    Task<StockSettingsRecord> SettingsAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<LaundryBatchRecord>> BatchesAsync(int propertyId, string? status, CancellationToken ct = default);

    Task<int> PendingCountsAsync(int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<CountRecord>> CountsAsync(int propertyId, string? status, CancellationToken ct = default);

    Task<IReadOnlyList<LossRecord>> LossesAsync(int propertyId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    Task<IReadOnlyList<MoveRecord>> MovesAsync(int propertyId, string? itemCode, DateOnly? from, DateOnly? to, int limit, CancellationToken ct = default);

    Task<IReadOnlyList<OccupancyDay>> OccupancyAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<ConsumptionUsed>> ConsumptionAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
