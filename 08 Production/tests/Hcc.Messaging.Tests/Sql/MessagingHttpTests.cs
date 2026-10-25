using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Messaging.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Messaging.Tests.Sql;

/// <summary>The messaging endpoints over real HTTP behind P1's session filter (test session service), the fake policy and the real SQL services.</summary>
[Trait("Category", "Sql")]
public class MessagingHttpTests : IAsyncLifetime
{
    private MsgWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await MsgWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddHccMessaging();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapMessagingEndpoints();
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

    private HttpRequestMessage Req(HttpMethod m, string path, int? principal, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (principal is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(principal.Value));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return r;
    }

    [Fact]
    public async Task Send_then_list_over_http_with_session_idempotency_and_permission_checks()
    {
        var (res, _, _) = await _w.BookingAsync("101", "2026-10-23", "2026-10-26", "Ananya Rao", "9820001041", "a@example.test", inHouse: true, ct: Ct);
        var body = new { reservationId = res, channel = "WhatsApp", body = "Hello {firstName}" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/SendMessage", null, body, "h0"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/SendMessage", Users.Cashier, body, "h1"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/SendMessage", Users.Reception, body), Ct)).StatusCode); // no Idempotency-Key

        var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SendMessage", Users.Reception, body, "h2"), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SendMessage", Users.Reception, body, "h2"), Ct);
        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));

        var list = await _http.SendAsync(Req(HttpMethod.Get, "/messaging/messages?status=Queued", Users.Reception), Ct);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync(Ct));
        Assert.Equal(1, doc.RootElement.GetProperty("total").GetInt32());
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Equal("••••••1041", item.GetProperty("to").GetString());
        Assert.Equal("Hello Ananya", item.GetProperty("body").GetString());
        Assert.True(item.GetProperty("simulated").GetBoolean());

        var summary = await _http.SendAsync(Req(HttpMethod.Get, "/messaging/summary", Users.Manager), Ct);
        Assert.Contains("\"queued\":1", await summary.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Get, "/messaging/summary", Users.Cashier), Ct)).StatusCode);
        var templates = await _http.SendAsync(Req(HttpMethod.Get, "/messaging/templates", Users.Reception), Ct);
        Assert.Contains("booking-confirmation", await templates.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Req(HttpMethod.Get, "/messaging/messages/99999", Users.Reception), Ct)).StatusCode);
    }
}
