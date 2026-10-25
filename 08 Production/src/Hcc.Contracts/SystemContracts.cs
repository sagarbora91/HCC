namespace Hcc.Contracts;

/// <summary>GET /api/v1/system/health result.</summary>
public sealed record HealthResult(string Status, string Database, int SchemaVersion, string ServerTimeUtc);
