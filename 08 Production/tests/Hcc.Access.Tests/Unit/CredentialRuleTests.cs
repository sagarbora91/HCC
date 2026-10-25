using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Access;

namespace Hcc.Access.Tests.Unit;

/// <summary>DEC-062 credential kind per role, PIN and password rules, PBKDF2-SHA256, session lifetime, field masks (DEC-061).</summary>
public class CredentialRuleTests
{
    [Theory]
    [InlineData(new[] { "OWN" }, CredentialKind.Password)]
    [InlineData(new[] { "ACC" }, CredentialKind.Password)]
    [InlineData(new[] { "TAD" }, CredentialKind.Password)]
    [InlineData(new[] { "REC", "OWN" }, CredentialKind.Password)]
    [InlineData(new[] { "MGR" }, CredentialKind.Pin)]
    [InlineData(new[] { "REC", "CSH" }, CredentialKind.Pin)]
    [InlineData(new[] { "HKP" }, CredentialKind.Pin)]
    public void DEC062_credential_kind_follows_roles(string[] roles, CredentialKind expected) =>
        Assert.Equal(expected, CredentialPolicy.RequiredKind(roles));

    [Fact]
    public void User_without_roles_cannot_sign_in() => Assert.Null(CredentialPolicy.RequiredKind([]));

    [Theory]
    [InlineData("482913")]
    [InlineData("305718")]
    [InlineData("920461")]
    public void Valid_pins(string pin) => PinRules.Validate(pin);

    [Theory]
    [InlineData("48291")]     // too short
    [InlineData("4829130")]   // too long
    [InlineData("48a913")]    // not digits
    [InlineData("")]
    [InlineData("111111")]    // one digit
    [InlineData("123456")]    // ascending
    [InlineData("987654")]    // descending
    [InlineData("121212")]    // repeated pair
    [InlineData("123123")]    // repeated triple
    [InlineData("123321")]    // mirror
    [InlineData("110011")]    // two digits only
    [InlineData("147258")]    // keypad column
    [InlineData("159753")]    // keypad pattern
    public void Short_or_trivial_pins_are_refused(string pin)
    {
        var ex = Assert.Throws<HccException>(() => PinRules.Validate(pin));
        Assert.Equal(HccErrorCode.VALIDATION, ex.Code);
    }

    [Fact]
    public void Password_rules()
    {
        PasswordRules.Validate("Sand-and-Maroon-26", "owner");
        Assert.Throws<HccException>(() => PasswordRules.Validate("short-pass", "owner"));
        Assert.Throws<HccException>(() => PasswordRules.Validate("my-owner-password", "owner"));
        Assert.Throws<HccException>(() => PasswordRules.Validate("aaaaaaaaaaaaaa", "owner"));
        Assert.Throws<HccException>(() => PasswordRules.Validate(null, "owner"));
    }

    [Fact]
    public void Pbkdf2_sha256_hash_and_verify()
    {
        var hasher = new Pbkdf2SecretHasher();
        var a = hasher.Hash("482913", 1000);
        var b = hasher.Hash("482913", 1000);
        Assert.Equal(("PBKDF2-SHA256", 1000, 16, 32), (a.Algorithm, a.Iterations, a.Salt.Length, a.Hash.Length));
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
        Assert.True(hasher.Verify("482913", a));
        Assert.False(hasher.Verify("482914", a));
        Assert.False(hasher.Verify("482913", a with { Iterations = 1001 }));
        Assert.False(hasher.Verify("482913", a with { Algorithm = "MD5" }));
        // Known-answer: equals Rfc2898DeriveBytes.Pbkdf2 with SHA-256 for the stored salt.
        var expected = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("482913"u8.ToArray(), a.Salt, 1000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        Assert.Equal(expected, a.Hash);
        Assert.Throws<ArgumentOutOfRangeException>(() => hasher.Hash("x", 10));
    }

    [Fact]
    public void Default_options_follow_contracts_and_DEC062()
    {
        var o = new AccessOptions();
        Assert.Equal((TimeSpan.FromMinutes(30), TimeSpan.FromHours(12), 5, TimeSpan.FromMinutes(15), 600_000, 3),
            (o.IdleTimeout, o.AbsoluteLifetime, o.MaxFailedAttempts, o.LockoutDuration, o.PasswordIterations, o.PinHistory));
    }

    [Fact]
    public void Session_lifetime_idle_30_min_absolute_12_h()
    {
        var t0 = new DateTime(2026, 10, 25, 6, 0, 0, DateTimeKind.Utc);
        var idle = TimeSpan.FromMinutes(30);
        var abs = TimeSpan.FromHours(12);
        Assert.True(SessionLifetime.IsAlive(t0, t0, t0.AddMinutes(29).AddSeconds(59), idle, abs));
        Assert.False(SessionLifetime.IsAlive(t0, t0, t0.AddMinutes(30), idle, abs));
        Assert.True(SessionLifetime.IsAlive(t0, t0.AddHours(11).AddMinutes(50), t0.AddHours(11).AddMinutes(59), idle, abs));
        Assert.False(SessionLifetime.IsAlive(t0, t0.AddHours(11).AddMinutes(59), t0.AddHours(12), idle, abs));
    }

    [Fact]
    public void Field_masks_RBAC_section9_and_DEC061_own_pay()
    {
        var hkp = FieldMasks.For(new HashSet<string> { "HKP" });
        Assert.Equal(FieldMasks.Hidden, hkp[FieldMasks.HotelMoney]);
        Assert.Equal(FieldMasks.Restricted, hkp[FieldMasks.GuestIdNumber]);
        Assert.Equal(FieldMasks.OwnOnly, hkp[FieldMasks.StaffPay]);
        Assert.True(FieldMasks.CanSeePayOf(new HashSet<string> { "HKP" }, 12, 12));    // own payslip (DEC-061)
        Assert.False(FieldMasks.CanSeePayOf(new HashSet<string> { "MNT" }, 12, 13));   // never other staff pay
        Assert.True(FieldMasks.CanSeePayOf(new HashSet<string> { "OWN" }, 1, 13));

        var csh = FieldMasks.For(new HashSet<string> { "CSH" });
        Assert.Equal((FieldMasks.Restricted, FieldMasks.Visible, FieldMasks.Visible), (csh[FieldMasks.GuestIdNumber], csh[FieldMasks.GuestContact], csh[FieldMasks.HotelMoney]));
        var acc = FieldMasks.For(new HashSet<string> { "ACC" });
        Assert.Equal((FieldMasks.Last4, FieldMasks.Visible), (acc[FieldMasks.GuestContact], acc[FieldMasks.AuditValues]));
        Assert.All(FieldMasks.For(new HashSet<string> { "OWN" }).Values, v => Assert.Equal(FieldMasks.Visible, v));
        Assert.Equal(FieldMasks.ActionAndTimeOnly, FieldMasks.For(new HashSet<string> { "TAD" })[FieldMasks.AuditValues]);

        Assert.Equal("••••••3210", FieldMasks.Apply(FieldMasks.Last4, "9876543210"));
        Assert.Equal("Restricted", FieldMasks.Apply(FieldMasks.Restricted, "ABCDE1234F"));
        Assert.Null(FieldMasks.Apply(FieldMasks.Hidden, "1500"));
    }
}
