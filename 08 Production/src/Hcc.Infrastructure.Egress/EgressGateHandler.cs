using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Infrastructure.Egress;

/// <summary>
/// The outermost handler of the "egress" client: asks <see cref="IEgressPolicy"/> BEFORE <c>base.SendAsync</c>, so a refused request opens no
/// socket and resolves no name. Refusal = <see cref="HccException"/> <see cref="HccErrorCode.NET_OFFLINE"/> (details.reason, details.integration).
/// Every decision is audited (refusals at most once per integration per minute).
/// </summary>
public sealed class EgressGateHandler(IEgressPolicy policy, IEgressAudit audit, EgressActivity activity, TimeProvider clock) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow();
        var host = request.RequestUri?.IsAbsoluteUri == true ? request.RequestUri.Host : null;
        if (!request.Options.TryGetValue(EgressKeys.Option, out var integration))
        {
            await RefuseAsync("unknown", EgressReasons.IntegrationMissing, host, now, cancellationToken).ConfigureAwait(false);
            throw Offline("unknown", EgressReasons.IntegrationMissing);
        }

        if (policy is IEgressSettingsCache cache) await cache.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var name = EgressKeys.Name(integration);
        var decision = policy.Evaluate(integration, now);
        if (!decision.Allowed)
        {
            await RefuseAsync(name, decision.Reason, host, now, cancellationToken).ConfigureAwait(false);
            throw Offline(name, decision.Reason);
        }

        await AuditAsync(new EgressAuditEntry(now, name, true, decision.Reason, host), cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefuseAsync(string integration, string reason, string? host, DateTimeOffset now, CancellationToken ct)
    {
        if (activity.Refused(integration, reason, now)) await AuditAsync(new EgressAuditEntry(now, integration, false, reason, host), ct).ConfigureAwait(false);
    }

    private async Task AuditAsync(EgressAuditEntry e, CancellationToken ct)
    {
        try
        {
            await audit.WriteAsync(e, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An audit failure never opens the gate and never breaks a refusal; the decision stands.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    public static HccException Offline(string integration, string reason) =>
        new(HccErrorCode.NET_OFFLINE, "Internet is switched off for this feature in Master configuration › Internet.", new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonSerializer.SerializeToElement(reason),
            ["integration"] = JsonSerializer.SerializeToElement(integration),
        });
}
