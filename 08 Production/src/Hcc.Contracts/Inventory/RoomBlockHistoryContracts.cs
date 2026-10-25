namespace Hcc.Contracts.Inventory;

/// <summary>One create/edit/release of a room block (W3-8). <c>EffectiveOn</c> = the business date it took effect.</summary>
public sealed record RoomBlockHistoryItem(long HistoryId, long RoomBlockId, int RoomId, string Event, string EffectiveOn, string? FromNight,
    string? ToNight, string Reason, string Source, long? SourceRef, long? RelatedBlockId, int? ActorId, string EventAt);

/// <summary>A room's out-of-service / occupancy state on one night, as known at the end of that business date.</summary>
public sealed record RoomStatusRow(int RoomId, string RoomNo, string RoomType, bool Blocked, string? BlockReason, long? RoomBlockId, bool Occupied);

public sealed record RoomStatusResult(string AsOf, string BusinessDate, IReadOnlyList<RoomStatusRow> Rooms);

public static class RoomBlockEvents
{
    public const string Created = "Created";
    public const string Edited = "Edited";
    public const string Released = "Released";
}
