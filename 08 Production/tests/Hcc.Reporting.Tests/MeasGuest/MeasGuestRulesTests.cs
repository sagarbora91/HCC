// F12-RPT-GUEST · pure rules of the six guest measurement reports (twin of 03 Prototype/tests/stream-meas-rpt-guest.mjs): rounding, NPS, exception boundaries, switch keys.
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.MeasGuest;

namespace Hcc.Reporting.Tests.MeasGuest;

public sealed class MeasGuestRulesTests
{
    private static IReadOnlyList<MeasGuestBase> Reports() =>
        [new SatisfactionReport(null!), new ReputationIndexReport(null!), new ServiceRecoveryReport(null!), new DeskTimingReport(null!), new UpsellReport(null!), new RepeatLoyaltyReport(null!)];

    [Fact]
    public void The_six_reports_carry_the_catalogue_switch_keys_and_exist_in_the_measurement_catalogue()
    {
        var want = new Dictionary<string, string>
        {
            ["rpt.guests.satisfaction"] = "meas.guest.survey", ["rpt.guests.reputation-index"] = "meas.guest.reputation", ["rpt.guests.service-recovery"] = "meas.guest.recovery",
            ["rpt.guests.desk-timing"] = "meas.guest.desk-timing", ["rpt.guests.upsell"] = "meas.guest.upsell", ["rpt.guests.repeat-loyalty"] = "meas.guest.repeat",
        };
        foreach (var r in Reports())
        {
            Assert.Equal(want[r.Definition.Id], r.MeasKey);
            var item = MeasurementCatalogue.Item(r.MeasKey);
            Assert.NotNull(item);
            Assert.Equal(r.Definition.Id, item!.ReportId);
            Assert.IsAssignableFrom<IMeasuredReport>(r);
        }
    }

    [Fact]
    public void Money_columns_are_flagged_one_by_one()
    {
        Assert.Equal(["compensation"], ServiceRecoveryReport.Columns.Where(c => c.Money).Select(c => c.Key));
        Assert.Equal(["lateCheckout", "breakfast", "extraBed", "revenuePerArrival"], UpsellReport.Columns.Where(c => c.Money).Select(c => c.Key));
        Assert.Equal(["revenueShare"], RepeatLoyaltyReport.Columns.Where(c => c.Money).Select(c => c.Key));
        Assert.DoesNotContain(SatisfactionReport.Columns, c => c.Money);
        Assert.DoesNotContain(DeskTimingReport.Columns, c => c.Money);
    }

    [Fact]
    public void Rounding_is_half_up_and_medians_are_the_lower_median()
    {
        Assert.Equal(3, MeasGuestRules.RoundHalf(2.5m));
        Assert.Equal(-2, MeasGuestRules.RoundHalf(-2.5m));
        Assert.Equal(66.7, MeasGuestRules.Pct1(2, 3));
        Assert.Equal(75.0, MeasGuestRules.Pct1(3, 4));
        Assert.Null(MeasGuestRules.Pct1(1, 0));
        Assert.Equal("3.2", MeasGuestRules.Avg1(16, 5));
        Assert.Equal("4.3", MeasGuestRules.Avg1(13, 3));
        Assert.Null(MeasGuestRules.Avg1(0, 0));
        Assert.Equal(2, MeasGuestRules.LowerMedian([3, 1, 2, 10]));
        Assert.Null(MeasGuestRules.LowerMedian([]));
        Assert.Equal("2030-12", MeasGuestRules.PrevMonth("2031-01"));
        Assert.Equal("2030-05", MeasGuestRules.PrevMonth("2030-06"));
    }

    [Fact]
    public void Nps_is_percent_9_10_minus_percent_0_6()
    {
        Assert.Equal(67, MeasGuestRules.Nps([9, 9, 7]));
        Assert.Null(MeasGuestRules.Nps([]));
        Assert.Equal(0, MeasGuestRules.Nps([10, 9, 8, 6, 0]));
        var thirty = Enumerable.Repeat(10, 30).Concat(Enumerable.Repeat(8, 70)).ToList();
        var twentyNine = Enumerable.Repeat(10, 29).Concat(Enumerable.Repeat(8, 71)).ToList();
        Assert.False(MeasGuestRules.SatisfactionRed(MeasGuestRules.Nps(thirty), 30, 0));
        Assert.True(MeasGuestRules.SatisfactionRed(MeasGuestRules.Nps(twentyNine), 30, 0));
        Assert.True(MeasGuestRules.SatisfactionRed(50, 30, 1), "a low score unanswered for 48 hours");
        Assert.False(MeasGuestRules.SatisfactionRed(null, 30, 0));
        Assert.True(MeasGuestRules.SurveyLow(5, 5, 2, 5, null));
        Assert.False(MeasGuestRules.SurveyLow(5, 5, 3, 5, 3));
        Assert.True(MeasGuestRules.SurveyLow(5, 5, 5, 5, 2));
    }

    [Fact]
    public void Reputation_red_boundaries_are_a_drop_of_0_30_and_a_response_rate_of_80_percent()
    {
        Assert.True(MeasGuestRules.ReputationRed(4.2m, 4.5m, 10, 10));
        Assert.False(MeasGuestRules.ReputationRed(4.21m, 4.5m, 10, 10));
        Assert.False(MeasGuestRules.ReputationRed(4.5m, null, 10, 10));
        Assert.True(MeasGuestRules.ReputationRed(4.5m, null, 100, 79));
        Assert.False(MeasGuestRules.ReputationRed(4.5m, null, 100, 80));
        Assert.False(MeasGuestRules.ReputationRed(4.5m, null, 0, 0));
    }

    [Fact]
    public void Desk_upsell_and_repeat_red_boundaries()
    {
        Assert.True(MeasGuestRules.DeskRed(9, null, 8, 6));
        Assert.False(MeasGuestRules.DeskRed(8, 6, 8, 6));
        Assert.True(MeasGuestRules.DeskRed(null, 7, 8, 6));
        Assert.True(MeasGuestRules.UpsellRed(1, 99999, MeasGuestRules.UpsellTargetDefault));
        Assert.True(MeasGuestRules.UpsellRed(0, 4999, MeasGuestRules.UpsellTargetDefault));
        Assert.False(MeasGuestRules.UpsellRed(0, 5000, MeasGuestRules.UpsellTargetDefault));
        Assert.True(MeasGuestRules.RepeatFalling(25, 50, 75));
        Assert.False(MeasGuestRules.RepeatFalling(50, 50, 75));
        Assert.False(MeasGuestRules.RepeatFalling(null, 50, 75));
        var stays = new List<(string, string)>();
        foreach (var p in new[] { "P1", "P2", "P3", "P4" }) stays.Add((p, "2030-07-05"));
        foreach (var p in new[] { "P1", "P2", "P3", "N1" }) stays.Add((p, "2030-08-10"));
        foreach (var p in new[] { "P1", "P2", "N2", "N3" }) stays.Add((p, "2030-09-10"));
        foreach (var p in new[] { "P1", "N4", "N5", "N6" }) stays.Add((p, "2030-10-10"));
        Assert.True(RepeatLoyaltyReport.Falling(stays, "2030-10"));
        Assert.False(RepeatLoyaltyReport.Falling(stays, "2030-09"), "75 then 50 only: needs three months");
    }

    [Fact]
    public void An_empty_result_carries_the_capture_needed_note()
    {
        Assert.Contains("Capture needed: gsv.survey (meas-cap-guest)", MeasGuestRules.CapNote("Basis.", "gsv.survey (meas-cap-guest)"));
    }
}
