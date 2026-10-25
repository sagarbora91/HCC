using System.Reflection;
using Hcc.Contracts.Stock;
using Hcc.Domain.Errors;
using Hcc.Domain.Stock;

namespace Hcc.Stock.Tests.Kernel;

/// <summary>Pure rules of linen &amp; stock with the prototype's figures (no database).</summary>
public class StockRulesTests
{
    private static readonly Dictionary<string, int> Rooms = new() { ["Classic"] = 12, ["Deluxe"] = 9, ["Suite"] = 3 };

    private static Dictionary<string, int> Stock(params int[] q) => StockRules.AllLocations.Select((l, i) => (l, i)).ToDictionary(x => x.l, x => q[x.i]);

    [Fact]
    public void Bath_towel_alert_suggests_65_like_the_prototype()
    {
        var bt = Stock(30, 5, 4, 6, 54, 14, 30);
        Assert.Equal(45, StockRules.Clean(bt));
        Assert.Equal(143, StockRules.Total(bt));
        Assert.True(StockRules.BelowMinimum(45, 55));
        Assert.Equal(65, StockRules.SuggestedReorder(45, 55, 60)); // max(60, 2×55−45)
        Assert.Equal(60, StockRules.SuggestedReorder(54, 55, 60));
        Assert.False(StockRules.BelowMinimum(55, 55));
    }

    [Fact]
    public void Par_required_is_rooms_times_par_times_sets()
    {
        var bs = new Dictionary<string, int> { ["Classic"] = 2, ["Deluxe"] = 2, ["Suite"] = 2 };
        Assert.Equal(48, StockRules.ParPerSet(bs, Rooms));
        Assert.Equal(144, StockRules.ParRequired(bs, Rooms, 3)); // 24 rooms × 2 × 3 sets
        var three = new Dictionary<string, int> { ["Classic"] = 3, ["Deluxe"] = 3, ["Suite"] = 3 };
        Assert.Equal(216, StockRules.ParRequired(three, Rooms, 3));
        Assert.Equal(100, StockRules.ParCoveragePercent(0, 0)); // nothing required
        Assert.Equal(128, StockRules.ParCoveragePercent(184, 144)); // 127.78 → 128
        Assert.Equal(51, StockRules.ParCoveragePercent(73, 144)); // 50.69 → 51
    }

    [Fact]
    public void Weighted_average_cost_rounds_half_away_from_zero()
    {
        Assert.Equal(7000, StockRules.WeightedAverageCost(6000, 6, 8000, 6)); // garbage bags: 6 rolls at ₹60 + 6 at ₹80
        Assert.Equal(1500, StockRules.WeightedAverageCost(0, 0, 1500, 10));
        Assert.Equal(1001, StockRules.WeightedAverageCost(1000, 1, 1001, 2));
        Assert.Equal(1000, StockRules.WeightedAverageCost(1000, 1, 1000, 1));
        Assert.Equal(3, StockRules.WeightedAverageCost(2, 1, 3, 1)); // 2.5 → 3
        Assert.Throws<HccException>(() => StockRules.WeightedAverageCost(1, 1, 1, 0));
    }

    [Fact]
    public void Laundry_missing_and_charge()
    {
        Assert.Equal(1, StockRules.Missing(10, 8, 1));
        Assert.Equal(0, StockRules.Missing(10, null, null));
        Assert.Equal(0, StockRules.Missing(10, 10, 0));
        Assert.Equal(9 * 1800 + 14 * 1400, StockRules.LaundryCharge(8, 1, 1800) + StockRules.LaundryCharge(14, 0, 1400));
    }

    [Fact]
    public void Occupancy_is_live_then_history_and_zero_after_the_business_date()
    {
        var bd = new DateOnly(2026, 10, 23);
        Assert.Equal(0, StockRules.Occupied(bd.AddDays(1), bd, 5, 5));
        Assert.Equal(8, StockRules.Occupied(bd, bd, 8, 3));
        Assert.Equal(15, StockRules.Occupied(bd.AddDays(-4), bd, 0, 15));
    }

    [Theory]
    [InlineData(38, 79, 200, -76)] // 0.48 per night vs 2.0
    [InlineData(158, 79, 200, 0)] // exactly standard
    [InlineData(198, 79, 200, 25)] // 25% over
    [InlineData(1, 1, 100, 0)]
    [InlineData(3, 2, 100, 50)]
    public void Variance_percent(int used, int nights, int std, int expected) => Assert.Equal(expected, StockRules.VariancePercent(used, nights, std));

    [Fact]
    public void Variance_is_absent_without_a_standard_or_nights_and_flags_beyond_15_percent()
    {
        Assert.Null(StockRules.VariancePercent(5, 0, 100));
        Assert.Null(StockRules.VariancePercent(5, 10, 0));
        Assert.True(StockRules.FlagVariance(16));
        Assert.True(StockRules.FlagVariance(-16));
        Assert.False(StockRules.FlagVariance(15));
        Assert.False(StockRules.FlagVariance(null));
    }

    [Fact]
    public void Validation_helpers()
    {
        StockRules.RequireQty(1);
        StockRules.RequireQty(StockRules.MaxQty);
        Assert.Throws<HccException>(() => StockRules.RequireQty(0));
        Assert.Throws<HccException>(() => StockRules.RequireQty(StockRules.MaxQty + 1));
        Assert.Equal("floor1", StockRules.RequireLocation("floor1", StockRules.Countable));
        Assert.Throws<HccException>(() => StockRules.RequireLocation("rooms", StockRules.Countable));
        Assert.Throws<HccException>(() => StockRules.RequireLocation("laundry", StockRules.LossLocations));
    }

    [Fact]
    public void Response_types_carry_money_only_as_nullable_fields()
    {
        // Housekeepers and supervisors receive the same view types with every money field null (DEC-063); a non-nullable money field would leak.
        var viewTypes = typeof(StockItemView).Assembly.GetTypes()
            .Where(t => t.Namespace == "Hcc.Contracts.Stock" && t.IsClass && !t.Name.EndsWith("Request", StringComparison.Ordinal) && t.Name != nameof(StockLocations) && t.Name != nameof(StockKinds))
            .ToList();
        Assert.NotEmpty(viewTypes);
        foreach (var p in viewTypes.SelectMany(t => t.GetProperties()).Where(p => p.Name.EndsWith("Paise", StringComparison.Ordinal)))
            Assert.True(Nullable.GetUnderlyingType(p.PropertyType) is not null, $"{p.DeclaringType!.Name}.{p.Name} must be nullable");
    }
}
