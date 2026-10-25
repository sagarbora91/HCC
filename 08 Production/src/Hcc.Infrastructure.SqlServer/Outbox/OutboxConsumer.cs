using System.Data;
using Hcc.Application.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Outbox;

/// <summary>
/// At-least-once outbox delivery with exactly-once effect per consumer: for each event the consumer has not processed,
/// one transaction records <c>(consumer, outbox_id)</c> under a key lock (check-then-insert, contracts §4 rule 8) and applies
/// the effect; redelivery or a parallel consumer instance finds the record and skips (spike T09).
/// </summary>
public sealed class OutboxConsumer(SqlConnectionFactory connections)
{
    public delegate Task EffectHandler(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct);

    /// <summary>Processes up to <paramref name="batchSize"/> pending events of the given types; returns how many effects were applied.</summary>
    public async Task<int> ConsumeAsync(string consumer, IReadOnlyCollection<string> eventTypes, EffectHandler effect, int batchSize = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentNullException.ThrowIfNull(eventTypes);
        ArgumentNullException.ThrowIfNull(effect);
        if (consumer.Length > 40) throw new ArgumentException("Consumer name is at most 40 characters.", nameof(consumer));

        var pending = new List<OutboxEvent>();
        await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
        await using (var q = connections.Command("""
            SELECT TOP (@n) o.outbox_id, o.event_type, o.aggregate_id, o.payload, o.created_utc
            FROM plat.outbox o
            WHERE o.event_type IN (SELECT value FROM OPENJSON(@types))
              AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = @c AND d.outbox_id = o.outbox_id)
            ORDER BY o.outbox_id;
            """, conn))
        {
            q.Parameters.Add("@n", SqlDbType.Int).Value = batchSize;
            q.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
            q.Parameters.Add("@types", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(eventTypes);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                pending.Add(new OutboxEvent(rd.GetInt64(0), rd.GetString(1), rd.GetInt64(2), rd.GetString(3), rd.GetDateTime(4)));
        }

        var applied = 0;
        foreach (var evt in pending)
        {
            if (await DeliverAsync(consumer, evt, effect, ct).ConfigureAwait(false)) applied++;
        }

        return applied;
    }

    /// <summary>Delivers one event to one consumer; false when it was already processed.</summary>
    public async Task<bool> DeliverAsync(string consumer, OutboxEvent evt, EffectHandler effect, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(effect);
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        try
        {
            await using (var chk = connections.Command("""
                SELECT COUNT(*) FROM plat.consumer_dedup WITH (UPDLOCK, HOLDLOCK) WHERE consumer = @c AND outbox_id = @o;
                """, conn, tx))
            {
                chk.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
                chk.Parameters.Add("@o", SqlDbType.BigInt).Value = evt.OutboxId;
                if ((int)(await chk.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 0)
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    return false;
                }
            }

            await using (var ins = connections.Command("""
                INSERT plat.consumer_dedup(consumer, outbox_id) VALUES (@c, @o);
                UPDATE plat.outbox SET published_utc = ISNULL(published_utc, SYSUTCDATETIME()), attempts = attempts + 1 WHERE outbox_id = @o;
                """, conn, tx))
            {
                ins.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
                ins.Parameters.Add("@o", SqlDbType.BigInt).Value = evt.OutboxId;
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await effect(evt, conn, tx, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            try
            {
                if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // already rolled back (XACT_ABORT)
            }

            throw;
        }
    }
}
