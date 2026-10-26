using System.Net;
using System.Text.Json;
using Hcc.Api.Endpoints;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Setup;
using Hcc.Domain.Errors;
using Hcc.Domain.Setup;
using Hcc.Integration.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Hcc.Integration.Tests;

/// <summary>W3-b first-time hotel setup: rules (no database) and SetupProperty over HTTP on an empty installation.</summary>
public sealed class SetupRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 20, 0, 0, TimeSpan.Zero); // 27 Oct 01:30 IST

    [Fact]
    public void Defaults_are_Maharashtra_Kolkata_main_and_standard_times_with_todays_IST_date()
    {
        var p = PropertySetupRules.Plan(" Hotel Sagar ", "Station Road, Latur", null, null, null, null, null, null, null, Now);
        Assert.Equal(("MAIN", "Hotel Sagar", "Maharashtra", "Asia/Kolkata", "14:00", "11:00"), (p.Code, p.Name, p.State, p.TimeZone, p.CheckInTime, p.CheckOutTime));
        Assert.Equal(new DateOnly(2026, 10, 27), p.BusinessDate);
        Assert.Null(p.Gstin);
    }

    [Fact]
    public void Gstin_must_match_the_state_code_and_time_zone_must_be_iana()
    {
        Assert.Equal("27AAAAA0000A1Z2", PropertySetupRules.Plan("H", "A", null, "27aaaaa0000a1z2", "maharashtra", null, null, null, null, Now).Gstin);
        // W4 D1/D2: the check character is verified at setup too (the invoicing rule) — a typing error would block every tax invoice.
        Assert.Contains("check character is wrong", Assert.Throws<HccException>(() => PropertySetupRules.Plan("H", "A", null, "27AAAAA0000A1Z5", null, null, null, null, null, Now)).Message, StringComparison.Ordinal);
        Assert.Throws<HccException>(() => PropertySetupRules.Plan("H", "A", null, "29AAAAA0000A1Z5", null, null, null, null, null, Now));
        Assert.Throws<HccException>(() => PropertySetupRules.Plan("H", "A", null, "27AAAA", null, null, null, null, null, Now));
        Assert.Throws<HccException>(() => PropertySetupRules.Plan("H", "A", null, null, "Atlantis", null, null, null, null, Now));
        Assert.Throws<HccException>(() => PropertySetupRules.Plan("H", "A", null, null, null, null, "India Standard Time", null, null, Now));
        Assert.Throws<HccException>(() => PropertySetupRules.Plan("H", "A", null, null, null, null, null, "2 pm", null, Now));
        Assert.Throws<HccException>(() => PropertySetupRules.Plan("", "A", null, null, null, null, null, null, null, Now));
        Assert.Throws<HccException>(() => PropertySetupRules.Plan("H", "", null, null, null, null, null, null, null, Now));
    }

    [Fact]
    public void Only_loopback_is_the_server_pc()
    {
        Assert.True(SetupEndpoints.IsServerPc(IPAddress.Loopback));
        Assert.True(SetupEndpoints.IsServerPc(IPAddress.IPv6Loopback));
        Assert.True(SetupEndpoints.IsServerPc(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.False(SetupEndpoints.IsServerPc(IPAddress.Parse("192.168.1.50")));
        Assert.False(SetupEndpoints.IsServerPc(IPAddress.Parse("::ffff:192.168.1.50")));
        Assert.False(SetupEndpoints.IsServerPc(null));
    }
}

[Trait("Category", "Sql")]
public sealed class SetupPropertyTests
{
    private const string LanHeader = "X-Test-Lan-Client";
    private const string Password = "Maroon&Sand#Ledger26";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SetupPropertyRequest Request(string name = "Hotel Sagar Residency") =>
        new(name, "Station Road, Latur 413512", "owner.sagar", "Sagar (Owner)", Password, Gstin: "27AAAAA0000A1Z2", BusinessDate: new DateOnly(2026, 10, 26), CheckInTime: "13:00", CheckOutTime: "10:00");

    /// <summary>Empty installation (no property row); a request carrying <see cref="LanHeader"/> arrives as if from a LAN tablet.</summary>
    private static Task<HccServer> EmptyServerAsync() => HccServer.StartAsync("SELECT 0;", Ct, app => app.Use(async (http, next) =>
    {
        if (http.Request.Headers.ContainsKey(LanHeader)) http.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        await next(http);
    }));

    private static async Task<HttpResponseMessage> SetupAsync(HttpClient c, SetupPropertyRequest body, string key) =>
        await Wire.PostAsync(c, "/setup/SetupProperty", body, key, Ct);

    [Fact]
    public async Task Empty_install_setup_creates_property_owner_and_identity_settings_then_owner_signs_in()
    {
        await using var s = await EmptyServerAsync();
        using var c = s.CookieClient();
        Assert.True((await Wire.GetAsync<SetupStatusResult>(c, "/setup/status", Ct)).SetupRequired);

        using var r = await SetupAsync(c, Request(), "setup-1");
        var res = await Wire.OkAsync<SetupPropertyResult>(r, "SetupProperty", Ct);
        Assert.Equal(("MAIN", "Maharashtra", "Asia/Kolkata", "owner.sagar", 5), (res.Code, res.State, res.TimeZone, res.OwnerLoginName, res.SettingsSeeded));
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM inv.property WHERE name = N'Hotel Sagar Residency' AND state_name = N'Maharashtra' AND gstin = '27AAAAA0000A1Z2' AND business_date = '2026-10-26' AND time_zone = 'Asia/Kolkata'", Ct));
        Assert.Equal(1, await s.CountAsync($"SELECT COUNT(*) FROM plat.user_role WHERE user_id = {res.OwnerUserId} AND role_code = 'OWN'", Ct));
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'PROPERTY_SETUP' AND operation = 'SetupProperty'", Ct));
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'OWNER_BOOTSTRAP'", Ct));
        Assert.Equal(5, await s.CountAsync($"SELECT COUNT(*) FROM cfg.setting_change WHERE property_id = {res.PropertyId} AND changed_by = {res.OwnerUserId} AND effective_from = '2026-10-26' AND setting_key IN ('hotel','brand.address','brand.gstin','checkin','checkout')", Ct));
        Assert.Equal("\"Hotel Sagar Residency\"", (string)(await s.ScalarAsync("SELECT value_json FROM cfg.setting_change WHERE setting_key = 'hotel'", Ct))!);
        Assert.False((await Wire.GetAsync<SetupStatusResult>(c, "/setup/status", Ct)).SetupRequired);

        // Same key + same request (+ same password) → the stored result, nothing written twice.
        using (var again = await SetupAsync(c, Request(), "setup-1"))
            Assert.Equal(res, await Wire.OkAsync<SetupPropertyResult>(again, "SetupProperty replay", Ct));
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM inv.property", Ct));

        // The Owner signs in with the password (DEC-062) and holds Owner rights in Master configuration.
        using (var signIn = await Wire.PostAsync(c, "/auth/sign-in", new SignInRequest("owner.sagar", Password: Password), null, Ct))
            Assert.Equal(res.OwnerUserId, (await Wire.OkAsync<SignInResult>(signIn, "owner sign-in", Ct)).UserId);
        using var cfg = await c.GetAsync(ApiConventions.BasePath + "/me/capabilities", Ct);
        Assert.Equal(HttpStatusCode.OK, cfg.StatusCode);
    }

    [Fact]
    public async Task Second_attempt_is_refused_and_a_reused_key_with_other_data_is_a_mismatch()
    {
        await using var s = await EmptyServerAsync();
        using var c = s.CookieClient();
        using (var first = await SetupAsync(c, Request(), "setup-a"))
            await Wire.OkAsync<SetupPropertyResult>(first, "SetupProperty", Ct);

        using (var second = await SetupAsync(c, Request("Another Hotel"), "setup-b"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
            Assert.Equal("FORBIDDEN", JsonSerializer.Deserialize<ErrorResponse>(await second.Content.ReadAsStringAsync(Ct), HccServer.Json)!.Code);
        }

        using (var reused = await SetupAsync(c, Request("Another Hotel"), "setup-a"))
            Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        using (var wrongPassword = await SetupAsync(c, Request() with { OwnerPassword = "Different#Secret77" }, "setup-a"))
            Assert.Equal(HttpStatusCode.Conflict, wrongPassword.StatusCode);
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM inv.property", Ct));
        Assert.Equal(1, await s.CountAsync("SELECT COUNT(*) FROM plat.app_user", Ct));
    }

    [Fact]
    public async Task Setup_from_another_device_on_the_network_is_refused()
    {
        await using var s = await EmptyServerAsync();
        using var c = s.CookieClient();
        c.DefaultRequestHeaders.Add(LanHeader, "1");
        using (var r = await SetupAsync(c, Request(), "setup-lan"))
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using (var st = await c.GetAsync(ApiConventions.BasePath + "/setup/status", Ct))
            Assert.Equal(HttpStatusCode.Forbidden, st.StatusCode);
        Assert.Equal(0, await s.CountAsync("SELECT COUNT(*) FROM inv.property", Ct));
        Assert.Equal(0, await s.CountAsync("SELECT COUNT(*) FROM plat.audit_event", Ct));
    }

    [Fact]
    public async Task Invalid_input_writes_nothing()
    {
        await using var s = await EmptyServerAsync();
        using var c = s.CookieClient();
        using (var r = await SetupAsync(c, Request() with { OwnerPassword = "short" }, "setup-bad"))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        using (var r = await SetupAsync(c, Request() with { Gstin = "29AAAAA0000A1Z5" }, "setup-bad2"))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(0, await s.CountAsync("SELECT COUNT(*) FROM inv.property", Ct));
    }
}
