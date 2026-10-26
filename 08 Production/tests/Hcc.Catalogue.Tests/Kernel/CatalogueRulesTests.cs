using Hcc.Contracts;
using Hcc.Contracts.Catalogue;
using Hcc.Domain.Catalogue;
using Hcc.Domain.Errors;

namespace Hcc.Catalogue.Tests.Kernel;

/// <summary>Catalogue rules without a database (prototype domain/streams/catalogue.ts mirrors them).</summary>
public class CatalogueRulesTests
{
    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    [Fact]
    public void Meals_are_canonical_B_L_D_and_unknown_or_repeated_meals_are_refused()
    {
        Assert.Equal("BD", CatalogueRules.MealsCode(["d", "B"]));
        Assert.Equal("", CatalogueRules.MealsCode(null));
        Assert.Equal("BLD", CatalogueRules.MealsCode(["L", "D", "B"]));
        Assert.Equal("MASTER_INVALID", Reason(Assert.Throws<HccException>(() => CatalogueRules.MealsCode(["X"]))));
        Assert.Equal("MASTER_INVALID", Reason(Assert.Throws<HccException>(() => CatalogueRules.MealsCode(["B", "b"]))));
    }

    [Fact]
    public void Choices_follow_the_lists_and_department_is_optional()
    {
        Assert.Equal("bundled", CatalogueRules.GstTreatment("bundled"));
        Assert.Equal("laundry", CatalogueRules.GstCategory("laundry"));
        Assert.Equal("trip", CatalogueRules.Unit("trip"));
        Assert.Null(CatalogueRules.Department(""));
        Assert.Equal("transport", CatalogueRules.Department("transport"));
        foreach (var f in new Action[] { () => CatalogueRules.GstTreatment("mixed"), () => CatalogueRules.GstCategory("room"), () => CatalogueRules.Unit("box"),
                     () => CatalogueRules.Category("spa"), () => CatalogueRules.Department("spa") })
            Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(f).Code);
    }

    [Fact]
    public void Roles_and_room_types_are_normalised_lists()
    {
        Assert.Equal("OWN,MGR", CatalogueRules.Roles(["mgr", "OWN"]));
        Assert.Equal("", CatalogueRules.Roles([]));
        Assert.Throws<HccException>(() => CatalogueRules.Roles(["CSH"]));
        Assert.Equal("Deluxe,Suite", CatalogueRules.RoomTypes([" Deluxe", "Suite", "Deluxe", ""]));
        Assert.True(CatalogueRules.AllowsRoomType("", "Classic"));
        Assert.True(CatalogueRules.AllowsRoomType("Deluxe,Suite", "Suite"));
        Assert.False(CatalogueRules.AllowsRoomType("Deluxe,Suite", "Classic"));
        Assert.Throws<HccException>(() => CatalogueRules.RoomTypes(["A,B"]));
    }

    [Fact]
    public void Discount_limits_and_caps_on_a_folio()
    {
        Assert.Equal((2500, (int?)1000), CatalogueRules.DiscountLimits(2500, 1000));
        Assert.Throws<HccException>(() => CatalogueRules.DiscountLimits(0, null));
        Assert.Throws<HccException>(() => CatalogueRules.DiscountLimits(1000, 1000));
        Assert.Equal((100000L, (long?)40000), CatalogueRules.ReasonCaps(400000, 2500, 1000));
        Assert.Equal((0L, (long?)null), CatalogueRules.ReasonCaps(-5, 2500, null));
    }

    [Fact]
    public void Dated_amounts_keep_the_amounts_in_effect_and_resolve_by_date()
    {
        var bd = new DateOnly(2026, 10, 24);
        var before = new List<(DateOnly, long)> { (new DateOnly(2025, 4, 1), 50000) };
        var list = CatalogueRules.DatedAmounts([new DatedAmount("2026-11-01", 60000), new DatedAmount("2025-04-01", 50000)], before, bd, "Default price", "price");
        Assert.Equal([(new DateOnly(2025, 4, 1), 50000L), (new DateOnly(2026, 11, 1), 60000L)], list);
        Assert.Equal(50000, CatalogueRules.AmountOn(list, bd));
        Assert.Equal(60000, CatalogueRules.AmountOn(list, new DateOnly(2026, 11, 2)));
        Assert.Null(CatalogueRules.AmountOn(list, new DateOnly(2025, 1, 1)));
        Assert.Equal("MASTER_INVALID", Reason(Assert.Throws<HccException>(() => CatalogueRules.DatedAmounts([new DatedAmount("2025-04-01", 1)], before, bd, "Default price", "price"))));
        Assert.Throws<HccException>(() => CatalogueRules.DatedAmounts([new DatedAmount("2025-04-01", 50000), new DatedAmount("2025-04-01", 1)], before, bd, "P", "p"));
        Assert.Throws<HccException>(() => CatalogueRules.DatedAmounts([new DatedAmount("24-10-2026", 1)], [], bd, "P", "p"));
        Assert.Throws<HccException>(() => CatalogueRules.DatedAmounts([new DatedAmount("2026-10-24", -1)], [], bd, "P", "p"));
        Assert.Equal("₹500 from 2025-04-01; ₹600 from 2026-11-01", CatalogueRules.ShowDated(list));
    }

    [Fact]
    public void Charge_label_quantity_and_plan_line_text_match_the_prototype()
    {
        Assert.Equal("Laundry", CatalogueRules.ChargeLabel(null, "Laundry", 1));
        Assert.Equal("Airport pickup × 2", CatalogueRules.ChargeLabel(" ", "Airport pickup", 2));
        Assert.Equal("Minibar · 2 juices", CatalogueRules.ChargeLabel("Minibar · 2 juices", "Minibar", 1));
        Assert.Equal(1, CatalogueRules.Quantity(null));
        Assert.Throws<HccException>(() => CatalogueRules.Quantity(0));
        Assert.Throws<HccException>(() => CatalogueRules.Quantity(100));
        Assert.Equal("Meal plan MAP · 2 × ₹750 · 2026-10-24", CatalogueRules.PlanLineLabel("MAP", 2, 75000, new DateOnly(2026, 10, 24)));
    }
}
