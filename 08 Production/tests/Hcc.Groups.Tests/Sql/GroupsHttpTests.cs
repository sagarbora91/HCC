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
        builder.Services.AddHccConfiguration(); // DEC-057 / A11-01: the module gate on the command routes (every module on by default)
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

    [Fact]
    public async Task Company_credit_is_wired_by_the_module_refusal_book_figures_and_payment_route()
    {
        // DEC-076 through the production DI line: a Rs 1,000 limit; routing the two 23 Oct nights (2 × Rs 2,100) to the company master is refused.
        var co = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveCompany", "owner", new SaveCompanyRequest(null, null, "Deccan Pharma Distributors (fictional)",
            CreditLimitPaise: 100_000), "c1"), Ct);
        Assert.True(co.IsSuccessStatusCode, await co.Content.ReadAsStringAsync(Ct));
        long companyId;
        using (var doc = JsonDocument.Parse(await co.Content.ReadAsStringAsync(Ct))) companyId = doc.RootElement.GetProperty("companyId").GetInt64();
        var create = new CreateGroupRequest("Deccan Pharma regional review", _w.Gb1, "Rahul Joshi", "Deccan Pharma Distributors (fictional)", "Rahul Joshi", CompanyId: companyId);
        long groupId;
        using (var doc = JsonDocument.Parse(await (await _http.SendAsync(Req(HttpMethod.Post, "/commands/CreateGroup", "reception", create, "c2"), Ct)).Content.ReadAsStringAsync(Ct)))
            groupId = doc.RootElement.GetProperty("groupId").GetInt64();
        var refused = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ApplyGroupRouting", "reception", new ApplyGroupRoutingRequest(groupId), "c3"), Ct);
        var body = await refused.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("\"code\":\"INVALID_TRANSITION\"", body, StringComparison.Ordinal);
        Assert.Contains("CREDIT_LIMIT", body, StringComparison.Ordinal);
        Assert.Contains("\"remainingPaise\":100000", body, StringComparison.Ordinal);
        Assert.Contains("\"neededPaise\":420000", body, StringComparison.Ordinal);
        var book = await (await _http.SendAsync(Req(HttpMethod.Get, "/groups/companies", "accountant"), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"exposurePaise\":0", book, StringComparison.Ordinal);
        Assert.Contains("\"remainingPaise\":100000", book, StringComparison.Ordinal);
        var pay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RecordCompanyPayment", "cashier", new RecordCompanyPaymentRequest(companyId, "UPI", 10_000, "UPI-1"), "c4"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, pay.StatusCode); // owes nothing yet
        Assert.Contains("owes nothing on company credit", await pay.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
