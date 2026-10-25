using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Payroll;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Payroll;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Payroll.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Payroll.Tests.Sql;

/// <summary>Test session service: bearer <c>test-&lt;principal&gt;</c> is a session of that principal at one property (real policy behind it).</summary>
public sealed class TestSessions(int propertyId) : ISessionService
{
    public static string Token(int principalId) => "test-" + principalId.ToString(CultureInfo.InvariantCulture);

    public Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default)
        => throw new NotSupportedException("Tests sign in with a bearer token.");

    public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default)
    {
        if (token is { } t && t.StartsWith("test-", StringComparison.Ordinal)
            && int.TryParse(t.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var principal) && principal > 0)
            return Task.FromResult(new AuthenticatedSession(principal, principal, propertyId, "Test user", CredentialKind.Pin, null, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(12)));
        throw new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.");
    }

    public Task SignOutAsync(string? token, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>HTTP smoke test of the payroll endpoints on Kestrel: Idempotency-Key, replay header, status codes, CSV download, own payslip.</summary>
[Trait("Category", "Sql")]
public sealed class PayrollHttpTests(PayrollDb db) : IClassFixture<PayrollDb>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<WebApplication> StartAsync(Hotel h)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(db.Platform);
        builder.Services.AddSingleton<SqlConnectionFactory>(db.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(db.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(db.Clock);
        builder.Services.AddSingleton<IAccessPolicy>(db.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(h.PropertyId));
        builder.Services.AddPayrollModule();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        app.UseMiddleware<HccErrorMiddleware>();
        app.MapGroup(ApiConventions.BasePath).MapPayrollEndpoints();
        await app.StartAsync(Ct);
        return app;
    }

    private static HttpClient Client(WebApplication app, int principal)
    {
        var c = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(principal));
        return c;
    }

    private static HttpRequestMessage Post(string command, object body, string? key)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, $"{ApiConventions.BasePath}/commands/{command}") { Content = JsonContent.Create(body) };
        if (key is not null) m.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        return m;
    }

    [Fact]
    public async Task Prepare_lock_and_read_a_payslip_over_http()
    {
        var h = await db.NewHotelAsync();
        await h.SeedGoldenAsync();
        await using var app = await StartAsync(h);
        using var acc = Client(app, h.U("accountant"));
        using var own = Client(app, h.U("owner"));
        using var hk = Client(app, h.U("housekeeper"));
        using var mgr = Client(app, h.U("manager"));

        var missing = await acc.SendAsync(Post(PayrollService.OpPreparePayRun, new PreparePayRunRequest("2026-08"), null), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        var forbidden = await mgr.SendAsync(Post(PayrollService.OpPreparePayRun, new PreparePayRunRequest("2026-08"), "k1"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var res = await acc.SendAsync(Post(PayrollService.OpPreparePayRun, new PreparePayRunRequest("2026-08"), "k1"), Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var run = (await res.Content.ReadFromJsonAsync<PayRunResult>(Web, Ct))!;
        Assert.Equal(21_210_166, run.NetPaise);
        var again = await acc.SendAsync(Post(PayrollService.OpPreparePayRun, new PreparePayRunRequest("2026-08"), "k1"), Ct);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        var self = await acc.SendAsync(Post(PayrollService.OpLockPayRun, new LockPayRunRequest(run.RunId, run.Version), "k2"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);   // maker cannot lock
        var locked = await own.SendAsync(Post(PayrollService.OpLockPayRun, new LockPayRunRequest(run.RunId, run.Version), "k3"), Ct);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);

        var csv = await acc.GetAsync($"{ApiConventions.BasePath}/payroll/runs/2026-08/register.csv", Ct);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("Month,Staff ID,Name", await csv.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var slip = await hk.GetAsync($"{ApiConventions.BasePath}/payroll/payslips/{run.RunId}/{h.S("HR-S05")}", Ct);
        Assert.Equal(HttpStatusCode.OK, slip.StatusCode);
        var other = await hk.GetAsync($"{ApiConventions.BasePath}/payroll/payslips/{run.RunId}/{h.S("HR-S06")}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        var rates = await acc.GetStringAsync($"{ApiConventions.BasePath}/payroll/rates/2026-08", Ct);
        Assert.Contains("\"rounding\":\"paise\"", rates, StringComparison.Ordinal);
        using var anon = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{ApiConventions.BasePath}/payroll/my/payslips", Ct)).StatusCode);
    }
}
