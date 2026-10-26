using System.Net;
using Hcc.Access.Tests.Harness;
using Hcc.Application.Access;

namespace Hcc.Access.Tests.Unit;

/// <summary>F-M02 sign-in throttle: per address, per (address, name), progressive per-name delay only for addresses that failed (A06-06); never locks the Owner out from elsewhere.</summary>
public class SignInThrottleTests
{
    private const string Lan = "192.168.1.50";
    private const string OwnerPc = "127.0.0.1";

    private static (SignInThrottle T, FakeClock Clock) New()
    {
        var clock = FakeClock.At2026();
        return (new SignInThrottle(new AccessOptions(), clock), clock);
    }

    [Fact]
    public void Twenty_failures_from_one_address_block_that_address_for_the_sliding_window_only()
    {
        var (t, clock) = New();
        for (var i = 0; i < 20; i++)
        {
            Assert.True(t.Check(Lan, "user" + i, false).Allowed);   // spraying many names
            t.RecordFailure(Lan, "user" + i);
            clock.Advance(TimeSpan.FromSeconds(10));
        }

        var blocked = t.Check(Lan, "owner", false);
        Assert.False(blocked.Allowed);
        Assert.True(blocked.RetryAfter > TimeSpan.Zero && blocked.RetryAfter <= TimeSpan.FromMinutes(15));
        Assert.True(t.Check("192.168.1.51", "owner", false).Allowed);   // other LAN devices are unaffected
        Assert.True(t.Check(OwnerPc, "owner", true).Allowed);

        clock.Advance(blocked.RetryAfter);   // the oldest failure leaves the window
        Assert.True(t.Check(Lan, "owner", false).Allowed);
    }

    [Fact]
    public void Five_failures_for_one_name_block_only_that_address_never_the_owner_elsewhere()
    {
        var (t, clock) = New();
        for (var i = 0; i < 5; i++) t.RecordFailure(Lan, "owner");
        Assert.False(t.Check(Lan, "owner", false).Allowed);
        Assert.False(t.Check(Lan, " OWNER ", false).Allowed);              // same name after normalising
        Assert.True(t.Check(Lan, "reception", false).Allowed);             // the attacker's other names: per-address limit only
        var fromServerPc = t.Check(OwnerPc, "owner", true);
        Assert.True(fromServerPc.Allowed);
        Assert.Equal(TimeSpan.Zero, fromServerPc.Delay);                    // the server PC is never slowed down
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.True(t.Check(Lan, "owner", false).Allowed);
    }

    [Fact]
    public void Failures_for_one_name_from_many_addresses_add_a_progressive_capped_delay_not_a_refusal()
    {
        var (t, _) = New();
        for (var i = 0; i < 5; i++) t.RecordFailure("10.0.0." + i, "owner");
        Assert.Equal(TimeSpan.FromSeconds(1), t.Check("10.0.0.4", "owner", false).Delay);    // an address that took part in the guessing
        t.RecordFailure("10.0.0.5", "owner");
        Assert.Equal(TimeSpan.FromSeconds(2), t.Check("10.0.0.5", "owner", false).Delay);
        Assert.Equal(TimeSpan.FromSeconds(2), t.Check("10.0.0.0", "owner", false).Delay);    // the count is the name's, over all addresses
        for (var i = 6; i < 15; i++) t.RecordFailure("10.0.0." + i, "owner");
        var gate = t.Check("10.0.0.14", "owner", false);
        Assert.True(gate.Allowed);
        Assert.Equal(TimeSpan.FromSeconds(8), gate.Delay);
        Assert.Equal(TimeSpan.Zero, t.Check(OwnerPc, "owner", true).Delay);

        t.RecordSuccess(OwnerPc, "owner");                                   // the real Owner signs in: the delay is cleared
        Assert.Equal(TimeSpan.Zero, t.Check("10.0.0.14", "owner", false).Delay);
    }

    [Fact]
    public void A06_06_the_name_delay_never_slows_down_an_address_that_has_not_failed_for_that_name_itself()
    {
        var (t, clock) = New();
        // Someone who knows the Owner's sign-in name fails 15 times from four LAN addresses (4 + 4 + 4 + 3, under the per-pair limit) …
        for (var i = 0; i < 15; i++) t.RecordFailure("192.168.1." + (50 + i % 4), "owner");
        Assert.Equal(TimeSpan.FromSeconds(8), t.Check("192.168.1.50", "owner", false).Delay);   // the guessing addresses are slowed down …

        // … but the Owner's own phone and laptop, which have not failed, sign in without any wait.
        var phone = t.Check("192.168.1.20", "owner", false);
        Assert.True(phone.Allowed);
        Assert.Equal(TimeSpan.Zero, phone.Delay);
        Assert.Equal(TimeSpan.Zero, t.Check("2001:db8:1:2::/64", "owner", false).Delay);
        Assert.Equal(TimeSpan.Zero, t.Check("192.168.1.50", "reception", false).Delay);         // other names from a guessing address: no delay

        // A typo on the phone makes the phone take part: its next attempt waits (never a refusal); success clears it for everyone.
        t.RecordFailure("192.168.1.20", "owner");
        var retry = t.Check("192.168.1.20", "owner", false);
        Assert.True(retry.Allowed);
        Assert.Equal(TimeSpan.FromSeconds(8), retry.Delay);
        t.RecordSuccess("192.168.1.20", "owner");
        Assert.Equal(TimeSpan.Zero, t.Check("192.168.1.20", "owner", false).Delay);

        // The per-address limit is unchanged: 20 failures from one address still give that address 429 for every name.
        for (var i = 0; i < 20; i++) t.RecordFailure("192.168.1.90", "x" + i);
        Assert.False(t.Check("192.168.1.90", "owner", false).Allowed);
        Assert.True(t.Check("192.168.1.20", "owner", false).Allowed);

        // In memory only: the window passes (or a restart, which is a new instance) and nothing is left.
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.True(t.Check("192.168.1.90", "owner", false).Allowed);
        Assert.Equal(TimeSpan.Zero, new SignInThrottle(new AccessOptions(), clock).Check("192.168.1.50", "owner", false).Delay);
    }

    [Fact]
    public void Success_clears_the_pair_but_not_the_address_budget()
    {
        var (t, _) = New();
        for (var i = 0; i < 4; i++) t.RecordFailure(Lan, "cashier");
        t.RecordSuccess(Lan, "cashier");
        for (var i = 0; i < 4; i++) t.RecordFailure(Lan, "cashier");
        Assert.True(t.Check(Lan, "cashier", false).Allowed);
        for (var i = 0; i < 12; i++) t.RecordFailure(Lan, "x" + i);
        Assert.False(t.Check(Lan, "cashier", false).Allowed);               // 20 failures from the address in all
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20")]
    [InlineData("::ffff:192.168.1.20", "192.168.1.20")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:1111:2222:3333:4444", "2001:db8:1:2::/64")]
    public void Client_address_key_groups_IPv6_by_64(string address, string key) =>
        Assert.Equal(key, ClientAddress.Key(IPAddress.Parse(address)));
}
