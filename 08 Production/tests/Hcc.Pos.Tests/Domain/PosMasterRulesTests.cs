using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Domain.Errors;
using Hcc.Domain.Pos;
using Hcc.Domain.Tax;

namespace Hcc.Pos.Tests.Domain;

/// <summary>MA-4 POS masters rules (ported from 03 Prototype/src/domain/streams/pos-master.ts).</summary>
public class PosMasterRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    [Theory]
    [InlineData("Beverages", "BEVERAGES")]
    [InlineData("Breads & rice", "BREADS-RICE")]
    [InlineData("  Chef's  specials! ", "CHEF-S-SPECIALS")]
    [InlineData("REST-T1", "REST-T1")]
    [InlineData("€€", "X")]
    [InlineData("A very long category name indeed", "A-VERY-LONG-CATEGORY")]
    public void Codes_from_names_follow_the_prototype_seed(string name, string code) => Assert.Equal(code, PosMasterRules.CodeOf(name));

    [Fact]
    public void Kot_groups_without_stations_are_one_ticket_without_a_station_as_before()
    {
        var g = PosMasterRules.KotGroups([(1, null), (2, null)], null);
        var only = Assert.Single(g);
        Assert.Null(only.Station);
        Assert.Equal([1L, 2L], only.LineIds);
    }

    [Fact]
    public void Kot_groups_split_by_item_station_then_outlet_station_in_order_of_first_appearance()
    {
        var g = PosMasterRules.KotGroups([(1, null), (2, "Tandoor"), (3, "Bar"), (4, "Tandoor")], null);
        Assert.Equal(["Kitchen", "Tandoor", "Bar"], g.Select(x => x.Station));
        Assert.Equal([2L, 4L], g[1].LineIds);
        var outletOnly = Assert.Single(PosMasterRules.KotGroups([(1, null)], "Main kitchen"));
        Assert.Equal("Main kitchen", outletOnly.Station);
        Assert.Equal("RE-KOT 5 · Tandoor", PosMasterRules.KotLabel("Restaurant", 5, "Tandoor"));
        Assert.Equal("RE-KOT 5", PosMasterRules.KotLabel("Restaurant", 5, null));
    }

    [Fact]
    public void Sold_out_today_lapses_after_its_business_date_a_legacy_86_does_not()
    {
        var d = new DateOnly(2026, 10, 24);
        Assert.True(PosMasterRules.SoldOut(false, d, d));
        Assert.False(PosMasterRules.SoldOut(false, d, d.AddDays(1)));
        Assert.True(PosMasterRules.SoldOut(false, null, d.AddDays(9)));
        Assert.False(PosMasterRules.SoldOut(true, null, d));
    }

    [Fact]
    public void Dated_prices_amount_on_a_date_and_amounts_in_effect_are_immutable()
    {
        var d = new DateOnly(2026, 10, 24);
        (DateOnly, long)[] before = [(PosMasterRules.PriceSince, 6000)];
        Assert.Equal(6000, PosMasterRules.PriceOn(before, d));
        Assert.Null(PosMasterRules.PriceOn([(d.AddDays(1), 7000)], d));
        var next = PosMasterRules.Prices([(d.AddDays(1), 7000), (PosMasterRules.PriceSince, 6000)], before, d);
        Assert.Equal([(PosMasterRules.PriceSince, 6000L), (d.AddDays(1), 7000L)], next);
        Assert.Equal(7000, PosMasterRules.PriceOn(next, d.AddDays(1)));
        Assert.Equal(MasterReasons.Invalid, Reason(Assert.Throws<HccException>(() => PosMasterRules.Prices([(PosMasterRules.PriceSince, 6500)], before, d))));
        Assert.Throws<HccException>(() => PosMasterRules.Prices([(d, 1), (d, 2)], [], d));
        Assert.Throws<HccException>(() => PosMasterRules.Prices([(d, -1)], [], d));
        Assert.Equal("₹60 from 2017-07-01; ₹70 from 2026-10-25", PosMasterRules.PricesText(next));
    }

    [Fact]
    public void Field_rules_raise_master_invalid_with_the_field()
    {
        var t = Assert.Throws<HccException>(() => PosMasterRules.Time("7am", "opens"));
        Assert.Equal((HccErrorCode.VALIDATION, MasterReasons.Invalid, "opens"), (t.Code, Reason(t), t.Details!["field"].GetString()));
        Assert.Equal("07:00", PosMasterRules.Time(" 07:00 ", "opens"));
        Assert.Null(PosMasterRules.Time("", "closes"));
        Assert.Throws<HccException>(() => PosMasterRules.Time("24:00", "closes"));
        Assert.Throws<HccException>(() => PosMasterRules.TableNo("T/1"));
        Assert.Equal("C 1", PosMasterRules.TableNo(" C 1 "));
        Assert.Throws<HccException>(() => PosMasterRules.ShortCode("TOO-LONG-CODE"));
        Assert.Throws<HccException>(() => PosMasterRules.Type("cafe"));
        Assert.Throws<HccException>(() => PosMasterRules.Profile("12pct"));
        Assert.Throws<HccException>(() => PosMasterRules.Seats(0));
        Assert.Equal(("hotel", "food"), (PosMasterRules.Profile(null), PosMasterRules.GstClass(null)));
        Assert.Equal(("room", "table", "table"), (PosMasterRules.KindOf("room-service"), PosMasterRules.KindOf("bar"), PosMasterRules.KindOf("banquet")));
        Assert.Equal((GstCategory.Food, GstCategory.Banquet, GstCategory.Other), (PosMasterRules.CategoryOf("food"), PosMasterRules.CategoryOf("banquet"), PosMasterRules.CategoryOf("other")));
    }
}
