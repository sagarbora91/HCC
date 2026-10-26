using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Outbox;

namespace Hcc.Api;

/// <summary>
/// A08-02: logs every failed outbox delivery and, as a separate warning, the event being parked (consumer, outbox id, event type,
/// exception type; the message is stored in <c>plat.consumer_failure.last_error</c> only, F-L07). Subscribes for the host's lifetime.
/// </summary>
public sealed partial class OutboxFailureLogger(SqlPlatform platform, ILogger<OutboxFailureLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        platform.Outbox.DeliveryFailed += OnFailed;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        platform.Outbox.DeliveryFailed -= OnFailed;
        return Task.CompletedTask;
    }

    private void OnFailed(object? sender, OutboxDeliveryFailure f)
    {
        if (f.Parked) LogParked(logger, f.Consumer, f.OutboxId, f.EventType, f.Attempts, f.ErrorType);
        else LogFailed(logger, f.Consumer, f.OutboxId, f.EventType, f.Attempts, OutboxConsumer.ParkAfterFailures, f.ErrorType);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox consumer {Consumer} failed on event {OutboxId} ({EventType}), attempt {Attempts} of {Limit}: {ErrorType}; retrying later, later events continue")]
    private static partial void LogFailed(ILogger logger, string consumer, long outboxId, string eventType, int attempts, int limit, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox consumer {Consumer} PARKED event {OutboxId} ({EventType}) after {Attempts} failures: {ErrorType}. It is not processed until fixed; accounting periods with it cannot be locked")]
    private static partial void LogParked(ILogger logger, string consumer, long outboxId, string eventType, int attempts, string errorType);
}
