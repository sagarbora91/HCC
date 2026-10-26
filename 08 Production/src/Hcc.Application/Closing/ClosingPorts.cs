using Hcc.Application.Commands;
using Hcc.Contracts.Closing;
using Hcc.Domain.Closing;
using Hcc.Domain.Reporting;

namespace Hcc.Application.Closing;

/// <summary>PERM ids used by closing and reporting (RBAC matrix §5 with DEC-063; seeded by P1 migration 0101).</summary>
public static class ClosingPermissions
{
    /// <summary>close.preflight.view (Owner, Manager; Accountant view).</summary>
    public const string PreflightView = "PERM-CLS-001";

    /// <summary>close.nightPost / closeDay / openNextDay (Owner, Manager).</summary>
    public const string Close = "PERM-CLS-002";

    /// <summary>
    /// Cash-book day lock (DEC-065 checker: Owner, Manager, Accountant). No dedicated PERM id exists in matrix v1; mapped to
    /// close.correction.request, which grants exactly those roles (lead/owner decision to confirm or add PERM-CLS-005).
    /// </summary>
    public const string CashBookLock = "PERM-CLS-003";

    /// <summary>Cash-book reopen (DEC-065: Owner only, with a reason). Mapped to close.correction.approve (Owner only).</summary>
    public const string CashBookReopen = "PERM-CLS-004";

    /// <summary>
    /// DEC-077 business-day reopen (Owner only, with a reason): close.correction.approve, the matrix's Owner-only closed-date correction
    /// approval (§7) — the same grant as <see cref="CashBookReopen"/>; a dedicated id can follow with role matrix v2.
    /// </summary>
    public const string DayReopen = "PERM-CLS-004";

    public const string ReportOperations = "PERM-RPT-001";
    public const string ReportFinance = "PERM-RPT-002";
    public const string ReportExport = "PERM-RPT-003";
}

/// <summary>
/// Late-write barrier (contracts §3 RunBusinessDayClose; prototype <c>open()</c>): a write for a closed business date is refused with
/// DAY_CLOSED. Called inside the writer's command transaction; the read holds a key-range lock on <c>cls.business_day</c>, so the close
/// transaction that inserts the barrier row waits for in-flight writers and every later writer sees the closed date.
/// </summary>
public interface IBusinessDayGuard
{
    /// <summary>The property's current business date, refusing with DAY_CLOSED when it is already closed (closed, next day not yet opened).</summary>
    Task<DateOnly?> EnsureBusinessDayOpenAsync(ICommandTransaction tx, int propertyId, CancellationToken ct = default);

    /// <summary>DAY_CLOSED when <paramref name="date"/> is before the business date or closed.</summary>
    Task EnsureOpenAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default);

    /// <summary>DEC-065: DAY_CLOSED while the cash-book day <paramref name="date"/> is locked (corrections or cash entries touching it).</summary>
    Task EnsureCashBookOpenAsync(ICommandTransaction tx, int propertyId, DateOnly date, string what, CancellationToken ct = default);

    /// <summary>
    /// DEC-077: true when <paramref name="date"/> is the property's reopened business day (Owner reopen, not closed again). Takes a shared
    /// lock on that day for the rest of the writer's transaction, so the re-close (exclusive) waits for in-flight corrections and every
    /// later writer sees the day closed again.
    /// </summary>
    Task<bool> IsReopenedAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct = default);
}

/// <summary>
/// The command runner every P2/P3/P4 service gets from DI (Wave 1 composition): each command first passes
/// <see cref="IBusinessDayGuard.EnsureBusinessDayOpenAsync"/> inside its own transaction, then runs. Replays of an already
/// committed command (same Idempotency-Key and payload) return the stored result as before. P1 access and the close itself use
/// the platform runner directly.
/// </summary>
public sealed class BusinessDayGuardedCommandRunner(ICommandRunner inner, IBusinessDayGuard guard) : ICommandRunner
{
    public Task<CommandResult<TResult>> RunAsync<TPayload, TResult>(CommandContext context, TPayload payload,
        Func<ICommandTransaction, CancellationToken, Task<TResult>> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        return inner.RunAsync(context, payload, async (tx, c) =>
        {
            await guard.EnsureBusinessDayOpenAsync(tx, context.PropertyId, c).ConfigureAwait(false);
            return await handler(tx, c).ConfigureAwait(false);
        }, ct);
    }
}

/// <summary>Test seam: called at named points of a close run ("preflight", "night", "cash", "barrier", "next-day"). Default does nothing.</summary>
public interface ICloseStepObserver
{
    Task OnStepAsync(int propertyId, DateOnly businessDate, string point, long? stayId, CancellationToken ct);
}

public sealed class NoCloseStepObserver : ICloseStepObserver
{
    public Task OnStepAsync(int propertyId, DateOnly businessDate, string point, long? stayId, CancellationToken ct) => Task.CompletedTask;
}

public sealed record CloseRunRow(long RunId, int PropertyId, DateOnly BusinessDate, string RunRef, string Status, string CurrentStep, int Attempts,
    int StartedBy, int? ClosedBy, DateTime? ClosedUtc);

public sealed record CloseStepRow(string Step, string Status, int Attempt, string Detail, DateTime UpdatedUtc);

public sealed record SnapshotRow(long SnapshotId, DateOnly BusinessDate, string Json, byte[] Sha256, long? ReopenId = null, DateTime? SupersededUtc = null);

/// <param name="ReopenId">DEC-077: the reopen whose re-close writes this record (null for a first close).</param>
public sealed record NewSnapshot(int PropertyId, DateOnly BusinessDate, long RunId, int ClosedBy, DateTime ClosedUtc, ClosingRecord Record, string Json, byte[] Sha256,
    long? ReopenId = null);

/// <summary>One reopen of a closed business day (DEC-077, migration 3600 <c>cls.day_reopen</c>); open while <see cref="ReclosedUtc"/> is null.</summary>
public sealed record DayReopenRow(long ReopenId, int PropertyId, DateOnly BusinessDate, int ReopenedBy, DateTime ReopenedUtc, string Reason, long PriorSnapshotId,
    bool CashBookLocked, bool TallyReexport, int? ReclosedBy, DateTime? ReclosedUtc, long? SnapshotId);

public sealed record NewDayReopen(int PropertyId, DateOnly BusinessDate, int ReopenedBy, DateTime ReopenedUtc, string Reason, long PriorSnapshotId, bool CashBookLocked);

public sealed record CashBookDayRow(DateOnly BusinessDate, bool Locked, int? LockedBy, DateTime? LockedUtc);

/// <summary>
/// Persistence for closing (migration 0500, schema <c>cls</c>) and the reads it needs from other modules' tables. Methods taking a
/// transaction run inside the command; <c>tx = null</c> reads on an own read-committed connection.
/// </summary>
public interface IClosingStore
{
    Task<DateOnly?> BusinessDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task<CloseRunRow?> FindRunAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);

    Task<CloseRunRow> InsertRunAsync(ICommandTransaction tx, int propertyId, DateOnly date, string runRef, int principalId, DateTime nowUtc, CancellationToken ct);

    Task UpdateRunAsync(ICommandTransaction tx, long runId, string status, string currentStep, int? attemptsIncrement, string? runRef, int principalId, DateTime nowUtc,
        CancellationToken ct);

    Task UpsertStepAsync(ICommandTransaction tx, long runId, CloseStep step, string status, int attempt, string detailJson, int principalId, DateTime nowUtc,
        CancellationToken ct);

    Task<IReadOnlyList<CloseStepRow>> StepsAsync(long runId, CancellationToken ct);

    Task<PreflightData> PreflightDataAsync(ICommandTransaction? tx, int propertyId, DateOnly date, DateTime nowUtc, CancellationToken ct);

    Task<bool> IsClosedAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<DateOnly>> ClosedDatesAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Inserts the barrier row (waits for in-flight guarded writers of the property, see <see cref="IBusinessDayGuard"/>).</summary>
    Task InsertClosedDayAsync(ICommandTransaction tx, int propertyId, DateOnly date, long runId, int closedBy, DateTime closedUtc, CancellationToken ct);

    Task<long> InsertSnapshotAsync(ICommandTransaction tx, NewSnapshot snapshot, CancellationToken ct);

    Task<SnapshotRow?> ReadSnapshotAsync(ICommandTransaction? tx, int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>The closing snapshots of the dates in the inclusive range, oldest first.</summary>
    Task<IReadOnlyList<SnapshotRow>> ReadSnapshotsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Moves the business date from <paramref name="date"/> to the next day; false when it had already moved.</summary>
    Task<bool> AdvanceBusinessDateAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct);

    Task<long> DepositsHeldAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    /// <summary>Who counted the day (shift submitters), approved its cash differences, or closed it (DEC-065 segregation).</summary>
    Task<IReadOnlyList<int>> CountersAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct);

    Task<CashBookDayRow?> LockCashBookDayRowAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct);

    Task SetCashBookLockAsync(ICommandTransaction tx, int propertyId, DateOnly date, bool locked, int principalId, DateTime nowUtc, string? reason, CancellationToken ct);

    Task<(CashBookDayRow? Day, IReadOnlyList<CashBookEventView> History)> ReadCashBookDayAsync(int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>A session-level exclusive lease for running the property's close (released on dispose or when the process/connection dies); null when held elsewhere.</summary>
    Task<IAsyncDisposable?> TryAcquireRunLeaseAsync(int propertyId, CancellationToken ct);

    // ── DEC-077 reopen of a closed business day ──

    /// <summary>The property's open reopen, if any. Inside a command the read holds an update lock (concurrent reopens serialize).</summary>
    Task<DayReopenRow?> OpenReopenAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Inserts the open reopen row (<paramref name="tallyReexport"/> = the day had been exported to Tally).</summary>
    Task<long> InsertReopenAsync(ICommandTransaction tx, NewDayReopen reopen, bool tallyReexport, CancellationToken ct);

    /// <summary>
    /// The re-close's barrier: an exclusive lock on the reopened day (writers that post on it hold it shared, see
    /// <see cref="IBusinessDayGuard.IsReopenedAsync"/>), taken before anything is read; then the open reopen row of that day, or null.
    /// </summary>
    Task<DayReopenRow?> LockReopenForCloseAsync(ICommandTransaction tx, int propertyId, DateOnly date, CancellationToken ct);

    /// <summary>Stamps the reopen closed again with the closing record it wrote.</summary>
    Task MarkReclosedAsync(ICommandTransaction tx, long reopenId, int principalId, DateTime nowUtc, long snapshotId, CancellationToken ct);

    /// <summary>Stamps the current closing record of a reopened day as replaced by that reopen's re-close (the one change migration 3600 allows).</summary>
    Task SupersedeSnapshotAsync(ICommandTransaction tx, long snapshotId, long reopenId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Reopens of the dates in the inclusive range plus the open reopen wherever its date is, oldest first.</summary>
    Task<IReadOnlyList<DayReopenRow>> ReopensAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>The replaced (superseded) closing records of the dates in the range, oldest first.</summary>
    Task<IReadOnlyList<SnapshotRow>> ReadReplacedSnapshotsAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Pending, unexpired correction requests whose target line is dated <paramref name="date"/> (the re-close check).</summary>
    Task<IReadOnlyList<PendingApproval>> PendingCorrectionsAsync(ICommandTransaction tx, int propertyId, DateOnly date, DateTime nowUtc, CancellationToken ct);
}

/// <summary>Reads the source records of the daily pack for a range (inside the close transaction, or on an own connection for reports).</summary>
public interface IReportSource
{
    Task<ReportData> LoadAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);
}
