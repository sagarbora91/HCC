using System.Text.RegularExpressions;
using Hcc.Contracts.Inventory;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Inventory;

/// <summary>House stay restriction (prototype YLDRestriction): arrivals in [ArrivalFrom, ArrivalTo]; null room type = all types.</summary>
public sealed record StayRestriction(string Code, int? RoomTypeId, DateOnly ArrivalFrom, DateOnly ArrivalTo, int MinStay, bool ClosedToArrival, string Note);

/// <summary>
/// Stay restrictions (A2.1, DEC-053) — port of <c>yldStayViolations</c> (<c>03 Prototype/src/domain/streams/yield.ts</c>), same
/// codes and messages. Enforced by CreateReservation and AmendReservation for every source including group pickup.
/// </summary>
public static class StayRestrictions
{
    public const string MinStay = "MIN_STAY";
    public const string ClosedToArrival = "CTA";

    public static IReadOnlyList<StayViolation> Violations(IEnumerable<StayRestriction> restrictions, int roomTypeId, string roomTypeCode,
        DateOnly arrival, DateOnly departure)
    {
        ArgumentNullException.ThrowIfNull(restrictions);
        var n = StayDates.Nights(arrival, departure);
        var a = StayDates.ToWire(arrival);
        var list = new List<StayViolation>();
        foreach (var r in restrictions.Where(r => (r.RoomTypeId is null || r.RoomTypeId == roomTypeId) && r.ArrivalFrom <= arrival && r.ArrivalTo >= arrival))
        {
            var note = string.IsNullOrEmpty(r.Note) ? "" : $" ({r.Note})";
            if (r.ClosedToArrival) list.Add(new StayViolation(ClosedToArrival, r.Code, $"{roomTypeCode} is closed to arrival on {a}{note}."));
            if (n < r.MinStay)
                list.Add(new StayViolation(MinStay, r.Code,
                    $"Minimum stay {r.MinStay} nights for arrivals {StayDates.ToWire(r.ArrivalFrom)} to {StayDates.ToWire(r.ArrivalTo)}{note}."));
        }

        return list;
    }

    /// <summary>Override text recorded on the booking and in the audit detail (prototype BOOK restriction suffix).</summary>
    public static string OverrideNote(IEnumerable<StayViolation> violations, string reason)
    {
        var reasonText = reason.Length > 200 ? reason[..200] : reason;
        return $"restriction overridden ({string.Join(", ", violations.Select(v => v.Code + " " + v.Restriction))}): {reasonText}";
    }
}

/// <summary>Stay and party validation ported from the prototype BOOK / CHANGE_DATES handlers (messages kept).</summary>
public static partial class StayRules
{
    public const int MaxNights = 30;
    public const int MaxAdults = 4;
    public const int MaxChildren = 3;

    /// <summary>Default hold length (DEC-038): 120 minutes.</summary>
    public static readonly TimeSpan HoldLength = TimeSpan.FromMinutes(120);

    [GeneratedRegex(@"^\+?[\d\s-]{7,16}$")]
    private static partial Regex Phone();

    public static (DateOnly Arrival, DateOnly Departure) Stay(string? arrival, string? departure, DateOnly businessDate)
    {
        DateOnly a, d;
        try
        {
            a = StayDates.Parse(arrival, "arrival");
            d = StayDates.Parse(departure, "departure");
        }
        catch (HccException)
        {
            throw HccException.Validation($"Departure must follow arrival (maximum {MaxNights} nights).");
        }

        var n = d.DayNumber - a.DayNumber;
        if (n <= 0 || n > MaxNights) throw HccException.Validation($"Departure must follow arrival (maximum {MaxNights} nights).");
        if (a < businessDate) throw HccException.Validation("Arrival cannot be before the current business date.");
        return (a, d);
    }

    public static int Party(int adults, int children)
    {
        if (adults < 1 || adults > MaxAdults) throw HccException.Validation($"Choose 1–{MaxAdults} guests.");
        if (children < 0 || children > MaxChildren) throw HccException.Validation($"Children must be between 0 and {MaxChildren}.");
        return adults + children;
    }

    public static void Capacity(RoomInfo room, int party)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (party > room.Capacity)
            throw HccException.Validation($"Room {room.RoomNo} ({room.RoomType}) accommodates at most {room.Capacity} guests including children.");
    }

    public static string GuestName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length < 2) throw HccException.Validation("Enter the guest’s name.");
        if (n.Length > 200) throw HccException.Validation("The guest’s name is too long (200 characters at most).");
        return n;
    }

    public static string? PhoneNumber(string? phone)
    {
        var p = (phone ?? "").Trim();
        if (p.Length == 0) return null;
        if (!Phone().IsMatch(p)) throw HccException.Validation("Enter a valid phone number (7–16 digits).");
        return p;
    }

    /// <summary>Restriction override reason: at least 3 characters (prototype BOOK).</summary>
    public static string OverrideReason(string? reason)
    {
        var r = (reason ?? "").Trim();
        if (r.Length < 3) throw HccException.Validation("Give a reason for overriding the stay restriction.");
        return r;
    }
}

public sealed record PricedNight(DateOnly Date, long RatePaise, int TaxBps, long TaxPaise);

/// <summary>
/// Nightly price snapshot. The agreed nightly rate is the room type's tariff on the arrival date (prototype BOOK stores
/// rateFor(room, arrival), WF-BOOK-001); each night carries the tax rate effective on its own date; tax per night rounds
/// half away from zero (contracts §4 rule 3).
/// </summary>
public static class StayPricing
{
    public static IReadOnlyList<PricedNight> Nights(DateOnly arrival, DateOnly departure, long nightlyRatePaise, Func<DateOnly, int> taxBpsOn)
    {
        ArgumentNullException.ThrowIfNull(taxBpsOn);
        new Paise(nightlyRatePaise).EnsureFieldRange();
        return StayDates.EachNight(arrival, departure).Select(n =>
        {
            var bps = taxBpsOn(n);
            return new PricedNight(n, nightlyRatePaise, bps, MoneyMath.TaxOf(new Paise(nightlyRatePaise), bps).Value);
        }).ToList();
    }

    /// <summary>Latest tariff with effective date ≤ the date, else the rack rate (prototype adminTariffOn / rateFor).</summary>
    public static long RateOn(IEnumerable<(DateOnly EffectiveFrom, long RatePaise)> tariffs, long rackRatePaise, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(tariffs);
        var t = tariffs.Where(x => x.EffectiveFrom <= date).OrderBy(x => x.EffectiveFrom).LastOrDefault();
        return t == default ? rackRatePaise : t.RatePaise;
    }
}
