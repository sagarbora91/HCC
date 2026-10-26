using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.FrontDeskMasters;
using Hcc.Contracts.Masters;
using Hcc.FrontDeskMasters.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.FrontDeskMasters.Tests.Sql;

/// <summary>The MA-5 endpoints over real HTTP (Kestrel on 127.0.0.1) with the same DI lines and Map call as HccComposition: the wire shapes
/// the production sync layer (sync/frontdesk-master.ts) reads and sends.</summary>
[Trait("Category", "Sql")]
public class FrontDeskMastersHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private FdmWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await FdmWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddMastersModule();
        builder.Services.AddFrontDeskMastersModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapFrontDeskMastersEndpoints();
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

    private async Task<string> GetAsync(string path, string user, HttpStatusCode expect = HttpStatusCode.OK)
    {
        using var res = await _http.SendAsync(Req(HttpMethod.Get, path, user), Ct);
        Assert.Equal(expect, res.StatusCode);
        return await res.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task Lists_read_for_every_role_history_and_commands_by_permission_with_the_sync_wire_shapes()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/masters/booking-sources", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        foreach (var p in new[] { "booking-sources", "id-types", "number-series", "policies" })
        {
            var json = await GetAsync($"/masters/{p}?page=1&pageSize=200", "housekeeper");
            Assert.Contains("\"rowId\":-1", json, StringComparison.Ordinal);
            Assert.Contains("\"version\":\"\"", json, StringComparison.Ordinal);
        }

        Assert.Contains("\"commissionBps\":null", await GetAsync("/masters/booking-sources", "housekeeper"), StringComparison.Ordinal);
        Assert.Contains("\"nextNumber\":\"INV/2627/0000001\"", await GetAsync("/masters/number-series", "reception"), StringComparison.Ordinal);
        Assert.Contains("\"sources\":[]", await GetAsync("/masters/policies", "reception"), StringComparison.Ordinal);
        // MA-11 (DEC-061): policy charge / deposit percentages are money — sent to the front desk, null for housekeeping and maintenance.
        Assert.Contains("\"cancelChargeBps\":0", await GetAsync("/masters/policies", "reception"), StringComparison.Ordinal);
        foreach (var u in new[] { "housekeeper", "maint" })
        {
            var pol = await GetAsync("/masters/policies", u);
            Assert.Contains("\"cancelChargeBps\":null", pol, StringComparison.Ordinal);
            Assert.Contains("\"depositBps\":null", pol, StringComparison.Ordinal);
        }
        await GetAsync("/masters/booking-sources/history", "manager", HttpStatusCode.Forbidden);
        await GetAsync("/masters/id-types/history?page=1&pageSize=200", "techadmin");
        await GetAsync("/masters/number-series/history", "techadmin", HttpStatusCode.Forbidden);
        await GetAsync("/masters/policies/history", "owner");

        // The sync layer's bodies (sync/frontdesk-master.ts toBody) bind; default rows by negative rowId with an empty version.
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveBookingSource", "manager", new { code = "X", name = "X", segment = "DIRECT" }, "h0"), Ct))
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        var tg = new { code = "AGENT-TG", name = "Travel Guru", sortOrder = 90, segment = "AGENT", channel = (string?)null };
        FrontDeskRowResult added;
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveBookingSource", "techadmin", tg, "h1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            added = (await ok.Content.ReadFromJsonAsync<FrontDeskRowResult>(Web, Ct))!;
            Assert.Equal(("bookingSources", "AGENT-TG"), (added.Master, added.Code));
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveBookingSource", "techadmin", tg, "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var series = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveNumberSeries", "owner",
                   new { code = "INV", name = "Tax invoice (hotel)", sortOrder = 10, document = "INV", prefix = "INV", startNo = 1, padding = 7, reset = "FY", activeFrom = "2000-04-01", rowId = -1, expectedVersion = "" }, "h2"), Ct))
            Assert.Equal(HttpStatusCode.OK, series.StatusCode);
        using (var gst = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveNumberSeries", "owner",
                   new { code = "BOS-2", name = "Bill of supply 2", document = "BOS", prefix = "BS", startNo = 1, padding = 5, reset = "NEVER", activeFrom = "2027-04-01" }, "h3"), Ct))
        {
            var err = (await gst.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("VALIDATION", "MASTER_INVALID", "reset"), (err.Code, err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        using (var pol = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveBookingPolicy", "owner",
                   new { code = "OTA-STD", name = "OTA standard", freeCancelHours = 48, cancelChargeBps = 10000, noShowChargeBps = 10000, depositBps = 2500, sources = new[] { "OTA" } }, "h4"), Ct))
            Assert.Equal(HttpStatusCode.OK, pol.StatusCode);
        using (var del = await _http.SendAsync(Req(HttpMethod.Post, "/commands/DeleteBookingPolicy", "owner", new { rowId = -1, expectedVersion = "" }, "h5"), Ct))
            Assert.Equal("STALE_VERSION", (await del.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var order = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReorderIdTypes", "techadmin", new { rowIds = new[] { -2, -1, -3, -4, -5, -6, -7 } }, "h6"), Ct))
            Assert.Equal(7, (await order.Content.ReadFromJsonAsync<FrontDeskReorderResult>(Web, Ct))!.Rows);
        using (var retire = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RetireBookingSource", "owner", new { rowId = added.RowId, expectedVersion = added.Version }, "h7"), Ct))
            Assert.Equal(HttpStatusCode.OK, retire.StatusCode);
        var hist = JsonSerializer.Deserialize<MasterHistoryList>(await GetAsync($"/masters/booking-sources/{added.RowId}/history?page=1&pageSize=50", "owner"), Web)!;
        Assert.Contains(hist.Items, h => h.Field == "active" && h.NewValue == "retired");
    }
}
