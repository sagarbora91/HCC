using System.Runtime.CompilerServices;
using Hcc.Application.Commands;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Dates;
using Hcc.Infrastructure.SqlServer.Outbox;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>
/// A08-03: one <see cref="InventoryEvents.InventoryChanged"/> outbox event per (property, room type) per transaction, written in that transaction.
/// <see cref="TypeNightDeltas.ApplyAsync"/> — the one writer of <c>inv.type_night_count</c> that reservations, holds and their expiry sweep,
/// stay changes (extend, shorten, early departure, room move), group blocks and room blocks share — reports the nights whose counts it changed;
/// the room-block port also reports its window, because nights without a count row yet have no delta. Several changes of one room type in one
/// transaction (a stay change releases and re-adds runs through the allocator) widen the same event instead of adding another, so the OTA
/// projector sees one event per command and type. The event's range is the hull of the changed nights: from the first to the night after the last.
/// </summary>
public static class InventoryChangePublisher
{
    /// <summary>Nights reported for a block without an end date: one year from the business date (the counts are materialised only where touched).</summary>
    public const int OpenEndedNights = 365;

    private sealed class Pending
    {
        public Dictionary<(int Property, int Type), (DateOnly From, DateOnly To, long OutboxId)> Ranges { get; } = [];
    }

    private static readonly ConditionalWeakTable<SqlTransaction, Pending> ByTransaction = new();

    /// <summary>Records the nights of <paramref name="changed"/> (room type, night) as changed in the scope's transaction.</summary>
    public static async Task RecordAsync(SqlScope scope, int propertyId, IEnumerable<(int RoomTypeId, DateOnly Night)> changed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(changed);
        foreach (var g in changed.GroupBy(x => x.RoomTypeId).OrderBy(g => g.Key))
            await RecordRangeAsync(scope, propertyId, g.Key, g.Min(x => x.Night), g.Max(x => x.Night).AddDays(1), ct).ConfigureAwait(false);
    }

    /// <summary>Records nights <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive) of one room type as changed.</summary>
    public static async Task RecordRangeAsync(SqlScope scope, int propertyId, int roomTypeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (to <= from) return;
        var pending = scope.Transaction is { } tx ? ByTransaction.GetOrCreateValue(tx) : new Pending();
        var key = (propertyId, roomTypeId);
        if (pending.Ranges.TryGetValue(key, out var cur))
        {
            var (f, t) = (from < cur.From ? from : cur.From, to > cur.To ? to : cur.To);
            if (f == cur.From && t == cur.To) return;
            await OutboxWriter.UpdatePayloadAsync(scope.Command, cur.OutboxId, Payload(propertyId, roomTypeId, f, t), ct).ConfigureAwait(false);
            pending.Ranges[key] = (f, t, cur.OutboxId);
            return;
        }

        var id = await OutboxWriter.InsertAsync(scope.Command, InventoryEvents.InventoryChanged, roomTypeId, Payload(propertyId, roomTypeId, from, to), propertyId,
            scope.PrincipalId, ct).ConfigureAwait(false);
        pending.Ranges[key] = (from, to, id);
    }

    private static string Payload(int propertyId, int roomTypeId, DateOnly from, DateOnly to)
        => CanonicalJson.Serialize(new InventoryChangedEvent(propertyId, roomTypeId, StayDates.ToWire(from), StayDates.ToWire(to)));
}
