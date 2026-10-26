using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Outbox;

/// <summary>One failed delivery of an event to a consumer, as recorded in <c>plat.consumer_failure</c>; <see cref="Parked"/> = no more retries.</summary>
public sealed class OutboxDeliveryFailure(string consumer, long outboxId, string eventType, int attempts, bool parked, string errorType) : EventArgs
{
    public string Consumer { get; } = consumer;

    public long OutboxId { get; } = outboxId;

    public string EventType { get; } = eventType;

    public int Attempts { get; } = attempts;

    public bool Parked { get; } = parked;

    /// <summary>Exception type (and SQL error number); the message itself is stored only in the database (F-L07: no exception text in logs).</summary>
    public string ErrorType { get; } = errorType;
}

/// <summary>A parked event of one consumer (operator view; parked events still block locking an accounting period).</summary>
public sealed record OutboxParkedEvent(string Consumer, long OutboxId, string EventType, int? PropertyId, int Attempts, string LastError,
    DateTime FirstFailedUtc, DateTime LastFailedUtc, DateTime ParkedUtc);

/// <summary>
/// At-least-once outbox delivery with exactly-once effect per consumer: for each event the consumer has not processed,
/// one transaction records <c>(consumer, outbox_id)</c> under a key lock (check-then-insert, contracts §4 rule 8) and applies
/// the effect; redelivery or a parallel consumer instance finds the record and skips (spike T09).
/// A08-01: each poll reads only the tail of the outbox: from the consumer's low watermark (<c>plat.outbox_consumer</c>; every subscribed
/// event at or below it is processed or parked) through <c>IX_outbox_type</c>, with the types as an IN list of parameters.
/// A08-02: a failing effect rolls back, then its attempt and error are recorded in a separate transaction; after
/// <see cref="ParkAfterFailures"/> failures the event is parked and <see cref="DeliveryFailed"/> reports it; later events keep flowing.
/// </summary>
public sealed class OutboxConsumer(SqlConnectionFactory connections)
{
    /// <summary>Failures of one event for one consumer after which it is parked (no more retries; visible in <see cref="ParkedAsync"/>).</summary>
    public const int ParkAfterFailures = 5;

    /// <summary>
    /// The watermark never passes an event younger than this (SQL Server clock): an identity value is taken just before its row is
    /// written, so a very new gap may still fill. In-flight transactions are covered by the locking read of the poll.
    /// </summary>
    public const int SettleSeconds = 60;

    private readonly ConcurrentDictionary<string, bool> _registered = new(StringComparer.Ordinal);

    public delegate Task EffectHandler(OutboxEvent evt, SqlConnection connection, SqlTransaction transaction, CancellationToken ct);

    /// <summary>Raised after every recorded delivery failure (also when it parks the event). Handlers must not throw.</summary>
    public event EventHandler<OutboxDeliveryFailure>? DeliveryFailed;

    /// <summary>Processes up to <paramref name="batchSize"/> pending events of the given types; returns how many effects were applied.</summary>
    public async Task<int> ConsumeAsync(string consumer, IReadOnlyCollection<string> eventTypes, EffectHandler effect, int batchSize = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentNullException.ThrowIfNull(eventTypes);
        ArgumentNullException.ThrowIfNull(effect);
        if (consumer.Length > 40) throw new ArgumentException("Consumer name is at most 40 characters.", nameof(consumer));
        var types = eventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (types.Length == 0) return 0;
        if (types.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 60)) throw new ArgumentException("Event types are 1 to 60 characters.", nameof(eventTypes));
        await RegisterAsync(consumer, types, ct).ConfigureAwait(false);
        return await RetryOnDeadlockAsync(() => ConsumeOnceAsync(consumer, types, effect, batchSize, ct), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Parallel consumers (several projector workers, redelivery) can deadlock on <c>plat.consumer_dedup</c> / <c>plat.outbox</c> (the
    /// pending read's NOT EXISTS against a delivery's key lock + outbox update). SQL Server rolls the victim back completely (error 1205),
    /// so a retry is safe: the dedup row decides exactly-once on the next attempt.
    /// </summary>
    private static async Task<T> RetryOnDeadlockAsync<T>(Func<Task<T>> run, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await run().ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number == 1205 && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary><c>(@t0), (@t1), …</c>: the event types as a VALUES row source (parameters, never OPENJSON inside the scan).</summary>
    private static string TypeRows(int count) => string.Join(", ", Enumerable.Range(0, count).Select(i => "(@t" + i.ToString(CultureInfo.InvariantCulture) + ")"));

    private static void AddTypes(SqlCommand cmd, string[] types)
    {
        for (var i = 0; i < types.Length; i++) cmd.Parameters.Add("@t" + i.ToString(CultureInfo.InvariantCulture), SqlDbType.VarChar, 60).Value = types[i];
    }

    /// <summary>
    /// Records the consumer and its event types (once per process and type set). A type new to an existing consumer resets its watermark
    /// to 0, so events of that type published before the subscription are still delivered (the next poll walks the history once).
    /// </summary>
    private async Task RegisterAsync(string consumer, string[] types, CancellationToken ct)
    {
        var key = consumer + "|" + string.Join(",", types);
        if (_registered.ContainsKey(key)) return;
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var cmd = connections.Command($"""
            IF NOT EXISTS (SELECT 1 FROM plat.outbox_consumer WITH (UPDLOCK, HOLDLOCK) WHERE consumer = @c)
              INSERT plat.outbox_consumer(consumer) VALUES (@c);
            INSERT plat.outbox_subscription(consumer, event_type)
            SELECT @c, v.event_type FROM (VALUES {TypeRows(types.Length)}) v(event_type)
            WHERE NOT EXISTS (SELECT 1 FROM plat.outbox_subscription s WHERE s.consumer = @c AND s.event_type = v.event_type);
            IF @@ROWCOUNT > 0 UPDATE plat.outbox_consumer SET low_outbox_id = 0, updated_utc = SYSUTCDATETIME() WHERE consumer = @c;
            """, conn, tx))
        {
            cmd.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
            AddTypes(cmd, types);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        _registered[key] = true;
    }

    private async Task<int> ConsumeOnceAsync(string consumer, string[] types, EffectHandler effect, int batchSize, CancellationToken ct)
    {
        // The pending read is a LOCKING read (also under a future READ_COMMITTED_SNAPSHOT): an event of a subscribed type that is still being
        // written blocks the scan until it commits, so the watermark below the first pending event never passes an unseen one.
        // One seek per type on (event_type, outbox_id > @low) via CROSS APPLY: a plain IN list of parameters becomes a range seek on
        // event_type alone (Merge Interval) and reads every row of those types, which is what this fix removes.
        var pending = new List<OutboxEvent>();
        await using (var conn = await connections.OpenAsync(ct).ConfigureAwait(false))
        await using (var q = connections.Command($"""
            DECLARE @low BIGINT = ISNULL((SELECT low_outbox_id FROM plat.outbox_consumer WHERE consumer = @c), 0);
            DECLARE @p TABLE(outbox_id BIGINT PRIMARY KEY);
            INSERT @p(outbox_id)
            SELECT TOP (@n) x.outbox_id
            FROM (VALUES {TypeRows(types.Length)}) t(event_type)
            CROSS APPLY (SELECT TOP (@n) o.outbox_id
                         FROM plat.outbox o WITH (READCOMMITTEDLOCK, INDEX(IX_outbox_type))
                         WHERE o.event_type = t.event_type AND o.outbox_id > @low
                           AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = @c AND d.outbox_id = o.outbox_id)
                           AND NOT EXISTS (SELECT 1 FROM plat.consumer_failure f WHERE f.consumer = @c AND f.outbox_id = o.outbox_id AND f.parked_utc IS NOT NULL)
                         ORDER BY o.outbox_id) x
            ORDER BY x.outbox_id;
            DECLARE @settled BIGINT = ISNULL((SELECT TOP (1) outbox_id FROM plat.outbox WITH (READPAST)
                                              WHERE created_utc < DATEADD(SECOND, -@settle, SYSUTCDATETIME()) ORDER BY outbox_id DESC), 0);
            DECLARE @first BIGINT = (SELECT MIN(outbox_id) FROM @p);
            DECLARE @new BIGINT = CASE WHEN @first IS NOT NULL AND @first - 1 < @settled THEN @first - 1 ELSE @settled END;
            IF @new > @low
              UPDATE plat.outbox_consumer SET low_outbox_id = @new, updated_utc = SYSUTCDATETIME() WHERE consumer = @c AND low_outbox_id < @new;
            SELECT o.outbox_id, o.event_type, o.aggregate_id, o.payload, o.created_utc
            FROM @p p JOIN plat.outbox o ON o.outbox_id = p.outbox_id
            ORDER BY o.outbox_id;
            """, conn))
        {
            q.Parameters.Add("@n", SqlDbType.Int).Value = batchSize;
            q.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
            q.Parameters.Add("@settle", SqlDbType.Int).Value = SettleSeconds;
            AddTypes(q, types);
            await using var rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                pending.Add(new OutboxEvent(rd.GetInt64(0), rd.GetString(1), rd.GetInt64(2), rd.GetString(3), rd.GetDateTime(4)));
        }

        var applied = 0;
        foreach (var evt in pending)
        {
            // A failed event is recorded (and parked after N failures) by TryDeliverAsync; the later events are still delivered.
            if ((await TryDeliverAsync(consumer, evt, effect, ct).ConfigureAwait(false)).Applied) applied++;
        }

        return applied;
    }

    /// <summary>
    /// Delivers one event to one consumer; false when it was already processed. A deadlock victim is retried (see <see cref="RetryOnDeadlockAsync"/>).
    /// A failing effect is rolled back, recorded as a failed attempt (parked after <see cref="ParkAfterFailures"/>) and rethrown.
    /// </summary>
    public async Task<bool> DeliverAsync(string consumer, OutboxEvent evt, EffectHandler effect, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(effect);
        var (applied, error) = await TryDeliverAsync(consumer, evt, effect, ct).ConfigureAwait(false);
        error?.Throw();
        return applied;
    }

    /// <summary>
    /// Cancellation and a lost database connection are not the event's fault: they propagate and count nothing. Any other exception
    /// counts as a failed attempt of this event, recorded in its own transaction after the effect transaction rolled back.
    /// </summary>
    private async Task<(bool Applied, ExceptionDispatchInfo? Error)> TryDeliverAsync(string consumer, OutboxEvent evt, EffectHandler effect, CancellationToken ct)
    {
        try
        {
            return (await RetryOnDeadlockAsync(() => DeliverOnceAsync(consumer, evt, effect, ct), ct).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.UNAVAILABLE && ex.InnerException is SqlException)
        {
            throw;
        }
#pragma warning disable CA1031 // Any effect failure is recorded against the event (A08-02); the caller decides whether to rethrow it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var failure = await RecordFailureAsync(consumer, evt, ex, ct).ConfigureAwait(false);
            DeliveryFailed?.Invoke(this, failure);
            return (false, ExceptionDispatchInfo.Capture(ex));
        }
    }

    private static string ErrorTypeOf(Exception ex) => ex is SqlException sql ? $"SqlException {sql.Number.ToString(CultureInfo.InvariantCulture)}" : ex.GetType().Name;

    private async Task<OutboxDeliveryFailure> RecordFailureAsync(string consumer, OutboxEvent evt, Exception ex, CancellationToken ct)
    {
        var error = ErrorTypeOf(ex) + ": " + ex.Message;
        if (error.Length > 1000) error = error[..1000];
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            UPDATE plat.consumer_failure WITH (UPDLOCK, HOLDLOCK)
               SET attempts = attempts + 1, last_error = @e, last_failed_utc = SYSUTCDATETIME(),
                   parked_utc = CASE WHEN parked_utc IS NULL AND attempts + 1 >= @park THEN SYSUTCDATETIME() ELSE parked_utc END
             WHERE consumer = @c AND outbox_id = @o;
            IF @@ROWCOUNT = 0
              INSERT plat.consumer_failure(consumer, outbox_id, attempts, last_error, first_failed_utc, last_failed_utc, parked_utc)
              VALUES (@c, @o, 1, @e, SYSUTCDATETIME(), SYSUTCDATETIME(), CASE WHEN @park <= 1 THEN SYSUTCDATETIME() END);
            UPDATE plat.outbox SET attempts = attempts + 1, last_error = @e WHERE outbox_id = @o;
            SELECT attempts, CASE WHEN parked_utc IS NULL THEN 0 ELSE 1 END FROM plat.consumer_failure WHERE consumer = @c AND outbox_id = @o;
            """, conn, tx);
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
        cmd.Parameters.Add("@o", SqlDbType.BigInt).Value = evt.OutboxId;
        cmd.Parameters.Add("@e", SqlDbType.NVarChar, 1000).Value = error;
        cmd.Parameters.Add("@park", SqlDbType.Int).Value = ParkAfterFailures;
        int attempts;
        bool parked;
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            await rd.ReadAsync(ct).ConfigureAwait(false);
            (attempts, parked) = (rd.GetInt32(0), rd.GetInt32(1) == 1);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new OutboxDeliveryFailure(consumer, evt.OutboxId, evt.EventType, attempts, parked, ErrorTypeOf(ex));
    }

    /// <summary>Parked events (all consumers, or one), oldest first. They are not booked: accounting still counts them as pending.</summary>
    public async Task<IReadOnlyList<OutboxParkedEvent>> ParkedAsync(string? consumer = null, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT f.consumer, f.outbox_id, o.event_type, o.property_id, f.attempts, f.last_error, f.first_failed_utc, f.last_failed_utc, f.parked_utc
            FROM plat.consumer_failure f JOIN plat.outbox o ON o.outbox_id = f.outbox_id
            WHERE f.parked_utc IS NOT NULL AND (@c IS NULL OR f.consumer = @c)
              AND NOT EXISTS (SELECT 1 FROM plat.consumer_dedup d WHERE d.consumer = f.consumer AND d.outbox_id = f.outbox_id)
            ORDER BY f.outbox_id, f.consumer;
            """, conn);
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = (object?)consumer ?? DBNull.Value;
        var list = new List<OutboxParkedEvent>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new OutboxParkedEvent(rd.GetString(0), rd.GetInt64(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetInt32(3), rd.GetInt32(4), rd.GetString(5),
                DateTime.SpecifyKind(rd.GetDateTime(6), DateTimeKind.Utc), DateTime.SpecifyKind(rd.GetDateTime(7), DateTimeKind.Utc), DateTime.SpecifyKind(rd.GetDateTime(8), DateTimeKind.Utc)));
        }

        return list;
    }

    private async Task<bool> DeliverOnceAsync(string consumer, OutboxEvent evt, EffectHandler effect, CancellationToken ct)
    {
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
