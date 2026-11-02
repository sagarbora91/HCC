using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hcc.Infrastructure.Egress;

/// <summary>
/// Registers the policy, the gate handler and the ONE named client "egress" (EGRESS-CONTRACT §1). The gate is added first, so it is the
/// outermost handler; adapters add their own handlers after it. Needs an <see cref="IEgressSettingsSource"/> registration (the Api's
/// Master configuration source); <see cref="IEgressAudit"/> defaults to none.
/// </summary>
public static class EgressServiceCollectionExtensions
{
    public static IServiceCollection AddHccEgress(this IServiceCollection s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.TryAddSingleton(TimeProvider.System);
        s.TryAddSingleton<IEgressAudit, NullEgressAudit>();
        s.TryAddSingleton<EgressActivity>();
        s.TryAddSingleton<EgressPolicy>();
        s.TryAddSingleton<IEgressPolicy>(sp => sp.GetRequiredService<EgressPolicy>());
        s.TryAddSingleton<IEgressSettingsCache>(sp => sp.GetRequiredService<EgressPolicy>());
        s.TryAddTransient<EgressGateHandler>();
        s.AddHttpClient(EgressKeys.ClientName).AddHttpMessageHandler<EgressGateHandler>();
        return s;
    }
}
