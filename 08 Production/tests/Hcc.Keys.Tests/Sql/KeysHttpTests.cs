using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Keys;
using Hcc.Keys.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Keys.Tests.Sql;

/// <summary>End to end over real HTTP: a staff session requests a key, the agent (bearer token, long poll) takes it through picked / waiting_card / result, the tablet reads the job and the panel.</summary>
[Trait("Category", "Sql")]
public class KeysHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private KeysWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await KeysWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddKeysModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapKeysEndpoints();
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

    private HttpRequestMessage Req(HttpMethod m, string path, string? bearer, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (bearer is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }

    private string Staff(string user) => TestSessions.Token(_w.Users[user]);

    [Fact]
    public async Task Staff_request_and_the_polling_agent_complete_a_key_over_HTTP()
    {
        // The Manager registers the encoder and receives the token once; Reception cannot.
        using (var denied = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RegisterKeyEncoder", Staff("reception"), new RegisterKeyEncoderRequest("FRONTDESK-1", "PC"), "reg-0"), Ct)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var reg = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RegisterKeyEncoder", Staff("manager"), new RegisterKeyEncoderRequest("FRONTDESK-1", "PC"), "reg-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var token = (await reg.Content.ReadFromJsonAsync<RegisterKeyEncoderResult>(Web, Ct))!.Token;

        // Agent calls need the encoder token, not a staff session.
        using (var noToken = await _http.SendAsync(Req(HttpMethod.Get, "/keys/jobs?encoder=FRONTDESK-1", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        using (var staffToken = await _http.SendAsync(Req(HttpMethod.Get, "/keys/jobs?encoder=FRONTDESK-1", Staff("manager")), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, staffToken.StatusCode);
        using (var hb = await _http.SendAsync(Req(HttpMethod.Post, "/keys/encoders/FRONTDESK-1/heartbeat", token, new AgentHeartbeatRequest("mock", true, "ok", true)), Ct)) Assert.Equal(HttpStatusCode.OK, hb.StatusCode);
        using (var empty = await _http.SendAsync(Req(HttpMethod.Get, "/keys/jobs?encoder=FRONTDESK-1&wait=0", token), Ct)) Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);

        using var ask = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RequestKey", Staff("reception"), new RequestKeyRequest(_w.Bookings["BK-1"], "issued", null, null), "iss-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
        var job = (await ask.Content.ReadFromJsonAsync<KeyJobView>(Web, Ct))!;
        Assert.Equal("queued", job.State);

        using var take = await _http.SendAsync(Req(HttpMethod.Get, "/keys/jobs?encoder=FRONTDESK-1&wait=2", token), Ct);
        Assert.Equal(HttpStatusCode.OK, take.StatusCode);
        var aj = (await take.Content.ReadFromJsonAsync<AgentJob>(Web, Ct))!;
        Assert.Equal((job.JobId, "issue_guest", "201"), (aj.JobId, aj.Kind, aj.Room));
        using (var st = await _http.SendAsync(Req(HttpMethod.Post, $"/keys/jobs/{aj.JobId}/state?encoder=FRONTDESK-1", token, new AgentStateRequest("waiting_card")), Ct)) Assert.Equal(HttpStatusCode.OK, st.StatusCode);
        using (var tablet = await _http.SendAsync(Req(HttpMethod.Get, $"/keys/job/{aj.JobId}", Staff("reception")), Ct))
            Assert.Equal("waiting_card", (await tablet.Content.ReadFromJsonAsync<KeyJobView>(Web, Ct))!.State);
        using (var res = await _http.SendAsync(Req(HttpMethod.Post, $"/keys/jobs/{aj.JobId}/result?encoder=FRONTDESK-1", token, new AgentResultRequest("Ok", "issued", "846716913", "846716913", "test")), Ct))
            Assert.Equal("done", (await res.Content.ReadFromJsonAsync<KeyJobView>(Web, Ct))!.State);

        using var panelRes = await _http.SendAsync(Req(HttpMethod.Get, $"/keys/panel/{_w.Bookings["BK-1"]}", Staff("reception")), Ct);
        var panel = (await panelRes.Content.ReadFromJsonAsync<KeyPanelView>(Web, Ct))!;
        Assert.Equal(1, panel.BookingCounts.Issued);
        Assert.True(panel.Encoder.Online);
        Assert.Single(panel.ActiveCards);
        using var log = await _http.SendAsync(Req(HttpMethod.Get, "/keys/log?event=issued&room=201", Staff("manager")), Ct);
        Assert.Equal(1, (await log.Content.ReadFromJsonAsync<KeyLogView>(Web, Ct))!.Total);
        using (var hk = await _http.SendAsync(Req(HttpMethod.Get, "/keys/log", Staff("housekeeper")), Ct)) Assert.Equal(HttpStatusCode.Forbidden, hk.StatusCode);
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/keys/log", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }
}
