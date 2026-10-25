using Hcc.Application.Housekeeping;
using Hcc.Contracts.Housekeeping;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Housekeeping;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<Application.Access.IAccessPolicy, DenyAllAccessPolicy>();
        services.AddSingleton(new HousekeepingOptions()); // inspection policy → Master configuration (DEC-057) later
        services.AddSingleton<IHousekeepingStore, SqlHousekeepingStore>();
        services.AddSingleton<IHousekeepingReadStore>(sp => new SqlHousekeepingReadStore(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IRoomBlockReader>(sp => new SqlRoomBlockReader(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IAssignedScopeSource>(sp => new SqlAssignedScopeSource(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton(sp => new HousekeepingTaskProjector(sp.GetRequiredService<SqlPlatform>().Outbox));
        services.AddSingleton<HousekeepingCommands>();
        services.AddSingleton<HousekeepingQueries>();
        services.AddHostedService<HousekeepingProjectorWorker>();
        return services;
    }

    public static RouteGroupBuilder MapHousekeepingEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapPost("/commands/" + HousekeepingCommands.OpTransition, async (HttpContext http, TransitionHousekeepingTaskRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            P3Session.Reply(http, await cmds.TransitionAsync(P3Session.Context(http, HousekeepingCommands.OpTransition), body, ct)));
        api.MapPost("/commands/" + HousekeepingCommands.OpBlockRoom, async (HttpContext http, BlockRoomRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            P3Session.Reply(http, await cmds.BlockRoomAsync(P3Session.Context(http, HousekeepingCommands.OpBlockRoom), body, ct)));
        api.MapPost("/commands/" + HousekeepingCommands.OpUnblockRoom, async (HttpContext http, UnblockRoomRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            P3Session.Reply(http, await cmds.UnblockRoomAsync(P3Session.Context(http, HousekeepingCommands.OpUnblockRoom), body, ct)));
        api.MapPost("/commands/" + HousekeepingCommands.OpRequestRepair, async (HttpContext http, RequestRepairRequest body, HousekeepingCommands cmds, CancellationToken ct) =>
            P3Session.Reply(http, await cmds.RequestRepairAsync(P3Session.Context(http, HousekeepingCommands.OpRequestRepair), body, ct)));
        api.MapGet("/rooms/board", async (HttpContext http, HousekeepingQueries q, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            var (property, principal) = P3Session.Actor(http);
            return Results.Ok(await q.BoardAsync(property, principal, page, pageSize, ct));
        });
        api.MapGet("/housekeeping/tasks", async (HttpContext http, HousekeepingQueries q, CancellationToken ct, int page = 1, int pageSize = 50) =>
        {
            var (property, principal) = P3Session.Actor(http);
            return Results.Ok(await q.OpenTasksAsync(property, principal, page, pageSize, ct));
        });
        return api;
    }
}

/// <summary>Polls the outbox for <c>StayDeparted</c> every 2 s (at-least-once delivery, exactly-once effect per consumer).</summary>
public sealed partial class HousekeepingProjectorWorker(HousekeepingTaskProjector projector, ILogger<HousekeepingProjectorWorker> logger) : BackgroundService
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Housekeeping outbox projection failed; retrying")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
