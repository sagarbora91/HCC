using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Application.Yield;
using Hcc.Contracts;
using Hcc.Import.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Import.Tests.Sql;

/// <summary>The import endpoints over real HTTP behind the session filter, with the real access policy and SQL stores.</summary>
[Trait("Category", "Sql")]
public class ImportHttpTests : IAsyncLifetime
{
    private ImportWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await ImportWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddSingleton<IYieldStore>(_w.Yield);
        builder.Services.AddSingleton<IRatePublicationPort>(_w.Rates);
        builder.Services.AddSingleton<IBillingStore>(_w.Billing);
        builder.Services.AddSingleton<IStaysStore, SqlStaysStore>();
        builder.Services.AddImportModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapImportEndpoints();
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
        if (body is not null) r.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return r;
    }

    [Fact]
    public async Task Upload_preview_and_commit_over_http_with_session_and_permissions()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Req(HttpMethod.Get, "/import/masters", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Get, "/import/masters", "reception"), Ct)).StatusCode);
        using (var masters = JsonDocument.Parse(await (await _http.SendAsync(Req(HttpMethod.Get, "/import/masters", "techadmin"), Ct)).Content.ReadAsStringAsync(Ct)))
        {
            Assert.True(masters.RootElement.GetProperty("setupState").GetBoolean());
            Assert.Equal(9, masters.RootElement.GetProperty("masters").GetArrayLength());
        }

        var body = new { master = "room-types", fileName = "room-types.csv", csvText = Templates.Sample("room-types") };
        var up = await _http.SendAsync(Req(HttpMethod.Post, "/commands/UploadImportBatch", "techadmin", body, "u-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        using var report = JsonDocument.Parse(await up.Content.ReadAsStringAsync(Ct));
        var batchId = report.RootElement.GetProperty("batchId").GetInt64();
        Assert.Equal(3, report.RootElement.GetProperty("counts").GetProperty("create").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/UploadImportBatch", "owner", body), Ct)).StatusCode);   // no Idempotency-Key

        var preview = await _http.SendAsync(Req(HttpMethod.Get, $"/import/batches/{batchId}", "techadmin"), Ct);
        Assert.Contains("\"commitAllowed\":true", await preview.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/CommitImportBatch", "techadmin", new { batchId }, "c-0"), Ct)).StatusCode);
        var commit = await _http.SendAsync(Req(HttpMethod.Post, "/commands/CommitImportBatch", "owner", new { batchId }, "c-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        Assert.Contains("\"committed\":true", await commit.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/CommitImportBatch", "owner", new { batchId }, "c-1"), Ct);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/CommitImportBatch", "owner", new { batchId }, "c-2"), Ct)).StatusCode);
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM inv.room_type"));
        var list = await _http.SendAsync(Req(HttpMethod.Get, "/import/batches", "owner"), Ct);
        Assert.Contains("\"status\":\"Committed\"", await list.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(Req(HttpMethod.Get, "/import/opening-balances", "owner"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(Req(HttpMethod.Get, "/import/ledgers", "owner"), Ct)).StatusCode);
    }
}
