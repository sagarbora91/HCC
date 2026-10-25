using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Inventory;
using Hcc.Contracts.Reservations;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Inventory.Tests.Harness;
using Hcc.Inventory.Tests.Sql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Inventory.Tests.Api;

/// <summary>
/// HTTP smoke test of the P2 endpoints on Kestrel (no extra packages): availability → quote → book → details, RESTRICTED body
/// with details.violations[], missing Idempotency-Key, replay header, and the error middleware's status codes.
/// </summary>
[Trait("Category", "Sql")]
public sealed class InventoryHttpTests(GoldenDatabaseFixture f) : IClassFixture<GoldenDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(WebApplication App, HttpClient Http)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var platform = f.Db.Platform;
        builder.Services.AddSingleton(platform);
        builder.Services.AddSingleton<SqlConnectionFactory>(platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(f.Services.Clock);
        builder.Services.AddInventoryModule();
        builder.Services.AddReservationsModule();
        builder.Services.AddBillingModule(); // tax rates + folio money check
        builder.Services.AddSingleton<IAccessPolicy>(new FakeAccessPolicy());
        builder.Services.AddSingleton<ISessionService>(new TestSessions(1));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        app.UseMiddleware<HccErrorMiddleware>();
        var api = app.MapGroup(ApiConventions.BasePath);
        api.MapInventoryEndpoints();
        api.MapReservationsEndpoints();
        await app.StartAsync(Ct);
        var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(3));
        return (app, http);
    }

    private static HttpRequestMessage Post(string command, object body, string? key = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, $"{ApiConventions.BasePath}/commands/{command}") { Content = JsonContent.Create(body) };
        if (key is not null) m.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        return m;
    }

    [Fact]
    public async Task Availability_quote_book_and_details_over_http()
    {
        var (app, http) = await StartAsync();
        await using var _ = app;
        using var __ = http;
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var avail = await http.GetFromJsonAsync<AvailabilityResult>($"{ApiConventions.BasePath}/availability?arrival=2026-10-24&departure=2026-10-25&adults=2", opts, Ct);
        Assert.Equal("Blocked", avail!.Rooms.Single(r => r.RoomNo == "308").Reasons[0]);
        Assert.True(avail.Rooms.Single(r => r.RoomNo == "105").Available);

        var quoteRes = await http.SendAsync(Post("QuoteStay", new QuoteStayRequest("2026-11-25", "2026-11-27", 2, 0, f.RoomIds["105"])), Ct);
        Assert.Equal(HttpStatusCode.OK, quoteRes.StatusCode);
        var quote = (await quoteRes.Content.ReadFromJsonAsync<QuoteResult>(opts, Ct))!;
        Assert.Equal((20000L, 420000L), (quote.TaxPaise, quote.TotalPaise)); // DEC-069 GST master: 2 × ₹2,000 at 5 % (real resolver via AddBillingModule)

        var body = new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Http Guest"), f.RoomIds["105"]);
        var noKey = await http.SendAsync(Post("CreateReservation", body), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        var created = await http.SendAsync(Post("CreateReservation", body, "http-book-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var res = (await created.Content.ReadFromJsonAsync<ReservationResult>(opts, Ct))!;
        var replay = await http.SendAsync(Post("CreateReservation", body, "http-book-1"), Ct);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());

        var details = await http.GetFromJsonAsync<ReservationDetails>($"{ApiConventions.BasePath}/reservations/{res.ReservationId}", opts, Ct);
        Assert.Equal(("105", 2, 420000L), (details!.RoomNo, details.Nights.Count, details.TotalPaise));
        var missing = await http.GetAsync($"{ApiConventions.BasePath}/reservations/99999999", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Restricted_booking_returns_409_with_violations_and_forbidden_is_403()
    {
        var (app, http) = await StartAsync();
        await using var _ = app;
        using var __ = http;
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var quote = (await (await http.SendAsync(Post("QuoteStay", new QuoteStayRequest("2026-11-07", "2026-11-08", 1, 0, f.RoomIds["105"])), Ct))
            .Content.ReadFromJsonAsync<QuoteResult>(opts, Ct))!;
        Assert.Equal("MIN_STAY", quote.Violations.Single().Code);
        var r = await http.SendAsync(Post("CreateReservation", new CreateReservationRequest(quote.QuoteId, quote.Version, new GuestInput("Http Guest"), f.RoomIds["105"]), "http-restricted"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(Ct));
        Assert.Equal("RESTRICTED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("MIN_STAY", doc.RootElement.GetProperty("details").GetProperty("violations")[0].GetProperty("code").GetString());

        using var hk = new HttpRequestMessage(HttpMethod.Get, $"{ApiConventions.BasePath}/availability?arrival=2026-10-24&departure=2026-10-25&adults=1");
        hk.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(4));
        using var other = new HttpClient { BaseAddress = http.BaseAddress };
        var denied = await other.SendAsync(hk, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var anonymous = await other.GetAsync($"{ApiConventions.BasePath}/availability?arrival=2026-10-24&departure=2026-10-25&adults=1", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
