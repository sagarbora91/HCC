namespace Hcc.Contracts;

/// <summary>HTTP status and user-facing title per <see cref="HccErrorCode"/> (contracts package §2; titles as the prototype's errorTitle).</summary>
public static class HccErrorCatalog
{
    public static int HttpStatus(HccErrorCode code) => code switch
    {
        HccErrorCode.VALIDATION => 422,
        HccErrorCode.FORBIDDEN => 403,
        HccErrorCode.NOT_FOUND => 404,
        HccErrorCode.NOT_AVAILABLE => 409,
        HccErrorCode.RESTRICTED => 409,
        HccErrorCode.INVALID_TRANSITION => 409,
        HccErrorCode.STALE_VERSION => 409,
        HccErrorCode.DAY_CLOSED => 409,
        HccErrorCode.APPROVAL_REQUIRED => 403,
        HccErrorCode.IDEMPOTENCY_MISMATCH => 409,
        HccErrorCode.UNAVAILABLE => 503,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown error code."),
    };

    public static string Title(HccErrorCode code) => code switch
    {
        HccErrorCode.VALIDATION => "Check the details",
        HccErrorCode.FORBIDDEN => "Not permitted",
        HccErrorCode.NOT_FOUND => "Record not found",
        HccErrorCode.NOT_AVAILABLE => "Not available",
        HccErrorCode.RESTRICTED => "Stay restriction",
        HccErrorCode.INVALID_TRANSITION => "Action not allowed now",
        HccErrorCode.STALE_VERSION => "Record changed",
        HccErrorCode.DAY_CLOSED => "Business day closed",
        HccErrorCode.APPROVAL_REQUIRED => "Approval required",
        HccErrorCode.IDEMPOTENCY_MISMATCH => "Request key already used",
        HccErrorCode.UNAVAILABLE => "Server unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown error code."),
    };
}
