using System.Globalization;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Tax;

// DEC-069 · GST master (replaces the DEC-039 synthetic 10 %). Ported from 03 Prototype/src/domain/gst.ts (pure rules, no I/O).
// Rates seeded from public sources on 25 Oct 2026 and ILLUSTRATIVE — CA to confirm for this hotel:
//   · Room accommodation, per unit per night, on the actual price charged for that night (never an average or declared tariff):
//       ≤ ₹7,500 → 5 % without ITC from 22 Sep 2025 (before that 12 % with ITC); > ₹7,500 → 18 % with ITC.
//   · Restaurant / room service supplied by the hotel: 5 % without ITC, unless the hotel is "specified premises" for the financial
//       year (any accommodation unit above ₹7,500 per night in the previous FY, or the Owner opted in by declaration) → 18 % with ITC.
//   · Other hotel services (laundry, banquet / hall, other): one dated rate each (seed 18 %).
// Intra-state only: each line's tax is split CGST + SGST (IGST out of scope). Rounding: tax per line in paise, half away from zero;
// SGST = half rounded toward zero, CGST = the rest (takes the odd paisa) so CGST + SGST = line tax exactly.

/// <summary>Supply category of a line (prototype <c>GstCategory</c>).</summary>
public enum GstCategory
{
    Room,
    Food,
    Laundry,
    Banquet,
    Other,
}

/// <summary>Rate for one supply on one date: basis points, input-tax-credit flag, category and the rule that produced it.</summary>
public sealed record GstRate(int Bps, bool Itc, GstCategory Category, string Rule);

/// <summary>Value kind of a master setting (prototype <c>type</c>): percent = basis points, money = paise, boolean = 0/1.</summary>
public enum GstValueKind
{
    Percent,
    Money,
    Boolean,
}

/// <summary>One dated version of one setting. <see cref="Sequence"/> orders versions with the same effective date (later wins).</summary>
public sealed record GstSettingVersion(string Key, DateOnly EffectiveFrom, long Value, long Sequence = 0);

/// <summary>Catalogue entry of a master setting with its statutory seed versions (oldest first; the first applies to every earlier date).</summary>
public sealed record GstSettingDef(string Key, string Label, GstValueKind Kind, string Unit, string Help, IReadOnlyList<GstSettingVersion> Seed);

public static class GstKeys
{
    public const string RoomThreshold = "gst.room.threshold";
    public const string RoomLowBps = "gst.room.lowBps";
    public const string RoomLowItc = "gst.room.lowItc";
    public const string RoomHighBps = "gst.room.highBps";
    public const string FoodBps = "gst.food.bps";
    public const string FoodSpecifiedBps = "gst.food.specifiedBps";
    public const string FoodOptIn = "gst.food.optIn";
    public const string LaundryBps = "gst.svc.laundryBps";
    public const string BanquetBps = "gst.svc.banquetBps";
    public const string OtherBps = "gst.svc.otherBps";
}

/// <summary>The seed catalogue (prototype <c>GST_SEED</c>), the same rows migration 0600 writes as statutory versions.</summary>
public static class GstSeed
{
    public const string Illustrative = "Illustrative — CA to confirm";

    /// <summary>Highest percentage a rate may take (prototype: 2900 bps is rejected; 28 % is the highest GST slab).</summary>
    public const int MaxBps = 2800;

    private static readonly DateOnly Gst = new(2017, 7, 1);

    private static GstSettingVersion[] V(string key, params (DateOnly From, long Value)[] versions)
        => versions.Select(v => new GstSettingVersion(key, v.From, v.Value)).ToArray();

    public static readonly IReadOnlyList<GstSettingDef> Settings =
    [
        new(GstKeys.RoomThreshold, "GST · room slab threshold (per unit per night)", GstValueKind.Money, "₹ per room per night",
            "Room nights charged at or below this take the lower slab; above it the higher slab. Tested per room per night on the actual price charged. " + Illustrative + ".",
            V(GstKeys.RoomThreshold, (Gst, 750_000))),
        new(GstKeys.RoomLowBps, "GST · room night at or below the threshold", GstValueKind.Percent, "% of the night’s charge",
            "5 % without input tax credit from 22 Sep 2025; 12 % with ITC before that. " + Illustrative + ".",
            V(GstKeys.RoomLowBps, (Gst, 1200), (new DateOnly(2025, 9, 22), 500))),
        new(GstKeys.RoomLowItc, "GST · input tax credit on the lower room slab", GstValueKind.Boolean, "available / not available",
            "Not available from 22 Sep 2025 (5 % slab); available before (12 % slab). " + Illustrative + ".",
            V(GstKeys.RoomLowItc, (Gst, 1), (new DateOnly(2025, 9, 22), 0))),
        new(GstKeys.RoomHighBps, "GST · room night above the threshold", GstValueKind.Percent, "% of the night’s charge",
            "18 % with input tax credit. " + Illustrative + ".", V(GstKeys.RoomHighBps, (Gst, 1800))),
        new(GstKeys.FoodBps, "GST · restaurant and room service (standard)", GstValueKind.Percent, "% of the bill",
            "5 % without input tax credit when the hotel is not “specified premises” for the financial year. " + Illustrative + ".", V(GstKeys.FoodBps, (Gst, 500))),
        new(GstKeys.FoodSpecifiedBps, "GST · restaurant and room service (specified premises)", GstValueKind.Percent, "% of the bill",
            "18 % with input tax credit when the hotel is “specified premises” for the financial year (from 1 Apr 2025). " + Illustrative + ".",
            V(GstKeys.FoodSpecifiedBps, (Gst, 1800))),
        new(GstKeys.FoodOptIn, "GST · declared specified premises (opt-in)", GstValueKind.Boolean, "declared / not declared",
            "Owner declaration that the hotel is specified premises. Without it the status is derived from last financial year’s tariffs (any unit above the room threshold). " + Illustrative + ".",
            V(GstKeys.FoodOptIn, (Gst, 0))),
        new(GstKeys.LaundryBps, "GST · laundry and dry cleaning", GstValueKind.Percent, "% of the charge", "Seed 18 %. " + Illustrative + ".", V(GstKeys.LaundryBps, (Gst, 1800))),
        new(GstKeys.BanquetBps, "GST · banquet, hall and conference", GstValueKind.Percent, "% of the charge", "Seed 18 %. " + Illustrative + ".", V(GstKeys.BanquetBps, (Gst, 1800))),
        new(GstKeys.OtherBps, "GST · other hotel services", GstValueKind.Percent, "% of the charge",
            "Seed 18 %. Used for any charge that is not room, food, laundry or banquet. " + Illustrative + ".", V(GstKeys.OtherBps, (Gst, 1800))),
    ];

    public static GstSettingDef Def(string key) => Settings.FirstOrDefault(d => d.Key == key)
        ?? throw HccException.Validation("Unknown GST setting. Choose one of the GST master settings.");

    public static bool IsKey(string? key) => key is not null && Settings.Any(d => d.Key == key);

    /// <summary>Every seed version (what migration 0600 inserts as statutory rows).</summary>
    public static IEnumerable<GstSettingVersion> AllVersions => Settings.SelectMany(d => d.Seed);

    /// <summary>VALIDATION unless <paramref name="value"/> fits the setting's kind (prototype <c>cfgValidate</c>).</summary>
    public static void ValidateValue(string key, long value)
    {
        var def = Def(key);
        switch (def.Kind)
        {
            case GstValueKind.Percent when value is < 0 or > MaxBps:
                throw HccException.Validation($"{def.Label}: enter a rate from 0 % to {MaxBps / 100} % (in basis points, e.g. 500 = 5 %).");
            case GstValueKind.Money when value is <= 0 or > 10_000_000_000:
                throw HccException.Validation($"{def.Label}: enter an amount between ₹0.01 and ₹10 crore.");
            case GstValueKind.Boolean when value is not (0 or 1):
                throw HccException.Validation($"{def.Label}: choose yes (1) or no (0).");
        }
    }
}

/// <summary>Indian financial year (1 April – 31 March) containing a date (prototype <c>gstFy</c>).</summary>
public sealed record FinancialYear(string Label, DateOnly From, DateOnly To)
{
    public static FinancialYear Of(DateOnly date)
    {
        var start = date.Month >= 4 ? date.Year : date.Year - 1;
        return new FinancialYear(string.Create(CultureInfo.InvariantCulture, $"{start}-{(start + 1) % 100:00}"), new DateOnly(start, 4, 1), new DateOnly(start + 1, 3, 31));
    }

    public FinancialYear Previous => Of(From.AddDays(-1));
}

/// <summary>Specified-premises status for the financial year containing a date (prototype <c>gstSpecifiedPremises</c>).</summary>
public sealed record GstSpecifiedPremises(string Fy, string PreviousFy, bool Specified, bool Derived, bool OptIn, long MaxPreviousFyTariffPaise, long ThresholdPaise, string Reason);

/// <summary>
/// The GST master in force: dated versions per setting (seed + Owner changes, withdrawn ones already removed by the caller).
/// <see cref="ValueOn"/> = the latest version with effective-from ≤ date (same date: higher sequence wins); before the first version,
/// the first version applies (prototype <c>gstSeedOn</c>).
/// </summary>
public sealed class GstMaster
{
    private readonly Dictionary<string, List<GstSettingVersion>> _versions;

    public GstMaster(IEnumerable<GstSettingVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        _versions = versions.GroupBy(v => v.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.EffectiveFrom).ThenBy(v => v.Sequence).ToList(), StringComparer.Ordinal);
        foreach (var def in GstSeed.Settings)
            if (!_versions.ContainsKey(def.Key)) throw new InvalidOperationException($"GST master has no version of {def.Key}.");
    }

    /// <summary>The statutory seed only (unit tests, and the fallback the migration writes).</summary>
    public static GstMaster Seed() => new(GstSeed.AllVersions);

    public long ValueOn(string key, DateOnly date)
    {
        if (!_versions.TryGetValue(key, out var list)) throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown GST setting.");
        var v = list[0].Value;
        foreach (var x in list)
        {
            if (x.EffectiveFrom > date) break;
            v = x.Value;
        }

        return v;
    }

    private int Bps(string key, DateOnly date) => checked((int)ValueOn(key, date));

    /// <summary>Room accommodation for one unit and one night, on the actual price charged for that night.</summary>
    public GstRate RoomNight(DateOnly night, Paise chargedPerUnit)
    {
        var threshold = ValueOn(GstKeys.RoomThreshold, night);
        return chargedPerUnit.Value <= threshold
            ? new GstRate(Bps(GstKeys.RoomLowBps, night), ValueOn(GstKeys.RoomLowItc, night) == 1, GstCategory.Room, "Room night at or below the threshold")
            : new GstRate(Bps(GstKeys.RoomHighBps, night), true, GstCategory.Room, "Room night above the threshold");
    }

    /// <summary>
    /// Specified premises for the FY containing <paramref name="date"/>: derived when any unit was above the threshold (as in force at
    /// the end of the previous FY) on any day of the previous FY, or the Owner's opt-in in force on the date.
    /// </summary>
    public GstSpecifiedPremises SpecifiedPremises(DateOnly date, long maxPreviousFyTariffPaise)
    {
        var fy = FinancialYear.Of(date);
        var prev = fy.Previous;
        var threshold = ValueOn(GstKeys.RoomThreshold, prev.To);
        var derived = maxPreviousFyTariffPaise > threshold;
        var optIn = ValueOn(GstKeys.FoodOptIn, date) == 1;
        var reason = derived ? $"a unit was above {new Paise(threshold)} per night in FY {prev.Label} (highest {new Paise(maxPreviousFyTariffPaise)})"
            : optIn ? "declared by the Owner (opt-in)"
            : $"no unit above {new Paise(threshold)} per night in FY {prev.Label} (highest {new Paise(maxPreviousFyTariffPaise)}) and no opt-in";
        return new GstSpecifiedPremises(fy.Label, prev.Label, derived || optIn, derived, optIn, maxPreviousFyTariffPaise, threshold, reason);
    }

    /// <summary>Restaurant / room service supplied by the hotel.</summary>
    public GstRate Food(DateOnly date, GstSpecifiedPremises specified)
    {
        ArgumentNullException.ThrowIfNull(specified);
        return specified.Specified
            ? new GstRate(Bps(GstKeys.FoodSpecifiedBps, date), true, GstCategory.Food, $"Restaurant · specified premises FY {specified.Fy}")
            : new GstRate(Bps(GstKeys.FoodBps, date), false, GstCategory.Food, $"Restaurant · not specified premises FY {specified.Fy}");
    }

    /// <summary>Other hotel services: one dated rate each.</summary>
    public GstRate Service(DateOnly date, GstCategory kind) => kind switch
    {
        GstCategory.Laundry => new GstRate(Bps(GstKeys.LaundryBps, date), true, GstCategory.Laundry, "Laundry"),
        GstCategory.Banquet => new GstRate(Bps(GstKeys.BanquetBps, date), true, GstCategory.Banquet, "Banquet / hall"),
        GstCategory.Other => new GstRate(Bps(GstKeys.OtherBps, date), true, GstCategory.Other, "Other hotel service"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Room and food charges use RoomNight / Food."),
    };

    /// <summary>Tax on a run of nights at one nightly price (quotes, estimates): per night, per line (prototype <c>gstStayTax</c>).</summary>
    public Paise StayTax(Paise nightly, DateOnly from, DateOnly to)
    {
        var t = Paise.Zero;
        for (var d = from; d < to; d = d.AddDays(1)) t += GstMath.LineTax(nightly, RoomNight(d, nightly).Bps);
        return t;
    }
}

/// <summary>Per-line tax and the CGST / SGST split (never on totals, so documents, vouchers and reports agree to the paisa).</summary>
public static class GstMath
{
    /// <summary>amount × bps / 10000, half away from zero (the kernel rule).</summary>
    public static Paise LineTax(Paise amount, int bps) => MoneyMath.TaxOf(amount, bps);

    /// <summary>Intra-state split: SGST = half rounded toward zero, CGST = the rest (takes the odd paisa).</summary>
    public static GstSplit Split(Paise tax)
    {
        var sgst = tax.Value / 2; // C# integer division truncates toward zero
        return new GstSplit(tax, new Paise(tax.Value - sgst), new Paise(sgst));
    }

    public static GstSplit SplitLine(Paise amount, int bps) => Split(LineTax(amount, bps));

    /// <summary>Sum of per-line splits.</summary>
    public static GstSplit SplitLines(IEnumerable<(Paise Amount, int Bps)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Paise tax = Paise.Zero, cgst = Paise.Zero, sgst = Paise.Zero;
        foreach (var (amount, bps) in lines)
        {
            var s = SplitLine(amount, bps);
            tax += s.Tax;
            cgst += s.Cgst;
            sgst += s.Sgst;
        }

        return new GstSplit(tax, cgst, sgst);
    }

    /// <summary>"5 %" / "2.5 %" labels for documents.</summary>
    public static string Percent(int bps) => (bps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %";
}

public readonly record struct GstSplit(Paise Tax, Paise Cgst, Paise Sgst);

/// <summary>Wire codes and the free-text category guess (prototype <c>gstCategoryOfLabel</c>).</summary>
public static partial class GstCategories
{
    public static string Code(GstCategory c) => c switch
    {
        GstCategory.Room => "room",
        GstCategory.Food => "food",
        GstCategory.Laundry => "laundry",
        GstCategory.Banquet => "banquet",
        GstCategory.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, null),
    };

    public static GstCategory Parse(string code) => code switch
    {
        "room" => GstCategory.Room,
        "food" => GstCategory.Food,
        "laundry" => GstCategory.Laundry,
        "banquet" => GstCategory.Banquet,
        "other" => GstCategory.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown GST category."),
    };

    /// <summary>A charge's category from the request: explicit (food / laundry / banquet / other) wins, else guessed from the label.</summary>
    public static GstCategory ForCharge(string? explicitCode, string label) => explicitCode switch
    {
        null or "" => OfLabel(label),
        "food" => GstCategory.Food,
        "laundry" => GstCategory.Laundry,
        "banquet" => GstCategory.Banquet,
        "other" => GstCategory.Other,
        _ => throw HccException.Validation("GST category must be food, laundry, banquet or other."),
    };

    [System.Text.RegularExpressions.GeneratedRegex(@"laund|dry.?clean|ironing|press(ing)?\b")]
    private static partial System.Text.RegularExpressions.Regex LaundryWords();

    [System.Text.RegularExpressions.GeneratedRegex(@"banquet|\bhall\b|conference|meeting room")]
    private static partial System.Text.RegularExpressions.Regex BanquetWords();

    [System.Text.RegularExpressions.GeneratedRegex(@"food|restaurant|room service|breakfast|lunch|dinner|meal|snack|minibar|mini-bar|\btea\b|coffee|chai|beverage|juice|water bottle")]
    private static partial System.Text.RegularExpressions.Regex FoodWords();

    public static GstCategory OfLabel(string? label)
    {
        var t = (label ?? string.Empty).ToLowerInvariant();
        if (LaundryWords().IsMatch(t)) return GstCategory.Laundry;
        if (BanquetWords().IsMatch(t)) return GstCategory.Banquet;
        if (FoodWords().IsMatch(t)) return GstCategory.Food;
        return GstCategory.Other;
    }
}

/// <summary>One ledger entry of a GST posting.</summary>
public sealed record LedgerEntry(string Account, Paise Debit, Paise Credit);

/// <summary>
/// GST ledgers and the posting of a taxed line (prototype accounting.ts default map, illustrative — CA to confirm):
/// 1100 Guest ledger dr (amount + tax) · revenue cr (amount) · 2110 Output CGST cr · 2120 Output SGST cr. Outlet bills debit the
/// tender's account. Negative lines (discounts, reversals) post the same entries with negative amounts, so every voucher balances.
/// </summary>
public static class GstPosting
{
    public const string OutputCgst = "2110";
    public const string OutputSgst = "2120";
    public const string GuestLedger = "1100";
    public const string RoomRevenue = "4000";
    public const string OtherRevenue = "4100";
    public const string DiscountsAllowed = "4900";
    public const string Adjustments = "4910";

    /// <summary>Revenue ledger of a folio line kind (room / service / discount / correction).</summary>
    public static string RevenueAccount(string lineKind) => lineKind switch
    {
        "room" => RoomRevenue,
        "service" => OtherRevenue,
        "discount" => DiscountsAllowed,
        "correction" => Adjustments,
        _ => throw new ArgumentOutOfRangeException(nameof(lineKind), lineKind, null),
    };

    public static IReadOnlyList<LedgerEntry> ForLine(string debitAccount, string revenueAccount, Paise amount, Paise tax)
    {
        var s = GstMath.Split(tax);
        return
        [
            new LedgerEntry(debitAccount, amount + tax, Paise.Zero),
            new LedgerEntry(revenueAccount, Paise.Zero, amount),
            new LedgerEntry(OutputCgst, Paise.Zero, s.Cgst),
            new LedgerEntry(OutputSgst, Paise.Zero, s.Sgst),
        ];
    }

    public static IReadOnlyList<LedgerEntry> ForFolioLine(string lineKind, Paise amount, Paise tax) => ForLine(GuestLedger, RevenueAccount(lineKind), amount, tax);

    public static bool Balanced(IEnumerable<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        return list.Aggregate(Paise.Zero, (a, e) => a + e.Debit) == list.Aggregate(Paise.Zero, (a, e) => a + e.Credit);
    }
}
