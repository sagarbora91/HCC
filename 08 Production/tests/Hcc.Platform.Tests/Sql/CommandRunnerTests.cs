using System.Data;
using System.Text.Json.Nodes;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Platform.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Platform.Tests.Sql;

public sealed record TestPayload(long AmountPaise, string Note);

public sealed record TestResult(long EffectId, string Note);

/// <summary>Idempotency + audit + outbox committed atomically by <see cref="SqlCommandRunner"/> (contracts §2, §4 rule 6; spike T08/T09).</summary>
[Trait("Category", "Sql")]
public class CommandRunnerTests : IClassFixture<MigratedDatabaseFixture>, IAsyncLifetime
{
    private readonly DisposableDatabase _db;

    public CommandRunnerTests(MigratedDatabaseFixture f) => _db = f.Db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _db.ExecAsync(
        "IF OBJECT_ID('dbo.test_effect') IS NULL CREATE TABLE dbo.test_effect(effect_id BIGINT IDENTITY PRIMARY KEY, idem_key VARCHAR(80) NOT NULL, note NVARCHAR(200) NOT NULL);", Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A command handler that writes a business row + audit + outbox; optionally fails afterwards.</summary>
    private static Func<ICommandTransaction, CancellationToken, Task<TestResult>> Handler(TestPayload p, bool failAfterWrites = false, bool audit = true) => async (tx, ct) =>
    {
        var sql = (SqlCommandTransaction)tx;
        await using var cmd = sql.Command("INSERT dbo.test_effect(idem_key, note) OUTPUT INSERTED.effect_id VALUES (@k, @n);");
        cmd.Parameters.Add("@k", SqlDbType.VarChar, 80).Value = tx.Context.IdempotencyKey;
        cmd.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = p.Note;
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        if (audit) await tx.AuditAsync("TEST", "effect-" + id, p.Note, ct);
        await tx.PublishAsync("TestEffectCreated", id, new { effectId = id, amountPaise = p.AmountPaise }, ct);
        if (failAfterWrites) throw new HccException(HccErrorCode.INVALID_TRANSITION, "Rule failed after writes.");
        return new TestResult(id, p.Note);
    };

    private async Task<(int Effects, int Audit, int Outbox, int Idem)> CountsAsync(string key) => (
        await _db.CountAsync($"SELECT COUNT(*) FROM dbo.test_effect WHERE idem_key = '{key}'", Ct),
        await _db.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE idem_key = '{key}'", Ct),
        await _db.CountAsync($"SELECT COUNT(*) FROM plat.outbox o JOIN dbo.test_effect e ON e.effect_id = o.aggregate_id WHERE o.event_type = 'TestEffectCreated' AND e.idem_key = '{key}'", Ct),
        await _db.CountAsync($"SELECT COUNT(*) FROM plat.idempotency WHERE idem_key = '{key}'", Ct));

    [Fact]
    public async Task Same_key_same_payload_replays_the_stored_result_without_a_second_effect()
    {
        var runner = _db.Platform().Commands;
        var ctx = new CommandContext(1, 7, "RecordReceipt", "k-replay");
        var p = new TestPayload(100000, "first");
        var first = await runner.RunAsync(ctx, p, Handler(p), Ct);
        var again = await runner.RunAsync(ctx, p with { }, Handler(p), Ct);
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Value, again.Value);
        Assert.Equal((1, 1, 1, 1), await CountsAsync("k-replay"));
    }

    [Fact]
    public async Task Payload_hash_is_canonical_so_key_order_does_not_matter()
    {
        var runner = _db.Platform().Commands;
        var ctx = new CommandContext(1, 7, "RecordReceipt", "k-canonical");
        var p = new TestPayload(5, "x");
        await runner.RunAsync(ctx, JsonNode.Parse("""{"amountPaise":5,"note":"x"}"""), Handler(p), Ct);
        var again = await runner.RunAsync(ctx, JsonNode.Parse("""{"note":"x","amountPaise":5}"""), Handler(p), Ct);
        Assert.True(again.Replayed);
    }

    [Fact]
    public async Task Same_key_different_payload_is_IDEMPOTENCY_MISMATCH_and_changes_nothing()
    {
        var runner = _db.Platform().Commands;
        var ctx = new CommandContext(1, 7, "RecordReceipt", "k-mismatch");
        var p = new TestPayload(100000, "a");
        await runner.RunAsync(ctx, p, Handler(p), Ct);
        var other = new TestPayload(999, "a");
        var ex = await Assert.ThrowsAsync<HccException>(() => runner.RunAsync(ctx, other, Handler(other), Ct));
        Assert.Equal(HccErrorCode.IDEMPOTENCY_MISMATCH, ex.Code);
        Assert.Equal(409, HccErrorCatalog.HttpStatus(ex.Code));
        Assert.Equal((1, 1, 1, 1), await CountsAsync("k-mismatch"));
    }

    [Fact]
    public async Task Failed_command_leaves_no_rows_and_the_same_key_can_be_retried()
    {
        var runner = _db.Platform().Commands;
        var ctx = new CommandContext(1, 7, "RecordReceipt", "k-fail");
        var p = new TestPayload(100, "fails");
        var ex = await Assert.ThrowsAsync<HccException>(() => runner.RunAsync(ctx, p, Handler(p, failAfterWrites: true), Ct));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        Assert.Equal((0, 0, 0, 0), await CountsAsync("k-fail"));
        var retry = await runner.RunAsync(ctx, p, Handler(p), Ct);
        Assert.False(retry.Replayed);
        Assert.Equal((1, 1, 1, 1), await CountsAsync("k-fail"));
    }

    [Fact]
    public async Task Command_without_audit_is_refused_and_rolled_back()
    {
        var runner = _db.Platform().Commands;
        var p = new TestPayload(1, "no audit");
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(new CommandContext(1, 7, "RecordReceipt", "k-noaudit"), p, Handler(p, audit: false), Ct));
        Assert.Equal((0, 0, 0, 0), await CountsAsync("k-noaudit"));
    }

    [Fact]
    public async Task Parallel_requests_with_one_key_apply_exactly_once()
    {
        var p = new TestPayload(100000, "parallel");
        var ctx = new CommandContext(1, 7, "RecordReceipt", "k-parallel");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _db.Platform().Commands.RunAsync(ctx, p, Handler(p), Ct)));
        Assert.Equal(1, results.Count(r => !r.Replayed));
        Assert.Single(results.Select(r => r.Value.EffectId).Distinct());
        Assert.Equal((1, 1, 1, 1), await CountsAsync("k-parallel"));
    }

    [Fact]
    public async Task Key_is_scoped_to_principal_and_operation()
    {
        var runner = _db.Platform().Commands;
        var p = new TestPayload(1, "scoped");
        var a = await runner.RunAsync(new CommandContext(1, 7, "RecordReceipt", "k-scope"), p, Handler(p), Ct);
        var b = await runner.RunAsync(new CommandContext(1, 8, "RecordReceipt", "k-scope"), p, Handler(p), Ct);
        var c = await runner.RunAsync(new CommandContext(1, 7, "CheckoutStay", "k-scope"), p, Handler(p), Ct);
        Assert.False(a.Replayed || b.Replayed || c.Replayed);
        Assert.Equal(3, (await CountsAsync("k-scope")).Effects);
    }

    [Fact]
    public async Task Audit_trail_is_append_only()
    {
        var runner = _db.Platform().Commands;
        var p = new TestPayload(1, "audit");
        await runner.RunAsync(new CommandContext(1, 7, "RecordReceipt", "k-audit"), p, Handler(p), Ct);
        var upd = await Assert.ThrowsAsync<SqlException>(() => _db.ExecAsync("UPDATE plat.audit_event SET detail = N'tampered' WHERE idem_key = 'k-audit'", Ct));
        Assert.Equal(51000, upd.Number);
        var del = await Assert.ThrowsAsync<SqlException>(() => _db.ExecAsync("DELETE plat.audit_event WHERE idem_key = 'k-audit'", Ct));
        Assert.Equal(51000, del.Number);
        Assert.Equal(1, (await CountsAsync("k-audit")).Audit);
    }

    [Fact]
    public async Task Outbox_consumer_applies_each_event_once_per_consumer_and_retries_failures()
    {
        var platform = _db.Platform();
        var p = new TestPayload(1, "outbox");
        var r = await platform.Commands.RunAsync(new CommandContext(1, 7, "CheckoutStay", "k-outbox"), p, Handler(p), Ct);
        await _db.ExecAsync("IF OBJECT_ID('dbo.test_task') IS NULL CREATE TABLE dbo.test_task(consumer VARCHAR(40) NOT NULL, outbox_id BIGINT NOT NULL);", Ct);
        var failOnce = true;
        async Task Effect(OutboxEvent e, SqlConnection c, SqlTransaction t, CancellationToken ct, string consumer)
        {
            await using var cmd = new SqlCommand("INSERT dbo.test_task(consumer, outbox_id) VALUES (@c, @o);", c, t);
            cmd.Parameters.Add("@c", SqlDbType.VarChar, 40).Value = consumer;
            cmd.Parameters.Add("@o", SqlDbType.BigInt).Value = e.OutboxId;
            await cmd.ExecuteNonQueryAsync(ct);
            if (consumer == "flaky" && failOnce)
            {
                failOnce = false;
                throw new InvalidOperationException("transient");
            }
        }

        string[] types = ["TestEffectCreated"];
        var outboxId = Convert.ToInt64(await _db.ScalarAsync($"SELECT outbox_id FROM plat.outbox WHERE event_type = 'TestEffectCreated' AND aggregate_id = {r.Value.EffectId}", Ct), System.Globalization.CultureInfo.InvariantCulture);
        var hk = await platform.Outbox.ConsumeAsync("housekeeping", types, (e, c, t, ct) => Effect(e, c, t, ct, "housekeeping"), ct: Ct);
        Assert.True(hk >= 1);
        Assert.Equal(0, await platform.Outbox.ConsumeAsync("housekeeping", types, (e, c, t, ct) => Effect(e, c, t, ct, "housekeeping"), ct: Ct));
        var evt = new OutboxEvent(outboxId, "TestEffectCreated", r.Value.EffectId, "{}", DateTime.UtcNow);
        Assert.False(await platform.Outbox.DeliverAsync("housekeeping", evt, (e, c, t, ct) => Effect(e, c, t, ct, "housekeeping"), Ct)); // redelivery
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM dbo.test_task WHERE consumer = 'housekeeping' AND outbox_id = {outboxId}", Ct));

        await Assert.ThrowsAsync<InvalidOperationException>(() => platform.Outbox.DeliverAsync("flaky", evt, (e, c, t, ct) => Effect(e, c, t, ct, "flaky"), Ct));
        Assert.Equal(0, await _db.CountAsync($"SELECT COUNT(*) FROM plat.consumer_dedup WHERE consumer = 'flaky' AND outbox_id = {outboxId}", Ct));
        Assert.True(await platform.Outbox.DeliverAsync("flaky", evt, (e, c, t, ct) => Effect(e, c, t, ct, "flaky"), Ct));
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM dbo.test_task WHERE consumer = 'flaky' AND outbox_id = {outboxId}", Ct));
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM plat.outbox WHERE outbox_id = {outboxId} AND published_utc IS NOT NULL", Ct));
    }
}
