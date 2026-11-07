// F12-RPT-GUEST · pure rules shared by the six guest-experience measurement reports. Twin of 03 Prototype/src/domain/reports/meas-guest/*.ts:
// same rounding (half up), same lower median, same exception rules. No SQL here, so every rule is unit-tested without a database.
using System.Globalization;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting.Reports.MeasGuest;

public static class MeasGuestRules
{
    public static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn P(string key, string label, bool money = false) => new(key, label, ReportColumnType.Percent, money);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public static ReportColumn B(string key, string label) => new(key, label, ReportColumnType.Bool, false);

    /// <summary>Round half up (toward +infinity), the prototype's Math.floor(x + 0.5).</summary>
    public static long RoundHalf(decimal x) => (long)Math.Floor(x + 0.5m);

    /// <summary>Percent with one decimal (45.5), null when the denominator is 0.</summary>
    public static double? Pct1(long num, long den) => den > 0 ? (double)(RoundHalf(num * 1000m / den) / 10m) : null;

    /// <summary>One-decimal text of sum / n, null when n is 0.</summary>
    public static string? Avg1(decimal sum, long n) => n > 0 ? (RoundHalf(sum * 10m / n) / 10m).ToString("0.0", CultureInfo.InvariantCulture) : null;

    /// <summary>Lower median (no fractions), null for an empty list.</summary>
    public static long? LowerMedian(IEnumerable<long> xs)
    {
        var a = xs.OrderBy(x => x).ToList();
        return a.Count == 0 ? null : a[(a.Count - 1) >> 1];
    }

    public static string PrevMonth(string m)
    {
        int y = int.Parse(m[..4], CultureInfo.InvariantCulture), mo = int.Parse(m[5..7], CultureInfo.InvariantCulture);
        return mo == 1 ? $"{y - 1}-12" : $"{y}-{mo - 1:00}";
    }

    public static string CapNote(string basis, string what) => $"{basis} Capture needed: {what}. Nothing has been captured for these filters yet.";

    // ── satisfaction ──
    public const int UnansweredMinutes = 48 * 60;

    public static long? Nps(IReadOnlyCollection<int> recommend)
    {
        if (recommend.Count == 0) return null;
        return RoundHalf((recommend.Count(r => r >= 9) - recommend.Count(r => r <= 6)) * 100m / recommend.Count);
    }

    public static bool SurveyLow(int overall, int cleanliness, int staff, int value, int? breakfast) => Math.Min(Math.Min(overall, cleanliness), Math.Min(Math.Min(staff, value), breakfast ?? 5)) <= 2;

    /// <summary>Red row: NPS below target, or any score of 2 or lower unanswered for 48 hours.</summary>
    public static bool SatisfactionRed(long? nps, long target, int unanswered) => (nps is { } n && n < target) || unanswered > 0;

    // ── reputation ──
    public const int DropHundredths = 30;
    public const int ResponseFloorPct = 80;

    public static long Hundredths(decimal score) => (long)Math.Round(score * 100m, MidpointRounding.AwayFromZero);

    /// <summary>Red row: score drop of 0.3 or more against the previous month of the same platform; response rate under 80 %.</summary>
    public static bool ReputationRed(decimal avg, decimal? prevAvg, int count, int responded) =>
        (prevAvg is { } p && Hundredths(p) - Hundredths(avg) >= DropHundredths) || (count > 0 && responded * 100L < ResponseFloorPct * (long)count);

    // ── service recovery ──
    public const int RepeatDays = 90;

    // ── desk timing ──
    public static bool DeskRed(long? arrivalToKey, long? billToCheckout, long checkinTarget, long checkoutTarget) =>
        (arrivalToKey is { } a && a > checkinTarget) || (billToCheckout is { } b && b > checkoutTarget);

    // ── upsell ──
    public const long UpsellTargetDefault = 5000;

    public static bool UpsellRed(int freeNoApprover, long? perArrival, long target) => freeNoApprover > 0 || (perArrival is { } p && p < target);

    // ── repeat ──
    /// <summary>Red row: the repeat share falls two months running (this month below the previous below the one before).</summary>
    public static bool RepeatFalling(double? a, double? b, double? c) => a is { } x && b is { } y && c is { } z && x < y && y < z;
}
