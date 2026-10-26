using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Contracts.Masters;
using Hcc.Contracts.RoomMasters;
using Hcc.Domain.Masters;

namespace Hcc.Domain.RoomMasters;

/// <summary>
/// Rules of the rooms masters (MA-1; prototype domain/streams/roommasters.ts): room-type occupancy and descriptive fields, room numbers,
/// range add (≤ 50), dated amount lists (past amounts frozen, one amount per date), child age band, amenity / feature code lists.
/// Code and name rules are MA-0's (<see cref="MastersRules"/>).
/// </summary>
public static partial class RoomMastersRules
{
    public const int RangeMax = 50;
    public static readonly IReadOnlyList<string> BedTypes = ["Single", "Twin", "Double", "Queen", "King"];
    public static readonly IReadOnlyList<string> Icons = ["wifi", "snowflake", "tv", "coffee", "briefcase", "building", "sofa", "bath", "utensils", "bed", "car", "dumbbell", "waves", "cigarette-off", "accessibility", "baby"];
    public const long MaxPaise = 10_000_000_000;

    [GeneratedRegex("^[A-Z0-9-]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoomNoPattern();

    [GeneratedRegex("^[0-9]{2,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionPattern();

    public sealed record RoomTypeFields(int MaxAdults, int MaxChildren, bool ExtraBedAllowed, string? BedType, int? SizeSqft, IReadOnlyList<string> Amenities, string? Description)
    {
        /// <summary>inv.room_type.max_occupancy (1–10): adults + children.</summary>
        public int MaxOccupancy => MaxAdults + MaxChildren;
    }

    public static RoomTypeFields ValidateRoomType(int maxAdults, int maxChildren, bool extraBed, string? bedType, int? sizeSqft, IReadOnlyList<string>? amenities, string? description)
    {
        if (maxAdults is < 1 or > 8) throw MastersRules.Fail(MasterReasons.Invalid, "Max adults: 1 to 8.", "maxAdults");
        if (maxChildren is < 0 or > 6) throw MastersRules.Fail(MasterReasons.Invalid, "Max children: 0 to 6.", "maxChildren");
        if (maxAdults + maxChildren > 10) throw MastersRules.Fail(MasterReasons.Invalid, "A room type sleeps at most 10 guests.", "maxChildren");
        var bed = Clean(bedType);
        if (bed is not null && !BedTypes.Contains(bed)) throw MastersRules.Fail(MasterReasons.Invalid, "Bed type: choose from the list.", "bedType");
        if (sizeSqft is { } sz && (sz < 50 || sz > 10_000)) throw MastersRules.Fail(MasterReasons.Invalid, "Size: 50 to 10,000 sq ft.", "sizeSqft");
        var desc = Clean(description);
        if (desc is { Length: > 500 }) throw MastersRules.Fail(MasterReasons.Invalid, "Description: at most 500 characters.", "description");
        return new RoomTypeFields(maxAdults, maxChildren, extraBed, bed, sizeSqft, Codes(amenities, "amenities"), desc);
    }

    /// <summary>Upper-case, distinct, valid master codes (amenities, features).</summary>
    public static IReadOnlyList<string> Codes(IReadOnlyList<string>? codes, string field)
    {
        var list = (codes ?? []).Select(MastersRules.NormalizeCode).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (list.Count > 40) throw MastersRules.Fail(MasterReasons.Invalid, "At most 40 amenities.", field);
        foreach (var c in list) _ = MastersRules.Validate(c, c, null);
        return list;
    }

    public static string CodesJson(IReadOnlyList<string> codes) => JsonSerializer.Serialize(codes);

    public static IReadOnlyList<string> CodesFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static string? Clean(string? v)
    {
        var t = (v ?? string.Empty).Trim();
        return t.Length == 0 ? null : t;
    }

    public static string RoomNo(string? roomNo, string field = "roomNo")
    {
        var n = MastersRules.NormalizeCode(roomNo);
        if (!RoomNoPattern().IsMatch(n)) throw MastersRules.Fail(MasterReasons.Invalid, "Room number: 1–10 capital letters, digits or “-”.", field);
        return n;
    }

    /// <summary>Range add: 1–50 distinct valid numbers.</summary>
    public static IReadOnlyList<string> RoomNos(IReadOnlyList<string>? roomNos)
    {
        var list = (roomNos ?? []).Select(n => RoomNo(n, "roomNos")).ToList();
        if (list.Count == 0) throw MastersRules.Fail(MasterReasons.Invalid, "Enter room numbers, e.g. 101–110.", "roomNos");
        if (list.Count > RangeMax) throw MastersRules.Fail(MasterReasons.Invalid, $"At most {RangeMax} rooms at once.", "roomNos");
        var dup = list.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw MastersRules.Fail(MasterReasons.Invalid, $"Room {dup.Key} is listed twice.", "roomNos");
        return list;
    }

    public sealed record RoomFields(IReadOnlyList<string> Features, bool Smoking, string? ConnectingRoomNo, string? Extension, string? Notes);

    public static RoomFields ValidateRoom(string roomNo, IReadOnlyList<string>? features, bool smoking, string? connecting, string? extension, string? notes)
    {
        var con = Clean(connecting)?.ToUpperInvariant();
        if (con is not null && (con == roomNo || !RoomNoPattern().IsMatch(con))) throw MastersRules.Fail(MasterReasons.Invalid, $"Connecting room {con}: choose another existing room.", "connectingRoomNo");
        var ext = Clean(extension);
        if (ext is not null && !ExtensionPattern().IsMatch(ext)) throw MastersRules.Fail(MasterReasons.Invalid, "Phone extension: 2–6 digits.", "extension");
        var n = Clean(notes);
        if (n is { Length: > 200 }) throw MastersRules.Fail(MasterReasons.Invalid, "Notes: at most 200 characters.", "notes");
        return new RoomFields(Codes(features, "features"), smoking, con, ext, n);
    }

    public static string? ValidateIcon(string? icon)
    {
        var i = Clean(icon);
        if (i is not null && !Icons.Contains(i)) throw MastersRules.Fail(MasterReasons.Invalid, "Icon: choose from the list.", "icon");
        return i;
    }

    public static void ValidateAges(int? from, int? to)
    {
        if (from is < 0 or > 17) throw MastersRules.Fail(MasterReasons.Invalid, "Child age from: 0 to 17.", "childAgeFrom");
        if (to is < 0 or > 17) throw MastersRules.Fail(MasterReasons.Invalid, "Child age to: 0 to 17.", "childAgeTo");
        if (from is { } f && to is { } t && f > t) throw MastersRules.Fail(MasterReasons.Invalid, "Child age from must not be above child age to.", "childAgeTo");
    }

    /// <summary>A dated amount list: amounts 0–₹10 crore, ISO dates, one amount per date; sorted by date.</summary>
    public static IReadOnlyList<(DateOnly From, long Paise)> Dated(IReadOnlyList<DatedAmount>? list, string field, string label)
    {
        var result = new List<(DateOnly, long)>();
        foreach (var x in list ?? [])
        {
            if (x.AmountPaise is < 0 or > MaxPaise) throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: each amount must be 0 or more.", field);
            if (!DateOnly.TryParseExact(x.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: each amount needs an effective-from date.", field);
            result.Add((d, x.AmountPaise));
        }

        if (result.Select(x => x.Item1).Distinct().Count() != result.Count) throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: one amount per effective date.", field);
        return result.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>Amounts already in effect (before the business date) cannot change: the past part of both lists must be identical.</summary>
    public static void EnsurePastUnchanged(IReadOnlyList<(DateOnly From, long Paise)> before, IReadOnlyList<(DateOnly From, long Paise)> after, DateOnly businessDate, string field, string label)
    {
        static string Key(IEnumerable<(DateOnly From, long Paise)> l, DateOnly bd) =>
            string.Join("|", l.Where(x => x.From < bd).OrderBy(x => x.From).Select(x => x.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" + x.Paise.ToString(CultureInfo.InvariantCulture)));
        if (Key(before, businessDate) != Key(after, businessDate))
            throw MastersRules.Fail(MasterReasons.Invalid, $"{label}: amounts already in effect cannot change — add a new amount from {businessDate:yyyy-MM-dd} or later.", field);
    }

    /// <summary>"₹2,000 from 2026-11-01; …" — history text of a dated list.</summary>
    public static string DatedText(IEnumerable<(DateOnly From, long Paise)> l) =>
        string.Join("; ", l.Select(x => "₹" + (x.Paise / 100m).ToString("#,##0.##", CultureInfo.InvariantCulture) + " from " + x.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    public static DateOnly BaseFrom => new(2000, 1, 1);
}
