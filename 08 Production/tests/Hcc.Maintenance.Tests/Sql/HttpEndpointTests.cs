using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Maintenance;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Maintenance;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Maintenance.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Maintenance.Tests.Sql;

/// <summary>Bearer token "test-&lt;principal&gt;" is a valid session of that principal (behind the real RequireHccSession filter).</summary>
internal sealed class TestSessions(int propertyId) : ISessionService
{
    public Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (token is { } t && t.StartsWith("test-", StringComparison.Ordinal) && int.TryParse(t.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var principal) && principal > 0)
            return Task.FromResult(new AuthenticatedSession(principal, principal, propertyId, "Test user " + t[5..], CredentialKind.Pin, null, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(12)));
        throw new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.");
    }

    public Task SignOutAsync(string? token, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>The maintenance routes over real HTTP (Kestrel on 127.0.0.1, same DI line and Map call as HccComposition, real policy).</summary>
[Trait("Category", "Sql")]
public class HttpEndpointTests : IAsyncLifetime
{
    private MntWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await MntWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddInventoryModule();
        builder.Services.AddHccHousekeeping();
        builder.Services.AddHccMaintenance();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapMaintenanceEndpoints();
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

    private HttpRequestMessage Req(HttpMethod method, string path, string? user, string? key = null, object? body = null)
    {
        var r = new HttpRequestMessage(method, ApiConventions.BasePath + path);
        if (user is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-" + _w.Users[user].ToString(CultureInfo.InvariantCulture));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return r;
    }

    [Fact]
    public async Task Routes_require_a_session_command_with_a_key_and_hide_money_from_maintenance()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Req(HttpMethod.Get, "/maintenance/work-orders", null), Ct)).StatusCode);

        // Technician: own work orders only, no part prices.
        var tech = await _http.SendAsync(Req(HttpMethod.Get, "/maintenance/work-orders", "maint"), Ct);
        Assert.Equal(HttpStatusCode.OK, tech.StatusCode);
        var body = await tech.Content.ReadAsStringAsync(Ct);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("ownOnly").GetBoolean());
        Assert.All(doc.RootElement.GetProperty("rows").EnumerateArray(), row => Assert.Equal(_w.Users["maint"], row.GetProperty("assigneeId").GetInt32()));
        Assert.DoesNotContain("180000", body, StringComparison.Ordinal);

        // Owner sees the part price; the AMC list hides values from the supervisor.
        var owner = await (await _http.SendAsync(Req(HttpMethod.Get, "/maintenance/work-orders/" + _w.Orders["WO-0901"].ToString(CultureInfo.InvariantCulture), "owner"), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("180000", owner, StringComparison.Ordinal);
        var sup = await (await _http.SendAsync(Req(HttpMethod.Get, "/maintenance/amcs", "supervisor"), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("9600000", sup, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Get, "/maintenance/amcs", "reception"), Ct)).StatusCode);

        // Command: Idempotency-Key required, then created; a replay is flagged.
        var create = new CreateWorkOrderRequest("Corridor light flickering", "P4", null, null, "2nd floor corridor");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/" + MaintenanceCommands.OpCreate, "supervisor", null, create), Ct)).StatusCode);
        var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/" + MaintenanceCommands.OpCreate, "supervisor", "http-1", create), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var result = await ok.Content.ReadFromJsonAsync<WorkOrderResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web), Ct);
        Assert.Equal("Open", result!.Status);
        var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/" + MaintenanceCommands.OpCreate, "supervisor", "http-1", create), Ct);
        Assert.True(replay.Headers.Contains("X-Hcc-Replayed") || replay.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM mnt.work_order WHERE title = N'Corridor light flickering';", Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(Req(HttpMethod.Post, "/commands/" + MaintenanceCommands.OpGeneratePm, "maint", "http-2", new GeneratePmWorkOrdersRequest()), Ct)).StatusCode);
    }
}
