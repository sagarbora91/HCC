using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Domain.Masters;
using Hcc.Domain.Tax;

namespace Hcc.Domain.Pos;

/// <summary>
/// MA-4 POS masters rules (plan Part 2 §8 F, M26–M28), ported from <c>03 Prototype/src/domain/streams/pos-master.ts</c>: outlet types and
/// GST profiles, menu GST classes, timings, table numbers, kitchen stations, "sold out today", dated prices. Code / name / sort order follow
/// <see cref="MastersRules"/> (MA-0). GST rates stay in the GST master (DEC-069); a profile only picks which rate — illustrative, CA to confirm.
/// </summary>
public static partial class PosMasterRules
{
    public const string Outlets = "outlets";
    public const string Tables = "posTables";
    public const string Categories = "menuCategories";
    public const string Items = "menuItems";

    public const string DefaultStation = "Kitchen";

    /// <summary>Effective-from of a price that has always applied (fixture / legacy / imported single price).</summary>
    public static readonly DateOnly PriceSince = new(2017, 7, 1);

    public static readonly IReadOnlyList<string> OutletTypes = ["restaurant", "room-service", "bar", "banquet"];
    public static readonly IReadOnlyList<string> GstProfiles = ["hotel", "standard", "specified"];
    public static readonly IReadOnlyList<string> GstClasses = ["food", "laundry", "banquet", "other"];

    [GeneratedRegex("^([01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();

    [GeneratedRegex("^[A-Za-z0-9 -]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex TableNoPattern();

    [GeneratedRegex("^[A-Za-z0-9-]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShortCodePattern();

    /// <summary>Check kind of an outlet type: room service takes checks on in-house rooms, every other type on tables.</summary>
    public static string KindOf(string type) => type == "room-service" ? "room" : "table";

    public static string Type(string? type) => type is not null && OutletTypes.Contains(type) ? type
        : throw MastersRules.Fail(MasterReasons.Invalid, "Type: restaurant, room service, bar or banquet.", "type");

    public static string Profile(string? profile) => profile is null ? "hotel" : GstProfiles.Contains(profile) ? profile
        : throw MastersRules.Fail(MasterReasons.Invalid, "GST profile: hotel status, 5 % (standard) or 18 % (specified premises).", "gstProfile");

    public static string GstClass(string? cls) => cls is null ? "food" : GstClasses.Contains(cls) ? cls
        : throw MastersRules.Fail(MasterReasons.Invalid, "GST class: food, laundry, banquet or other.", "gstClass");

    /// <summary>HH:MM (24-hour) or null.</summary>
    public static string? Time(string? value, string field)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        return TimePattern().IsMatch(t) ? t : throw MastersRules.Fail(MasterReasons.Invalid, $"{(field == "opens" ? "Opens" : "Closes")} at: use HH:MM (24-hour), e.g. 07:00.", field);
    }

    /// <summary>Optional text trimmed to null, at most <paramref name="max"/> characters.</summary>
    public static string? Text(string? value, int max, string field, string label)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        return t.Length <= max ? t : throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: at most {max} characters.", field);
    }

    public static string TableNo(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        return TableNoPattern().IsMatch(t) ? t : throw MastersRules.Fail(MasterReasons.Invalid, "Table / counter no.: 1–10 letters, digits, spaces or “-”.", "tableNo");
    }

    public static int? Seats(int? seats) => seats is null or (>= 1 and <= 60) ? seats
        : throw MastersRules.Fail(MasterReasons.Invalid, "Seats: 1 to 60.", "seats");

    public static string? ShortCode(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        return ShortCodePattern().IsMatch(t) ? t : throw MastersRules.Fail(MasterReasons.Invalid, "Short code: 1–10 letters, digits or “-”.", "shortCode");
    }

    /// <summary>Code from a name ("Breads &amp; rice" → BREADS-RICE), as the prototype seeds category codes (posCodeOf).</summary>
    public static string CodeOf(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in (name ?? string.Empty).ToUpperInvariant())
        {
            var ok = ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';
            if (ok) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        var code = sb.ToString().Trim('-');
        if (code.Length > MastersRules.CodeMax) code = code[..MastersRules.CodeMax].TrimEnd('-');
        return code.Length == 0 ? "X" : code;
    }

    /// <summary>Kitchen station of an item at an outlet: item station → outlet KOT station → Kitchen.</summary>
    public static string StationOf(string? itemStation, string? outletStation) =>
        !string.IsNullOrWhiteSpace(itemStation) ? itemStation : !string.IsNullOrWhiteSpace(outletStation) ? outletStation : DefaultStation;

    /// <summary>
    /// The KOT groups of one send, in order of first appearance; the station is written on the tickets only when a station is configured
    /// (more than one group, a non-default station, or an outlet KOT station) — otherwise one ticket without a station, as before MA-4.
    /// </summary>
    public static IReadOnlyList<(string? Station, IReadOnlyList<long> LineIds)> KotGroups(IEnumerable<(long LineId, string? ItemStation)> lines, string? outletStation)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var groups = new List<(string Station, List<long> Ids)>();
        foreach (var (id, st) in lines)
        {
            var s = StationOf(st, outletStation);
            var g = groups.FindIndex(x => x.Station == s);
            if (g < 0) groups.Add((s, [id]));
            else groups[g].Ids.Add(id);
        }

        var configured = groups.Count > 1 || (groups.Count == 1 && groups[0].Station != DefaultStation) || !string.IsNullOrWhiteSpace(outletStation);
        return groups.Select(g => (configured ? g.Station : null, (IReadOnlyList<long>)g.Ids)).ToList();
    }

    /// <summary>"Sold out today": available = false lapses after the business date it was set on (a legacy "86" without a date stays).</summary>
    public static bool SoldOut(bool available, DateOnly? soldOutOn, DateOnly businessDate) => !available && (soldOutOn is null || soldOutOn >= businessDate);

    /// <summary>The price in force on a date: the latest effective-from on or before it (null = no price yet).</summary>
    public static long? PriceOn(IEnumerable<(DateOnly From, long Paise)> prices, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(prices);
        long? p = null;
        foreach (var x in prices.Where(x => x.From <= date).OrderBy(x => x.From)) p = x.Paise;
        return p;
    }

    /// <summary>
    /// Validates a new dated price list against the stored one: one amount per date, 0 … ₹10 crore, and amounts already in effect (dated
    /// before the business date) unchanged — a new price is added from the business date or later (tariff rule, DEC-058).
    /// </summary>
    public static IReadOnlyList<(DateOnly From, long Paise)> Prices(IReadOnlyList<(DateOnly From, long Paise)> next, IReadOnlyList<(DateOnly From, long Paise)> before, DateOnly businessDate)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(before);
        if (next.Any(x => x.Paise is < 0 or > 10_000_000_000)) throw MastersRules.Fail(MasterReasons.Invalid, "Price: each amount must be from ₹0 to ₹10 crore.", "price");
        if (next.Select(x => x.From).Distinct().Count() != next.Count) throw MastersRules.Fail(MasterReasons.Invalid, "Price: one amount per effective date.", "price");
        static string Past(IEnumerable<(DateOnly From, long Paise)> l, DateOnly d) =>
            string.Join("|", l.Where(x => x.From < d).OrderBy(x => x.From).Select(x => x.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" + x.Paise.ToString(CultureInfo.InvariantCulture)));
        if (Past(next, businessDate) != Past(before, businessDate))
            throw MastersRules.Fail(MasterReasons.Invalid, $"Price: amounts already in effect cannot change — add a new amount from {businessDate:yyyy-MM-dd} or later.", "price");
        return next.OrderBy(x => x.From).ToList();
    }

    /// <summary>"₹60 from 2017-07-01; ₹70 from 2026-11-01" (history text).</summary>
    public static string PricesText(IEnumerable<(DateOnly From, long Paise)> prices) =>
        string.Join("; ", prices.OrderBy(x => x.From).Select(x => $"{new Hcc.Domain.Money.Paise(x.Paise)} from {x.From:yyyy-MM-dd}"));

    /// <summary>GST category of a menu GST class (food → the outlet profile decides the food rate).</summary>
    public static GstCategory CategoryOf(string gstClass) => gstClass switch
    {
        "laundry" => GstCategory.Laundry,
        "banquet" => GstCategory.Banquet,
        "other" => GstCategory.Other,
        _ => GstCategory.Food,
    };

    /// <summary>Kitchen ticket label with its station, e.g. <c>RE-KOT 5 · Tandoor</c>.</summary>
    public static string KotLabel(string outletName, int kotNo, string? station) =>
        PosRules.KotLabel(outletName, kotNo) + (string.IsNullOrEmpty(station) ? string.Empty : " · " + station);
}
