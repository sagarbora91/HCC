using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Domain.Groups;
using Hcc.Domain.Money;

namespace Hcc.Groups.Tests.Kernel;

/// <summary>DEC-076 credit rule (prototype grpCreditRefusal: same wording and figures).</summary>
public sealed class CompanyCreditRulesTests
{
    private static Paise R(long rupees) => Paise.FromRupees(rupees);

    [Fact]
    public void Remaining_is_limit_minus_used_and_never_negative()
    {
        Assert.Equal(R(5_700), CompanyCreditRules.Remaining(R(10_000), R(4_300)));
        Assert.Equal(Paise.Zero, CompanyCreditRules.Remaining(R(4_400), R(8_600)));
        Assert.Equal(Paise.Zero, CompanyCreditRules.Remaining(Paise.Zero, Paise.Zero));
    }

    [Fact]
    public void Exactly_the_limit_passes_one_paisa_more_is_refused_with_the_figures()
    {
        CompanyCreditRules.Ensure(7, "Deccan Pharma", R(4_400), R(4_300), R(100), "stay on company credit"); // 4,300 + 100 = 4,400
        var ex = Assert.Throws<HccException>(() => CompanyCreditRules.Ensure(7, "Deccan Pharma", R(4_400), R(4_300), new Paise(10_001), "stay on company credit"));
        Assert.Equal(HccErrorCode.INVALID_TRANSITION, ex.Code);
        Assert.Equal("Credit limit exceeded for Deccan Pharma: limit ₹4,400, used ₹4,300, remaining ₹100; this stay on company credit needs ₹100.01. "
            + "Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).", ex.Message);
        Assert.Equal((7L, 440_000L, 430_000L, 10_000L, 10_001L), (ex.Details!["companyId"].GetInt64(), ex.Details["limitPaise"].GetInt64(), ex.Details["usedPaise"].GetInt64(),
            ex.Details["remainingPaise"].GetInt64(), ex.Details["neededPaise"].GetInt64()));
    }

    [Fact]
    public void A_limit_of_zero_is_no_credit_and_nothing_needed_always_passes()
    {
        Assert.True(CompanyCreditRules.Exceeds(Paise.Zero, Paise.Zero, new Paise(1)));
        Assert.False(CompanyCreditRules.Exceeds(Paise.Zero, R(500), Paise.Zero)); // nothing added: never refused, even over the limit
        Assert.StartsWith("Credit limit exceeded for Kale Textiles: limit ₹0 (no credit), used ₹0, remaining ₹0; this booking needs ₹6,300.",
            CompanyCreditRules.Message("Kale Textiles", Paise.Zero, Paise.Zero, R(6_300), "booking"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_is_refused_only_when_it_raises_the_exposure_beyond_the_limit()
    {
        // Limit Rs 4,400 lowered below an exposure of Rs 8,600: shortening a stay (8,600 → 6,500) passes, extending it (8,600 → 10,700) is refused.
        CompanyCreditRules.EnsureChange(1, "Deccan Pharma", R(4_400), R(8_600), R(6_500), R(2_200), "booking");
        var ex = Assert.Throws<HccException>(() => CompanyCreditRules.EnsureChange(1, "Deccan Pharma", R(4_400), R(8_600), R(10_700), R(6_400), "booking"));
        Assert.Contains("used ₹4,300, remaining ₹100; this booking needs ₹6,400", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Exposure_total_and_per_item_lookup()
    {
        var e = new CompanyExposure(R(100), R(8_500), R(4_200),
        [
            new("receivable", 9, "HCC-1048", R(100)), new("payment", 1, "INV/2627/0001", -R(0)), new("stay", 44, "HCC-1044", R(4_300)),
            new("stay", 48, "HCC-1048", R(4_200)), new("group", 3, "GRP-1001", R(4_200)),
        ]);
        Assert.Equal(R(12_800), e.Total);
        Assert.Equal(R(4_300), e.Of("stay", 44));
        Assert.Equal(Paise.Zero, e.Of("stay", 99));
    }
}
