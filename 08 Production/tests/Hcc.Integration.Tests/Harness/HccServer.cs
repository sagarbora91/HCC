using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hcc.Api;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Integration.Tests.Harness;

/// <summary>
/// The whole HCC server (Program.cs composition: <see cref="HccComposition"/>, real P1 sessions + policy, every module, the
/// housekeeping projector worker) on Kestrel 127.0.0.1 (ephemeral port) over a disposable <c>HccTest_*</c> database on
/// <c>.\SQLEXPRESS</c> (env <c>HCC_TEST_SQL_SERVER</c>). Only names matching the pattern are ever dropped.
/// </summary>
public sealed partial class HccServer : IAsyncDisposable
{
    [GeneratedRegex(@"^HccTest_\d{14}_[0-9a-f]{8}$")]
    private static partial Regex NamePattern();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplication? _app;

    private HccServer(string server, string name)
    {
        Server = server;
        Name = name;
        Options = new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" };
        Platform = new SqlPlatform(Options);
    }

    public string Server { get; }

    public string Name { get; }

    public SqlOptions Options { get; }

    public SqlPlatform Platform { get; }

    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>The running server's services (e.g. the A09-04 schema guard).</summary>
    public IServiceProvider Services => _app?.Services ?? throw new InvalidOperationException("The server is not started.");

    public int PropertyId { get; private set; }

    /// <param name="beforeMap">Optional middleware added before the HCC pipeline (e.g. a simulated LAN client address in the setup tests).</param>
    public static async Task<HccServer> StartAsync(string seedSql, CancellationToken ct = default, Action<WebApplication>? beforeMap = null)
    {
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        if (!NamePattern().IsMatch(name)) throw new InvalidOperationException("Unexpected test database name.");
        var h = new HccServer(server, name);
        await using (var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, "master")))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}];", conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await h.Platform.Migrations.MigrateAsync(ct);   // 0001 → latest, every module block
            h.PropertyId = Convert.ToInt32(await h.ScalarAsync(seedSql, ct), CultureInfo.InvariantCulture);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["Hcc:Access:PasswordIterations"] = "20000"; // fast test hashes (stored per credential)
            builder.Configuration["Hcc:Access:PinIterations"] = "20000";
            builder.Services.AddHcc(builder.Configuration, h.Platform);
            h._app = builder.Build();
            beforeMap?.Invoke(h._app);
            h._app.MapHcc();
            await h._app.StartAsync(ct);
            h.BaseAddress = new Uri(h._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
            return h;
        }
        catch
        {
            await h.DisposeAsync();
            throw;
        }
    }

    /// <summary>A browser-like client: the session travels in the HttpOnly cookie set by sign-in.</summary>
    public HttpClient CookieClient() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true }) { BaseAddress = BaseAddress };

    /// <summary>An APK-like client on a registered device: bearer session token + device header.</summary>
    public HttpClient DeviceClient(string deviceToken)
    {
        var c = new HttpClient { BaseAddress = BaseAddress };
        c.DefaultRequestHeaders.Add(AccessConventions.DeviceTokenHeader, deviceToken);
        return c;
    }

    public static void UseBearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var conn = await Platform.Connections.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<int> CountAsync(string sql, CancellationToken ct = default) => Convert.ToInt32(await ScalarAsync(sql, ct), CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }

        if (!NamePattern().IsMatch(Name)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(Server, "master"));
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"""
            IF DB_ID(N'{Name}') IS NOT NULL
            BEGIN
              ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
              DROP DATABASE [{Name}];
            END
            """, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Small JSON-over-HTTP helpers with contract-error messages on failure.</summary>
public static class Wire
{
    private static int _key;

    public static string NewKey(string prefix) => prefix + "-" + Interlocked.Increment(ref _key).ToString(CultureInfo.InvariantCulture);

    public static async Task<HttpResponseMessage> PostAsync(HttpClient c, string path, object body, string? key, CancellationToken ct)
    {
        using var m = new HttpRequestMessage(HttpMethod.Post, ApiConventions.BasePath + path) { Content = JsonContent.Create(body, options: HccServer.Json) };
        if (key is not null) m.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        return await c.SendAsync(m, ct);
    }

    /// <summary>POST /commands/{name} with a fresh Idempotency-Key; asserts 200 and returns the typed result.</summary>
    public static async Task<T> CommandAsync<T>(HttpClient c, string name, object body, CancellationToken ct, string? key = null)
    {
        using var r = await PostAsync(c, "/commands/" + name, body, key ?? NewKey(name), ct);
        return await OkAsync<T>(r, name, ct);
    }

    /// <summary>POST /commands/{name}; asserts the HTTP status and contract error code; returns the error body.</summary>
    public static async Task<ErrorResponse> CommandFailsAsync(HttpClient c, string name, object body, HttpStatusCode status, string code, CancellationToken ct)
    {
        using var r = await PostAsync(c, "/commands/" + name, body, NewKey(name), ct);
        var text = await r.Content.ReadAsStringAsync(ct);
        Assert.True(r.StatusCode == status, $"{name}: expected {(int)status}, got {(int)r.StatusCode}: {text}");
        var err = JsonSerializer.Deserialize<ErrorResponse>(text, HccServer.Json)!;
        Assert.Equal(code, err.Code);
        return err;
    }

    public static async Task<T> GetAsync<T>(HttpClient c, string path, CancellationToken ct)
    {
        using var r = await c.GetAsync(ApiConventions.BasePath + path, ct);
        return await OkAsync<T>(r, "GET " + path, ct);
    }

    public static async Task<T> OkAsync<T>(HttpResponseMessage r, string what, CancellationToken ct)
    {
        var text = await r.Content.ReadAsStringAsync(ct);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{what}: expected 200, got {(int)r.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, HccServer.Json)!;
    }
}
