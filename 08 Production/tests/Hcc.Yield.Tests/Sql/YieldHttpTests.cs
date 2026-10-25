using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Yield;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Inventory.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Yield.Tests.Sql;

/// <summary>HTTP smoke over Kestrel with the real access policy: calendar, Owner publish, Manager propose, Owner decide, 401/403/422 behaviour.</summary>
[Trait("Category", "Sql")]
public sealed class YieldHttpTests : YieldSqlTest
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
        builder.Services.AddYieldModule();
        builder.Services.AddSingleton<IAccessPolicy>(F.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(1));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        app.UseMiddleware<HccErrorMiddleware>();
        app.MapGroup(ApiConventions.BasePath).MapYieldEndpoints();
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
    public async Task Calendar_propose_decide_and_publish_over_http_with_role_checks()
    {
        var (app, uri) = await StartAsync();
        await using var _ = app;
        using var owner = Client(uri, F.Owner);
        using var manager = Client(uri, F.Manager);
        using var reception = Client(uri, F.Reception);
        using var anonymous = Client(uri, null);

        var cal = await manager.GetFromJsonAsync<YieldCalendarResult>($"{ApiConventions.BasePath}/yield/calendar?nights=14", Web, Ct);
        Assert.Equal(220000, cal!.Cells.Single(c => c.RoomType == "Classic" && c.Date == "2026-10-24").SuggestedPaise);
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync($"{ApiConventions.BasePath}/yield/calendar", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{ApiConventions.BasePath}/yield/config", Ct)).StatusCode);

        var types = new[] { F.TypeIds["Classic"] };
        var body = new ProposeYieldPublishRequest("2026-10-30", "2026-11-01", types, "Weekend demand");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.SendAsync(Post("ProposeYieldPublish", body, null), Ct)).StatusCode); // Idempotency-Key required
        var proposed = await manager.SendAsync(Post("ProposeYieldPublish", body, "http-p1"), Ct);
        Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
        var p = (await proposed.Content.ReadFromJsonAsync<YieldProposalResult>(Web, Ct))!;
        var replay = await manager.SendAsync(Post("ProposeYieldPublish", body, "http-p1"), Ct);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(Post("PublishYieldTariffs", new PublishYieldRequest("2026-10-30", "2026-11-01", types, null), "http-x"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(Post("DecideYieldProposal", new DecideYieldProposalRequest(p.ProposalId, p.ExpectedVersion, true, null), "http-d0"), Ct)).StatusCode);
        var decided = await owner.SendAsync(Post("DecideYieldProposal", new DecideYieldProposalRequest(p.ProposalId, p.ExpectedVersion, true, null), "http-d1"), Ct);
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal("Executed", (await decided.Content.ReadFromJsonAsync<YieldDecisionResult>(Web, Ct))!.Status);

        var proposals = await owner.GetFromJsonAsync<List<YieldProposalView>>($"{ApiConventions.BasePath}/yield/proposals", Web, Ct);
        Assert.Equal("Executed", proposals!.Single().Status);
        var quote = await owner.GetFromJsonAsync<YieldQuoteResult>($"{ApiConventions.BasePath}/yield/quote?planCode=BAR&roomTypeId={types[0]}&arrival=2026-10-31&nights=1", Web, Ct);
        Assert.Equal(240000, quote!.TotalPaise);

        var stale = await owner.SendAsync(Post("SetYieldGuardrails", new SetYieldGuardrailsRequest(-500, 4000, 5000, Convert.ToBase64String(new byte[8]), null), "http-g"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var doc = JsonDocument.Parse(await stale.Content.ReadAsStringAsync(Ct));
        Assert.Equal("STALE_VERSION", doc.RootElement.GetProperty("code").GetString());
    }
}
