using Hcc.Contracts.Setup;

namespace Hcc.Application.Setup;

/// <summary>
/// First-time hotel setup (W3-b). The endpoint enforces "server PC only" (loopback); the implementation enforces "empty
/// installation only" inside its transaction (refused once any property exists) and idempotency on the Idempotency-Key.
/// </summary>
public interface IPropertySetup
{
    Task<SetupStatusResult> StatusAsync(CancellationToken ct = default);

    /// <summary>Property + first Owner + Master configuration identity in one transaction; same key + same request → the stored result.</summary>
    Task<SetupPropertyResult> SetupAsync(string idempotencyKey, SetupPropertyRequest request, CancellationToken ct = default);
}
