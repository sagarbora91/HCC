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
    public async Task State_views_and_commands_over_HTTP()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/compliance/state", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using (var mgr = await _http.SendAsync(Req(HttpMethod.Get, "/compliance/state", Users.Manager), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, mgr.StatusCode);
            Assert.Equal("FORBIDDEN", (await mgr.Content.ReadFromJsonAsync<ErrorResponse>(Ct))!.Code);
        }

        using (var badge = await _http.SendAsync(Req(HttpMethod.Get, "/compliance/badge", Users.Manager), Ct))
            Assert.Equal(0, (await badge.Content.ReadFromJsonAsync<BadgeResult>(Web, Ct))!.Count);
        var state = (await (await _http.SendAsync(Req(HttpMethod.Get, "/compliance/state", Users.Accountant), Ct)).Content.ReadFromJsonAsync<ComplianceStateView>(Web, Ct))!;
        Assert.Equal((2026, 55, "regular"), (state.FyStart, state.Records.Count, state.Profile!.GstType));
        var month = (await (await _http.SendAsync(Req(HttpMethod.Get, "/compliance/month?fy=2026&month=October", Users.Accountant), Ct)).Content.ReadFromJsonAsync<MonthView>(Web, Ct))!;
        Assert.Equal(1, month.Stats.Overdue);
        var ac = (await (await _http.SendAsync(Req(HttpMethod.Get, "/compliance/action-center", Users.Owner), Ct)).Content.ReadFromJsonAsync<ActionCenterView>(Web, Ct))!;
        Assert.Equal(11, ac.Rows.Count);
        foreach (var path in new[] { "/compliance/dashboard?q=Q3", "/compliance/register", "/compliance/print?month=October", "/compliance/payable", "/compliance/evidence-zip", "/compliance/due-this-week" })
            using (var r = await _http.SendAsync(Req(HttpMethod.Get, path, Users.Owner), Ct)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = new MarkDoneRequest(2026, "oc4", null, "ARN-AA2610-0042");
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceDone", Users.Accountant, body), Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceDone", Users.Accountant, body, "h1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(13, (await ok.Content.ReadFromJsonAsync<MarkDoneResult>(Web, Ct))!.DaysLate);
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceDone", Users.Accountant, body, "h1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var again = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceDone", Users.Accountant, body, "h2"), Ct))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var reopen = new ReopenItemRequest(2026, "oc4", "Wrong reference");
        using (var acc = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReopenComplianceItem", Users.Accountant, reopen, "h3"), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, acc.StatusCode);
        using (var owner = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReopenComplianceItem", Users.Owner, reopen, "h4"), Ct))
            Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        using (var na = await _http.SendAsync(Req(HttpMethod.Post, "/commands/MarkComplianceNotApplicable", Users.Accountant, new MarkNotApplicableRequest(2026, "oc2"), "h5"), Ct))
            Assert.Equal(HttpStatusCode.OK, na.StatusCode);
        using (var ev = await _http.SendAsync(Req(HttpMethod.Post, "/commands/AddComplianceEvidence", Users.Accountant, new AddEvidenceRequest(2026, "oc4", "a.pdf", "application/pdf", "JVBERg=="), "h6"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ev.StatusCode);
            var id = (await ev.Content.ReadFromJsonAsync<EvidenceResult>(Web, Ct))!.EvidenceId;
            var file = (await (await _http.SendAsync(Req(HttpMethod.Get, "/compliance/evidence/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture), Users.Owner), Ct)).Content.ReadFromJsonAsync<EvidenceFileView>(Web, Ct))!;
            Assert.Equal("JVBERg==", file.Base64);
        }

        using (var fy = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SetComplianceFy", Users.Accountant, new SetFyRequest(2027), "h7"), Ct))
            Assert.Equal(HttpStatusCode.OK, fy.StatusCode);
        using (var pack = await _http.SendAsync(Req(HttpMethod.Post, "/commands/GenerateCaPack", Users.Accountant, null, "h8"), Ct))
            Assert.Equal(HttpStatusCode.OK, pack.StatusCode);
        var packs = (await (await _http.SendAsync(Req(HttpMethod.Get, "/compliance/ca-packs", Users.Owner), Ct)).Content.ReadFromJsonAsync<List<CaPackDto>>(Web, Ct))!;
        Assert.Single(packs);
    }
}
