using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Stays;

/// <summary>The room nights [From, To) one room holds for a reservation (one contiguous run).</summary>
public sealed record NightRun(int RoomId, int RoomTypeId, DateOnly From, DateOnly To);

/// <summary>One room night of a reservation as stored in <c>inv.room_night</c>.</summary>
public readonly record struct RoomNightKey(int RoomId, int RoomTypeId, DateOnly Night);

/// <summary>
/// Change plans for the inventory allocator. The allocator re-allocates a whole reservation at once (<c>DELETE inv.room_night WHERE
/// reservation_id</c>), so a stay that moved rooms is planned night by night: <see cref="Runs"/> turns nights into contiguous runs,
/// <see cref="Removed"/> / <see cref="Added"/> decide whether the change can be applied by adding runs only (extension) or needs the full
/// release and re-add (move, shorten).
/// </summary>
public static class NightPlan
{
    public static IReadOnlyList<NightRun> Runs(IEnumerable<RoomNightKey> nights)
    {
        ArgumentNullException.ThrowIfNull(nights);
        var runs = new List<NightRun>();
        foreach (var n in nights.OrderBy(x => x.Night))
        {
            if (runs.Count > 0 && runs[^1] is { } last && last.RoomId == n.RoomId && last.To == n.Night)
                runs[^1] = last with { To = n.Night.AddDays(1) };
            else
                runs.Add(new NightRun(n.RoomId, n.RoomTypeId, n.Night, n.Night.AddDays(1)));
        }

        return runs;
    }

    public static IEnumerable<RoomNightKey> Nights(IEnumerable<NightRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        foreach (var r in runs)
            for (var n = r.From; n < r.To; n = n.AddDays(1)) yield return new RoomNightKey(r.RoomId, r.RoomTypeId, n);
    }

    /// <summary>Nights held now and not wanted afterwards (by room and night).</summary>
    public static IReadOnlyList<RoomNightKey> Removed(IEnumerable<RoomNightKey> current, IEnumerable<RoomNightKey> desired)
    {
        var want = desired.Select(x => (x.RoomId, x.Night)).ToHashSet();
        return current.Where(x => !want.Contains((x.RoomId, x.Night))).ToList();
    }

    /// <summary>Nights wanted afterwards and not held now.</summary>
    public static IReadOnlyList<RoomNightKey> Added(IEnumerable<RoomNightKey> current, IEnumerable<RoomNightKey> desired)
    {
        var have = current.Select(x => (x.RoomId, x.Night)).ToHashSet();
        return desired.Where(x => !have.Contains((x.RoomId, x.Night))).ToList();
    }
}

/// <summary>Resolved rate handling of a change.</summary>
public sealed record RateChoice(bool Change, long? NewRatePaise);

/// <summary>
/// Rules for changing an in-house stay, ported from the prototype <c>MOVE</c> handler (handlers/guests.ts): target validated before
/// the old room is released; departure must follow the active arrival (the business date for an in-house stay), at most 30 nights;
/// a shortening is refused below the nights already posted; the agreed tariff is preserved unless a rate change is requested.
/// The prototype has no early-departure fee and no rate-change approval: both are production additions kept explicit here
/// (downgrade rate loss needs the approval permission; no fee).
/// </summary>
public static class StayChangeRules
{
    /// <summary>Prototype: <c>nights(arrival, departure) &lt;= 30</c> from the active arrival.</summary>
    public const int MaxNightsFromBusinessDate = 30;

    public const int MinReasonLength = 3;

    public static string Reason(string? reason, bool required)
    {
        var r = (reason ?? string.Empty).Trim();
        if (r.Length == 0 && !required) return string.Empty;
        if (r.Length < MinReasonLength) throw HccException.Validation("Enter the reason for the change.");
        if (r.Length > 200) throw HccException.Validation("The reason is too long (maximum 200 characters).");
        return r;
    }

    public static RateChoice Rate(string? decision, long? newRatePaise)
    {
        var d = string.IsNullOrWhiteSpace(decision) ? "keep" : decision.Trim().ToLowerInvariant();
        switch (d)
        {
            case "keep":
                if (newRatePaise is not null) throw HccException.Validation("A new nightly rate needs rateDecision \"change\".");
                return new RateChoice(false, null);
            case "change":
                if (newRatePaise is not { } r || r <= 0 || r > 10_000_000_000) throw HccException.Validation("Enter the new nightly rate in paise (greater than zero).");
                return new RateChoice(true, r);
            default:
                throw HccException.Validation("rateDecision must be keep or change.");
        }
    }

    /// <summary>Revenue given up by lowering the nightly rate over the un-posted nights; zero for a raise or no change.</summary>
    public static long DowngradeLoss(long agreedRatePaise, long newRatePaise, int nights)
    {
        if (nights < 0) throw new ArgumentOutOfRangeException(nameof(nights));
        return newRatePaise >= agreedRatePaise ? 0 : checked((agreedRatePaise - newRatePaise) * nights);
    }

    public static void AssertInHouse(string stayStatus)
    {
        if (stayStatus != StayStatus.InHouse) throw new HccException(HccErrorCode.INVALID_TRANSITION, "This stay is not in house.");
    }

    /// <summary>A group pickup keeps its dates and room type (Amend rule); only a same-type room swap is allowed.</summary>
    public static void AssertDatesChangeable(long? groupBlockId)
    {
        if (groupBlockId is not null)
            throw HccException.Validation("A group pickup keeps the group dates. Change it from the group booking, or move it to another room of the same type.");
    }

    public static void AssertMoveTarget(int fromRoomId, int toRoomId, int fromTypeId, int toTypeId, long? groupBlockId, int adults, int children, int maxOccupancy,
        string targetHk, bool targetOccupied, bool targetBlocked)
    {
        if (toRoomId == fromRoomId) throw HccException.Validation("Choose a different room.");
        if (groupBlockId is not null && fromTypeId != toTypeId)
            throw HccException.Validation("A group pickup can move only to another room of the same type.");
        if (adults + children > maxOccupancy) throw HccException.Validation($"This room sleeps at most {maxOccupancy} guests.");
        if (targetBlocked) throw new HccException(HccErrorCode.NOT_AVAILABLE, "Target room is unavailable for the requested stay.");
        if (targetHk != "Ready" || targetOccupied) throw new HccException(HccErrorCode.NOT_AVAILABLE, "Target room must be vacant and ready.");
    }

    public static void AssertExtend(DateOnly oldDeparture, DateOnly newDeparture, DateOnly businessDate)
    {
        if (newDeparture <= oldDeparture) throw HccException.Validation("The new departure must be after the current departure.");
        if (newDeparture.DayNumber - businessDate.DayNumber > MaxNightsFromBusinessDate)
            throw HccException.Validation("Departure must follow the active arrival date (maximum 30 nights).");
    }

    public static void AssertShorten(DateOnly arrival, DateOnly oldDeparture, DateOnly newDeparture, DateOnly businessDate, DateOnly? lastPostedNight)
    {
        if (newDeparture >= oldDeparture) throw HccException.Validation("The new departure must be before the current departure.");
        if (newDeparture <= businessDate)
            throw HccException.Validation("The guest keeps tonight: choose a departure after the business date, or record an early departure to leave today.");
        if (newDeparture <= arrival) throw HccException.Validation("A stay has at least one night.");
        if (lastPostedNight is { } p && p >= newDeparture)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Room nights are already posted on or after the new departure date. Reverse them before shortening.");
    }

    /// <summary>Early departure = leaving on the business date: nights [business date, departure) are released.</summary>
    public static void AssertEarlyDeparture(DateOnly arrival, DateOnly oldDeparture, DateOnly businessDate, DateOnly? lastPostedNight)
    {
        if (oldDeparture <= businessDate)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "There are no remaining nights to release. Check the guest out.");
        if (arrival >= businessDate)
            throw HccException.Validation("The guest arrived today and a stay has at least one night: shorten to tomorrow, or check the guest out after the night is charged.");
        if (lastPostedNight is { } p && p >= businessDate)
            throw new HccException(HccErrorCode.INVALID_TRANSITION, "Room nights are already posted on or after the new departure date. Reverse them before shortening.");
    }
}
