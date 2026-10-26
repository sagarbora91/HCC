using System.Data;
using Hcc.Application.Access;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>What one retention pass removed.</summary>
public sealed record RetentionPurgeResult(int IdempotencyRecords, int Sessions, int OutboxEvents = 0);

/// <summary>
/// F-L03 retention: idempotency records (stored command results, which can hold guest names and phones) older than
/// <see cref="AccessOptions.IdempotencyRetention"/> (30 days) and ended sessions (revoked, or past their absolute expiry) older than
/// <see cref="AccessOptions.SessionRetention"/> (90 days). Deletes in batches; a replay of a purged key simply runs the command again,
/// which every command already treats as a new request. A08-01: delivered outbox events older than
/// <see cref="AccessOptions.OutboxRetention"/> (30 days) that every subscribed consumer (<c>plat.outbox_subscription</c>) has processed,
/// with their dedup and failure rows. Never an unprocessed or parked event, and never one an accounting source (<c>acc.source</c>,
/// append-only) points to. One audit row per pass that removed something.
/// </summary>
public sealed class SqlRetentionPurge(SqlConnectionFactory connections, AccessOptions options, TimeProvider clock)
{
    private const int Batch = 5000;

    /// <summary>One batch of fully processed old outbox events (dedup and failure rows first: they reference the event). Returns the count.</summary>
    private const string OutboxPurgeSql = """
        SET NOCOUNT ON;
        DECLARE @ids TABLE(outbox_id BIGINT PRIMARY KEY);
        BEGIN TRANSACTION;
        INSERT @ids(outbox_id)
        SELECT TOP (@n) o.outbox_id FROM plat.outbox o
         WHERE o.created_utc < @cut AND o.published_utc IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM plat.outbox_subscription s WHERE s.event_type = o.event_type
                            AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = s.consumer AND d.outbox_id = o.outbox_id))
           AND NOT EXISTS (SELECT 1 FROM plat.consumer_failure f WHERE f.outbox_id = o.outbox_id AND f.parked_utc IS NOT NULL)
           AND NOT EXISTS (SELECT 1 FROM acc.source x WHERE x.outbox_id = o.outbox_id)
         ORDER BY o.outbox_id;
        DELETE d FROM plat.consumer_dedup d JOIN @ids i ON i.outbox_id = d.outbox_id;
        DELETE f FROM plat.consumer_failure f JOIN @ids i ON i.outbox_id = f.outbox_id;
        DELETE o FROM plat.outbox o JOIN @ids i ON i.outbox_id = o.outbox_id;
        DECLARE @deleted INT = @@ROWCOUNT;
        COMMIT;
        SELECT @deleted;
        """;

    public async Task<RetentionPurgeResult> RunOnceAsync(CancellationToken ct = default)
    {
        var now = SqlSessionService.Truncate(clock.GetUtcNow().UtcDateTime);
        var idem = await DeleteAsync("DELETE TOP (@n) FROM plat.idempotency WHERE created_utc < @cut;", now - options.IdempotencyRetention, ct).ConfigureAwait(false);
        var sessions = await DeleteAsync("""
            DELETE TOP (@n) FROM plat.session
             WHERE (revoked_utc IS NOT NULL AND revoked_utc < @cut) OR (revoked_utc IS NULL AND absolute_expires_utc < @cut);
            """, now - options.SessionRetention, ct).ConfigureAwait(false);
        var outbox = await PurgeOutboxAsync(now - options.OutboxRetention, ct).ConfigureAwait(false);
        if (idem + sessions + outbox > 0)
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await AccessAudit.WriteAsync(connections, conn, null, 0, 0, "RETENTION_PURGE", "platform",
                $"Removed {idem} idempotency record(s) older than {options.IdempotencyRetention.TotalDays:0} days, {sessions} ended session(s) older than {options.SessionRetention.TotalDays:0} days and {outbox} processed outbox event(s) older than {options.OutboxRetention.TotalDays:0} days.",
                "RetentionPurge", ct).ConfigureAwait(false);
        }

        return new RetentionPurgeResult(idem, sessions, outbox);
    }

    private async Task<int> DeleteAsync(string sql, DateTime cutoff, CancellationToken ct)
    {
        var total = 0;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        while (true)
        {
            await using var cmd = connections.Command(sql, conn);
            cmd.Parameters.Add("@n", SqlDbType.Int).Value = Batch;
            cmd.Parameters.Add("@cut", SqlDbType.DateTime2).Value = cutoff;
            var n = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            total += n;
            if (n < Batch) return total;
        }
    }

    private async Task<int> PurgeOutboxAsync(DateTime cutoff, CancellationToken ct)
    {
        var total = 0;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        while (true)
        {
            await using var cmd = connections.Command(OutboxPurgeSql, conn);
            cmd.Parameters.Add("@n", SqlDbType.Int).Value = Batch;
            cmd.Parameters.Add("@cut", SqlDbType.DateTime2).Value = cutoff;
            var n = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            total += n;
            if (n < Batch) return total;
        }
    }
}
