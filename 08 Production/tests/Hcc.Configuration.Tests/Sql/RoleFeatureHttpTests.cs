using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Configuration.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Access;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer.Access;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Configuration.Tests.Sql;

/// <summary>
/// ADM-2 [ADM2] per-role feature levels over real HTTP (02 Planning/agents/ADM_Briefs.md §2.3–§2.4): the day-shift manager (MGR base,
/// accounting.* off) is refused on every Accounting command route with FORBIDDEN, reason MODULE_OFF, featureId and roleCode, and sees the
/// accounting features as off in /me/capabilities; the night-shift manager (MGR base, accounting.* view) is refused on SaveManualVoucher but
/// reaches RunBusinessDayClose (core: follows the base). Roles are created through POST /commands/CreateRole + SetRoleFeature as the Owner.
/// </summary>
[Trait("Category", "Sql")]
public sealed class RoleFeatureHttpTests(ConfigDatabase db) : IClassFixture<ConfigDatabase>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Pin = "482913";

    /// <summary>Accounting command routes gated per feature (same list as FeatureGateHttpTests).</summary>
    private static readonly Dictionary<string, string[]> Accounting = new(StringComparer.Ordinal)
    {
        [FeatureIds.Vouchers] = ["SaveManualVoucher", "DecideManualVoucher", "VoidManualVoucher", "AddLoan"],
        [FeatureIds.Gst] = ["PostGstSetoff", "RecordGstPayment", "RecordStatutoryRemittance", "LockAccountingMonth", "RequestAccountingMonthLock", "UnlockAccountingMonth", "SetGstOnAdvances"],
        [FeatureIds.Tds] = ["SetTdsSettings", "SetTdsRate", "RecordTdsRemittance", "SetTdsForm168"],
    };

    private static readonly string[] AccountingFeatures = [FeatureIds.Vouchers, FeatureIds.Gst, FeatureIds.Tds, FeatureIds.Tally];

    private WebApplication _app = null!;
    private Uri _base = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHcc(builder.Configuration, db.Platform);
        _app = builder.Build();
        _app.MapHcc();
        await _app.StartAsync(Ct);
        _base = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }

    private async Task<HttpClient> OwnerAsync(Hotel h)
    {
        var login = await db.PasswordAsync(h.Owner, "Sand-and-Maroon-26");
        var c = new HttpClient { BaseAddress = _base };
        using var r = await c.PostAsJsonAsync(ApiConventions.BasePath + "/auth/sign-in", new SignInRequest(login, Password: "Sand-and-Maroon-26", ReturnToken: true), Json, Ct);
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, body);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JsonSerializer.Deserialize<SignInResult>(body, Json)!.SessionToken);
        return c;
    }

    /// <summary>A fictional staff member holding <paramref name="role"/>, signed in by PIN on a registered device of the hotel.</summary>
    private async Task<HttpClient> StaffAsync(Hotel h, string login, string role)
    {
        var id = Convert.ToInt32(await db.ScalarAsync("INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES (@p, @l, @d, '2026-01-01');",
            ("@p", h.PropertyId), ("@l", login), ("@d", "Staff " + login)), CultureInfo.InvariantCulture);
        await db.ScalarAsync("INSERT plat.user_role(user_id, role_code, valid_from_utc, assigned_utc) VALUES (@u, @r, '2026-01-01', '2026-01-01');", ("@u", id), ("@r", role));
        var hash = new Pbkdf2SecretHasher().Hash(Pin, 1000);
        await db.ScalarAsync("INSERT plat.credential(user_id, kind, algorithm, iterations, salt, secret_hash, changed_utc) VALUES (@u, 'PIN', @a, @i, @s, @h, '2026-01-01');",
            ("@u", id), ("@a", hash.Algorithm), ("@i", hash.Iterations), ("@s", hash.Salt), ("@h", hash.Hash));
        var token = AccessTokens.NewToken();
        await db.ScalarAsync("INSERT plat.device(property_id, label, token_sha256, registered_by, registered_utc) VALUES (@p, @l, @h, @by, '2026-01-01');",
            ("@p", h.PropertyId), ("@l", "Desk tablet " + login), ("@h", AccessTokens.Sha256(token)), ("@by", h.Owner));

        var c = new HttpClient { BaseAddress = _base };
        c.DefaultRequestHeaders.Add(AccessConventions.DeviceTokenHeader, token);
        using var r = await c.PostAsJsonAsync(ApiConventions.BasePath + "/auth/sign-in", new SignInRequest(login, Pin: Pin, ReturnToken: true), Json, Ct);
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, body);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JsonSerializer.Deserialize<SignInResult>(body, Json)!.SessionToken);
        return c;
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(HttpClient c, string path, object body)
    {
        using var m = new HttpRequestMessage(HttpMethod.Post, ApiConventions.BasePath + path) { Content = JsonContent.Create(body, options: Json) };
        m.Headers.Add(ApiConventions.IdempotencyKeyHeader, ConfigDatabase.Key());
        using var r = await c.SendAsync(m, Ct);
        return (r.StatusCode, await r.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<string> CreateRoleAsync(HttpClient owner, string name, string[] features, string access)
    {
        var created = await PostAsync(owner, "/commands/CreateRole", new CreateRoleRequest(name + " " + Guid.NewGuid().ToString("N")[..6], RoleCodes.Manager));
        Assert.True(created.Status == HttpStatusCode.OK, created.Body);
        var code = JsonSerializer.Deserialize<RoleChangeResult>(created.Body, Json)!.RoleCode;
        foreach (var f in features)
        {
            var set = await PostAsync(owner, "/commands/SetRoleFeature", new SetRoleFeatureRequest(code, f, access));
            Assert.True(set.Status == HttpStatusCode.OK, set.Body);
        }

        return code;
    }

    private static bool IsRoleRefusal((HttpStatusCode Status, string Body) r, string featureId, string roleCode)
    {
        if (r.Status != HttpStatusCode.Forbidden) return false;
        var e = JsonSerializer.Deserialize<ErrorResponse>(r.Body, Json)!;
        return e.Details is { } d && d.TryGetValue("featureId", out var f) && f.GetString() == featureId
            && d.TryGetValue("roleCode", out var rc) && rc.GetString() == roleCode && d["reason"].GetString() == RoleRules.ModuleOffReason;
    }

    [Fact]
    public async Task Day_shift_manager_is_refused_on_every_accounting_command_and_sees_accounting_off()
    {
        var h = await db.NewHotelAsync();
        var owner = await OwnerAsync(h);
        var day = await CreateRoleAsync(owner, "Day shift manager", AccountingFeatures, "off");
        var meera = await StaffAsync(h, "dayshift" + h.PropertyId.ToString(CultureInfo.InvariantCulture), day);

        foreach (var (feature, routes) in Accounting)
            foreach (var route in routes)
            {
                var r = await PostAsync(meera, "/commands/" + route, new { });
                Assert.True(IsRoleRefusal(r, feature, day), $"{route}: {(int)r.Status} {r.Body}");
                Assert.Contains("is not part of your role", JsonSerializer.Deserialize<ErrorResponse>(r.Body, Json)!.Message, StringComparison.Ordinal);
            }

        using var caps = await meera.GetAsync(ApiConventions.BasePath + "/me/capabilities", Ct);
        var result = JsonSerializer.Deserialize<CapabilitiesResult>(await caps.Content.ReadAsStringAsync(Ct), Json)!;
        Assert.Contains(RoleCodes.Manager, result.Roles);   // base code kept for role checks
        Assert.Contains(day, result.Roles);
        Assert.NotNull(result.Features);
        Assert.All(AccountingFeatures, f => Assert.Equal("off", result.Features![f]));

        // A built-in Manager of the same hotel is unaffected.
        var builtIn = await StaffAsync(h, "mgrplain" + h.PropertyId.ToString(CultureInfo.InvariantCulture), RoleCodes.Manager);
        Assert.False(IsRoleRefusal(await PostAsync(builtIn, "/commands/SaveManualVoucher", new { }), FeatureIds.Vouchers, RoleCodes.Manager));
    }

    [Fact]
    public async Task Night_shift_manager_with_accounting_view_is_refused_on_SaveManualVoucher_but_reaches_RunBusinessDayClose()
    {
        var h = await db.NewHotelAsync();
        var owner = await OwnerAsync(h);
        var night = await CreateRoleAsync(owner, "Night shift manager", AccountingFeatures, "view");
        var ravi = await StaffAsync(h, "nightshift" + h.PropertyId.ToString(CultureInfo.InvariantCulture), night);

        var save = await PostAsync(ravi, "/commands/SaveManualVoucher", new { });
        Assert.True(IsRoleRefusal(save, FeatureIds.Vouchers, night), $"{(int)save.Status} {save.Body}");
        Assert.Contains("view only", JsonSerializer.Deserialize<ErrorResponse>(save.Body, Json)!.Message, StringComparison.Ordinal);

        var close = await PostAsync(ravi, "/commands/RunBusinessDayClose", new { });
        Assert.False(close.Status == HttpStatusCode.Forbidden && close.Body.Contains("\"roleCode\"", StringComparison.Ordinal), $"{(int)close.Status} {close.Body}");
        Assert.False(close.Status == HttpStatusCode.Forbidden && close.Body.Contains("\"featureId\"", StringComparison.Ordinal), $"{(int)close.Status} {close.Body}");

        // Roles are Owner-only: the night-shift manager cannot create one.
        var refused = await PostAsync(ravi, "/commands/CreateRole", new CreateRoleRequest("Sneaky", RoleCodes.Manager));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);

        // GET /admin/roles shows the custom role with its base, money flag and view levels.
        using var roles = await owner.GetAsync(ApiConventions.BasePath + "/admin/roles", Ct);
        var list = JsonSerializer.Deserialize<RolesResult>(await roles.Content.ReadAsStringAsync(Ct), Json)!;
        var view = Assert.Single(list.Roles, r => r.RoleCode == night);
        Assert.True(view.IsCustom);
        Assert.Equal(RoleCodes.Manager, view.BaseRole);
        Assert.True(view.Money);
        Assert.Equal("view", view.Features![FeatureIds.Vouchers]);
        Assert.True(view.Version > 1);
    }
}
