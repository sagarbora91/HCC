using Hcc.Application.Commands;
using Hcc.Contracts.Pos;

namespace Hcc.Application.Pos;

/// <summary>The four POS master tables (MA-4); the generic retire / reactivate / reorder / delete commands work on any of them.</summary>
public enum PosMasterTable
{
    Outlet,
    Table,
    Category,
    Item,
}

/// <summary>The common part of every POS master row (lock root for code / name uniqueness and reorder).</summary>
public sealed record PosMasterBase(int Id, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, byte[] RowVer);

public sealed record OutletRec(int OutletId, string Code, string Name, string Kind, string Type, string? Opens, string? Closes, string GstProfile, string? KotStation,
    int SortOrder, bool Active, DateOnly? RetiredOn, byte[] RowVer);

public sealed record TableRec(int TableId, int OutletId, string Code, string Name, string TableNo, int? Seats, string? Section, bool Counter, int SortOrder, bool Active,
    DateOnly? RetiredOn, byte[] RowVer);

public sealed record CategoryRec(int CategoryId, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, byte[] RowVer);

public sealed record ItemRec(int ItemId, string Code, string Name, int? CategoryId, string Category, string? ShortCode, string GstClass, bool Veg, string? Station,
    int SortOrder, bool Active, DateOnly? RetiredOn, bool Available, DateOnly? SoldOutOn, IReadOnlyList<int> OutletIds, IReadOnlyList<(DateOnly From, long Paise)> Prices,
    byte[] RowVer);

/// <summary>How a POS master row is used: every record, the open ones (open checks), and a short label per kind.</summary>
public sealed record PosMasterUsage(int Count, int Open, IReadOnlyList<string> Where);

/// <summary>
/// Persistence of the POS masters (migration 2840). Write methods run inside the command transaction; the Lock* reads take UPDLOCK +
/// HOLDLOCK on the property's rows of one master (code and name uniqueness, reorder). Implemented by <c>SqlPosStore</c>.
/// </summary>
public interface IPosMastersStore
{
    Task<IReadOnlyList<PosMasterBase>> LockBaseAsync(ICommandTransaction tx, PosMasterTable table, int propertyId, CancellationToken ct = default);

    Task<IReadOnlyList<OutletRec>> LockOutletsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<int> InsertOutletAsync(ICommandTransaction tx, int propertyId, OutletRec row, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateOutletAsync(ICommandTransaction tx, OutletRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    Task<IReadOnlyList<TableRec>> LockTablesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<int> InsertTableAsync(ICommandTransaction tx, TableRec row, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateTableAsync(ICommandTransaction tx, TableRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    Task<IReadOnlyList<CategoryRec>> LockCategoriesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    Task<int> InsertCategoryAsync(ICommandTransaction tx, int propertyId, CategoryRec row, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateCategoryAsync(ICommandTransaction tx, CategoryRec row, byte[] expectedVersion, int principalId, DateTime utc, CancellationToken ct = default);

    Task<IReadOnlyList<ItemRec>> LockItemsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Inserts the item with its outlets and dated prices; <c>price_paise</c> = the price in force on <paramref name="businessDate"/>.</summary>
    Task<int> InsertItemAsync(ICommandTransaction tx, int propertyId, ItemRec row, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Updates the item, replaces its outlets and its dated prices (the caller has checked that amounts in effect are unchanged).</summary>
    Task UpdateItemAsync(ICommandTransaction tx, ItemRec row, byte[] expectedVersion, DateOnly businessDate, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, PosMasterTable table, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetSortAsync(ICommandTransaction tx, PosMasterTable table, int id, int sortOrder, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, PosMasterTable table, int id, byte[] expectedVersion, CancellationToken ct = default);

    Task<PosMasterUsage> UsageAsync(ICommandTransaction tx, PosMasterTable table, int id, CancellationToken ct = default);

    /// <summary>One <c>mst.master_history</c> row (MA-0 shared history: who, when, field old → new).</summary>
    Task AddHistoryAsync(ICommandTransaction tx, int propertyId, string master, long rowId, string? code, string field, string? oldValue, string? newValue, int principalId,
        DateTime utc, CancellationToken ct = default);

    /// <summary>GET /pos/menu-categories (own connection): categories in sort order with their item counts.</summary>
    Task<IReadOnlyList<MenuCategoryView>> CategoriesAsync(int propertyId, CancellationToken ct = default);
}
