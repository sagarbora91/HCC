using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Application.Access;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.GuestIds;
using Hcc.Contracts.Stays;
using Hcc.Contracts.Tax;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.GstIds.Tests.Harness;
using Hcc.Infrastructure.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hcc.GstIds.Tests.GuestIds;

/// <summary>Bearer token <c>test-&lt;principal&gt;</c> = a session of that principal (stand-in for P1 sign-in behind the real filter).</summary>
public sealed class TestSessions(int propertyId) : ISessionService
{
    public static string Token(int principalId) => "test-" + principalId.ToString(CultureInfo.InvariantCulture);

    public Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (token is { } t && t.StartsWith("test-", StringComparison.Ordinal) && int.TryParse(t.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0)
            return Task.FromResult(new AuthenticatedSession(p, p, propertyId, "Test " + t[5..], CredentialKind.Password, null, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(12)));
        throw new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.");
    }

    public Task SignOutAsync(string? token, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Captures every log line the host writes (all categories, Trace and up), exception text included.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new L(categoryName, Lines);

    public void Dispose()
    {
    }

    private sealed class L(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => lines.Enqueue($"{logLevel} {category} {formatter(state, exception)} {exception}");
    }
}

/// <summary>
/// The real composition (HccComposition.AddHcc / MapHcc) over HTTP with a configured guest ID key: register with a full Aadhaar
/// number, read, reveal, fail validation, GST master read — then scan responses (except the reveal), every log line, the audit,
/// the outbox and the idempotency records for the number.
/// </summary>
[Trait("Category", "Sql")]
public class GuestIdHttpLeakTests(GstIdsDb db) : IClassFixture<GstIdsDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Number_never_reaches_logs_audit_outbox_idempotency_or_non_reveal_responses()
    {
        var aad = GuestIdUnitTests.Aad;
        var logs = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hcc:GuestIdKeys:Current"] = "dev1",
            ["Hcc:GuestIdKeys:Keys:dev1"] = "plain:" + Convert.ToBase64String(GstIdsDb.Key1),
            ["Hcc:GuestIdKeys:AllowPlainKeys"] = "true",
        });
        builder.Services.AddHcc(builder.Configuration, new SqlPlatform(db.Options));
        builder.Services.AddSingleton<ISessionService>(new TestSessions(db.PropertyId));
        await using var app = builder.Build();
        app.MapHcc();
        await app.StartAsync(Ct);
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var bodies = new List<string>();

        async Task<HttpResponseMessage> Post(string role, string op, object body)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, $"{ApiConventions.BasePath}/commands/{op}") { Content = JsonContent.Create(body, options: opts) };
            r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(db.User[role]));
            r.Headers.Add(ApiConventions.IdempotencyKeyHeader, GstIdsDb.Key());
            return await http.SendAsync(r, Ct);
        }

        async Task<string> Get(string role, string path)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, ApiConventions.BasePath + path);
            r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(db.User[role]));
            var res = await http.SendAsync(r, Ct);
            return await res.Content.ReadAsStringAsync(Ct);
        }

        var rid = await db.LongAsync("""
            INSERT inv.reservation(property_id, public_no, guest_name, arrival, departure, status, nightly_rate_paise)
            OUTPUT INSERTED.reservation_id VALUES (@p, 'LEAK1', N'Fictional guest', '2026-10-24', '2026-10-26', 'Confirmed', 200000);
            """, ("@p", SqlDbType.Int, db.PropertyId));
        var ver = Convert.ToBase64String((byte[])(await db.ScalarAsync($"SELECT row_ver FROM inv.reservation WHERE reservation_id = {rid};"))!);
        var guest = new GuestDraftDto("Leak Guest (fictional)", "+91 98200 07777", null, "7 Demo Lane, Latur (fictional)", "Indian", "Aadhaar (demo)", IdNumber: aad[..4] + " " + aad[4..8] + " " + aad[8..]);

        // Validation failure first (wrong type for the number): 422 without the number.
        var bad = await Post("REC", "RegisterGuest", new RegisterGuestRequest(rid, ver, guest with { IdType = "PAN (demo)" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        bodies.Add(await bad.Content.ReadAsStringAsync(Ct));

        var ok = await Post("REC", "RegisterGuest", new RegisterGuestRequest(rid, ver, guest));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var reg = (await ok.Content.ReadFromJsonAsync<RegisterGuestResult>(opts, Ct))!;
        bodies.Add(JsonSerializer.Serialize(reg));
        foreach (var role in new[] { "OWN", "REC", "CSH", "HKP" }) bodies.Add(await Get(role, $"/guests/{reg.GuestId}"));
        Assert.Contains("XXXX-XXXX-" + aad[^4..], bodies[^3], StringComparison.Ordinal); // REC view

        var forbidden = await Post("CSH", "RevealGuestId", new RevealGuestIdRequest(reg.GuestId));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        bodies.Add(await forbidden.Content.ReadAsStringAsync(Ct));
        var shown = await Post("REC", "RevealGuestId", new RevealGuestIdRequest(reg.GuestId));
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        Assert.Equal("no-store", shown.Headers.CacheControl?.ToString());
        Assert.Equal(aad, (await shown.Content.ReadFromJsonAsync<RevealGuestIdResult>(opts, Ct))!.IdNumber);

        // GST master read over HTTP too (Tech admin read-only).
        var gst = JsonSerializer.Deserialize<GstMasterView>(await Get("TAD", "/tax/gst?date=2026-10-24"), opts)!;
        Assert.False(gst.CanChange);

        await app.StopAsync(Ct);
        Assert.NotEmpty(logs.Lines);
        var grouped = aad[..4] + " " + aad[4..8] + " " + aad[8..];
        foreach (var text in bodies.Concat(logs.Lines))
        {
            Assert.DoesNotContain(aad, text, StringComparison.Ordinal);
            Assert.DoesNotContain(grouped, text, StringComparison.Ordinal);
        }

        var stored = await db.DumpAsync("SELECT * FROM plat.audit_event; SELECT * FROM plat.outbox; SELECT * FROM plat.idempotency;");
        Assert.DoesNotContain(aad, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(grouped, stored, StringComparison.Ordinal);
        Assert.Contains("GUEST_ID_VIEW", stored, StringComparison.Ordinal);
    }
}
