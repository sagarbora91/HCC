using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Stock;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Stock.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Stock.Tests.Sql;

/// <summary>Bearer token "test-&lt;principal&gt;" is a session of that principal (stand-in for P1's session service behind its real filter).</summary>
internal sealed class TokenSessions(int propertyId) : ISessionService
{
    public Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (token is { } t && t.StartsWith("test-", StringComparison.Ordinal) && int.TryParse(t.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0)
            return Task.FromResult(new AuthenticatedSession(p, p, propertyId, "Test user " + p, CredentialKind.Pin, null, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(12)));
        throw new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.");
    }

    public Task SignOutAsync(string? token, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>The stock endpoints over real HTTP (Kestrel on 127.0.0.1) with the same DI line and Map call as the composition.</summary>
[Trait("Category", "Sql")]
public class HttpStockTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private StockWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await StockWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Db.Platform);
        builder.Services.AddSingleton(_w.Db.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Db.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddHccStock();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TokenSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapStockEndpoints();
        await _app.StartAsync(Ct);
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        await _w.DisposeAsync();
    }

    private static HttpRequestMessage Req(HttpMethod m, string path, int? principal, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (principal is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-" + principal.Value.ToString(CultureInfo.InvariantCulture));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }

    [Fact]
    public async Task Routes_need_a_session_commands_need_a_key_errors_use_the_contract_and_money_is_masked()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/stock/items", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using (var denied = await _http.SendAsync(Req(HttpMethod.Get, "/stock/items", Users.Reception), Ct)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        string hk;
        using (var r = await _http.SendAsync(Req(HttpMethod.Get, "/stock/items?pageSize=100", Users.Housekeeper), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            hk = await r.Content.ReadAsStringAsync(Ct);
        }

        var page = JsonSerializer.Deserialize<StockItemsPage>(hk, Web)!;
        Assert.Equal(17, page.Total);
        Assert.All(page.Rows, x => Assert.Null(x.UnitCostPaise));
        Assert.Null(page.TotalValuePaise);
        using (var m = await _http.SendAsync(Req(HttpMethod.Get, "/stock/items?pageSize=100", Users.Manager), Ct))
            Assert.NotNull(JsonSerializer.Deserialize<StockItemsPage>(await m.Content.ReadAsStringAsync(Ct), Web)!.TotalValuePaise);

        var body = new IssueStockRequest("AMN-SP", 5, "store", "floor1");
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/StockIssue", Users.Housekeeper, body), Ct))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
            Assert.Equal("VALIDATION", (await noKey.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
        }

        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/StockIssue", Users.Housekeeper, body, "h1"), Ct)) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/StockIssue", Users.Housekeeper, body, "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        Assert.Equal(255, await _w.StockAsync("AMN-SP", "store"));

        using (var over = await _http.SendAsync(Req(HttpMethod.Post, "/commands/StockIssue", Users.Housekeeper, body with { Qty = 9999 }, "h2"), Ct))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, over.StatusCode);
            Assert.Equal("VALIDATION", (await over.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
        }

        using (var grn = await _http.SendAsync(Req(HttpMethod.Post, "/commands/StockReceiveGrn", Users.Housekeeper, new ReceiveGrnRequest("Morning Supplies", null, [new("AMN-SH", 5)]), "h3"), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, grn.StatusCode);
        using (var alerts = await _http.SendAsync(Req(HttpMethod.Get, "/stock/alerts", Users.Supervisor), Ct))
            Assert.Equal(3, JsonSerializer.Deserialize<List<StockAlertView>>(await alerts.Content.ReadAsStringAsync(Ct), Web)!.Count);
        using (var set = await _http.SendAsync(Req(HttpMethod.Get, "/stock/settings", Users.Supervisor), Ct))
            Assert.True(JsonSerializer.Deserialize<StockSettingsResult>(await set.Content.ReadAsStringAsync(Ct), Web)!.ParSets >= 1);
        using (var setDenied = await _http.SendAsync(Req(HttpMethod.Get, "/stock/settings", Users.Reception), Ct)) Assert.Equal(HttpStatusCode.Forbidden, setDenied.StatusCode);
        using (var cons = await _http.SendAsync(Req(HttpMethod.Get, "/stock/consumption?from=2026-10-17&to=2026-10-23", Users.Supervisor), Ct))
            Assert.Equal(79, JsonSerializer.Deserialize<ConsumptionReport>(await cons.Content.ReadAsStringAsync(Ct), Web)!.OccupiedRoomNights);
        using (var bad = await _http.SendAsync(Req(HttpMethod.Get, "/stock/losses?from=oops", Users.Manager), Ct)) Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
    }
}
