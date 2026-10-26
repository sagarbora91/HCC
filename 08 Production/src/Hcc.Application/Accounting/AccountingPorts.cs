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
/// An open Tally re-export flag (DEC-077, migration 3601): the date's vouchers changed after they were exported (its day was reopened,
/// <see cref="ReopenId"/> set), or (A03b-05, <see cref="ReopenId"/> null) the date was exported while accounting events were still pending.
/// </summary>
public sealed record TallyReexportFlag(long ReexportId, DateOnly BusinessDate, long? ReopenId, string Reason, DateTime MarkedUtc);

/// <summary>One recorded Tally export: the range asked for, what it carried per date, and the flags it clears.</summary>
public sealed record NewTallyExport(int PropertyId, DateOnly From, DateOnly To, int VoucherCount, long Debit, long Credit, int ExportedBy, DateTime ExportedUtc,
    IReadOnlyList<(DateOnly Date, int Vouchers, bool Reexport, bool Provisional)> Days, IReadOnlyList<long> ClearFlags);

/// <summary>
/// DEC-077: what the Closing module asks Accounting inside its ReopenBusinessDay transaction (absent when Accounting is not composed).
/// </summary>
public interface IAccountingDayHooks
{
    /// <summary>Serialises with LockAccountingPeriod / UnlockAccountingPeriod of the date (same application lock); true when the date has a current period lock.</summary>
    Task<bool> IsLockedAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>True when an earlier Tally export carried the date.</summary>
    Task<bool> WasExportedToTallyAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>Flags an exported date changed (its day was reopened) so the next Tally export carries it again (an open flag stays as it is).</summary>
    Task MarkChangedForTallyAsync(ICommandTransaction tx, int propertyId, DateOnly date, long reopenId, string reason, DateTime nowUtc, CancellationToken ct);
}

/// <summary>Unbooked accounting events of a property: <see cref="Pending"/> still being processed, <see cref="Parked"/> set aside after repeated failures.</summary>
public sealed record PendingEvents(int Pending, int Parked)
{
    public int Total => Pending + Parked;
}

/// <summary>SQL-agnostic <see cref="IAccountingDayHooks"/> over the accounting store.</summary>
public sealed class AccountingDayHooks(IAccountingStore store) : IAccountingDayHooks
{
    public async Task<bool> IsLockedAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct)
    {
        await store.SerializeAsync(tx, AccountingDays.PeriodResource(propertyId, date), ct).ConfigureAwait(false);
        return await store.CurrentLockAsync(tx, propertyId, date, ct).ConfigureAwait(false) is not null;
    }

    public Task<bool> WasExportedToTallyAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct)
        => store.TallyExportedAsync(tx, propertyId, date, ct);

    public Task MarkChangedForTallyAsync(ICommandTransaction tx, int propertyId, DateOnly date, long reopenId, string reason, DateTime nowUtc, CancellationToken ct)
        => store.FlagTallyReexportAsync(tx, propertyId, date, reopenId, reason, nowUtc, ct);
}

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

    /// <summary>
    /// Consumed outbox events for this property not yet processed by the accounting consumer: still being processed (incl. accepted cash
    /// differences not yet swept) and parked after repeated failures (Wave C3: shown as "stuck — see System health").
    /// </summary>
    Task<PendingEvents> PendingEventsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Folio/receipt figures for the date straight from billing and payments (reconciliation).</summary>
    Task<OperationalFigures> OperationalFiguresAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>Dates in [from, to] with sources or locks, ascending.</summary>
    Task<IReadOnlyList<DateOnly>> ActivityDatesAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<string> PropertyNameAsync(int propertyId, CancellationToken ct);

    // ── DEC-077: reopened business day and the Tally export log (migrations 3600, 3601) ──

    /// <summary>The business date the Owner has reopened (Closing, not closed again), if any: it counts as Open here (not lockable, vouchers live).</summary>
    Task<DateOnly?> ReopenedDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>True when an earlier Tally export carried <paramref name="date"/>.</summary>
    Task<bool> TallyExportedAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>Flags <paramref name="date"/> for Tally re-export when an earlier export carried it (once: an open flag stays); true when it had been exported.</summary>
    Task<bool> FlagTallyReexportAsync(ICommandTransaction tx, int propertyId, DateOnly date, long? reopenId, string reason, DateTime now, CancellationToken ct);

    /// <summary>The property's open re-export flags, oldest date first.</summary>
    Task<IReadOnlyList<TallyReexportFlag>> TallyReexportsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Writes the export log row (A03b-05: marked as carrying voucher ids) and its days and clears <see cref="NewTallyExport.ClearFlags"/>; returns the export id.</summary>
    Task<long> RecordTallyExportAsync(ICommandTransaction tx, NewTallyExport export, CancellationToken ct);

    // ── A03b-05: stable Tally voucher ids (migration 3950) ──

    /// <summary>
    /// The property's Tally identity (32 hex digits, <c>acc.tally_identity</c>), created once on first use and never changed: the middle
    /// part of every voucher's REMOTEID. Inside a command it is created in the command's transaction.
    /// </summary>
    Task<string> TallyIdentityAsync(ICommandTransaction? tx, int propertyId, DateTime now, CancellationToken ct);

    /// <summary>Those of <paramref name="dates"/> that an export recorded before vouchers carried a REMOTEID took to Tally (Tally may hold them without an id).</summary>
    Task<IReadOnlyList<DateOnly>> TallyExportedWithoutIdsAsync(ICommandTransaction? tx, int propertyId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct);
}
