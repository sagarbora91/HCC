namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>
/// A08-03 follow-up (Wave D): a room added to or deleted from a room type, or moved to another type (Master configuration › Rooms and the
/// masters import), changes that type's sellable count on every night from the business date on — also on nights without a count row yet,
/// like an open-ended room block. These writers bypass <see cref="TypeNightDeltas"/> (or reach only the counted nights through it), so they
/// report the change themselves to <see cref="InventoryChangePublisher"/>: one InventoryChanged per room type per transaction, from the business
/// date for one year (<see cref="InventoryChangePublisher.OpenEndedNights"/>), or to the last counted night when that is later.
/// </summary>
internal static class RoomCountChange
{
    public static Task RecordAsync(SqlScope scope, int propertyId, int roomTypeId, DateOnly from, DateOnly? lastCountedNight, CancellationToken ct)
    {
        var to = from.AddDays(InventoryChangePublisher.OpenEndedNights);
        if (lastCountedNight is { } last && last >= to) to = last.AddDays(1);
        return InventoryChangePublisher.RecordRangeAsync(scope, propertyId, roomTypeId, from, to, ct);
    }
}
