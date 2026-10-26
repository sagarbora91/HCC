using Hcc.Application.Documents;
using Hcc.Application.Messaging;

namespace Hcc.Api.Hosting;

/// <summary>
/// A09-08: which adapters stand behind the messaging and OCR ports. The simulated ones (a fake gateway that marks messages as sent, an OCR
/// engine that invents a checksum-valid Aadhaar number for any image) are for Development and the tests only. In the Production
/// environment the "not configured" adapters are registered instead: they refuse clearly and never pretend. The decision is taken when the
/// service is first resolved, from the host's own <see cref="IHostEnvironment"/>, so no host can forget to opt out of the simulators; a
/// service collection without a host (a unit test) cannot be Production and gets the simulators.
/// </summary>
public static class SimulatedAdapters
{
    /// <summary>True outside the Production environment (Development, Staging, tests); false in Production.</summary>
    public static bool Allowed(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetService<IHostEnvironment>() is not { } env || !env.IsProduction();
    }

    public static IMessageGateway MessageGateway(IServiceProvider services) =>
        Allowed(services) ? new FakeMessageGateway() : new NotConfiguredMessageGateway();

    public static IIdOcrEngine IdOcrEngine(IServiceProvider services) =>
        Allowed(services) ? new SimulatedIdOcrEngine() : new NotConfiguredIdOcrEngine();
}
