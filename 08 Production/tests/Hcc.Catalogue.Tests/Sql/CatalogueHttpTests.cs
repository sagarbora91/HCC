using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Catalogue.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Billing;
using Hcc.Contracts.Catalogue;
using Hcc.Contracts.Masters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Catalogue.Tests.Sql;

/// <summary>The catalogue endpoints over real HTTP with the same DI lines as HccComposition (Billing, Tax, Masters, Catalogue), the real
/// access policy and a test session service: read gating per role and the wire shapes the production sync layer (sync/catalogue.ts) reads.</summary>
[Trait("Category", "Sql")]
public class CatalogueHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private CatalogueWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await CatalogueWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddTaxModule();
        builder.Services.AddSingleton<Application.Billing.IBillingStore>(new Infrastructure.SqlServer.Billing.SqlBillingStore(_w.Platform.Connections));
        builder.Services.AddMastersModule();
        builder.Services.AddCatalogueModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapCatalogueEndpoints();
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
    public async Task Lists_read_with_folio_view_history_for_the_Owner_and_wire_shapes()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/catalogue/extras", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        foreach (var (path, user, status) in new[]
                 {
                     ("/catalogue/extras", "reception", HttpStatusCode.OK), ("/catalogue/meal-plans", "accountant", HttpStatusCode.OK),
                     ("/catalogue/discount-reasons", "cashier", HttpStatusCode.OK), ("/catalogue/extras", "housekeeper", HttpStatusCode.Forbidden),
                     ("/catalogue/meal-plans", "techadmin", HttpStatusCode.Forbidden), ("/catalogue/links", "manager", HttpStatusCode.OK),
                     ("/catalogue/extras/history", "manager", HttpStatusCode.Forbidden), ("/catalogue/meal-plans/history", "owner", HttpStatusCode.OK),
                     ("/catalogue/discount-reasons/history", "owner", HttpStatusCode.OK),
                 })
        {
            using var r = await _http.SendAsync(Req(HttpMethod.Get, path, user), Ct);
            Assert.True(status == r.StatusCode, $"{user} {path}: {r.StatusCode}");
        }

        using var ex = await _http.SendAsync(Req(HttpMethod.Get, "/catalogue/extras?page=1&pageSize=200", "reception"), Ct);
        var json = await ex.Content.ReadAsStringAsync(Ct);
        foreach (var f in new[] { "\"extraItemId\":", "\"gstCategory\":\"laundry\"", "\"prices\":[{\"effectiveFrom\":\"2025-04-01\",\"amountPaise\":50000}]", "\"usageCount\":0", "\"version\":" })
            Assert.Contains(f, json, StringComparison.Ordinal);
        using var mp = await _http.SendAsync(Req(HttpMethod.Get, "/catalogue/meal-plans", "reception"), Ct);
        Assert.Contains("\"meals\":[\"B\",\"D\"]", await mp.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commands_over_http_PostExtra_and_Owner_only_saves_with_contract_errors()
    {
        var (_, folio) = await _w.StayAsync(ct: Ct);
        var laundry = await _w.IdAsync("bill.extra_item", "extra_item_id", "LAUNDRY", Ct);
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/PostExtra", "reception", new PostExtraRequest(folio, laundry), "h1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var r = (await ok.Content.ReadFromJsonAsync<ChargeResult>(Web, Ct))!;
            Assert.Equal((50000L, 1800, 9000L), (r.AmountPaise, r.TaxBps, r.TaxPaise));
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/PostExtra", "reception", new PostExtraRequest(folio, laundry), "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        var save = new SaveMealPlanRequest(null, "CP", "Again", ["B"], "bundled", [], []);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveMealPlan", "manager", save, "h2"), Ct))
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var taken = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveMealPlan", "owner", save, "h3"), Ct))
        {
            var err = (await taken.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("VALIDATION", "MASTER_CODE_TAKEN", "code"), (err.Code, err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        using (var del = await _http.SendAsync(Req(HttpMethod.Post, "/commands/DeleteExtraItem", "owner", new ExtraItemRef(laundry, await _w.VersionAsync("bill.extra_item", "extra_item_id", laundry, Ct)), "h4"), Ct))
            Assert.Equal(MasterReasons.InUse, (await del.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Details!["reason"].GetString());
        using (var hist = await _http.SendAsync(Req(HttpMethod.Get, "/catalogue/extras/" + laundry + "/history", "owner"), Ct))
            Assert.Equal(0, (await hist.Content.ReadFromJsonAsync<MasterHistoryList>(Web, Ct))!.Total);
    }
}
