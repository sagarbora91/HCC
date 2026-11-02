using Hcc.Domain.GuestServices;

namespace Hcc.GuestServices.Tests.Kernel;

/// <summary>F4-T3 qw-feedback rules, mirroring 03 Prototype/tests/stream-feedback.mjs. No database.</summary>
public class FeedbackRulesTests
{
    [Theory]
    [InlineData(0, false)] [InlineData(1, true)] [InlineData(5, true)] [InlineData(6, false)]
    public void Stars_are_one_to_five(int stars, bool ok) => Assert.Equal(ok, FeedbackRules.StarsValid(stars));

    [Fact]
    public void Low_stars_or_a_categorised_comment_open_a_complaint()
    {
        Assert.True(FeedbackRules.OpensComplaint(1, null, null));
        Assert.True(FeedbackRules.OpensComplaint(2, "", null));
        Assert.False(FeedbackRules.OpensComplaint(4, "Lovely", null));
        Assert.True(FeedbackRules.OpensComplaint(4, "AC was weak", "Maintenance"));
        Assert.False(FeedbackRules.OpensComplaint(4, "AC was weak", "Not a category"));
        Assert.False(FeedbackRules.OpensComplaint(4, "", "Maintenance"));
    }

    [Fact]
    public void Severity_and_average()
    {
        Assert.Equal(["High", "Medium", "Low"], new[] { 1, 2, 3 }.Select(FeedbackRules.Severity));
        Assert.Null(FeedbackRules.Average([]));
        Assert.Equal(3.5, FeedbackRules.Average([3, 4]));
    }

    [Fact]
    public void Seven_days_is_overdue_six_is_not()
    {
        var d = new DateOnly(2026, 10, 24);
        Assert.False(FeedbackRules.IsOverdue(d, d.AddDays(6)));
        Assert.True(FeedbackRules.IsOverdue(d, d.AddDays(7)));
    }
}
