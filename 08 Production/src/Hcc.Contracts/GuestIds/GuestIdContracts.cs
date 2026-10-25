namespace Hcc.Contracts.GuestIds;

// DEC-068 guest ID numbers. The full number leaves the server only in RevealGuestIdResult (never in a stored result, audit,
// log, outbox event or error). Legal risk recorded in DEC-068 (Aadhaar Act 2016 / UIDAI masking rules) — CA/lawyer to confirm.

/// <summary>POST /api/v1/commands/RevealGuestId (K). Needs the ID-view permission (PERM-GST-003); writes an audit row without the number.</summary>
public sealed record RevealGuestIdRequest(long GuestId);

/// <summary><c>IdNumber</c> = normalised full number; <c>Formatted</c> = readable grouping (Aadhaar in groups of 4). Not cached by the client.</summary>
public sealed record RevealGuestIdResult(long GuestId, string IdType, string IdNumber, string Formatted, string IdMasked);

/// <summary>POST /api/v1/commands/RekeyGuestIds (K). Owner only: re-encrypts every stored number under the current key (key rotation).</summary>
public sealed record RekeyGuestIdsRequest(string Reason);

public sealed record RekeyGuestIdsResult(int Rekeyed, string CurrentKeyId, IReadOnlyList<string> FromKeyIds);
