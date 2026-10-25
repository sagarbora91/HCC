namespace Hcc.Contracts.Setup;

/// <summary>
/// POST /api/v1/setup/SetupProperty (header Idempotency-Key): first-time hotel setup on an EMPTY installation, from the server PC
/// itself (loopback) only. Creates the property and the first Owner (password, DEC-062) in one transaction and seeds the Master
/// configuration hotel identity (hotel, brand.address, brand.gstin, checkin, checkout). Refused once any property exists.
/// Optional fields: <see cref="Code"/> (default <c>MAIN</c>), <see cref="Gstin"/>, <see cref="State"/> (default Maharashtra),
/// <see cref="BusinessDate"/> (default today in the time zone), <see cref="TimeZone"/> (default <c>Asia/Kolkata</c>, IANA),
/// <see cref="CheckInTime"/>/<see cref="CheckOutTime"/> (HH:mm, default 14:00 / 11:00).
/// </summary>
public sealed record SetupPropertyRequest(
    string Name,
    string Address,
    string OwnerLoginName,
    string OwnerDisplayName,
    string OwnerPassword,
    string? Code = null,
    string? Gstin = null,
    string? State = null,
    DateOnly? BusinessDate = null,
    string? TimeZone = null,
    string? CheckInTime = null,
    string? CheckOutTime = null);

public sealed record SetupPropertyResult(
    int PropertyId,
    string Code,
    string Name,
    string State,
    string? Gstin,
    DateOnly BusinessDate,
    string TimeZone,
    string CheckInTime,
    string CheckOutTime,
    int OwnerUserId,
    string OwnerLoginName,
    int SettingsSeeded,
    string Message);

/// <summary>GET /api/v1/setup/status (loopback only): <see cref="SetupRequired"/> = no property exists yet.</summary>
public sealed record SetupStatusResult(bool SetupRequired, int Properties);
