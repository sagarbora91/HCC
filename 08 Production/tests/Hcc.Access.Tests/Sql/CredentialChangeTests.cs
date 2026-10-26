using Hcc.Access.Tests.Harness;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;

namespace Hcc.Access.Tests.Sql;

/// <summary>
/// A06-01 (security review F-M03 residual): a password / PIN set by an administrator (SetCredential) is flagged must-change; the
/// sign-in and every validated session report it; the user's own change (ChangeOwnCredential) clears it and never sets it.
/// A02b-05: the self-service change needs the current secret (wrong ones count towards a 15-minute lockout), follows the PIN /
/// password rules and must differ from the current secret. The HTTP gate (403 on every other route) is in Hcc.Integration.Tests.
/// </summary>
[Trait("Category", "Sql")]
public class CredentialChangeTests(AccessDatabase db) : IClassFixture<AccessDatabase>
{
    private const string Pw = "Sand-and-Maroon-26";
    private const string AdminPin = "482913";
    private const string OwnPin = "305718";

    private async Task<int> MustChangeAsync(int userId, string kind) =>
        await db.CountAsync("SELECT COUNT(*) FROM plat.credential WHERE user_id = @u AND kind = @k AND must_change = 1;", ("@u", userId), ("@k", kind));

    private static string Reason(HccException ex) => ex.Details is { } d && d.TryGetValue("reason", out var r) ? r.GetString()! : string.Empty;

    [Fact]
    public async Task Admin_reset_flags_the_credential_sign_in_reports_it_and_the_own_change_clears_it_A06_01()
    {
        var m = db.Module(FakeClock.At2026());
        await db.SeedUserAsync("mc.owner", ["OWN"], password: Pw);
        var owner = await AccessDatabase.SignInPasswordAsync(m, "mc.owner", Pw);
        var rec = await db.SeedUserAsync("mc.rec", ["REC"]);
        var (_, device) = await db.SeedDeviceAsync();

        await m.Users.SetCredentialAsync(owner, "mc-set", new SetCredentialRequest(rec, null, AdminPin, await db.VersionOfAsync(rec)));
        Assert.Equal(1, await MustChangeAsync(rec, "PIN"));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CREDENTIAL_SET' AND target = @t AND detail LIKE '%must be changed at the next sign-in%';", ("@t", $"user:{rec}")));

        // Sign-in, the validated session and the capabilities all carry the flag.
        var (session, token) = await m.Sessions.SignInAsync(new SignInRequest("mc.rec", Pin: AdminPin), device);
        Assert.True(session.MustChangeCredential);
        Assert.True((await m.Sessions.ValidateAsync(token, device, CancellationToken.None)).MustChangeCredential);
        Assert.True((await m.Capabilities.GetAsync(session)).MustChangeCredential);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'SIGN_IN' AND principal_id = @u AND detail LIKE '%must be changed first%';", ("@u", rec)));

        // The user's own change clears it at once for this very session (read live), for new sign-ins and in the database.
        await m.Users.ChangeOwnCredentialAsync(session, "mc-own", new ChangeOwnCredentialRequest(AdminPin, null, OwnPin));
        Assert.Equal(0, await MustChangeAsync(rec, "PIN"));
        var same = await m.Sessions.ValidateAsync(token, device, CancellationToken.None);
        Assert.False(same.MustChangeCredential);
        Assert.False((await m.Capabilities.GetAsync(same)).MustChangeCredential);
        var (again, _) = await m.Sessions.SignInAsync(new SignInRequest("mc.rec", Pin: OwnPin), device);
        Assert.False(again.MustChangeCredential);
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'CREDENTIAL_CHANGE' AND principal_id = @u AND detail LIKE '%replaces the one an administrator set%';", ("@u", rec)));

        // A later administrator reset flags it again (and the admin-set PIN is never accepted back as "own": history).
        await m.Users.SetCredentialAsync(owner, "mc-set2", new SetCredentialRequest(rec, null, "920461", await db.VersionOfAsync(rec)));
        Assert.Equal(1, await MustChangeAsync(rec, "PIN"));
        var (third, _) = await m.Sessions.SignInAsync(new SignInRequest("mc.rec", Pin: "920461"), device);
        Assert.True(third.MustChangeCredential);
        var back = await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(third, "mc-own2", new ChangeOwnCredentialRequest("920461", null, AdminPin)));
        Assert.Equal(HccErrorCode.VALIDATION, back.Code);
        Assert.Equal(1, await MustChangeAsync(rec, "PIN"));
    }

    [Fact]
    public async Task Owner_password_reset_of_an_Accountant_is_flagged_and_cleared_by_their_own_new_password_A06_01()
    {
        var m = db.Module(FakeClock.At2026());
        await db.SeedUserAsync("mcp.owner", ["OWN"], password: Pw);
        var owner = await AccessDatabase.SignInPasswordAsync(m, "mcp.owner", Pw);
        var acc = await db.SeedUserAsync("mcp.acc", ["ACC"], password: Pw);
        Assert.Equal(0, await MustChangeAsync(acc, "PASSWORD"));
        Assert.False((await AccessDatabase.SignInPasswordAsync(m, "mcp.acc", Pw)).MustChangeCredential);

        await m.Users.SetCredentialAsync(owner, "mcp-set", new SetCredentialRequest(acc, "Owner-chosen-pass-7", null, await db.VersionOfAsync(acc)));
        var session = await AccessDatabase.SignInPasswordAsync(m, "mcp.acc", "Owner-chosen-pass-7");
        Assert.True(session.MustChangeCredential);

        // The same password back is no change: refused (the Owner knows it).
        var same = await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(session, "mcp-same", new ChangeOwnCredentialRequest("Owner-chosen-pass-7", "Owner-chosen-pass-7", null)));
        Assert.Equal(HccErrorCode.VALIDATION, same.Code);
        Assert.Contains("different from the current one", same.Message, StringComparison.Ordinal);
        Assert.Equal(1, await MustChangeAsync(acc, "PASSWORD"));

        await m.Users.ChangeOwnCredentialAsync(session, "mcp-own", new ChangeOwnCredentialRequest("Owner-chosen-pass-7", "Accountant-own-pass-9", null));
        Assert.Equal(0, await MustChangeAsync(acc, "PASSWORD"));
        Assert.False((await AccessDatabase.SignInPasswordAsync(m, "mcp.acc", "Accountant-own-pass-9")).MustChangeCredential);
    }

    [Fact]
    public async Task A_users_own_change_never_sets_the_flag_A06_01()
    {
        var m = db.Module(FakeClock.At2026());
        var csh = await db.SeedUserAsync("own.csh", ["CSH"], pin: AdminPin);
        var tad = await db.SeedUserAsync("own.tad", ["TAD"], password: Pw);
        var (_, device) = await db.SeedDeviceAsync();
        var (pinSession, _) = await m.Sessions.SignInAsync(new SignInRequest("own.csh", Pin: AdminPin), device);
        Assert.False(pinSession.MustChangeCredential);
        await m.Users.ChangeOwnCredentialAsync(pinSession, "own-1", new ChangeOwnCredentialRequest(AdminPin, null, OwnPin));
        var pwSession = await AccessDatabase.SignInPasswordAsync(m, "own.tad", Pw);
        await m.Users.ChangeOwnCredentialAsync(pwSession, "own-2", new ChangeOwnCredentialRequest(Pw, "Tech-admin-own-pass-3", null));
        Assert.Equal((0, 0), (await MustChangeAsync(csh, "PIN"), await MustChangeAsync(tad, "PASSWORD")));
        Assert.False((await m.Sessions.SignInAsync(new SignInRequest("own.csh", Pin: OwnPin), device)).Session.MustChangeCredential);
        Assert.False((await AccessDatabase.SignInPasswordAsync(m, "own.tad", "Tech-admin-own-pass-3")).MustChangeCredential);
    }

    [Fact]
    public async Task Wrong_current_secret_is_refused_counted_and_locks_for_15_minutes_with_the_minutes_left_A02b_05()
    {
        var clock = FakeClock.At2026();
        var m = db.Module(clock);
        var id = await db.SeedUserAsync("lock.sup", ["SUP"], pin: AdminPin);
        var (_, device) = await db.SeedDeviceAsync();
        var (session, _) = await m.Sessions.SignInAsync(new SignInRequest("lock.sup", Pin: AdminPin), device);

        for (var i = 0; i < 4; i++)
        {
            var wrong = await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(session, "lk-" + i, new ChangeOwnCredentialRequest("000000", null, OwnPin)));
            Assert.Equal((HccErrorCode.FORBIDDEN, "The current password or PIN is wrong."), (wrong.Code, wrong.Message));
        }

        var fifth = await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(session, "lk-5", new ChangeOwnCredentialRequest("000000", null, OwnPin)));
        Assert.Equal("The current password or PIN is wrong. Too many wrong attempts. Try again in 15 minutes.", fifth.Message);

        // Locked: even the right PIN is refused, with the minutes left; nothing changed.
        clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(10));
        var locked = await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(session, "lk-6", new ChangeOwnCredentialRequest(AdminPin, null, OwnPin)));
        Assert.Equal((HccErrorCode.FORBIDDEN, "Too many wrong attempts. Try again in 1 minute."), (locked.Code, locked.Message));
        Assert.Equal(0, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE principal_id = @u AND action = 'CREDENTIAL_CHANGE';", ("@u", id)));

        clock.Advance(TimeSpan.FromMinutes(1));
        await m.Users.ChangeOwnCredentialAsync(session, "lk-7", new ChangeOwnCredentialRequest(AdminPin, null, OwnPin));
        Assert.Equal(1, await db.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE principal_id = @u AND action = 'CREDENTIAL_CHANGE';", ("@u", id)));
    }

    [Fact]
    public async Task New_secret_follows_the_PIN_and_password_rules_and_the_account_kind_A02b_05()
    {
        var m = db.Module(FakeClock.At2026());
        await db.SeedUserAsync("rule.hkp", ["HKP"], pin: AdminPin);
        await db.SeedUserAsync("rule.own", ["OWN"], password: Pw);
        var (_, device) = await db.SeedDeviceAsync();
        var (pin, _) = await m.Sessions.SignInAsync(new SignInRequest("rule.hkp", Pin: AdminPin), device);
        var pw = await AccessDatabase.SignInPasswordAsync(m, "rule.own", Pw);

        async Task<HccException> Refused(AuthenticatedSession s, string current, string? newPassword, string? newPin) =>
            await Assert.ThrowsAsync<HccException>(() => m.Users.ChangeOwnCredentialAsync(s, "r-" + Guid.NewGuid().ToString("N")[..8], new ChangeOwnCredentialRequest(current, newPassword, newPin)));

        Assert.Contains("exactly 6 digits", (await Refused(pin, AdminPin, null, "30571")).Message, StringComparison.Ordinal);
        Assert.Contains("exactly 6 digits", (await Refused(pin, AdminPin, null, "30a718")).Message, StringComparison.Ordinal);
        Assert.Contains("too easy", (await Refused(pin, AdminPin, null, "123456")).Message, StringComparison.Ordinal);
        Assert.Contains("too easy", (await Refused(pin, AdminPin, null, "121212")).Message, StringComparison.Ordinal);
        Assert.Contains("different from the current one", (await Refused(pin, AdminPin, null, AdminPin)).Message, StringComparison.Ordinal);
        Assert.Contains("does not use a PASSWORD", (await Refused(pin, AdminPin, "A-long-enough-pass-1", null)).Message, StringComparison.Ordinal);
        Assert.Contains("either a password or a PIN", (await Refused(pin, AdminPin, null, null)).Message, StringComparison.Ordinal);
        Assert.Contains("12–128 characters", (await Refused(pw, Pw, "short-pass", null)).Message, StringComparison.Ordinal);
        Assert.Contains("must not contain the sign-in name", (await Refused(pw, Pw, "my-rule.own-password", null)).Message, StringComparison.Ordinal);
        Assert.Contains("too simple", (await Refused(pw, Pw, "abababababab", null)).Message, StringComparison.Ordinal);
        Assert.Contains("does not use a PIN", (await Refused(pw, Pw, null, OwnPin)).Message, StringComparison.Ordinal);
        Assert.All(new[] { await Refused(pin, AdminPin, null, "123456"), await Refused(pw, Pw, "short-pass", null) }, e => Assert.Equal(HccErrorCode.VALIDATION, e.Code));
    }

    [Fact]
    public void Gate_refusal_and_lockout_wording_A06_01()
    {
        var pin = CredentialChangeGate.Refusal(CredentialKind.Pin);
        var pw = CredentialChangeGate.Refusal(CredentialKind.Password);
        Assert.Equal((HccErrorCode.FORBIDDEN, CredentialChangeGate.Reason), (pin.Code, Reason(pin)));
        Assert.StartsWith("Change your PIN first.", pin.Message, StringComparison.Ordinal);
        Assert.StartsWith("Change your password first.", pw.Message, StringComparison.Ordinal);
        Assert.Equal("Too many wrong attempts. Try again in 15 minutes.", OwnCredentialRules.LockedMessage(TimeSpan.FromMinutes(15)));
        Assert.Equal("Too many wrong attempts. Try again in 1 minute.", OwnCredentialRules.LockedMessage(TimeSpan.FromSeconds(5)));
        Assert.Equal("Too many wrong attempts. Try again in 3 minutes.", OwnCredentialRules.LockedMessage(TimeSpan.FromSeconds(121)));
    }
}
