// F12-RPT-REVENUE · server twin of tests/stream-meas-rpt-revenue.mjs: the pure rules at their boundaries (same numbers as the prototype), the six definitions
// (ids, area, money flag, switch key, column keys) and the pure computed-row builders. SQL loading is covered by the integrator's gate run on a database.
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.MeasRevenue;

namespace Hcc.Reporting.Tests;

/// <summary>The six revenue reports as the engine test's server catalogue lists them (constructors take nulls: only definitions are read).</summary>
public static class MeasRevenueCatalogue
{
    public static IEnumerable<IReportSelector> Reports() =>
    [
        new DemandCalendarReport(null!, null!), new PickupPaceReport(null!, null!), new DisplacementReport(null!, null!),
        new OverbookingWashReport(null!, null!), new RestrictionEffectReport(null!, null!), new RateParityReport(null!, null!),
    ];
}

public sealed class MeasRevenueTests
{
    private static readonly string[] Ids =
    [
        "rpt.revenue.demand-calendar", "rpt.revenue.pickup-pace", "rpt.revenue.displacement", "rpt.revenue.overbooking-wash", "rpt.revenue.restriction-effect", "rpt.revenue.rate-parity",
    ];

    [Fact]
    public void F12_RPT_REVENUE_six_reports_ids_area_money_flag_and_switch_key()
    {
        var sel = MeasRevenueCatalogue.Reports().ToList();
        Assert.Equal(Ids, sel.Select(s => s.Definition.Id));
        foreach (var s in sel)
        {
            var d = s.Definition;
            Assert.Equal("revenue", d.Area);
            Assert.Equal("MEAS", d.Status);
            Assert.True(d.MoneyFlag);
            Assert.Equal("PERM-RPT-002", d.MoneyGate);
            Assert.All(d.Columns.Where(c => c.Money), c => Assert.Equal(ReportColumnType.Money, c.Type));
            var key = Assert.IsAssignableFrom<IMeasuredReport>(s).MeasKey;
            var item = MeasurementCatalogue.Item(key);
            Assert.NotNull(item);
            Assert.Equal(d.Id, item!.ReportId);
        }
    }

    [Fact]
    public void F12_RPT_REVENUE_column_keys_follow_the_catalogue_order()
    {
        string[] Keys(ReportDefinition d) => d.Columns.Select(c => c.Key).ToArray();
        Assert.Equal(["date", "weekday", "onBooks", "forecast", "lastYear", "event", "bar", "suggested", "offerOpen"], Keys(new DemandCalendarReport(null!, null!).Definition));
        Assert.Equal(["month", "rooms", "lastYearRooms", "revenue", "lastYearRevenue", "pace", "pickup7", "lead"], Keys(new PickupPaceReport(null!, null!).Definition));
        Assert.Equal(["night", "forecast", "displaced", "displacedRevenue", "offerRevenue", "net", "recommendation"], Keys(new DisplacementReport(null!, null!).Definition));
        Assert.Equal(["weekday", "channel", "noShowPct", "cancelPct", "washRooms", "allowed", "walked", "walkCost"], Keys(new OverbookingWashReport(null!, null!).Definition));
        Assert.Equal(["night", "restriction", "refused", "reshaped", "revenue", "unrestricted", "mode"], Keys(new RestrictionEffectReport(null!, null!).Definition));
        Assert.Equal(["stayDate", "direct", "ota", "comp1", "comp2", "comp3", "comp4", "comp5", "comp6", "median", "position", "exception"], Keys(new RateParityReport(null!, null!).Definition));
    }

    [Fact]
    public void F12_RPT_REVENUE_demand_rule_boundaries()
    {
        Assert.True(MeasRevenueLogic.DemandRed(85, true, true));
        Assert.False(MeasRevenueLogic.DemandRed(84, true, true));
        Assert.False(MeasRevenueLogic.DemandRed(85, false, true));
        Assert.True(MeasRevenueLogic.DemandRed(25, false, false));
        Assert.False(MeasRevenueLogic.DemandRed(26, false, false));
        Assert.False(MeasRevenueLogic.DemandRed(25, false, true));
        var red = DemandCalendarReport.Row(new DateOnly(2026, 12, 1), 1, 85, null, "", 200000, 200000, 200000, true);
        Assert.True(red.Red);
        Assert.Equal("Tuesday", red.Cells["weekday"]);
        Assert.False(DemandCalendarReport.Row(new DateOnly(2026, 12, 1), 1, 85, null, "", 205000, 205000, 200000, true).Red);
        Assert.False(DemandCalendarReport.Row(new DateOnly(2026, 12, 1), 1, 84, null, "", 200000, 200000, 200000, true).Red);
    }

    [Fact]
    public void F12_RPT_REVENUE_pace_rule_boundaries()
    {
        Assert.Equal(-101, MeasRevenueLogic.PaceTenths(899, 1000));
        Assert.True(MeasRevenueLogic.PaceRed(MeasRevenueLogic.PaceTenths(899, 1000)));
        Assert.Equal(-100, MeasRevenueLogic.PaceTenths(900, 1000));
        Assert.False(MeasRevenueLogic.PaceRed(MeasRevenueLogic.PaceTenths(900, 1000)));
        Assert.Null(MeasRevenueLogic.PaceTenths(5, 0));
        Assert.False(MeasRevenueLogic.PaceRed(null));
        var r = PickupPaceReport.Row("2026-12", 899, 1000, 1, 1, 3, "8-30 days");
        Assert.True(r.Red);
        Assert.Equal(-10.1, r.Cells["pace"]);
        Assert.False(PickupPaceReport.Row("2026-12", 900, 1000, 1, 1, 3, "").Red);
        Assert.Equal("2027-01", PickupPaceReport.MonthPlus(new DateOnly(2026, 11, 7), 2));
        Assert.Equal("2026-11", PickupPaceReport.MonthPlus(new DateOnly(2026, 11, 7), 0));
    }

    [Fact]
    public void F12_RPT_REVENUE_displacement_rule_and_recommendation()
    {
        Assert.True(MeasRevenueLogic.DisplacementRed(-1));
        Assert.False(MeasRevenueLogic.DisplacementRed(0));
        Assert.Equal("accept", MeasRevenueLogic.Recommendation(0, 150000, 0));
        Assert.Equal("counter-rate", MeasRevenueLogic.Recommendation(-1, 100000, 150000));
        Assert.Equal("decline", MeasRevenueLogic.Recommendation(-1, 100000, 150001));
        Assert.Equal(120000, MeasRevenueLogic.CounterRate(1200000, 10));
        Assert.Equal(125000, MeasRevenueLogic.CounterRate(1210000, 10));
        Assert.Equal(0, MeasRevenueLogic.CounterRate(0, 10));
    }

    [Fact]
    public void F12_RPT_REVENUE_oversold_restriction_and_parity_boundaries()
    {
        Assert.True(MeasRevenueLogic.OversoldRed(1, 0));
        Assert.False(MeasRevenueLogic.OversoldRed(1, 1));
        Assert.False(MeasRevenueLogic.OversoldRed(0, 0));
        Assert.True(MeasRevenueLogic.RestrictionRed(39, 100));
        Assert.False(MeasRevenueLogic.RestrictionRed(40, 100));
        Assert.False(MeasRevenueLogic.RestrictionRed(0, 0));
        Assert.True(MeasRevenueLogic.RestrictionRed(9, 24));
        Assert.False(MeasRevenueLogic.RestrictionRed(10, 24));
        Assert.True(MeasRevenueLogic.ParityRed(200000, 199999, null, 0, 100));
        Assert.False(MeasRevenueLogic.ParityRed(200000, 200000, null, 0, 100));
        Assert.False(MeasRevenueLogic.ParityRed(230000, null, 200000, 39, 100));
        Assert.True(MeasRevenueLogic.ParityRed(230100, null, 200000, 39, 100));
        Assert.False(MeasRevenueLogic.ParityRed(240000, null, 200000, 40, 100));
        Assert.True(MeasRevenueLogic.ParityRed(240000, null, 200000, 39, 100));
    }

    [Fact]
    public void F12_RPT_REVENUE_median_position_and_lead_bucket()
    {
        Assert.Equal(200L, MeasRevenueLogic.Median([300, 100, 200]));
        Assert.Equal(150L, MeasRevenueLogic.Median([100, 200]));
        Assert.Null(MeasRevenueLogic.Median([]));
        Assert.Equal(15, MeasRevenueLogic.PositionPct(230000, 200000));
        Assert.Equal("0-7 days", MeasRevenueLogic.LeadBucket(7));
        Assert.Equal("8-30 days", MeasRevenueLogic.LeadBucket(8));
        Assert.Equal("31-90 days", MeasRevenueLogic.LeadBucket(90));
        Assert.Equal("Over 90 days", MeasRevenueLogic.LeadBucket(91));
        Assert.Equal(33.3, MeasRevenueLogic.Tenths(1, 3));
    }
}
