using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Commands;
using Hcc.Application.Messaging;
using Hcc.Contracts;
using Hcc.Contracts.Messaging;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Outbox;
using Hcc.Messaging.Tests.Harness;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hcc.Messaging.Tests.Sql;

/// <summary>
/// A09-08 on a real database: with the Production gateway (messaging not configured) a manual send is refused clearly and stores nothing,
/// while event-driven messages are still recorded, as "Not sent: Messaging not configured", and the outbox consumer neither fails nor parks.
/// </summary>
[Trait("Category", "Sql")]
public class MessagingNotConfiguredTests : IAsyncLifetime
{
    private const string Sentence = "Messaging is not configured on this server: no message was sent.";

    private MsgWorld _w = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _w = await MsgWorld.CreateAsync(Ct, new NotConfiguredMessageGateway());

    public async ValueTask DisposeAsync() => await _w.DisposeAsync();

    private async Task PublishAsync(string type, long aggregate, string payloadJson) =>
        await _w.ExecAsync($"INSERT plat.outbox(event_type, aggregate_id, payload, property_id, principal_id) VALUES ('{type}', {aggregate}, N'{payloadJson}', {_w.PropertyId}, {Users.Reception});", Ct);

    private static void AssertNotConfigured(HccException e, string feature, string message)
    {
        Assert.Equal(HccErrorCode.UNAVAILABLE, e.Code);
        Assert.Equal(message, e.Message);
        Assert.Equal("NOT_CONFIGURED", e.Details!["reason"].GetString());
        Assert.Equal(feature, e.Details["feature"].GetString());
    }

    [Fact]
    public async Task A_manual_send_retry_and_simulation_are_refused_with_the_clear_message_and_store_nothing()
    {
        var (res, _, _) = await _w.BookingAsync("101", "2026-10-23", "2026-10-26", "Ananya Rao", "9820001041", "a@example.test", inHouse: true, ct: Ct);
        var e = await Assert.ThrowsAsync<HccException>(() => _w.Commands.SendAsync(_w.Ctx(Users.Reception, "n1"), new SendMessageRequest(res, "WhatsApp", "Hello {firstName}"), Ct));
        AssertNotConfigured(e, "messaging", Sentence);
        AssertNotConfigured(await Assert.ThrowsAsync<HccException>(() => _w.Commands.RetryAsync(_w.Ctx(Users.Reception, "n2"), new RetryMessageRequest(1), Ct)), "messaging", Sentence);
        AssertNotConfigured(await Assert.ThrowsAsync<HccException>(() => _w.Commands.SimulateAsync(_w.Ctx(Users.Reception, "n3"), new SimulateMessageDeliveryRequest(), Ct)), "messaging", Sentence);

        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM msg.message;", Ct));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.audit_event WHERE operation = 'SendMessage';", Ct));
        // the refusal does not leak to someone who may not send at all
        Assert.Equal("FORBIDDEN", await Expect.CodeAsync(() => _w.Commands.SendAsync(_w.Ctx(Users.Cashier, "n4"), new SendMessageRequest(res, "SMS", "Hi"), Ct)));
    }

    [Fact]
    public async Task Outbox_events_are_recorded_as_not_sent_and_the_projector_neither_fails_nor_parks_them()
    {
        var (res, _, _) = await _w.BookingAsync("104", "2026-10-24", "2026-10-26", "Riya Mehta", "9820007777", "guest3@example.test", inHouse: true, ct: Ct);
        var (res2, guest2, _) = await _w.BookingAsync("105", "2026-10-24", "2026-10-26", "Kabir Sen", "9820001046", "k@example.test", ct: Ct);
        await _w.Commands.SetConsentAsync(_w.Ctx(Users.Reception, "n5"), new SetMessagingConsentRequest(guest2, OptOut: true), Ct);
        await PublishAsync("ReservationConfirmed", res, $"{{\"reservationId\":{res}}}");
        await PublishAsync("StayDeparted", 1, $"{{\"stayId\":1,\"reservationId\":{res},\"propertyId\":{_w.PropertyId},\"roomId\":1,\"businessDate\":\"2026-10-26\"}}");
        await PublishAsync("ReservationConfirmed", res2, $"{{\"reservationId\":{res2}}}");

        var failures = new List<OutboxDeliveryFailure>();
        _w.Platform.Outbox.DeliveryFailed += (_, f) => failures.Add(f);
        Assert.Equal(3, await _w.Projector.RunOnceAsync(ct: Ct));
        Assert.Equal(0, await _w.Projector.RunOnceAsync(ct: Ct));   // all three were delivered: nothing is retried
        Assert.Empty(failures);
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM plat.consumer_failure;", Ct));
        Assert.Empty(await _w.Platform.Outbox.ParkedAsync(ct: Ct));

        var list = await _w.Queries.MessagesAsync(_w.PropertyId, Users.Reception, null, 1, 50, Ct);
        Assert.Equal(3, list.Total);                                   // R1 + R5 of the first booking, R1 of the second: nothing is lost
        Assert.Equal(Sentence, list.NotConfigured);
        Assert.All(list.Items, m =>
        {
            Assert.Equal("Not sent", m.Status);
            Assert.False(m.Simulated);
        });
        var reasons = list.Items.Where(m => m.ReservationId == res).Select(m => m.BlockedReason).ToList();
        Assert.Equal(2, reasons.Count);
        Assert.All(reasons, r => Assert.Equal("Messaging not configured", r));
        // a guest-level reason (here an opt-out) is the more specific fact and is kept
        Assert.Equal("Guest opted out of messages", Assert.Single(list.Items, m => m.ReservationId == res2).BlockedReason);

        var summary = await _w.Queries.SummaryAsync(_w.PropertyId, Users.Reception, Ct);
        Assert.Equal((3, 0, 0), (summary.NotSent, summary.Queued, summary.Sent));
        Assert.False(summary.Simulated);
        Assert.False(summary.Configured);
    }

    [Fact]
    public async Task Run_triggers_records_the_sweep_as_not_sent_instead_of_refusing()
    {
        await _w.BookingAsync("101", "2026-10-24", "2026-10-26", "Today Arrival", "9820000001", null, ct: Ct);
        await _w.BookingAsync("105", "2026-10-23", "2026-10-26", "In House Guest", "9820000004", null, inHouse: true, ct: Ct);
        var r = await _w.Commands.RunTriggersAsync(_w.Ctx(Users.Reception, "n6"), new RunMessagingTriggersRequest(), Ct);
        Assert.Equal((0, 2), (r.Value.Queued, r.Value.NotSent));
        Assert.Contains("messaging not configured", r.Value.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("simulated", r.Value.Summary, StringComparison.Ordinal);
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM msg.message WHERE blocked_reason = N'Messaging not configured';", Ct));
        Assert.Equal(0, (await _w.Queries.SummaryAsync(_w.PropertyId, Users.Reception, Ct)).PendingTriggers);
    }
}

/// <summary>The Production gateway over real HTTP: the 503 carries the sentence and the stable reason, and the list carries the notice for the screens.</summary>
[Trait("Category", "Sql")]
public class MessagingNotConfiguredHttpTests : IAsyncLifetime
{
    private MsgWorld _w = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await MsgWorld.CreateAsync(Ct, new NotConfiguredMessageGateway());
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });   // the real selection: no gateway override
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddHccMessaging();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        _app.MapGroup(ApiConventions.BasePath).MapMessagingEndpoints();
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

    private HttpRequestMessage Req(HttpMethod m, string path, int principal, object? body = null, string? key = null)
    {
        var r = new HttpRequestMessage(m, ApiConventions.BasePath + path);
        r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestSessions.Token(principal));
        if (key is not null) r.Headers.Add(ApiConventions.IdempotencyKeyHeader, key);
        if (body is not null) r.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return r;
    }

    [Fact]
    public async Task A_Production_host_refuses_a_send_with_503_and_a_stable_reason_and_tells_the_list_screen()
    {
        Assert.IsType<NotConfiguredMessageGateway>(_app.Services.GetRequiredService<IMessageGateway>());
        var (res, _, _) = await _w.BookingAsync("101", "2026-10-23", "2026-10-26", "Ananya Rao", "9820001041", "a@example.test", inHouse: true, ct: Ct);
        var send = await _http.SendAsync(Req(HttpMethod.Post, "/commands/SendMessage", Users.Reception, new { reservationId = res, channel = "WhatsApp", body = "Hello {firstName}" }, "p1"), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, send.StatusCode);
        using var err = JsonDocument.Parse(await send.Content.ReadAsStringAsync(Ct));
        Assert.Equal("UNAVAILABLE", err.RootElement.GetProperty("code").GetString());
        Assert.Equal("Messaging is not configured on this server: no message was sent.", err.RootElement.GetProperty("message").GetString());
        Assert.Equal("NOT_CONFIGURED", err.RootElement.GetProperty("details").GetProperty("reason").GetString());
        Assert.Equal("messaging", err.RootElement.GetProperty("details").GetProperty("feature").GetString());

        var list = await _http.SendAsync(Req(HttpMethod.Get, "/messaging/messages", Users.Reception), Ct);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync(Ct));
        Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("Messaging is not configured on this server: no message was sent.", doc.RootElement.GetProperty("notConfigured").GetString());
        var summary = await _http.SendAsync(Req(HttpMethod.Get, "/messaging/summary", Users.Manager), Ct);
        Assert.Contains("\"configured\":false", await summary.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
