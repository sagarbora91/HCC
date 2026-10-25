using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Domain.Stock;

/// <summary>
/// Linen, laundry and stock rules ported from the prototype (<c>03 Prototype/src/domain/streams/inventory.ts</c>, handlers/inventory.ts).
/// Pure functions: no I/O, no clock. Percentages and averages use integer arithmetic with the prototype's rounding
/// (JavaScript Math.round = half up; money = half away from zero).
/// </summary>
public static class StockRules
{
    public const int MaxQty = 10_000;
    public const int VarianceFlagPercent = 15;
    public const long MaxUnitCostPaise = 10_000_000;

    public static readonly IReadOnlyList<string> AllLocations = ["store", "floor1", "floor2", "floor3", "rooms", "soiled", "laundry"];
    public static readonly IReadOnlyList<string> Floors = ["floor1", "floor2", "floor3"];

    /// <summary>Shelf locations that can be physically counted and issued between.</summary>
    public static readonly IReadOnlyList<string> Countable = ["store", "floor1", "floor2", "floor3"];

    /// <summary>Locations a loss can be written off at (not the laundry: losses there arise on return).</summary>
    public static readonly IReadOnlyList<string> LossLocations = ["store", "floor1", "floor2", "floor3", "rooms", "soiled"];

    public static readonly IReadOnlyList<string> Kinds = ["linen", "amenity", "cleaning"];

    public static readonly IReadOnlyList<string> LossReasons = ["Damaged", "Stained beyond use", "Missing", "Taken by guest", "Expired", "Other"];

    public const string LossDamagedAtLaundry = "Damaged at laundry";
    public const string LossMissingFromLaundry = "Missing from laundry batch (vendor claim)";
    public const string LossCountShortage = "Count shortage";

    public static void RequireQty(int qty, string what = "Quantity")
    {
        if (qty < 1 || qty > MaxQty) throw HccException.Validation($"{what} must be a whole number from 1 to {MaxQty:N0}.");
    }

    public static string RequireLocation(string? loc, IReadOnlyList<string> allowed)
    {
        if (loc is null || !allowed.Contains(loc)) throw HccException.Validation("Choose a valid location.");
        return loc;
    }

    /// <summary>Clean usable stock: main store + floor pantries.</summary>
    public static int Clean(IReadOnlyDictionary<string, int> stock) => Qty(stock, "store") + Floors.Sum(f => Qty(stock, f));

    /// <summary>Every piece the hotel owns (all locations).</summary>
    public static int Total(IReadOnlyDictionary<string, int> stock) => AllLocations.Sum(l => Qty(stock, l));

    public static int Qty(IReadOnlyDictionary<string, int> stock, string loc) => stock.TryGetValue(loc, out var q) ? q : 0;

    /// <summary>Weighted-average unit cost after receiving <paramref name="addQty"/> at <paramref name="addCost"/> (half away from zero).</summary>
    public static long WeightedAverageCost(long oldCost, int totalQty, long addCost, int addQty)
    {
        if (addQty < 1) throw HccException.Validation("Quantity must be at least 1.");
        return MoneyMath.DivideRoundHalfAwayFromZero(checked(oldCost * totalQty + addCost * addQty), totalQty + addQty);
    }

    /// <summary>Pieces needed to dress every room once: Σ rooms of type × par for the type.</summary>
    public static int ParPerSet(IReadOnlyDictionary<string, int> par, IReadOnlyDictionary<string, int> roomsByType) =>
        roomsByType.Sum(kv => kv.Value * (par.TryGetValue(kv.Key, out var p) ? p : 0));

    /// <summary>Linen required in circulation = pieces per set × par sets (rooms, laundry, shelf).</summary>
    public static int ParRequired(IReadOnlyDictionary<string, int> par, IReadOnlyDictionary<string, int> roomsByType, int parSets) => ParPerSet(par, roomsByType) * parSets;

    /// <summary>Total owned ÷ required, percent rounded half up; 100 when nothing is required.</summary>
    public static int ParCoveragePercent(int total, int required) => required <= 0 ? 100 : (int)((total * 200L + required) / (2L * required));

    public static bool BelowMinimum(int clean, int min) => clean < min;

    /// <summary>Suggested reorder quantity: max(reorder, 2 × min − clean).</summary>
    public static int SuggestedReorder(int clean, int min, int reorderQty) => Math.Max(reorderQty, 2 * min - clean);

    /// <summary>Pieces not returned: sent − received − damaged (0 until a return is recorded).</summary>
    public static int Missing(int sent, int? received, int? damaged) => received is null ? 0 : Math.Max(0, sent - received.Value - (damaged ?? 0));

    /// <summary>Laundry vendor charge for a returned line: (received + damaged) × rate.</summary>
    public static long LaundryCharge(int received, int damaged, long ratePaise) => checked((received + (long)damaged) * ratePaise);

    /// <summary>Occupied room-nights: none after the business date; live stays when any, else the imported history.</summary>
    public static int Occupied(DateOnly date, DateOnly businessDate, int live, int history) => date > businessDate ? 0 : live > 0 ? live : history;

    /// <summary>Variance of use per occupied room-night against standard, whole percent (half up); null without a standard or nights.</summary>
    public static int? VariancePercent(int used, int roomNights, int stdX100)
    {
        if (stdX100 <= 0 || roomNights <= 0) return null;
        // (used/rn − std/100) / (std/100) × 100  =  (100·used − std·rn) × 100 / (std·rn)
        var num = (100L * used - (long)stdX100 * roomNights) * 100;
        var den = (long)stdX100 * roomNights;
        return (int)FloorDiv(2 * num + den, 2 * den);
    }

    public static bool FlagVariance(int? variance) => variance is { } v && Math.Abs(v) > VarianceFlagPercent;

    private static long FloorDiv(long a, long b)
    {
        var q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }
}
