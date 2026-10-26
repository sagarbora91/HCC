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
        builder.Services.AddHccConfiguration(); // DEC-057 / A11-01: the module gate on the command routes (every module on by default)
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

    /// <summary>Master configuration › Modules on/off for the public hotel, as the hub stores it (cfg.setting + one applied change; newest wins).</summary>
    private static async Task SetSiteAsync(WebWorld w, bool on)
    {
        var login = "cfg-" + Guid.NewGuid().ToString("N")[..12];
        var by = await w.Db.LongAsync($"INSERT plat.app_user(property_id, login_name, display_name, created_utc) OUTPUT INSERTED.user_id VALUES ({w.PropertyId}, '{login}', N'Owner (test)', '2026-01-01');");
        await w.Db.ExecAsync($"IF NOT EXISTS (SELECT 1 FROM cfg.setting WHERE property_id = {w.PropertyId} AND setting_key = 'module.website') " +
            $"INSERT cfg.setting(property_id, setting_key, created_utc, changed_utc) VALUES ({w.PropertyId}, 'module.website', '2026-10-24', '2026-10-24');" +
            "INSERT cfg.setting_change(property_id, setting_key, effective_from, value_json, before_json, before_text, after_text, source, changed_by, changed_utc) " +
            $"VALUES ({w.PropertyId}, 'module.website', '2026-10-24', N'{(on ? "true" : "false")}', N'{(on ? "false" : "true")}', N'{(on ? "Off" : "On")}', N'{(on ? "On" : "Off")}', 'change', {by}, '2026-10-24');");
    }

    private static HttpRequestMessage Staff(HttpMethod method, string path, int who, object? body = null)
    {
        var m = new HttpRequestMessage(method, ApiConventions.BasePath + path);
        m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(who));
        if (body is not null)
        {
            m.Content = JsonContent.Create(body);
            m.Headers.Add(ApiConventions.IdempotencyKeyHeader, Guid.NewGuid().ToString("N"));
        }

        return m;
    }

    [Fact]
    public async Task Booking_site_switched_off_in_Master_configuration_closes_the_public_routes_and_refuses_staff_commands()
    {
        // DEC-057 / A11-01: "Direct booking site" off → the public site answers exactly as when it is disabled (no internal wording for
        // guests), staff commands are refused with MODULE_OFF, the staff queue stays readable; switched back on, everything works again.
        await using var w = await WebWorld.CreateAsync(ct: Ct);
        var (app, http) = await StartAsync(w, new WebBookingOptions { Enabled = true });
        await using var _ = app;
        using var __ = http;
        const string url = "/public/booking/quote?arrival=2026-10-25&departure=2026-10-27&adults=2&roomType=Suite";
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(ApiConventions.BasePath + url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Post("/public/booking/requests", WebWorld.Request(), "on-1"), Ct)).StatusCode);
        var id = await w.RequestIdAsync("WEB-0001");
        await w.Web.ConfirmAsync(w.Ctx(Who.Reception, "c1"), new(id), Ct);
        var link = (await w.Web.CreateDepositLinkAsync(w.Ctx(Who.Reception, "l1"), new(id), Ct)).Value;

        await SetSiteAsync(w, on: false);
        foreach (var r in new[]
        {
            await http.GetAsync(ApiConventions.BasePath + url, Ct),
            await http.SendAsync(Post("/public/booking/requests", WebWorld.Request(phone: "9800012399"), "off-1"), Ct),
            await http.GetAsync(ApiConventions.BasePath + "/public/booking/deposit/" + link.Token, Ct),
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            var e = (await r.Content.ReadFromJsonAsync<ErrorResponse>(Opts, Ct))!;
            Assert.Equal(("NOT_FOUND", "Online booking is not available."), (e.Code, e.Message));
        }

        Assert.Equal(1, await w.Db.CountAsync("SELECT COUNT(*) FROM web.request;"));
        var refused = await http.SendAsync(Staff(HttpMethod.Post, "/commands/DeclineWebRequest", Who.Reception, new DeclineWebRequestRequest(id, "Guest cancelled")), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var err = (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Opts, Ct))!;
        Assert.Equal("Direct booking site is switched off in Master configuration › Modules on/off. Its data is kept; the Owner or Tech admin can switch it back on.", err.Message);
        Assert.Equal(("MODULE_OFF", "website"), (err.Details!["reason"].GetString(), err.Details["moduleId"].GetString()));
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Staff(HttpMethod.Get, "/web-booking/requests", Who.Reception), Ct)).StatusCode);

        await SetSiteAsync(w, on: true);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(ApiConventions.BasePath + url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(ApiConventions.BasePath + "/public/booking/deposit/" + link.Token, Ct)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await http.SendAsync(Staff(HttpMethod.Post, "/commands/DeclineWebRequest", Who.Reception, new DeclineWebRequestRequest(id, "Guest cancelled")), Ct)).StatusCode);
    }
}
