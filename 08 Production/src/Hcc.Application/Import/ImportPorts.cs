using Hcc.Application.Commands;
using Hcc.Contracts.Import;
using Hcc.Domain.Import;

namespace Hcc.Application.Import;

/// <summary>
/// Import PERM ids (migration 2701). Prepare = upload / validate / preview / dry-run (Owner, Tech admin). Commit: the non-money masters
/// (rooms, guest profiles) need <see cref="CommitSetup"/>; room types (rack rate), tariffs, seasons, staff roles, ledgers, opening balances
/// and till cash need <see cref="CommitOwner"/> (money/roles, DEC-058). Both are Owner-only in the seed.
/// </summary>
public static class ImportPermissions
{
    public const string Prepare = "PERM-IMP-001";
    public const string CommitSetup = "PERM-IMP-002";
    public const string CommitOwner = "PERM-IMP-003";

    public static string CommitFor(ImportMasterSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.OwnerOnlyCommit ? CommitOwner : CommitSetup;
    }
}

public sealed record ImportBatchRecord(long BatchId, int PropertyId, string Master, string FileName, string CsvText, DateOnly? AsOfDate, int Rows, int Errors,
    string Status, int UploadedBy, DateTime UploadedUtc, int? CommittedBy, DateTime? CommittedUtc, string? ResultJson);

/// <summary>Persistence of the import module (imp schema) and the read-only snapshot of the owning modules' masters.</summary>
public interface IImportStore
{
    /// <summary>Serializes imports of one property for the transaction (application lock).</summary>
    Task LockImportsAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>
    /// The property's current masters (room types, rooms, tariffs, seasons, roles, users, ledgers, opening balances, tills, guest keys) and whether
    /// any business day is closed. Inside a command transaction the closed-day read holds a key-range lock (the P6 late-write barrier pattern).
    /// </summary>
    Task<ImportSnapshot> SnapshotAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct = default);

    Task<long> InsertBatchAsync(ICommandTransaction tx, int propertyId, string master, string fileName, byte[] sha256, string csvText, DateOnly? asOf, int rows, int errors,
        int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>A batch of the property (UPDLOCK when <paramref name="lockRow"/> inside a transaction); null when absent.</summary>
    Task<ImportBatchRecord?> GetBatchAsync(ICommandTransaction? tx, int propertyId, long batchId, bool lockRow, CancellationToken ct = default);

    Task<IReadOnlyList<ImportBatchRecord>> ListBatchesAsync(int propertyId, int limit, CancellationToken ct = default);

    Task MarkCommittedAsync(ICommandTransaction tx, long batchId, int principalId, DateTime utc, string resultJson, CancellationToken ct = default);

    Task UpsertLedgerAsync(ICommandTransaction tx, int propertyId, LedgerData ledger, long batchId, int principalId, DateTime utc, CancellationToken ct = default);

    /// <summary>Replaces the whole opening trial balance (the CHECK constraint refuses an unbalanced set).</summary>
    Task ReplaceOpeningAsync(ICommandTransaction tx, int propertyId, DateOnly asOf, IReadOnlyList<OpeningData> rows, long batchId, int principalId, DateTime utc,
        CancellationToken ct = default);

    /// <summary>Batch id of the current opening trial balance (null when none).</summary>
    Task<long?> OpeningBatchAsync(int propertyId, CancellationToken ct = default);
}

/// <summary>
/// Writer port toward P2 (inventory masters). P2 has no create/update command for room types and rooms yet, so the import module
/// ships this port with a SQL adapter over inv.room_type / inv.room (Infrastructure/Import); when P2 owns a writer, register P2's
/// implementation instead. Runs inside the caller's command transaction.
/// </summary>
public interface IImportInventoryWriter
{
    Task<int> InsertRoomTypeAsync(ICommandTransaction tx, int propertyId, RoomTypeData type, CancellationToken ct = default);

    Task UpdateRoomTypeAsync(ICommandTransaction tx, int propertyId, RoomTypeData type, CancellationToken ct = default);

    /// <summary>Adds the room (Ready) and raises the sellable count of already-materialised type-nights (inv.type_night_count) by one.</summary>
    Task InsertRoomAsync(ICommandTransaction tx, int propertyId, RoomData room, CancellationToken ct = default);
}

/// <summary>
/// Writer port toward P1 (users). <c>IUserAdministration.CreateUserAsync</c> runs its own transaction per user, so a batch commit (one transaction)
/// uses this port: user + roles, NO credential (the first password/PIN is set by the Owner with the Access admin SetCredential — the existing
/// one-time reset flow). When P1 exposes a transaction-scoped writer, register it instead.
/// </summary>
public interface IImportAccessWriter
{
    Task<(int UserId, string Version)> CreateUserAsync(ICommandTransaction tx, int propertyId, StaffData user, int assignedBy, DateTime utc, CancellationToken ct = default);
}

/// <summary>Read port for Accounting (W2-3): the imported opening trial balance, to be added to the cumulative trial balance.</summary>
public interface IOpeningBalanceSource
{
    Task<OpeningBalancesView> GetAsync(int propertyId, CancellationToken ct = default);
}
