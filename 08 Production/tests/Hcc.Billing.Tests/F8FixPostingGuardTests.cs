using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Billing.Tests;

/// <summary>[F8-FIX] Money review M2: the posting-time D7 rule on a split stay's company folio (prototype twin sfAfterCommand, SF-CONTRACT §7.3).</summary>
public class F8FixPostingGuardTests
{
    private static SplitFolioCredit After(long usedWithoutFolio, long folioEstimate, long limit = 1_000_000) =>
        new("Deccan Agro Ltd (fictional)", new Paise(limit), new Paise(usedWithoutFolio), new Paise(folioEstimate));

    [Fact]
    public void A_line_that_takes_the_company_folio_over_the_limit_is_refused_with_the_D7_text()
    {
        var e = Assert.Throws<HccException>(() => CompanyFolioPostingGuard.Check(7, After(0, 1_023_000), new Paise(315_000)));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, e.Code);
        Assert.Equal("CREDIT_LIMIT", e.Details!["reason"].GetString());
        Assert.Equal(1_023_000L, e.Details["neededPaise"].GetInt64());
        Assert.Contains("this split folio needs", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Used_elsewhere_counts_towards_the_limit()
    {
        // Within the limit alone (₹3,740) but the company already owes ₹9,000 elsewhere: 9,000 + 3,740 > 10,000.
        Assert.Throws<HccException>(() => CompanyFolioPostingGuard.Check(7, After(900_000, 374_000), new Paise(315_000)));
        CompanyFolioPostingGuard.Check(7, After(600_000, 374_000), new Paise(315_000)); // 6,000 + 3,740 fits
    }

    [Fact]
    public void A_command_that_does_not_raise_the_folio_is_never_refused_even_when_the_company_is_over()
    {
        // e.g. a discount or a line routed to folio A: the folio's exposure did not rise, so an over-limit company is not a reason to refuse it.
        CompanyFolioPostingGuard.Check(7, After(2_000_000, 315_000), new Paise(315_000));
        CompanyFolioPostingGuard.Check(7, After(2_000_000, 300_000), new Paise(315_000));
    }

    [Fact]
    public void Exactly_at_the_limit_passes()
        => CompanyFolioPostingGuard.Check(7, After(0, 1_000_000), new Paise(315_000));

    [Fact]
    public async Task Without_split_folio_wiring_the_guard_reads_nothing()
    {
        Assert.Null(await CompanyFolioPostingGuard.BeforeAsync(null, null!, 1, 1, TestContext.Current.CancellationToken));
        await CompanyFolioPostingGuard.AfterAsync(null, null!, 1, null, TestContext.Current.CancellationToken);
    }
}
