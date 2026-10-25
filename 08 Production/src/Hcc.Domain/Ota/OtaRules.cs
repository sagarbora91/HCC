using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Ota;

/// <summary>
/// Pure OTA rules ported from the prototype (src/domain/streams/channels.ts): bounds, channel-code format, rate markup,
/// commission (R9) and the ledger arithmetic. Money is paise, rounding half away from zero.
/// </summary>
public static partial class OtaRules
{
    public const int MaxCommissionBps = 3000;
    public const int MaxMarkupBps = 2500;
    public const int PushDays = 7;
    public const int MaxStayNights = 60;

    [GeneratedRegex("^[A-Z0-9-]{2,20}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Z0-9-]{2,20}$")]
    private static partial Regex ChannelCodePattern();

    public static string Pct(int bps) => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>Channel code for a room type: trimmed, upper-cased, 2-20 letters, digits or dashes; blank = unmapped (null).</summary>
    public static string? ChannelCode(string? raw, string roomType)
    {
        var code = (raw ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return null;
        if (!ChannelCodePattern().IsMatch(code)) throw HccException.Validation($"Channel code for {roomType} must be 2-20 letters, digits or dashes.");
        return code;
    }

    /// <summary>Short internal code of a channel (CODE column).</summary>
    public static string InternalCode(string? raw)
    {
        var code = (raw ?? "").Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code)) throw HccException.Validation("Channel code must be 2-20 letters, digits or dashes.");
        return code;
    }

    public static void Terms(int commissionBps, int markupBps)
    {
        if (commissionBps < 0 || commissionBps > MaxCommissionBps) throw HccException.Validation($"Commission must be between 0% and {Pct(MaxCommissionBps)}.");
        if (markupBps < 0 || markupBps > MaxMarkupBps) throw HccException.Validation($"Rate markup must be between 0% and {Pct(MaxMarkupBps)}.");
    }

    /// <summary>Rate pushed to a channel: tariff x (1 + markup), half away from zero (prototype Math.round on a positive amount).</summary>
    public static long MarkedUpRate(long ratePaise, int markupBps)
        => MoneyMath.DivideRoundHalfAwayFromZero(checked(ratePaise * (MoneyMath.BpsScale + markupBps)), MoneyMath.BpsScale);

    /// <summary>Commission on a base (room revenue net of GST): base x bps / 10000, half away from zero; a negative base is symmetric.</summary>
    public static long Commission(long basePaise, int bps)
    {
        if (bps < 0 || bps > MoneyMath.BpsScale) throw HccException.Validation("Rate must be 0-10000 basis points.");
        return MoneyMath.DivideRoundHalfAwayFromZero(checked(basePaise * bps), MoneyMath.BpsScale);
    }

    /// <summary>Reservation statuses that earn commission (prototype R9).</summary>
    public static bool EarnsCommission(string reservationStatus) => reservationStatus is "Confirmed" or "InHouse" or "Departed";

    /// <summary>Ledger change when the booking total moves from <paramref name="oldBase"/> to <paramref name="newBase"/>, so the running sum always equals Commission(newBase).</summary>
    public static long AdjustmentDelta(long oldBase, long newBase, int bps) => Commission(newBase, bps) - Commission(oldBase, bps);

    /// <summary>Check order of the prototype for a New message (R6); returns the first problem or null.</summary>
    public static string? NewMessageProblem(string channelName, string roomTypeCode, bool mapped, DateOnly arrival, DateOnly departure, DateOnly businessDate,
        int adults, int children, int capacity)
    {
        if (!mapped) return $"{roomTypeCode} is not mapped on {channelName}.";
        if (arrival < businessDate) return "Arrival is before the business date.";
        if (departure <= arrival) return "Departure must follow arrival.";
        if (departure.DayNumber - arrival.DayNumber > MaxStayNights) return $"Stays of more than {MaxStayNights} nights are handled at the front desk.";
        var party = adults + children;
        if (capacity <= 0) return $"The hotel has no {roomTypeCode} rooms.";
        if (adults < 1 || adults > 4 || children < 0 || children > 3 || party > capacity) return $"Party of {party} exceeds {roomTypeCode} capacity ({capacity}).";
        return null;
    }
}
