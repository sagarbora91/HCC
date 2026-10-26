namespace Hcc.Contracts.OpsMasters;

// Wire contracts of the operations masters (MA-7: housekeeping task types M23, zones M24, complaint and lost-and-found categories M25).
// Same envelope as the floors master (Masters module): rows carry id, code, name, sortOrder, active, retiredOn, used (records that use
// the row) and a version token; master errors travel as VALIDATION / INVALID_TRANSITION with details.reason (MasterReasons) and
// details.field. No money field exists in this file (DEC-061: housekeeping and maintenance read these lists).

/// <summary>List ids used in <c>mst.master_history.master</c> and by <c>IMasterUsage</c>.</summary>
public static class OpsMasterIds
{
    public const string HkTaskTypes = "hkTaskTypes";
    public const string HkZones = "hkZones";
    public const string ComplaintCategories = "complaintCategories";
    public const string LostFoundCategories = "lostFoundCategories";
}

/// <summary>Task kinds and roles a task type can serve (prototype hkmasters.ts HKT_APPLIES).</summary>
public static class HkTypeKinds
{
    public const string Checkout = "checkout";
    public const string Move = "move";
    public const string Stayover = "stayover";
    public const string Inspection = "inspection";
    public const string Deep = "deep";

    public static readonly IReadOnlyList<string> All = [Checkout, Move, Stayover, Inspection, Deep];
}

/// <summary>One page of a master list (status = active | retired | all).</summary>
public sealed record OpsList<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);

/// <summary>Housekeeping task type: <c>AppliesTo</c> kinds (HkTypeKinds), standard minutes 5–240, inspection, 1–8 checklist items.</summary>
public sealed record HkTaskTypeView(long Id, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, int Used, string UpdatedUtc, string Version,
    IReadOnlyList<string> AppliesTo, int StandardMinutes, bool NeedsInspection, IReadOnlyList<string> Checklist);

/// <summary>Zone: floor codes (mst.floor) and/or room numbers, with a default attendant (principal id).</summary>
public sealed record HkZoneView(long Id, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, int Used, string UpdatedUtc, string Version,
    IReadOnlyList<string> Floors, IReadOnlyList<string> Rooms, int? DefaultAttendantId);

/// <summary>Complaint category: department (Housekeeping | Maintenance | Front desk) and SLA hours 1–720.</summary>
public sealed record ComplaintCategoryView(long Id, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, int Used, string UpdatedUtc, string Version,
    string Department, int SlaHours);

/// <summary>Lost-and-found category: as a complaint category plus <c>Valuable</c> (safe storage, manager disposal).</summary>
public sealed record LostFoundCategoryView(long Id, string Code, string Name, int SortOrder, bool Active, string? RetiredOn, int Used, string UpdatedUtc, string Version,
    string Department, int SlaHours, bool Valuable);

/// <summary>SaveHkTaskType K [V]: <c>Id</c> null = create; an edit needs <c>ExpectedVersion</c>.</summary>
public sealed record SaveHkTaskTypeRequest(long? Id, string Code, string Name, int? SortOrder, IReadOnlyList<string>? AppliesTo, int? StandardMinutes, bool NeedsInspection,
    IReadOnlyList<string>? Checklist, string? ExpectedVersion = null);

/// <summary>SaveHkZone K [V]: at least one floor or room; <c>DefaultAttendantId</c> must be an enabled housekeeper.</summary>
public sealed record SaveHkZoneRequest(long? Id, string Code, string Name, int? SortOrder, IReadOnlyList<string>? Floors, IReadOnlyList<string>? Rooms, int? DefaultAttendantId,
    string? ExpectedVersion = null);

public sealed record SaveComplaintCategoryRequest(long? Id, string Code, string Name, int? SortOrder, string? Department, int? SlaHours, string? ExpectedVersion = null);

public sealed record SaveLostFoundCategoryRequest(long? Id, string Code, string Name, int? SortOrder, string? Department, int? SlaHours, bool Valuable, string? ExpectedVersion = null);

/// <summary>Retire* / Reactivate* / Delete* K V (one body for every master): <c>Id</c> of the row.</summary>
public sealed record OpsRowRequest(long Id, string ExpectedVersion);

/// <summary>Reorder* K: every row id of the master once, in the new order (sort order 10, 20, …).</summary>
public sealed record OpsReorderRequest(IReadOnlyList<long> Ids);

public sealed record OpsDeleteResult(long Id, string Code, bool Deleted);

public sealed record OpsReorderResult(int Count);

/// <summary>Result of Retire* / Reactivate*: the row's new status and version.</summary>
public sealed record OpsStatusResult(long Id, string Code, bool Active, string? RetiredOn, string Version);
