using Hcc.Api.Hosting;
using Hcc.Infrastructure.SqlServer.Access;

namespace Hcc.Api;

/// <summary>
/// Runs the F-L03 retention purge (<see cref="SqlRetentionPurge"/>) a few minutes after start and then every six hours. A09-05: a failed
/// pass is an Error with the exception type only (F-L07), which reaches the Windows Event Log in Production.
/// </summary>
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
                await RunPassAsync(ct => purge.RunOnceAsync(ct), logger, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>One purge pass; false when it failed (logged as an Error, type only) — the next tick tries again.</summary>
    public static async Task<bool> RunPassAsync(Func<CancellationToken, Task> pass, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pass);
        try
        {
            await pass(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed pass is logged (type only, F-L07) and retried on the next tick.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(logger, OperatorAlertText.Describe(ex), (int)Interval.TotalHours);
            return false;
        }
    }

    [LoggerMessage(EventId = HccEventLog.RetentionPurgeFailed, Level = LogLevel.Error, Message = "Retention purge failed ({ExceptionType}); expired sessions, old idempotency records, processed outbox events and old banking slip photos stay until the next pass in {Hours} h.")]
    private static partial void LogFailed(ILogger logger, string exceptionType, int hours);
}
