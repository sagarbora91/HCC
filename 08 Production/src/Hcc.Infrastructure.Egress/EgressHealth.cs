namespace Hcc.Infrastructure.Egress;

/// <summary>Firewall rule state row (<c>ops.egress_firewall_state</c>, migration 4734).</summary>
public sealed record EgressFirewallRow(string Wanted, DateTimeOffset WantedAt, string? Applied, DateTimeOffset? AppliedAt, string? Detail);

/// <summary>Health reads and the firewall wanted-state write (SQL adapter: <c>SqlEgressStore</c>).</summary>
public interface IEgressHealthStore
{
    Task<EgressFirewallRow?> FirewallAsync(CancellationToken ct = default);

    /// <summary>Records the state the API wants the firewall rule in ("On" = internet allowed, "Off" = blocked).</summary>
    /// <returns>True when the wanted state changed.</returns>
    Task<bool> SetWantedFirewallAsync(string wanted, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Last audited refusal per integration (survives restarts).</summary>
    Task<IReadOnlyDictionary<string, (DateTimeOffset At, string Reason)>> LastRefusalsAsync(CancellationToken ct = default);
}

public sealed record EgressIntegrationView(string Integration, bool Switch, bool EffectiveNow, string Reason, DateTimeOffset? LastRefusalAt, string? LastRefusalReason);

/// <summary>System health block "Egress" (EGRESS-CONTRACT §5). ChangedBy only for the Owner (§6).</summary>
public sealed record EgressHealthView(bool MasterOn, bool EffectiveNow, string Reason, IReadOnlyList<EgressIntegrationView> Integrations, string? Window,
    DateTimeOffset? ChangedAt, string? ChangedBy, string? ChangeReason, IReadOnlyList<EgressQueueCount> Queues, string FirewallRule, string? FirewallDetail);

/// <summary>Builds the Egress block. Firewall: "applied" when the task applied the wanted state, "not applied" when it differs or never ran, "unknown" when unreadable.</summary>
public sealed class EgressHealth(IEgressPolicy policy, IEgressOutboxStore outbox, IEgressHealthStore store, EgressActivity activity, TimeProvider clock)
{
    public async Task<EgressHealthView> ReadAsync(bool owner, CancellationToken ct = default)
    {
        if (policy is IEgressSettingsCache cache) await cache.RefreshAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        var state = policy.Current(now);
        var stored = await store.LastRefusalsAsync(ct).ConfigureAwait(false);
        var rows = EgressKeys.All.Select(i =>
        {
            var name = EgressKeys.Name(i);
            var d = policy.Evaluate(i, now);
            (DateTimeOffset At, string Reason)? last = activity.LastRefusals.TryGetValue(name, out var mem) ? mem : stored.TryGetValue(name, out var db) ? db : null;
            return new EgressIntegrationView(name, state.Integrations[i], d.Allowed, d.Reason, last?.At, last?.Reason);
        }).ToList();
        var reason = !state.MasterOn ? policy.Evaluate(EgressIntegration.Messaging, now).Reason is EgressReasons.SettingsUnreadable ? EgressReasons.SettingsUnreadable : EgressReasons.MasterOff
            : state.EffectiveNow ? EgressReasons.On : rows.Select(r => r.Reason).FirstOrDefault(r => r == EgressReasons.OutsideWindow) ?? EgressReasons.IntegrationOff;
        var window = state.Window is { } w ? $"{w.From:HH\\:mm}–{w.To:HH\\:mm} ({string.Join(", ", w.Days.Select(x => x.ToString()[..3]))})" : null;
        IReadOnlyList<EgressQueueCount> queues;
        string fw;
        string? fwDetail = null;
        try
        {
            queues = await outbox.CountsAsync(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // a health read never fails as a whole
        catch (Exception)
        {
            queues = [];
        }

        try
        {
            var f = await store.FirewallAsync(ct).ConfigureAwait(false);
            fw = f is null ? "unknown" : f.Applied is not null && f.Applied == f.Wanted && f.AppliedAt >= f.WantedAt ? "applied" : "not applied";
            fwDetail = f?.Detail;
        }
        catch (Exception)
        {
            fw = "unknown";
        }
#pragma warning restore CA1031

        return new EgressHealthView(state.MasterOn, state.EffectiveNow, reason, rows, window, state.ChangedAt, owner ? state.ChangedBy : null, state.Reason, queues, fw, fwDetail);
    }
}
