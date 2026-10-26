namespace Hcc.Contracts.Masters;

// Wire contracts of the Masters module (MA-0: framework + Floors, plan Part 2 §9 / M1). Master errors travel as the frozen error codes
// (VALIDATION / INVALID_TRANSITION) with details.reason = one of MasterReasons and details.field when one field is at fault; the
// prototype (domain/streams/masters.ts MstError) uses the same pairs.

/// <summary>details.reason values of master errors.</summary>
public static class MasterReasons
{
    public const string CodeTaken = "MASTER_CODE_TAKEN";
    public const string InUse = "MASTER_IN_USE";
    public const string Retired = "MASTER_RETIRED";
    public const string LockedByBookings = "MASTER_LOCKED_BY_BOOKINGS";
    public const string Invalid = "MASTER_INVALID";

    public static readonly IReadOnlyList<string> All = [CodeTaken, InUse, Retired, LockedByBookings, Invalid];
}

/// <summary>List ids used in <c>mst.master_history.master</c> and by <c>IMasterUsage</c>.</summary>
public static class MasterIds
{
    public const string Floors = "floors";
}

/// <summary>One floor. <c>RoomCount</c> = rooms on the floor (hundreds digit of the room number until MA-1 links rooms to floors).</summary>
public sealed record FloorView(int FloorId, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, int RoomCount, string UpdatedUtc, string Version);

/// <summary>GET /masters/floors: floors in sort order (status = active | retired | all, default all), paged.</summary>
public sealed record FloorList(int Page, int PageSize, int Total, IReadOnlyList<FloorView> Items);

/// <summary>SaveFloor K [V]: <c>FloorId</c> null = create (SortOrder defaults to after the last floor); an edit needs <c>ExpectedVersion</c>.</summary>
public sealed record SaveFloorRequest(int? FloorId, string Code, string Name, int? SortOrder = null, string? ExpectedVersion = null);

/// <summary>RetireFloor K V: active → retired (allowed while rooms use it; the rooms keep the name).</summary>
public sealed record RetireFloorRequest(int FloorId, string ExpectedVersion);

/// <summary>ReactivateFloor K V: retired → active.</summary>
public sealed record ReactivateFloorRequest(int FloorId, string ExpectedVersion);

/// <summary>DeleteFloor K V: only a floor that no room uses (MASTER_IN_USE otherwise — retire it instead).</summary>
public sealed record DeleteFloorRequest(int FloorId, string ExpectedVersion);

public sealed record DeleteFloorResult(int FloorId, string Code, bool Deleted);

/// <summary>ReorderFloors K: every floor id of the property once, in the new order (sort order 10, 20, …).</summary>
public sealed record ReorderFloorsRequest(IReadOnlyList<int> FloorIds);

/// <summary>One history row: who changed which field of which row, old → new (values as shown to people).</summary>
public sealed record MasterHistoryView(long HistoryId, long RowId, string? Code, string Field, string? OldValue, string? NewValue, int ChangedBy, string? ChangedByName, string ChangedUtc);

public sealed record MasterHistoryList(int Page, int PageSize, int Total, IReadOnlyList<MasterHistoryView> Items);
