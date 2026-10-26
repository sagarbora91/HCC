using System.Net;
using System.Text.Json;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Integration.Tests.Harness;
using Microsoft.AspNetCore.Builder;

namespace Hcc.Integration.Tests;

/// <summary>
/// A06-01 (security review F-M03 residual) over HTTP with the real composition: after an administrator sets a password / PIN the
/// sign-in says so, every route except GET /me/capabilities, ChangeOwnCredential and sign-out answers 403 MUST_CHANGE_CREDENTIAL
/// ("Change your … first"), health details stay hidden, and the user's own change (A02b-05) lifts the gate for the same session.
/// </summary>
[Trait("Category", "Sql")]
public sealed class CredentialChangeHttpTests
{
    private const string LanHeader = "X-Test-Lan-Client";
    private const string OwnerLogin = "owner.sagar";
    private const string OwnerPassword = "Maroon&Sand#Ledger26";
    private const string FirstPin = "816305";
    private const string OwnPin = "472913";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        SELECT SCOPE_IDENTITY();
        """;

    /// <summary>A request carrying <see cref="LanHeader"/> arrives from that LAN address (not the server PC: no health details by default).</summary>
    private static Task<HccServer> ServerAsync() => HccServer.StartAsync(Seed, Ct, app => app.Use(async (http, next) =>
    {
        if (http.Request.Headers.TryGetValue(LanHeader, out var ip)) http.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
        await next(http);
    }));

    private static async Task<HttpClient> OwnerAsync(HccServer s)
    {
        var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "Sagar (Owner)", OwnerPassword), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);
        using (var r = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: OwnerPassword), null, Ct))
            Assert.False((await Wire.OkAsync<SignInResult>(r, "owner sign-in", Ct)).MustChangeCredential);   // bootstrap: the Owner's own password
        return owner;
    }

    private static async Task<ErrorResponse> ErrorAsync(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<ErrorResponse>(await r.Content.ReadAsStringAsync(Ct), HccServer.Json)!;

    private static async Task AssertGatedAsync(HttpResponseMessage r, string first)
    {
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var e = await ErrorAsync(r);
        Assert.Equal(("FORBIDDEN", "MUST_CHANGE_CREDENTIAL"), (e.Code, e.Details!["reason"].GetString()));
        Assert.StartsWith(first, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_reset_PIN_session_reaches_only_its_profile_the_change_and_sign_out_until_changed_A06_01()
    {
        await using var s = await ServerAsync();
        using var owner = await OwnerAsync(s);
        var rec = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest("rec.asha", "Asha (Reception)", [new RoleAssignmentInput("REC")]), Ct);
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(rec.UserId, null, FirstPin, rec.Version), Ct);
        var device = (await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("Front desk tablet"), Ct)).DeviceToken!;

        using var tablet = s.DeviceClient(device);
        SignInResult signIn;
        using (var r = await Wire.PostAsync(tablet, "/auth/sign-in", new SignInRequest("rec.asha", Pin: FirstPin, ReturnToken: true), null, Ct))
            signIn = await Wire.OkAsync<SignInResult>(r, "PIN sign-in", Ct);
        Assert.Equal(("PIN", true), (signIn.CredentialKind, signIn.MustChangeCredential));
        HccServer.UseBearer(tablet, signIn.SessionToken!);

        // Own profile: allowed, and it says so (the app re-reads it after a restart).
        Assert.True((await Wire.GetAsync<CapabilitiesResult>(tablet, "/me/capabilities", Ct)).MustChangeCredential);
        // Everything else: 403 with the clear message, reads and commands alike (Reception holds PERM-RES-001 normally).
        foreach (var path in new[] { "/reservations?pageSize=5", "/me/directory", "/rooms/status", "/admin/users" })
        {
            using var r = await tablet.GetAsync(ApiConventions.BasePath + path, Ct);
            await AssertGatedAsync(r, "Change your PIN first.");
        }

        using (var r = await Wire.PostAsync(tablet, "/commands/RegisterDevice", new RegisterDeviceRequest("Sneaky"), Wire.NewKey("gate"), Ct))
            await AssertGatedAsync(r, "Change your PIN first.");
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM plat.device;", Ct));

        // The change itself: the current PIN is checked (wrong → refused, not the gate), then the same session is released.
        var wrong = await Wire.CommandFailsAsync(tablet, "ChangeOwnCredential", new ChangeOwnCredentialRequest("000000", null, OwnPin), HttpStatusCode.Forbidden, "FORBIDDEN", Ct);
        Assert.Equal("The current password or PIN is wrong.", wrong.Message);
        await Wire.CommandAsync<UserVersionResult>(tablet, "ChangeOwnCredential", new ChangeOwnCredentialRequest(FirstPin, null, OwnPin), Ct);
        Assert.False((await Wire.GetAsync<CapabilitiesResult>(tablet, "/me/capabilities", Ct)).MustChangeCredential);
        using (var r = await tablet.GetAsync(ApiConventions.BasePath + "/me/directory", Ct))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // Sign-out is always possible: a second flagged session (after another reset) signs out and ends.
        var v = (await Wire.GetAsync<PagedUsers>(owner, "/admin/users?pageSize=50", Ct)).Items.Single(u => u.UserId == rec.UserId).Version;
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(rec.UserId, null, "920461", v), Ct);
        using var tablet2 = s.DeviceClient(device);
        using (var r = await Wire.PostAsync(tablet2, "/auth/sign-in", new SignInRequest("rec.asha", Pin: "920461", ReturnToken: true), null, Ct))
            HccServer.UseBearer(tablet2, (await Wire.OkAsync<SignInResult>(r, "PIN sign-in 2", Ct)).SessionToken!);
        using (var r = await Wire.PostAsync(tablet2, "/auth/sign-out", new { }, null, Ct))
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using (var r = await tablet2.GetAsync(ApiConventions.BasePath + "/me/capabilities", Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Admin_reset_password_session_is_gated_health_details_hidden_until_the_own_change_A06_01()
    {
        await using var s = await ServerAsync();
        using var owner = await OwnerAsync(s);
        var tad = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest("tech.lata", "Lata (Tech admin)", [new RoleAssignmentInput("TAD")]), Ct);
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(tad.UserId, "First#Owner-Set-2026", null, tad.Version), Ct);

        using var laptop = s.CookieClient();
        laptop.DefaultRequestHeaders.Add(LanHeader, "192.168.1.40");
        using (var r = await Wire.PostAsync(laptop, "/auth/sign-in", new SignInRequest("tech.lata", Password: "First#Owner-Set-2026"), null, Ct))
            Assert.True((await Wire.OkAsync<SignInResult>(r, "password sign-in", Ct)).MustChangeCredential);

        // A Tech admin normally sees health details (PERM-ADM-005); with the Owner's password still in force: the anonymous answer only.
        Assert.Null((await Wire.GetAsync<HealthResult>(laptop, "/system/health", Ct)).Database);
        using (var r = await laptop.GetAsync(ApiConventions.BasePath + "/admin/devices", Ct))
            await AssertGatedAsync(r, "Change your password first.");

        // The same password back is no change; a new one releases the session.
        var same = await Wire.CommandFailsAsync(laptop, "ChangeOwnCredential", new ChangeOwnCredentialRequest("First#Owner-Set-2026", "First#Owner-Set-2026", null), (HttpStatusCode)422, "VALIDATION", Ct);
        Assert.Contains("different from the current one", same.Message, StringComparison.Ordinal);
        await Wire.CommandAsync<UserVersionResult>(laptop, "ChangeOwnCredential", new ChangeOwnCredentialRequest("First#Owner-Set-2026", "Lata-own-secret-42", null), Ct);
        Assert.Equal(s.Name, (await Wire.GetAsync<HealthResult>(laptop, "/system/health", Ct)).Database);
        using (var r = await laptop.GetAsync(ApiConventions.BasePath + "/admin/devices", Ct))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, await s.CountAsync("SELECT COUNT(*) FROM plat.credential WHERE must_change = 1;", Ct));
    }

    private sealed record PagedUsers(IReadOnlyList<UserSummary> Items);
}
