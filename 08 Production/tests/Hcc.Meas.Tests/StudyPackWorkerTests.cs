// F12-D · L2 integer paise division, L5 invariant date parse, L6 the pack schedule worker logs instead of swallowing.
using System.Globalization;
using Hcc.Application.Configuration;
using Hcc.Domain.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Reporting;
using Hcc.Reporting.Analytics;
using Hcc.Reporting.Reports.StudyPack;
using Microsoft.Extensions.Logging;

namespace Hcc.Meas.Tests;

public class StudyPackWorkerTests
{
    [Fact]
    public void DivP_rounds_half_away_from_zero_in_integers()
    {
        Assert.Equal(3, PackRules.DivP(5, 2));
        Assert.Equal(-3, PackRules.DivP(-5, 2));
        Assert.Equal(2, PackRules.DivP(7, 4));
        Assert.Equal(0, PackRules.DivP(1, 3));
        Assert.Equal(0, PackRules.DivP(5, 0));
        Assert.Equal(0, PackRules.DivP(5, -2));
    }

    [Fact]
    public void DivP_is_exact_above_2_pow_53()
    {
        Assert.Equal(1L << 39, PackRules.DivP(1L << 40, 2));
        Assert.Equal((1L << 39) + 2, PackRules.DivP((1L << 40) + 3, 2)); // 2^39 + 1.5 -> 2^39 + 2
        var huge = 1L << 60;
        Assert.Equal((1L << 59) + 2, PackRules.DivP(huge + 3, 2)); // 2^59 + 1.5 -> +2; a double would blur the +3 at 2^60
        Assert.Equal((1L << 59) + 1, PackRules.DivP(huge + 1, 2)); // 2^59 + 0.5 -> +1
        Assert.Equal(1L << 59, PackRules.DivP(huge, 2));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("hi-IN")]
    [InlineData("de-DE")]
    public void AgeDays_and_buckets_do_not_depend_on_the_thread_culture(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(30, LegacyOccupancy.AgeDays("2026-10-31", "2026-10-01"));
            Assert.Equal(365, LegacyOccupancy.AgeDays("2026-10-31", "2025-10-31"));
            Assert.Equal(0, LegacyOccupancy.AgeDays("2026-10-31", "2026-10-31"));
            Assert.Equal("31-90", LegacyOccupancy.Bucket(LegacyOccupancy.AgeDays("2026-10-31", "2026-09-01")));
            Assert.Equal("0-30", LegacyOccupancy.Bucket(LegacyOccupancy.AgeDays("2026-04-30", "2026-04-01")));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Store : IReportScheduleStore
    {
        public Task<IReadOnlyList<ReportScheduleRecord>> ListAsync(int propertyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportScheduleRecord>>([]);

        public Task<long> SaveAsync(int propertyId, ReportScheduleRecord schedule, CancellationToken ct = default) => Task.FromResult(0L);

        public Task DeleteAsync(int propertyId, long id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Settings : ISettingsReader
    {
        public Task<ConfigValue> GetAsync(int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => throw new HccException(HccErrorCode.NOT_FOUND, "x"); // read as "monthly"

        public Task<ConfigValue> GetAsync(Hcc.Application.Commands.ICommandTransaction tx, int propertyId, string key, DateOnly? on = null, CancellationToken ct = default) => throw new HccException(HccErrorCode.NOT_FOUND, "x");
    }

    private sealed class Property : IReportPropertyInfo
    {
        public bool Fail { get; set; }

        public Task<ReportProperty> GetAsync(int propertyId, CancellationToken ct = default) =>
            Fail ? throw new InvalidOperationException("registry broke") : Task.FromResult(new ReportProperty("HPE", "T", new DateOnly(2026, 10, 24)));
    }

    [Fact]
    public async Task A_failing_sync_is_logged_once_then_rate_limited_then_the_recovery_is_logged_and_ticking_continues()
    {
        var prop = new Property { Fail = true };
        var scheduler = new StudyPackScheduler(new Store(), new Settings(), prop);
        var logger = new CapturingLogger();
        var log = new StudyPackFailureLog(logger, 3);
        static Task<IEnumerable<int>> Ids(CancellationToken ct) => Task.FromResult<IEnumerable<int>>([1, 2]);

        await StudyPackTick.RunAsync(scheduler, Ids, log, CancellationToken.None);
        Assert.Equal(2, logger.Lines.Count(l => l.Level == LogLevel.Warning)); // one warning each; the loop did not stop at property 1
        await StudyPackTick.RunAsync(scheduler, Ids, log, CancellationToken.None);
        Assert.Equal(2, logger.Lines.Count(l => l.Level == LogLevel.Warning)); // 2nd minute in a row: silent
        await StudyPackTick.RunAsync(scheduler, Ids, log, CancellationToken.None);
        Assert.Equal(4, logger.Lines.Count(l => l.Level == LogLevel.Warning)); // 3rd minute in a row: warns again

        prop.Fail = false;
        await StudyPackTick.RunAsync(scheduler, Ids, log, CancellationToken.None);
        Assert.Equal(2, logger.Lines.Count(l => l.Level == LogLevel.Information));
        await StudyPackTick.RunAsync(scheduler, Ids, log, CancellationToken.None);
        Assert.Equal(2, logger.Lines.Count(l => l.Level == LogLevel.Information)); // no repeat once recovered
    }

    [Fact]
    public async Task A_failing_property_list_is_logged_and_does_not_throw()
    {
        var logger = new CapturingLogger();
        await StudyPackTick.RunAsync(new StudyPackScheduler(new Store(), new Settings(), new Property()), _ => throw new InvalidOperationException("db down"), new StudyPackFailureLog(logger), CancellationToken.None);
        Assert.Single(logger.Lines, l => l.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StudyPackTick.RunAsync(new StudyPackScheduler(new Store(), new Settings(), new Property()),
            ct => { ct.ThrowIfCancellationRequested(); return Task.FromResult<IEnumerable<int>>([]); }, new StudyPackFailureLog(new CapturingLogger()), cts.Token));
    }
}
