using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Inventory;

namespace Hcc.Application.Inventory;

/// <summary>One history row of a room block (SQL: inv.room_block_history).</summary>
public sealed record RoomBlockHistoryEntry(long HistoryId, long RoomBlockId, int RoomId, string Event, DateOnly EffectiveOn, DateOnly? FromNight,
    DateOnly? ToNight, string Reason, string Source, long? SourceRef, long? RelatedBlockId, int? ActorId, DateTime EventUtc);

/// <summary>A room's state on one night as known at that business date (see <see cref="IRoomBlockHistoryReader.RoomStatusAsync"/>).</summary>
public sealed record RoomStatusEntry(int RoomId, string RoomNo, string RoomType, bool Blocked, string? BlockReason, long? RoomBlockId, bool Occupied);

/// <summary>
/// Effective-dated view of room blocks (W3-8). A block placed on business date C and released on R covers its window clipped to
/// [.., R) for any question asked "as of" a date on or after R, and is invisible as of a date before C. Edits are clip-and-replace
/// (the old row is released as of the edit date, a successor row carries the new window), so past nights never change.
/// </summary>
public interface IRoomBlockHistoryReader
{
    Task<IReadOnlyList<RoomBlockHistoryEntry>> ListAsync(int propertyId, int? roomId, long? roomBlockId, CancellationToken ct = default);

    /// <summary>Block windows overlapping [from, to) as they stood on business date <paramref name="asOf"/>.</summary>
    Task<IReadOnlyList<RoomBlockWindow>> WindowsAsOfAsync(int propertyId, DateOnly asOf, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Every room's block/occupancy state on the night <paramref name="asOf"/>, using the history as of that date.</summary>
    Task<IReadOnlyList<RoomStatusEntry>> RoomStatusAsync(int propertyId, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Edits an active block inside the caller's command transaction. Nights before today's business date are never changed: the old row
/// is released as of today and a successor carries the new window (from clamped to today). Returns the successor's id, or null when
/// the edit leaves nothing in force (the block simply ends today). Throws NOT_AVAILABLE if the new window has live bookings.
/// </summary>
public interface IRoomBlockEditPort
{
    Task<long?> EditAsync(ICommandTransaction tx, int propertyId, long roomBlockId, DateOnly? from, DateOnly? to, string reason, CancellationToken ct = default);
}

/// <summary>Queries over room-block history and the "as of" room status (permission: reservation.view).</summary>
public sealed class RoomBlockQueries(IRoomBlockHistoryReader history, IInventoryStore store, IAccessPolicy access)
{
    public async Task<IReadOnlyList<RoomBlockHistoryItem>> GetHistoryAsync(int propertyId, int principalId, int? roomId, long? roomBlockId, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, InventoryPermissions.ViewReservations, ct: ct).ConfigureAwait(false);
        var rows = await history.ListAsync(propertyId, roomId, roomBlockId, ct).ConfigureAwait(false);
        return rows.Select(h => new RoomBlockHistoryItem(h.HistoryId, h.RoomBlockId, h.RoomId, h.Event, StayDates.ToWire(h.EffectiveOn),
            h.FromNight is { } f ? StayDates.ToWire(f) : null, h.ToNight is { } t ? StayDates.ToWire(t) : null, h.Reason, h.Source, h.SourceRef,
            h.RelatedBlockId, h.ActorId, InventoryService.ToWireInstant(h.EventUtc))).ToList();
    }

    public async Task<RoomStatusResult> GetRoomStatusAsync(int propertyId, int principalId, string? asOf, CancellationToken ct = default)
    {
        await access.RequireAsync(propertyId, principalId, InventoryPermissions.ViewReservations, ct: ct).ConfigureAwait(false);
        var property = await store.GetPropertyAsync(propertyId, null, ct).ConfigureAwait(false);
        var date = string.IsNullOrWhiteSpace(asOf) ? property.BusinessDate : StayDates.Parse(asOf, "asOf");
        if (date > property.BusinessDate) throw HccException.Validation("asOf cannot be after the current business date.");
        var rows = await history.RoomStatusAsync(propertyId, date, ct).ConfigureAwait(false);
        return new RoomStatusResult(StayDates.ToWire(date), StayDates.ToWire(property.BusinessDate),
            rows.Select(r => new RoomStatusRow(r.RoomId, r.RoomNo, r.RoomType, r.Blocked, r.BlockReason, r.RoomBlockId, r.Occupied)).ToList());
    }
}
