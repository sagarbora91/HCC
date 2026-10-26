using Hcc.Application.Commands;

namespace Hcc.Application.FrontDeskMasters;

/// <summary>A row of one MA-5 list as stored (Fields: the list's own columns, keyed by wire name — see FrontDeskMastersRules).</summary>
public sealed record FdmRecord(int RowId, string Code, string Name, int SortOrder, bool Active, DateOnly? RetiredOn, IReadOnlyDictionary<string, object?> Fields,
    DateTime UpdatedUtc, byte[] RowVer)
{
    public T Get<T>(string field) => Fields.TryGetValue(field, out var v) && v is T t ? t : default!;
}

/// <summary>Documents of one kind: count, last document date and the FY codes ("2627") that have any.</summary>
public sealed record DocumentActivity(int Count, DateOnly? LastDocument, IReadOnlyCollection<string> FyCodes)
{
    public static readonly DocumentActivity None = new(0, null, []);
}

/// <summary>The numbering series of a document in force on a date (read by the invoices module).</summary>
public sealed record SeriesInForce(int RowId, string Code, string Prefix, int StartNo, int Padding, string Reset);

/// <summary>
/// Numbering series of documents (MA-5, bill.number_series) for the modules that number them — today the invoices module (tax invoice,
/// bill of supply, credit note). Null = the property has not stored its list yet: the caller keeps its built-in numbering (the defaults
/// are exactly that numbering).
/// </summary>
public interface INumberSeriesSource
{
    Task<SeriesInForce?> InForceAsync(ICommandTransaction tx, int propertyId, string document, DateOnly date, CancellationToken ct = default);
}

/// <summary>Write side of the four lists (inside the command transaction; rows locked with UPDLOCK, HOLDLOCK on the property's list).</summary>
public interface IFrontDeskMastersStore
{
    Task<IReadOnlyList<FdmRecord>> LockAllAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default);

    /// <summary>Rows without locks (numbering read by the invoices module inside its own transaction).</summary>
    Task<IReadOnlyList<FdmRecord>> ReadAllAsync(ICommandTransaction tx, int propertyId, string master, CancellationToken ct = default);

    Task<int> InsertAsync(ICommandTransaction tx, int propertyId, string master, string code, string name, int sortOrder, IReadOnlyDictionary<string, object?> fields,
        int principalId, DateTime utc, CancellationToken ct = default);

    Task UpdateAsync(ICommandTransaction tx, string master, int rowId, byte[] expectedVersion, string code, string name, int sortOrder, IReadOnlyDictionary<string, object?> fields,
        int principalId, DateTime utc, CancellationToken ct = default);

    Task SetActiveAsync(ICommandTransaction tx, string master, int rowId, byte[] expectedVersion, bool active, DateOnly? retiredOn, int principalId, DateTime utc, CancellationToken ct = default);

    Task SetSortAsync(ICommandTransaction tx, string master, int rowId, int sortOrder, int principalId, DateTime utc, CancellationToken ct = default);

    Task DeleteAsync(ICommandTransaction tx, string master, int rowId, byte[] expectedVersion, CancellationToken ct = default);

    /// <summary>Records that use the row (reservations / guest profiles / documents issued under a series / policies naming a source).</summary>
    Task<int> UsageAsync(ICommandTransaction tx, int propertyId, string master, FdmRecord row, IReadOnlyList<FdmRecord> list, CancellationToken ct = default);

    /// <summary>Issued documents of a kind (INV / BOS from ivc.invoice, CRN from ivc.credit_note; others are not numbered on the server yet).</summary>
    Task<DocumentActivity> DocumentsAsync(ICommandTransaction tx, int propertyId, string document, DateOnly? from = null, DateOnly? toExclusive = null, CancellationToken ct = default);
}

/// <summary>Read side (own connection).</summary>
public interface IFrontDeskMastersReadStore
{
    Task<IReadOnlyList<FdmRecord>> ListAsync(int propertyId, string master, CancellationToken ct = default);

    Task<int> UsageAsync(int propertyId, string master, FdmRecord row, IReadOnlyList<FdmRecord> list, CancellationToken ct = default);

    Task<DateOnly> BusinessDateAsync(int propertyId, CancellationToken ct = default);

    /// <summary>The invoices module's series of a FY and document (prefix frozen for the FY, last number), or null before its first document.</summary>
    Task<(string Prefix, int LastSeq)?> FySeriesAsync(int propertyId, string fyCode, string document, CancellationToken ct = default);
}
