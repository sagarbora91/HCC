namespace Hcc.Contracts.Inventory;

/// <summary>
/// A08-03: outbox events of the inventory counts. <see cref="InventoryChanged"/> is published by the one place every availability change goes
/// through (the type-night count writer used by the allocator, holds, group blocks and room blocks), once per room type per transaction.
/// </summary>
public static class InventoryEvents
{
    public const string InventoryChanged = "InventoryChanged";
}

/// <summary>Payload of <see cref="InventoryEvents.InventoryChanged"/>: availability of <paramref name="RoomTypeId"/> changed on the nights
/// <paramref name="From"/> (inclusive) to <paramref name="To"/> (exclusive), wire dates yyyy-MM-dd.</summary>
public sealed record InventoryChangedEvent(int PropertyId, int RoomTypeId, string From, string To);
