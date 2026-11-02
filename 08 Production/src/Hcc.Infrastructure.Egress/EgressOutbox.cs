namespace Hcc.Infrastructure.Egress;

/// <summary>Held / pending counts of one queue (System health).</summary>
public sealed record EgressQueueCount(string Queue, int Held, int Pending);

/// <summary>An outbound item (<c>ops.egress_outbox</c>, migration 4731).</summary>
public sealed record EgressOutboxItem(long ItemId, string Queue, EgressIntegration Integration, string? RefId, string Payload, string Status, string? HeldReason, DateTimeOffset? HeldAt);

/// <summary>Store of outbound items (SQL adapter: <c>SqlEgressStore</c>).</summary>
public interface IEgressOutboxStore
{
    Task<long> InsertAsync(string queue, EgressIntegration integration, string? refId, string payloadJson, bool held, string? heldReason, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Pending → held for an integration that is OFF (no attempt consumed). Returns the number held.</summary>
    Task<int> HoldPendingAsync(EgressIntegration integration, string heldReason, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Held → pending for an integration that is ON, oldest first (original order kept by item id). Returns the number released.</summary>
    Task<int> ReplayHeldAsync(EgressIntegration integration, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Pending items of an integration in original order (what an adapter sends next).</summary>
    Task<IReadOnlyList<EgressOutboxItem>> PendingAsync(EgressIntegration integration, int max, CancellationToken ct = default);

    Task<IReadOnlyList<EgressQueueCount>> CountsAsync(CancellationToken ct = default);
}

/// <summary>
/// EGRESS-CONTRACT §1.2: queue outbound work with its hold status, and the replay hook the outbox worker runs each tick (and right after a
/// <c>net.*</c> change, through <see cref="IEgressSettingsCache.Invalidate"/>): for every integration, ON → held items back to pending in order;
/// OFF → pending items held (reason "internet off").
/// </summary>
public sealed class EgressOutbox(IEgressOutboxStore store, IEgressPolicy policy, TimeProvider clock)
{
    public const string HeldReason = "internet off";

    public async Task<long> EnqueueAsync(string queue, EgressIntegration integration, string? refId, string payloadJson, CancellationToken ct = default)
    {
        if (policy is IEgressSettingsCache cache) await cache.RefreshAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var on = policy.Evaluate(integration, now).Allowed;
        return await store.InsertAsync(queue, integration, refId, payloadJson, !on, on ? null : HeldReason, now, ct).ConfigureAwait(false);
    }

    /// <summary>One replay tick; returns (released, held) totals.</summary>
    public async Task<(int Released, int Held)> TickAsync(CancellationToken ct = default)
    {
        if (policy is IEgressSettingsCache cache) await cache.RefreshAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        int released = 0, held = 0;
        foreach (var i in EgressKeys.All)
        {
            if (policy.Evaluate(i, now).Allowed) released += await store.ReplayHeldAsync(i, now, ct).ConfigureAwait(false);
            else held += await store.HoldPendingAsync(i, HeldReason, now, ct).ConfigureAwait(false);
        }

        return (released, held);
    }
}
