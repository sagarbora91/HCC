using Hcc.Domain.Reporting;

namespace Hcc.Application.Reporting;

/// <summary>F12 MEAS (MEAS-DIGEST item 6, review M2): the Owner digest's "Measurement" block for a closed business date. Implemented in Hcc.Reporting over the
/// MEAS report lanes' own exception rules (red rows) and the measurement gate; null = no block (every item off / nothing checkable / not wired).</summary>
public interface IMeasDigestSource
{
    Task<MeasDigestBlock?> BuildAsync(int propertyId, DateOnly date, CancellationToken ct = default);
}
