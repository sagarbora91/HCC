using Hcc.Access.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;

namespace Hcc.Access.Tests.Sql;

/// <summary>DEC-062 sign-in (password roles vs PIN + device), lockout, opaque sessions (idle/absolute), sign-out, AS-07 revocation.</summary>
[Trait("Category", "Sql")]
public class SignInAndSessionTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    private const string OwnerPassword = "Sand-and-Maroon-26";
    private const string Pin = "482913";

    private static string Reason(HccException ex) => ex.Details is { } d && d.TryGetValue("reason", out var r) ? r.GetString()! : string.Empty;

    private static string SessionEnd(HccException ex) => ex.Details!["sessionEnd"].GetString()!;

    [Fact]
    public async Task Owner_signs_in_with_password_and_gets_an_opaque_session()
    {
        var clock = FakeClock.At2026();
        var m = db.Module(clock);
        var id = await db.SeedUserAsync("owner.pw", ["OWN"], password: OwnerPassword);
        var (session, token) = await m.Sessions.SignInAsync(new SignInRequest("  Owner.PW ", Password: OwnerPassword), null);
        Assert.Equal((id, CredentialKind.Password, (int?)null), (session.UserId, session.Kind, session.DeviceId));
        Assert.True(token.Length >= 40);
        Assert.Equal(clock.Now.UtcDateTime.AddMinutes(30), session.IdleExpiresUtc);
        Assert.Equal(clock.Now.UtcDateTime.AddHours(12), session.AbsoluteExpiresUtc);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.session WHERE token_sha256 = @h;", ("@h", Hcc.Infrastructure.SqlServer.Access.AccessTokens.Sha256(token))));
        Assert.Equal(id, (await m.Sessions.ValidateAsync(token)).UserId);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE principal_id = @u AND action = 'SIGN_IN';", ("@u", id)));
    }

    [Theory]
    [InlineData("OWN")]
    [InlineData("ACC")]
    [InlineData("TAD")]
    public async Task Owner_Accountant_TechAdmin_PIN_sign_in_is_refused_even_on_a_registered_device_with_a_PIN_on_file(string role)
    {
        var m = db.Module(FakeClock.At2026());
        var login = "pinrefused." + role.ToLowerInvariant();
        await db.SeedUserAsync(login, [role], password: OwnerPassword, pin: Pin);   // PIN row forced in directly: sign-in must still refuse
        var (_, device) = await db.SeedDeviceAsync();
        var ex = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest(login, Pin: Pin), device));
        Assert.Equal((HccErrorCode.FORBIDDEN, "WRONG_CREDENTIAL_KIND"), (ex.Code, Reason(ex)));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.session s JOIN plat.app_user u ON u.user_id = s.user_id WHERE u.login_name = @l;", ("@l", login)));
    }

    [Fact]
    public async Task Staff_sign_in_by_PIN_only_on_a_registered_unrevoked_device()
    {
        var m = db.Module(FakeClock.At2026());
        await db.SeedUserAsync("rec.pin", ["REC"], pin: Pin);
        var (deviceId, device) = await db.SeedDeviceAsync();

        var noDevice = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("rec.pin", Pin: Pin), null));
        Assert.Equal("DEVICE_NOT_REGISTERED", Reason(noDevice));
        var unknown = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("rec.pin", Pin: Pin), "not-a-registered-device"));
        Assert.Equal("DEVICE_NOT_REGISTERED", Reason(unknown));

        var (session, _) = await m.Sessions.SignInAsync(new SignInRequest("rec.pin", Pin: Pin), device);
        Assert.Equal((CredentialKind.Pin, (int?)deviceId), (session.Kind, session.DeviceId));

        // Staff cannot fall back to a password (DEC-062).
        var pw = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("rec.pin", Password: "Some-long-password-1"), null));
        Assert.Equal("WRONG_CREDENTIAL_KIND", Reason(pw));

        await db.ScalarAsync("UPDATE plat.device SET revoked_utc = '2026-10-01' WHERE device_id = @d;", ("@d", deviceId));
        var revoked = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("rec.pin", Pin: Pin), device));
        Assert.Equal("DEVICE_NOT_REGISTERED", Reason(revoked));
    }

    [Fact]
    public async Task Lockout_after_five_wrong_PINs_then_unlocks_after_fifteen_minutes()
    {
        var clock = FakeClock.At2026();
        var m = db.Module(clock);
        var id = await db.SeedUserAsync("hkp.lock", ["HKP"], pin: Pin);
        var (_, device) = await db.SeedDeviceAsync();
        for (var i = 0; i < 5; i++)
        {
            var bad = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("hkp.lock", Pin: "305718"), device));
            Assert.Equal("BAD_CREDENTIALS", Reason(bad));
        }

        var locked = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("hkp.lock", Pin: Pin), device));
        Assert.Equal("LOCKED_OUT", Reason(locked));   // even the correct PIN is refused while locked
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal("LOCKED_OUT", Reason(await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("hkp.lock", Pin: Pin), device))));
        clock.Advance(TimeSpan.FromMinutes(1));
        var (session, _) = await m.Sessions.SignInAsync(new SignInRequest("hkp.lock", Pin: Pin), device);
        Assert.Equal(id, session.UserId);
        Assert.Equal(0, await db.CountAsync("SELECT failed_count FROM plat.credential WHERE user_id = @u;", ("@u", id)));
        Assert.Equal(7, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE principal_id = @u AND action = 'SIGN_IN_FAILED';", ("@u", id)));
    }

    [Fact]
    public async Task Four_failures_then_success_resets_the_counter()
    {
        var m = db.Module(FakeClock.At2026());
        var id = await db.SeedUserAsync("acc.reset", ["ACC"], password: OwnerPassword);
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("acc.reset", Password: "Wrong-password-123"), null));
        Assert.Equal(4, await db.CountAsync("SELECT failed_count FROM plat.credential WHERE user_id = @u;", ("@u", id)));
        await m.Sessions.SignInAsync(new SignInRequest("acc.reset", Password: OwnerPassword), null);
        Assert.Equal(0, await db.CountAsync("SELECT failed_count FROM plat.credential WHERE user_id = @u;", ("@u", id)));
    }

    [Fact]
    public async Task Unknown_user_disabled_user_and_input_errors()
    {
        var m = db.Module(FakeClock.At2026());
        var unknown = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("nobody.here", Password: OwnerPassword), null));
        Assert.Equal("BAD_CREDENTIALS", Reason(unknown));
        var id = await db.SeedUserAsync("disabled.own", ["OWN"], password: OwnerPassword);
        await db.ScalarAsync("UPDATE plat.app_user SET enabled = 0 WHERE user_id = @u;", ("@u", id));
        var disabled = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("disabled.own", Password: OwnerPassword), null));
        Assert.Equal(unknown.Message, disabled.Message);   // does not reveal that the account exists
        var both = await Assert.ThrowsAsync<HccException>(() => m.Sessions.SignInAsync(new SignInRequest("disabled.own", Password: OwnerPassword, Pin: Pin), null));
        Assert.Equal(HccErrorCode.VALIDATION, both.Code);
    }

    [Fact]
    public async Task Idle_timeout_30_minutes_and_activity_extends_it()
    {
        var clock = FakeClock.At2026();
        var m = db.Module(clock);
        await db.SeedUserAsync("idle.own", ["OWN"], password: OwnerPassword);
        var (_, token) = await m.Sessions.SignInAsync(new SignInRequest("idle.own", Password: OwnerPassword), null);
        clock.Advance(TimeSpan.FromMinutes(29));
        await m.Sessions.ValidateAsync(token);            // activity at 29 min
        clock.Advance(TimeSpan.FromMinutes(29));
        await m.Sessions.ValidateAsync(token);            // 58 min since sign-in, 29 idle: alive
        clock.Advance(TimeSpan.FromMinutes(30));
        var ex = await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(token));
        Assert.Equal((HccErrorCode.FORBIDDEN, "IDLE_TIMEOUT"), (ex.Code, SessionEnd(ex)));
        Assert.Equal("REVOKED", SessionEnd(await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(token))));
    }

    [Fact]
    public async Task Absolute_lifetime_12_hours_even_with_constant_activity()
    {
        var clock = FakeClock.At2026();
        var m = db.Module(clock);
        await db.SeedUserAsync("abs.own", ["OWN"], password: OwnerPassword);
        var (_, token) = await m.Sessions.SignInAsync(new SignInRequest("abs.own", Password: OwnerPassword), null);
        for (var i = 0; i < 47; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(15));
            await m.Sessions.ValidateAsync(token);       // up to 11 h 45 min
        }

        clock.Advance(TimeSpan.FromMinutes(15));          // 12 h
        Assert.Equal("ABSOLUTE_TIMEOUT", SessionEnd(await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(token))));
    }

    [Fact]
    public async Task Sign_out_revokes_the_session()
    {
        var m = db.Module(FakeClock.At2026());
        var id = await db.SeedUserAsync("out.own", ["OWN"], password: OwnerPassword);
        var (_, token) = await m.Sessions.SignInAsync(new SignInRequest("out.own", Password: OwnerPassword), null);
        await m.Sessions.SignOutAsync(token);
        Assert.Equal("REVOKED", SessionEnd(await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(token))));
        Assert.Equal("NO_SESSION", SessionEnd(await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync("garbage"))));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE principal_id = @u AND action = 'SIGN_OUT';", ("@u", id)));
    }

    [Fact]
    public async Task AS07_disable_role_change_and_device_revocation_end_sessions_on_the_next_request()
    {
        var clock = FakeClock.At2026();
        var m = db.Module(clock);
        var ownerA = await db.SeedUserAsync("as07.owner", ["OWN"], password: OwnerPassword);
        await db.SeedUserAsync("as07.owner2", ["OWN"], password: OwnerPassword);
        var (owner, ownerToken) = await m.Sessions.SignInAsync(new SignInRequest("as07.owner", Password: OwnerPassword), null);
        var (deviceId, device) = await db.SeedDeviceAsync("Housekeeping phone");

        // Disable → next request fails.
        var rec = await db.SeedUserAsync("as07.rec", ["REC"], pin: Pin);
        var (_, recToken) = await m.Sessions.SignInAsync(new SignInRequest("as07.rec", Pin: Pin), device);
        await m.Users.SetUserEnabledAsync(owner, "k-disable", new SetUserEnabledRequest(rec, false, await db.VersionOfAsync(rec)));
        Assert.Equal("USER_DISABLED", SessionEnd(await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(recToken))));

        // Role change → next request fails.
        var hkp = await db.SeedUserAsync("as07.hkp", ["HKP"], pin: Pin);
        var (_, hkpToken) = await m.Sessions.SignInAsync(new SignInRequest("as07.hkp", Pin: Pin), device);
        await m.Users.AssignRoleAsync(owner, "k-role", new AssignRoleRequest(hkp, "SUP", null, null, await db.VersionOfAsync(hkp)));
        Assert.Equal("ACCESS_CHANGED", SessionEnd(await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(hkpToken))));

        // Device revoked → PIN sessions on it fail; the Owner's password session is unaffected.
        var (_, hkpToken2) = await m.Sessions.SignInAsync(new SignInRequest("as07.hkp", Pin: Pin), device);
        var revoked = await m.Users.RevokeDeviceAsync(owner, "k-dev", new RevokeDeviceRequest(deviceId));
        Assert.Equal(1, revoked.Value.SessionsRevoked);
        await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(hkpToken2));
        Assert.Equal(ownerA, (await m.Sessions.ValidateAsync(ownerToken)).UserId);
    }

    [Fact]
    public async Task Changing_own_PIN_keeps_this_session_but_ends_others_and_refuses_reuse()
    {
        var m = db.Module(FakeClock.At2026());
        var id = await db.SeedUserAsync("own.pin", ["CSH"], pin: Pin);
        var (_, device) = await db.SeedDeviceAsync();
        var (s1, t1) = await m.Sessions.SignInAsync(new SignInRequest("own.pin", Pin: Pin), device);
        var (_, t2) = await m.Sessions.SignInAsync(new SignInRequest("own.pin", Pin: Pin), device);
        var wrong = await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(s1, "k1", new ChangeOwnCredentialRequest("000000", null, "305718")));
        Assert.Equal(HccErrorCode.FORBIDDEN, wrong.Code);
        await m.Users.ChangeOwnCredentialAsync(s1, "k2", new ChangeOwnCredentialRequest(Pin, null, "305718"));
        await m.Sessions.ValidateAsync(t1);
        Assert.Equal("ACCESS_CHANGED", SessionEnd(await Assert.ThrowsAsync<HccException>(() => m.Sessions.ValidateAsync(t2))));
        await m.Sessions.SignInAsync(new SignInRequest("own.pin", Pin: "305718"), device);
        // Back to a PIN from the history (305718 is current, history holds it) → refused.
        var reuse = await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(s1, "k3", new ChangeOwnCredentialRequest("305718", null, "305718")));
        Assert.Equal(HccErrorCode.VALIDATION, reuse.Code);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE principal_id = @u AND action = 'CREDENTIAL_CHANGE';", ("@u", id)));
    }
}
