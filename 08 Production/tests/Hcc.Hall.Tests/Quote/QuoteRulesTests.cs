using Hcc.Domain.Hall;
using Hcc.Domain.Money;

namespace Hcc.Hall.Tests.Quote;

/// <summary>F6 H2 quotation + T&amp;C rules without a database (same truth table as the prototype's tests/stream-hall-quote.mjs).</summary>
public sealed class QuoteRulesTests
{
    private static HallLine L(string kind, int qty, long list, long rate) => new("L", kind, "x", qty, "each", new Paise(rate), new Paise(list), Paise.Zero, HallBilling.ClassService);

    private static IReadOnlyList<HallLine> Golden(int pct) =>
        [L("HALL", 1, 1_500_000, 1_500_000 - 15_000 * pct), L("MENU", 150, 65_000, 65_000 - 650 * pct), L("EXTRA", 1, 1_000_000, 1_000_000 - 10_000 * pct)];

    [Theory]
    [InlineData("2026-11-03", "2627")]
    [InlineData("2027-03-31", "2627")]
    [InlineData("2027-04-01", "2728")]
    [InlineData("2099-12-31", "9900")]
    public void Fy_code_restarts_on_1_April(string date, string fy) => Assert.Equal(fy, HallQuoteRules.FyCode(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Number_format_is_QT_fy_four_digits() => Assert.Equal("QT/2627/0001", HallQuoteRules.Format("QT", "2627", 1));

    [Fact]
    public void Discount_limits_Reception_0_Manager_10_Owner_any()
    {
        Assert.True(HallQuoteRules.NeedsApproval(Golden(1), "Reception"));
        Assert.False(HallQuoteRules.NeedsApproval(Golden(0), "Reception"));
        Assert.False(HallQuoteRules.NeedsApproval(Golden(10), "Manager"));
        Assert.True(HallQuoteRules.NeedsApproval(Golden(11), "Manager"));
        Assert.False(HallQuoteRules.NeedsApproval(Golden(40), "Owner"));
        Assert.True(HallQuoteRules.NeedsApproval(Golden(1), "Accountant"));
    }

    [Fact]
    public void Status_machine_revise_supersedes_only_a_sent_version()
    {
        Assert.True(HallQuoteRules.CanEdit("DRAFT"));
        Assert.False(HallQuoteRules.CanEdit("SENT"));
        Assert.True(HallQuoteRules.CanRevise("SENT") && HallQuoteRules.CanRevise("EXPIRED"));
        Assert.False(HallQuoteRules.CanRevise("DRAFT"));
        Assert.Equal("SUPERSEDED", HallQuoteRules.StatusAfterRevise("SENT"));
        Assert.Equal("EXPIRED", HallQuoteRules.StatusAfterRevise("EXPIRED"));
        Assert.True(HallQuoteRules.CanCancel("DRAFT"));
        Assert.False(HallQuoteRules.CanCancel("SENT"));
        Assert.False(HallQuoteRules.CanLose("ACCEPTED"));
    }

    [Fact]
    public void Expiry_after_15_days_and_reminder_2_days_before()
    {
        var sent = new DateOnly(2026, 10, 24);
        var until = HallQuoteRules.ValidUntil(sent);
        Assert.Equal(new DateOnly(2026, 11, 8), until);
        Assert.False(HallQuoteRules.Expired("SENT", until, until));
        Assert.True(HallQuoteRules.Expired("SENT", until, until.AddDays(1)));
        Assert.False(HallQuoteRules.Expired("DRAFT", until, until.AddDays(1)));
        Assert.True(HallQuoteRules.ExpiryReminder("SENT", until, until.AddDays(-2)));
        Assert.False(HallQuoteRules.ExpiryReminder("SENT", until, until.AddDays(-3)));
    }

    [Fact]
    public void Templates_general_fallback_version_in_force_and_placeholder_guard()
    {
        var real = new HallTncTemplate("T-WEDDING", "WEDDING",
            [new HallTncVersion(1, new DateOnly(2026, 4, 1), [new HallTncClause(1, "Advance", "25 % on booking.")]),
             new HallTncVersion(2, new DateOnly(2026, 12, 1), [new HallTncClause(1, "Advance", "30 % on booking.")])]);
        IReadOnlyList<HallTncTemplate> list = [HallQuoteRules.DefaultTemplate(), real];
        var gen = HallQuoteRules.Snapshot(list, "BIRTHDAY", new DateOnly(2026, 11, 3));
        Assert.Equal("T-GEN", gen.TemplateCode);
        Assert.True(gen.HasPlaceholder);
        Assert.Equal(10, gen.Clauses.Count);
        var w = HallQuoteRules.Snapshot(list, "WEDDING", new DateOnly(2026, 11, 3));
        Assert.Equal((1, false), (w.V, w.HasPlaceholder));
        Assert.Equal(2, HallQuoteRules.Snapshot(list, "WEDDING", new DateOnly(2026, 12, 1)).V);
        Assert.True(HallQuoteRules.Snapshot([], "WEDDING", new DateOnly(2026, 11, 3)).HasPlaceholder);
        Assert.False(HallQuoteRules.Final(true, false));
        Assert.True(HallQuoteRules.Final(true, true));
        Assert.True(HallQuoteRules.Final(false, false));
    }

    [Fact]
    public void Template_roles_and_clause_validation()
    {
        Assert.True(HallQuoteRules.MayEditTemplates("Owner"));
        Assert.False(HallQuoteRules.MayEditTemplates("Manager"));
        Assert.False(HallQuoteRules.MayEditTemplates("Reception"));
        Assert.False(HallQuoteRules.MayQuote("Cashier"));
        Assert.Equal("Give at least one clause.", HallQuoteRules.ClausesProblem([]));
        Assert.Equal("Clause 1: text 3–2000 characters.", HallQuoteRules.ClausesProblem([("Heading", "x")]));
        Assert.Null(HallQuoteRules.ClausesProblem([("Heading", "Some text")]));
    }
}
