using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Configuration.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Configuration.Tests.Sql;

/// <summary>
/// Master configuration over real HTTP with the whole composition (<see cref="HccComposition"/>: P1 sessions + policy, every module):
/// Owner and Tech admin sign in with passwords (DEC-062), read groups, change settings, raise and approve a request, switch a module
/// off and see a test command route guarded by <c>RequireHccModule</c> refused.
/// </summary>
[Trait("Category", "Sql")]
public sealed class ConfigurationHttpTests(ConfigDatabase db) : IClassFixture<ConfigDatabase>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private WebApplication _app = null!;
    private Uri _base = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHcc(builder.Configuration, db.Platform);
        _app = builder.Build();
        var api = _app.MapHcc();
        api.MapHccSecured().MapPost("/commands/TestPosCommand", () => Results.Ok(new { ok = true })).RequireHccModule("pos");
        await _app.StartAsync(Ct);
        _base = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }

    private async Task<HttpClient> SignInAsync(int userId)
    {
        var login = await db.PasswordAsync(userId, "Sand-and-Maroon-26");
        var c = new HttpClient { BaseAddress = _base };
        using var r = await c.PostAsJsonAsync(ApiConventions.BasePath + "/auth/sign-in", new SignInRequest(login, Password: "Sand-and-Maroon-26", ReturnToken: true), Json, Ct);
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

    private static async Task<T> GetAsync<T>(HttpClient c, string path)
    {
        using var r = await c.GetAsync(ApiConventions.BasePath + path, Ct);
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)r.StatusCode} {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static T Ok<T>((HttpStatusCode Status, string Body) r)
    {
        Assert.True(r.Status == HttpStatusCode.OK, $"{(int)r.Status} {r.Body}");
        return JsonSerializer.Deserialize<T>(r.Body, Json)!;
    }

    [Fact]
    public async Task Owner_and_Tech_admin_work_the_hub_over_HTTP_and_a_switched_off_module_is_refused()
    {
        var h = await db.NewHotelAsync();
        var owner = await SignInAsync(h.Owner);
        var tad = await SignInAsync(h.TechAdmin);

        using (var anon = new HttpClient { BaseAddress = _base })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(ApiConventions.BasePath + "/configuration/groups", Ct)).StatusCode);

        var groups = await GetAsync<ConfigGroupsResult>(tad, "/configuration/groups");
        Assert.Equal(12, groups.Groups.Count);
        Assert.True(groups.Groups.Single(g => g.Id == "money").ReadOnly);

        var money = await PostAsync(tad, "/commands/SetSetting", new SetSettingRequest("discountLimit", ConfigDatabase.J(1000)));
        Assert.Equal(HttpStatusCode.Forbidden, money.Status);
        Assert.Equal("FORBIDDEN", JsonSerializer.Deserialize<ErrorResponse>(money.Body, Json)!.Code);

        var applied = Ok<SetSettingResult>(await PostAsync(owner, "/commands/SetSetting", new SetSettingRequest("brand.printFormat", ConfigDatabase.J("A5"))));
        Assert.Equal(("applied", "A4", "A5"), (applied.Status, applied.Before, applied.After));
        var detail = await GetAsync<ConfigGroupDetail>(owner, "/configuration/groups/property");
        var pf = detail.Settings.Single(s => s.Key == "brand.printFormat");
        Assert.Equal(("A5", applied.Version), (pf.Display, pf.Version));

        var req = Ok<SetSettingResult>(await PostAsync(tad, "/commands/SetSetting", new SetSettingRequest("sec.pinLock", ConfigDatabase.J(true))));
        Assert.Equal("requested", req.Status);
        var pending = await GetAsync<SettingRequestsResult>(owner, "/configuration/requests");
        var item = Assert.Single(pending.Items);
        Ok<DecideSettingRequestResult>(await PostAsync(owner, "/commands/DecideSettingRequest", new DecideSettingRequestRequest(item.RequestId, true, null, item.Version)));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(owner, "/commands/TestPosCommand", new { })).Status);
        Ok<SetSettingResult>(await PostAsync(tad, "/commands/SetSetting", new SetSettingRequest("module.pos", ConfigDatabase.J(false))));
        var refused = await PostAsync(owner, "/commands/TestPosCommand", new { });
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains("Restaurant & POS is switched off", refused.Body, StringComparison.Ordinal);
        var modules = await GetAsync<ModulesResult>(tad, "/configuration/modules");
        Assert.False(modules.Modules.Single(m => m.Id == "pos").On);

        var history = await GetAsync<SettingHistoryPage>(owner, "/configuration/history?key=sec.pinLock");
        Assert.Equal(("Off", "On"), (history.Items[0].Before, history.Items[0].After));
        var search = await GetAsync<ConfigSearchResult>(owner, "/configuration/search?q=gstin");
        Assert.Equal("property", Assert.Single(search.Hits).GroupId);
        var export = await GetAsync<ConfigurationExport>(owner, "/configuration/export");
        Assert.Equal("A5", export.Values["brand.printFormat"].GetString());
        var plan = Ok<ImportPlanView>(await PostAsync(tad, "/configuration/import-plan", export));
        Assert.Empty(plan.Errors);
    }
}
