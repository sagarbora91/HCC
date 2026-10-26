using System.Collections.Concurrent;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Api.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hcc.Operations.Tests;

/// <summary>
/// A09-05: worker, retention-purge and BackgroundService failures reach the operator's Windows Event Log (source HCC) through
/// <see cref="HccEventLogLoggerProvider"/>, with a fake sink in place of the Event Log. Entries carry types and SQL error numbers only
/// (F-L07), and a run of failed polls becomes a Warning, one Error after <see cref="OutboxPollingWorker.ErrorAfter"/> failures, hourly
/// repeats and an Information line on recovery instead of a Warning every 2 s. No database, no Event Log written.
/// </summary>
public sealed class OperatorAlertsTests
{
    private const string Personal = "Violation of UNIQUE KEY: duplicate key value is (98765 43210, Ravi Kumar)";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class FakeSink : IOperatorAlertSink
    {
        public ConcurrentQueue<(OperatorAlertLevel Level, int EventId, string Message)> Entries { get; } = new();

        public void Write(OperatorAlertLevel level, int eventId, string message) => Entries.Enqueue((level, eventId, message));
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A polling worker whose poll fails while <see cref="Failing"/> is set (the real base class and the real worker's log category).</summary>
    private sealed class FlakyWorker(ILogger logger, TimeProvider clock) : OutboxPollingWorker(logger, "Accounting outbox projection", clock)
    {
        public bool Failing { get; set; } = true;

        protected override Task PollAsync(CancellationToken ct) => Failing ? throw new InvalidOperationException(Personal) : Task.CompletedTask;
    }

    private static ILoggerFactory Factory(FakeSink sink) => LoggerFactory.Create(b =>
    {
        b.SetMinimumLevel(LogLevel.Trace);
        b.AddFilter<HccEventLogLoggerProvider>((category, level) => HccEventLogLoggerProvider.Forwards(category, level));
        b.AddProvider(new HccEventLogLoggerProvider(sink));
    });

    [Fact]
    public void Operator_categories_are_the_four_outbox_workers_the_failure_logger_retention_and_the_schema_guard()
    {
        string[] expected =
        [
            typeof(AccountingProjectorWorker).FullName!, typeof(HousekeepingProjectorWorker).FullName!, typeof(MessagingTriggerWorker).FullName!,
            typeof(OtaChannelManagerWorker).FullName!, typeof(OutboxFailureLogger).FullName!, typeof(RetentionWorker).FullName!, typeof(SchemaGuard).FullName!,
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), HccEventLogLoggerProvider.OperatorCategories.Order(StringComparer.Ordinal));
        Assert.True(HccEventLogLoggerProvider.Forwards(typeof(OutboxFailureLogger).FullName, LogLevel.Warning));
        Assert.True(HccEventLogLoggerProvider.Forwards(typeof(RetentionWorker).FullName, LogLevel.Information));
        Assert.False(HccEventLogLoggerProvider.Forwards(typeof(RetentionWorker).FullName, LogLevel.Debug));
        Assert.True(HccEventLogLoggerProvider.Forwards(HccEventLogLoggerProvider.HostCategory, LogLevel.Error));
        Assert.False(HccEventLogLoggerProvider.Forwards(HccEventLogLoggerProvider.HostCategory, LogLevel.Warning));
        Assert.False(HccEventLogLoggerProvider.Forwards(typeof(HccErrorMiddleware).FullName, LogLevel.Error));   // request errors stay in the normal logs
        Assert.False(HccEventLogLoggerProvider.Forwards("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Critical));
    }

    [Fact]
    public async Task Failed_polls_write_a_warning_then_one_error_after_thirty_then_hourly_and_information_on_recovery_without_exception_text()
    {
        var sink = new FakeSink();
        using var factory = Factory(sink);
        var clock = new Clock(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));
        var worker = new FlakyWorker(factory.CreateLogger<AccountingProjectorWorker>(), clock);

        await worker.PollOnceAsync(Ct);   // failure 1: one Warning
        var first = Assert.Single(sink.Entries);
        Assert.Equal((OperatorAlertLevel.Warning, HccEventLog.OutboxPollFailing), (first.Level, first.EventId));
        Assert.Contains("Accounting outbox projection failed (InvalidOperationException)", first.Message, StringComparison.Ordinal);

        for (var i = 2; i < OutboxPollingWorker.ErrorAfter; i++)
        {
            clock.Now = clock.Now.AddSeconds(2);
            await worker.PollOnceAsync(Ct);
        }

        Assert.Single(sink.Entries);   // failures 2–29: Debug only, nothing for the operator
        clock.Now = clock.Now.AddSeconds(2);
        await worker.PollOnceAsync(Ct);   // failure 30: one Error
        var error = sink.Entries.Last();
        Assert.Equal((OperatorAlertLevel.Error, HccEventLog.OutboxPollFailing), (error.Level, error.EventId));
        Assert.Contains("has failed 30 times in a row since 2026-10-26 04:00:00Z (InvalidOperationException)", error.Message, StringComparison.Ordinal);

        clock.Now = clock.Now.AddMinutes(59);
        await worker.PollOnceAsync(Ct);
        Assert.Equal(2, sink.Entries.Count);   // within the hour: quiet
        clock.Now = clock.Now.AddMinutes(2);
        await worker.PollOnceAsync(Ct);
        Assert.Equal(3, sink.Entries.Count);   // an hour after the Error: one more Error
        Assert.Contains("has failed 32 times in a row", sink.Entries.Last().Message, StringComparison.Ordinal);

        worker.Failing = false;
        await worker.PollOnceAsync(Ct);
        var back = sink.Entries.Last();
        Assert.Equal((OperatorAlertLevel.Information, HccEventLog.OutboxPollRecovered), (back.Level, back.EventId));
        Assert.Contains("works again after 32 failed poll(s)", back.Message, StringComparison.Ordinal);
        await worker.PollOnceAsync(Ct);
        Assert.Equal(4, sink.Entries.Count);   // a working poll writes nothing

        Assert.All(sink.Entries, e => Assert.DoesNotContain("Ravi", e.Message, StringComparison.Ordinal));   // F-L07
        Assert.All(sink.Entries, e => Assert.DoesNotContain("duplicate key", e.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_retention_purge_is_an_error_with_the_exception_type_only()
    {
        var sink = new FakeSink();
        using var factory = Factory(sink);
        var ok = await RetentionWorker.RunPassAsync(_ => throw new InvalidOperationException(Personal), factory.CreateLogger<RetentionWorker>(), Ct);
        Assert.False(ok);
        var e = Assert.Single(sink.Entries);
        Assert.Equal((OperatorAlertLevel.Error, HccEventLog.RetentionPurgeFailed), (e.Level, e.EventId));
        Assert.StartsWith("Retention purge failed (InvalidOperationException)", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ravi", e.Message, StringComparison.Ordinal);
        Assert.True(await RetentionWorker.RunPassAsync(_ => Task.CompletedTask, factory.CreateLogger<RetentionWorker>(), Ct));
        Assert.Single(sink.Entries);
    }

    private sealed class CrashingService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new InvalidOperationException(Personal);
        }
    }

    [Fact]
    public async Task A_crashed_background_service_reaches_the_operator_log_with_its_type_only()
    {
        var sink = new FakeSink();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter<HccEventLogLoggerProvider>((category, level) => HccEventLogLoggerProvider.Forwards(category, level));
        builder.Logging.AddProvider(new HccEventLogLoggerProvider(sink));
        builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
        builder.Services.AddHostedService<CrashingService>();
        using var host = builder.Build();
        await host.StartAsync(Ct);
        for (var i = 0; i < 100 && sink.Entries.IsEmpty; i++) await Task.Delay(50, Ct);
        await host.StopAsync(Ct);
        var e = Assert.Single(sink.Entries, x => x.EventId == HccEventLog.BackgroundServiceFailed);
        Assert.Equal(OperatorAlertLevel.Error, e.Level);
        Assert.Contains("[InvalidOperationException]", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ravi", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_logging_switches_the_framework_event_log_off_and_forwards_only_operator_categories()
    {
        if (!OperatingSystem.IsWindows()) return;   // the Event Log exists on the Windows hotel PC only
        var sink = new FakeSink();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            if (!OperatingSystem.IsWindows()) return;
            b.AddEventLog();                                // what UseWindowsService / the default host adds
            b.AddHccOperatorAlerts(sink);
        });
        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILoggerFactory>();
        var request = factory.CreateLogger("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware");
        Assert.False(request.IsEnabled(LogLevel.Error));   // the framework EventLog provider is off: nothing else would log whole exceptions there
        var retention = factory.CreateLogger<RetentionWorker>();
        Assert.True(retention.IsEnabled(LogLevel.Error));
        retention.LogError(new InvalidOperationException(Personal), "Retention purge failed");
        var e = Assert.Single(sink.Entries);
        Assert.Equal("Retention purge failed [InvalidOperationException]", e.Message);
        Assert.Equal(HccEventLog.Other, e.EventId);
    }

    [Fact]
    public void The_event_log_sink_falls_back_silently_when_the_source_is_not_registered()
    {
        var source = "HccTest-NoSuchSource-" + Guid.NewGuid().ToString("N")[..8];
        var sink = new WindowsEventLogAlertSink(source);
        Assert.False(WindowsEventLogAlertSink.SourceRegistered(source));
        sink.Write(OperatorAlertLevel.Error, HccEventLog.OutboxEventParked, "test entry");   // no source: nothing written, nothing thrown
        sink.Write(OperatorAlertLevel.Warning, HccEventLog.OutboxDeliveryFailed, "test entry");
        Assert.False(sink.Wrote);
        Assert.Contains(new EventLogAlertStatus().EventLog(), new[] { "on", "off", "unavailable" });
        Assert.Equal(WindowsEventLogAlertSink.SourceRegistered() ? "on" : OperatingSystem.IsWindows() ? "off" : "unavailable", new EventLogAlertStatus().EventLog());
    }

    [Fact]
    public void Exception_descriptions_carry_type_hcc_code_and_sql_number_only()
    {
        Assert.Equal("InvalidOperationException", OperatorAlertText.Describe(new InvalidOperationException(Personal)));
        Assert.Equal("HccException UNAVAILABLE", OperatorAlertText.Describe(new Hcc.Domain.Errors.HccException(Hcc.Contracts.HccErrorCode.UNAVAILABLE, Personal)));
    }
}
