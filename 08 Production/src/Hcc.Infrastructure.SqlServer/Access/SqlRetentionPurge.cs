using System.Data;
using Hcc.Application.Access;
using Hcc.Infrastructure.SqlServer.Connection;

namespace Hcc.Infrastructure.SqlServer.Access;

/// <summary>What one retention pass removed.</summary>
public sealed record RetentionPurgeResult(int IdempotencyRecords, int Sessions, int OutboxEvents = 0, int BankingSlips = 0);

/// <summary>
/// F-L03 retention: idempotency records (stored command results, which can hold guest names and phones) older than
/// <see cref="AccessOptions.IdempotencyRetention"/> (30 days) and ended sessions (revoked, or past their absolute expiry) older than
/// <see cref="AccessOptions.SessionRetention"/> (90 days). Deletes in batches; a replay of a purged key simply runs the command again,
/// which every command already treats as a new request. A08-01: delivered outbox events older than
/// <see cref="AccessOptions.OutboxRetention"/> (30 days) that every subscribed consumer (<c>plat.outbox_subscription</c>) has processed,
/// with their dedup and failure rows. Never an unprocessed or parked event, and never one an accounting source (<c>acc.source</c>,
/// append-only) points to. DEC-073: bank-deposit slip photos (<c>pay.banking_slip</c>) recorded more than
/// <see cref="AccessOptions.BankingSlipRetention"/> (600 days) ago; the banking record stays and is stamped <c>slip_purged_utc</c>.
/// One audit row per pass that removed something.
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

    /// <summary>One batch of slip photos past retention: the photo row is deleted, the banking record is stamped. Returns the count.</summary>
    private const string SlipPurgeSql = """
        SET NOCOUNT ON;
        DECLARE @ids TABLE(transfer_id BIGINT PRIMARY KEY);
        BEGIN TRANSACTION;
        INSERT @ids(transfer_id)
        SELECT TOP (@n) t.transfer_id FROM pay.cash_transfer t WITH (UPDLOCK)
         WHERE t.kind = 'banking' AND t.slip_purged_utc IS NULL AND t.recorded_utc < @cut ORDER BY t.transfer_id;
        -- LF-docdb (contract §1.7): the photo's object in HccDocuments is released by the document-release consumer.
        INSERT plat.outbox(event_type, aggregate_id, payload, property_id)
        SELECT 'DocumentObjectReleased', b.transfer_id, CONCAT('{"objectRef":"', b.object_ref, '","kind":"banking-slip"}'), t.property_id
          FROM pay.banking_slip b JOIN @ids i ON i.transfer_id = b.transfer_id JOIN pay.cash_transfer t ON t.transfer_id = b.transfer_id
         WHERE b.object_ref IS NOT NULL;
        DELETE b FROM pay.banking_slip b JOIN @ids i ON i.transfer_id = b.transfer_id;
        UPDATE t SET slip_purged_utc = @now FROM pay.cash_transfer t JOIN @ids i ON i.transfer_id = t.transfer_id;
        DECLARE @done INT = @@ROWCOUNT;
        COMMIT;
        SELECT @done;
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
        var slips = await PurgeSlipsAsync(now - options.BankingSlipRetention, now, ct).ConfigureAwait(false);
        if (slips > 0)
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await AccessAudit.WriteAsync(connections, conn, null, 0, 0, "RETENTION_PURGE", "banking-slip",
                $"Deleted {slips} bank-deposit slip photo(s) recorded more than {options.BankingSlipRetention.TotalDays:0} days ago; the deposit records are kept (DEC-073).",
                "RetentionPurge", ct).ConfigureAwait(false);
        }

        if (idem + sessions + outbox > 0)
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await AccessAudit.WriteAsync(connections, conn, null, 0, 0, "RETENTION_PURGE", "platform",
                $"Removed {idem} idempotency record(s) older than {options.IdempotencyRetention.TotalDays:0} days, {sessions} ended session(s) older than {options.SessionRetention.TotalDays:0} days and {outbox} processed outbox event(s) older than {options.OutboxRetention.TotalDays:0} days.",
                "RetentionPurge", ct).ConfigureAwait(false);
        }

        return new RetentionPurgeResult(idem, sessions, outbox, slips);
    }

    private async Task<int> PurgeSlipsAsync(DateTime cutoff, DateTime now, CancellationToken ct)
    {
        var total = 0;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        while (true)
        {
            await using var cmd = connections.Command(SlipPurgeSql, conn);
            cmd.Parameters.Add("@n", SqlDbType.Int).Value = Batch;
            cmd.Parameters.Add("@cut", SqlDbType.DateTime2).Value = cutoff;
            cmd.Parameters.Add("@now", SqlDbType.DateTime2).Value = now;
            var n = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            total += n;
            if (n < Batch) return total;
        }
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
