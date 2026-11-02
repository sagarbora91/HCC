using System.Collections.Concurrent;

namespace Hcc.Infrastructure.Egress;

/// <summary>EGRESS-CONTRACT §1: the decision for one integration. Never throws (anything unreadable = OFF).</summary>
public interface IEgressPolicy
{
    EgressDecision Evaluate(EgressIntegration integration, DateTimeOffset now);

    EgressState Current(DateTimeOffset now);
}

/// <summary>Where the policy reads <c>net.*</c> (production: Master configuration via <c>ISettingsReader</c>). May throw: the policy fails closed.</summary>
public interface IEgressSettingsSource
{
    Task<EgressSettings> ReadAsync(CancellationToken ct = default);

    /// <summary>The hotel's time zone (the window is hotel local time).</summary>
    TimeZoneInfo TimeZone { get; }
}

/// <summary>The settings cache (≤ <see cref="EgressPolicy.CacheFor"/>; invalidated when a <c>net.*</c> setting is saved).</summary>
public interface IEgressSettingsCache
{
    ValueTask RefreshAsync(CancellationToken ct = default);

    void Invalidate();
}

/// <summary>
/// Production policy: a snapshot of the settings, re-read at most every <see cref="CacheFor"/>; a failed read leaves NO snapshot (fail closed,
/// reason <c>settings-unreadable</c>); a snapshot older than <see cref="StaleAfter"/> (nobody refreshed it) is also ignored.
/// </summary>
public sealed class EgressPolicy(IEgressSettingsSource source, TimeProvider clock) : IEgressPolicy, IEgressSettingsCache
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private sealed record Snapshot(EgressSettings? Settings, DateTimeOffset ReadAt);

    private volatile Snapshot? _snap;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask RefreshAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        if (_snap is { } s && now - s.ReadAt < CacheFor) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = clock.GetUtcNow();
            if (_snap is { } again && now - again.ReadAt < CacheFor) return;
            EgressSettings? read;
            try
            {
                read = await source.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Settings store failure = OFF (fail closed), shown in System health.
            catch (Exception)
#pragma warning restore CA1031
            {
                read = null;
            }

            _snap = new Snapshot(read, now);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _snap = null;

    private EgressSettings? Fresh(DateTimeOffset now) => _snap is { } s && now - s.ReadAt < StaleAfter ? s.Settings : null;

    private DateTimeOffset Local(DateTimeOffset now)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(now, source.TimeZone);
        }
#pragma warning disable CA1031 // never throws
        catch (Exception)
#pragma warning restore CA1031
        {
            return now;
        }
    }

    public EgressDecision Evaluate(EgressIntegration integration, DateTimeOffset now)
    {
        try
        {
            return EgressRules.Evaluate(Fresh(now), integration, Local(now));
        }
#pragma warning disable CA1031 // never throws: OFF
        catch (Exception)
#pragma warning restore CA1031
        {
            return EgressDecision.Off(EgressReasons.SettingsUnreadable);
        }
    }

    public EgressState Current(DateTimeOffset now)
    {
        var s = Fresh(now);
        var map = EgressKeys.All.ToDictionary(i => i, i => s?.Switches.TryGetValue(i, out var v) == true && v == true);
        EgressWindow? w = null;
        if (s is not null) EgressRules.TryParseWindow(s.WindowJson, out w);
        var effective = EgressKeys.All.Any(i => Evaluate(i, now).Allowed);
        return new EgressState(s?.Master == true, map, w, effective, s?.ChangedAt, s?.ChangedBy, s?.Reason);
    }
}

/// <summary>One audited gate decision (<c>ops.egress_audit</c>, migration 4732).</summary>
public sealed record EgressAuditEntry(DateTimeOffset At, string Integration, bool Allowed, string Reason, string? Host, int? UserId = null);

/// <summary>Writes gate decisions (SQL adapter in Hcc.Infrastructure.SqlServer/Egress). Must not throw for the caller (the handler swallows).</summary>
public interface IEgressAudit
{
    Task WriteAsync(EgressAuditEntry entry, CancellationToken ct = default);
}

/// <summary>No audit (tests / when no store is registered).</summary>
public sealed class NullEgressAudit : IEgressAudit
{
    public Task WriteAsync(EgressAuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>In-memory last refusal per integration and the refusal audit throttle (one row per integration per minute).</summary>
public sealed class EgressActivity
{
    public static readonly TimeSpan RefusalAuditEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, string Reason)> _lastRefusal = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAudited = new(StringComparer.Ordinal);

    /// <summary>Records a refusal; true when it should be audited (first in this minute for the integration).</summary>
    public bool Refused(string integration, string reason, DateTimeOffset at)
    {
        _lastRefusal[integration] = (at, reason);
        var audit = true;
        _lastAudited.AddOrUpdate(integration, at, (_, prev) =>
        {
            if (at - prev < RefusalAuditEvery)
            {
                audit = false;
                return prev;
            }

            return at;
        });
        return audit;
    }

    public IReadOnlyDictionary<string, (DateTimeOffset At, string Reason)> LastRefusals => _lastRefusal;
}
