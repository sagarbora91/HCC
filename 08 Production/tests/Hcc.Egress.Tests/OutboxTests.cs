using Hcc.Infrastructure.Egress;

namespace Hcc.Egress.Tests;

/// <summary>In-memory store with the SQL adapter's semantics (the SQL test pins the adapter itself).</summary>
public sealed class MemoryOutbox : IEgressOutboxStore
{
    public List<(long Id, string Queue, EgressIntegration I, string Status, int Attempts)> Items { get; } = [];

    public Task<long> InsertAsync(string queue, EgressIntegration integration, string? refId, string payloadJson, bool held, string? heldReason, DateTimeOffset at, CancellationToken ct = default)
    {
        Items.Add((Items.Count + 1, queue, integration, held ? "held" : "pending", 0));
        return Task.FromResult((long)Items.Count);
    }

    private int Move(EgressIntegration i, string from, string to)
    {
        var n = 0;
        for (var k = 0; k < Items.Count; k++)
        {
            if (Items[k].I == i && Items[k].Status == from)
            {
                Items[k] = Items[k] with { Status = to };
                n++;
            }
        }

        return n;
    }

    public Task<int> HoldPendingAsync(EgressIntegration integration, string heldReason, DateTimeOffset at, CancellationToken ct = default) => Task.FromResult(Move(integration, "pending", "held"));

    public Task<int> ReplayHeldAsync(EgressIntegration integration, DateTimeOffset at, CancellationToken ct = default) => Task.FromResult(Move(integration, "held", "pending"));

    public Task<IReadOnlyList<EgressOutboxItem>> PendingAsync(EgressIntegration integration, int max, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EgressOutboxItem>>(Items.Where(x => x.I == integration && x.Status == "pending").OrderBy(x => x.Id).Take(max)
            .Select(x => new EgressOutboxItem(x.Id, x.Queue, x.I, null, "{}", x.Status, null, null)).ToList());

    public Task<IReadOnlyList<EgressQueueCount>> CountsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EgressQueueCount>>(Items.GroupBy(x => x.Queue).Select(g => new EgressQueueCount(g.Key, g.Count(x => x.Status == "held"), g.Count(x => x.Status == "pending"))).ToList());
}

public sealed class OutboxTests
{
    [Fact]
    public async Task Off_holds_on_replays_in_original_order_without_consuming_attempts()
    {
        var ct = TestContext.Current.CancellationToken;
        var src = new FakeSource { Master = false };
        var clock = new TestClock(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
        var policy = new EgressPolicy(src, clock);
        var store = new MemoryOutbox();
        var outbox = new EgressOutbox(store, policy, clock);
        for (var n = 0; n < 3; n++) await outbox.EnqueueAsync("messaging", EgressIntegration.Messaging, null, "{}", ct);
        await outbox.EnqueueAsync("ota-push", EgressIntegration.Ota, null, "{}", ct);
        Assert.All(store.Items, x => Assert.Equal("held", x.Status));

        src.AllOn();
        src.Switches[EgressIntegration.Ota] = false;
        policy.Invalidate();
        var (released, _) = await outbox.TickAsync(ct);
        Assert.Equal(3, released);
        Assert.Equal([1L, 2L, 3L], (await store.PendingAsync(EgressIntegration.Messaging, 10, ct)).Select(x => x.ItemId));
        Assert.Equal("held", store.Items[3].Status);
        Assert.All(store.Items, x => Assert.Equal(0, x.Attempts));

        src.Master = false;
        clock.Now += TimeSpan.FromSeconds(6);
        var (_, held) = await outbox.TickAsync(ct);
        Assert.Equal(3, held);
        Assert.Equal(4, store.Items.Count(x => x.Status == "held"));
    }
}
