// F12-RPT-REVENUE · pure rules of the six revenue-management reports. Server twin of 03 Prototype/src/domain/reports/meas-revenue/logic.ts: same names in PascalCase,
// same integer arithmetic, same worked numbers (tests/Hcc.Reporting.Tests/MeasRevenueTests.cs). Money = paise.
namespace Hcc.Reporting.Reports.MeasRevenue;

public static class MeasRevenueLogic
{
    public static readonly string[] Weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    public static readonly string[] LeadBuckets = ["0-7 days", "8-30 days", "31-90 days", "Over 90 days"];

    /// <summary>Forecast 85 % or more with the BAR unchanged, or forecast 25 % or less with no offer open.</summary>
    public static bool DemandRed(int forecastPct, bool barUnchanged, bool offerOpen) => (forecastPct >= 85 && barUnchanged) || (forecastPct <= 25 && !offerOpen);

    /// <summary>Pace in tenths of a percent against last year's same snapshot; null when last year had nothing.</summary>
    public static int? PaceTenths(long cur, long ly) => ly > 0 ? (int)Math.Floor((cur - ly) * 1000.0 / ly + 0.5) : null;

    /// <summary>Worse than minus 10 % (exactly -10.0 % is not red).</summary>
    public static bool PaceRed(int? tenths) => tenths is < -100;

    public static bool DisplacementRed(long netPaise) => netPaise < 0;

    public static bool OversoldRed(int over, int allowed) => over > allowed;

    /// <summary>The night stayed under 40 % occupancy (integer compare).</summary>
    public static bool RestrictionRed(int rooms, int sellable) => sellable > 0 && rooms * 100 < 40 * sellable;

    /// <summary>Median of whole paise; even count = floor of the middle pair's mean.</summary>
    public static long? Median(IReadOnlyCollection<long> xs)
    {
        if (xs.Count == 0) return null;
        var a = xs.Order().ToArray();
        var m = a.Length >> 1;
        return a.Length % 2 == 1 ? a[m] : (long)Math.Floor((a[m - 1] + a[m]) / 2.0);
    }

    /// <summary>OTA rate below direct; or our rate more than 15 % above the set median with occupancy under 40 %.</summary>
    public static bool ParityRed(long? direct, long? ota, long? median, int rooms, int sellable)
    {
        if (direct is { } d && ota is { } o && o < d) return true;
        return direct is { } d2 && median is { } m && d2 * 100 > m * 115 && RestrictionRed(rooms, sellable);
    }

    public static int? PositionPct(long? ours, long? median) =>
        ours is { } o && median is { } m && m > 0 ? (int)Math.Floor((o - m) * 100.0 / m + 0.5) : null;

    public static string LeadBucket(int days) => days <= 7 ? LeadBuckets[0] : days <= 30 ? LeadBuckets[1] : days <= 90 ? LeadBuckets[2] : LeadBuckets[3];

    /// <summary>Accept when the net is not a loss; counter when the break-even rate is within 150 % of the offer; else decline.</summary>
    public static string Recommendation(long netPaise, long offeredPaise, long counterPaise) =>
        netPaise >= 0 ? "accept" : counterPaise * 100 <= offeredPaise * 150 ? "counter-rate" : "decline";

    /// <summary>Per-night break-even counter rate: displaced revenue per group room, rounded up to Rs 50.</summary>
    public static long CounterRate(long displacedPaise, int rooms) =>
        rooms > 0 ? (((displacedPaise + rooms - 1) / rooms) + 4999) / 5000 * 5000 : 0;

    public static int Dow(DateOnly d) => (int)d.DayOfWeek;

    public static int Pct(int a, int b) => b > 0 ? (int)Math.Floor(a * 100.0 / b + 0.5) : 0;

    /// <summary>One decimal, half up (the prototype's Math.round(x * 1000 / n) / 10).</summary>
    public static double Tenths(int a, int b) => b > 0 ? Math.Floor(a * 1000.0 / b + 0.5) / 10 : 0;
}
