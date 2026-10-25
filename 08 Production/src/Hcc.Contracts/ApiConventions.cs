namespace Hcc.Contracts;

/// <summary>Constants shared by server and clients (contracts package §2).</summary>
public static class ApiConventions
{
    public const string BasePath = "/api/v1";
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const int IdempotencyKeyMaxLength = 80;
    public const int MaxPageSize = 100;

    /// <summary>₹10 crore per money field = 10,000,000,000 paise.</summary>
    public const long MaxPaisePerField = 10_000_000_000L;

    /// <summary>Largest integer a JSON number can carry exactly in a JavaScript client (2^53 − 1).</summary>
    public const long JsonSafeIntegerMax = 9_007_199_254_740_991L;
}
