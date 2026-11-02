using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Health;
using Hcc.Domain.Errors;

namespace Hcc.Application.Health;

/// <summary>PERM id of System health (migration 3921): read the panel and retry a parked event — Owner and Tech admin.</summary>
public static class SystemHealthPermissions
{
    public const string Manage = "PERM-SYS-001";
}

/// <summary>A parked outbox event as stored (<c>plat.consumer_failure</c> + <c>plat.outbox</c>). <c>LastError</c> never leaves the server.</summary>
public sealed record ParkedEventRec(string Consumer, long OutboxId, string EventType, int? PropertyId, int Attempts, string LastError,
    DateTime FirstFailedUtc, DateTime LastFailedUtc, DateTime ParkedUtc);

/// <summary>A recorded backup or restore drill (<c>plat.backup_run</c>).</summary>
public sealed record BackupRunRec(string Kind, bool Succeeded, DateTime StartedUtc, DateTime FinishedUtc, string? FileName, long? SizeBytes, string? Reason);

/// <summary>The backup part of the panel.</summary>
public sealed record BackupSummary(BackupRunRec? LastBackup, DateTime? LastSuccessUtc, BackupRunRec? LastRestoreDrill);

/// <summary>Reads and the release write of System health (SQL adapter: <c>SqlSystemHealthStore</c>).</summary>
public interface ISystemHealthStore
{
    /// <summary>Parked, undelivered events of every consumer for this property or without a property, oldest first.</summary>
    Task<IReadOnlyList<ParkedEventRec>> ParkedAsync(int propertyId, CancellationToken ct = default);

    Task<BackupSummary> BackupsAsync(CancellationToken ct = default);

    /// <summary>The parked, undelivered event (row locked for the command), or null.</summary>
    Task<ParkedEventRec?> LockParkedAsync(ICommandTransaction tx, string consumer, long outboxId, CancellationToken ct = default);

    /// <summary>Clears the park and moves the consumer's watermark back below the event (<c>OutboxConsumer.ReleaseParkedSql</c>); false when nothing was released.</summary>
    Task<bool> ReleaseParkedAsync(ICommandTransaction tx, string consumer, long outboxId, CancellationToken ct = default);
}

/// <summary>LF-docdb: the "Databases" block of System health (sizes, versions, last backup per database; SQL adapter <c>SqlDatabaseHealth</c>).</summary>
public interface IDatabaseHealth
{
    Task<IReadOnlyList<DatabaseHealthView>> ReadAsync(CancellationToken ct = default);
}

/// <summary>Whether operator alerts can reach the Windows Event Log on this server ("on" / "off" / "unavailable").</summary>
public interface IOperatorAlertStatus
{
    string EventLog();
}

/// <summary>
/// System health (Wave C3: audit A09-05 and the fix wave A leftovers on parked outbox events). GET /admin/system-health lists the parked
/// events (consumer, event type, attempts, failure times, error TYPE — never the payload or the error text, F-L07) and the last backup
/// and restore-drill results the backup scripts recorded. RetryParkedEvent releases one parked event for one more attempt by the
/// consumer's next poll (attempts keep counting, so a further failure parks it again at once), through <see cref="ICommandRunner"/>
/// (Idempotency-Key + audit <c>OUTBOX_PARKED_RETRY</c>). Both need PERM-SYS-001 (Owner, Tech admin). No dismiss: a parked event stays
/// visible (and keeps blocking the accounting lock) until it is delivered.
/// </summary>
public sealed partial class SystemHealthService(ICommandRunner runner, IAccessPolicy policy, ISystemHealthStore store, IOperatorAlertStatus alerts, TimeProvider clock,
    IDatabaseHealth? databases = null)
{
    public const string OpRetryParked = "RetryParkedEvent";
    public const string AuditRetry = "OUTBOX_PARKED_RETRY";

    /// <summary>The shipped consumers (migration 2900) as people call them; any other consumer shows its technical name.</summary>
    public static readonly IReadOnlyDictionary<string, string> ConsumerLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accounting-vouchers"] = "Accounting vouchers",
        ["housekeeping-tasks"] = "Check-out cleaning tasks",
        ["housekeeping-stayover"] = "Stay-over cleaning tasks",
        ["messaging-triggers"] = "Guest messages",
        ["ota-channel-manager"] = "OTA channel manager",
        ["feedback-followups"] = "Owner follow-up calls (F4-T3)",
    };

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.]{0,59}( -?\d{1,6})?$")]
    private static partial Regex ErrorTypeShape();

    public static string LabelOf(string consumer) => ConsumerLabels.TryGetValue(consumer, out var l) ? l : consumer;

    /// <summary>The error TYPE of a stored failure ("JsonException", "SqlException 547"): the part before the first ": " when it looks like a type, else "Error".</summary>
    public static string ErrorTypeOf(string? lastError)
    {
        if (string.IsNullOrEmpty(lastError)) return "Error";
        var i = lastError.IndexOf(": ", StringComparison.Ordinal);
        var head = i < 0 ? lastError : lastError[..i];
        return ErrorTypeShape().IsMatch(head) ? head : "Error";
    }

    private static string Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static ParkedEventView View(ParkedEventRec p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new ParkedEventView(p.Consumer, LabelOf(p.Consumer), p.OutboxId, p.EventType, p.Attempts, ErrorTypeOf(p.LastError), Utc(p.FirstFailedUtc), Utc(p.LastFailedUtc), Utc(p.ParkedUtc));
    }

    private static BackupRunView? View(BackupRunRec? b) => b is null ? null : new BackupRunView(b.Kind, b.Succeeded, Utc(b.StartedUtc), Utc(b.FinishedUtc), b.FileName, b.SizeBytes, b.Reason);

    public async Task<SystemHealthView> HealthAsync(int propertyId, int principalId, CancellationToken ct = default)
    {
        await policy.RequireAsync(propertyId, principalId, SystemHealthPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var parked = await store.ParkedAsync(propertyId, ct).ConfigureAwait(false);
        var backups = await store.BackupsAsync(ct).ConfigureAwait(false);
        var dbs = databases is null ? null : await databases.ReadAsync(ct).ConfigureAwait(false);
        return new SystemHealthView(Utc(clock.GetUtcNow().UtcDateTime), parked.Select(View).ToList(), View(backups.LastBackup),
            backups.LastSuccessUtc is { } ok ? Utc(ok) : null, View(backups.LastRestoreDrill), alerts.EventLog(), dbs);
    }

    public async Task<CommandResult<RetryParkedEventResult>> RetryParkedAsync(CommandContext ctx, RetryParkedEventRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(ctx.PropertyId, ctx.PrincipalId, SystemHealthPermissions.Manage, null, null, ct).ConfigureAwait(false);
        var consumer = (req.Consumer ?? string.Empty).Trim();
        if (consumer.Length is 0 or > 40) throw HccException.Validation("Choose the consumer of the parked event (1 to 40 characters).");
        if (req.OutboxId <= 0) throw HccException.Validation("Choose the parked event (outbox id).");
        return await runner.RunAsync(ctx with { Operation = OpRetryParked }, req with { Consumer = consumer }, async (tx, c) =>
        {
            var p = await store.LockParkedAsync(tx, consumer, req.OutboxId, c).ConfigureAwait(false);
            // Another property's event is "not found" for this caller (no hint that it exists).
            if (p is null || (p.PropertyId is { } pid && pid != ctx.PropertyId))
                throw new HccException(HccErrorCode.NOT_FOUND, "This parked event was not found: it may have been retried or delivered already. Refresh System health.");
            if (!await store.ReleaseParkedAsync(tx, consumer, req.OutboxId, c).ConfigureAwait(false))
                throw new HccException(HccErrorCode.INVALID_TRANSITION, "This event is no longer parked. Refresh System health.");
            var id = req.OutboxId.ToString(CultureInfo.InvariantCulture);
            await tx.AuditAsync(AuditRetry, "outbox:" + id,
                $"Parked event {id} ({p.EventType}) of {LabelOf(consumer)} [{consumer}] released for one more delivery attempt after {p.Attempts.ToString(CultureInfo.InvariantCulture)} failed attempt(s) ({ErrorTypeOf(p.LastError)})", c).ConfigureAwait(false);
            return new RetryParkedEventResult(consumer, req.OutboxId, p.EventType, p.Attempts, true);
        }, ct).ConfigureAwait(false);
    }
}
