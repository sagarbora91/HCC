using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Handover;
using Hcc.Handover.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Handover.Tests.Sql;

/// <summary>The handover endpoints over real HTTP (Kestrel on 127.0.0.1) with the same DI line and Map call as HccComposition, the real access policy and a test session service.</summary>
[Trait("Category", "Sql")]
public class HandoverHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private HandoverWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await HandoverWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddHandoverModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapHandoverEndpoints();
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
    public async Task Checklists_notes_and_commands_over_HTTP_use_the_contract_errors()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/handover/checklists", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using (var acc = await _http.SendAsync(Req(HttpMethod.Get, "/handover/checklists", "accountant"), Ct)) Assert.Equal(HttpStatusCode.Forbidden, acc.StatusCode);
        using (var hk = await _http.SendAsync(Req(HttpMethod.Get, "/handover/notes", "housekeeper"), Ct))
        {
            var json = await hk.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.OK, hk.StatusCode);
            Assert.DoesNotContain("amountPaise\":5", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Front desk", json, StringComparison.Ordinal);
        }

        using (var list = await _http.SendAsync(Req(HttpMethod.Get, "/handover/checklists", "reception"), Ct))
        {
            var l = (await list.Content.ReadFromJsonAsync<ChecklistList>(Web, Ct))!;
            Assert.Equal("2026-10-24", l.Date);
            Assert.Equal(4, l.Runs.Count);
        }

        var body = new TickChecklistItemRequest(_w.Tpl["FD-OPEN"], "fo1", true);
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/TickChecklistItem", "reception", body), Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/TickChecklistItem", "reception", body, "t1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(3, (await ok.Content.ReadFromJsonAsync<ChecklistRunView>(Web, Ct))!.Progress.Done);
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/TickChecklistItem", "reception", body, "t1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var other = await _http.SendAsync(Req(HttpMethod.Post, "/commands/TickChecklistItem", "housekeeper", body, "t2"), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
            Assert.Equal("FORBIDDEN", (await other.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        }

        using (var dash = await _http.SendAsync(Req(HttpMethod.Get, "/handover/dashboard?by=desk", "manager"), Ct))
            Assert.Equal(HttpStatusCode.OK, dash.StatusCode);
        using (var noDash = await _http.SendAsync(Req(HttpMethod.Get, "/handover/dashboard", "reception"), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, noDash.StatusCode);
    }
}
