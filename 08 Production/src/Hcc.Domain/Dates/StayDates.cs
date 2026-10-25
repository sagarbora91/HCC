using System.Globalization;
using Hcc.Domain.Errors;

namespace Hcc.Domain.Dates;

/// <summary>
/// Calendar/business/service dates are <see cref="DateOnly"/> (SQL <c>DATE</c>, wire <c>YYYY-MM-DD</c>; contracts §4 rule 10).
/// Ports the prototype's <c>nights()</c>/<c>addDays()</c> (<c>03 Prototype/src/domain/money.ts</c>) with validation.
/// </summary>
public static class StayDates
{
    public const string WireFormat = "yyyy-MM-dd";

    /// <summary>Parses a strict <c>YYYY-MM-DD</c> wire date; anything else is a VALIDATION error.</summary>
    public static DateOnly Parse(string? value, string field = "date")
    {
        if (value is null || !DateOnly.TryParseExact(value, WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw HccException.Validation($"{field} must be a date in YYYY-MM-DD form.");
        return d;
    }

    public static string ToWire(DateOnly date) => date.ToString(WireFormat, CultureInfo.InvariantCulture);

    /// <summary>Nights in the half-open stay [arrival, departure). Departure must follow arrival.</summary>
    public static int Nights(DateOnly arrival, DateOnly departure)
    {
        var n = departure.DayNumber - arrival.DayNumber;
        if (n <= 0) throw HccException.Validation("Departure must follow arrival.");
        return n;
    }

    /// <summary>Each night of the stay in ascending order (the lock order for <c>inv.room_night</c> inserts, contracts §4 rule 1).</summary>
    public static IEnumerable<DateOnly> EachNight(DateOnly arrival, DateOnly departure)
    {
        var n = Nights(arrival, departure);
        for (var i = 0; i < n; i++) yield return arrival.AddDays(i);
    }

    /// <summary>Half-open interval overlap: adjacent stays (departure = next arrival) do not overlap.</summary>
    public static bool Overlaps(DateOnly arrivalA, DateOnly departureA, DateOnly arrivalB, DateOnly departureB)
        => arrivalA < departureB && departureA > arrivalB;
}
