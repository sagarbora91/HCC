using System.Diagnostics;
using System.Globalization;
using Hcc.Domain.Errors;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace Hcc.Api.Hosting;

/// <summary>Level of an operator alert (= the Windows Event Log entry type).</summary>
public enum OperatorAlertLevel
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// A09-05: where operator alerts go. Production on Windows: the Application log, source <see cref="HccEventLog.Source"/> ("HCC"), which
/// Install-HccService.ps1 (and the installer) register — creating a source needs an administrator, the service account never does it.
/// Tests inject a fake. An implementation never throws: an alert must never stop the worker that raised it.
/// </summary>
public interface IOperatorAlertSink
{
    void Write(OperatorAlertLevel level, int eventId, string message);
}

/// <summary>Event Log source and the event ids the owner can filter on (INSTALL.md §7b). The backup scripts use 2001–2003 (Hcc-Common.ps1).</summary>
public static class HccEventLog
{
    public const string Source = "HCC";
    public const string LogName = "Application";

    /// <summary>Registry key that exists once the source is registered (every account may read it; no administrator needed to check).</summary>
    public const string SourceRegistryKey = @"SYSTEM\CurrentControlSet\Services\EventLog\Application\" + Source;

    public const int OutboxDeliveryFailed = 1001;
    public const int OutboxEventParked = 1002;
    public const int OutboxPollFailing = 1010;
    public const int OutboxPollRecovered = 1011;
    public const int RetentionPurgeFailed = 1020;
    public const int BackgroundServiceFailed = 1030;
    public const int SchemaBehind = 1040;
    public const int SchemaCheckFailed = 1041;

    /// <summary>Any other forwarded entry (an operator category without its own id).</summary>
    public const int Other = 1099;
}

/// <summary>Exception text for logs (F-L07): the type, the HCC error code and the SQL Server error number — never the message (SQL Server messages carry key values such as a phone number or a login).</summary>
public static class OperatorAlertText
{
    public static string Describe(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        SqlException? sql = null;
        for (var e = ex; e is not null && sql is null; e = e.InnerException) sql = e as SqlException;
        if (ex is SqlException s) return "SqlException " + s.Number.ToString(CultureInfo.InvariantCulture);
        var name = ex is HccException h ? "HccException " + h.Code : ex.GetType().Name;
        return sql is null ? name : name + " (SqlException " + sql.Number.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

/// <summary>Alerts go nowhere (development, tests, a PC without Windows). The normal log providers still get the log line.</summary>
public sealed class NullOperatorAlertSink : IOperatorAlertSink
{
    public static readonly NullOperatorAlertSink Instance = new();

    public void Write(OperatorAlertLevel level, int eventId, string message)
    {
    }
}

/// <summary>
/// Writes to the Windows Application log under <see cref="HccEventLog.Source"/>, only when that source is registered on this PC (checked in
/// the registry, again at most every <see cref="RecheckAfter"/> while it is missing). Falls back silently: no source, no Windows, or a
/// failed write = nothing is written and nothing is thrown. It never creates the source (that needs an administrator).
/// </summary>
public sealed class WindowsEventLogAlertSink(string source = HccEventLog.Source) : IOperatorAlertSink
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(10);
    private const int MaxMessage = 4000;
    private long _offUntil = long.MinValue;   // Environment.TickCount64 before which nothing is written
    private int _registered;                  // 1 = the source was found

    public string Source { get; } = source;

    /// <summary>True when the source is registered in the Application log of this PC (false off Windows).</summary>
    public static bool SourceRegistered(string source = HccEventLog.Source)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\EventLog\" + HccEventLog.LogName + @"\" + source);
            return key is not null;
        }
#pragma warning disable CA1031 // A registry read that fails means "not registered"; alerts must never throw.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>Last write outcome, for tests and the System health panel: true once an entry was written.</summary>
    public bool Wrote { get; private set; }

    public void Write(OperatorAlertLevel level, int eventId, string message)
    {
        if (!OperatingSystem.IsWindows() || Environment.TickCount64 < Interlocked.Read(ref _offUntil)) return;
        try
        {
            if (Volatile.Read(ref _registered) == 0)
            {
                if (!SourceRegistered(Source))
                {
                    Interlocked.Exchange(ref _offUntil, Environment.TickCount64 + (long)RecheckAfter.TotalMilliseconds);
                    return;
                }

                Volatile.Write(ref _registered, 1);
            }

            var type = level switch { OperatorAlertLevel.Error => EventLogEntryType.Error, OperatorAlertLevel.Warning => EventLogEntryType.Warning, _ => EventLogEntryType.Information };
            var text = string.IsNullOrEmpty(message) ? "(no text)" : message.Length > MaxMessage ? message[..MaxMessage] : message;
            EventLog.WriteEntry(Source, text, type, Math.Clamp(eventId, 0, 65535));
            Wrote = true;
        }
#pragma warning disable CA1031 // No right to write, log full, source removed: alerts fall back silently and are retried later.
        catch (Exception)
#pragma warning restore CA1031
        {
            Volatile.Write(ref _registered, 0);
            Interlocked.Exchange(ref _offUntil, Environment.TickCount64 + (long)RecheckAfter.TotalMilliseconds);
        }
    }
}

/// <summary>
/// A09-05: the ILogger → Windows Event Log bridge for the operator categories only — the outbox polling workers, the outbox
/// delivery-failure logger, the retention purge and the schema guard (Information and up), and the generic host's
/// "BackgroundService failed" (Error and up). The entry is the log template filled with its values (consumer, outbox id, event type,
/// counts) plus the exception TYPE and SQL error number (<see cref="OperatorAlertText"/>) — never the exception message or stack
/// (F-L07). The framework's own EventLog provider is switched off in Production (<see cref="OperatorAlertLogging.AddHccOperatorAlerts"/>)
/// because it writes whole exceptions under a source nobody registered.
/// </summary>
[ProviderAlias("HccEventLog")]
public sealed class HccEventLogLoggerProvider(IOperatorAlertSink sink) : ILoggerProvider
{
    /// <summary>Category of the generic host (it logs a BackgroundService whose ExecuteAsync threw, then stops the host).</summary>
    public const string HostCategory = "Microsoft.Extensions.Hosting.Internal.Host";

    /// <summary>The HCC categories forwarded from Information up: every <see cref="OutboxPollingWorker"/>, the failure logger, retention, schema guard, the documents sweep.</summary>
    public static readonly IReadOnlySet<string> OperatorCategories = typeof(OutboxPollingWorker).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(OutboxPollingWorker)) && !t.IsAbstract)
        .Concat([typeof(OutboxFailureLogger), typeof(RetentionWorker), typeof(SchemaGuard), typeof(Hcc.Api.Endpoints.DocumentSweepWorker)])   // LF-docdb: the nightly orphan sweep reaches the Event Log too
        .Select(t => t.FullName!).ToHashSet(StringComparer.Ordinal);

    public IOperatorAlertSink Sink { get; } = sink;

    /// <summary>Does an entry of this category and level go to the operator log?</summary>
    public static bool Forwards(string? category, LogLevel level) => category is not null && level != LogLevel.None
        && (OperatorCategories.Contains(category) ? level >= LogLevel.Information : category == HostCategory && level >= LogLevel.Error);

    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, Sink);

    public void Dispose()
    {
    }

    private sealed class SinkLogger(string category, IOperatorAlertSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => Forwards(category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            ArgumentNullException.ThrowIfNull(formatter);
            var text = formatter(state, null);   // the template with its values; the exception is passed separately and never formatted
            if (exception is not null) text += " [" + OperatorAlertText.Describe(exception) + "]";
            var id = eventId.Id is >= 1000 and < 2000 ? eventId.Id : category == HostCategory ? HccEventLog.BackgroundServiceFailed : HccEventLog.Other;
            var level = logLevel >= LogLevel.Error ? OperatorAlertLevel.Error : logLevel == LogLevel.Warning ? OperatorAlertLevel.Warning : OperatorAlertLevel.Information;
            sink.Write(level, id, text);
        }
    }
}

/// <summary>Production logging set-up for operator alerts (called by <see cref="HccHostingExtensions.UseHccHosting"/> on Windows).</summary>
public static class OperatorAlertLogging
{
    /// <summary>
    /// Adds the HCC Event Log bridge with its own filter (it wins over the "Logging" levels of appsettings, which never hide an operator
    /// alert) and switches the framework's EventLog provider off (it would write whole exceptions, F-L07).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static ILoggingBuilder AddHccOperatorAlerts(this ILoggingBuilder logging, IOperatorAlertSink sink)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(sink);
        logging.AddFilter<Microsoft.Extensions.Logging.EventLog.EventLogLoggerProvider>((_, _) => false);
        logging.AddFilter<HccEventLogLoggerProvider>((category, level) => HccEventLogLoggerProvider.Forwards(category, level));
        logging.AddProvider(new HccEventLogLoggerProvider(sink));
        return logging;
    }
}
