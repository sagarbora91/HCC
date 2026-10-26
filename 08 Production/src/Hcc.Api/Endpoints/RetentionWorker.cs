using Hcc.Infrastructure.SqlServer.Access;

namespace Hcc.Api;

/// <summary>Runs the F-L03 retention purge (<see cref="SqlRetentionPurge"/>) a few minutes after start and then every six hours.</summary>
public sealed partial class RetentionWorker(SqlRetentionPurge purge, ILogger<RetentionWorker> logger) : BackgroundService
{
    public static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wait = FirstRun;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            wait = Interval;
            try
            {
                await purge.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failed pass is logged (type only, F-L07) and retried on the next tick.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retention purge failed ({ExceptionType}); retrying later")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
