namespace Hcc.Contracts.Health;

// Wire contracts of System health (Wave C3: audit A09-05 + fix wave A "Left open"). Read and retry need PERM-SYS-001 (Owner, Tech admin).
// No payloads and no error text travel here (F-L07): a parked event shows its error TYPE only ("JsonException", "SqlException 547").

/// <summary>
/// One parked outbox event: its consumer failed it <c>OutboxConsumer.ParkAfterFailures</c> times and stopped retrying. Times are UTC
/// (ISO 8601). <c>ConsumerLabel</c> names the consumer for people ("Accounting vouchers").
/// </summary>
public sealed record ParkedEventView(string Consumer, string ConsumerLabel, long OutboxId, string EventType, int Attempts, string ErrorType,
    string FirstFailedUtc, string LastFailedUtc, string ParkedUtc);

/// <summary>One recorded backup or restore drill (<c>plat.backup_run</c>, written by Backup-HccDatabase.ps1 / Restore-HccDrill.ps1).
/// Kind = Full | Differential | PreMigration | RestoreDrill. <c>Reason</c> = why it failed (script text or SQL error numbers).</summary>
public sealed record BackupRunView(string Kind, bool Succeeded, string StartedUtc, string FinishedUtc, string? FileName, long? SizeBytes, string? Reason);

/// <summary>
/// GET /admin/system-health: the parked events of this property (and property-less ones), oldest first; the last recorded backup, the last
/// successful one and the last restore drill (null = none recorded yet); <c>EventLog</c> = "on" (Windows Event Log source HCC registered on
/// the server PC), "off" (not registered: run Install-HccService.ps1) or "unavailable" (not Windows).
/// </summary>
public sealed record SystemHealthView(string CheckedUtc, IReadOnlyList<ParkedEventView> Parked, BackupRunView? LastBackup, string? LastSuccessfulBackupUtc,
    BackupRunView? LastRestoreDrill, string EventLog, IReadOnlyList<DatabaseHealthView>? Databases = null);

/// <summary>
/// LF-docdb (LF-CONTRACT §1.9): one row of the System health "Databases" panel. <c>Kind</c> = main | documents; <c>SchemaVersion</c> is the highest applied
/// migration ("4674" / "0002"); sizes come from <c>sys.database_files</c> inside each database; <c>Pct</c> is data size over the 50 GB Express limit (amber at
/// 70, red at 85). The documents row also carries the object count, content bytes, and the daily reference check (dangling refs, orphan objects).
/// </summary>
public sealed record DatabaseHealthView(string Name, string Kind, string SchemaVersion, bool Reachable, long DataBytes, long LogBytes, long LimitBytes, int Pct,
    int? ObjectCount, long? ContentBytes, int? DanglingRefs, int? OrphanObjects, string? LastBackupUtc, string? LastDrillUtc);

/// <summary>RetryParkedEvent K: release one parked event for one more delivery attempt by the next poll (attempts keep counting; one more failure parks it again).</summary>
public sealed record RetryParkedEventRequest(string Consumer, long OutboxId);

/// <summary>The released event; <c>Attempts</c> = failed attempts so far.</summary>
public sealed record RetryParkedEventResult(string Consumer, long OutboxId, string EventType, int Attempts, bool Released);
