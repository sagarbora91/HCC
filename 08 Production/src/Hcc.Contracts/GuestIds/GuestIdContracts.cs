namespace Hcc.Contracts.GuestIds;

// DEC-068 guest ID numbers. The full number leaves the server only in RevealGuestIdResult (never in a stored result, audit,
// log, outbox event or error). Legal risk recorded in DEC-068 (Aadhaar Act 2016 / UIDAI masking rules) — CA/lawyer to confirm.

/// <summary>POST /api/v1/commands/RevealGuestId (K). Needs the ID-view permission (PERM-GST-003); writes an audit row without the number.
/// DEC-193: <c>PartySeq</c> (1–6) reveals an accompanying guest's own ID instead of the registered guest's; the audit row carries the seq.</summary>
public sealed record RevealGuestIdRequest(long GuestId, int? PartySeq = null);

/// <summary><c>IdNumber</c> = normalised full number; <c>Formatted</c> = readable grouping (Aadhaar in groups of 4). Not cached by the client.</summary>
public sealed record RevealGuestIdResult(long GuestId, string IdType, string IdNumber, string Formatted, string IdMasked);

/// <summary>POST /api/v1/commands/RekeyGuestIds (K). Owner only: re-encrypts every stored number under the current key (key rotation).</summary>
public sealed record RekeyGuestIdsRequest(string Reason);

/// <summary><c>Rekeyed</c> = guest ID numbers re-encrypted; <c>FromKeyIds</c> = the keys any re-encrypted value was under; <c>SealedSecretsRekeyed</c> = other
/// values sealed with the same keys and re-encrypted with them (ID capture images and OCR text, A07-07).</summary>
public sealed record RekeyGuestIdsResult(int Rekeyed, string CurrentKeyId, IReadOnlyList<string> FromKeyIds, int SealedSecretsRekeyed = 0);
