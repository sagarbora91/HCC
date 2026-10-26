using System.Net;
using System.Text.Json;
using Hcc.Api;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Integration.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Hcc.Integration.Tests;

/// <summary>
/// Security review 2026-10-26 server fixes over HTTP with the real composition: sign-in throttling per client address that never locks
/// the Owner out from the server PC (F-M02), uniform sign-in failures (F-M01), device-bound PIN sessions (F-M07), anonymous health
/// without database details (F-L01) and 500 errors without exception text (F-L07).
/// </summary>
[Trait("Category", "Sql")]
public sealed class SecurityHardeningTests
{
    private const string LanHeader = "X-Test-Lan-Client";
    private const string OwnerLogin = "owner.sagar";
    private const string OwnerPassword = "Maroon&Sand#Ledger26";
    private const string ReceptionPin = "472913";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Seed = """
        INSERT inv.property(code, name, time_zone, business_date) VALUES ('DEMO', N'HCC Demo Residency (fictional)', 'Asia/Kolkata', '2026-10-24');
        SELECT SCOPE_IDENTITY();
        """;

    /// <summary>A request carrying <see cref="LanHeader"/> arrives from the LAN address in the header's value.</summary>
    private static Task<HccServer> ServerAsync() => HccServer.StartAsync(Seed, Ct, app => app.Use(async (http, next) =>
    {
        if (http.Request.Headers.TryGetValue(LanHeader, out var ip)) http.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
        await next(http);
    }));

    private static HttpClient Lan(HccServer s, string ip = "192.168.1.50", string? device = null)
    {
        var c = device is null ? s.CookieClient() : s.DeviceClient(device);
        c.DefaultRequestHeaders.Add(LanHeader, ip);
        return c;
    }

    private static async Task<HttpClient> OwnerAsync(HccServer s)
    {
        var owner = s.CookieClient();
        using (var boot = await Wire.PostAsync(owner, "/auth/bootstrap-owner", new BootstrapOwnerRequest(s.PropertyId, OwnerLogin, "Sagar (Owner)", OwnerPassword), null, Ct))
            await Wire.OkAsync<UserVersionResult>(boot, "bootstrap-owner", Ct);
        using (var r = await Wire.PostAsync(owner, "/auth/sign-in", new SignInRequest(OwnerLogin, Password: OwnerPassword), null, Ct))
            await Wire.OkAsync<SignInResult>(r, "owner sign-in", Ct);
        return owner;
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient c, SignInRequest body) => await Wire.PostAsync(c, "/auth/sign-in", body, null, Ct);

    private static async Task<ErrorResponse> ErrorAsync(HttpResponseMessage r) =>
        JsonSerializer.Deserialize<ErrorResponse>(await r.Content.ReadAsStringAsync(Ct), HccServer.Json)!;

    [Fact]
    public async Task LAN_attacker_is_throttled_but_the_Owner_still_signs_in_from_the_server_PC_and_other_devices()
    {
        await using var s = await ServerAsync();
        using var owner = await OwnerAsync(s);
        using var attacker = Lan(s);

        // Five wrong passwords for the Owner from one LAN address: that (address, name) pair is throttled …
        for (var i = 0; i < 5; i++)
        {
            using var bad = await SignInAsync(attacker, new SignInRequest(OwnerLogin, Password: "Wrong-password-" + i));
            Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
            Assert.Equal("BAD_CREDENTIALS", (await ErrorAsync(bad)).Details!["reason"].GetString());
        }

        using (var limited = await SignInAsync(attacker, new SignInRequest(OwnerLogin, Password: OwnerPassword)))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.True(limited.Headers.RetryAfter is not null);
            Assert.Equal("TOO_MANY_ATTEMPTS", (await ErrorAsync(limited)).Details!["reason"].GetString());
        }

        // … but the Owner is not locked out: the server PC (loopback) and another LAN device still sign in.
        using (var serverPc = s.CookieClient())
        using (var ok = await SignInAsync(serverPc, new SignInRequest(OwnerLogin, Password: OwnerPassword)))
            await Wire.OkAsync<SignInResult>(ok, "owner on the server PC", Ct);
        using (var laptop = Lan(s, "192.168.1.60"))
        using (var ok = await SignInAsync(laptop, new SignInRequest(OwnerLogin, Password: OwnerPassword)))
            await Wire.OkAsync<SignInResult>(ok, "owner on another device", Ct);

        // Spraying other names from the attacker's address: 20 failures in all, then the whole address gets 429.
        for (var i = 0; i < 15; i++)
        {
            using var bad = await SignInAsync(attacker, new SignInRequest("spray." + i, Password: "Wrong-password-1"));
            Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        }

        using (var blocked = await SignInAsync(attacker, new SignInRequest("someone.else", Password: "Wrong-password-1")))
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(20, await s.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE action = 'SIGN_IN_FAILED';", Ct));   // throttled tries never reach the database
    }

    [Fact]
    public async Task PIN_bearer_token_works_only_with_its_device_header_and_failures_do_not_reveal_accounts()
    {
        await using var s = await ServerAsync();
        using var owner = await OwnerAsync(s);
        var rec = await Wire.CommandAsync<UserVersionResult>(owner, "CreateUser", new CreateUserRequest("rec.asha", "Asha (Reception)", [new RoleAssignmentInput("REC")]), Ct);
        await Wire.CommandAsync<UserVersionResult>(owner, "SetCredential", new SetCredentialRequest(rec.UserId, null, ReceptionPin, rec.Version), Ct);
        var device = (await Wire.CommandAsync<RegisterDeviceResult>(owner, "RegisterDevice", new RegisterDeviceRequest("Front desk tablet"), Ct)).DeviceToken!;

        using var tablet = Lan(s, "192.168.1.70", device);
        string token;
        using (var r = await SignInAsync(tablet, new SignInRequest("rec.asha", Pin: ReceptionPin, ReturnToken: true)))
            token = (await Wire.OkAsync<SignInResult>(r, "PIN sign-in", Ct)).SessionToken!;
        HccServer.UseBearer(tablet, token);
        await Wire.GetAsync<CapabilitiesResult>(tablet, "/me/capabilities", Ct);

        // The same bearer token from another host without (or with another) device token: 401, and the tablet keeps working.
        using (var thief = Lan(s, "192.168.1.99"))
        {
            HccServer.UseBearer(thief, token);
            using var r = await thief.GetAsync(ApiConventions.BasePath + "/me/capabilities", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal("DEVICE_MISMATCH", (await ErrorAsync(r)).Details!["sessionEnd"].GetString());
        }

        await Wire.GetAsync<CapabilitiesResult>(tablet, "/me/capabilities", Ct);

        // Unknown name, wrong PIN, and PIN for the Owner (password account) look the same on the wire.
        using var probe = Lan(s, "192.168.1.71", device);
        var bodies = new List<string>();
        foreach (var body in new[] { new SignInRequest("nobody.here", Pin: ReceptionPin), new SignInRequest("rec.asha", Pin: "305718"), new SignInRequest(OwnerLogin, Pin: ReceptionPin) })
        {
            using var r = await SignInAsync(probe, body);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            bodies.Add(await r.Content.ReadAsStringAsync(Ct));
        }

        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task Anonymous_health_has_no_database_details_the_server_PC_and_signed_in_admins_do()
    {
        await using var s = await ServerAsync();
        using (var lan = Lan(s))
        {
            var h = await Wire.GetAsync<HealthResult>(lan, "/system/health", Ct);
            Assert.Equal(("ok", (string?)null, (int?)null), (h.Status, h.Database, h.SchemaVersion));
            Assert.False(string.IsNullOrEmpty(h.ServerTimeUtc));   // the clients' clock
        }

        using (var proxied = s.CookieClient())
        {
            proxied.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");   // loopback via a local proxy is not "the server PC"
            Assert.Null((await Wire.GetAsync<HealthResult>(proxied, "/system/health", Ct)).Database);
        }

        using var serverPc = s.CookieClient();
        var local = await Wire.GetAsync<HealthResult>(serverPc, "/system/health", Ct);
        Assert.Equal(s.Name, local.Database);
        Assert.True(local.SchemaVersion > 0);

        using var owner = await OwnerAsync(s);
        owner.DefaultRequestHeaders.Add(LanHeader, "192.168.1.20");   // the Owner's session from a LAN device
        Assert.Equal(s.Name, (await Wire.GetAsync<HealthResult>(owner, "/system/health", Ct)).Database);
    }

    private sealed class CapturingLogger : ILogger<HccErrorMiddleware>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
        }
    }

    [Fact]
    public async Task Server_errors_log_a_correlation_id_without_the_exception_text_and_answer_generically()
    {
        const string Pii = "Violation of UNIQUE KEY constraint. The duplicate key value is (9822233344).";
        var log = new CapturingLogger();
        var middleware = new HccErrorMiddleware(_ => throw new InvalidOperationException(Pii), log);
        var http = new DefaultHttpContext { TraceIdentifier = "corr-123" };
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/commands/CreateUser";
        http.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(http);

        Assert.Equal(500, http.Response.StatusCode);
        Assert.Equal("corr-123", http.Response.Headers["X-Correlation-Id"].ToString());
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync(Ct);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, HccServer.Json)!;
        Assert.Equal("INTERNAL", error.Code);
        Assert.Equal("corr-123", error.Details!["correlationId"].GetString());
        Assert.DoesNotContain("9822233344", body, StringComparison.Ordinal);
        var line = Assert.Single(log.Lines);
        Assert.Contains("corr-123", line, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", line, StringComparison.Ordinal);
        Assert.DoesNotContain("9822233344", line, StringComparison.Ordinal);
    }
}
