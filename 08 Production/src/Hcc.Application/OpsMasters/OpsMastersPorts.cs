using Hcc.Application.Commands;
using Hcc.Contracts.OpsMasters;

namespace Hcc.Application.OpsMasters;

/// <summary>
/// One operations master (MA-7). <c>Suffix</c> builds the command names (Save/Retire/Reactivate/Delete + Suffix, Reorder + Plural);
/// <c>Keys</c> are the master's own fields in the order the change history lists them (prototype field keys).
/// </summary>
public sealed record OpsMasterDef(string Master, string Label, string Singular, string Suffix, string Plural, string AuditKind, string UsedWhat, IReadOnlyList<string> Keys)
{
    public string Save => "Save" + Suffix;

    public string Retire => "Retire" + Suffix;

    public string Reactivate => "Reactivate" + Suffix;

    public string Delete => "Delete" + Suffix;

    public string Reorder => "Reorder" + Plural;
}

public static class OpsMasterDefs
{
    public static readonly OpsMasterDef HkTaskTypes = new(OpsMasterIds.HkTaskTypes, "Housekeeping task types", "task type", "HkTaskType", "HkTaskTypes", "MST_HK_TASK_TYPE", "housekeeping tasks",
        ["appliesTo", "standardMinutes", "needsInspection", "checklist"]);

    public static readonly OpsMasterDef HkZones = new(OpsMasterIds.HkZones, "Housekeeping zones", "zone", "HkZone", "HkZones", "MST_HK_ZONE", "open tasks in its rooms",
        ["floors", "rooms", "defaultAttendant"]);

    public static readonly OpsMasterDef ComplaintCategories = new(OpsMasterIds.ComplaintCategories, "Complaint categories", "complaint category", "ComplaintCategory", "ComplaintCategories", "MST_COMPLAINT_CATEGORY", "complaints",
        ["department", "slaHours"]);

    public static readonly OpsMasterDef LostFoundCategories = new(OpsMasterIds.LostFoundCategories, "Lost-and-found categories", "lost-and-found category", "LostFoundCategory", "LostFoundCategories", "MST_LOSTFOUND_CATEGORY", "found items",
        ["department", "slaHours", "valuable"]);

    public static readonly IReadOnlyList<OpsMasterDef> All = [HkTaskTypes, HkZones, ComplaintCategories, LostFoundCategories];

    public static OpsMasterDef Of(string master) => All.First(d => d.Master == master);
}

/// <summary>A master row as stored; <c>Fields</c> hold the master's own columns as canonical text (numbers, "1"/"0", comma lists, JSON checklist).</summary>
public sealed record OpsRow(long Id, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, DateTime UpdatedUtc, byte[] RowVer, IReadOnlyDictionary<string, string?> Fields);

/// <summary>Write side of the four lists (inside the command transaction; rows locked with UPDLOCK).</summary>
public interface IOpsMastersStore
{
    Task<IReadOnlyList<OpsRow>> LockAllAsync(ICommandTransaction tx, OpsMasterDef def, int propertyId, CancellationToken ct = default);

    Task<OpsRow?> LockOneAsync(ICommandTransaction tx, OpsMasterDef def, int propertyId, long id, CancellationToken ct = default);

    Task<long> InsertAsync(ICommandTransaction tx, OpsMasterDef def, int propertyId, string code, string name, int sortOrder, IReadOnlyDictionary<string, string?> fields, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateAsync(ICommandTransaction tx, OpsMasterDef def, long id, byte[] expectedVersion, string code, string name, int sortOrder, IReadOnlyDictionary<string, string?> fields, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, OpsMasterDef def, long id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetSortAsync(ICommandTransaction tx, OpsMasterDef def, long id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, OpsMasterDef def, long id, byte[] expectedVersion, CancellationToken ct = default);

    /// <summary>Codes of the property's floors (zones may list only floors that exist).</summary>
    Task<IReadOnlySet<string>> FloorCodesAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Room numbers of the property (zones may list only rooms that exist).</summary>
    Task<IReadOnlySet<string>> RoomNosAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);
}

/// <summary>Read side (own connection): rows in sort order with how many records use each.</summary>
public interface IOpsMastersReadStore
{
    Task<IReadOnlyList<(OpsRow Row, int Used)>> ListAsync(OpsMasterDef def, int propertyId, CancellationToken ct = default);
}

/// <summary>What the housekeeping task machine needs from the task type of a task kind.</summary>
public sealed record HkTypeRule(int ChecklistItems, bool NeedsInspection, int StandardMinutes);

/// <summary>Task-type lookup used by the housekeeping commands (inside their transaction): the first active type covering the kind, else a retired one, else the built-in values.</summary>
public interface IHkTaskTypeSource
{
    Task<HkTypeRule> ForKindAsync(ICommandTransaction tx, int propertyId, string kind, CancellationToken ct = default);
}

/// <summary>A category row as guest services needs it when a record is logged.</summary>
public sealed record GsCategory(string Code, string Name, string Department, int SlaHours, bool Valuable);

/// <summary>Category lookup used by the guest-services commands: an active row by code or by name (case-insensitive), else null.</summary>
public interface IGsCategorySource
{
    Task<GsCategory?> ComplaintAsync(ICommandTransaction tx, int propertyId, string? codeOrName, CancellationToken ct = default);

    Task<GsCategory?> LostFoundAsync(ICommandTransaction tx, int propertyId, string? codeOrName, CancellationToken ct = default);

    /// <summary>Complaint category names of the property in sort order, retired rows included (the report lists them).</summary>
    Task<IReadOnlyList<string>> ComplaintNamesAsync(int propertyId, CancellationToken ct = default);
}
