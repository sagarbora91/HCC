using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Ota;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Inventory.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Ota.Tests.Sql;

/// <summary>HTTP smoke over Kestrel with the real access policy: inbox, accept with Idempotency-Key, commission report by role, 401/403/422 behaviour.</summary>
[Trait("Category", "Sql")]
public sealed class OtaHttpTests : OtaSqlTest
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<(WebApplication App, Uri Base)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var platform = F.Db.Platform;
        builder.Services.AddSingleton(platform);
        builder.Services.AddSingleton<SqlConnectionFactory>(platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(F.Clock);
        builder.Services.AddSingleton(F.Queries);
        builder.Services.AddSingleton(F.Service);
        builder.Services.AddSingleton<IAccessPolicy>(F.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(1));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        app.UseMiddleware<HccErrorMiddleware>();
        app.MapGroup(ApiConventions.BasePath).MapOtaEndpoints();
        await app.StartAsync(Ct);
        return (app, new Uri(app.Urls.First()));
    }

    private static HttpClient Client(Uri baseUri, int? principal)
    {
        var http = new HttpClient { BaseAddress = baseUri };
        if (principal is { } p) http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(p));
        return http;
    }

    private static HttpRequestMessage Post(string command, object body, string? key)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, $"{ApiConventions.BasePath}/commands/{command}") { Content = JsonContent.Create(body) };
        if (key is not null) m.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        return m;
    }

    [Fact]
    public async Task Inbox_accept_and_commission_over_http_with_role_checks()
    {
        var (app, uri) = await StartAsync();
        await using var _ = app;
        using var reception = Client(uri, F.Reception);
        using var accountant = Client(uri, F.Accountant);
        using var manager = Client(uri, F.Manager);
        using var anonymous = Client(uri, null);
        var b = ApiConventions.BasePath;

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{b}/ota/channels", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync($"{b}/ota/channels", Ct)).StatusCode);
        var channels = await reception.GetFromJsonAsync<List<ChannelView>>($"{b}/ota/channels", Web, Ct);
        Assert.Equal(4, channels!.Count);
        Assert.Equal(3, channels.Single(c => c.Code == "BOOKNEST").PendingMessages); // F1, F3, F5 (F0 was rejected)
        var inbox = await reception.GetFromJsonAsync<List<ChannelMessageView>>($"{b}/ota/messages?status=Pending", Web, Ct);
        var f1 = inbox!.Single(m => m.ChannelRef == "BN-88231");
        var check = await reception.GetFromJsonAsync<MessageCheckView>($"{b}/ota/messages/{f1.MessageId}/check", Web, Ct);
        Assert.True(check!.Ok);

        var body = new AcceptChannelMessageRequest(f1.MessageId, f1.Version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await reception.SendAsync(Post("AcceptChannelMessage", body, null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.SendAsync(Post("AcceptChannelMessage", body, "http-a0"), Ct)).StatusCode);
        var accepted = await reception.SendAsync(Post("AcceptChannelMessage", body, "http-a1"), Ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var result = (await accepted.Content.ReadFromJsonAsync<AcceptResult>(Web, Ct))!;
        Assert.Equal((MessageStatuses.Accepted, 108000L), (result.Status, result.CommissionPaise));
        var replay = await reception.SendAsync(Post("AcceptChannelMessage", body, "http-a1"), Ct);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        var again = await reception.SendAsync(Post("AcceptChannelMessage", body, "http-a2"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync($"{b}/ota/commission?from=2026-10-01&to=2026-12-31", Ct)).StatusCode);
        var commission = await accountant.GetFromJsonAsync<CommissionReportView>($"{b}/ota/commission?from=2026-10-01&to=2026-12-31", Web, Ct);
        Assert.Equal(108000, commission!.Total.CommissionPaise);
        var rec = await manager.GetFromJsonAsync<ReconciliationView>($"{b}/ota/reconciliation?from=2026-10-01&to=2026-12-31", Web, Ct);
        Assert.Equal((1, 1), (rec!.Bookings, rec.Matched));

        var preview = await reception.GetFromJsonAsync<PushPreviewView>($"{b}/ota/channels/{F.ChannelIds["BOOKNEST"]}/push-preview", Web, Ct);
        Assert.Equal(21, preview!.Rows.Count);
        var bad = await reception.SendAsync(Post("SaveChannel", new SaveChannelRequest(null, null, "XX", "Nope", 100, 0, []), "http-s1"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        var stale = await manager.SendAsync(Post("SetChannelStatus", new SetChannelStatusRequest(F.ChannelIds["BOOKNEST"], Convert.ToBase64String(new byte[8]), "pause"), "http-s2"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var doc = JsonDocument.Parse(await stale.Content.ReadAsStringAsync(Ct));
        Assert.Equal("STALE_VERSION", doc.RootElement.GetProperty("code").GetString());
    }
}
