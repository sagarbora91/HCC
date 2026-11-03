namespace Hcc.Domain.Hall;

public sealed record HallVenueRef(string Id, string[]? CombinedOf);

public sealed record HallHold(string EventId, IReadOnlyList<string> VenueIds, IReadOnlyList<DateOnly> Dates, string Slot);

/// <summary>
/// F6 hall rules (contract §2, §3; spec §5, §11) — the same as the prototype <c>src/domain/streams/hall.ts</c>: slot overlap (FULL_DAY overlaps
/// every slot), venue footprint (a combined venue blocks its parts and every combination sharing a part), calendar clash, the enquiry
/// status machine, auto-lost after 30 days, role sets (PERM-HAL-001…006) and DEC-061 (housekeeping / maintenance never see money).
/// </summary>
public static class HallRules
{
    public static readonly IReadOnlyList<string> Slots = ["MORNING", "AFTERNOON", "EVENING", "FULL_DAY"];

    public static readonly IReadOnlyList<string> EventTypes = ["WEDDING", "RECEPTION", "BIRTHDAY", "ENGAGEMENT", "CORPORATE", "CONFERENCE", "RELIGIOUS", "OTHER"];

    public const int EnquiryAutoLostDays = 30, SoftHoldMaxDays = 3, ValidityDays = 15, ExpiryReminderDays = 2;

    public const string Placeholder = "OWNER TO REPLACE";

    public static bool SlotsOverlap(string a, string b) => a == "FULL_DAY" || b == "FULL_DAY" || a == b;

    public static IReadOnlyList<string> Footprint(IReadOnlyList<HallVenueRef> venues, string venueId)
    {
        ArgumentNullException.ThrowIfNull(venues);
        var v = venues.FirstOrDefault(x => x.Id == venueId);
        var parts = v?.CombinedOf is { Length: > 0 } c ? c : [venueId];
        var set = new HashSet<string>(StringComparer.Ordinal) { venueId };
        foreach (var p in parts) set.Add(p);
        foreach (var x in venues)
            if (x.CombinedOf is { Length: > 0 } cc && cc.Any(parts.Contains)) set.Add(x.Id);
        return [.. set];
    }

    /// <summary>The first hard hold clashing with venue × dates × slot, or null.</summary>
    public static HallHold? Clash(IReadOnlyList<HallVenueRef> venues, IReadOnlyList<HallHold> holds, IReadOnlyList<string> venueIds, IReadOnlyList<DateOnly> dates, string slot, string? exceptEvent = null)
    {
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentNullException.ThrowIfNull(venueIds);
        ArgumentNullException.ThrowIfNull(dates);
        var foot = venueIds.SelectMany(v => Footprint(venues, v)).ToHashSet(StringComparer.Ordinal);
        return holds.FirstOrDefault(h => h.EventId != exceptEvent && SlotsOverlap(h.Slot, slot) && h.Dates.Any(dates.Contains)
            && h.VenueIds.Any(v => Footprint(venues, v).Any(foot.Contains)));
    }

    public static bool EnquiryTransition(string from, string to) => (from, to) switch
    {
        ("OPEN", "QUOTED" or "WON" or "LOST") => true,
        ("QUOTED", "WON" or "LOST" or "OPEN") => true,
        ("LOST", "OPEN") => true,
        _ => false,
    };

    public static bool AutoLost(string status, DateOnly followUp, DateOnly today) => status is "OPEN" or "QUOTED" && followUp.AddDays(EnquiryAutoLostDays) <= today;

    public static bool HasPlaceholder(IEnumerable<string> clauseTexts) => clauseTexts.Any(t => t.Contains(Placeholder, StringComparison.Ordinal));

    private static readonly string[] Desk = ["Owner", "Manager", "Reception"];

    public static bool MayEnquire(string role) => Desk.Contains(role);

    public static bool MayAccept(string role) => Desk.Contains(role);

    public static bool MayTakeAdvance(string role) => Desk.Contains(role) || role == "Cashier";

    public static bool MayBill(string role) => Desk.Contains(role) || role == "Cashier";

    public static bool MayCancel(string role) => Desk.Contains(role);

    public static bool MaySoftHold(string role) => role is "Owner" or "Manager";

    public static bool MayEditVenues(string role) => role == "Owner";

    public static bool MayEditMenus(string role) => role is "Owner" or "Manager";

    /// <summary>DEC-061: housekeeping and maintenance never receive *Paise fields (the DTO strips them).</summary>
    public static bool SeesMoney(string role) => role is "Owner" or "Manager" or "Reception" or "Cashier" or "Accountant";
}
