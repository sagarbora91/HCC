using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Groups;
using Hcc.Groups.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Groups.Tests.Sql;

/// <summary>The group endpoints over real HTTP with the module's own DI line (+ Billing's), the session filter and the real access policy.</summary>
[Trait("Category", "Sql")]
public sealed class GroupsHttpTests : IAsyncLifetime
{
    private GroupWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await GroupWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddBillingModule();
        builder.Services.AddGroupsModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapGroupsEndpoints();
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
        if (body is not null) r.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return r;
    }

    [Fact]
    public async Task Create_route_and_read_the_statement_over_http()
    {
        var create = new CreateGroupRequest("Deccan Pharma regional review", _w.Gb1, "Rahul Joshi", "Deccan Pharma Distributors (fictional)", "Rahul Joshi");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/CreateGroup", null, create, "k1"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/CreateGroup", "housekeeper", create, "k1"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/CreateGroup", "reception", create), Ct)).StatusCode); // no Idempotency-Key (VALIDATION)
        var res = await _http.SendAsync(Req(HttpMethod.Post, "/commands/CreateGroup", "reception", create, "k1"), Ct);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync(Ct));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        var id = doc.RootElement.GetProperty("groupId").GetInt64();
        var routed = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ApplyGroupRouting", "reception", new ApplyGroupRoutingRequest(id), "k2"), Ct);
        Assert.Contains("\"routed\":2", await routed.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var st = await _http.SendAsync(Req(HttpMethod.Get, $"/groups/{id}/statement", "accountant"), Ct);
        var body = await st.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"membersEstimatePaise\":860000", body, StringComparison.Ordinal);
        Assert.Contains("\"masterChargesPaise\":420000", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Get, $"/groups/{id}/statement", "housekeeper"), Ct)).StatusCode);
        Assert.Contains("GRP-", await (await _http.SendAsync(Req(HttpMethod.Get, "/groups", "cashier"), Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
