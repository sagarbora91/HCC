namespace Hcc.Contracts.Inventory;

/// <summary>One create/edit/release of a room block (W3-8). <c>EffectiveOn</c> = the business date it took effect.</summary>
public sealed record RoomBlockHistoryItem(long HistoryId, long RoomBlockId, int RoomId, string Event, string EffectiveOn, string? FromNight,
    string? ToNight, string Reason, string Source, long? SourceRef, long? RelatedBlockId, int? ActorId, string EventAt);

/// <summary>A room's out-of-service / occupancy state on one night, as known at the end of that business date.
/// D5 (front desk without PERM-HK-001): <c>Hk</c> = the room's clean/dirty state (Ready / Dirty / Cleaning / Inspection), only when
/// asked as of the current business date (null for a past date: the server keeps no history of it); <c>BlockFrom</c> / <c>BlockTo</c> =
/// the effective window of the block in force that night (null = open-ended). No money and no guest fields.</summary>
public sealed record RoomStatusRow(int RoomId, string RoomNo, string RoomType, bool Blocked, string? BlockReason, long? RoomBlockId, bool Occupied,
    string? Hk = null, string? BlockFrom = null, string? BlockTo = null);

public sealed record RoomStatusResult(string AsOf, string BusinessDate, IReadOnlyList<RoomStatusRow> Rooms);

public static class RoomBlockEvents
{
    public const string Created = "Created";
    public const string Edited = "Edited";
    public const string Released = "Released";
}
