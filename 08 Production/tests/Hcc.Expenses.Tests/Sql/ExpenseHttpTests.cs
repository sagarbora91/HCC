using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Contracts.Expenses;
using Hcc.Domain.Access;
using Hcc.Domain.Errors;
using Hcc.Expenses.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Expenses.Tests.Sql;

/// <summary>The expense endpoints over real HTTP (Kestrel on 127.0.0.1) behind P1's real session filter (bearer token "test-N") and the same DI/Map lines as the composition.</summary>
[Trait("Category", "Sql")]
public class ExpenseHttpTests : IAsyncLifetime
{
    private ExpensesDb _db = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Sessions(int propertyId) : ISessionService
    {
        public Task<(AuthenticatedSession Session, string Token)> SignInAsync(SignInRequest request, string? deviceToken, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthenticatedSession> ValidateAsync(string? token, CancellationToken ct = default)
        {
            if (token is { } t && t.StartsWith("test-", StringComparison.Ordinal) && int.TryParse(t.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p > 0)
                return Task.FromResult(new AuthenticatedSession(p, p, propertyId, "Test " + p, CredentialKind.Pin, null, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(12)));
            throw new HccException(HccErrorCode.UNAUTHENTICATED, "Your session has ended. Sign in again.");
        }

        public Task SignOutAsync(string? token, CancellationToken ct = default) => Task.CompletedTask;
    }

    public async ValueTask InitializeAsync()
    {
        _db = new ExpensesDb();
        await _db.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_db.Platform);
        builder.Services.AddSingleton(_db.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_db.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_db.Clock);
        builder.Services.AddBillingModule();
        builder.Services.AddExpensesModule();
        builder.Services.AddSingleton<IAccessPolicy>(_db.Policy);
        builder.Services.AddSingleton<ISessionService>(new Sessions(_db.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        var api = _app.MapGroup(ApiConventions.BasePath);
        api.MapExpensesEndpoints();
        await _app.StartAsync(Ct);
        _http = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    private HttpRequestMessage Req(HttpMethod m, string path, int? principal, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        if (principal is not null) r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-" + principal.Value.ToString(CultureInfo.InvariantCulture));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return r;
    }

    [Fact]
    public async Task Submit_decide_and_list_over_HTTP_with_contract_errors()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/expenses", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SubmitExpense", Who.Reception, new SubmitExpenseRequest("Aqua", "Supplies", 3_000, "UPI")), Ct))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);

        var submitted = await Send<ExpenseResult>(Req(HttpMethod.Post, "/commands/SubmitExpense", Who.Reception, new SubmitExpenseRequest("Aqua", "Supplies", 3_000, "UPI"), "k-submit-1"));
        Assert.Equal("Pending", submitted.Expense.Status);
        using (var own = await _http.SendAsync(Req(HttpMethod.Post, "/commands/DecideExpense", Who.Reception, new DecideExpenseRequest(submitted.Expense.ExpenseId, submitted.Expense.Version, true), "k-dec-0"), Ct))
            Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        var posted = await Send<ExpenseResult>(Req(HttpMethod.Post, "/commands/DecideExpense", Who.Manager, new DecideExpenseRequest(submitted.Expense.ExpenseId, submitted.Expense.Version, true), "k-dec-1"));
        Assert.Equal("Posted", posted.Expense.Status);
        using (var stale = await _http.SendAsync(Req(HttpMethod.Post, "/commands/ReverseExpense", Who.Manager, new ReverseExpenseRequest(submitted.Expense.ExpenseId, submitted.Expense.Version, "old"), "k-rev-0"), Ct))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var page = await Send<ExpensePage>(Req(HttpMethod.Get, "/expenses?status=Posted", Who.Reception));
        Assert.True(page.OwnOnly);
        Assert.Contains(page.Items, i => i.ExpenseId == submitted.Expense.ExpenseId);
        var summary = await Send<ExpenseSummaryView>(Req(HttpMethod.Get, "/expenses/summary?date=2026-10-24", Who.Accountant));
        Assert.True(summary.NonCashPaise >= 3_000);
        using (var hk = await _http.SendAsync(Req(HttpMethod.Get, "/expenses", Who.Housekeeper), Ct)) Assert.Equal(HttpStatusCode.Forbidden, hk.StatusCode);
    }

    private async Task<T> Send<T>(HttpRequestMessage r)
    {
        using var res = await _http.SendAsync(r, Ct);
        var text = await res.Content.ReadAsStringAsync(Ct);
        Assert.True(res.IsSuccessStatusCode, text);
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
