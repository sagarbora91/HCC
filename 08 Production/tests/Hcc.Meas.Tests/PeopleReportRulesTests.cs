// F12-RPT-PEOPLE · the pure rules of the five people measurement reports (boundary tests, same numbers as 03 Prototype/tests/stream-meas-rpt-people.mjs),
// the definitions (ids, columns, flags, gates) and the switch keys against MeasurementCatalogue.
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Reporting.Reports.MeasPeople;

namespace Hcc.Meas.Tests;

public sealed class PeopleReportRulesTests
{
    private static ProdFigures F(long ot = 0, long absentHalves = 0, long lowRoster = 0, long lowRooms = 0) => new(1000, ot, 100, absentHalves, 100, lowRoster, lowRooms);

    [Fact]
    public void Ot_rule_is_red_only_above_the_target()
    {
        Assert.False(PeopleRules.ProductivityRule(F(ot: 100), 10, 5).Ot);
        Assert.True(PeopleRules.ProductivityRule(F(ot: 101), 10, 5).Ot);
    }

    [Fact]
    public void Absenteeism_rule_counts_half_days()
    {
        Assert.False(PeopleRules.ProductivityRule(F(absentHalves: 10), 10, 5).Absent);
        Assert.True(PeopleRules.ProductivityRule(F(absentHalves: 11), 10, 5).Absent);
    }

    [Fact]
    public void Low_night_rostering_is_red_above_the_need_by_twenty_percent()
    {
        Assert.False(PeopleRules.ProductivityRule(F(lowRoster: 120, lowRooms: 10), 10, 5).OverRostered);
        Assert.True(PeopleRules.ProductivityRule(F(lowRoster: 121, lowRooms: 10), 10, 5).OverRostered);
        Assert.False(PeopleRules.ProductivityRule(F(lowRoster: 0, lowRooms: 10), 10, 5).OverRostered);
    }

    [Fact]
    public void Attrition_thirty_percent_is_not_red_forty_is()
    {
        Assert.False(PeopleRules.AttritionRed(3, 10, 10, 30));
        Assert.True(PeopleRules.AttritionRed(4, 10, 10, 30));
        Assert.False(PeopleRules.AttritionRed(0, 0, 0, 30));
    }

    [Fact]
    public void Enps_is_whole_percent_and_red_below_zero()
    {
        Assert.Equal(0, PeopleRules.Enps([9, 9, 6, 6]));
        Assert.Equal(-20, PeopleRules.Enps([9, 6, 6, 7, 8]));
        Assert.Equal(40, PeopleRules.Enps([10, 9, 9, 7, 0]));
        Assert.Equal(0, PeopleRules.Enps([]));
        Assert.Equal(5, PeopleRules.PulseMinAnswers);
    }

    [Fact]
    public void Upper_quartile_rule_needs_four_users_a_positive_value_and_a_value_above_q3()
    {
        Assert.False(PeopleRules.AboveUpperQuartile([10, 20, 30], 1000));
        Assert.Equal(30, PeopleRules.NearestRank([10, 20, 30, 40], 0.75));
        Assert.True(PeopleRules.AboveUpperQuartile([10, 20, 30, 40], 40));
        Assert.False(PeopleRules.AboveUpperQuartile([10, 20, 30, 40], 30));
        Assert.False(PeopleRules.AboveUpperQuartile([0, 0, 0, 0], 0));
    }

    [Fact]
    public void Mandatory_certificate_expired_unless_renewed_by_a_later_valid_record()
    {
        var old = new TrainingRec("1", "S1", "Fire safety", new DateOnly(2025, 5, 2), 4, null, new DateOnly(2026, 5, 1), true);
        var asOf = new DateOnly(2026, 10, 24);
        Assert.True(PeopleRules.CertificateExpired([old], old, asOf));
        var renewed = new TrainingRec("2", "S1", "Fire safety", new DateOnly(2026, 9, 1), 4, null, new DateOnly(2027, 9, 1), true);
        Assert.False(PeopleRules.CertificateExpired([old, renewed], old, asOf));
        var today = new TrainingRec("3", "S2", "First aid", new DateOnly(2026, 1, 1), 4, null, asOf, true);
        Assert.False(PeopleRules.CertificateExpired([today], today, asOf));
        Assert.True(PeopleRules.CertificateExpired([today], today, asOf.AddDays(1)));
        Assert.NotNull(PeopleRules.MandatoryKey("FSSAI handler course"));
        Assert.Null(PeopleRules.MandatoryKey("Guest service"));
    }

    [Fact]
    public void Percent_and_shift_helpers_match_the_prototype()
    {
        Assert.Equal(12.5, PeopleRules.Pct1(125, 1000));
        Assert.Equal(0, PeopleRules.Pct1(5, 0));
        Assert.Equal(480, PeopleRules.ShiftMinutesOf("N"));
        Assert.Equal(540, PeopleRules.ShiftMinutesOf("G"));
        Assert.Equal("1.5", PeopleRules.HoursText(90));
    }

    [Fact]
    public void Definitions_carry_the_catalogue_columns_flags_gates_and_switch_keys()
    {
        var want = new (IReportSelector Sel, int Cols, bool Money, string Perm, string Key, ReportDateClass Dc)[]
        {
            (new StaffProductivityReport(null!), 8, false, "PERM-HR-001", "meas.people.productivity", ReportDateClass.Month),
            (new AttritionReport(null!), 8, false, "PERM-HR-001", "meas.people.attrition", ReportDateClass.FinancialYear),
            (new AgentScorecardReport(null!, null!), 14, true, "PERM-RPT-002", "meas.people.scorecard", ReportDateClass.Month),
            (new TrainingReport(null!), 9, false, "PERM-HR-001", "meas.people.training", ReportDateClass.AsOf),
            (new StaffPulseReport(null!, null!), 10, false, "PERM-HR-002", "meas.people.pulse", ReportDateClass.FinancialYear),
        };
        foreach (var (sel, cols, money, perm, key, dc) in want)
        {
            var d = sel.Definition;
            Assert.Equal("people", d.Area);
            Assert.Equal(cols, d.Columns.Count);
            Assert.Equal(money, d.MoneyFlag);
            Assert.Equal([perm], d.ViewPerms);
            Assert.Equal(dc, d.DateClass);
            Assert.Equal("exception", d.Columns[^1].Key);
            Assert.Equal(key, ((IMeasuredReport)sel).MeasKey);
            Assert.Equal(d.Id, MeasurementCatalogue.Item(key)!.ReportId);
        }
    }
}
