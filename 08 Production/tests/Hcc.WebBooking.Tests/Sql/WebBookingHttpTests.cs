using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.WebBooking;
using Hcc.Contracts;
using Hcc.Contracts.WebBooking;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.WebBooking.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.WebBooking.Tests.Sql;

/// <summary>The public routes over Kestrel: anonymous access, rate limit (429), disabled switch (404), no staff data, staff routes need a session.</summary>
[Trait("Category", "Sql")]
public sealed class WebBookingHttpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    private static async Task<(WebApplication App, HttpClient Http)> StartAsync(WebWorld w, WebBookingOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var platform = w.Db.Platform;
        builder.Services.AddSingleton(platform);
        builder.Services.AddSingleton<SqlConnectionFactory>(platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(w.Clock);
        builder.Services.AddInventoryModule();
        builder.Services.AddReservationsModule();
        builder.Services.AddBillingModule();
        builder.Services.AddWebBookingModule();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IAccessPolicy>(w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        app.UseMiddleware<HccErrorMiddleware>();
        var api = app.MapGroup(ApiConventions.BasePath);
        api.MapWebBookingEndpoints();
        await app.StartAsync(Ct);
        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    private static HttpRequestMessage Post(string path, object body, string? key = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, ApiConventions.BasePath + path) { Content = JsonContent.Create(body) };
        if (key is not null) m.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        return m;
    }

    [Fact]
    public async Task Anonymous_quote_request_and_deposit_page_without_staff_data()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var (app, http) = await StartAsync(w, new WebBookingOptions { Enabled = true });
        await using var _ = app;
        using var __ = http;

        var q = await http.GetAsync(ApiConventions.BasePath + "/public/booking/quote?arrival=2026-10-25&departure=2026-10-27&adults=2&roomType=Suite", Ct);
        Assert.Equal(HttpStatusCode.OK, q.StatusCode);
        Assert.Equal("no-store", q.Headers.CacheControl?.ToString());
        var body = await q.Content.ReadAsStringAsync(Ct);
        Assert.Contains("945000", body, StringComparison.Ordinal);
        Assert.DoesNotContain("roomNo", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guest", body, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.SendAsync(Post("/public/booking/requests", WebWorld.Request()), Ct)).StatusCode);   // Idempotency-Key required
        var res = await http.SendAsync(Post("/public/booking/requests", WebWorld.Request(), "web-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var receipt = (await res.Content.ReadFromJsonAsync<WebRequestReceipt>(Opts, Ct))!;
        Assert.Equal(("WEB-0001", "Pending", 945_000L), (receipt.RequestNo, receipt.Status, receipt.TotalPaise));
        var replay = await http.SendAsync(Post("/public/booking/requests", WebWorld.Request(), "web-1"), Ct);
        Assert.True(replay.Headers.Contains(HccReplayHeader));
        Assert.Equal(1, await w.Db.CountAsync("SELECT COUNT(*) FROM web.request;"));

        // Staff routes need a session; the public ones never accept one for anything.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(ApiConventions.BasePath + "/web-booking/requests", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Post("/commands/ConfirmWebRequest", new ConfirmWebRequestRequest(1), "x"), Ct)).StatusCode);
        var staff = new HttpRequestMessage(HttpMethod.Get, ApiConventions.BasePath + "/web-booking/requests");
        staff.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(Who.Reception));
        var list = await http.SendAsync(staff, Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, (await list.Content.ReadFromJsonAsync<WebRequestList>(Opts, Ct))!.Pending);
        var denied = new HttpRequestMessage(HttpMethod.Get, ApiConventions.BasePath + "/web-booking/requests");
        denied.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(Who.Housekeeper));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(denied, Ct)).StatusCode);

        // Deposit page by token: shows the amount, never a reservation or folio number.
        var id = await w.RequestIdAsync("WEB-0001");
        await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "c1"), new(id), Ct);
        var link = (await w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l1"), new(id), Ct)).Value;
        var page = await http.GetStringAsync(ApiConventions.BasePath + "/public/booking/deposit/" + link.Token, Ct);
        Assert.Contains("283500", page, StringComparison.Ordinal);
        Assert.DoesNotContain("HCC-", page, StringComparison.Ordinal);
        Assert.DoesNotContain("folio", page, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(ApiConventions.BasePath + "/public/booking/deposit/" + WebBookingTokens.Other, Ct)).StatusCode);
    }

    private const string HccReplayHeader = AccessEndpoints.ReplayedHeader;

    [Fact]
    public async Task Public_routes_are_rate_limited_and_off_when_disabled()
    {
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var (app, http) = await StartAsync(w, new WebBookingOptions { Enabled = true, QuotesPerMinute = 3, RequestsPerTenMinutes = 2 });
        await using var _ = app;
        using var __ = http;
        const string url = "/public/booking/quote?arrival=2026-10-25&departure=2026-10-26&adults=1";
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(ApiConventions.BasePath + url, Ct)).StatusCode);
        var limited = await http.GetAsync(ApiConventions.BasePath + url, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Post("/public/booking/requests", WebWorld.Request(phone: "98000123" + (50 + i)), "k" + i), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.SendAsync(Post("/public/booking/requests", WebWorld.Request(phone: "9800012360"), "k9"), Ct)).StatusCode);

        var (app2, http2) = await StartAsync(w, new WebBookingOptions { Enabled = false });
        await using var _2 = app2;
        using var __2 = http2;
        Assert.Equal(HttpStatusCode.NotFound, (await http2.GetAsync(ApiConventions.BasePath + url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http2.SendAsync(Post("/public/booking/requests", WebWorld.Request(), "z"), Ct)).StatusCode);
    }
}
