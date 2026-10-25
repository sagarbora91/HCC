using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Housekeeping;
using Hcc.Contracts.Stays;
using Hcc.Stays.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Stays.Tests.Sql;

/// <summary>
/// The P3 endpoints over real HTTP (Kestrel on 127.0.0.1, ephemeral port; no test-host package): the same DI lines and
/// Map calls as Program.cs, the fake policy, and a test-only header standing in for P1's session.
/// </summary>
[Trait("Category", "Sql")]
public class HttpEndpointTests : IAsyncLifetime
{
    private P3World _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await P3World.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Db.Platform);
        builder.Services.AddSingleton(_w.Db.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Db.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddHccStays();
        builder.Services.AddHccHousekeeping();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.Use((ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue("X-Test-Principal", out var p))
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, p.ToString()), new Claim(P3Session.PropertyClaim, _w.PropertyId.ToString(CultureInfo.InvariantCulture))], "test"));
            return next(ctx);
        });
        var api = _app.MapGroup(ApiConventions.BasePath);
        api.MapStaysEndpoints();
        api.MapHousekeepingEndpoints();
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

    private HttpRequestMessage Req(HttpMethod m, string path, int? principal, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (principal is not null) r.Headers.Add("X-Test-Principal", principal.Value.ToString(CultureInfo.InvariantCulture));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return r;
    }

    [Fact]
    public async Task Board_and_commands_over_HTTP_use_the_contract_errors_and_the_background_projector()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/rooms/board", null), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, anon.StatusCode);
        using (var hk = await _http.SendAsync(Req(HttpMethod.Get, "/rooms/board?pageSize=100", Users.Housekeeper), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, hk.StatusCode);
            var json = await hk.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("paise", json, StringComparison.OrdinalIgnoreCase);
            var page = JsonSerializer.Deserialize<RoomBoardPage>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(page.AssignedOnly);
        }

        var stay = _w.Seed.Stays["HCC-1041"];
        var body = new CheckoutStayRequest(stay, await _w.Seed.StayVersionAsync(stay, Ct));
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/CheckoutStay", Users.Reception, body), Ct))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
            Assert.Equal("VALIDATION", (await noKey.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
        }

        using (var unpaid = await _http.SendAsync(Req(HttpMethod.Post, "/commands/CheckoutStay", Users.Reception, body, "k1"), Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, unpaid.StatusCode);
            Assert.Equal("INVALID_TRANSITION", (await unpaid.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
        }

        var folio = await _w.Seed.FolioOfAsync(_w.Seed.Reservations["HCC-1041"], Ct);
        await _w.Seed.PostLinesAsync(folio, new[] { "2026-10-23", "2026-10-24", "2026-10-25" }.Select(d => new GoldenLine("room", d, 200000, 1000)), Ct);
        await _w.Seed.ReceiptAsync(folio, "payment", "UPI", 660000, Ct);
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/CheckoutStay", Users.Reception, body, "k2"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("Dirty", (await ok.Content.ReadFromJsonAsync<CheckoutStayResult>(Ct))!.RoomHk);
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/CheckoutStay", Users.Reception, body, "k2"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(P3Session.ReplayedHeader).Single());

        // The hosted projector picks up StayDeparted on its own.
        for (var i = 0; i < 50 && await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE stay_id = {stay};", Ct) == 0; i++)
            await Task.Delay(200, Ct);
        Assert.Equal(1, await _w.Db.CountAsync($"SELECT COUNT(*) FROM ops.hk_task WHERE stay_id = {stay};", Ct));

        using var guest = await _http.SendAsync(Req(HttpMethod.Get, "/guests/" + await _w.Db.ScalarAsync($"SELECT guest_id FROM gst.stay WHERE stay_id = {stay};", Ct), Users.Cashier), Ct);
        var view = (await guest.Content.ReadFromJsonAsync<GuestView>(Ct))!;
        Assert.Equal(("Restricted", true), (view.IdLast4, view.Masked));
    }
}
