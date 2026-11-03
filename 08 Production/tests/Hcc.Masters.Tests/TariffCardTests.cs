using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.Tariff;
using Hcc.Domain.Tax;

namespace Hcc.Masters.Tests;

/// <summary>F4-T1 tariff card rules without a database (prototype tests/stream-tariffcard.mjs parity, F4-CONTRACT §1.3–§1.5).</summary>
public class TariffCardTests
{
    private static readonly DateOnly Nov1 = new(2026, 11, 1);
    private static readonly DateOnly Today = new(2026, 10, 24);

    private static string Reason(HccException e) => e.Details!["reason"].GetString()!;

    private static TrcLine Room(int no, string type, long? excl, string daySet = "all", string? plan = "EP", long? incl = null) =>
        new(no, "room", type, plan, null, null, null, null, daySet, excl, 0, null, incl);

    private static TrcLine Occ(int no, string type, string kind, long? excl) => new(no, "occupancy", type, null, kind, null, null, null, "all", excl, 0, null, null);

    private static readonly IReadOnlySet<string> NoRefs = new HashSet<string>();

    [Fact]
    public void Inclusive_splits_each_half_and_round_trips_at_5_and_18_percent()
    {
        Assert.Equal(200_000, TariffCardRules.Inclusive(169_492, 1800));
        Assert.Equal(169_492, TariffCardRules.ExclusiveFromInclusive(200_000, 1800));
        foreach (var bps in new[] { 500, 1200, 1800 })
            foreach (var e in new long[] { 1, 99, 12_345, 169_492, 279_661, 750_000 })
            {
                var i = TariffCardRules.Inclusive(e, bps);
                Assert.Equal(i, TariffCardRules.Inclusive(TariffCardRules.ExclusiveFromInclusive(i, bps), bps));
            }

        Assert.Equal(329_999, TariffCardRules.Inclusive(TariffCardRules.ExclusiveFromInclusive(330_000, 1800), 1800));
    }

    [Fact]
    public void Deluxe_golden_and_dated_gst_master()
    {
        var master = GstMaster.Seed();
        Assert.Equal(1200, master.RoomNight(new DateOnly(2025, 9, 21), new Paise(200_000)).Bps);
        Assert.Equal(500, master.RoomNight(new DateOnly(2025, 9, 22), new Paise(200_000)).Bps);
        var l = TariffCardRules.Price(Room(1, "DLX", null, incl: 200_000), _ => 1800);
        Assert.Equal(169_492, l.RateExcl);
        Assert.Equal(200_000, l.DisplayIncl);
        Assert.Equal(1800, l.TaxBps);
    }

    [Theory] // F4-int rounding ruling: published inclusive price authoritative; exact where possible, else nearest + shown difference (CA to confirm)
    [InlineData(330_000, 1800, 279_661, -1)]
    [InlineData(50_000, 1800, 42_373, 1)]
    [InlineData(20_000, 1800, 16_949, -1)]
    [InlineData(330_000, 500, 314_286, 0)]
    [InlineData(50_000, 500, 47_619, -1)]
    [InlineData(20_000, 500, 19_048, 0)]
    public void Inclusive_card_price_is_reproduced_exactly_or_nearest(long incl, int bps, long excl, long diff)
    {
        var e = TariffCardRules.ExclusiveFromInclusive(incl, bps);
        Assert.Equal(excl, e);
        Assert.Equal(diff, TariffCardRules.Inclusive(e, bps) - incl);
        for (var x = Math.Max(0, e - 50); x <= e + 50; x++)
            Assert.True(Math.Abs(TariffCardRules.Inclusive(x, bps) - incl) >= Math.Abs(diff));
    }

    [Fact]
    public void Inclusive_entry_on_the_slab_boundary_is_refused()
    {
        int Slab(long e) => e <= 750_000 ? 500 : 1800;
        Assert.Equal(750_000, TariffCardRules.Price(Room(1, "STE", null, incl: 787_500), Slab).RateExcl);
        Assert.Equal(1800, TariffCardRules.Price(Room(1, "STE", null, incl: 886_000), Slab).TaxBps);
        var e = Assert.Throws<HccException>(() => TariffCardRules.Price(Room(1, "STE", null, incl: 800_000), Slab));
        Assert.Equal(TariffCardReasons.SlabAmbiguous, Reason(e));
    }

    [Fact]
    public void Weekend_pair_resolves_by_night_and_all_rows_unchanged()
    {
        var rows = new[]
        {
            new TrcTariffRow("DLX", new DateOnly(2026, 1, 1), 280_000, 'A', null),
            new TrcTariffRow("DLX", new DateOnly(2026, 11, 2), 300_000, 'W', 7),
            new TrcTariffRow("DLX", new DateOnly(2026, 11, 2), 360_000, 'E', 7),
        };
        IReadOnlyCollection<int> Weekend(long? _) => [5, 6, 0];
        Assert.Equal(280_000, TariffCardRules.Resolve(rows, new DateOnly(2026, 11, 1), Weekend)!.RatePaise);
        Assert.Equal(300_000, TariffCardRules.Resolve(rows, new DateOnly(2026, 11, 2), Weekend)!.RatePaise);
        Assert.Equal(360_000, TariffCardRules.Resolve(rows, new DateOnly(2026, 11, 6), Weekend)!.RatePaise);
        Assert.Equal(360_000, TariffCardRules.Resolve(rows, new DateOnly(2026, 11, 8), Weekend)!.RatePaise);
        Assert.Equal(300_000, TariffCardRules.Resolve(rows, new DateOnly(2026, 11, 8), _ => [5, 6])!.RatePaise);
    }

    [Fact]
    public void Plan_writes_two_rows_for_a_pair_blank_child_nothing_and_cp_add_on()
    {
        var lines = new[] { Room(1, "DLX", 300_000, "weekday"), Room(2, "DLX", 360_000, "weekend"), Room(3, "STE", 500_000), Room(4, "STE", 540_000, plan: "CP"),
            Occ(5, "DLX", "adult", 90_000), Occ(6, "DLX", "child", null) };
        var money = new Dictionary<string, (long? Now, long? OnDate)> { ["occupancyCharges|DLX|Adult"] = (80_000, null), ["mealPlans|CP|AddOn"] = (30_000, null) };
        var plan = TariffCardRules.Plan(lines, Nov1, [], NoRefs, k => money.TryGetValue(k, out var m) ? m : null);
        Assert.Equal(2, plan.Writes.Count(w => w.Master == "tariffs" && w.Ref.StartsWith("DLX|", StringComparison.Ordinal)));
        Assert.Contains(plan.Writes, w => w.Master == "occupancyCharges" && w.After == 90_000 && w.Before == 80_000);
        Assert.DoesNotContain(plan.Writes, w => w.Ref.Contains("Child", StringComparison.Ordinal));
        Assert.Contains(plan.Writes, w => w.Master == "mealPlans" && w.After == 20_000);
        Assert.Contains(plan.Skipped, x => x.Contains("line 6", StringComparison.Ordinal));
    }

    [Fact]
    public void Refusals_past_date_duplicate_hall_empty_conflict()
    {
        Assert.Equal(TariffCardReasons.PastEffective, Reason(Assert.Throws<HccException>(() => TariffCardRules.CheckDraft(new DateOnly(2026, 10, 23), Today, [5, 6, 0], []))));
        Assert.Equal(TariffCardReasons.DupLine, Reason(Assert.Throws<HccException>(() => TariffCardRules.CheckDraft(Nov1, Today, [5, 6, 0], [Room(1, "STE", 1), Room(2, "STE", 2)]))));
        var hall = new TrcLine(1, "hall", null, null, null, null, null, "H1", "all", 500_000, 1800, null, null);
        Assert.Equal(TariffCardReasons.HallNotReady, Reason(Assert.Throws<HccException>(() => TariffCardRules.Plan([hall], Nov1, [], NoRefs, _ => null))));
        Assert.Equal(TariffCardReasons.Empty, Reason(Assert.Throws<HccException>(() => TariffCardRules.Plan([Occ(1, "STE", "child", null)], Nov1, [], NoRefs, _ => (0, null)))));
        var hand = new[] { new TrcTariffRow("CLS", Nov1, 220_000, 'A', null) };
        var e = Assert.Throws<HccException>(() => TariffCardRules.Plan([Room(1, "CLS", 230_000)], Nov1, hand, NoRefs, _ => null));
        Assert.Equal(TariffCardReasons.Conflict, Reason(e));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, e.Code);
        var byCard = new[] { new TrcTariffRow("CLS", Nov1, 220_000, 'A', 3) };
        Assert.Equal(220_000, TariffCardRules.Plan([Room(1, "CLS", 230_000)], Nov1, byCard, NoRefs, _ => null).Writes.Single().Before);
    }

    [Fact]
    public void Dec198_card_figures_within_one_paisa_at_18_percent()
    {
        var card = new (long Excl, long Incl)[] { (279_661, 330_000), (296_610, 350_000), (237_288, 280_000), (254_237, 300_000), (169_492, 200_000), (186_441, 220_000),
            (203_390, 240_000), (127_119, 150_000), (144_068, 170_000), (42_373, 50_000), (33_898, 40_000) };
        foreach (var (excl, incl) in card) Assert.InRange(TariffCardRules.Inclusive(excl, 1800) - incl, -1, 1);
    }

    [Fact]
    public void Dec239_card_figures_at_room_gst_5_percent_reproduce_exactly()
    {
        // CA ruling 3 Nov 2026 (DEC-239): room GST 5 %; the GST-inclusive card stays (DEC-222), the exclusive rates recompute.
        var card = new (long Excl, long Incl)[] { (314_286, 330_000), (333_334, 350_000), (266_666, 280_000), (285_714, 300_000), (190_476, 200_000), (209_524, 220_000),
            (228_572, 240_000), (142_858, 150_000), (161_904, 170_000), (9_524, 10_000) };
        foreach (var (excl, incl) in card)
        {
            Assert.Equal(excl, TariffCardRules.ExclusiveFromInclusive(incl, 500));
            Assert.Equal(incl, TariffCardRules.Inclusive(excl, 500));
        }
    }
}
