using Hcc.Domain.Yield;

namespace Hcc.Yield.Tests.Domain;

/// <summary>
/// The prototype's yield figures (03 Prototype tests/stream-yield.mjs, BASE fixture + module-13 fixture rules) reproduced by the pure engine.
/// Room types 1 Classic ₹2,000 · 2 Deluxe ₹3,000 · 3 Suite ₹4,500, 23 sellable rooms, 10 rooms on the books on Sat 24 Oct.
/// </summary>
public sealed class YieldRulesGoldenTests
{
    private const int Classic = 1, Deluxe = 2, Suite = 3;
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly int[] Forecast = [14, 16, 9, 10, 12, 14, 18, 20, 13, 8, 9, 12, 16, 21, 22, 22, 20, 15, 10, 9, 10, 11, 12, 15, 17, 12, 10, 10, 11, 13];

    public static YieldConfig Rules(IReadOnlyList<YieldRestrictionDef>? restrictions = null) => new(
        [
            new YieldPlan("BAR", "Best available rate", 0, 1, null, null, null, null, true, ""),
            new YieldPlan("CORP", "Corporate", -1200, 1, null, null, null, null, true, ""),
            new YieldPlan("WKND", "Weekend stay", -500, 2, [5, 6, 0], null, null, null, true, ""),
            new YieldPlan("LONG7", "Long-stay 7+ nights", -1500, 7, null, null, null, null, true, ""),
            new YieldPlan("DAYUSE", "Day-use (10:00-17:00)", -4500, 0, null, "10:00", "17:00", 90, true, ""),
        ],
        [
            new YieldSeason(1, "Diwali festive", new(2026, 11, 6), new(2026, 11, 10), 2000, [], 2),
            new YieldSeason(2, "Latur MIDC trade expo (fictional)", new(2026, 10, 28), new(2026, 10, 30), 800, [Deluxe, Suite], 1),
            new YieldSeason(3, "Wedding season", new(2026, 11, 20), new(2026, 12, 14), 1000, [Deluxe, Suite], 1),
        ],
        [0, 0, 0, 0, 0, 500, 1000],
        [new YieldOccRule(1, ">=", 80, 1000), new YieldOccRule(2, ">=", 90, 1500), new YieldOccRule(3, "<", 40, -1000)],
        new YieldGuard(-2000, 4000, 5000),
        restrictions ??
        [
            new YieldRestrictionDef("YLDR-1", null, new(2026, 11, 7), new(2026, 11, 9), 2, false, "Diwali weekend"),
            new YieldRestrictionDef("YLDR-2", Suite, new(2026, 11, 8), new(2026, 11, 8), 1, true, "Lakshmi Puja day"),
        ],
        []);

    public static YieldInputs Inputs(IReadOnlyList<TariffRow>? tariffs = null, IReadOnlySet<TariffRow>? owned = null)
    {
        var days = Enumerable.Range(0, 60).Select(i => Today.AddDays(i)).ToList();
        return new YieldInputs(Today,
            [new YieldRoomType(Classic, "Classic", 200000), new YieldRoomType(Deluxe, "Deluxe", 300000), new YieldRoomType(Suite, "Suite", 450000)],
            tariffs ?? [], owned ?? new HashSet<TariffRow>(),
            days.ToDictionary(d => d, _ => 23),
            days.ToDictionary(d => d, d => d == Today ? 10 : 0),
            days.Select((d, i) => (d, i)).Where(x => x.i < Forecast.Length).ToDictionary(x => x.d, x => Forecast[x.i]));
    }

    private static YieldEngine Engine(YieldConfig? cfg = null, YieldInputs? inputs = null) => new(cfg ?? Rules(), inputs ?? Inputs());

    [Fact]
    public void Saturday_classic_explains_base_weekday_and_occupancy()
    {
        var n = Engine().Night(Classic, new(2026, 10, 24));
        Assert.Equal(200000, n.Base);
        Assert.Equal(61, n.Occ.Pct);
        Assert.Equal(23, n.Occ.Sellable);
        Assert.Equal(10, n.Occ.OnBooks);
        Assert.Equal(14, n.Occ.Forecast);
        Assert.True(n.Occ.Simulated);
        Assert.Equal([1000], n.Steps.Select(s => s.Bps));
        Assert.Equal(220000, n.Suggested);
        Assert.Contains("Sat +10%", n.Explain, StringComparison.Ordinal);
    }

    [Fact]
    public void Low_occupancy_tier_lowers_the_rate_and_seasons_apply_only_to_listed_types()
    {
        var e = Engine();
        Assert.Equal(180000, e.Night(Classic, new(2026, 11, 2)).Suggested);
        Assert.Equal(200000, e.Night(Classic, new(2026, 10, 28)).Suggested);
        Assert.Equal(325000, e.Night(Deluxe, new(2026, 10, 28)).Suggested);
    }

    [Fact]
    public void Highest_occupancy_tier_only_ceiling_clamp_and_fifty_rupee_rounding()
    {
        var n = Engine().Night(Suite, new(2026, 11, 7));
        Assert.Equal(2000 + 1000 + 1500, n.RawBps);
        Assert.Equal(4000, n.AppliedBps);
        Assert.Equal("ceiling", n.Clamped);
        Assert.Equal(630000, n.Suggested);
        Assert.Equal(205000, YieldEngine.Round(203333, 5000));
        Assert.Equal(5000, YieldEngine.Round(1, 5000));
    }

    [Fact]
    public void Floor_clamp_with_tighter_guardrail()
    {
        var cfg = Rules() with { Guard = new YieldGuard(-500, 4000, 5000) };
        var n = Engine(cfg).Night(Classic, new(2026, 11, 2));
        Assert.Equal("floor", n.Clamped);
        Assert.Equal(190000, n.Suggested);
    }

    [Fact]
    public void Forecast_occupancy_is_the_larger_of_books_and_forecast_over_sellable_rooms()
    {
        var o = Engine().Occ(new(2026, 10, 24));
        Assert.Equal((23, 10, 14, 61, true), (o.Sellable, o.OnBooks, o.Forecast, o.Pct, o.Simulated));
    }

    [Fact]
    public void Base_follows_a_manual_schedule_never_yield_owned_rows()
    {
        var manual = new TariffRow(Classic, new(2026, 10, 27), 220000);
        var owned = new TariffRow(Classic, new(2026, 10, 31), 240000);
        var e = Engine(inputs: Inputs([manual, owned], new HashSet<TariffRow> { owned }));
        Assert.Equal(220000, e.Base(Classic, new(2026, 10, 27)).Rate);
        Assert.Equal(220000, e.Base(Classic, new(2026, 10, 31)).Rate);
        Assert.Equal(200000, e.Base(Classic, new(2026, 10, 26)).Rate);
        Assert.Equal(240000, e.Live(Classic, new(2026, 10, 31)));
    }

    [Fact]
    public void Publish_plan_writes_changed_nights_and_a_closing_row_that_keeps_the_rate_after_the_window()
    {
        var plan = Engine().PlanPublish(Today, Today.AddDays(13), [Classic, Deluxe, Suite]);
        long RateOn(int type, DateOnly d) => plan.Tariffs.Where(t => t.RoomTypeId == type && t.EffectiveFrom <= d).OrderBy(t => t.EffectiveFrom).Select(t => t.RatePaise).LastOrDefault();
        Assert.Equal(630000, RateOn(Suite, new(2026, 11, 6)));
        Assert.Equal(180000, RateOn(Classic, new(2026, 11, 2)));
        Assert.Equal(240000, RateOn(Classic, new(2026, 10, 31)));
        Assert.Equal(450000, RateOn(Suite, new(2026, 11, 7))); // the night after the window is pinned to the old rate by the closing row
        Assert.Contains(plan.Rows, r => r.Closing && r.RoomTypeId == Suite && r.After == 450000);
        Assert.All(plan.Rows.Where(r => r.Closing), r => Assert.Equal(r.Before, r.After));
    }

    [Fact]
    public void Partial_window_leaves_later_nights_unchanged()
    {
        var plan = Engine().PlanPublish(new(2026, 10, 31), new(2026, 10, 31), [Classic]);
        Assert.Equal(2, plan.Rows.Count);
        Assert.Equal(240000, plan.Rows[0].After);
        Assert.True(plan.Rows[1].Closing);
        Assert.Equal(200000, plan.Rows[1].After);
    }

    [Fact]
    public void Rate_plans_derive_from_bar_day_use_closes_on_high_demand()
    {
        var e = Engine();
        var corp = e.Config.Plans.First(p => p.Code == "CORP");
        var day = e.Config.Plans.First(p => p.Code == "DAYUSE");
        var sat = e.Night(Classic, new(2026, 10, 27)).Suggested;
        Assert.Equal(175000, e.PlanRate(corp, sat));
        Assert.False(e.PlanAvailable(day, new(2026, 11, 7), e.Occ(new(2026, 11, 7)).Pct).Ok);
        Assert.True(e.PlanAvailable(day, new(2026, 10, 27), e.Occ(new(2026, 10, 27)).Pct).Ok);
        Assert.Equal(110000, e.Quote("DAYUSE", Classic, new(2026, 10, 27), 3).Total);
        Assert.Equal(7, e.Quote("LONG7", Classic, new(2026, 10, 26), 7).Rows.Count);
    }

    [Fact]
    public void Quotes_report_minimum_stay_closed_to_arrival_and_plan_rules()
    {
        var e = Engine();
        Assert.Contains(e.Quote("BAR", Classic, new(2026, 11, 7), 1).Issues, x => x.Contains("Minimum stay 2", StringComparison.Ordinal));
        Assert.Empty(e.Quote("BAR", Classic, new(2026, 11, 7), 2).Issues);
        Assert.Contains(e.Quote("BAR", Suite, new(2026, 11, 8), 2).Issues, x => x.Contains("Closed to arrival", StringComparison.Ordinal));
        Assert.Contains(e.Quote("LONG7", Deluxe, new(2026, 10, 26), 3).Issues, x => x.Contains("at least 7", StringComparison.Ordinal));
        Assert.Contains(e.Quote("WKND", Deluxe, new(2026, 10, 26), 2).Issues, x => x.Contains("Not sold on Mon", StringComparison.Ordinal));
        Assert.Contains(e.Quote("BAR", Classic, new(2026, 10, 20), 1).Issues, x => x.Contains("before the business date", StringComparison.Ordinal));
    }

    [Fact]
    public void Override_replaces_the_computed_rate_and_is_explained()
    {
        var cfg = Rules() with { Overrides = [new YieldOverrideDef(Deluxe, new(2026, 10, 27), 350000, "Corporate block")] };
        var n = Engine(cfg).Night(Deluxe, new(2026, 10, 27));
        Assert.Equal(350000, n.Suggested);
        Assert.Equal(300000, n.Computed);
        Assert.Contains("override", n.Explain, StringComparison.Ordinal);
    }

    [Fact]
    public void Signature_changes_when_a_tariff_row_of_the_types_changes()
    {
        var a = YieldEngine.TariffSignature([new TariffRow(Classic, new(2026, 10, 30), 200000)], [Classic]);
        Assert.Equal(a, YieldEngine.TariffSignature([new TariffRow(Classic, new(2026, 10, 30), 200000), new TariffRow(Deluxe, new(2026, 10, 30), 1)], [Classic]));
        Assert.NotEqual(a, YieldEngine.TariffSignature([new TariffRow(Classic, new(2026, 10, 30), 210000)], [Classic]));
    }

    [Fact]
    public void Formatting_matches_the_prototype_wording()
    {
        Assert.Equal("+10%", YieldFormat.Pct(1000));
        Assert.Equal("−12%", YieldFormat.Pct(-1200));
        Assert.Equal("±0%", YieldFormat.Pct(0));
        Assert.Equal("₹2,00,000", YieldFormat.Money(20000000));
        Assert.Equal("₹2,000", YieldFormat.Money(200000));
    }
}
