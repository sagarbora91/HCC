using Hcc.Operations.Meas;
using static Hcc.Operations.Meas.GuestRules;

namespace Hcc.Meas.Tests;

/// <summary>[F12-CAP-GUEST] Rules of the guest captures; the prototype twin is tests/stream-meas-guest.mjs (same boundaries).</summary>
public class GuestRulesTests
{
    private static SurveyInput Ok(string via = "desk") => new(via, 5, 4, 5, 4, null, 9, "Quiet room", true);

    [Fact]
    public void Survey_valid_input_has_no_problem() => Assert.Null(SurveyProblem(Ok()));

    [Theory]
    [InlineData(0, 4, 5, 4, 9)]
    [InlineData(6, 4, 5, 4, 9)]
    [InlineData(5, 0, 5, 4, 9)]
    [InlineData(5, 4, 6, 4, 9)]
    [InlineData(5, 4, 5, 0, 9)]
    [InlineData(5, 4, 5, 4, 11)]
    [InlineData(5, 4, 5, 4, -1)]
    public void Survey_scores_outside_range_are_refused(int overall, int clean, int staff, int value, int recommend)
    {
        var p = SurveyProblem(new SurveyInput("desk", overall, clean, staff, value, null, recommend, null, true));
        Assert.Equal("MEAS_VALIDATION", p!.Value.Reason);
    }

    [Fact]
    public void Survey_boundaries_are_accepted()
    {
        Assert.Null(SurveyProblem(new SurveyInput("qr", 1, 1, 1, 1, 1, 0, null, true)));
        Assert.Null(SurveyProblem(new SurveyInput("qr", 5, 5, 5, 5, 5, 10, new string('x', CommentMax), true)));
        Assert.Equal("MEAS_VALIDATION", SurveyProblem(Ok() with { Breakfast = 6 })!.Value.Reason);
        Assert.Equal("MEAS_VALIDATION", SurveyProblem(Ok() with { Comment = new string('x', CommentMax + 1) })!.Value.Reason);
        Assert.Equal("MEAS_VALIDATION", SurveyProblem(Ok("sms"))!.Value.Reason);
    }

    [Fact]
    public void Survey_without_consent_is_refused() =>
        Assert.Equal("MEAS_NO_CONSENT", SurveyProblem(Ok() with { Consent = false })!.Value.Reason);

    [Fact]
    public void Survey_comment_with_an_id_number_is_refused()
    {
        Assert.Equal("MEAS_ID_DATA", SurveyProblem(Ok() with { Comment = "passport K1234567 thanks" })!.Value.Reason);
        Assert.Null(SurveyProblem(Ok() with { Comment = "Call 9876543210 for booking HCC-1041" }));
    }

    [Fact]
    public void Survey_input_type_has_no_identity_or_id_member()
    {
        var names = typeof(SurveyInput).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToArray();
        foreach (var banned in new[] { "name", "phone", "email", "address", "idnumber", "idtype", "photo", "aadhaar", "passport" })
            Assert.DoesNotContain(banned, names);
    }

    [Fact]
    public void EnsureSurvey_throws_validation_with_the_meas_reason()
    {
        var e = Assert.Throws<Hcc.Domain.Errors.HccException>(() => EnsureSurvey(Ok() with { Consent = false }));
        Assert.Equal("MEAS_NO_CONSENT", e.Details!["reason"].GetString());
    }

    [Fact]
    public void Qr_rate_limit_is_ten_in_ten_minutes()
    {
        var now = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var nine = Enumerable.Range(0, 9).Select(i => now.AddMinutes(-i * 0.5));
        Assert.False(RateLimited(nine, now));
        var ten = Enumerable.Range(0, 10).Select(i => now.AddMinutes(-i * 0.5));
        Assert.True(RateLimited(ten, now));
        Assert.False(RateLimited(ten.Select(t => t.AddMinutes(-20)), now));
    }

    [Fact]
    public void Nps_matches_the_prototype_cases()
    {
        Assert.Null(Nps([]));
        Assert.Equal(25, Nps([10, 9, 8, 6]));
        Assert.Equal(-67, Nps([0, 6, 7]));
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("26-10")]
    public void Review_month_must_be_yyyy_MM(string month) =>
        Assert.Equal("Month must be yyyy-MM.", ReviewProblem(new ReviewInput(month, "google", 1, 4, null, 1, null)));

    [Fact]
    public void Review_boundaries()
    {
        Assert.Null(ReviewProblem(new ReviewInput("2026-10", "google", 42, 5, 0, 42, "clean")));
        Assert.Null(ReviewProblem(new ReviewInput("2026-10", "agoda", 0, 0, null, 0, null)));
        Assert.NotNull(ReviewProblem(new ReviewInput("2026-10", "tripadvisor", 1, 4, null, 1, null)));
        Assert.NotNull(ReviewProblem(new ReviewInput("2026-10", "google", 1, 5.1m, null, 1, null)));
        Assert.NotNull(ReviewProblem(new ReviewInput("2026-10", "google", 1, 4, 6, 1, null)));
        Assert.NotNull(ReviewProblem(new ReviewInput("2026-10", "google", 42, 4, null, 43, null)));
        Assert.NotNull(ReviewProblem(new ReviewInput("2026-10", "google", 42, 4, null, 1, new string('x', 201))));
        Assert.NotNull(ReviewProblem(new ReviewInput("2026-10", "google", 42, 4, null, 1, "K1234567")));
    }

    [Fact]
    public void Root_cause_list_rules()
    {
        Assert.Null(RootCauseListProblem([new("STAFF", "Staff", true), new("A-1", "x", false)]));
        Assert.NotNull(RootCauseListProblem([]));
        Assert.NotNull(RootCauseListProblem(null));
        Assert.NotNull(RootCauseListProblem([new("a", "x", true)]));
        Assert.NotNull(RootCauseListProblem([new("A", "x", true), new("A", "y", true)]));
        Assert.NotNull(RootCauseListProblem([new("A", " ", true)]));
        Assert.NotNull(RootCauseListProblem([new("A", new string('x', 41), true)]));
        Assert.NotNull(RootCauseListProblem(Enumerable.Range(0, 31).Select(i => new RootCauseRow("C" + i, "x", true)).ToArray()));
    }

    [Fact]
    public void Item_keys_are_the_contract_keys()
    {
        Assert.Equal(["meas.guest.survey", "meas.guest.reputation", "meas.guest.recovery", "meas.guest.repeat"], [ItemSurvey, ItemReputation, ItemRecovery, ItemRepeat]);
    }
}
