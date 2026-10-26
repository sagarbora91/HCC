namespace Hcc.Contracts;

/// <summary>GET /api/v1/system/health result. <c>Database</c>/<c>SchemaVersion</c> are null for anonymous LAN callers (F-L01).</summary>
public sealed record HealthResult(string Status, string? Database, int? SchemaVersion, string ServerTimeUtc);
