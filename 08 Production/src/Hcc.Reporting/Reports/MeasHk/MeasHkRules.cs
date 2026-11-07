// F12 MEAS · meas-rpt-hk — pure exception rules, targets and helpers shared by the six housekeeping / engineering / asset reports.
// Server twin of 03 Prototype/src/domain/reports/meas-hk/*.ts: same constants, same boundaries (strictly above / below), no I/O.
using System.Globalization;
using Hcc.Application.Reporting.Rpt;

namespace Hcc.Reporting.Reports.MeasHk;

public static class MeasHkRules
{
    public const string RedGroup = "red";
    public const int PassMinPct = 95;           // pass rate under this is red
    public const int DepartureMaxMinutes = 45;  // median departure room above this is red
    public const int OooNoTicketDays = 7;       // out of order more than this many days without a work order is red
    public const int IdleNights = 14;           // sellable and idle this many nights is red
    public const int OverdueDays = 7;           // preventive task overdue by more than this is red
    public const int P1MttrHours = 24;          // P1 mean time to repair above this is red
    public const int RepeatFailures = 3;        // failures of one asset ...
    public const int RepeatWindowDays = 90;     // ... inside this many days is red
    public const int UtilityOverPct = 15;       // more than this above the 3-month average per occupied night is red
    public const int LpgKgPerCylinder = 19;
    public const int DefaultAdrPaise = 205000;  // meas.target.adr default (M-9)
    public const int DefaultLossPct = 2;        // meas.target.lossPct default
    public const int DefaultFfePct = 3;         // ffe.reservePct default (M-5)

    public static bool PassRateRed(int pass, int inspected) => inspected > 0 && pass * 100 < PassMinPct * inspected;

    public static bool MedianDepartureRed(double? medianMinutes) => medianMinutes is { } m && m > DepartureMaxMinutes;

    public static bool OooNoTicketRed(int daysWithoutTicket) => daysWithoutTicket > OooNoTicketDays;

    public static bool IdleRed(int idleNights) => idleNights >= IdleNights;

    /// <summary>Days a preventive task is overdue at <paramref name="asOf"/>: more than <see cref="OverdueDays"/> is red.</summary>
    public static bool OverdueRed(DateOnly due, DateOnly asOf) => asOf.DayNumber - due.DayNumber > OverdueDays;

    public static bool P1MttrRed(double? hours) => hours is { } h && h > P1MttrHours;

    /// <summary>True when 3 failures (created dates of corrective work orders of ONE asset) fall inside a 90-day span (inclusive).</summary>
    public static bool RepeatFailureRed(IReadOnlyList<DateOnly> failures)
    {
        var l = failures.OrderBy(d => d).ToList();
        for (var i = 0; i + RepeatFailures - 1 < l.Count; i++)
            if (l[i + RepeatFailures - 1].DayNumber - l[i].DayNumber <= RepeatWindowDays) return true;
        return false;
    }

    /// <summary>Strictly more than 15 % above the baseline (with the same 1e-9 tolerance as the prototype).</summary>
    public static bool UtilityRed(double? perNight, double? baseline) => perNight is { } p && baseline is { } b && p * 100 > b * (100 + UtilityOverPct) + 1e-9;

    public static bool LinenLossRed(long loss, long par, long lossPct) => par > 0 && loss * 100 > lossPct * par;

    public static bool PastLife(DateOnly? replacementDue, DateOnly asOf) => replacementDue is { } d && asOf > d;

    public static bool ReserveRed(long capexSpent, long accrued) => capexSpent > accrued;

    public static double? Median(IReadOnlyList<double> xs)
    {
        if (xs.Count == 0) return null;
        var a = xs.OrderBy(x => x).ToList();
        var m = a.Count / 2;
        return a.Count % 2 == 1 ? a[m] : (a[m - 1] + a[m]) / 2;
    }

    public static double? Mean(IReadOnlyList<double> xs) => xs.Count == 0 ? null : xs.Average();

    /// <summary>One decimal, half away from zero (JavaScript Math.round on positives), as the prototype's r1.</summary>
    public static double R1(double n) => Math.Round(n * 10, MidpointRounding.AwayFromZero) / 10;

    /// <summary>Two-decimal text; null stays null.</summary>
    public static string? Dec2(double? n) => n is null ? null : (Math.Round(n.Value * 100, MidpointRounding.AwayFromZero) / 100).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Morning 06:00-13:59, Afternoon 14:00-21:59, Night otherwise; no start time = Morning.</summary>
    public static string ShiftOf(int? startHour) => startHour is not { } h ? "Morning" : h is >= 6 and < 14 ? "Morning" : h is >= 14 and < 22 ? "Afternoon" : "Night";

    public static int WholeMonths(DateOnly a, DateOnly b)
    {
        var m = (b.Year - a.Year) * 12 + b.Month - a.Month;
        if (b.Day < a.Day) m--;
        return Math.Max(0, m);
    }

    public static string AgeText(int months) => $"{months / 12} y {months % 12} m";

    public static ReportRow Row(Dictionary<string, object?> cells, bool red = false) => new(ReportRowKind.Row, cells, red ? RedGroup : null);

    public static string CaptureNote(string basis, string need) => $"{basis} Nothing is captured yet — capture needed: {need}.";

    public static ReportColumn T(string key, string label) => new(key, label, ReportColumnType.Text, false);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn P(string key, string label) => new(key, label, ReportColumnType.Percent, false);

    public static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);
}
