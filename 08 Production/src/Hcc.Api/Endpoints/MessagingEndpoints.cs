using Hcc.Application.Messaging;
using Hcc.Contracts.Messaging;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Messaging;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Messaging endpoints (W2-10). ALL SENDING IS SIMULATED. Commands: <c>POST /api/v1/commands/{SendMessage|RunMessagingTriggers|SimulateMessageDelivery|
/// RetryMessage|SetMessagingConsent|UpdateMessageTemplate|UpdateMessageRule}</c>; reads under <c>/api/v1/messaging/*</c>. Recipients are masked everywhere.
/// A background worker turns outbox events (confirmed booking, payment, departure) into queued messages.
/// </summary>
public static class MessagingEndpoints
{
    public static IServiceCollection AddHccMessaging(this IServiceCollection services)
    {
        services.AddSingleton(new MessagingOptions());
        services.AddSingleton<IMessageGateway, FakeMessageGateway>(); // a real WhatsApp/SMS gateway replaces this one registration
        services.AddSingleton<IMessagingDataFactory>(sp => new SqlMessagingDataFactory(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<TriggerEngine>();
        services.AddSingleton(sp => new MessagingTriggerProjector(sp.GetRequiredService<SqlPlatform>().Outbox, sp.GetRequiredService<SqlPlatform>().Connections,
            sp.GetRequiredService<TriggerEngine>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<MessagingCommands>();
        services.AddSingleton<MessagingQueries>();
        services.AddHostedService<MessagingTriggerWorker>();
        return services;
    }

    public static RouteGroupBuilder MapMessagingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + MessagingCommands.OpSend, async (HttpContext http, SendMessageRequest body, MessagingCommands c, CancellationToken ct) =>
            http.HccReply(await c.SendAsync(http.HccCommand(MessagingCommands.OpSend), body, ct)));
        g.MapPost("/commands/" + MessagingCommands.OpRunTriggers, async (HttpContext http, RunMessagingTriggersRequest body, MessagingCommands c, CancellationToken ct) =>
            http.HccReply(await c.RunTriggersAsync(http.HccCommand(MessagingCommands.OpRunTriggers), body, ct)));
        g.MapPost("/commands/" + MessagingCommands.OpSimulate, async (HttpContext http, SimulateMessageDeliveryRequest body, MessagingCommands c, CancellationToken ct) =>
            http.HccReply(await c.SimulateAsync(http.HccCommand(MessagingCommands.OpSimulate), body, ct)));
        g.MapPost("/commands/" + MessagingCommands.OpRetry, async (HttpContext http, RetryMessageRequest body, MessagingCommands c, CancellationToken ct) =>
            http.HccReply(await c.RetryAsync(http.HccCommand(MessagingCommands.OpRetry), body, ct)));
        g.MapPost("/commands/" + MessagingCommands.OpConsent, async (HttpContext http, SetMessagingConsentRequest body, MessagingCommands c, CancellationToken ct) =>
            http.HccReply(await c.SetConsentAsync(http.HccCommand(MessagingCommands.OpConsent), body, ct)));
        g.MapPost("/commands/" + MessagingCommands.OpTemplate, async (HttpContext http, UpdateMessageTemplateRequest body, MessagingCommands c, CancellationToken ct) =>
            http.HccReply(await c.UpdateTemplateAsync(http.HccCommand(MessagingCommands.OpTemplate), body, ct)));
        g.MapPost("/commands/" + MessagingCommands.OpRule, async (HttpContext http, UpdateMessageRuleRequest body, MessagingCommands c, CancellationToken ct) =>
            http.HccReply(await c.UpdateRuleAsync(http.HccCommand(MessagingCommands.OpRule), body, ct)));

        g.MapGet("/messaging/messages", async (HttpContext http, MessagingQueries q, CancellationToken ct, string? status = null, int page = 1, int pageSize = 50) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.MessagesAsync(p, u, status, page, pageSize, ct));
        });
        g.MapGet("/messaging/messages/{id:long}", async (HttpContext http, long id, MessagingQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.GetMessageAsync(p, u, id, ct));
        });
        g.MapGet("/messaging/summary", async (HttpContext http, MessagingQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.SummaryAsync(p, u, ct));
        });
        g.MapGet("/messaging/templates", async (HttpContext http, MessagingQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.TemplatesAsync(p, u, ct));
        });
        g.MapGet("/messaging/rules", async (HttpContext http, MessagingQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.RulesAsync(p, u, ct));
        });
        g.MapGet("/messaging/consent/{guestId:long}", async (HttpContext http, long guestId, MessagingQueries q, CancellationToken ct) =>
        {
            var (p, u) = http.HccActor();
            return Results.Ok(await q.ConsentAsync(p, u, guestId, ct));
        });
        return api;
    }
}

/// <summary>Polls the outbox for messaging trigger events every 2 s.</summary>
// ETP-REUSE: align with ETP background-job hosting (Windows service worker, retry/back-off, logging) when ETP (V: drive) is available.
public sealed partial class MessagingTriggerWorker(MessagingTriggerProjector projector, ILogger<MessagingTriggerWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await projector.RunOnceAsync(ct: stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed poll is logged and retried on the next tick; the event stays undelivered.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(logger, ex);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Messaging outbox projection failed; retrying")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
