using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Pricing;

namespace Hcc.Platform.Tests.Kernel;

/// <summary>Money kernel + the H02 port. H02 cases are the lab's hcc.test.ts (07 Integration Lab/qualifications/H02-los-tier-pricing) re-run in C#.</summary>
public class MoneyAndPricingTests
{
    private static readonly LosTier[] Tiers = [new(7, -500), new(14, -1000), new(21, -1500)];
    private static readonly OccupancyBand[] Bands = [new(0, 50, -1000), new(50, 80, 0), new(80, 100, 1500)];

    private static RateRuleErrorCode? CodeOf(Action a)
    {
        try
        {
            a();
            return null;
        }
        catch (RateRuleException e)
        {
            return e.Code;
        }
    }

    [Fact]
    public void Paise_formats_like_the_prototype_money_helper()
    {
        // 03 Prototype/tests/stream-compat.mjs: ['₹10,750','₹123.45','-₹123.45','₹0']
        Assert.Equal("₹10,750", new Paise(1075000).ToString());
        Assert.Equal("₹123.45", new Paise(12345).ToString());
        Assert.Equal("-₹123.45", new Paise(-12345).ToString());
        Assert.Equal("₹0", Paise.Zero.ToString());
        Assert.Equal("₹1,00,00,000", Paise.FromRupees(10_000_000).ToString());
    }

    [Fact]
    public void Receipt_amount_and_wire_range_checks()
    {
        Assert.Equal(10_000_000_000L, new Paise(10_000_000_000L).EnsureReceiptAmount().Value);
        Assert.Throws<HccException>(() => Paise.Zero.EnsureReceiptAmount());
        Assert.Throws<HccException>(() => new Paise(-500).EnsureReceiptAmount());
        Assert.Throws<HccException>(() => new Paise(10_000_000_001L).EnsureReceiptAmount());
        Assert.Throws<HccException>(() => Paise.FromWire(ApiConventions.JsonSafeIntegerMax + 1));
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => Paise.FromWire(-10_000_000_001L)).Code);
        Assert.Throws<OverflowException>(() => new Paise(long.MaxValue) + new Paise(1));
    }

    [Fact]
    public void Tax_rounds_half_away_from_zero_symmetrically()
    {
        Assert.Equal(40000, MoneyMath.TaxOf(new Paise(400000), 1000).Value);
        Assert.Equal(1, MoneyMath.TaxOf(new Paise(5), 1000).Value);    // 0.5 → 1
        Assert.Equal(-1, MoneyMath.TaxOf(new Paise(-5), 1000).Value);  // −0.5 → −1 (prototype Math.round would give 0; see owner questions)
        Assert.Equal(0, MoneyMath.TaxOf(new Paise(4), 1000).Value);
        Assert.Throws<HccException>(() => MoneyMath.TaxOf(new Paise(100), 10001));
    }

    [Fact]
    public void H02_upstream_highest_qualifying_LOS_tier()
    {
        Assert.Null(RateAdjustments.SelectLosTier(Tiers, 6));
        Assert.Equal(-500, RateAdjustments.SelectLosTier(Tiers, 7)!.AdjBps);
        Assert.Equal(-500, RateAdjustments.SelectLosTier(Tiers, 13)!.AdjBps);
        Assert.Equal(-1000, RateAdjustments.SelectLosTier(Tiers, 14)!.AdjBps);
        Assert.Equal(-1500, RateAdjustments.SelectLosTier(Tiers, 21)!.AdjBps);
        Assert.Equal(-1500, RateAdjustments.SelectLosTier(Tiers, 30)!.AdjBps);
    }

    [Fact]
    public void H02_rejects_duplicate_zero_tiers_and_invalid_nights()
    {
        Assert.Equal(RateRuleErrorCode.InvalidRule, CodeOf(() => RateAdjustments.SelectLosTier([new(7, -500), new(7, -900)], 8)));
        Assert.Equal(RateRuleErrorCode.InvalidRule, CodeOf(() => RateAdjustments.SelectLosTier([new(0, -500)], 8)));
        Assert.Equal(RateRuleErrorCode.InvalidRule, CodeOf(() => RateAdjustments.SelectLosTier([new(7, -10001)], 8)));
        Assert.Equal(RateRuleErrorCode.InvalidInput, CodeOf(() => RateAdjustments.SelectLosTier(Tiers, 0)));
    }

    [Fact]
    public void H02_half_open_occupancy_bands()
    {
        Assert.Equal(-1000, RateAdjustments.SelectOccupancyBand(Bands, 40)!.AdjBps);
        Assert.Equal(0, RateAdjustments.SelectOccupancyBand(Bands, 65)!.AdjBps);
        Assert.Equal(1500, RateAdjustments.SelectOccupancyBand(Bands, 90)!.AdjBps);
        Assert.Equal(RateRuleErrorCode.InvalidInput, CodeOf(() => RateAdjustments.SelectOccupancyBand(Bands, 101)));
        Assert.Equal(0, RateAdjustments.SelectOccupancyBand(Bands, 50)!.AdjBps);
        Assert.Equal(-1000, RateAdjustments.SelectOccupancyBand(Bands, 0)!.AdjBps);
        Assert.Equal(1500, RateAdjustments.SelectOccupancyBand(Bands, 100)!.AdjBps);
        Assert.Equal(RateRuleErrorCode.InvalidRule, CodeOf(() => RateAdjustments.ValidateOccupancyBands([new(0, 51, 0), new(50, 80, 0)])));
        Assert.Equal(RateRuleErrorCode.InvalidRule, CodeOf(() => RateAdjustments.ValidateOccupancyBands([new(60, 60, 0)])));
        Assert.Equal(RateRuleErrorCode.InvalidRule, CodeOf(() => RateAdjustments.ValidateOccupancyBands([new(-1, 20, 0)])));
        Assert.Null(RateAdjustments.SelectOccupancyBand([new(0, 40, -500), new(60, 100, 500)], 50));
    }

    [Fact]
    public void H02_integer_paise_arithmetic()
    {
        Assert.Equal(18000, RateAdjustments.ApplyBps(20000, -1000));
        Assert.Equal(22400, RateAdjustments.ApplyBps(20000, 1200));
        Assert.Equal(2, RateAdjustments.ApplyBps(1, 5000));
        Assert.Equal(-1, RateAdjustments.ApplyBps(1, -15000));
        Assert.Equal(3500, RateAdjustments.ApplyBps(3333, 500));
        Assert.Equal(3500, RateAdjustments.PriceNight(3333, new LosTier(7, -1000), new OccupancyBand(80, 100, 1500)));
        Assert.Equal(3450, RateAdjustments.ApplyBps(RateAdjustments.ApplyBps(3333, -1000), 1500));
        Assert.Equal(RateRuleErrorCode.NegativeRate, CodeOf(() => RateAdjustments.PriceNight(250000, fixedPaise: [-300000])));
        Assert.Equal(0, RateAdjustments.PriceNight(250000, fixedPaise: [-250000]));
        Assert.Equal(RateRuleErrorCode.InvalidInput, CodeOf(() => RateAdjustments.ApplyBps(-1, 0)));
        Assert.Equal(RateRuleErrorCode.InvalidInput, CodeOf(() => RateAdjustments.ApplyBps(long.MaxValue / 2, 100)));
    }
}
