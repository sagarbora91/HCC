using Hcc.Domain.Customers;
using Hcc.Domain.Money;
using Hcc.Domain.Stays;

namespace Hcc.Stays.Tests;

/// <summary>LF B2 kernel rules (LF-CONTRACT §7) — the same cases as the prototype's tests/stream-lf-counter.mjs (credit-guest part).</summary>
public class GuestCreditTests
{
    [Fact]
    public void Credit_key_is_the_phone_last_ten_else_the_guest_id()
    {
        Assert.Equal("9820001041", CustomerCreditKey.Of(1, "+91 98200 01041"));
        Assert.Equal("G-12", CustomerCreditKey.Of(12, ""));
        Assert.Equal("1120/UD-9820001041", GuestCreditRules.PartyLedger("9820001041"));
    }

    [Fact]
    public void Refusal_mirrors_company_credit()
    {
        var acc = new GuestCreditAccount(1, new Paise(1_000_000), true, "9820001041");
        Assert.Null(GuestCreditRules.Refusal("Ananya", acc, new Paise(200_000), new Paise(800_000)));
        Assert.StartsWith("Credit limit exceeded for Ananya", GuestCreditRules.Refusal("Ananya", acc, new Paise(200_000), new Paise(800_001)));
        Assert.Contains("no active credit account", GuestCreditRules.Refusal("Ananya", acc with { Active = false }, Paise.Zero, new Paise(1)));
        Assert.Contains("no active credit account", GuestCreditRules.Refusal("Ananya", null, Paise.Zero, new Paise(1)));
    }

    [Fact]
    public void Manager_within_the_limit_owner_beyond_and_ageing()
    {
        var lim = new Paise(1_000_000);
        Assert.True(GuestCreditRules.CanApprove(false, true, lim, lim));
        Assert.False(GuestCreditRules.CanApprove(false, true, new Paise(1_000_001), lim));
        Assert.True(GuestCreditRules.CanApprove(true, false, new Paise(99_000_000), lim));
        Assert.Equal(["0–30", "31–60", "61–90", "90+"], new[] { 30, 31, 90, 91 }.Select(GuestCreditRules.Bucket).ToArray());
    }
}
