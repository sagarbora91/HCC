using Hcc.Domain.Compliance;
using Hcc.Domain.Errors;

namespace Hcc.Compliance.Tests.Domain;

/// <summary>Golden due-date rules from the prototype fixture (tests/stream-compliance.mjs) — no database.</summary>
public class ComplianceRulesTests
{
    private static ObligationSchedule S(DueRule r, string freq = ComplianceFrequency.Monthly, string from = "2026-04") =>
        new(freq, from, [new DatedRule(ComplianceRules.RuleOrigin, r)]);

    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd");

    private static readonly ObligationSchedule Tds = S(new DueRule(DueRuleType.Day, 7, 0, new SpecialDate(3, 30)));

    [Fact]
    public void Scc_due_dates_match_the_prototype()
    {
        Assert.Equal(D("2026-10-07"), ComplianceRules.DueDate(Tds, "2026-09"));
        Assert.Equal(D("2026-04-30"), ComplianceRules.DueDate(Tds, "2026-03"));
        Assert.Equal(D("2026-10-11"), ComplianceRules.DueDate(S(new DueRule(DueRuleType.Day, 11)), "2026-09"));
        Assert.Equal(D("2026-10-20"), ComplianceRules.DueDate(S(new DueRule(DueRuleType.Day, 20)), "2026-09"));
        Assert.Equal(D("2026-10-15"), ComplianceRules.DueDate(S(new DueRule(DueRuleType.Day, 15)), "2026-09"));
        var pt = S(new DueRule(DueRuleType.MonthEnd));
        Assert.Equal(D("2026-10-31"), ComplianceRules.DueDate(pt, "2026-09"));
        Assert.Equal(D("2027-02-28"), ComplianceRules.DueDate(pt, "2027-01"));
        Assert.Equal(D("2028-01-31"), ComplianceRules.DueDate(pt, "2027-12"));
        Assert.Equal(D("2026-12-31"), ComplianceRules.DueDate(S(new DueRule(DueRuleType.Fixed, 31, 12), ComplianceFrequency.Annual, "2025-26"), "2025-26"));
    }

    [Fact]
    public void Annual_rule_due_in_January_to_March_falls_after_the_financial_year_ends()
    {
        var g9 = S(new DueRule(DueRuleType.Fixed, 15, 2), ComplianceFrequency.Annual, "2025-26");
        Assert.Equal(D("2027-02-15"), ComplianceRules.DueDate(g9, "2025-26"));
    }

    [Fact]
    public void Only_finished_periods_are_tracked()
    {
        var today = D("2026-10-24");
        Assert.Equal(["2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09"], ComplianceRules.Periods(ComplianceFrequency.Monthly, "2026-04", today));
        Assert.Equal(["2025-26"], ComplianceRules.Periods(ComplianceFrequency.Annual, "2025-26", today));
        Assert.Equal("2026-09", ComplianceRules.DefaultTrackedFrom(ComplianceFrequency.Monthly, today));
        Assert.Equal("2025-26", ComplianceRules.DefaultTrackedFrom(ComplianceFrequency.Annual, today));
        Assert.Equal("2025-26", ComplianceRules.DefaultTrackedFrom(ComplianceFrequency.Annual, D("2027-02-01")));
    }

    [Fact]
    public void Dated_rule_applies_from_its_date_only()
    {
        var epf = new ObligationSchedule(ComplianceFrequency.Monthly, "2026-04", [
            new DatedRule(ComplianceRules.RuleOrigin, new DueRule(DueRuleType.Day, 15)),
            new DatedRule(D("2026-11-01"), new DueRule(DueRuleType.Day, 20))]);
        Assert.Equal(D("2026-10-15"), ComplianceRules.DueDate(epf, "2026-09"));
        Assert.Equal(D("2026-11-15"), ComplianceRules.DueDate(epf, "2026-10"));
        Assert.Equal(D("2026-12-20"), ComplianceRules.DueDate(epf, "2026-11"));
    }

    [Fact]
    public void Status_text_and_labels()
    {
        Assert.Equal("Overdue by 13 days", ComplianceRules.StatusText(ComplianceStatus.Overdue, -13));
        Assert.Equal("Overdue by 1 day", ComplianceRules.StatusText(ComplianceStatus.Overdue, -1));
        Assert.Equal("Due in 7 days", ComplianceRules.StatusText(ComplianceStatus.DueSoon, 7));
        Assert.Equal("Due in 1 day", ComplianceRules.StatusText(ComplianceStatus.DueSoon, 1));
        Assert.Equal("Due today", ComplianceRules.StatusText(ComplianceStatus.DueToday, 0));
        Assert.Equal(ComplianceStatus.DueSoon, ComplianceRules.StatusOf(7, 7));
        Assert.Equal(ComplianceStatus.Upcoming, ComplianceRules.StatusOf(8, 7));
        Assert.Equal("Sep 2026", ComplianceRules.PeriodLabel(ComplianceFrequency.Monthly, "2026-09"));
        Assert.Equal("FY 2025-26", ComplianceRules.PeriodLabel(ComplianceFrequency.Annual, "2025-26"));
    }

    [Fact]
    public void Rule_validation_follows_the_prototype()
    {
        Assert.Throws<HccException>(() => ComplianceRules.ParseRule(ComplianceFrequency.Monthly, "day", 31, null, null, null));
        Assert.Throws<HccException>(() => ComplianceRules.ParseRule(ComplianceFrequency.Monthly, "day", 7, null, 1, 30));
        Assert.Equal(new DueRule(DueRuleType.Day, 7, 0, new SpecialDate(3, 30)), ComplianceRules.ParseRule(ComplianceFrequency.Monthly, "day", 7, null, 3, 30));
        Assert.Equal(new DueRule(DueRuleType.MonthEnd), ComplianceRules.ParseRule(ComplianceFrequency.Monthly, "month-end", null, null, null, null));
        Assert.Throws<HccException>(() => ComplianceRules.ParseRule(ComplianceFrequency.Annual, "fixed", 31, 13, null, null));
        Assert.Throws<HccException>(() => ComplianceRules.CleanReference("ab"));
        Assert.Throws<HccException>(() => ComplianceRules.CleanReference("<script>x</script>"));
        Assert.Equal("ARN-AA2610-0042", ComplianceRules.CleanReference("  ARN-AA2610-0042 "));
        Assert.Throws<HccException>(() => ComplianceRules.CleanReason("no"));
    }

    [Fact]
    public void Default_obligations_are_the_seven_scc_rules()
    {
        Assert.Equal(7, ComplianceDefaults.Obligations.Count);
        Assert.Equal("7th of the next month (Mar period: 30 Apr)", ComplianceRules.RuleText(ComplianceDefaults.Obligations[0].Rule));
        Assert.Equal("Last day of the next month", ComplianceRules.RuleText(ComplianceDefaults.Obligations[6].Rule));
    }
}
