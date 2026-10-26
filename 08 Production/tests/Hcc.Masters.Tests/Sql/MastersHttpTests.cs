using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Masters.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Masters.Tests.Sql;

/// <summary>The masters endpoints over real HTTP (Kestrel on 127.0.0.1) with the same DI line and Map call as HccComposition, the real
/// access policy and a test session service; the wire shapes the production sync layer (sync/masters.ts) reads.</summary>
[Trait("Category", "Sql")]
public class MastersHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private MastersWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await MastersWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddMastersModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapMastersEndpoints();
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
    public async Task Floors_read_for_every_role_commands_for_Owner_and_Tech_admin_with_contract_errors()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/masters/floors", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using (var hk = await _http.SendAsync(Req(HttpMethod.Get, "/masters/floors?page=1&pageSize=200", "housekeeper"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, hk.StatusCode);
            var json = await hk.Content.ReadAsStringAsync(Ct);
            Assert.Contains("\"floorId\":", json, StringComparison.Ordinal);
            Assert.Contains("\"roomCount\":8", json, StringComparison.Ordinal);
            Assert.Contains("\"version\":", json, StringComparison.Ordinal);
            Assert.Equal(3, JsonSerializer.Deserialize<FloorList>(json, Web)!.Total);
        }

        using (var mgrHist = await _http.SendAsync(Req(HttpMethod.Get, "/masters/floors/history", "manager"), Ct)) Assert.Equal(HttpStatusCode.Forbidden, mgrHist.StatusCode);
        var body = new SaveFloorRequest(null, "F4", "Annexe");
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveFloor", "owner", body), Ct)) Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveFloor", "manager", body, "h0"), Ct))
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        FloorView added;
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveFloor", "techadmin", body, "h1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            added = (await ok.Content.ReadFromJsonAsync<FloorView>(Web, Ct))!;
            Assert.Equal(("F4", 40), (added.Code, added.SortOrder));
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveFloor", "techadmin", body, "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var taken = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveFloor", "owner", new SaveFloorRequest(null, "F1", "Again"), "h2"), Ct))
        {
            var err = (await taken.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("VALIDATION", "MASTER_CODE_TAKEN", "code"), (err.Code, err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        using (var stale = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RetireFloor", "owner", new RetireFloorRequest(_w.Floors["F1"], "AAAAAAAAAAA="), "h3"), Ct))
            Assert.Equal("STALE_VERSION", (await stale.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var del = await _http.SendAsync(Req(HttpMethod.Post, "/commands/DeleteFloor", "owner", new DeleteFloorRequest(_w.Floors["F1"], await _w.VersionAsync(_w.Floors["F1"], Ct)), "h4"), Ct))
        {
            var err = (await del.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("INVALID_TRANSITION", "MASTER_IN_USE"), (err.Code, err.Details!["reason"].GetString()));
        }

        using (var order = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReorderFloors", "owner", new ReorderFloorsRequest([added.FloorId, _w.Floors["F1"], _w.Floors["F2"], _w.Floors["F3"]]), "h5"), Ct))
            Assert.Equal("F4", (await order.Content.ReadFromJsonAsync<FloorList>(Web, Ct))!.Items[0].Code);
        using (var hist = await _http.SendAsync(Req(HttpMethod.Get, $"/masters/floors/{added.FloorId}/history?page=1&pageSize=2", "owner"), Ct))
        {
            var h = (await hist.Content.ReadFromJsonAsync<MasterHistoryList>(Web, Ct))!;
            Assert.Equal((4, 2), (h.Total, h.Items.Count));
        }

        using (var all = await _http.SendAsync(Req(HttpMethod.Get, "/masters/floors/history?page=1&pageSize=200", "techadmin"), Ct))
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
    }
}
