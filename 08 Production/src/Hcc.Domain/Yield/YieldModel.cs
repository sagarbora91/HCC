using System.Globalization;

namespace Hcc.Domain.Yield;

/// <summary>Rate plan derived from the night's suggested BAR (prototype YLDPlan). Sold on <see cref="SellOnDays"/> (0 = Sunday) or every night when null.</summary>
public sealed record YieldPlan(string Code, string Name, int AdjBps, int MinNights, IReadOnlyList<int>? SellOnDays, string? DayUseFrom, string? DayUseTo,
    int? CloseAtOcc, bool Active, string Note)
{
    public bool IsDayUse => CloseAtOcc is not null;
}

public sealed record YieldSeason(int Id, string Name, DateOnly From, DateOnly To, int AdjBps, IReadOnlyList<int> RoomTypeIds, int Priority);

public sealed record YieldOccRule(int Id, string Op, int Occ, int AdjBps);

public sealed record YieldRestrictionDef(string Code, int? RoomTypeId, DateOnly From, DateOnly To, int MinStay, bool ClosedToArrival, string Note);

public sealed record YieldOverrideDef(int RoomTypeId, DateOnly Night, long RatePaise, string Reason);

/// <summary>Guardrails: total adjustment is clamped to [FloorBps, CeilBps]; rates round to <see cref="RoundToPaise"/> (5000 = ₹50).</summary>
public sealed record YieldGuard(int FloorBps, int CeilBps, int RoundToPaise);

public sealed record YieldConfig(IReadOnlyList<YieldPlan> Plans, IReadOnlyList<YieldSeason> Seasons, IReadOnlyList<int> Dow, IReadOnlyList<YieldOccRule> OccRules,
    YieldGuard Guard, IReadOnlyList<YieldRestrictionDef> Restrictions, IReadOnlyList<YieldOverrideDef> Overrides);

public sealed record YieldRoomType(int Id, string Code, long RackPaise);

public sealed record TariffRow(int RoomTypeId, DateOnly EffectiveFrom, long RatePaise);

/// <summary>What the engine reads from inventory: business date, types, every tariff row, which rows yield wrote, sellable/on-books per night, simulated forecast.</summary>
public sealed record YieldInputs(DateOnly BusinessDate, IReadOnlyList<YieldRoomType> Types, IReadOnlyList<TariffRow> Tariffs, IReadOnlySet<TariffRow> Owned,
    IReadOnlyDictionary<DateOnly, int> Sellable, IReadOnlyDictionary<DateOnly, int> OnBooks, IReadOnlyDictionary<DateOnly, int> Forecast);

public sealed record YieldOcc(int OnBooks, int Forecast, int Sellable, int Pct, bool Simulated);

public sealed record YieldStep(string Label, int Bps, string Detail);

public sealed record YieldNight(int RoomTypeId, string RoomType, DateOnly Date, long Base, string BaseSource, IReadOnlyList<YieldStep> Steps, int RawBps,
    int AppliedBps, string? Clamped, long Computed, long? Override, long Suggested, long Live, YieldOcc Occ, IReadOnlyList<YieldRestrictionDef> Restrictions, string Explain);

public sealed record YieldPublishRow(int RoomTypeId, DateOnly Date, long Before, long After, bool Closing);

public sealed record YieldPublishPlan(IReadOnlyList<YieldPublishRow> Rows, IReadOnlyList<TariffRow> Tariffs);

public sealed record YieldQuoteLine(DateOnly Date, long Rate, long Bar);

public sealed record YieldQuote(YieldPlan Plan, int RoomTypeId, DateOnly Arrival, int Nights, IReadOnlyList<YieldQuoteLine> Rows, long Total, IReadOnlyList<string> Issues);

/// <summary>Display helpers matching the prototype's wording (yldPct, money).</summary>
public static class YieldFormat
{
    public static readonly string[] DowNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static string Pct(int bps)
    {
        var sign = bps > 0 ? "+" : bps < 0 ? "−" : "±";
        var v = Math.Abs(bps) / 100m;
        return sign + v.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>₹ with Indian digit grouping, paise shown only when non-zero (prototype money()).</summary>
    public static string Money(long paise)
    {
        var neg = paise < 0;
        var abs = Math.Abs(paise);
        var rupees = abs / 100;
        var s = rupees.ToString(CultureInfo.InvariantCulture);
        if (s.Length > 3)
        {
            var head = s[..^3];
            var parts = new List<string>();
            while (head.Length > 2) { parts.Insert(0, head[^2..]); head = head[..^2]; }
            if (head.Length > 0) parts.Insert(0, head);
            s = string.Join(",", parts) + "," + s[^3..];
        }

        var frac = abs % 100;
        return (neg ? "-" : "") + "₹" + s + (frac == 0 ? "" : "." + frac.ToString("00", CultureInfo.InvariantCulture));
    }
}
