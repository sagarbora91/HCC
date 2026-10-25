namespace Hcc.Contracts;

/// <summary>
/// Error codes on the wire (contracts package §2). The names are serialized verbatim and match the prototype's
/// <c>ErrorCode</c> union in <c>03 Prototype/src/domain/errors.ts</c>, except the prototype-only fault-injection codes
/// (<c>SIMULATED_REJECTION</c>, <c>RESULT_UNKNOWN</c>), plus the three production-only codes from the contracts table and
/// <c>UNAUTHENTICATED</c> (Wave 1 integration: no session, or the session expired/was revoked → sign in again).
/// </summary>
#pragma warning disable CA1707 // Identifiers should not contain underscores: names are the wire codes.
public enum HccErrorCode
{
    VALIDATION,
    FORBIDDEN,
    NOT_FOUND,
    NOT_AVAILABLE,
    RESTRICTED,
    INVALID_TRANSITION,
    STALE_VERSION,
    DAY_CLOSED,
    APPROVAL_REQUIRED,
    IDEMPOTENCY_MISMATCH,
    UNAVAILABLE,
    UNAUTHENTICATED,
}
#pragma warning restore CA1707
