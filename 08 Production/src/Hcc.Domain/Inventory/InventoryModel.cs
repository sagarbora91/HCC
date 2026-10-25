using Hcc.Contracts.Reservations;

namespace Hcc.Domain.Inventory;

public sealed record RoomInfo(int RoomId, string RoomNo, int RoomTypeId, string RoomType, int Capacity);

public sealed record RoomTypeInfo(int RoomTypeId, string Code, int MaxOccupancy, long RackRatePaise, int SortOrder);

/// <summary>Out-of-service window on a room covering nights [From, To); null From = no lower bound, null To = open-ended.</summary>
public sealed record RoomBlockWindow(int RoomId, DateOnly? From, DateOnly? To, string Reason);

/// <summary>A reservation that may hold a room (Held / Confirmed / InHouse; other statuses never occupy inventory).</summary>
public sealed record StayOnRoom(long ReservationId, int RoomId, DateOnly Arrival, DateOnly Departure, string Status,
    DateTime? HoldUntilUtc, string GuestName, long? GroupBlockId)
{
    /// <summary>A Held reservation whose hold is still running (now &lt; holdUntil; a Held stay without holdUntil never lapses).</summary>
    public bool HoldRunning(DateTime nowUtc) => Status == ReservationStatuses.Held && (HoldUntilUtc is null || nowUtc < HoldUntilUtc.Value);

    /// <summary>Port of the prototype's live-booking test in <c>availableRaw</c>: Confirmed, InHouse, or a running hold.</summary>
    public bool Occupies(DateTime nowUtc) => Status is ReservationStatuses.Confirmed or ReservationStatuses.InHouse || HoldRunning(nowUtc);
}

public sealed record GroupBlockTypeInfo(int RoomTypeId, int Allotted, int Picked, int Released, long? RatePaise);

public sealed record GroupBlockInfo(long GroupBlockId, string Code, string Name, DateOnly Arrival, DateOnly Departure, DateOnly Cutoff,
    string Status, IReadOnlyList<GroupBlockTypeInfo> Types, byte[]? RowVersion = null)
{
    /// <summary>Effective status: an Open block whose cut-off has passed reads Released before the sweep persists it (grpStatus).</summary>
    public string EffectiveStatus(DateOnly businessDate) => Status == "Open" && businessDate > Cutoff ? "Released" : Status;

    /// <summary>Unpicked rooms of a type while the block is effectively open (grpRemaining).</summary>
    public int Remaining(DateOnly businessDate, int roomTypeId)
        => EffectiveStatus(businessDate) == "Open" ? Math.Max(0, Types.Where(t => t.RoomTypeId == roomTypeId).Sum(t => t.Allotted - t.Picked)) : 0;
}

/// <summary>Everything the availability rules read for one stay window (loaded by the inventory store, no I/O here).</summary>
public sealed record InventorySnapshot(DateOnly BusinessDate, DateTime NowUtc, IReadOnlyList<RoomInfo> Rooms,
    IReadOnlyList<RoomTypeInfo> RoomTypes, IReadOnlyList<RoomBlockWindow> RoomBlocks, IReadOnlyList<StayOnRoom> Stays,
    IReadOnlyList<GroupBlockInfo> GroupBlocks, IReadOnlyList<StayRestriction> Restrictions);
