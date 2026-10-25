using Hcc.Application.Commands;
using Hcc.Domain.Accounting;

namespace Hcc.Application.Accounting;

/// <summary>
/// Accounting PERM ids (seeded by migration 1001 per DEC-063: G11 ledger mapping Owner only, G13 voucher export not for the
/// Cashier and the Tally tab Owner + Accountant only; prototype ACC_LOCK roles Owner/Manager/Accountant, ACC_UNLOCK Owner).
/// </summary>
public static class AccountingPermissions
{
    public const string View = "PERM-ACC-001";       // vouchers, trial balance, exceptions, reconciliation, mapping (read)
    public const string Lock = "PERM-ACC-002";       // lock a closed business date
    public const string Unlock = "PERM-ACC-003";     // unlock with a reason (Owner)
    public const string Mapping = "PERM-ACC-004";    // ledger mapping set / reset (Owner, DEC-063 G11)
    public const string Export = "PERM-ACC-005";     // generic voucher CSV (not Cashier, G13)
    public const string Tally = "PERM-ACC-006";      // simulated Tally XML (Owner + Accountant, G13)
}

/// <summary>A locked period: the snapshot vouchers as numbered at lock time.</summary>
public sealed record PeriodLock(long LockId, int PropertyId, DateOnly BusinessDate, int LockedBy, DateTime LockedUtc, int VoucherCount, long Debit, long Credit,
    string Version, IReadOnlyList<Voucher> Vouchers);

public sealed record MappingChange(long ChangeId, DateTime AtUtc, int Actor, string MapKey, string? Before, string? After);

public sealed record LockEvent(long LockId, DateOnly BusinessDate, string Action, DateTime AtUtc, int Actor, string? Reason);

/// <summary>
/// Accounting persistence. A null transaction reads committed data on its own connection (queries); inside a command the
/// command's transaction is used. Sources are append-only; locks are never deleted (unlock stamps the row, history kept).
/// </summary>
public interface IAccountingStore
{
    Task SerializeAsync(ICommandTransaction tx, string resource, CancellationToken ct);

    Task<IReadOnlyList<AccountingSource>> SourcesAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyDictionary<string, string>> MappingOverridesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Classification keys found in the property's sources with how many source records use each.</summary>
    Task<IReadOnlyDictionary<string, int>> KeyUsesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Sets (account) or removes (null) the override and appends the history row.</summary>
    Task WriteMappingAsync(ICommandTransaction tx, int propertyId, string key, string? account, string? before, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<MappingChange>> MappingHistoryAsync(int propertyId, CancellationToken ct);

    Task<PeriodLock?> CurrentLockAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<PeriodLock>> LocksAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<PeriodLock> InsertLockAsync(ICommandTransaction tx, int propertyId, DateOnly date, IReadOnlyList<Voucher> vouchers, DateTime now, CancellationToken ct);

    /// <summary>Stamps the current lock as unlocked; STALE_VERSION when <paramref name="expectedVersion"/> is not the row's version.</summary>
    Task UnlockAsync(ICommandTransaction tx, long lockId, string expectedVersion, string reason, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<LockEvent>> LockHistoryAsync(int propertyId, CancellationToken ct);

    /// <summary>The property's current business date (inv.property); null when the property does not exist.</summary>
    Task<DateOnly?> BusinessDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Consumed outbox events for this property not yet processed by the accounting consumer.</summary>
    Task<int> PendingEventsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Folio/receipt figures for the date straight from billing and payments (reconciliation).</summary>
    Task<OperationalFigures> OperationalFiguresAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>Dates in [from, to] with sources or locks, ascending.</summary>
    Task<IReadOnlyList<DateOnly>> ActivityDatesAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<string> PropertyNameAsync(int propertyId, CancellationToken ct);
}
