using Hcc.Api.Hosting;

namespace Hcc.Api;

/// <summary>What a failed poll is reported as (see <see cref="PollFailureThrottle"/>).</summary>
public enum PollFailureNotice
{
    /// <summary>Not reported (Debug only): the failure run was already reported and the repeat interval has not passed.</summary>
    Quiet,

    /// <summary>The first failure of a run: one Warning.</summary>
    Warning,

    /// <summary>The run reached <see cref="PollFailureThrottle.ErrorAfter"/> failures in a row, or the repeat interval passed: one Error.</summary>
    Error,
}

/// <summary>
/// A09-05: turns a run of failed polls into a few operator entries instead of one Warning every 2 s: the first failure = Warning,
/// the <see cref="ErrorAfter"/>th failure in a row = Error, then one Error per <see cref="RepeatEvery"/> while the run lasts; the next
/// success reports how many polls failed. Not thread-safe (one worker loop owns it).
/// </summary>
public sealed class PollFailureThrottle(TimeProvider clock, int errorAfter, TimeSpan repeatEvery)
{
    public int ErrorAfter { get; } = Math.Max(1, errorAfter);

    public TimeSpan RepeatEvery { get; } = repeatEvery;

    /// <summary>Failed polls in a row (0 = the last poll worked).</summary>
    public int Consecutive { get; private set; }

    /// <summary>When the current run of failures started (UTC); null while polls work.</summary>
    public DateTimeOffset? Since { get; private set; }

    private DateTimeOffset _lastError;

    public PollFailureNotice Failed()
    {
        var now = clock.GetUtcNow();
        Consecutive++;
        if (Consecutive == 1) Since = now;
        if (Consecutive == ErrorAfter || (Consecutive > ErrorAfter && now - _lastError >= RepeatEvery))
        {
            _lastError = now;
            return PollFailureNotice.Error;
        }

        return Consecutive == 1 ? PollFailureNotice.Warning : PollFailureNotice.Quiet;
    }

    /// <summary>A poll worked: the number of failed polls before it (null when there were none), and the run is reset.</summary>
    public int? Succeeded()
    {
        if (Consecutive == 0) return null;
        var n = Consecutive;
        Consecutive = 0;
        Since = null;
        return n;
    }
}

/// <summary>
/// Base of the outbox projector workers (accounting, housekeeping, messaging, OTA): poll every <see cref="Interval"/>. A failed poll
/// (database unreachable, a SQL error in the poll itself — an event's own failure is recorded per event by <c>OutboxConsumer</c> and
/// logged by <see cref="OutboxFailureLogger"/>) is logged with the exception TYPE only (F-L07) and throttled for the operator
/// (<see cref="PollFailureThrottle"/>: Warning, Error after <see cref="ErrorAfter"/> failures ≈ one minute, then hourly; Information when
/// it works again). Those entries reach the Windows Event Log in Production (<see cref="HccEventLogLoggerProvider"/>, A09-05).
/// </summary>
// ETP-REUSE: align with ETP background-job hosting (Windows service worker, retry/back-off, logging) when ETP (V: drive) is available.
public abstract partial class OutboxPollingWorker(ILogger logger, string what, TimeProvider? clock = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Failed polls in a row before the Error entry (30 × 2 s ≈ one minute).</summary>
    public const int ErrorAfter = 30;

    public static readonly TimeSpan RepeatEvery = TimeSpan.FromHours(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private PollFailureThrottle? _throttle;

    private PollFailureThrottle Throttle => _throttle ??= new PollFailureThrottle(_clock, ErrorAfter, RepeatEvery);

    /// <summary>What the worker does, in the log text ("Accounting outbox projection").</summary>
    public string What { get; } = what;

    /// <summary>One poll of the worker's projector(s).</summary>
    protected abstract Task PollAsync(CancellationToken ct);

    /// <summary>One poll with the loop's failure handling (the loop calls it every <see cref="Interval"/>; tests call it directly).</summary>
    public async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            await PollAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failed poll is logged (type only, F-L07) and retried on the next tick; the event stays undelivered.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var type = OperatorAlertText.Describe(ex);
            switch (Throttle.Failed())
            {
                case PollFailureNotice.Warning:
                    LogFirstFailure(logger, What, type, (int)Interval.TotalSeconds);
                    break;
                case PollFailureNotice.Error:
                    LogFailing(logger, What, Throttle.Consecutive, Throttle.Since!.Value.UtcDateTime, type);
                    break;
                default:
                    LogQuiet(logger, What, Throttle.Consecutive, type);
                    break;
            }

            return;
        }

        if (Throttle.Succeeded() is { } failed) LogRecovered(logger, What, failed);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
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

    [LoggerMessage(EventId = HccEventLog.OutboxPollFailing, Level = LogLevel.Warning, Message = "{What} failed ({ErrorType}); retrying every {Seconds} s. Later events wait until it works again.")]
    private static partial void LogFirstFailure(ILogger logger, string what, string errorType, int seconds);

    [LoggerMessage(EventId = HccEventLog.OutboxPollFailing, Level = LogLevel.Error, Message = "{What} has failed {Failures} times in a row since {SinceUtc:u} ({ErrorType}). Bookings, tasks or vouchers that depend on it are not being created. Check that SQL Server is running and see Admin & Setup > System health.")]
    private static partial void LogFailing(ILogger logger, string what, int failures, DateTime sinceUtc, string errorType);

    [LoggerMessage(EventId = HccEventLog.OutboxPollFailing, Level = LogLevel.Debug, Message = "{What} failed again ({Failures} in a row, {ErrorType})")]
    private static partial void LogQuiet(ILogger logger, string what, int failures, string errorType);

    [LoggerMessage(EventId = HccEventLog.OutboxPollRecovered, Level = LogLevel.Information, Message = "{What} works again after {Failures} failed poll(s).")]
    private static partial void LogRecovered(ILogger logger, string what, int failures);
}
