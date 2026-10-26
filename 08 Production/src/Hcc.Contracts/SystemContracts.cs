namespace Hcc.Contracts;

/// <summary>GET /api/v1/system/health result. <c>Database</c>/<c>SchemaVersion</c> are null for anonymous LAN callers (F-L01). While the
/// database schema is behind the server build (A09-04) health answers 503 with an <c>ErrorResponse</c> (UNAVAILABLE, reason SCHEMA_BEHIND) instead.</summary>
public sealed record HealthResult(string Status, string? Database, int? SchemaVersion, string ServerTimeUtc);
