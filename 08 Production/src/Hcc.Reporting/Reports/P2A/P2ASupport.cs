// F8-RPT-P2-A · shared pieces of the batch A selectors (guests + rooms). Twin rules of 03 Prototype/src/domain/reports/p2-a/common.ts. Money = long paise.
using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.Guests;

namespace Hcc.Reporting.Reports.P2A;

internal static partial class P2A
{
    public static ReportColumn T(string key, string label, ReportMask mask = ReportMask.None) => new(key, label, ReportColumnType.Text, false, mask);

    public static ReportColumn I(string key, string label) => new(key, label, ReportColumnType.Int, false);

    public static ReportColumn D(string key, string label) => new(key, label, ReportColumnType.Date, false);

    public static ReportColumn M(string key, string label) => new(key, label, ReportColumnType.Money, true);

    public static ReportRow Row(ReportRowKind kind, Dictionary<string, object?> cells, string? group = null) => new(kind, cells, group);

    public static Dictionary<string, object?> Cells() => new(StringComparer.Ordinal);

    public static string Plural(long n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    /// <summary>Numeric-aware ordering, like the prototype's <c>localeCompare(…, numeric)</c>.</summary>
    public static readonly StringComparer Natural = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering);

    /// <summary>Whole paise, half away from zero.</summary>
    public static long DivRound(long a, long b) => b == 0 ? 0 : Math.Sign(a) * ((Math.Abs(a) * 2 + b) / (2 * b));

    public static string Cap(string t) => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];

    [GeneratedRegex(@"extra\s+(person|bed|guest|adult)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex ExtraRx();

    [GeneratedRegex(@"meal|breakfast|\b(CP|MAP|AP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex MealRx();

    [GeneratedRegex(@"laund|dry.?clean|ironing|press(ing)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex LaundryRx();

    [GeneratedRegex(@"banquet|\bhall\b|conference|meeting room", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex BanquetRx();

    [GeneratedRegex(@"food|restaurant|room service|breakfast|lunch|dinner|meal|snack|minibar|mini-bar|\btea\b|coffee|chai|beverage|juice|water bottle", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex FoodRx();

    /// <summary>Room-wise column of a charge: tariff / extra / other (meal plan lines, discounts, corrections) / null (food, laundry and other services are not room charges).</summary>
    public static string? RoomColumn(string kind, string label)
    {
        if (kind == "room") return "tariff";
        if (kind is "discount" or "correction") return "other";
        if (ExtraRx().IsMatch(label)) return "extra";
        return MealRx().IsMatch(label) ? "other" : null;
    }

    /// <summary>Service-line group: catalogue extra, POS bill → food, otherwise the label words (laundry, food; banquet and the rest → other).</summary>
    public static string ServiceGroup(string label, bool extra)
    {
        if (extra) return "extras";
        if (label.Contains(" · bill ", StringComparison.Ordinal)) return "food";
        var t = label.ToLowerInvariant();
        if (LaundryRx().IsMatch(t)) return "laundry";
        if (BanquetRx().IsMatch(t)) return "other";
        return FoodRx().IsMatch(t) ? "food" : "other";
    }

    /// <summary>SQL fragment: the room a charge <c>c</c> of reservation <c>r</c> belongs to on its service date (the dated stay segment, else the booked room).</summary>
    public const string ChargeRoomIdExpr = """
        COALESCE((SELECT TOP (1) sg.room_id FROM gst.stay_segment sg JOIN gst.stay sy ON sy.stay_id = sg.stay_id
                   WHERE sy.reservation_id = r.reservation_id AND sg.from_date <= c.service_date AND (sg.to_date IS NULL OR c.service_date < sg.to_date) ORDER BY sg.seq DESC), r.room_id)
        """;

    public static string StatusOf(string reservationStatus) => reservationStatus switch
    {
        "InHouse" => "checked in", "Departed" => "checked out", "NoShow" => "no-show", "Held" => "held", "Confirmed" => "confirmed", "Cancelled" => "cancelled", "Expired" => "expired", _ => reservationStatus.ToLowerInvariant(),
    };

    /// <summary>Reservation holds a room: confirmed, in house, departed, or a hold that is still running.</summary>
    public const string HoldingExpr = "(r.status IN ('Confirmed','InHouse','Departed') OR (r.status = 'Held' AND (r.hold_until_utc IS NULL OR r.hold_until_utc > SYSUTCDATETIME())))";

    public static ReportAbout About(ReportDefinition def, BoundParams p) => new(DateTimeOffset.MinValue, "", def.DateBasis, false, p.Legacy, p.From, p.To);

    public static void Range(GuestsSql.Clock clock, BoundParams p, int max, int ahead = 0) => GuestsSql.RangeGuard(p.From, p.To, max, clock.BusinessDate, ahead);
}
