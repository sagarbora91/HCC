using Hcc.Operations.Meas;

namespace Hcc.Operations.Tests.Meas;

/// <summary>[F12-CAP-PEOPLE] people-capture rules (mirror of tests/stream-meas-people.mjs). No database.</summary>
public sealed class PeopleRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Theory]
    [InlineData("2026-04-01", "2026-27-Q1")]
    [InlineData("2026-12-31", "2026-27-Q3")]
    [InlineData("2027-03-31", "2026-27-Q4")]
    [InlineData("2027-04-01", "2027-28-Q1")]
    public void F12_CAP_PEOPLE_Quarter_key_is_the_financial_year_quarter(string date, string expected) =>
        Assert.Equal(expected, PeopleRules.QuarterKey(DateOnly.Parse(date)));

    [Fact]
    public void Training_rules_refuse_short_course_future_date_bad_hours_and_valid_to_before_date()
    {
        Assert.Null(PeopleRules.CheckTraining("Fire safety", Today, Today, 4, Today.AddDays(20)));
        Assert.NotNull(PeopleRules.CheckTraining(" ", Today, Today, 4, null));
        Assert.NotNull(PeopleRules.CheckTraining("Fire", Today.AddDays(1), Today, 4, null));
        Assert.NotNull(PeopleRules.CheckTraining("Fire", Today, Today, 0, null));
        Assert.NotNull(PeopleRules.CheckTraining("Fire", Today, Today, 200.5m, null));
        Assert.NotNull(PeopleRules.CheckTraining("Fire", Today, Today, 4, Today.AddDays(-1)));
    }

    [Fact]
    public void Exit_rules_use_the_fixed_reason_list_and_date_window()
    {
        Assert.Null(PeopleRules.CheckExit("Resigned", Today, new DateOnly(2024, 1, 1), Today));
        Assert.NotNull(PeopleRules.CheckExit("Bored", Today, null, Today));
        Assert.NotNull(PeopleRules.CheckExit("Resigned", new DateOnly(2023, 1, 1), new DateOnly(2024, 1, 1), Today));
        Assert.NotNull(PeopleRules.CheckExit("Resigned", Today.AddDays(32), null, Today));
        Assert.Equal(11, PeopleRules.ExitReasons.Count);
    }

    [Fact]
    public void Pulse_scores_are_one_to_five_and_recommend_zero_to_ten()
    {
        Assert.Null(PeopleRules.CheckPulse(1, 5, 3, 2, 0));
        Assert.Null(PeopleRules.CheckPulse(1, 5, 3, 2, 10));
        Assert.NotNull(PeopleRules.CheckPulse(0, 5, 3, 2, 5));
        Assert.NotNull(PeopleRules.CheckPulse(1, 6, 3, 2, 5));
        Assert.NotNull(PeopleRules.CheckPulse(1, 5, 3, 2, 11));
    }

    [Fact]
    public void A_pulse_answer_has_no_staff_user_or_actor_member()
    {
        var names = typeof(PeopleRules.PulseAnswer).GetProperties().Select(p => p.Name).ToArray();
        Assert.Equal(["QuarterKey", "DeptCode", "Workload", "Respect", "Tools", "PayFairness", "Recommend"], names.Where(n => n != "EqualityContract").ToArray());
        Assert.DoesNotContain(names, n => n.Contains("Staff", StringComparison.OrdinalIgnoreCase) || n.Contains("User", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Actor", StringComparison.OrdinalIgnoreCase) || n.Contains("By", StringComparison.Ordinal));
    }
}
