using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Contracts;
using Hcc.Contracts.Masters;
using Hcc.Contracts.MoneyMasters;
using Hcc.MoneyMasters.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.MoneyMasters.Tests.Sql;

/// <summary>The money-master endpoints over real HTTP (Kestrel on 127.0.0.1) with the same DI lines and Map calls as HccComposition (Masters +
/// MoneyMasters), the real access policy and a test session service; the wire shapes the production sync layer (sync/money-master.ts) reads.</summary>
[Trait("Category", "Sql")]
public class MoneyMastersHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private MoneyWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await MoneyWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddMastersModule();
        builder.Services.AddMoneyMastersModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        var api = _app.MapGroup(ApiConventions.BasePath);
        api.MapMastersEndpoints();
        api.MapMoneyMastersEndpoints();
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
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }

    private async Task<string> GetAsync(string path, string user)
    {
        using var res = await _http.SendAsync(Req(HttpMethod.Get, path, user), Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task Lists_read_for_every_signed_in_role_in_the_sync_wire_shape()
    {
        using (var anon = await _http.SendAsync(Req(HttpMethod.Get, "/masters/payment-methods", null), Ct)) Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        var pm = await GetAsync("/masters/payment-methods?page=1&pageSize=200", "housekeeper");
        foreach (var f in new[] { "\"paymentMethodId\":", "\"settlesTo\":\"TILL\"", "\"usableAt\":[\"frontdesk\",\"pos\",\"expenses\"]", "\"surchargeBps\":null" /* MA-11 old → new: 0 → null (money, DEC-061) */, "\"needsReference\":", "\"version\":", "\"usageCount\":2" })
            Assert.Contains(f, pm, StringComparison.Ordinal);
        Assert.Equal(6, JsonSerializer.Deserialize<PaymentMethodList>(pm, Web)!.Total);
        var ba = await GetAsync("/masters/bank-accounts?page=1&pageSize=200", "cashier");
        Assert.Contains("\"bankAccountId\":", ba, StringComparison.Ordinal);
        Assert.Contains("\"openingBalancePaise\":null", ba, StringComparison.Ordinal);
        Assert.Contains("\"defaultFor\":[\"upi\",\"card\",\"bank\"]", ba, StringComparison.Ordinal);
        Assert.Contains("\"openingBalancePaise\":0", await GetAsync("/masters/bank-accounts", "owner"), StringComparison.Ordinal);
        var ec = await GetAsync("/masters/expense-categories?status=active", "reception");
        Assert.Contains("\"expenseCategoryId\":", ec, StringComparison.Ordinal);
        Assert.Contains("\"approvalLimitPaise\":null", ec, StringComparison.Ordinal);
        var ev = await GetAsync("/masters/expense-vendors", "manager");
        Assert.Contains("\"expenseVendorId\":", ev, StringComparison.Ordinal);
        Assert.Contains("\"Morning Supplies\"", ev, StringComparison.Ordinal);
        using var bad = await _http.SendAsync(Req(HttpMethod.Get, "/masters/expense-vendors?status=gone", "owner"), Ct);
        Assert.Equal("VALIDATION", (await bad.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
    }

    [Fact]
    public async Task Commands_are_Owner_only_with_idempotency_contract_errors_and_history_routes()
    {
        var body = new SavePaymentMethodRequest(null, "PAYTM", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk"], 150, "1010");
        using (var noKey = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SavePaymentMethod", "owner", body), Ct)) Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);
        foreach (var u in new[] { "manager", "techadmin" })
        {
            using var no = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SavePaymentMethod", u, body, "k-" + u), Ct);
            Assert.Equal("FORBIDDEN", (await no.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        }

        PaymentMethodView added;
        using (var ok = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SavePaymentMethod", "owner", body, "k1"), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            added = (await ok.Content.ReadFromJsonAsync<PaymentMethodView>(Web, Ct))!;
            Assert.Equal(("PAYTM", "BANK-1", 150), (added.Code, added.SettlesTo, added.SurchargeBps));
            Assert.Equal(["frontdesk"], added.UsableAt);
        }

        using (var replay = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SavePaymentMethod", "owner", body, "k1"), Ct))
            Assert.Equal("true", replay.Headers.GetValues(AccessEndpoints.ReplayedHeader).Single());
        using (var bad = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SavePaymentMethod", "owner", body with { Code = "X9", Name = "Cash two", Kind = "cash" }, "k2"), Ct))
        {
            var err = (await bad.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!;
            Assert.Equal(("VALIDATION", "MASTER_INVALID", "settlesTo"), (err.Code, err.Details!["reason"].GetString(), err.Details["field"].GetString()));
        }

        using (var stale = await _http.SendAsync(Req(HttpMethod.Post, "/commands/RetirePaymentMethod", "owner", new PaymentMethodIdRequest(added.PaymentMethodId, "AAAAAAAAAAA="), "k3"), Ct))
            Assert.Equal("STALE_VERSION", (await stale.Content.ReadFromJsonAsync<ErrorResponse>(Web, Ct))!.Code);
        using (var del = await _http.SendAsync(Req(HttpMethod.Post, "/commands/DeletePaymentMethod", "owner", new PaymentMethodIdRequest(added.PaymentMethodId, added.Version), "k4"), Ct))
            Assert.True((await del.Content.ReadFromJsonAsync<MoneyMasterDeleteResult>(Web, Ct))!.Deleted);
        using (var vendor = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SaveExpenseVendor", "owner", new SaveExpenseVendorRequest(null, "V-NEW", "New Traders", "supplier", null, "ABCDE1234F", true), "k5"), Ct))
            Assert.Equal(HttpStatusCode.OK, vendor.StatusCode);
        using (var mgrHist = await _http.SendAsync(Req(HttpMethod.Get, "/masters/payment-methods/history", "techadmin"), Ct)) Assert.Equal(HttpStatusCode.Forbidden, mgrHist.StatusCode);
        var hist = JsonSerializer.Deserialize<MasterHistoryList>(await GetAsync($"/masters/payment-methods/{added.PaymentMethodId}/history?page=1&pageSize=200", "owner"), Web)!;
        Assert.Contains(hist.Items, h => h.Field == "deleted");
        Assert.Contains(hist.Items, h => h.Field == "surchargePct" && h.NewValue == "1.5 %");
        foreach (var route in new[] { "bank-accounts", "expense-categories", "expense-vendors" })
            Assert.Contains("\"total\":", await GetAsync($"/masters/{route}/history", "owner"), StringComparison.Ordinal);
    }
}
