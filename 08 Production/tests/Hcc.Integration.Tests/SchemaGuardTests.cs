using System.Net;
using System.Text.Json;
using Hcc.Api.Hosting;
using Hcc.Contracts;
using Hcc.Integration.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Integration.Tests;

/// <summary>
/// A09-04: the server refuses to serve while the database schema is behind the build (the installer started the new build before
/// <c>Hcc.Api.exe migrate</c>). Health says why (counts only for the server PC), other API routes answer 503 with the same reason,
/// discovery still answers, the "current" result is cached (no query per request), and the server recovers once the schema is current.
/// </summary>
[Trait("Category", "Sql")]
public sealed class SchemaGuardTests
{
    private const string LanHeader = "X-Test-Lan-Client";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        SELECT SCOPE_IDENTITY();
        """;

    private static Task<HccServer> ServerAsync() => HccServer.StartAsync(Seed, Ct, app => app.Use(async (http, next) =>
    {
        if (http.Request.Headers.TryGetValue(LanHeader, out var ip)) http.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
        await next(http);
    }));

    private static async Task<(HttpStatusCode Status, ErrorResponse? Error)> GetAsync(HttpClient c, string path)
    {
        using var r = await c.GetAsync(ApiConventions.BasePath + path, Ct);
        var text = await r.Content.ReadAsStringAsync(Ct);
        return (r.StatusCode, r.StatusCode == HttpStatusCode.ServiceUnavailable ? JsonSerializer.Deserialize<ErrorResponse>(text, HccServer.Json) : null);
    }

    [Fact]
    public async Task Schema_behind_the_build_answers_503_with_the_migrate_message_and_recovers_when_current()
    {
        await using var s = await ServerAsync();
        using var serverPc = s.CookieClient();
        using var lan = s.CookieClient();
        lan.DefaultRequestHeaders.Add(LanHeader, "192.168.1.50");
        var guard = s.Services.GetRequiredService<SchemaGuard>();
        var build = await s.CountAsync("SELECT COUNT(*) FROM dbo.schema_migration;", Ct);

        // Current: everything is served (an anonymous protected route answers 401, not 503).
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(serverPc, "/system/health")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(lan, "/me/capabilities")).Status);

        // The newest migration disappears from the journal (as if the new build started before migrate).
        var latest = await s.CountAsync("SELECT MAX(version) FROM dbo.schema_migration;", Ct);
        var row = (string)(await s.ScalarAsync($"SELECT name + '|' + checksum_sha256 FROM dbo.schema_migration WHERE version = {latest};", Ct))!;
        await s.ScalarAsync($"DELETE dbo.schema_migration WHERE version = {latest};", Ct);

        // "Current" is cached for the life of the process: no journal query per request.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(serverPc, "/system/health")).Status);

        guard.Invalidate();   // a fresh process (the upgraded service starting) reads the journal again
        var health = await GetAsync(serverPc, "/system/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, health.Status);
        Assert.Equal("UNAVAILABLE", health.Error!.Code);
        Assert.Equal($"Database schema is behind this build: run `Hcc.Api migrate` (applied {build - 1} of {build}).", health.Error.Message);
        Assert.Equal("SCHEMA_BEHIND", health.Error.Details!["reason"].GetString());
        Assert.Equal(build - 1, health.Error.Details["appliedMigrations"].GetInt32());

        var lanHealth = await GetAsync(lan, "/system/health");   // F-L01: no migration counts on the LAN
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lanHealth.Status);
        Assert.Equal(SchemaGuard.PublicMessage, lanHealth.Error!.Message);
        Assert.False(lanHealth.Error.Details!.ContainsKey("appliedMigrations"));

        var other = await GetAsync(lan, "/me/capabilities");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, other.Status);
        Assert.Equal("SCHEMA_BEHIND", other.Error!.Details!["reason"].GetString());
        using (var post = await Wire.PostAsync(serverPc, "/auth/sign-in", new { loginName = "owner", password = "x" }, null, Ct))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await serverPc.GetAsync(ApiConventions.BasePath + "/system/discovery", Ct)).StatusCode);

        // migrate has run: the server serves again by itself.
        var parts = row.Split('|');
        await s.ScalarAsync($"INSERT dbo.schema_migration(version, name, checksum_sha256) VALUES ({latest}, '{parts[0]}', '{parts[1]}');", Ct);
        guard.Invalidate();   // instead of waiting SchemaGuard.RecheckInterval
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(serverPc, "/system/health")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(lan, "/me/capabilities")).Status);
    }
}
