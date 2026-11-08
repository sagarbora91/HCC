// F8-QW-NS · Due no-shows (QWQ-7): pure rules shared by the query and its tests. Twin of prototype src/domain/streams/noshow.ts.
using System.Globalization;

namespace Hcc.Application.Reservations;

public static class NoShowRules
{
    public const string DefaultCutoff = "23:00";

    /// <summary>The cut-off as HH:mm; anything malformed reads as the default 23:00.</summary>
    public static TimeOnly Cutoff(string? hhmm) =>
        TimeOnly.TryParseExact(hhmm ?? "", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.Parse(DefaultCutoff, CultureInfo.InvariantCulture);

    /// <summary>True from the cut-off minute on the business date (local property time), or on any later calendar day.</summary>
    public static bool PastCutoff(DateOnly businessDate, DateTime localNow, TimeOnly cutoff)
    {
        var d = DateOnly.FromDateTime(localNow);
        return d > businessDate || (d == businessDate && TimeOnly.FromDateTime(localNow) >= cutoff);
    }

    /// <summary>No-show charge (paise) = first night x no-show bps, rounded half away from zero (twin of fdmNoShowCharge).</summary>
    public static long Charge(long nightlyRatePaise, int noShowBps) =>
        (long)Math.Round(nightlyRatePaise * (decimal)noShowBps / 10000m, 0, MidpointRounding.AwayFromZero);

    /// <summary>"Policy &lt;name&gt;: no-show charge ₹… — not posted" (DEC-MA-5b: shown, never posted). A null charge (no money permission) omits the amount.</summary>
    public static string ChargeText(string? policyName, long? chargePaise) =>
        $"Policy {policyName ?? "none"}: no-show charge {(chargePaise is { } c ? "₹" + (c / 100m).ToString("#,##0.##", CultureInfo.InvariantCulture) : "hidden")} — not posted";
}
