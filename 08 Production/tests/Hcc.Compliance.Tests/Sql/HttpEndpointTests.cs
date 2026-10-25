using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Compliance.Tests.Harness;
using Hcc.Contracts;
using Hcc.Contracts.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Compliance.Tests.Sql;

/// <summary>The compliance endpoints over real HTTP (Kestrel on 127.0.0.1): session required, contract errors, Idempotency-Key replay, role split.</summary>
[Trait("Category", "Sql")]
public class HttpEndpointTests : IAsyncLifetime
{
    private ComplianceWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await ComplianceWorld.BaseAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Db.Platform);
        builder.Services.AddSingleton(_w.Db.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Db.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddHccCompliance();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapComplianceEndpoints();
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

    private static HttpRequestMessage Req(HttpMethod m, string path, int? principal, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (principal is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(principal.Value));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }

    [Fact]
    public async Task Calendar_and_commands_over_HTTP()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/compliance/calendar", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Get, "/compliance/calendar", Users.Manager), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, mgr.StatusCode);
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
        }

        using (var badge = await _http.SendAsync(Req(HttpMethod.Get, "/compliance/badge", Users.Manager), Ct))
            Assert.Equal(0, (await badge.Content.ReadFromJsonAsync<BadgeResult>(Web, Ct))!.Count);
        var view = (await (await _http.SendAsync(Req(HttpMethod.Get, "/compliance/calendar", Users.Accountant), Ct)).Content.ReadFromJsonAsync<CalendarView>(Web, Ct))!;
        Assert.Equal(37, view.Items.Count);
        Assert.Equal(1, view.Counts.Overdue);

        var g1 = view.Items.Single(i => i.Kind == "GSTR-1" && i.Period == "2026-09").ObligationId;
        var body = new MarkFiledRequest(g1, "2026-09", "ARN-AA2610-0042");
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceFiled", Users.Accountant, body), Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceFiled", Users.Accountant, body, "h1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(13, (await ok.Content.ReadFromJsonAsync<FilingResult>(Web, Ct))!.DaysLate);
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceFiled", Users.Accountant, body, "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var again = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceFiled", Users.Accountant, body, "h2"), Ct))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var reopen = new ReopenFilingRequest(g1, "2026-09", "Wrong reference");
        using (var acc = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReopenComplianceFiling", Users.Accountant, reopen, "h3"), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, acc.StatusCode);
        using (var owner = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReopenComplianceFiling", Users.Owner, reopen, "h4"), Ct))
            Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        using (var pack = await _http.SendAsync(Req(HttpMethod.Post, "/commands/GenerateCaPack", Users.Accountant, null, "h5"), Ct))
            Assert.Equal(HttpStatusCode.OK, pack.StatusCode);
        var packs = (await (await _http.SendAsync(Req(HttpMethod.Get, "/compliance/ca-packs", Users.Owner), Ct)).Content.ReadFromJsonAsync<List<CaPackDto>>(Web, Ct))!;
        Assert.Single(packs);
    }
}
