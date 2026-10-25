using Hcc.Application.WebBooking;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;
using Hcc.Domain.WebBooking;
using Hcc.WebBooking.Tests.Harness;

namespace Hcc.WebBooking.Tests.Kernel;

/// <summary>Booking-website rules without a database (prototype CHN_WEB_REQUEST field checks, deposit share, tokens, rate limiter).</summary>
public sealed class RulesTests
{
    [Fact]
    public void Field_rules_follow_the_prototype()
    {
        Assert.Equal("Meher Ansari", WebBookingRules.Guest("  Meher   Ansari "));
        Assert.Throws<HccException>(() => WebBookingRules.Guest("M"));
        Assert.Equal("9800012345", WebBookingRules.Phone("9800012345"));
        Assert.Throws<HccException>(() => WebBookingRules.Phone("12345"));
        Assert.Throws<HccException>(() => WebBookingRules.Phone("98000-abc-123"));
        Assert.Null(WebBookingRules.Email("  "));
        Assert.Throws<HccException>(() => WebBookingRules.Email("not-an-email"));
        Assert.Equal(200, WebBookingRules.Notes(new string('x', 500))!.Length);
        Assert.Equal("a b", WebBookingRules.Notes("a\u0000\u0007b"));
        Assert.Throws<HccException>(() => WebBookingRules.EnsureHuman("http://spam"));
        Assert.Throws<HccException>(() => WebBookingRules.Reason("no"));
    }

    [Fact]
    public void Phone_is_masked_to_the_last_four_digits()
    {
        Assert.Equal("••••••••2345", WebBookingRules.MaskPhone("+91 98000 12345").Replace("•••", "•••"));
        Assert.EndsWith("2345", WebBookingRules.MaskPhone("+91 98000 12345"));
        Assert.DoesNotContain("9800", WebBookingRules.MaskPhone("+91 98000 12345"));
    }

    [Fact]
    public void Deposit_is_a_share_of_the_total_rounded_half_away_from_zero()
    {
        Assert.Equal(283_500, WebBookingRules.DefaultDeposit(new Paise(945_000), 3000).Value);   // golden: Suite 2 nights ₹9,450 → 30 % = ₹2,835
        Assert.Equal(1, WebBookingRules.DefaultDeposit(new Paise(1), 1).Value);                    // never zero
        Assert.Equal(500_000, WebBookingRules.DepositAmount(500_000, new Paise(945_000), 3000).Value);
        Assert.Throws<HccException>(() => WebBookingRules.DepositAmount(945_001, new Paise(945_000), 3000));
        Assert.Throws<HccException>(() => WebBookingRules.DepositAmount(0, new Paise(945_000), 3000));
    }

    [Fact]
    public void Tokens_are_unguessable_and_hashed_not_stored()
    {
        var a = WebBookingRules.NewToken();
        var b = WebBookingRules.NewToken();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 40);
        Assert.Equal(32, WebBookingRules.TokenHash(a).Length);
        Assert.NotEqual(WebBookingRules.TokenHash(a), WebBookingRules.TokenHash(b));
        Assert.Throws<HccException>(() => WebBookingRules.TokenHash("short"));
    }

    [Fact]
    public void Step_keys_stay_within_the_idempotency_limit()
    {
        Assert.Equal("abc:q", WebBookingRules.StepKey("abc", "q"));
        Assert.True(WebBookingRules.StepKey(new string('k', 80), "r").Length <= 80);
        Assert.Equal(WebBookingRules.StepKey(new string('k', 80), "r"), WebBookingRules.StepKey(new string('k', 80), "r"));
    }

    [Fact]
    public void Rate_limiter_counts_per_client_and_resets_after_the_window()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero));
        var limiter = new PublicRateLimiter(clock);
        for (var i = 0; i < 5; i++) Assert.True(limiter.TryAcquire("q", "1.1.1.1", 5, TimeSpan.FromMinutes(1), out _));
        Assert.False(limiter.TryAcquire("q", "1.1.1.1", 5, TimeSpan.FromMinutes(1), out var retry));
        Assert.True(retry > TimeSpan.Zero && retry <= TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire("q", "2.2.2.2", 5, TimeSpan.FromMinutes(1), out _));       // other client unaffected
        Assert.True(limiter.TryAcquire("other", "1.1.1.1", 5, TimeSpan.FromMinutes(1), out _));   // other bucket unaffected
        clock.Now = clock.Now.AddSeconds(61);
        Assert.True(limiter.TryAcquire("q", "1.1.1.1", 5, TimeSpan.FromMinutes(1), out _));
    }
}
