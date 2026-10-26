using Hcc.Application.Commands;
using Hcc.Domain.PeopleLists;

namespace Hcc.Application.PeopleLists;

/// <summary>A People list row as stored: the framework columns plus the list's own fields (string, int, bool or null by key; see PeopleListsRules.FieldKeys).</summary>
public sealed record ListRec(int Id, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, DateTime UpdatedUtc, byte[] RowVer, IReadOnlyDictionary<string, object?> Fields)
{
    public string? Text(string key) => Fields.TryGetValue(key, out var v) ? v as string : null;

    public int? Int(string key) => Fields.TryGetValue(key, out var v) && v is int i ? i : null;

    public bool Flag(string key) => Fields.TryGetValue(key, out var v) && v is true;
}

/// <summary>Write side (inside the command transaction; rows locked with UPDLOCK / HOLDLOCK on the property's set).</summary>
public interface IPeopleListsStore
{
    /// <summary>Seeds the property's lists the first time (shift patterns M E N G, the six leave types with the stored policy's CL / SL / EL days, one designation per staff title); a list that has rows is left alone.</summary>
    Task EnsureSeededAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>Every row of the list for the property, locked, in sort order.</summary>
    Task<IReadOnlyList<ListRec>> LockRowsAsync(ICommandTransaction tx, PeopleList list, int propertyId, CancellationToken ct = default);

    Task<int> InsertAsync(ICommandTransaction tx, PeopleList list, int propertyId, CleanListRow row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateAsync(ICommandTransaction tx, PeopleList list, int id, byte[] expectedVersion, CleanListRow row, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, PeopleList list, int id, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetSortAsync(ICommandTransaction tx, PeopleList list, int id, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, PeopleList list, int id, byte[] expectedVersion, CancellationToken ct = default);

    Task<bool> DepartmentExistsAsync(ICommandTransaction tx, int propertyId, string dept, CancellationToken ct = default);

    /// <summary>Names of active staff whose default shift is the pattern (retiring it is refused while there are any).</summary>
    Task<IReadOnlyList<string>> ActiveStaffOnShiftAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default);

    /// <summary>Keeps hr.leave_policy (cl / sl / el days) equal to the CL / SL / EL quotas of the Leave types list.</summary>
    Task MirrorLeavePolicyAsync(ICommandTransaction tx, int propertyId, string code, int quota, int principalId, DateTime utc, CancellationToken ct = default);

    Task<DateOnly> BusinessDateAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);
}

/// <summary>Read side (own connection).</summary>
public interface IPeopleListsReadStore
{
    Task EnsureSeededAsync(int propertyId, CancellationToken ct = default);

    /// <summary>Rows in sort order with how many records use each.</summary>
    Task<IReadOnlyList<(ListRec Rec, int Used)>> RowsAsync(PeopleList list, int propertyId, CancellationToken ct = default);
}
