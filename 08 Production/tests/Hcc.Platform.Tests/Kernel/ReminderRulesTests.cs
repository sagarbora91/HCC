using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Reminders;

namespace Hcc.Platform.Tests.Kernel;

/// <summary>LF B4 desk reminder rules (same cases as the prototype's tests/stream-lf-alerts.mjs). No database.</summary>
public class ReminderRulesTests
{
    private static readonly string[] Roles = ["OWN", "MGR", "REC", "CSH", "ACC"];

    [Theory]
    [InlineData("2026-01-31", "monthly", "2026-02-28")]
    [InlineData("2026-12-15", "monthly", "2027-01-15")]
    [InlineData("2028-02-29", "yearly", "2029-02-28")]
    [InlineData("2026-05-05", "none", "2026-05-05")]
    public void A_repeating_reminder_rolls_a_month_or_a_year_and_clamps_the_month_end(string from, string repeat, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), ReminderRules.Roll(DateOnly.Parse(from), repeat));

    [Fact]
    public void A_reminder_shows_from_its_alert_days_and_stays_while_overdue()
    {
        var date = new DateOnly(2026, 10, 28);
        Assert.False(ReminderRules.IsDue("open", date, null, 3, new DateOnly(2026, 10, 24)));
        Assert.True(ReminderRules.IsDue("open", date, null, 3, new DateOnly(2026, 10, 25)));
        Assert.True(ReminderRules.IsDue("open", date, null, 3, new DateOnly(2026, 11, 5)));
        Assert.False(ReminderRules.IsDue("done", date, null, 3, new DateOnly(2026, 10, 28)));
        Assert.Equal(new DateOnly(2026, 11, 28), ReminderRules.DueDate(date, new DateOnly(2026, 11, 28)));
        Assert.Equal(new DateOnly(2026, 11, 25), ReminderRules.ShowFrom(date, new DateOnly(2026, 11, 28), 3));
    }

    [Fact]
    public void Validate_applies_the_defaults_and_refuses_bad_input()
    {
        var v = ReminderRules.Validate("Fire extinguisher refill", "2026-10-28", null, null, null, null, null, 3, Roles);
        Assert.Equal((3, "all", "none"), (v.Lead, v.Role, v.Repeat));
        Assert.Equal(HccErrorCode.VALIDATION, Assert.Throws<HccException>(() => ReminderRules.Validate("x", "2026-10-28", null, null, null, null, null, 3, Roles)).Code);
        Assert.Throws<HccException>(() => ReminderRules.Validate("Subject", "soon", null, null, null, null, null, 3, Roles));
        Assert.Throws<HccException>(() => ReminderRules.Validate("Subject", "2026-10-28", "25:00", null, null, null, null, 3, Roles));
        Assert.Throws<HccException>(() => ReminderRules.Validate("Subject", "2026-10-28", null, 99, null, null, null, 3, Roles));
        Assert.Throws<HccException>(() => ReminderRules.Validate("Subject", "2026-10-28", null, null, "ZZZ", null, null, 3, Roles));
        Assert.Throws<HccException>(() => ReminderRules.Validate("Subject", "2026-10-28", null, null, null, null, "weekly", 3, Roles));
    }

    [Fact]
    public void Dismissal_needs_a_reason_and_only_author_boss_or_addressee_may_act()
    {
        Assert.Throws<HccException>(() => ReminderRules.CleanReason("x"));
        Assert.Equal("Done by the vendor already", ReminderRules.CleanReason(" Done by the vendor already "));
        Assert.True(ReminderRules.MayAct(7, 7, "REC", ["CSH"]));
        Assert.True(ReminderRules.MayAct(8, 7, "all", ["CSH"]));
        Assert.True(ReminderRules.MayAct(8, 7, "REC", ["MGR"]));
        Assert.False(ReminderRules.MayAct(8, 7, "REC", ["CSH"]));
    }
}
