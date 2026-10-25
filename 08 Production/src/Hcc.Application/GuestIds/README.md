# Guest ID numbers (DEC-068) — Wave 1 agent C

## Legal note (DEC-068, recorded as the owner decided)
**The full ID number is stored for every ID type, including Aadhaar — owner decision of 25 Oct 2026, taken against advice (APR-012).**
**Legal risk:** UIDAI regulations require hotels to keep only a masked Aadhaar, and storing full Aadhaar numbers can be an offence
under the Aadhaar Act 2016. The owner was told. **A CA / lawyer must confirm before production.** Until then the data stays fictional
(test numbers start with 1 or Z and are obviously fake). If the ruling goes against storage, switch registration back to last 4 only and
purge `gst.guest_id_secret` (the display value `gst.guest.id_last4` is independent of it).

## What is stored where
| Data | Where | Notes |
|---|---|---|
| Full number (normalised, upper-case, no spaces/hyphens) | `gst.guest_id_secret.sealed` (migration 0601) | AES-256-GCM: `format 1 | nonce 12 | tag 16 | ciphertext`; associated data `HCC-GUESTID-v1|property|guest|keyId` (a blob copied to another guest fails) |
| Key id of the sealing key | `gst.guest_id_secret.key_id` | per row, so keys can rotate |
| Last 4 (display) | `gst.guest.id_last4` (0300) | derived from the number |
| Keys | **never in git or SQL** | configuration `Hcc:GuestIdKeys` (below), DPAPI-wrapped |

SQL Server Always Encrypted is not used: it is not available on every Express install, and application-level AES-GCM keeps the key
off the database server's backups. `System.Security.Cryptography` (BCL) only; DPAPI via crypt32 P/Invoke (no extra NuGet package).

## Rules (enforced and tested in `tests/Hcc.GstIds.Tests`)
- Per-type validation (prototype `guestIdNumberError`, same wording): Aadhaar 12 digits + Verhoeff checksum; passport 1 letter + 7 digits;
  driving licence 2 letters + 13 digits; voter ID 3 letters + 7 digits; PAN 5 letters + 4 digits + 1 letter; other 4–20 letters/digits.
  Messages never echo the input.
- Registration needs the number, or (profiles saved before DEC-068) the last 4. Editing without retyping keeps the stored number; changing
  the ID type or the last 4 without a new number is VALIDATION.
- Reads (`GET /guests/{id}`) never carry the number: `idMasked` = `XXXX-XXXX-1234` for holders of PERM-GST-003 (Owner, Manager, Reception),
  `Restricted` for everyone else (prototype `guestView`); `idOnFile` / `canRevealId` flags.
- `POST /commands/RevealGuestId {guestId}` — PERM-GST-003 only; writes audit `GUEST_ID_VIEW` (who, when, which guest; never the number or
  its last 4); the stored command result (`plat.idempotency.result_json`) holds no number; a replay of the same Idempotency-Key returns the
  number again under the original audit row. Response `Cache-Control: no-store`.
- The number never appears in audit text, outbox events, logs, error messages or the idempotency record. The idempotency payload hash is
  computed over the request with the number replaced by a keyed HMAC fingerprint (an unsalted SHA-256 of a payload containing an Aadhaar
  number could otherwise be brute-forced).
- No key configured → the module is not registered; RegisterGuest refuses a full number (UNAVAILABLE) instead of storing it in clear.
- Wrong key / altered data / missing key → UNAVAILABLE without the number (fail closed).

## Key configuration
```
"Hcc": { "GuestIdKeys": { "Current": "2026-10", "Keys": { "2026-10": "dpapi-user:AQAAANCMnd8B…" } } }
```
Put this in the service account's environment (`Hcc__GuestIdKeys__Current`, `Hcc__GuestIdKeys__Keys__2026-10`) or an
`appsettings.Production.json` outside the repository with an ACL for the service account only. Schemes: `dpapi-user:` (bound to the
Windows account that runs HCC — preferred), `dpapi-machine:` (any process on that PC), `plain:` (development/tests only, and only with
`Hcc:GuestIdKeys:AllowPlainKeys=true`). Generate a key **as the service account**, in Windows PowerShell 5.1 (`powershell.exe`):
```
Add-Type -AssemblyName System.Security
$k = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($k)
$e = [Text.Encoding]::ASCII.GetBytes('HCC guest ID key v1')
'dpapi-user:' + [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect($k, $e, 'CurrentUser'))
```
(`DpapiGuestIdKeyRing.NewWrappedKey` does the same from code.) **Back up the key**: a DPAPI-user blob is useless on another PC or
account, so also keep an offline copy of the raw key (sealed envelope / owner's password manager). Losing every copy makes the stored
numbers unreadable (last 4 stay). Database backups alone never contain the key.

## Key rotation
1. Generate a new key (above) under a new id, add it to `Keys`, keep the old one, set `Current` to the new id, restart HCC.
   New and edited numbers are sealed under the new key at once; old rows still open with the old key.
2. As the Owner: `POST /commands/RekeyGuestIds {reason}` (PERM-ADM-003 'A') — re-encrypts every row not under `Current` in one
   transaction and audits `GUEST_ID_REKEY` (count and key ids only).
3. Check `SELECT key_id, COUNT(*) FROM gst.guest_id_secret GROUP BY key_id` shows only the new id, then remove the old key from
   configuration (and destroy its copies). Suggested cadence: yearly, and at once if a key may have leaked.

## Requested / open (owner or lead)
- CA/lawyer ruling on storing full Aadhaar (above).
- Where the production key lives (service-account environment vs protected appsettings) and who holds the offline copy — P7 deployment.
