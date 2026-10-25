using Hcc.Application.Access;
using Hcc.Application.Housekeeping;
using Hcc.Contracts.Housekeeping;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Housekeeping;

namespace Hcc.Api.Endpoints;

/// <summary>
/// Housekeeping endpoints: <c>POST /api/v1/commands/{TransitionHousekeepingTask|BlockRoom|UnblockRoom|RequestRepair}</c>,
/// <c>GET /api/v1/rooms/board</c>, <c>GET /api/v1/housekeeping/tasks</c> (paged, assigned-scope filtered, no money fields),
/// plus the background projector turning <c>StayDeparted</c> outbox events into checkout-clean tasks.
/// </summary>
public static class HousekeepingEndpoints
{
    public static IServiceCollection AddHccHousekeeping(this IServiceCollection services)
    {
        services.AddSingleton(new HousekeepingOptions()); // fallback only: the inspection policy is read from Master configuration (DEC-057)
        services.AddSingleton<Hcc.Application.Configuration.ISettingsProvider>(HousekeepingSettings.Provider); // registers "inspection"
        services.AddSingleton<IHousekeepingStore, SqlHousekeepingStore>();
        services.AddSingleton<IHousekeepingReadStore>(sp => new SqlHousekeepingReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        // Assigned-scope resolvers for P1's policy (scope kinds "task" and "room").
        services.AddSingleton<IAccessScopeResolver>(sp => new SqlHousekeepingScopeResolver(sp.GetRequiredService<SqlPlatform>().Connections, AccessScopeKinds.Task));
        services.AddSingleton<IAccessScopeResolver>(sp => new SqlHousekeepingScopeResolver(sp.GetRequiredService<SqlPlatform>().Connections, AccessScopeKinds.Room));
        services.AddSingleton(sp => new HousekeepingTaskProjector(sp.GetRequiredService<SqlPlatform>().Outbox));
        services.AddSingleton<ITaskAssigneeEligibility, PolicyTaskAssigneeEligibility>(); // grants-based: HK-003 for cleans, ROOM-001 without HK-003 for repairs
        services.AddSingleton<HousekeepingCommands>();
        services.AddSingleton<IStayoverStore, SqlStayoverStore>();       // W3-7 stay-over cleaning tasks
        services.AddSingleton(sp => new StayoverTaskProjector(sp.GetRequiredService<SqlPlatform>().Outbox));
        services.AddSingleton<StayoverCommands>();
        services.AddSingleton<HousekeepingQueries>();
        services.AddHostedService<HousekeepingProjectorWorker>();
        return services;
    }

    public static RouteGroupBuilder MapHousekeepingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/commands/" + HousekeepingCommands.OpTransition, async (HttpContext http, TransitionHousekeepingTaskRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.TransitionAsync(http.HccCommand(HousekeepingCommands.OpTransition), body, ct)));
        g.MapPost("/commands/" + HousekeepingCommands.OpBlockRoom, async (HttpContext http, BlockRoomRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.BlockRoomAsync(http.HccCommand(HousekeepingCommands.OpBlockRoom), body, ct)));
        g.MapPost("/commands/" + HousekeepingCommands.OpUnblockRoom, async (HttpContext http, UnblockRoomRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.UnblockRoomAsync(http.HccCommand(HousekeepingCommands.OpUnblockRoom), body, ct)));
        g.MapPost("/commands/" + HousekeepingCommands.OpRequestRepair, async (HttpContext http, RequestRepairRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.RequestRepairAsync(http.HccCommand(HousekeepingCommands.OpRequestRepair), body, ct)));
        g.MapPost("/commands/" + StayoverCommands.OpGenerate, async (HttpContext http, GenerateStayoverTasksRequest body, StayoverCommands cmds, CancellationToken ct) =>
            http.HccReply(await cmds.GenerateAsync(http.HccCommand(StayoverCommands.OpGenerate), body, ct)));
        g.MapGet("/rooms/board", async (HttpContext http, HousekeepingQueries q, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.BoardAsync(property, principal, page, pageSize, ct));
        });
        g.MapGet("/housekeeping/tasks", async (HttpContext http, HousekeepingQueries q, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            var (property, principal) = http.HccActor();
            return Results.Ok(await q.OpenTasksAsync(property, principal, page, pageSize, ct));
        });
        return api;
    }
}

/// <summary>Polls the outbox for <c>StayDeparted</c> every 2 s (at-least-once delivery, exactly-once effect per consumer).</summary>
// ETP-REUSE: align with ETP background-job hosting (Windows service worker, retry/back-off, logging) when ETP (V: drive) is available.
public sealed partial class HousekeepingProjectorWorker(HousekeepingTaskProjector projector, StayoverTaskProjector stayover, ILogger<HousekeepingProjectorWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await projector.RunOnceAsync(ct: stoppingToken).ConfigureAwait(false);
                await stayover.RunOnceAsync(ct: stoppingToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Housekeeping outbox projection failed; retrying")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
