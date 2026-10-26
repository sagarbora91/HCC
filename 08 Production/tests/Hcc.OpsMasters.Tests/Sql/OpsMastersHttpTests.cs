using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Housekeeping;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.OpsMasters;
using Hcc.OpsMasters.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.OpsMasters.Tests.Sql;

/// <summary>The operations masters over real HTTP (Kestrel on 127.0.0.1) with the same DI lines and Map calls as HccComposition, the real
/// access policy and a test session service: the wire shapes the production sync layer (sync/ops-masters.ts) reads and sends.</summary>
[Trait("Category", "Sql")]
public class OpsMastersHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private OpsWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await OpsWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ITaskAssigneeEligibility, PolicyTaskAssigneeEligibility>();
        builder.Services.AddMastersModule();
        builder.Services.AddOpsMastersModule();
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        var api = _app.MapGroup(ApiConventions.BasePath);
        api.MapOpsMastersEndpoints();
        await _app.StartAsync(Ct);
        _http = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        await _w.DisposeAsync();
    }

    private HttpRequestMessage Req(HttpMethod m, string path, string? user, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (user is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(_w.Users[user]));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }

    [Fact]
    public async Task Lists_read_for_every_role_commands_and_history_for_Owner_and_Tech_admin_with_contract_errors()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/housekeeping/task-types", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using (var hk = await _http.SendAsync(Req(HttpMethod.Get, "/housekeeping/task-types?page=1&pageSize=200", "housekeeper"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, hk.StatusCode);
            var json = await hk.Content.ReadAsStringAsync(Ct);
            foreach (var key in new[] { "\"appliesTo\":", "\"standardMinutes\":40", "\"needsInspection\":true", "\"used\":", "\"version\":" })
                Assert.Contains(key, json, StringComparison.Ordinal);
            var list = JsonSerializer.Deserialize<OpsList<HkTaskTypeView>>(json, Web)!;
            Assert.Equal(4, list.Total);
            Assert.Equal(["Linen & bathroom", "Surfaces & amenities", "Final room check"], list.Items[0].Checklist);
        }

        using (var cat = await _http.SendAsync(Req(HttpMethod.Get, "/guest-services/complaint-categories?status=active", "reception"), Ct))
            Assert.Equal(8, (await cat.Content.ReadFromJsonAsync<OpsList<ComplaintCategoryView>>(Web, Ct))!.Total);
        using (var lf = await _http.SendAsync(Req(HttpMethod.Get, "/guest-services/lost-found-categories", "housekeeper"), Ct))
            Assert.Equal(4, (await lf.Content.ReadFromJsonAsync<OpsList<LostFoundCategoryView>>(Web, Ct))!.Items.Count(x => x.Valuable));
        using (var zones = await _http.SendAsync(Req(HttpMethod.Get, "/housekeeping/zones", "housekeeper"), Ct)) Assert.Equal(0, (await zones.Content.ReadFromJsonAsync<OpsList<HkZoneView>>(Web, Ct))!.Total);
        using (var bad = await _http.SendAsync(Req(HttpMethod.Get, "/housekeeping/zones?status=weird", "owner"), Ct)) Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);

        using (var mgrHist = await _http.SendAsync(Req(HttpMethod.Get, "/housekeeping/task-types/history", "manager"), Ct)) Assert.Equal(HttpStatusCode.Forbidden, mgrHist.StatusCode);
        var body = new SaveHkZoneRequest(null, "Z1", "Ground section", null, ["F1"], null, _w.Users["housekeeper"]);
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveHkZone", "owner", body), Ct)) Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveHkZone", "manager", body, "h0"), Ct))
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        HkZoneView zone;
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveHkZone", "techadmin", body, "h1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            zone = (await ok.Content.ReadFromJsonAsync<HkZoneView>(Web, Ct))!;
            Assert.Equal(("Z1", 10, _w.Users["housekeeper"]), (zone.Code, zone.SortOrder, zone.DefaultAttendantId));
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveHkZone", "techadmin", body, "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var taken = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveHkZone", "owner", body with { Name = "Again", Floors = ["F2"] }, "h2"), Ct))
        {
            var err = (await taken.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("VALIDATION", "MASTER_CODE_TAKEN", "code"), (err.Code, err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        using (var stale = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RetireHkZone", "owner", new OpsRowRequest(zone.Id, "AAAAAAAAAAA="), "h3"), Ct))
            Assert.Equal("STALE_VERSION", (await stale.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var retire = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RetireHkZone", "owner", new OpsRowRequest(zone.Id, zone.Version), "h4"), Ct))
            Assert.False((await retire.Content.ReadFromJsonAsync<OpsStatusResult>(Web, Ct))!.Active);
        using (var cats = await _http.SendAsync(Req(HttpMethod.Get, "/guest-services/complaint-categories", "owner"), Ct))
        {
            var list = (await cats.Content.ReadFromJsonAsync<OpsList<ComplaintCategoryView>>(Web, Ct))!.Items;
            using var order = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReorderComplaintCategories", "owner", new OpsReorderRequest(list.Select(c => c.Id).Reverse().ToList()), "h5"), Ct);
            Assert.Equal(8, (await order.Content.ReadFromJsonAsync<OpsReorderResult>(Web, Ct))!.Count);
        }

        using (var lfSave = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveLostFoundCategory", "owner", new SaveLostFoundCategoryRequest(null, "ART", "Artwork", null, "Front desk", 6, true), "h6"), Ct))
            Assert.True((await lfSave.Content.ReadFromJsonAsync<LostFoundCategoryView>(Web, Ct))!.Valuable);
        using (var hist = await _http.SendAsync(Req(HttpMethod.Get, $"/housekeeping/zones/{zone.Id}/history?page=1&pageSize=3", "owner"), Ct))
        {
            var h = (await hist.Content.ReadFromJsonAsync<MasterHistoryList>(Web, Ct))!;
            Assert.Equal((7, 3), (h.Total, h.Items.Count));
        }

        using (var all = await _http.SendAsync(Req(HttpMethod.Get, "/guest-services/lost-found-categories/history?page=1&pageSize=200", "techadmin"), Ct))
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
    }
}
