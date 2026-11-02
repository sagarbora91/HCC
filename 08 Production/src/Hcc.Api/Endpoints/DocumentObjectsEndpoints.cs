using Hcc.Application.Access;
using Hcc.Application.Documents;
using Hcc.Application.Health;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Documents;
using Hcc.Infrastructure.SqlServer.DocumentObjects;
using Hcc.Infrastructure.SqlServer.Health;

namespace Hcc.Api.Endpoints;

/// <summary>
/// LF-docdb (LF-CONTRACT §1, DEC-200): the documents database in the API host. Registers the object store and its jobs, the release consumer, the
/// daily sweep (03:30 server time) and <c>POST /admin/documents/sweep</c> (PERM-DOC-006: Owner, Tech admin).
/// </summary>
public static class DocumentObjectsEndpoints
{
    public const string PermAdmin = "PERM-DOC-006";

    public static IServiceCollection AddDocumentObjectsModule(this IServiceCollection services)
    {
        services.AddSingleton(sp => sp.GetRequiredService<SqlPlatform>().Documents);
        services.AddSingleton(sp => sp.GetRequiredService<SqlPlatform>().DocumentObjects);
        services.AddSingleton(sp => sp.GetRequiredService<SqlPlatform>().DocumentSweep);
        services.AddSingleton(sp => sp.GetRequiredService<SqlPlatform>().DocumentRefs);
        services.AddSingleton(sp => new DocumentReleaseProjector(sp.GetRequiredService<SqlPlatform>().Outbox, sp.GetRequiredService<IDocumentObjectStore>()));
        services.AddSingleton<IDatabaseHealth>(sp => new SqlDatabaseHealth(sp.GetRequiredService<SqlPlatform>().Connections, sp.GetRequiredService<SqlPlatform>().Documents));
        services.AddHostedService<DocumentReleaseWorker>();
        services.AddHostedService<DocumentSweepWorker>();
        return services;
    }

    public static RouteGroupBuilder MapDocumentObjectEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapPost("/admin/documents/sweep", async (HttpContext http, IAccessPolicy policy, DocumentSweepJob sweep, DocumentRefCheck refs, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var (property, principal) = http.HccActor();
            await policy.RequireAsync(property, principal, PermAdmin, null, null, ct);
            var removed = await sweep.RunAsync(null, ct);
            var check = await refs.RunAsync(true, ct);
            return Results.Ok(new { removed = removed.Removed, dangling = check.Dangling, orphans = check.Orphans });
        });
        return api;
    }
}

/// <summary>Marks the objects of purged / deleted Hcc rows deleted (event <c>DocumentObjectReleased</c>).</summary>
public sealed class DocumentReleaseWorker(DocumentReleaseProjector projector, ILogger<DocumentReleaseWorker> logger) : OutboxPollingWorker(logger, "Document object release")
{
    protected override Task PollAsync(CancellationToken ct) => projector.RunOnceAsync(ct: ct);
}

/// <summary>Daily at 03:30 (server time): the orphan sweep and the reference check (contract §1.7). A failed pass is logged by type only and tried again the next day.</summary>
public sealed partial class DocumentSweepWorker(DocumentSweepJob sweep, DocumentRefCheck refs, ILogger<DocumentSweepWorker> logger) : BackgroundService
{
    public static TimeSpan UntilNext(DateTime nowLocal)
    {
        var next = nowLocal.Date.AddHours(3).AddMinutes(30);
        if (next <= nowLocal) next = next.AddDays(1);
        return next - nowLocal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(UntilNext(DateTime.Now), stoppingToken).ConfigureAwait(false);
                await sweep.RunAsync(null, stoppingToken).ConfigureAwait(false);
                await refs.RunAsync(true, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed pass is logged (type only, F-L07) and tried again tomorrow.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Document orphan sweep failed ({ExceptionType}); tried again tomorrow at 03:30.")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
