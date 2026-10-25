using Hcc.Domain.Money;
using Hcc.Domain.Tax;

namespace Hcc.GstIds.Tests.Gst;

/// <summary>
/// Golden ports of 03 Prototype/tests/stream-gst.mjs ([GST] cases) over the pure GST master (no database). Seed rates are
/// illustrative (public sources, 25 Oct 2026) — CA to confirm.
/// </summary>
public class GstRulesGoldenTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly GstMaster Seed = GstMaster.Seed();

    private static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static GstMaster With(params GstSettingVersion[] changes) => new(GstSeed.AllVersions.Concat(changes));

    [Fact]
    public void Slab_boundary_7500_00_is_5_percent_without_ITC_and_7500_01_is_18_percent_with_ITC()
    {
        Assert.Equal((500, false), (Seed.RoomNight(Today, new Paise(750_000)).Bps, Seed.RoomNight(Today, new Paise(750_000)).Itc));
        Assert.Equal((1800, true), (Seed.RoomNight(Today, new Paise(750_001)).Bps, Seed.RoomNight(Today, new Paise(750_001)).Itc));
        Assert.Equal(500, Seed.RoomNight(Today, new Paise(1)).Bps);
        Assert.Equal(1800, Seed.RoomNight(Today, new Paise(10_000_000)).Bps);
        Assert.Equal(37_500, GstMath.LineTax(new Paise(750_000), 500).Value);
        Assert.Equal(135_000, GstMath.LineTax(new Paise(750_001), 1800).Value); // 1,35,000.18 paise → 1,35,000
        Assert.Equal(GstCategory.Room, Seed.RoomNight(Today, new Paise(1)).Category);
    }

    [Fact]
    public void Before_and_after_22_Sep_2025_lower_slab_12_with_ITC_then_5_without_upper_18_throughout()
    {
        Assert.Equal((1200, true), (Seed.RoomNight(D("2025-09-21"), new Paise(300_000)).Bps, Seed.RoomNight(D("2025-09-21"), new Paise(300_000)).Itc));
        Assert.Equal((500, false), (Seed.RoomNight(D("2025-09-22"), new Paise(300_000)).Bps, Seed.RoomNight(D("2025-09-22"), new Paise(300_000)).Itc));
        Assert.Equal(1800, Seed.RoomNight(D("2025-09-21"), new Paise(800_000)).Bps);
        Assert.Equal(1800, Seed.RoomNight(D("2025-09-22"), new Paise(800_000)).Bps);
        // A stay across the change is taxed per night: 20, 21 Sep at 12 %, 22, 23 Sep at 5 %.
        Assert.Equal(2 * 36_000 + 2 * 15_000, Seed.StayTax(new Paise(300_000), D("2025-09-20"), D("2025-09-24")).Value);
        // Dates before the first seed version take the first version.
        Assert.Equal(1200, Seed.RoomNight(D("2017-01-01"), new Paise(100)).Bps);
    }

    [Fact]
    public void Multi_night_stay_tests_each_night_on_its_own_price_and_never_averages()
    {
        var nights = new (DateOnly Date, long Price)[] { (D("2026-10-24"), 450_000), (D("2026-10-25"), 800_000), (D("2026-10-26"), 800_000) };
        var rates = nights.Select(n => Seed.RoomNight(n.Date, new Paise(n.Price)).Bps).ToArray();
        Assert.Equal([500, 1800, 1800], rates);
        Assert.Equal(22_500 + 144_000 + 144_000, nights.Sum(n => GstMath.LineTax(new Paise(n.Price), Seed.RoomNight(n.Date, new Paise(n.Price)).Bps).Value));
        Assert.Equal(500, Seed.RoomNight(Today, new Paise((450_000 + 800_000 * 2) / 3)).Bps); // the average (₹6,833) would be the lower slab
        Assert.All(nights, n => Assert.Equal(500, Seed.RoomNight(n.Date, new Paise(750_000)).Bps));
    }

    [Fact]
    public void Quotes_add_GST_per_night()
    {
        Assert.Equal(67_500, Seed.StayTax(new Paise(450_000), D("2026-10-25"), D("2026-10-28")).Value); // Suite 3 nights: 13,500 + 675
        Assert.Equal(2 * 144_000, Seed.StayTax(new Paise(800_000), D("2026-10-25"), D("2026-10-27")).Value);
        Assert.Equal(20_000, Seed.StayTax(new Paise(200_000), D("2026-10-24"), D("2026-10-26")).Value);  // room 105: 2 × ₹2,000 → ₹200
    }

    [Fact]
    public void Financial_year_bounds()
    {
        Assert.Equal(new FinancialYear("2025-26", D("2025-04-01"), D("2026-03-31")), FinancialYear.Of(D("2026-03-31")));
        Assert.Equal("2026-27", FinancialYear.Of(D("2026-04-01")).Label);
        Assert.Equal("2025-26", FinancialYear.Of(D("2026-10-24")).Previous.Label);
        Assert.Equal("1999-00", FinancialYear.Of(D("1999-05-01")).Label);
    }

    [Fact]
    public void Restaurant_5_percent_without_ITC_when_no_unit_above_7500_last_FY()
    {
        var sp = Seed.SpecifiedPremises(Today, 450_000);
        Assert.Equal(("2026-27", "2025-26", false, false, false, 450_000L), (sp.Fy, sp.PreviousFy, sp.Specified, sp.Derived, sp.OptIn, sp.MaxPreviousFyTariffPaise));
        var food = Seed.Food(Today, sp);
        Assert.Equal((500, false, GstCategory.Food), (food.Bps, food.Itc, food.Category));
        Assert.Contains("no unit above ₹7,500", sp.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Specified_premises_derived_from_last_FY_and_exactly_7500_is_not_above()
    {
        var sp = Seed.SpecifiedPremises(Today, 800_000);
        Assert.Equal((true, true), (sp.Specified, sp.Derived));
        Assert.Equal((1800, true), (Seed.Food(Today, sp).Bps, Seed.Food(Today, sp).Itc));
        Assert.Equal(500, Seed.Food(Today, Seed.SpecifiedPremises(Today, 750_000)).Bps);
    }

    [Fact]
    public void Specified_premises_opt_in_applies_from_its_effective_date()
    {
        var m = With(new GstSettingVersion(GstKeys.FoodOptIn, D("2026-10-25"), 1, 1));
        Assert.Equal(500, m.Food(D("2026-10-24"), m.SpecifiedPremises(D("2026-10-24"), 450_000)).Bps);
        var sp = m.SpecifiedPremises(D("2026-10-25"), 450_000);
        Assert.Equal((true, true, false), (sp.Specified, sp.OptIn, sp.Derived));
        Assert.Contains("opt-in", sp.Reason, StringComparison.Ordinal);
        Assert.Equal(1800, m.Food(D("2026-10-25"), sp).Bps);
        Assert.Equal(5_400, GstMath.LineTax(new Paise(30_000), m.Food(D("2026-10-25"), sp).Bps).Value); // paratha ₹240 + chai ₹60 at 18 %
    }

    [Fact]
    public void Other_services_18_percent_dated_and_category_from_label_explicit_wins()
    {
        foreach (var k in new[] { GstCategory.Laundry, GstCategory.Banquet, GstCategory.Other }) Assert.Equal(1800, Seed.Service(Today, k).Bps);
        Assert.Equal(
            [GstCategory.Laundry, GstCategory.Laundry, GstCategory.Banquet, GstCategory.Banquet, GstCategory.Food, GstCategory.Food, GstCategory.Food, GstCategory.Other],
            new[] { "Laundry service", "Dry cleaning", "Banquet hall", "Conference room", "Room service dinner", "Minibar", "Masala chai", "Airport taxi" }.Select(GstCategories.OfLabel));
        Assert.Equal(GstCategory.Food, GstCategories.ForCharge("food", "Hall hire"));
        Assert.Equal(GstCategory.Laundry, GstCategories.ForCharge(null, "Laundry"));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => GstCategories.ForCharge("liquor", "X"));
        var m = With(new GstSettingVersion(GstKeys.LaundryBps, Today, 1200, 1));
        Assert.Equal(1200, m.Service(Today, GstCategory.Laundry).Bps);
        Assert.Equal(1800, m.Service(Today, GstCategory.Banquet).Bps);
        Assert.Equal(1800, m.Service(Today.AddDays(-1), GstCategory.Laundry).Bps);
    }

    [Fact]
    public void Per_line_rounding_half_away_from_zero_and_CGST_takes_the_odd_paisa()
    {
        Assert.Equal(1, GstMath.LineTax(new Paise(10), 500).Value);
        Assert.Equal(-1, GstMath.LineTax(new Paise(-10), 500).Value);
        Assert.Equal(0, GstMath.LineTax(new Paise(9), 500).Value);
        Assert.Equal(0, GstMath.LineTax(new Paise(-9), 500).Value);
        Assert.Equal(2, GstMath.LineTax(new Paise(30), 500).Value);
        Assert.Equal(-2, GstMath.LineTax(new Paise(-30), 500).Value);
        Assert.Equal(2222, GstMath.LineTax(new Paise(12345), 1800).Value);
        Assert.Equal(-2222, GstMath.LineTax(new Paise(-12345), 1800).Value);
        Assert.Equal((1112L, 1111L), (GstMath.Split(new Paise(2223)).Cgst.Value, GstMath.Split(new Paise(2223)).Sgst.Value));
        Assert.Equal((1L, 0L), (GstMath.Split(new Paise(1)).Cgst.Value, GstMath.Split(new Paise(1)).Sgst.Value));
        Assert.Equal((-1L, 0L), (GstMath.Split(new Paise(-1)).Cgst.Value, GstMath.Split(new Paise(-1)).Sgst.Value));
        Assert.Equal((-1112L, -1111L), (GstMath.Split(new Paise(-2223)).Cgst.Value, GstMath.Split(new Paise(-2223)).Sgst.Value));
        Assert.Equal((0L, 0L), (GstMath.Split(Paise.Zero).Cgst.Value, GstMath.Split(Paise.Zero).Sgst.Value));
        foreach (var bps in new[] { 0, 500, 1200, 1800, 2800 })
            for (var amount = -2003L; amount <= 2003; amount += 7)
            {
                var x = GstMath.SplitLine(new Paise(amount), bps);
                Assert.Equal(x.Tax, x.Cgst + x.Sgst);
                Assert.True(Math.Abs(x.Cgst.Value - x.Sgst.Value) <= 1);
                Assert.True(Math.Abs(x.Tax.Value * 10_000 - amount * bps) <= 5_000, $"{amount}@{bps}");
            }

        var lines = new[] { (new Paise(12345), 1800), (new Paise(174000), 500), (new Paise(-50000), 500) };
        var sum = GstMath.SplitLines(lines);
        Assert.Equal(sum.Tax, sum.Cgst + sum.Sgst);
        Assert.Equal(lines.Sum(l => GstMath.LineTax(l.Item1, l.Item2).Value), sum.Tax.Value);
    }

    [Fact]
    public void Postings_split_output_CGST_2110_and_SGST_2120_and_balance()
    {
        // Laundry ₹123.45 at 18 % → ₹22.22 → CGST 11.11 + SGST 11.11 (prototype voucher test).
        var v = GstPosting.ForFolioLine("service", new Paise(12345), GstMath.LineTax(new Paise(12345), 1800));
        Assert.Equal([("1100", 14567L, 0L), ("4100", 0L, 12345L), ("2110", 0L, 1111L), ("2120", 0L, 1111L)], v.Select(e => (e.Account, e.Debit.Value, e.Credit.Value)));
        Assert.True(GstPosting.Balanced(v));
        // Outlet bill ₹1,740 at 5 % → ₹87 → 43.50 + 43.50, cash.
        var pos = GstPosting.ForLine("1000", GstPosting.OtherRevenue, new Paise(174000), GstMath.LineTax(new Paise(174000), 500));
        Assert.Equal([("1000", 182700L, 0L), ("4100", 0L, 174000L), ("2110", 0L, 4350L), ("2120", 0L, 4350L)], pos.Select(e => (e.Account, e.Debit.Value, e.Credit.Value)));
        // Discount (negative line) balances too.
        Assert.True(GstPosting.Balanced(GstPosting.ForFolioLine("discount", new Paise(-50000), GstMath.LineTax(new Paise(-50000), 500))));
        Assert.Equal(("2110", "2120"), (GstPosting.OutputCgst, GstPosting.OutputSgst));
    }

    [Fact]
    public void Dated_versions_later_change_on_the_same_date_wins_and_seed_is_illustrative()
    {
        var m = With(new GstSettingVersion(GstKeys.RoomLowBps, D("2026-10-25"), 1200, 5), new GstSettingVersion(GstKeys.RoomLowBps, D("2026-10-25"), 1000, 9));
        Assert.Equal(500, m.ValueOn(GstKeys.RoomLowBps, D("2026-10-24")));
        Assert.Equal(1000, m.ValueOn(GstKeys.RoomLowBps, D("2026-10-25")));
        Assert.Equal(1200, m.ValueOn(GstKeys.RoomLowBps, D("2025-09-21")));
        Assert.All(GstSeed.Settings, d => Assert.Contains(GstSeed.Illustrative, d.Help, StringComparison.Ordinal));
        Assert.Equal(10, GstSeed.Settings.Count);
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => GstSeed.ValidateValue(GstKeys.RoomLowBps, 2900));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => GstSeed.ValidateValue(GstKeys.FoodOptIn, 2));
        Assert.Throws<Hcc.Domain.Errors.HccException>(() => GstSeed.ValidateValue(GstKeys.RoomThreshold, 0));
        GstSeed.ValidateValue(GstKeys.RoomLowBps, 2800);
    }

    [Fact]
    public void Room_105_journey_is_4265_incl_265_GST_on_the_seed()
    {
        // 2 nights × ₹2,000 at 5 %, laundry ₹500 at 18 %, goodwill discount −₹500 at the room-night rate (5 %).
        var lines = new[] { (new Paise(200_000), Seed.RoomNight(Today, new Paise(200_000)).Bps), (new Paise(200_000), Seed.RoomNight(Today.AddDays(1), new Paise(200_000)).Bps),
            (new Paise(50_000), Seed.Service(Today, GstCategory.Laundry).Bps), (new Paise(-50_000), Seed.RoomNight(Today, new Paise(200_000)).Bps) };
        var t = FolioTotals.Compute(lines.Select(l => new FolioLine(l.Item1, l.Item2)), []);
        Assert.Equal((400_000L, 26_500L, 426_500L), (t.Net.Value, t.Tax.Value, t.Total.Value));
        Assert.Equal("₹4,265", t.Total.ToString());
    }
}
