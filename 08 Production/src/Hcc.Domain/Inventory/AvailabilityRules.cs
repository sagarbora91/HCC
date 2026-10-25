using System.Globalization;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;

namespace Hcc.Domain.Inventory;

/// <summary>A specific room soft-held for an open group block's unpicked allotment over [From, To).</summary>
public sealed record GroupHold(int RoomId, string RoomNo, long GroupBlockId, string BlockCode, string BlockName, int RoomTypeId, DateOnly From, DateOnly To)
{
    public bool Overlaps(DateOnly a, DateOnly d) => a < To && d > From;
}

/// <summary>
/// Availability rules ported from the prototype (<c>03 Prototype/src/domain/selectors.ts</c> blockedDuring / availableRaw /
/// available, <c>streams/reservations.ts</c> resExclusion, <c>streams/groups.ts</c> grpHolds). Stays are half-open [a, d).
/// The prototype's global <c>withHoldExempt</c> is replaced by an explicit <c>exemptBlockId</c> argument (contracts §4 rule 2).
/// </summary>
public static class AvailabilityRules
{
    /// <summary>Whether a room is out of service for any night in [a, d) (prototype blockedDuring).</summary>
    public static bool BlockedDuring(IEnumerable<RoomBlockWindow> blocks, int roomId, DateOnly a, DateOnly d)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return blocks.Any(b => b.RoomId == roomId && (b.To is null || a < b.To.Value) && (b.From is null || d > b.From.Value));
    }

    /// <summary>Live stays on a room overlapping [a, d), except one reservation.</summary>
    public static IEnumerable<StayOnRoom> Clashes(InventorySnapshot s, int roomId, DateOnly a, DateOnly d, long exceptReservationId = 0)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Stays.Where(x => x.ReservationId != exceptReservationId && x.RoomId == roomId && x.Occupies(s.NowUtc) && a < x.Departure && d > x.Arrival);
    }

    /// <summary>Free of out-of-service blocks and live bookings, ignoring group holds (prototype availableRaw).</summary>
    public static bool AvailableRaw(InventorySnapshot s, int roomId, DateOnly a, DateOnly d, long exceptReservationId = 0)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Rooms.Any(r => r.RoomId == roomId) && !BlockedDuring(s.RoomBlocks, roomId, a, d) && !Clashes(s, roomId, a, d, exceptReservationId).Any();
    }

    /// <summary>
    /// Specific rooms held for open blocks' unpicked allotment — deterministic like the prototype: blocks by arrival then code,
    /// rooms highest number first, each room free (AvailableRaw) and not already held for an overlapping block.
    /// </summary>
    public static IReadOnlyList<GroupHold> GroupHolds(InventorySnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var holds = new List<GroupHold>();
        var open = s.GroupBlocks.Where(b => b.EffectiveStatus(s.BusinessDate) == "Open" && b.Departure > s.BusinessDate)
            .OrderBy(b => b.Arrival).ThenBy(b => b.Code, StringComparer.Ordinal);
        foreach (var blk in open)
        {
            var a = blk.Arrival < s.BusinessDate ? s.BusinessDate : blk.Arrival;
            var d = blk.Departure;
            foreach (var type in blk.Types.Select(t => t.RoomTypeId).Distinct())
            {
                var n = blk.Remaining(s.BusinessDate, type);
                if (n <= 0) continue;
                var candidates = s.Rooms.Where(r => r.RoomTypeId == type).OrderByDescending(r => r, RoomNumberComparer.Instance)
                    .Where(r => AvailableRaw(s, r.RoomId, a, d) && !holds.Any(h => h.RoomId == r.RoomId && h.Overlaps(a, d)));
                foreach (var room in candidates.Take(n))
                    holds.Add(new GroupHold(room.RoomId, room.RoomNo, blk.GroupBlockId, blk.Code, blk.Name, type, a, d));
            }
        }

        return holds;
    }

    /// <summary>The hold (if any) on a room for any night in [a, d) by a block other than <paramref name="exemptBlockId"/>.</summary>
    public static GroupHold? RoomHold(IEnumerable<GroupHold> holds, int roomId, DateOnly a, DateOnly d, long? exemptBlockId)
    {
        ArgumentNullException.ThrowIfNull(holds);
        return holds.FirstOrDefault(h => h.RoomId == roomId && h.GroupBlockId != exemptBlockId && h.Overlaps(a, d));
    }

    /// <summary>
    /// Why a room cannot take [a, d) for <paramref name="party"/> occupants; empty = available (prototype resExclusion).
    /// Order: Blocked, Over capacity, Occupied, Booked, Held; a room free of those but held for another block reads GroupHold
    /// (the prototype's defensive "Booked" fallback for that case).
    /// </summary>
    public static IReadOnlyList<string> Exclusion(InventorySnapshot s, IReadOnlyList<GroupHold> holds, RoomInfo room, DateOnly a, DateOnly d,
        int party, long exceptReservationId = 0, long? exemptBlockId = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(room);
        var reasons = new List<string>();
        if (BlockedDuring(s.RoomBlocks, room.RoomId, a, d)) reasons.Add(AvailabilityReasons.Blocked);
        if (party > room.Capacity) reasons.Add(AvailabilityReasons.OverCapacity);
        var clash = Clashes(s, room.RoomId, a, d, exceptReservationId).ToList();
        if (clash.Any(x => x.Status == ReservationStatuses.InHouse)) reasons.Add(AvailabilityReasons.Occupied);
        if (clash.Any(x => x.Status == ReservationStatuses.Confirmed)) reasons.Add(AvailabilityReasons.Booked);
        if (clash.Any(x => x.Status == ReservationStatuses.Held)) reasons.Add(AvailabilityReasons.Held);
        if (reasons.Count == 0 && RoomHold(holds, room.RoomId, a, d, exemptBlockId) is not null) reasons.Add(AvailabilityReasons.GroupHold);
        return reasons;
    }
}

/// <summary>Room numbers compared like the prototype's <c>localeCompare(…, {numeric:true})</c>: digit runs by value.</summary>
public sealed class RoomNumberComparer : IComparer<RoomInfo>
{
    public static readonly RoomNumberComparer Instance = new();

    public int Compare(RoomInfo? x, RoomInfo? y) => CompareNumbers(x?.RoomNo, y?.RoomNo);

    public static int CompareNumbers(string? a, string? b)
    {
        if (a is null || b is null) return string.CompareOrdinal(a, b);
        var aNum = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var x);
        var bNum = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var y);
        if (aNum && bNum) return x.CompareTo(y);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
