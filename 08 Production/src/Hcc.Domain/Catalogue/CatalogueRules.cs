using System.Globalization;
using Hcc.Contracts.Catalogue;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Masters;

namespace Hcc.Domain.Catalogue;

/// <summary>
/// Rules of the MA-2 catalogue masters (prototype domain/streams/catalogue.ts): meal plans (meals ⊆ B L D, GST treatment bundled |
/// separate — Illustrative, CA to confirm — room types allowed, dated add-on per guest per night), extras (category, GST category from the
/// GST master keys, unit, department, dated default price) and discount reasons (max % and approval % of the folio's charges in basis points,
/// roles OWN / MGR / REC). Code, name and sort order follow <see cref="MastersRules"/>. Dated amounts: one per date, 0 … ₹10 crore, and the
/// amounts already in effect on the business date can never change (a new amount from that date or later is added).
/// </summary>
public static class CatalogueRules
{
    public static readonly IReadOnlyList<string> Meals = ["B", "L", "D"];
    public static readonly IReadOnlyList<string> GstTreatments = ["bundled", "separate"];
    public static readonly IReadOnlyList<string> Categories = ["transport", "laundry", "fnb", "room", "misc"];
    public static readonly IReadOnlyList<string> GstCategories = ["food", "laundry", "banquet", "other"];
    public static readonly IReadOnlyList<string> Units = ["each", "piece", "bag", "night", "trip", "hour", "person"];
    public static readonly IReadOnlyList<string> Departments = ["frontdesk", "housekeeping", "laundry", "restaurant", "transport", "banquet", "maintenance"];
    public static readonly IReadOnlyList<string> DiscountRoles = ["OWN", "MGR", "REC"];
    public const int QuantityMax = 99;
    public const long AmountMax = 10_000_000_000;

    public static HccException Invalid(string message, string? field = null) => MastersRules.Fail(MasterReasons.Invalid, message, field);

    private static string Choose(string? value, IReadOnlyList<string> allowed, string label, string field, bool required = true)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0 && !required) return v;
        if (!allowed.Contains(v)) throw Invalid($"{label}: choose from the list.", field);
        return v;
    }

    /// <summary>Meals in canonical order (B, L, D), each at most once.</summary>
    public static string MealsCode(IReadOnlyList<string>? meals)
    {
        var set = (meals ?? []).Select(m => (m ?? string.Empty).Trim().ToUpperInvariant()).ToList();
        if (set.Any(m => !Meals.Contains(m))) throw Invalid("Meals included: breakfast (B), lunch (L) or dinner (D).", "meals");
        if (set.Distinct().Count() != set.Count) throw Invalid("Meals included: each meal once.", "meals");
        return string.Concat(Meals.Where(set.Contains));
    }

    public static IReadOnlyList<string> SplitList(string? stored) =>
        string.IsNullOrEmpty(stored) ? [] : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string GstTreatment(string? v) => Choose(v, GstTreatments, "GST treatment", "gst");

    /// <summary>Room type codes, distinct, 1–20 characters each, at most 400 characters stored.</summary>
    public static string RoomTypes(IReadOnlyList<string>? types)
    {
        var list = (types ?? []).Select(t => (t ?? string.Empty).Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (list.Any(t => t.Length > 20 || t.Contains(',', StringComparison.Ordinal))) throw Invalid("Room types allowed: room type codes (up to 20 characters, no commas).", "roomTypes");
        var joined = string.Join(",", list);
        if (joined.Length > 400) throw Invalid("Room types allowed: too many room types.", "roomTypes");
        return joined;
    }

    public static string Category(string? v) => Choose(v, Categories, "Category", "category");

    public static string GstCategory(string? v) => Choose(v, GstCategories, "GST class", "gst");

    public static string Unit(string? v) => Choose(v, Units, "Unit", "unit");

    public static string? Department(string? v)
    {
        var d = Choose(v, Departments, "Department", "department", required: false);
        return d.Length == 0 ? null : d;
    }

    /// <summary>Roles of a discount reason, canonical order (OWN, MGR, REC).</summary>
    public static string Roles(IReadOnlyList<string>? roles)
    {
        var set = (roles ?? []).Select(r => (r ?? string.Empty).Trim().ToUpperInvariant()).ToList();
        if (set.Any(r => !DiscountRoles.Contains(r))) throw Invalid("Shown to roles: Owner (OWN), Manager (MGR) or Reception (REC).", "roles");
        return string.Join(",", DiscountRoles.Where(set.Contains));
    }

    public static (int Max, int? ApprovalAbove) DiscountLimits(int maxBps, int? approvalAboveBps)
    {
        if (maxBps is < 1 or > 10000) throw Invalid("Maximum discount: 0.01 % to 100 % of the charges.", "maxPct");
        if (approvalAboveBps is { } a && (a < 0 || a > 10000)) throw Invalid("Needs approval above: 0 % to 100 %.", "approvalAbovePct");
        if (approvalAboveBps is { } b && b >= maxBps) throw Invalid("“Needs approval above” must be below the maximum discount.");
        return (maxBps, approvalAboveBps);
    }

    /// <summary>
    /// Validates dated amounts (distinct dates, 0 … ₹10 crore) and that the amounts in effect before <paramref name="businessDate"/> are the same
    /// as before (prototype mstProblems datedMoney rule). Returns them sorted by date.
    /// </summary>
    public static IReadOnlyList<(DateOnly From, long Amount)> DatedAmounts(IReadOnlyList<DatedAmount>? input, IReadOnlyList<(DateOnly From, long Amount)> before,
        DateOnly businessDate, string label, string field)
    {
        ArgumentNullException.ThrowIfNull(before);
        var list = new List<(DateOnly, long)>();
        foreach (var x in input ?? [])
        {
            if (x is null || !DateOnly.TryParseExact(x.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                throw Invalid($"{label}: each amount needs an effective-from date.", field);
            if (x.AmountPaise < 0 || x.AmountPaise > AmountMax) throw Invalid($"{label}: each amount must be 0 or more.", field);
            list.Add((d, x.AmountPaise));
        }

        if (list.Select(x => x.Item1).Distinct().Count() != list.Count) throw Invalid($"{label}: one amount per effective date.", field);
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        static string Past(IEnumerable<(DateOnly From, long Amount)> l, DateOnly bd) =>
            string.Join("|", l.Where(x => x.From < bd).OrderBy(x => x.From).Select(x => x.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" + x.Amount.ToString(CultureInfo.InvariantCulture)));
        if (Past(before, businessDate) != Past(list, businessDate))
            throw Invalid($"{label}: amounts already in effect cannot change — add a new amount from {businessDate:yyyy-MM-dd} or later.", field);
        return list;
    }

    /// <summary>Amount in force on a date (latest effective-from ≤ date), or null before the first.</summary>
    public static long? AmountOn(IEnumerable<(DateOnly From, long Amount)> prices, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(prices);
        var list = prices.Where(p => p.From <= date).OrderBy(p => p.From).ToList();
        return list.Count == 0 ? null : list[^1].Amount;
    }

    /// <summary>"₹300 from 2025-04-01; ₹350 from 2026-11-01" — history text of dated amounts.</summary>
    public static string ShowDated(IEnumerable<(DateOnly From, long Amount)> prices) =>
        string.Join("; ", prices.OrderBy(p => p.From).Select(p => $"₹{p.Amount / 100m:0.##} from {p.From:yyyy-MM-dd}"));

    public static int Quantity(int? q)
    {
        var v = q ?? 1;
        if (v < 1 || v > QuantityMax) throw HccException.Validation($"Quantity: a whole number from 1 to {QuantityMax}.");
        return v;
    }

    /// <summary>Label of a catalogue charge: the given text, else the item name (× qty).</summary>
    public static string ChargeLabel(string? label, string name, int qty)
    {
        var l = (label ?? string.Empty).Trim();
        if (l.Length == 0) l = qty > 1 ? $"{name} × {qty.ToString(CultureInfo.InvariantCulture)}" : name;
        if (l.Length > 120) throw HccException.Validation("Keep the description to 120 characters.");
        return l;
    }

    /// <summary>Is the room type allowed by the plan's list (empty = all)?</summary>
    public static bool AllowsRoomType(string storedRoomTypes, string roomTypeCode) =>
        SplitList(storedRoomTypes) is { Count: > 0 } list ? list.Contains(roomTypeCode, StringComparer.Ordinal) : true;

    /// <summary>Discount caps on a folio: (max, approval threshold) in paise from the folio's net charges.</summary>
    public static (long Max, long? ApprovalAbove) ReasonCaps(long netPaise, int maxBps, int? approvalAboveBps)
    {
        var net = Math.Max(0, netPaise);
        return (net * maxBps / 10000, approvalAboveBps is { } a ? net * a / 10000 : null);
    }

    /// <summary>Plan line label (prototype catPlanNightLine).</summary>
    public static string PlanLineLabel(string code, int guests, long eachPaise, DateOnly date) =>
        $"Meal plan {code} · {guests.ToString(CultureInfo.InvariantCulture)} × ₹{(eachPaise / 100m).ToString("#,##0.##", CultureInfo.GetCultureInfo("en-IN"))} · {date:yyyy-MM-dd}";
}
