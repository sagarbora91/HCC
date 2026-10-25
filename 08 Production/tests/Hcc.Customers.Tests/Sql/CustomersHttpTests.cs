using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Messaging;
using Hcc.Contracts;
using Hcc.Customers.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Customers.Tests.Sql;

/// <summary>The customer endpoints over real HTTP behind the session filter with the real access policy and SQL services.</summary>
[Trait("Category", "Sql")]
public class CustomersHttpTests : IAsyncLifetime
{
    private CrmWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await CrmWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddSingleton<IMessagingDataFactory>(_w.Messaging);
        builder.Services.AddCustomersModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapCustomersEndpoints();
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
    public async Task List_profile_and_merge_over_http_with_session_idempotency_and_permissions()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Req(HttpMethod.Get, "/customers", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Get, "/customers", "accountant"), Ct)).StatusCode);
        var list = await _http.SendAsync(Req(HttpMethod.Get, "/customers?q=asha", "cashier"), Ct);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync(Ct));
        Assert.Equal(3, doc.RootElement.GetProperty("total").GetInt32());
        Assert.True(doc.RootElement.GetProperty("customers")[0].GetProperty("masked").GetBoolean());
        var p = await _http.SendAsync(Req(HttpMethod.Get, "/customers/profile?key=" + CrmWorld.Asha, "owner"), Ct);
        Assert.Contains("\"lifetimePaise\":1732500", await p.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Req(HttpMethod.Get, "/customers/profile?key=n:ghost", "owner"), Ct)).StatusCode);

        var body = new { sourceKey = CrmWorld.VikramName, targetKey = CrmWorld.VikramPhone, reason = "Same guest, mobile added later" };
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/ProposeCustomerMerge", "reception", body, "m0"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/ProposeCustomerMerge", "manager", body), Ct)).StatusCode);
        var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ProposeCustomerMerge", "manager", body, "m1"), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await _http.SendAsync(Req(HttpMethod.Post, "/commands/ProposeCustomerMerge", "manager", body, "m1"), Ct)).Headers.Contains("Idempotent-Replayed"));
        using var m = JsonDocument.Parse(await ok.Content.ReadAsStringAsync(Ct));
        var decide = new { mergeId = m.RootElement.GetProperty("mergeId").GetInt64(), approve = true, expectedVersion = m.RootElement.GetProperty("version").GetString() };
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/DecideCustomerMerge", "manager", decide, "m2"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/DecideCustomerMerge", "owner", decide, "m3"), Ct)).StatusCode);
        var merges = await _http.SendAsync(Req(HttpMethod.Get, "/customers/merges?status=Approved", "manager"), Ct);
        Assert.Contains("Vikram Shah", await merges.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var dups = await _http.SendAsync(Req(HttpMethod.Get, "/customers/duplicates", "owner"), Ct);
        Assert.Equal(HttpStatusCode.OK, dups.StatusCode);
    }
}
