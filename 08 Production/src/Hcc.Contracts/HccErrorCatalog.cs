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
        HccErrorCode.UNAUTHENTICATED => 401,
        HccErrorCode.NET_OFFLINE => 503, // F5-E1
        HccErrorCode.HCC_HALL_CLASH or HccErrorCode.HCC_TNC_PLACEHOLDER or HccErrorCode.HCC_QUO_NOT_LATEST or HccErrorCode.HCC_QUO_EXPIRED
            or HccErrorCode.HCC_HALL_CAPACITY => 409, // F6-H1
        HccErrorCode.HCC_DISCOUNT_LIMIT or HccErrorCode.HCC_TNC_OWNER_ONLY => 403, // F6-H1
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
        HccErrorCode.UNAUTHENTICATED => "Sign in again",
        HccErrorCode.NET_OFFLINE => "Internet is off", // F5-E1
        HccErrorCode.HCC_HALL_CLASH => "Hall already booked", // F6-H1 (titles as the prototype's errorTitle)
        HccErrorCode.HCC_TNC_PLACEHOLDER => "T&C not final",
        HccErrorCode.HCC_QUO_NOT_LATEST => "Not the latest version",
        HccErrorCode.HCC_QUO_EXPIRED => "Quotation expired",
        HccErrorCode.HCC_DISCOUNT_LIMIT => "Discount needs approval",
        HccErrorCode.HCC_HALL_CAPACITY => "Above hall capacity",
        HccErrorCode.HCC_TNC_OWNER_ONLY => "Owner only",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown error code."),
    };
}
