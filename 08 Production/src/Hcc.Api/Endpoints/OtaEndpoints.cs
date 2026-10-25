using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Ota;
using Hcc.Contracts.Ota;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Ota;

namespace Hcc.Api.Endpoints;

/// <summary>
/// OTA channel endpoints (W3-1; ALL SIMULATED). Commands <c>POST /api/v1/commands/{SaveChannel, SetChannelStatus, SetChannelStopSell, PushChannelRates,
/// ImportChannelMessage, QueueSimulatedMessage, PullChannelMessages, AcceptChannelMessage, RejectChannelMessage}</c>; reads under <c>/api/v1/ota/*</c>.
/// A background worker plays the channel manager from the outbox (push acknowledgements, "push due" flags). Every route needs a session; the actor comes from it only.
/// </summary>
public static class OtaEndpoints
{
    public static IServiceCollection AddOtaModule(this IServiceCollection services)
    {
        services.AddSingleton<IOtaStore>(sp => new SqlOtaStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<OtaQueries>();
        services.AddSingleton<OtaService>();
        services.AddSingleton(sp => new OtaChannelManagerProjector(sp.GetRequiredService<SqlPlatform>().Outbox, sp.GetRequiredService<SqlPlatform>().Connections,
            sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<OtaChannelManagerWorker>();
        return services;
    }

    public static RouteGroupBuilder MapOtaEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        BillingEndpoints.Command<SaveChannelRequest, ChannelResult>(g, "SaveChannel", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().SaveChannelAsync(Ctx(c, "SaveChannel", k), r, ct));
        BillingEndpoints.Command<SetChannelStatusRequest, ChannelResult>(g, "SetChannelStatus", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().SetStatusAsync(Ctx(c, "SetChannelStatus", k), r, ct));
        BillingEndpoints.Command<SetChannelStopSellRequest, ChannelResult>(g, "SetChannelStopSell", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().SetStopSellAsync(Ctx(c, "SetChannelStopSell", k), r, ct));
        BillingEndpoints.Command<PushChannelRatesRequest, PushResult>(g, "PushChannelRates", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().PushRatesAsync(Ctx(c, "PushChannelRates", k), r, ct));
        BillingEndpoints.Command<ImportChannelMessageRequest, ChannelMessageResult>(g, "ImportChannelMessage", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().ImportMessageAsync(Ctx(c, "ImportChannelMessage", k), r, ct));
        BillingEndpoints.Command<QueueSimulatedMessageRequest, ChannelMessageResult>(g, "QueueSimulatedMessage", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().QueueSimulatedAsync(Ctx(c, "QueueSimulatedMessage", k), r, ct));
        BillingEndpoints.Command<PullChannelMessagesRequest, PullResult>(g, "PullChannelMessages", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().PullAsync(Ctx(c, "PullChannelMessages", k), r, ct));
        BillingEndpoints.Command<AcceptChannelMessageRequest, AcceptResult>(g, "AcceptChannelMessage", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().AcceptAsync(Ctx(c, "AcceptChannelMessage", k), r, ct));
        BillingEndpoints.Command<RejectChannelMessageRequest, ChannelMessageResult>(g, "RejectChannelMessage", (sp, c, k, r, ct) => sp.GetRequiredService<OtaService>().RejectAsync(Ctx(c, "RejectChannelMessage", k), r, ct));

        g.MapGet("/ota/channels", async (HttpContext http, OtaQueries q, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListChannelsAsync(property, principal, ct).ConfigureAwait(false));
        });
        g.MapGet("/ota/messages", async (HttpContext http, OtaQueries q, CancellationToken ct, string? status = null, int limit = 100) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ListMessagesAsync(property, principal, status, limit, ct).ConfigureAwait(false));
        });
        g.MapGet("/ota/messages/{id:long}/check", async (HttpContext http, OtaQueries q, long id, CancellationToken ct, int? roomId = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.CheckMessageAsync(property, principal, id, roomId, ct).ConfigureAwait(false));
        });
        g.MapGet("/ota/channels/{id:int}/push-preview", async (HttpContext http, OtaQueries q, int id, CancellationToken ct) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.PushPreviewAsync(property, principal, id, ct).ConfigureAwait(false));
        });
        g.MapGet("/ota/pushes", async (HttpContext http, OtaQueries q, CancellationToken ct, int? channelId = null, int limit = 50) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.PushLogAsync(property, principal, channelId, limit, ct).ConfigureAwait(false));
        });
        g.MapGet("/ota/commission", async (HttpContext http, OtaQueries q, CancellationToken ct, string? from = null, string? to = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.CommissionReportAsync(property, principal, from, to, ct).ConfigureAwait(false));
        });
        g.MapGet("/ota/reconciliation", async (HttpContext http, OtaQueries q, CancellationToken ct, string? from = null, string? to = null, int? channelId = null) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.ReconciliationAsync(property, principal, from, to, channelId, ct).ConfigureAwait(false));
        });
        return api;
    }

    private static CommandContext Ctx(Caller caller, string operation, string key) => new(caller.PropertyId, caller.PrincipalId, operation, key);
}

/// <summary>Polls the outbox for channel-manager events every 2 s (simulated channel manager).</summary>
// ETP-REUSE: align with ETP background-job hosting (Windows service worker, retry/back-off, logging) when ETP (V: drive) is available.
public sealed partial class OtaChannelManagerWorker(OtaChannelManagerProjector projector, ILogger<OtaChannelManagerWorker> logger) : BackgroundService
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "OTA channel-manager outbox projection failed; retrying")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
