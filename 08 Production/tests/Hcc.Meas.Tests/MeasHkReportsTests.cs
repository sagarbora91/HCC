using System.Text.Json;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.MeasHk;
using static Hcc.Reporting.Reports.MeasHk.MeasHkRules;

namespace Hcc.Meas.Tests;

/// <summary>[F12-RPT-HK] The six housekeeping / engineering / asset reports: exception-rule boundaries (same numbers as tests/stream-meas-rpt-hk.mjs),
/// definitions against the prototype catalogue.json, switch keys, DEC-061 money flags.</summary>
public class MeasHkReportsTests
{
    private static readonly MeasHkReportBase[] All =
    [
        new HkProductivityReport(null!), new OooDaysReport(null!), new PpmComplianceReport(null!), new UtilitiesPerRoomReport(null!), new LinenCostReport(null!), new AssetLifeReport(null!),
    ];

    [Fact]
    public void Pass_rate_95_percent_is_not_red_and_below_is_red()
    {
        Assert.False(PassRateRed(19, 20));
        Assert.True(PassRateRed(18, 20));
        Assert.False(PassRateRed(0, 0));
    }

    [Fact]
    public void Median_departure_45_minutes_is_not_red_46_is()
    {
        Assert.False(MedianDepartureRed(45));
        Assert.True(MedianDepartureRed(46));
        Assert.False(MedianDepartureRed(null));
        Assert.Equal(45, Median([30, 60, 45]));
        Assert.Equal(37.5, Median([30, 45]));
    }

    [Fact]
    public void Out_of_order_7_days_without_ticket_is_not_red_8_is()
    {
        Assert.False(OooNoTicketRed(7));
        Assert.True(OooNoTicketRed(8));
    }

    [Theory]
    [InlineData("2026-10-17", 8)]
    [InlineData("2026-10-18", 7)]
    public void Out_of_order_window_days_are_inclusive_of_the_to_date(string windowFrom, int expected) =>
        Assert.Equal(expected, OooDaysReport.Days(DateOnly.Parse(windowFrom), null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 24)));

    [Fact]
    public void Idle_14_nights_is_red_13_is_not_and_a_stay_breaks_the_run()
    {
        var to = new DateOnly(2026, 10, 24);
        Assert.Equal(14, OooDaysReport.Idle(new HashSet<DateOnly>(), new DateOnly(2026, 10, 11), to));
        Assert.True(IdleRed(14));
        Assert.False(IdleRed(13));
        Assert.Equal(3, OooDaysReport.Idle(new HashSet<DateOnly> { new(2026, 10, 21) }, new DateOnly(2026, 10, 11), to));
    }

    [Fact]
    public void Preventive_overdue_7_days_is_not_red_8_is()
    {
        var asOf = new DateOnly(2026, 10, 24);
        Assert.False(OverdueRed(asOf.AddDays(-7), asOf));
        Assert.True(OverdueRed(asOf.AddDays(-8), asOf));
    }

    [Fact]
    public void P1_mttr_24_hours_is_not_red_24_hours_1_minute_is()
    {
        Assert.False(P1MttrRed(24));
        Assert.True(P1MttrRed(24 + 1 / 60.0));
        Assert.False(P1MttrRed(null));
    }

    [Fact]
    public void Third_failure_inside_90_days_is_red_on_day_91_it_is_not()
    {
        var d0 = new DateOnly(2026, 7, 1);
        Assert.True(RepeatFailureRed([d0, d0.AddDays(31), d0.AddDays(90)]));
        Assert.False(RepeatFailureRed([d0, d0.AddDays(31), d0.AddDays(91)]));
        Assert.False(RepeatFailureRed([d0, d0.AddDays(10)]));
    }

    [Fact]
    public void Utility_per_night_15_percent_above_baseline_is_not_red_above_is()
    {
        Assert.False(UtilityRed(1.15, 1.0));
        Assert.True(UtilityRed(1.16, 1.0));
        Assert.False(UtilityRed(2.0, null));
        Assert.False(UtilityRed(null, 1.0));
    }

    [Fact]
    public void Utility_use_is_the_difference_of_consecutive_readings_and_lpg_cylinders_become_kg()
    {
        var d = new DateOnly(2026, 9, 10);
        var use = UtilitiesPerRoomReport.Use(
        [
            ("E", "electricity", "kWh", d, 100), ("E", "electricity", "kWh", d.AddDays(5), 110.5),
            ("L", "lpg", "cylinders", d, 5), ("L", "lpg", "cylinders", d.AddDays(5), 7),
        ]);
        Assert.Equal(10.5, use["electricity"][d.AddDays(5)], 6);
        Assert.Equal(38, use["lpg"][d.AddDays(5)], 6);
        Assert.Empty(UtilitiesPerRoomReport.Use([("E", "electricity", "kWh", d, 100)], "X")["electricity"]);
        Assert.Equal("1.15", Dec2(1.15));
        Assert.Null(Dec2(null));
    }

    [Fact]
    public void Linen_loss_of_exactly_2_percent_of_par_is_not_red_one_more_is()
    {
        Assert.False(LinenLossRed(4, 200, 2));
        Assert.True(LinenLossRed(5, 200, 2));
        Assert.False(LinenLossRed(5, 0, 2));
    }

    [Fact]
    public void Asset_past_life_is_the_day_after_the_replacement_date_and_reserve_rule_is_strict()
    {
        var due = new DateOnly(2025, 1, 1);
        Assert.False(PastLife(due, due));
        Assert.True(PastLife(due, due.AddDays(1)));
        Assert.False(PastLife(null, due));
        Assert.False(ReserveRed(30000, 30000));
        Assert.True(ReserveRed(30001, 30000));
        Assert.Equal("5 y 0 m", AgeText(60));
        Assert.Equal(60, WholeMonths(new DateOnly(2020, 1, 1), new DateOnly(2025, 1, 1)));
        Assert.Equal(59, WholeMonths(new DateOnly(2020, 1, 2), new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void Shift_follows_the_start_hour()
    {
        Assert.Equal("Morning", ShiftOf(6));
        Assert.Equal("Morning", ShiftOf(13));
        Assert.Equal("Afternoon", ShiftOf(14));
        Assert.Equal("Afternoon", ShiftOf(21));
        Assert.Equal("Night", ShiftOf(22));
        Assert.Equal("Night", ShiftOf(3));
        Assert.Equal("Morning", ShiftOf(null));
    }

    [Fact]
    public void Switch_keys_and_money_flags_match_the_catalogue()
    {
        var want = new Dictionary<string, (string Key, bool Money)>
        {
            ["rpt.rooms.hk-productivity"] = ("meas.hk.productivity", false), ["rpt.rooms.ooo-days"] = ("meas.hk.ooo", true), ["rpt.rooms.ppm-compliance"] = ("meas.hk.ppm", false),
            ["rpt.rooms.utilities-per-room"] = ("meas.hk.utilities", true), ["rpt.stock.linen-cost"] = ("meas.hk.linen", true), ["rpt.acc.asset-life"] = ("meas.hk.assets", true),
        };
        foreach (var r in All)
        {
            Assert.IsAssignableFrom<IMeasuredReport>(r);
            Assert.Equal(want[r.Definition.Id].Key, r.MeasKey);
            Assert.NotNull(MeasurementCatalogue.Item(r.MeasKey));
            Assert.Equal(r.MeasKey, MeasurementCatalogue.Items.First(i => i.ReportId == r.Definition.Id).Key);
            Assert.Equal(want[r.Definition.Id].Money, r.Definition.MoneyFlag);
            Assert.Equal(r.Definition.MoneyFlag, r.Definition.Columns.Any(c => c.Money));
            Assert.Equal("MEAS", r.Definition.Status);
        }

        Assert.Equal(["wdv", "replacementCost", "reserveAccrued", "capexSpent"], new AssetLifeReport(null!).Definition.Columns.Where(c => c.Money).Select(c => c.Key));
        Assert.Equal(["lostRevenue"], new OooDaysReport(null!).Definition.Columns.Where(c => c.Money).Select(c => c.Key));
        Assert.Equal(["unitCost", "costPerNight"], new UtilitiesPerRoomReport(null!).Definition.Columns.Where(c => c.Money).Select(c => c.Key));
        Assert.Equal(["costPerNight"], new LinenCostReport(null!).Definition.Columns.Where(c => c.Money).Select(c => c.Key));
    }

    [Fact]
    public void Definitions_equal_the_prototype_catalogue_json()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "03 Prototype", "src", "domain", "reports", "catalogue.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "03 Prototype", "src", "domain", "reports", "catalogue.json")));
        var proto = doc.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);
        foreach (var r in All)
        {
            var d = r.Definition;
            var e = proto[d.Id];
            Assert.Equal(e.GetProperty("title").GetString(), d.Title);
            Assert.Equal(e.GetProperty("area").GetString(), d.Area);
            Assert.Equal(e.GetProperty("priority").GetString(), d.Priority);
            Assert.Equal(e.GetProperty("moneyFlag").GetBoolean(), d.MoneyFlag);
            Assert.Equal(e.GetProperty("moneyGate").GetString(), d.MoneyGate);
            Assert.Equal(e.GetProperty("viewPerms").EnumerateArray().Select(x => x.GetString()), d.ViewPerms);
            Assert.Equal(e.GetProperty("deskPerms").EnumerateArray().Select(x => x.GetString()), d.DeskPerms);
            Assert.Equal(e.GetProperty("params").EnumerateArray().Select(x => x.GetString()), d.Params.Select(p => p.Key));
            Assert.Equal(e.GetProperty("phone").GetString(), d.Phone.ToString().ToLowerInvariant());
            Assert.Equal(e.GetProperty("formats").EnumerateArray().Select(x => x.GetString()), d.Formats.Select(f => f.ToString().ToLowerInvariant()));
            var cols = e.GetProperty("columns").EnumerateArray().ToList();
            Assert.Equal(cols.Select(c => c.GetProperty("key").GetString()), d.Columns.Select(c => c.Key));
            Assert.Equal(cols.Select(c => c.GetProperty("money").GetBoolean()), d.Columns.Select(c => c.Money));
            Assert.Equal(cols.Select(c => c.GetProperty("type").GetString()), d.Columns.Select(c => c.Type == ReportColumnType.Percent ? "pct" : c.Type.ToString().ToLowerInvariant()));
        }
    }
}
