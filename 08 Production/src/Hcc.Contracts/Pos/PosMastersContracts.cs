namespace Hcc.Contracts.Pos;

// MA-4 · POS masters wire contracts (plan Part 2 §8 F: M26 outlets, M27 tables and counters, M28 menu categories and items). Commands go
// through ICommandRunner (Idempotency-Key; ExpectedVersion on edits, STALE_VERSION). Master errors travel as VALIDATION / INVALID_TRANSITION
// with details.reason = MasterReasons.* (MA-0). Permissions: PERM-MST-001 (lists) and PERM-MST-002 (price, GST class, outlet GST profile).
// SaveMenuItem keeps its W2-6 request shape (PosContracts.cs) with the master fields added as optional members.

/// <summary>SaveOutlet K [V]. <c>OutletId</c> null = create. Type restaurant / room-service / bar / banquet (room service = checks on rooms);
/// GstProfile hotel (default) / standard / specified (Owner-only); timings HH:MM.</summary>
public sealed record SaveOutletRequest(int? OutletId, string Code, string Name, string Type, string? Opens = null, string? Closes = null, string? GstProfile = null,
    string? KotStation = null, int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>SaveOutletTable K [V]. A table or takeaway counter of a table outlet; the table number is fixed once a check used it.</summary>
public sealed record SaveOutletTableRequest(int? TableId, int OutletId, string Code, string Name, string TableNo, int? Seats = null, string? Section = null, bool Counter = false,
    int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>SaveMenuCategory K [V].</summary>
public sealed record SaveMenuCategoryRequest(int? CategoryId, string Code, string Name, int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>Retire / Reactivate / Delete of an outlet (K V). Retire is refused while the outlet has open checks; delete only when never used.</summary>
public sealed record OutletRefRequest(int OutletId, string ExpectedVersion);

/// <summary>Retire / Reactivate / Delete of a table or counter (K V). Retire is refused while a check is open on it.</summary>
public sealed record OutletTableRefRequest(int TableId, string ExpectedVersion);

/// <summary>Retire / Reactivate / Delete of a menu category (K V).</summary>
public sealed record MenuCategoryRefRequest(int CategoryId, string ExpectedVersion);

/// <summary>Retire / Reactivate / Delete of a menu item (K V). A retired item leaves the POS; check lines keep its name.</summary>
public sealed record MenuItemRefRequest(int ItemId, string ExpectedVersion);

/// <summary>ReorderOutlets K: every outlet id of the property once, in the new order (sort order 10, 20, …).</summary>
public sealed record ReorderOutletsRequest(IReadOnlyList<int> OutletIds);

public sealed record ReorderOutletTablesRequest(IReadOnlyList<int> TableIds);

public sealed record ReorderMenuCategoriesRequest(IReadOnlyList<int> CategoryIds);

public sealed record ReorderMenuItemsRequest(IReadOnlyList<int> ItemIds);

/// <summary>Result of a POS master command: the list, the row, its code and new version, and what happened.</summary>
public sealed record PosMasterResult(string Master, int Id, string Code, string? Version, string Action);

/// <summary>One dated menu price (paise, before GST).</summary>
public sealed record MenuPriceView(string EffectiveFrom, long AmountPaise);

/// <summary>A table or takeaway counter of an outlet (GET /pos/outlets → TableRows).</summary>
public sealed record OutletTableView(int TableId, string Code, string Name, string TableNo, int? Seats, string? Section, bool Counter, int SortOrder, bool Active,
    string? RetiredOn, string Version);

/// <summary>GET /pos/menu-categories item: <c>Items</c> = menu items in the category.</summary>
public sealed record MenuCategoryView(int CategoryId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, int Items, string Version);

public sealed record MenuCategoryList(int Page, int PageSize, int Total, IReadOnlyList<MenuCategoryView> Items);
