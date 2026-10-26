using System.Data;
using System.Globalization;
using Hcc.Api;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Infrastructure.SqlServer.Access;
using Hcc.Infrastructure.SqlServer.Outbox;
using Hcc.Platform.Tests.Harness;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Hcc.Platform.Tests.Sql;

/// <summary>
/// Fix wave A (audit A08-01, A08-02 / A03b-04): the poll reads from a per-consumer watermark through <c>IX_outbox_type</c>; a poison event
/// is recorded outside the rolled-back effect, parked after <see cref="OutboxConsumer.ParkAfterFailures"/> and does not stop later
/// events; the retention purge removes only delivered, fully consumed, old events.
/// </summary>
[Trait("Category", "Sql")]
public sealed class OutboxConsumerTests : IClassFixture<MigratedDatabaseFixture>, IAsyncLifetime
{
    private readonly DisposableDatabase _db;

    public OutboxConsumerTests(MigratedDatabaseFixture f) => _db = f.Db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _db.ExecAsync("""
        IF OBJECT_ID('dbo.outbox_effect') IS NULL
          CREATE TABLE dbo.outbox_effect(seq BIGINT IDENTITY PRIMARY KEY, consumer VARCHAR(40) NOT NULL, outbox_id BIGINT NOT NULL);
        """, Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task Record(OutboxEvent e, SqlConnection c, SqlTransaction t, string consumer, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("INSERT dbo.outbox_effect(consumer, outbox_id) VALUES (@c, @o);", c, t);
        cmd.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
        cmd.Parameters.Add("@o", SqlDbType.BigInt).Value = e.OutboxId;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Inserts one event; <paramref name="ageMinutes"/> &gt; 0 backdates it past the watermark's settle window.</summary>
    private async Task<long> InsertAsync(string type, string payload = "{}", int ageMinutes = 0)
        => Convert.ToInt64(await _db.ScalarAsync($"""
            INSERT plat.outbox(event_type, aggregate_id, payload, created_utc) OUTPUT INSERTED.outbox_id
            VALUES ('{type}', 1, N'{payload}', DATEADD(MINUTE, -{ageMinutes}, SYSUTCDATETIME()));
            """, Ct), CultureInfo.InvariantCulture);

    private async Task<long> LowAsync(string consumer)
        => Convert.ToInt64(await _db.ScalarAsync($"SELECT low_outbox_id FROM plat.outbox_consumer WHERE consumer = '{consumer}'", Ct), CultureInfo.InvariantCulture);

    private async Task<long[]> EffectsAsync(string consumer)
    {
        var list = new List<long>();
        await using var conn = await _db.OpenAsync(Ct);
        await using var cmd = new SqlCommand($"SELECT outbox_id FROM dbo.outbox_effect WHERE consumer = '{consumer}' ORDER BY seq", conn);
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        while (await rd.ReadAsync(Ct)) list.Add(rd.GetInt64(0));
        return [.. list];
    }

    [Fact]
    public async Task Poll_after_many_delivered_events_reads_from_the_watermark_and_still_picks_up_new_events_in_order()
    {
        const string c = "wm-consumer";
        string[] types = ["WmA", "WmB"];
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_outbox_type' AND object_id = OBJECT_ID('plat.outbox')", Ct));
        // 3,000 old events, two of three of the consumer's types (the third is another module's type).
        await _db.ExecAsync("""
            ;WITH n AS (SELECT TOP (3000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT plat.outbox(event_type, aggregate_id, payload, created_utc)
            SELECT CASE i % 3 WHEN 0 THEN 'WmA' WHEN 1 THEN 'WmB' ELSE 'WmOther' END, i, N'{}', DATEADD(MINUTE, -10, SYSUTCDATETIME()) FROM n ORDER BY i;
            """, Ct);
        var lastOld = Convert.ToInt64(await _db.ScalarAsync("SELECT MAX(outbox_id) FROM plat.outbox WHERE event_type LIKE 'Wm%'", Ct), CultureInfo.InvariantCulture);
        var platform = _db.Platform();
        var total = 0;
        for (var n = -1; n != 0;)
        {
            n = await platform.Outbox.ConsumeAsync(c, types, (e, cn, t, ct) => Record(e, cn, t, c, ct), batchSize: 500, ct: Ct);
            total += n;
        }

        Assert.Equal(2000, total);
        // Everything delivered: the cursor moved to the newest settled event, so the next polls read only the tail.
        Assert.True(await LowAsync(c) >= lastOld, $"watermark {await LowAsync(c)} below {lastOld}");
        Assert.Equal(2, await _db.CountAsync($"SELECT COUNT(*) FROM plat.outbox_subscription WHERE consumer = '{c}' AND event_type IN ('WmA', 'WmB')", Ct));

        // New events (younger than the settle window) after the delivered history are still picked up, in outbox order.
        var b = await InsertAsync("WmB");
        await InsertAsync("WmOther");
        var a = await InsertAsync("WmA");
        Assert.Equal(2, await platform.Outbox.ConsumeAsync(c, types, (e, cn, t, ct) => Record(e, cn, t, c, ct), ct: Ct));
        Assert.Equal([b, a], (await EffectsAsync(c))[^2..]);
        Assert.Equal(0, await platform.Outbox.ConsumeAsync(c, types, (e, cn, t, ct) => Record(e, cn, t, c, ct), ct: Ct));
        Assert.True(await LowAsync(c) < b, "the watermark never passes an event younger than the settle window");
        Assert.Equal(2002, (await EffectsAsync(c)).Length);
    }

    [Fact]
    public async Task A_type_added_to_a_consumer_resets_its_watermark_so_older_events_of_that_type_are_delivered()
    {
        const string c = "wm-added";
        var x = await InsertAsync("NtA", ageMinutes: 10);
        var y = await InsertAsync("NtB", ageMinutes: 10);
        var platform = _db.Platform();
        Assert.Equal(1, await platform.Outbox.ConsumeAsync(c, ["NtA"], (e, cn, t, ct) => Record(e, cn, t, c, ct), ct: Ct));
        Assert.Equal(0, await platform.Outbox.ConsumeAsync(c, ["NtA"], (e, cn, t, ct) => Record(e, cn, t, c, ct), ct: Ct));
        Assert.True(await LowAsync(c) >= y);

        Assert.Equal(1, await platform.Outbox.ConsumeAsync(c, ["NtA", "NtB"], (e, cn, t, ct) => Record(e, cn, t, c, ct), ct: Ct));
        Assert.Equal([x, y], await EffectsAsync(c));
    }

    [Fact]
    public async Task Poison_event_is_recorded_outside_the_rolled_back_effect_parked_after_five_failures_and_later_events_continue()
    {
        const string c = "poison-consumer";
        string[] types = ["PoisonT"];
        var good1 = await InsertAsync("PoisonT", ageMinutes: 10);
        var bad = await InsertAsync("PoisonT", """{"bad":true}""", ageMinutes: 10);
        var good2 = await InsertAsync("PoisonT", ageMinutes: 10);
        var platform = _db.Platform();
        var failures = new List<OutboxDeliveryFailure>();
        platform.Outbox.DeliveryFailed += (_, f) => failures.Add(f);
        var log = new CapturingLogger();
        var alerts = new OutboxFailureLogger(platform, log);
        await alerts.StartAsync(Ct);

        async Task Effect(OutboxEvent e, SqlConnection cn, SqlTransaction t, CancellationToken ct)
        {
            await Record(e, cn, t, c, ct); // written, then rolled back for the bad event
            if (e.PayloadJson.Contains("bad", StringComparison.Ordinal)) throw new InvalidOperationException("Test: unreadable payload.");
        }

        Task<int> Poll() => platform.Outbox.ConsumeAsync(c, types, Effect, ct: Ct);
        Task<int> Attempts() => _db.CountAsync($"SELECT ISNULL(MAX(attempts), 0) FROM plat.consumer_failure WHERE consumer = '{c}' AND outbox_id = {bad}", Ct);

        // Poll 1: the bad event comes first after good1 but does not stop good2.
        Assert.Equal(2, await Poll());
        Assert.Equal([good1, good2], await EffectsAsync(c));
        Assert.Equal(1, await Attempts());
        Assert.Equal("InvalidOperationException: Test: unreadable payload.",
            await _db.ScalarAsync($"SELECT last_error FROM plat.consumer_failure WHERE consumer = '{c}' AND outbox_id = {bad}", Ct));
        Assert.Equal("InvalidOperationException: Test: unreadable payload.", await _db.ScalarAsync($"SELECT last_error FROM plat.outbox WHERE outbox_id = {bad}", Ct));
        Assert.Equal(0, await _db.CountAsync($"SELECT COUNT(*) FROM plat.consumer_dedup WHERE consumer = '{c}' AND outbox_id = {bad}", Ct));
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM plat.consumer_failure WHERE consumer = '{c}' AND parked_utc IS NULL", Ct));
        Assert.True(await LowAsync(c) < bad, "a failing (not yet parked) event holds the watermark");

        // Polls 2–5: a later event is still processed; the fifth failure parks the event.
        var good3 = await InsertAsync("PoisonT", ageMinutes: 10);
        Assert.Equal(1, await Poll());
        for (var i = 3; i <= OutboxConsumer.ParkAfterFailures; i++) Assert.Equal(0, await Poll());
        Assert.Equal(OutboxConsumer.ParkAfterFailures, await Attempts());
        Assert.Equal([good1, good2, good3], await EffectsAsync(c));
        Assert.Equal(OutboxConsumer.ParkAfterFailures, failures.Count);
        Assert.All(failures.SkipLast(1), f => Assert.False(f.Parked));
        var parked = failures[^1];
        Assert.True(parked.Parked);
        Assert.Equal((c, bad, "PoisonT", "InvalidOperationException"), (parked.Consumer, parked.OutboxId, parked.EventType, parked.ErrorType));
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("PARKED", StringComparison.Ordinal)
            && e.Message.Contains(c, StringComparison.Ordinal) && e.Message.Contains(bad.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            && e.Message.Contains("PoisonT", StringComparison.Ordinal) && e.Message.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("unreadable payload", StringComparison.Ordinal)); // F-L07

        // Parked: no more retries, visible to operators, and the watermark moves past it.
        Assert.Equal(0, await Poll());
        Assert.Equal(OutboxConsumer.ParkAfterFailures, await Attempts());
        var view = Assert.Single(await platform.Outbox.ParkedAsync(c, Ct));
        Assert.Equal((bad, "PoisonT", OutboxConsumer.ParkAfterFailures), (view.OutboxId, view.EventType, view.Attempts));
        Assert.StartsWith("InvalidOperationException", view.LastError, StringComparison.Ordinal);
        Assert.True(await LowAsync(c) >= good3);

        // A direct delivery of a failing event is recorded the same way and still throws.
        await alerts.StopAsync(Ct);
        var other = new OutboxEvent(bad, "PoisonT", 1, """{"bad":true}""", DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => platform.Outbox.DeliverAsync("poison-direct", other, Effect, Ct));
        Assert.Equal(1, await _db.CountAsync($"SELECT attempts FROM plat.consumer_failure WHERE consumer = 'poison-direct' AND outbox_id = {bad}", Ct));
    }

    [Fact]
    public async Task Purge_removes_only_delivered_old_events_that_every_subscribed_consumer_processed()
    {
        await _db.ExecAsync("""
            INSERT plat.outbox_consumer(consumer) VALUES ('pg-a'), ('pg-b');
            INSERT plat.outbox_subscription(consumer, event_type) VALUES ('pg-a', 'PgX'), ('pg-b', 'PgX'), ('pg-b', 'PgY');
            IF NOT EXISTS (SELECT 1 FROM inv.property) INSERT inv.property(code, name, time_zone, business_date) VALUES ('PURGE', N'Purge test', 'Asia/Kolkata', '2026-10-24');
            """, Ct);

        async Task<long> Event(string type, int ageDays, params string[] processedBy)
        {
            var id = Convert.ToInt64(await _db.ScalarAsync($"""
                INSERT plat.outbox(event_type, aggregate_id, payload, created_utc, published_utc) OUTPUT INSERTED.outbox_id
                VALUES ('{type}', 1, N'[]', DATEADD(DAY, -{ageDays}, SYSUTCDATETIME()), DATEADD(DAY, -{ageDays}, SYSUTCDATETIME()));
                """, Ct), CultureInfo.InvariantCulture);
            foreach (var c in processedBy) await _db.ExecAsync($"INSERT plat.consumer_dedup(consumer, outbox_id) VALUES ('{c}', {id});", Ct);
            return id;
        }

        var allDone = await Event("PgX", 40, "pg-a", "pg-b");
        var oneMissing = await Event("PgX", 40, "pg-a");
        var otherType = await Event("PgY", 40, "pg-b");
        var recent = await Event("PgX", 10, "pg-a", "pg-b");
        var parked = await Event("PgY", 40);
        await _db.ExecAsync($"""
            INSERT plat.consumer_failure(consumer, outbox_id, attempts, last_error, first_failed_utc, last_failed_utc, parked_utc)
            VALUES ('pg-b', {parked}, 5, N'test', SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
            """, Ct);
        var retriedThenDone = await Event("PgX", 40, "pg-a", "pg-b");
        await _db.ExecAsync($"""
            INSERT plat.consumer_failure(consumer, outbox_id, attempts, last_error, first_failed_utc, last_failed_utc)
            VALUES ('pg-a', {retriedThenDone}, 2, N'transient', SYSUTCDATETIME(), SYSUTCDATETIME());
            """, Ct);
        var booked = await Event("PgX", 40, "pg-a", "pg-b");
        await _db.ExecAsync($"""
            INSERT acc.source(property_id, source_key, source_kind, sub, voucher_type, business_date, narration, event_type, outbox_id, received_utc)
            SELECT TOP (1) property_id, 'purge-test:{booked}', 'line', 'room', 'Sales', '2026-09-15', N'test', 'PgX', {booked}, SYSUTCDATETIME() FROM inv.property;
            """, Ct);
        var undelivered = Convert.ToInt64(await _db.ScalarAsync("""
            INSERT plat.outbox(event_type, aggregate_id, payload, created_utc) OUTPUT INSERTED.outbox_id VALUES ('PgNobody', 1, N'{}', DATEADD(DAY, -40, SYSUTCDATETIME()));
            """, Ct), CultureInfo.InvariantCulture);

        var result = await new SqlRetentionPurge(_db.Platform().Connections, new AccessOptions(), TimeProvider.System).RunOnceAsync(Ct);

        Assert.Equal(3, result.OutboxEvents);
        async Task<bool> Exists(long id) => await _db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE outbox_id = {id}", Ct) == 1;
        foreach (var gone in new[] { allDone, otherType, retriedThenDone }) Assert.False(await Exists(gone), $"event {gone} should be purged");
        foreach (var kept in new[] { oneMissing, recent, parked, booked, undelivered }) Assert.True(await Exists(kept), $"event {kept} should be kept");
        Assert.Equal(0, await _db.CountAsync($"SELECT COUNT(*) FROM plat.consumer_dedup WHERE outbox_id IN ({allDone}, {otherType}, {retriedThenDone})", Ct));
        Assert.Equal(0, await _db.CountAsync($"SELECT COUNT(*) FROM plat.consumer_failure WHERE outbox_id = {retriedThenDone}", Ct));
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM plat.consumer_dedup WHERE outbox_id = {oneMissing}", Ct));
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM plat.consumer_failure WHERE outbox_id = {parked} AND parked_utc IS NOT NULL", Ct));
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'RETENTION_PURGE' AND detail LIKE '%3 processed outbox event(s) older than 30 days%'", Ct));
        Assert.Equal(TimeSpan.FromDays(30), new AccessOptions().OutboxRetention);
    }

    private sealed class CapturingLogger : ILogger<OutboxFailureLogger>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
