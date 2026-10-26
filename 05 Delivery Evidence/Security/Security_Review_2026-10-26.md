# HCC Security Review: production server and production client (Wave 5 preparation)

- **Date:** 2026-10-26
- **Job:** G8, report only. No product code was changed.
- **Baseline:** branch `claude/w4g8-secreview` from `claude/wave4-integration` @ `245a709`
- **Scope:**
  - `08 Production\`: .NET 10 API, Application, Infrastructure.SqlServer, migrations, `deployment\windows\*`
  - production client: `03 Prototype\src\clients\` (`sync\*`, `http.ts`)
  - `04 Android\` (Capacitor `productionDebug` build type, manifests, network security config)
- **Method:**
  - manual code review of every endpoint file (`src\Hcc.Api\Endpoints\*.cs`) and the Access, GuestIds, Documents, WebBooking, Import, Configuration and command-runner code
  - scripted cross-checks, kept outside the repo:
    - every `Map*` route matched to the `IAccessPolicy` permission ids used by its handler and that handler's private helpers
    - every public Application method that takes an actor, checked for a permission call
    - every interpolated hole in a SQL string checked to be a constant or a whitelist
  - `dotnet test tests\Hcc.Access.Tests -maxcpucount:1`: 70/70 passed, using disposable `HccTest_*` databases on `.\SQLEXPRESS`
  - no live probes against real data

## Summary

| Severity | Count |
|---|---|
| Critical | 0 |
| High | 4 |
| Medium | 7 |
| Low | 11 |
| Info | 6 |

The server code is in good shape. All 373 routes were enumerated:

- **Staff routes:** 363 need a validated session (`RequireHccSession`). 358 of these also call `IAccessPolicy` in the handler. The other 5 are deliberately session-only: own capabilities, business date, module switches, non-secret configuration values, and changing your own credential (which needs the current secret).
- **Unauthenticated routes:** 10. These are health, discovery, sign-in, sign-out, the loopback-only first-time setup routes (owner bootstrap, setup status, SetupProperty) and the 3 public booking routes. Each one is justified.
- **Other checks:**
  - No SQL injection was found.
  - Guest ID numbers are sealed correctly with AES-256-GCM (AAD bound to property, guest and key), and no plaintext was found in audit, outbox or idempotency records.
  - Password and PIN hashing and session handling are sound.

The High findings are about deployment and the client, not the API logic:

1. The offline read cache shows the last user's data without authentication.
2. The "production" APK is a debug build that allows backup.
3. The backup folder ACL is inherited and the backups are unencrypted.
4. There is no safe way to expose the public booking routes beyond the LAN.

## Findings

Effort: S = under half a day, M = 1–2 days, L = more than 2 days.

| ID | Severity | Location (file:line) | Description | Evidence | Recommended fix | Effort |
|---|---|---|---|---|---|---|
| F-H01 | High | `03 Prototype\src\clients\sync\runtime.ts:246-256`; `cache.ts:1-4,166-191` | **Offline read cache is shown without authentication.** At boot the cached slices are loaded into state first. If `/me/capabilities` cannot reach the server, `status='ready'` is set whenever a cache exists, so the last user's guests, reservations, folios, payroll board, audit log, OTA and web-request contacts, users and so on are displayed with no session check. The cache sits in IndexedDB `hcc-server-cache` unencrypted. It is cleared only on sign-out or user switch, not when the session expires while the device is idle. | `boot(){ const c=await cache.load(); … if(r==='unreachable'){ if(c)status='ready'; …}}`. Anyone holding a shared tablet or front-desk PC can switch off Wi-Fi, open the app and read the cache. | Require a local unlock before showing cached data: at minimum, a still-valid unexpired token on this device (`tokenAuth.expired()`/`hasSession()`) plus the same `userId`. Wipe the cache on idle expiry or absolute expiry. Exclude PII-heavy slices (guests, audit, payroll, users, OTA/web messages) from the cache, or encrypt the cache with a key held only in memory or the Android Keystore. | M |
| F-H02 | High | `04 Android\android\app\build.gradle:24-31`; `android\app\src\main\AndroidManifest.xml:4` | **The "production" APK is a debug build.** `productionDebug { initWith debug }`, so the APK is `debuggable=true`, signed with the debug key, has WebView remote debugging on (Capacitor default for debuggable apps), and `allowBackup="true"`. On any tablet with USB debugging on, `adb run-as`, `adb backup` or `chrome://inspect` gives the session token (Capacitor Preferences), the device token (`localStorage hcc-device-token`), the staff roster and the whole IndexedDB read cache. Anyone can also sideload an APK signed with the same debug key as an "update". | `README.md:31-39` ("productionDebug … installable next to the prototype"); manifest `allowBackup="true"`. | Add a `productionRelease` build type: `debuggable false`, minify on, release keystore kept off the repo, `webContentsDebuggingEnabled:false`, `android:allowBackup="false"` plus `dataExtractionRules`/`fullBackupContent` excluding everything, and `applicationId` without `.prototype`. | M |
| F-H03 | High | `08 Production\deployment\windows\Initialize-HccDatabase.ps1:49-50`; `Backup-HccDatabase.ps1:110`; `Restore-HccDrill.ps1:54` | **Database backups are readable by local users and not encrypted.** The backup folder (e.g. `D:\HccBackups`) is created with inherited ACLs; on a non-system drive root that normally gives *Authenticated Users* Modify. The scripts only *add* the SQL service account and never remove inheritance. SQL Express cannot encrypt backups. Any Windows user on the hotel PC, typically the same PC staff use, can copy full backups: guest names, phones, stays, payroll with bank account numbers, audit. The sealed ID numbers can also be opened if the key is `dpapi-machine` (F-M05). | `New-Item -ItemType Directory …; icacls $BackupDirectory /grant "<sqlService>:(OI)(CI)M"`, with no `/inheritance:r`. Same for `LogDirectory` and `ScratchDirectory`. | Use `icacls <dir> /inheritance:r /grant:r "SYSTEM:(OI)(CI)F" "BUILTIN\Administrators:(OI)(CI)F" "<sqlService>:(OI)(CI)M" "<ops account>:(OI)(CI)M"` in Initialize and assert it in Backup. Document BitLocker on the backup volume and encrypt off-site copies (e.g. a 7-Zip AES archive, or a password-protected copy with the password kept by the owner). | S |
| F-H04 | High (go-live gate if web booking is turned on) | `src\Hcc.Api\Hosting\HccHosting.cs:112-113`; `Endpoints\WebBookingEndpoints.cs:31`; `Endpoints\AccessEndpoints.cs:136-137`; `Endpoints\SetupEndpoints.cs:30-41`; `ETP_Reuse_Pending.md:41` | **The public booking routes have no safe exposure path.** They are served by the same Kestrel listener as the whole staff API. **Option (a), port-forward 5443:** sign-in (no per-client throttle, F-M02), bootstrap/setup probing and all 360+ staff routes become internet-facing. **Option (b), local reverse proxy (IIS/Caddy/nginx to 127.0.0.1:5080):** every request arrives from loopback, which has three effects: (i) the loopback gate of `/auth/bootstrap-owner` and `/setup/*` passes for internet callers while the install is empty; (ii) the per-IP limiter becomes one global bucket, so one client can block all bookings; (iii) the session cookie is issued without `Secure` (`IsHttps` is false). There is no `UseForwardedHeaders`, no host filtering (`AllowedHosts:"*"`) and no path allow-list. | `client = http.Connection.RemoteIpAddress`; `IsServerPc(remote)`; `Secure = http.Request.IsHttps`. `ETP_Reuse_Pending.md` #35 already notes "a reverse proxy / CAPTCHA before exposing the public routes beyond the LAN". | Before enabling `Hcc:WebBooking:Enabled` beyond the LAN: run the public routes on a **separate listener/port** (or a separate host process) that maps only `/api/v1/public/booking/*`. Put it behind a reverse proxy with `ForwardedHeaders` restricted to the proxy's address, rate limiting and a CAPTCHA. Refuse loopback-gated routes when `X-Forwarded-For` is present. Document the topology in INSTALL.md. | M |
| F-M01 | Medium | `src\Hcc.Infrastructure.SqlServer\Access\SqlSessionService.cs:49,98,130-150,174,180-182` | **Sign-in reveals which accounts exist.** Unknown names return `BAD_CREDENTIALS` after a 1,000-iteration dummy PBKDF2, but real users are verified at 600,000 (password) or 210,000 (PIN) iterations, a large timing gap. Disabled and role-less users skip hashing entirely. Response `details.reason` also differs: `WRONG_CREDENTIAL_KIND` (and the message says which kind), `DEVICE_NOT_REGISTERED` and `LOCKED_OUT` (with `lockedUntilUtc`) are returned only for existing accounts. A LAN client can list every login and tell password roles (Owner/Accountant/Tech admin) from PIN roles. | `Dummy = new(…, 1000, …)`; `Forbidden(msg, "WRONG_CREDENTIAL_KIND")`; `DEVICE_NOT_REGISTERED` before any secret check. | Give the dummy hash the configured `PasswordIterations` (or PIN iterations, matching the attempted kind). Hash on the disabled and no-role paths. Return one generic `BAD_CREDENTIALS` to the client and keep the precise reason in the audit row only. The device check message can stay, but only after the user is known *and* the PIN verifies. | S |
| F-M02 | Medium | `src\Hcc.Api\Endpoints\AccessEndpoints.cs:111-124`; `SqlSessionService.cs:96-100,185-200` | **No per-client throttling on `/auth/sign-in`.** The only control is a per-credential lockout (5 failures for 15 minutes). Any LAN device can (a) lock the Owner and every staff account repeatedly (denial of service at shift change); (b) spray one password across all accounts; (c) flood `plat.audit_event` with `SIGN_IN_FAILED` rows for unknown names. The audit table is append-only and never pruned. | No limiter on the route; the `PublicRateLimiter` is used only by web booking. | Add an ASP.NET Core rate limiter (fixed or sliding window per remote IP plus per login) on `/auth/*` and `ChangeOwnCredential`, and an exponential back-off per login. Consider lockouts that do not lock the Owner from the server PC (loopback). | S |
| F-M03 | Medium | `SqlUserAdministration.cs:189-209,611-616`; seed `0101_access_rbac_seed.sql:199-200` | **Tech admin can take over money-role accounts.** `SetCredential` needs only PERM-ADM-001 (held by OWN and **TAD**). The only target protection is "Owner accounts need an Owner". A Tech admin can reset the password of an Accountant or the PIN of a Manager, sign in as them, and use money and approval permissions (refunds, approvals, payroll, Tally export). This bypasses the DEC-058 rule that money roles are Owner-assigned. The action is audited (`CREDENTIAL_SET`), but the user is not forced to change the credential at next sign-in, so the admin keeps a working secret. | `EnsureOwnerForOwnerAccountAsync` returns when the target has no `OWN` role; `EnsureMayAssignAsync` is not applied to credentials. | Require an Owner for `SetCredential`, `SetUserEnabled` and `RevokeDevice` on any user holding an `owner_only_assign` role (reuse `EnsureMayAssignAsync` logic over the target's roles). Add a `must_change` flag set by an admin reset and enforce it at sign-in. Notify the Owner of credential resets on money-role accounts. | S |
| F-M04 | Medium | `deployment\windows\New-HccCertificate.ps1:77-91`; `INSTALL.md:83`; `ANDROID_SERVER_ADDRESS.md` | **The hotel CA is a general-purpose trusted root, and its private key stays on the server.** "HCC Hotel CA" is created with `KeyExportPolicy Exportable` in `LocalMachine\My` and is removed only when `-ExportCaPfx` is passed (optional). It has no Name Constraints. INSTALL.md tells staff to install it into **Local Machine › Trusted Root** on every PC, and Android user CA stores trust it in Chrome. Anyone who compromises the hotel PC (or an admin) can mint certificates for *any* domain (bank, Gmail) that every enrolled PC and phone browser accepts. | `New-SelfSignedCertificate … -KeyUsage CertSign … -KeyExportPolicy Exportable … -TextExtension @('2.5.29.19={critical}{text}ca=true&pathlength=0')`; no `2.5.29.30`. | Make CA key export plus removal from the store the default (fail if not exported). Add a critical Name Constraints extension permitting only the hotel's DNS names and IP ranges (`2.5.29.30={critical}{text}dNSName=…&IPAddress=192.168.0.0/16…`). Alternatively, skip the CA: pin the single server certificate in the APK and use a per-hotel cert for browsers. | S |
| F-M05 | Medium | `src\Hcc.Api\Hosting\HccHosting.cs:36-74`; `Endpoints\GuestIdEndpoints.cs:19-23`; `Infrastructure.SqlServer\GuestIds\DpapiGuestIdKeyRing.cs:15,227-230`; `Application\GuestIds\README.md:38-53`; `deployment\windows\INSTALL.md`; `Install-HccService.ps1:58` | **Guest-ID key handling is not production-hardened.** (1) The production config validator does not check `Hcc:GuestIdKeys` at all: `AllowPlainKeys=true` with `plain:` keys starts in Production, and `dpapi-machine:` (openable by *any* process or user on the PC) is accepted silently. (2) The documented generator `dotnet run … guest-id-key new` does not exist (`Program.cs` handles only `migrate`), and the README PowerShell must run "as the service account", which is impractical for the default virtual account `NT SERVICE\HccServer`. (3) INSTALL.md has no key provisioning, escrow or rotation step. (4) `appsettings.Production.json` lives in `C:\Program Files\HCC\Server`, readable by `BUILTIN\Users`; `Install-HccService.ps1` only adds RX for the service. The likely outcome is no key (the ID module silently not registered) or a weak plain or machine key. | Validator has no `GuestIdKeys` rule; `Unwrap` accepts `dpapi-machine`; `AddGuestIdModule` returns when `Current` is empty. | In Production: refuse `AllowPlainKeys` and `plain:`, warn or refuse `dpapi-machine`, and fail start-up when the key is missing if ID capture is in use. Ship `Hcc.Api.exe guest-id-key new <id>` (runs under the service account via `Install-HccService.ps1`). Add an INSTALL.md section covering generation, offline escrow and rotation. Set the ACL of `appsettings.Production.json` to SYSTEM, Administrators and the service account only. | M |
| F-M06 | Medium | `deployment\windows\Register-HccBackupTasks.ps1:159,163-169,179`; `Hcc-Common.ps1:29-33` | **Scheduled backup tasks can let a local user run code as SYSTEM.** The tasks run as **SYSTEM** with `-ExecutionPolicy Bypass -File <ScriptDirectory>\Backup-HccDatabase.ps1`, and `ScriptDirectory` defaults to wherever the installer was run from (`$PSScriptRoot`, e.g. a Downloads or USB copy). The scripts dot-source `Hcc-Common.ps1` and read `hcc-ops.json` from that folder. If the folder is writable by a non-admin, editing any of these files gives SYSTEM code execution. | `$ScriptDirectory = $PSScriptRoot`; `New-ScheduledTaskPrincipal -UserId 'SYSTEM' … -RunLevel Highest`. | Copy the ops scripts to `C:\Program Files\HCC\Operations` (admin-only ACL) and register from there. Refuse to register if the script folder or any `.ps1`/`.json` in it is writable by non-admins. Prefer a dedicated low-privilege account with `db_backupoperator` over SYSTEM. | S |
| F-M07 | Medium | `03 Prototype\src\clients\sync\auth.ts:22-26,52-75`; `apk.ts:212-222,320`; `instance.ts:23-25`; `SqlSessionService.cs:234-278` | **Client tokens are stored in plaintext, and PIN sessions are not tied to the device.** The APK session token goes to Capacitor Preferences (plaintext SharedPreferences), with a `sessionStorage` fallback. The long-lived **device token** goes to WebView `localStorage` (any injected script or debug session can read it), and the browser build keeps it in `localStorage` as well. On the server, a PIN session is bound to the device only at sign-in. `ValidateAsync` never re-checks `X-HCC-Device`, so a stolen bearer token works from any host on the LAN for up to 12 hours. | `localStorage.setItem('hcc-device-token', …)`; `p.set({key:'hcc-session-token', …})`; `ValidateAsync` has no device check. | Store both tokens with Android Keystore-backed encrypted storage (e.g. EncryptedSharedPreferences via a small plugin). For PIN sessions, require the device header on every request and compare its hash with `session.device_id`. Rotate the device token at registration renewal. | M |
| F-L01 | Low | `src\Hcc.Api\HccComposition.cs:62-66` | Anonymous `GET /system/health` returns the database name and schema version, and opens a SQL connection on every call (cheap unauthenticated database load). | `new HealthResult("ok", conn.Database, version, …)` | Return only `status` anonymously and move details behind a session; or cache the result for a few seconds. | S |
| F-L02 | Low | `src\Hcc.Api\appsettings.json:8`; `deployment\windows\appsettings.Production.sample.json:3`; `HccComposition.cs:58-61`; `HccHosting.cs:108-114` | No HSTS and no security headers (`X-Content-Type-Options: nosniff`, `Referrer-Policy`, `frame-ancestors`/`X-Frame-Options`, `Cache-Control: no-store` on authenticated JSON; it is set only on reveal and public routes). `AllowedHosts:"*"`. Kestrel limits are left at the defaults (30 MB body, unlimited header count) although the largest legitimate request is about 5.4 MB (4 MB image as base64). The server also does not host the web build, so its CSP is undefined (see F-I06). | No `UseHsts`, no header middleware, no `Limits` configured. | Add a small header middleware with `no-store` for all `/api/v1` responses. Set `AllowedHosts` to the server name and IP. Set `MaxRequestBodySize` to about 8 MB globally. Add HSTS once browsers are used. | S |
| F-L03 | Low | `src\Hcc.Application\WebBooking\WebBookingService.cs:198-216`; `Infrastructure.SqlServer\Commands\SqlCommandRunner.cs:121-129`; migration `0001_platform.sql:20` | The plaintext **deposit-link token** is returned by the handler and therefore stored in `plat.idempotency.result_json`, contrary to "only `TokenHash` is stored" (`WebBookingRules.cs:108`). More generally, idempotency rows (results containing names and phones) and sessions are never purged. | `return new WebDepositLinkResult(link.LinkId, r.RequestId, token, "/pay/" + token, …)` inside `runner.RunAsync`. | Return the token outside the runner, as `RegisterDeviceAsync` already does. Add a retention job (for example, idempotency records older than 30 days and revoked or expired sessions older than 90 days). | S |
| F-L04 | Low | `src\Hcc.Application\GuestIds\GuestIdService.cs:106-128`; `Documents\DocumentService.cs:145-181` | Replaying an earlier reveal (same principal, same `Idempotency-Key`) returns the **full ID number or image again with no new audit row**. Replays also skip any permission check placed inside a handler, so a user whose ID-view permission was later revoked could still replay. The outer `RequireAsync` runs first, so the second case applies only to checks inside the handler. | `if (sealedId is null) { // Replay … that request's audit row covers it }` | On a replayed reveal, write a `GUEST_ID_VIEW (replay)` audit row in its own transaction, or refuse reveal replays older than a few minutes. | S |
| F-L05 | Low | `WebBookingService.cs:306`; `Application\WebBooking\WebBookingPorts.cs:80-110`; `Domain\WebBooking\WebBookingRules.cs:18,60-63` | Public quote exposes exact **rooms left** per room type (competitive and occupancy intelligence). The rate limiter is in memory and per address: it resets on restart and is bypassed by IPv6 rotation. The `MaxPendingPerPhone=3` cap is bypassed by varying the phone. There is no CAPTCHA and no global cap, so spam requests can fill the staff queue. | `ToPublic(p) => new(…, p.RoomsLeft, …)` | Return `available`/`few left` instead of a count. Add a global pending-request cap and key the limiter by /64 for IPv6. A CAPTCHA belongs in front of the public site (F-H04). | S |
| F-L06 | Low | `Domain\Access\FieldMasks.cs:23-32`; `Application\Stays\StaysCommands.cs:234-266`; `Customers\CustomerQueries.cs:20,99`; `Reservations\ReservationService.cs:193-210`; `Ota\OtaQueries.cs:61`; `WebBookingService.cs:107` | Field masks (`guest.contact`, `money`) are enforced server-side only in Stays and CRM. Reservations, OTA messages and web-request detail return full phone and e-mail to anyone holding the module permission. With today's seed this matches the mask table (OWN/MGR/REC), but a **user exception** granting PERM-RES-001 or PERM-OTA-001 to a Housekeeping or Accountant user would expose contacts the mask table hides. | Mask applied only in `StaysCommands.Mask` and `CustomerViews`. | Apply `FieldMasks.For(roles)` in the reservation, OTA, web-booking and POS in-house read models (one shared helper). | M |
| F-L07 | Low | `src\Hcc.Api\HccErrorMiddleware.cs:16-20`; `Infrastructure.SqlServer\Commands\SqlCommandRunner.cs:135-139` | Errors with status 500 or above, including `UNAVAILABLE` (503) wrapping a `SqlException`, are logged with the full exception. SQL Server duplicate-key messages include the key value (e.g. phone or login), so PII can reach the Windows event log or console. The client body stays generic. | `if (status >= 500) LogUnhandled(logger, ex);` | Log the exception type, SQL error number and correlation id; drop `ex.Message` of inner `SqlException` 2601/2627. | S |
| F-L08 | Low | `deployment\windows\Initialize-HccDatabase.ps1:7-8,38` | The runtime service login is `db_owner` (so the API can migrate itself). A future injection or remote-code-execution bug would get schema rights, including `DISABLE TRIGGER plat.TR_audit_event_append_only`. | `ALTER ROLE db_owner ADD MEMBER …` | Use a separate migration principal (installer or admin) and give the runtime `db_datareader`/`db_datawriter` plus `EXECUTE`, with `DENY ALTER` on schema `plat`. | M |
| F-L09 | Low | `03 Prototype\src\clients\sync\apk.ts:16-19,85-106` | Each registered tablet stores the PIN staff roster (`loginName`, `displayName`) in `localStorage hcc-device-staff`, read with the admin's session. Together with F-M01 this gives a ready-made target list. | `saveStaff(pin)` | Accept as a usability trade-off, but clear it on `forgetDevice` and when the device is revoked (already partly done). Store display names only and resolve login names server-side. | S |
| F-L10 | Low | `04 Android\android\app\src\main\res\xml\file_paths.xml:3` | The FileProvider shares `external-path "."` (all of external storage). The provider is not exported, but any `content://` URI the app grants could reach any external file. | `<external-path name="my_images" path="." />` | Narrow it to the app's own export folder (`external-files-path`/`cache-path` with a subdirectory). | S |
| F-L11 | Low | `src\Hcc.Api\Hosting\HccHosting.cs:21,113`; `INSTALL.md:53-68` | Plain-HTTP loopback listener `127.0.0.1:5080` stays on permanently. Local processes or users on the server PC can sign in over cleartext loopback (cookie without `Secure`). Between installing the service and running `Initialize-HccProperty.ps1`, any local user can claim the first Owner. | `LoopbackHttpPort = 5080` default. | Set `LoopbackHttpPort` to 0 after setup (have `Initialize-HccProperty.ps1` do it), or accept only `/setup/*`, `/auth/bootstrap-owner` and `/system/health` on that listener. | S |
| F-I01 | Info | `src\Hcc.Infrastructure.SqlServer\**` | **SQL injection: none found.** All values are bound parameters. The 60 distinct interpolation holes in SQL strings are compile-time column lists or `FROM` fragments, lock hints chosen by a boolean, whitelisted `WHERE` fragments (`SqlReservationStore.cs:195-218`, `SqlStaysStore.cs:301`) or `QUOTENAME` (`MigrationRunner.cs:89`). `LIKE` input is escaped with `ESCAPE '!'`. Deployment scripts quote with `ConvertTo-HccSqlName`/`ConvertTo-HccSqlLiteral` and allow-list the database name (`Hcc*` only). | Script output kept outside the repo; list in this report. | None. Keep the pattern: never interpolate request data. | — |
| F-I02 | Info | `src\Hcc.Api\Endpoints\*.cs`; route table below | **Authorization coverage is complete.** Every staff route is in a `MapHccSecured()`/`RequireHccSession()` group, and the actor always comes from the session. Every Application method that takes an actor calls `IAccessPolicy.RequireAsync`/`HasAsync` (directly or through a helper such as `Require`, `ManageAsync`, `GateAsync`, `TodayAsync`, `RequireAnyAsync`) before reading or writing. Idempotency records are scoped per property, principal and operation, so one user cannot replay another user's result. | Route table: 373 routes. | None. Keep a test that enumerates `EndpointDataSource` and fails on a new anonymous route. | — |
| F-I03 | Info | `Pbkdf2SecretHasher.cs`; `SqlSessionService.cs`; `AccessEndpoints.cs:114-121`; `Domain\Access\CredentialRules.cs` | **Credential and session handling is sound.** PBKDF2-HMAC-SHA256 with 600,000 iterations (passwords) or 210,000 (PINs), a 16-byte salt and `FixedTimeEquals`. Passwords are 12–128 characters; PINs are 6 digits with a trivial-pattern list and PIN history. Session tokens are 256-bit random values, stored only as SHA-256. Cookies are `HttpOnly`, `SameSite=Strict`, `Path=/api/v1`, and `Secure` on HTTPS. Sessions expire after 30 minutes idle or 12 hours absolute, and are revoked by security stamp, user disable or device revoke. Changing your own credential revokes your other sessions. Device tokens are never stored in plaintext, even in idempotency records. | Access tests 70/70 pass. | None. | — |
| F-I04 | Info | `Application\GuestIds\GuestIdProtector.cs`; `GuestIdService.cs`; `Documents\DocumentService.cs` | **Guest-ID sealing is sound.** AES-256-GCM with a random 96-bit nonce, a 128-bit tag, and AAD of `HCC-GUESTID-v1\|property\|guest\|keyId`. The idempotency payload hash uses an HMAC fingerprint instead of the number. Audit text, outbox and errors never carry the number. ID images and OCR text are sealed with purpose-bound AAD, images are type-checked (magic bytes) and capped at 4 MB. Reveals require PERM-GST-003 and are audited, with responses sent as `no-store`. Secret settings are sealed and shown as "Set (hidden)". | — | See F-M05 (key provisioning) and F-L04 (replay audit). | — |
| F-I05 | Info | `Domain\Import\CsvTable.cs:18-30,120`; `Domain\Import\ImportMasters.cs:107-115`; `Domain\Accounting\VoucherExport.cs:26-37`; `Domain\Payroll\PayrollFiles.cs:29-35`; `Application\Invoices\SimpleInvoiceRenderer.cs:58` | **Import and export handling is sound.** CSV import is capped at 2,000,000 characters and 5,000 rows, and refuses any password, PIN, secret or ID-number column. Exported CSVs (vouchers, payroll) neutralise leading `= + - @ TAB CR` (formula injection). The invoice HTML renderer HTML-encodes every field. | — | Apply the same `CsvSafe` helper to any future CSV export (and to client-side exports in `app/native.ts`). | — |
| F-I06 | Info | `deployment\windows\INSTALL.md:3`; `src\Hcc.Api\HccComposition.cs` | INSTALL.md says staff use HCC "from a browser", but the API host serves no static files and the web build's hosting origin is not defined. With no CORS and a `SameSite=Strict` cookie scoped to `/api/v1`, the web build must be served from the same origin. Its CSP, HSTS and cache headers are unspecified. | No `UseStaticFiles`/`MapFallbackToFile`. | Decide on hosting before Wave 5: either serve `dist/production` from Kestrel with a strict CSP (`default-src 'self'`, no inline script) or use the APK only. | S |


## Go-live checklist (Wave 5)

Blocking items come first. Tick each one with evidence (screenshot, command output or commit) in `05 Delivery Evidence\Security\`.

1. [ ] **F-H02:** Build and hand out a *release* APK: not debuggable, release-signed, WebView debugging off, `allowBackup="false"`. Confirm with `aapt dump badging` / `apksigner verify --print-certs` and `adb shell run-as <pkg>` (it must fail).
2. [ ] **F-H01:** The read cache is never shown without a still-valid local session for the same user. The cache is wiped on idle or absolute expiry. PII slices are excluded or encrypted. Test: sign in, wait past the idle timeout, turn on airplane mode, relaunch; no data may be visible.
3. [ ] **F-H03:** Backup, log and drill folders have inheritance removed and allow only SYSTEM, Administrators, the SQL service and the ops account (`icacls D:\HccBackups` output attached). The backup volume uses BitLocker. Off-site copies are encrypted.
4. [ ] **F-H04:** Web booking stays `Hcc:WebBooking:Enabled=false`, or is exposed only through a separate public listener behind a reverse proxy with forwarded-header trust, rate limit and CAPTCHA. Port 5443 is **not** port-forwarded. Windows Firewall rule profile is Private/Domain only (already scripted).
5. [ ] **F-M05:** A guest-ID key is generated with the shipped command under the service account (`dpapi-user:`), with an offline escrow copy in the safe. `AllowPlainKeys` is absent. `appsettings.Production.json` has an ACL for SYSTEM, Administrators and the service account only.
6. [ ] **F-M01 / F-M02:** Sign-in returns one generic failure. Rate limiting is on `/auth/*`. Test: 20 bad sign-ins from one IP are throttled, and the Owner can still sign in from the server PC.
7. [ ] **F-M03:** A credential reset of a money-role account requires an Owner. Admin resets force a change at next sign-in.
8. [ ] **F-M04:** The CA private key is exported to the safe and removed from `LocalMachine\My` (`certlm.msc` shows no "HCC Hotel CA" with a private key). Name Constraints are present, or the CA is used only inside the APK.
9. [ ] **F-M06:** Ops scripts are in `C:\Program Files\HCC\Operations` (admin-only). Scheduled tasks point there (`Get-ScheduledTask -TaskPath \HCC\ | % Actions`).
10. [ ] **F-M07:** Tokens are in Keystore-backed storage, and PIN sessions are checked against the device header on every request.
11. [ ] `appsettings.Production.json`: `TrustServerCertificate` is absent, Integrated Security is on, `BackupDirectory` is set, `CertificateThumbprint` is set, `AllowedHosts` is the server name/IP, and `LoopbackHttpPort` is `0` after first-time setup (F-L11). The service starts with `ASPNETCORE_ENVIRONMENT=Production`, which the validator enforces.
12. [ ] First-time setup is done immediately after the service install. `GET http://127.0.0.1:5080/api/v1/setup/status` shows `setupRequired:false`, and `/auth/bootstrap-owner` answers FORBIDDEN.
13. [ ] Restore drill passes (`Restore-HccDrill.ps1`), and a backup is verified readable only by the allowed accounts.
14. [ ] Regression: a route-inventory test fails on any new anonymous route (F-I02), and the full server suite passes (`dotnet test -maxcpucount:1`).
15. [ ] Low items are scheduled or accepted with an owner sign-off: F-L01 … F-L11. The web-client hosting/CSP decision (F-I06) is recorded.

## Route-by-route permission table

How to read this table:

- **Access:**
  - *Anonymous*: no session.
  - *Loopback only*: anonymous, but refused unless the TCP peer is 127.0.0.1 or ::1.
  - *Session*: inside `MapHccSecured()`/`RequireHccSession()`, so a valid cookie or bearer session is required (401 otherwise).
- **Permission checked by the handler:** the PERM ids that the Application method (and its private helpers) passes to `IAccessPolicy.RequireAsync`/`HasAsync`.
  - Where several ids are listed, the handler uses them as alternatives, scopes or step-ups. Examples: "Edit **or** View" for CRM reads, approval permission when over a limit, own-shift scope for cashiers.
  - A finer `scope`/`amount` check inside the handler is not shown.
- **Command routes** are all `POST /api/v1/commands/<Name>`. They need an `Idempotency-Key` header, except `QuoteStay`, where a missing key gets a server key.
- **Method:** derived by a script from `src\Hcc.Api\Endpoints\*.cs` and `src\Hcc.Application\**` at `245a709`, then checked by hand. The rows with manual notes are the 10 unauthenticated routes and the 5 deliberate session-only routes.

| # | Module | Method | Route (under /api/v1) | Access | Permission checked by the handler | Notes |
|---|---|---|---|---|---|---|
| 1 | Platform | GET | `/system/health` | Anonymous | none (deliberate) | Opens a SQL connection; returns DB name + schema version (F-L01) |
| 2 | Platform | GET | `/system/discovery` | Anonymous | none (deliberate) | Product, server name, version; no secrets |
| 3 | Access | POST | `/auth/sign-in` | Anonymous | none (deliberate) | Lockout per credential; no per-client throttle (F-M02) |
| 4 | Access | POST | `/auth/sign-out` | Anonymous | none (deliberate) | Revokes the presented token only |
| 5 | Access | POST | `/auth/bootstrap-owner` | Loopback only | none; refused once any user exists | IPAddress.IsLoopback(remote) |
| 6 | Access | GET | `/me/capabilities` | Session | session only (deliberate: own grants and masks) |  |
| 7 | Access | GET | `/admin/users` | Session | PERM-ADM-001 |  |
| 8 | Access | GET | `/admin/devices` | Session | PERM-ADM-001 |  |
| 9 | Access | GET | `/admin/roles` | Session | any mode of PERM-ADM-001 / 002 / 003 | RequireAnyAsync |
| 10 | Access | GET | `/admin/exceptions` | Session | any mode of PERM-ADM-001 / 002 | RequireAnyAsync |
| 11 | Access | GET | `/admin/audit` | Session | PERM-ADM-004 |  |
| 12 | Access | POST | `/commands/CreateUser` | Session | PERM-ADM-001 |  |
| 13 | Access | POST | `/commands/SetUserEnabled` | Session | PERM-ADM-001 |  |
| 14 | Access | POST | `/commands/AssignRole` | Session | PERM-ADM-001 |  |
| 15 | Access | POST | `/commands/RevokeRole` | Session | PERM-ADM-001 |  |
| 16 | Access | POST | `/commands/SetCredential` | Session | PERM-ADM-001 |  |
| 17 | Access | POST | `/commands/ChangeOwnCredential` | Session | session + current password/PIN (counts toward lockout) | Own account only |
| 18 | Access | POST | `/commands/GrantException` | Session | PERM-ADM-002 |  |
| 19 | Access | POST | `/commands/RevokeException` | Session | PERM-ADM-002 |  |
| 20 | Access | POST | `/commands/RegisterDevice` | Session | PERM-ADM-001 |  |
| 21 | Access | POST | `/commands/RevokeDevice` | Session | PERM-ADM-001 |  |
| 22 | Accounting | POST | `/commands/LockAccountingPeriod` | Session | PERM-ACC-002 |  |
| 23 | Accounting | POST | `/commands/UnlockAccountingPeriod` | Session | PERM-ACC-003 |  |
| 24 | Accounting | POST | `/commands/SetLedgerMapping` | Session | PERM-ACC-004 |  |
| 25 | Accounting | POST | `/commands/ResetLedgerMapping` | Session | PERM-ACC-004 |  |
| 26 | Accounting | GET | `/accounting/days/{date}` | Session | PERM-ACC-001 |  |
| 27 | Accounting | GET | `/accounting/days` | Session | PERM-ACC-001 |  |
| 28 | Accounting | GET | `/accounting/trial-balance` | Session | PERM-ACC-001 |  |
| 29 | Accounting | GET | `/accounting/mapping` | Session | PERM-ACC-001 |  |
| 30 | Accounting | GET | `/accounting/mapping/history` | Session | PERM-ACC-001 |  |
| 31 | Accounting | GET | `/accounting/chart` | Session | PERM-ACC-001 |  |
| 32 | Accounting | GET | `/accounting/locks/history` | Session | PERM-ACC-001 |  |
| 33 | Accounting | GET | `/accounting/export/csv` | Session | PERM-ACC-005 |  |
| 34 | Accounting | GET | `/accounting/export/tally` | Session | PERM-ACC-006 |  |
| 35 | Billing | POST | `/commands/PostRoomNight` | Session | PERM-CLS-002 |  |
| 36 | Billing | POST | `/commands/PostCharge` | Session | PERM-FOL-002 |  |
| 37 | Billing | POST | `/commands/RecordReceipt` | Session | own-shift scope, PERM-PAY-001, PERM-PAY-002 |  |
| 38 | Billing | POST | `/commands/RequestAdjustment` | Session | PERM-FOL-003, PERM-FOL-005, PERM-PAY-003, PERM-STY-003 |  |
| 39 | Billing | POST | `/commands/DecideApproval` | Session | PERM-APR-001, PERM-PAY-005 |  |
| 40 | Billing | POST | `/commands/ExecuteAdjustment` | Session | own-shift scope, PERM-PAY-004, PERM-STY-003 |  |
| 41 | Billing | GET | `/folios/{folioId:long}` | Session | PERM-FOL-001 |  |
| 42 | Billing | GET | `/folios/by-reservation/{reservationId:long}` | Session | PERM-FOL-001 |  |
| 43 | Closing | POST | `/commands/RunBusinessDayClose` | Session | PERM-CLS-002 |  |
| 44 | Closing | POST | `/commands/PostRoomNights` | Session | PERM-CLS-002 |  |
| 45 | Closing | POST | `/commands/LockCashBookDay` | Session | PERM-CLS-003 |  |
| 46 | Closing | POST | `/commands/ReopenCashBookDay` | Session | PERM-CLS-004 |  |
| 47 | Closing | GET | `/closing/preflight` | Session | PERM-CLS-001 |  |
| 48 | Closing | GET | `/closing/business-date` | Session | session only (deliberate) | Business date + closed flag |
| 49 | Closing | GET | `/closing/records` | Session | PERM-RPT-002 |  |
| 50 | Closing | GET | `/closing/runs/{date}` | Session | PERM-CLS-001 |  |
| 51 | Closing | GET | `/closing/snapshots/{date}` | Session | PERM-RPT-002 |  |
| 52 | Closing | GET | `/cashbook/{date}` | Session | PERM-RPT-002 |  |
| 53 | Closing | GET | `/cashbook` | Session | PERM-RPT-002 |  |
| 54 | Closing | GET | `/cashbook/{date}/book` | Session | PERM-RPT-002 |  |
| 55 | Closing | GET | `/reports/daily` | Session | PERM-RPT-002 |  |
| 56 | Compliance | POST | `/commands/MarkComplianceFiled` | Session | PERM-CMP-001 |  |
| 57 | Compliance | POST | `/commands/MarkComplianceNotApplicable` | Session | PERM-CMP-001 |  |
| 58 | Compliance | POST | `/commands/ReopenComplianceFiling` | Session | PERM-CMP-002 |  |
| 59 | Compliance | POST | `/commands/SaveComplianceObligation` | Session | PERM-CMP-002 |  |
| 60 | Compliance | POST | `/commands/ToggleComplianceObligation` | Session | PERM-CMP-002 |  |
| 61 | Compliance | POST | `/commands/SaveComplianceSettings` | Session | PERM-CMP-002 |  |
| 62 | Compliance | POST | `/commands/GenerateCaPack` | Session | PERM-CMP-001 |  |
| 63 | Compliance | POST | `/commands/SeedComplianceDefaults` | Session | PERM-CMP-002 |  |
| 64 | Compliance | GET | `/compliance/calendar` | Session | PERM-CMP-001 |  |
| 65 | Compliance | GET | `/compliance/due-this-week` | Session | PERM-CMP-001 |  |
| 66 | Compliance | GET | `/compliance/badge` | Session | PERM-CMP-001 |  |
| 67 | Compliance | GET | `/compliance/obligations` | Session | PERM-CMP-001 |  |
| 68 | Compliance | GET | `/compliance/ca-packs` | Session | PERM-CMP-001 |  |
| 69 | Configuration | POST | `/commands/SetSetting` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 70 | Configuration | POST | `/commands/DecideSettingRequest` | Session | PERM-APR-001 |  |
| 71 | Configuration | POST | `/commands/WithdrawScheduledSetting` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 72 | Configuration | POST | `/commands/RevertSetting` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 73 | Configuration | POST | `/commands/ImportConfiguration` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 74 | Configuration | GET | `/configuration/groups` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 75 | Configuration | GET | `/configuration/groups/{groupId}` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 76 | Configuration | GET | `/configuration/search` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 77 | Configuration | GET | `/configuration/history` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 78 | Configuration | GET | `/configuration/requests` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 79 | Configuration | GET | `/configuration/modules` | Session | session only (deliberate) | Module on/off switches |
| 80 | Configuration | GET | `/configuration/values` | Session | session only (deliberate) | Secrets, fixed and Owner-only values excluded |
| 81 | Configuration | GET | `/configuration/export` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 82 | Configuration | POST | `/configuration/import-plan` | Session | PERM-ADM-003 (A = full, V = non-money groups; resolved by ConfigAuthorityResolver) |  |
| 83 | Customers | POST | `/commands/ProposeCustomerMerge` | Session | PERM-CRM-001 |  |
| 84 | Customers | POST | `/commands/DecideCustomerMerge` | Session | PERM-CRM-002 |  |
| 85 | Customers | POST | `/commands/AddCustomerTag` | Session | PERM-GST-002 |  |
| 86 | Customers | POST | `/commands/RemoveCustomerTag` | Session | PERM-GST-002 |  |
| 87 | Customers | POST | `/commands/AddCustomerNote` | Session | PERM-GST-002 |  |
| 88 | Customers | POST | `/commands/SetCustomerPreferences` | Session | PERM-GST-002 |  |
| 89 | Customers | POST | `/commands/SetCustomerConsent` | Session | PERM-GST-002 |  |
| 90 | Customers | GET | `/customers` | Session | PERM-FOL-001, PERM-GST-001, PERM-GST-002 |  |
| 91 | Customers | GET | `/customers/profile` | Session | PERM-FOL-001, PERM-GST-001, PERM-GST-002 |  |
| 92 | Customers | GET | `/customers/duplicates` | Session | PERM-CRM-001, PERM-CRM-002, PERM-FOL-001, PERM-GST-002 |  |
| 93 | Customers | GET | `/customers/merges` | Session | PERM-CRM-001, PERM-CRM-002 |  |
| 94 | Documents | POST | `/commands/CaptureIdDocument` | Session | PERM-DOC-001 |  |
| 95 | Documents | POST | `/commands/ConfirmIdCapture` | Session | PERM-DOC-001, PERM-GST-003 |  |
| 96 | Documents | POST | `/commands/RejectIdCapture` | Session | PERM-DOC-001 |  |
| 97 | Documents | POST | `/commands/ViewIdCaptureImage` | Session | PERM-GST-003 |  |
| 98 | Documents | POST | `/commands/RevealIdCaptureNumber` | Session | PERM-GST-003 |  |
| 99 | Documents | POST | `/commands/PurgeIdCaptures` | Session | PERM-DOC-002 |  |
| 100 | Documents | GET | `/documents/id-captures` | Session | PERM-DOC-001 |  |
| 101 | Documents | GET | `/documents/id-captures/{id:long}` | Session | PERM-DOC-001 |  |
| 102 | Expenses | POST | `/commands/SubmitExpense` | Session | PERM-EXP-001 |  |
| 103 | Expenses | POST | `/commands/SubmitExpenseDraft` | Session | PERM-EXP-001 |  |
| 104 | Expenses | POST | `/commands/DecideExpense` | Session | PERM-EXP-002, PERM-EXP-003 |  |
| 105 | Expenses | POST | `/commands/ReverseExpense` | Session | PERM-EXP-003 |  |
| 106 | Expenses | GET | `/expenses` | Session | PERM-EXP-001, PERM-EXP-002, PERM-EXP-003 |  |
| 107 | Expenses | GET | `/expenses/summary` | Session | PERM-EXP-002 |  |
| 108 | Expenses | GET | `/expenses/options` | Session | PERM-EXP-001, PERM-EXP-002, PERM-EXP-003 |  |
| 109 | Expenses | GET | `/expenses/{expenseId:long}` | Session | PERM-EXP-001, PERM-EXP-002, PERM-EXP-003 |  |
| 110 | Groups | POST | `/commands/CreateGroup` | Session | PERM-GRP-002 |  |
| 111 | Groups | POST | `/commands/UpdateGroup` | Session | PERM-GRP-002 |  |
| 112 | Groups | POST | `/commands/SetGroupRouting` | Session | PERM-GRP-002 |  |
| 113 | Groups | POST | `/commands/AttachGroupMember` | Session | PERM-GRP-002 |  |
| 114 | Groups | POST | `/commands/DetachGroupMember` | Session | PERM-GRP-002 |  |
| 115 | Groups | POST | `/commands/ApplyGroupRouting` | Session | PERM-GRP-002 |  |
| 116 | Groups | POST | `/commands/RerouteGroupCharge` | Session | PERM-GRP-002 |  |
| 117 | Groups | POST | `/commands/TransferGroupMember` | Session | PERM-GRP-002 |  |
| 118 | Groups | POST | `/commands/RecordGroupReceipt` | Session | PERM-GRP-003, PERM-GRP-005 |  |
| 119 | Groups | POST | `/commands/RequestGroupAttrition` | Session | PERM-GRP-002 |  |
| 120 | Groups | POST | `/commands/DecideGroupAttrition` | Session | PERM-GRP-003 |  |
| 121 | Groups | POST | `/commands/SettleGroup` | Session | PERM-GRP-002 |  |
| 122 | Groups | GET | `/groups` | Session | PERM-GRP-001 |  |
| 123 | Groups | GET | `/groups/{groupId:long}/statement` | Session | PERM-GRP-004 |  |
| 124 | GuestId | POST | `/commands/RevealGuestId` | Session | PERM-GST-003 |  |
| 125 | GuestId | POST | `/commands/RekeyGuestIds` | Session | PERM-ADM-003 |  |
| 126 | GuestServices | POST | `/commands/LogServiceRequest` | Session | PERM-GST-002, PERM-HK-003, PERM-ROOM-001 |  |
| 127 | GuestServices | POST | `/commands/AssignServiceRequest` | Session | PERM-GST-002, PERM-HK-002, PERM-HK-003, PERM-ROOM-001 |  |
| 128 | GuestServices | POST | `/commands/StartServiceRequest` | Session | PERM-GST-002, PERM-HK-003, PERM-ROOM-001 |  |
| 129 | GuestServices | POST | `/commands/CompleteServiceRequest` | Session | PERM-GST-002, PERM-HK-003, PERM-ROOM-001 |  |
| 130 | GuestServices | POST | `/commands/CancelServiceRequest` | Session | PERM-GST-002, PERM-HK-002 |  |
| 131 | GuestServices | POST | `/commands/LogComplaint` | Session | PERM-GST-002 |  |
| 132 | GuestServices | POST | `/commands/UpdateComplaint` | Session | PERM-GST-002 |  |
| 133 | GuestServices | POST | `/commands/ResolveComplaint` | Session | PERM-GST-002 |  |
| 134 | GuestServices | POST | `/commands/RecordComplaintFollowUp` | Session | PERM-APR-001, PERM-GST-002 |  |
| 135 | GuestServices | POST | `/commands/OfferRecoveryDiscount` | Session | PERM-GST-002 |  |
| 136 | GuestServices | POST | `/commands/LogLostItem` | Session | PERM-GST-002, PERM-HK-003, PERM-ROOM-001 |  |
| 137 | GuestServices | POST | `/commands/ClaimLostItem` | Session | PERM-GST-002 |  |
| 138 | GuestServices | POST | `/commands/HandOverLostItem` | Session | PERM-GST-002 |  |
| 139 | GuestServices | POST | `/commands/DisposeLostItem` | Session | PERM-APR-001, PERM-GST-002 |  |
| 140 | GuestServices | POST | `/commands/SaveGuestServiceSettings` | Session | PERM-APR-001 |  |
| 141 | GuestServices | GET | `/guest-services/requests` | Session | PERM-GST-002, PERM-HK-002, PERM-HK-003, PERM-ROOM-001 |  |
| 142 | GuestServices | GET | `/guest-services/requests/{id:long}` | Session | PERM-GST-002, PERM-HK-002 |  |
| 143 | GuestServices | GET | `/guest-services/complaints` | Session | PERM-GST-002 |  |
| 144 | GuestServices | GET | `/guest-services/complaints/{id:long}` | Session | PERM-GST-002 |  |
| 145 | GuestServices | GET | `/guest-services/lost-items` | Session | PERM-GST-002, PERM-HK-003, PERM-ROOM-001 |  |
| 146 | GuestServices | GET | `/guest-services/settings` | Session | PERM-APR-001, PERM-GST-002 |  |
| 147 | GuestServices | GET | `/guest-services/history/{kind}/{id:long}` | Session | PERM-GST-002 |  |
| 148 | GuestServices | GET | `/guest-services/histories/{kind}` | Session | PERM-GST-002 |  |
| 149 | GuestServices | GET | `/guest-services/stays` | Session | PERM-GST-002 |  |
| 150 | GuestServices | GET | `/guest-services/reports/response` | Session | PERM-GST-002, PERM-RPT-001 |  |
| 151 | GuestServices | GET | `/guest-services/reports/complaints` | Session | PERM-GST-002, PERM-RPT-001 |  |
| 152 | GuestServices | GET | `/guest-services/reports/lost-found` | Session | PERM-GST-002, PERM-RPT-001 |  |
| 153 | Handover | POST | `/commands/SaveHandoverTemplate` | Session | PERM-APR-001 |  |
| 154 | Handover | POST | `/commands/ToggleHandoverTemplate` | Session | PERM-APR-001 |  |
| 155 | Handover | POST | `/commands/TickChecklistItem` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 156 | Handover | POST | `/commands/SubmitChecklistRun` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 157 | Handover | POST | `/commands/ReopenChecklistRun` | Session | PERM-APR-001 |  |
| 158 | Handover | POST | `/commands/SubmitHandoverNote` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 159 | Handover | POST | `/commands/AcknowledgeHandoverNote` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 160 | Handover | POST | `/commands/CompleteCarryItem` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 161 | Handover | GET | `/handover/checklists` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 162 | Handover | GET | `/handover/notes` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 163 | Handover | GET | `/handover/carry` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 164 | Handover | GET | `/handover/templates` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 165 | Handover | GET | `/handover/dashboard` | Session | any of PERM-HK-001 / PERM-PAY-001 / PERM-APR-001, PERM-APR-001, PERM-PAY-001, PERM-RPT-002 |  |
| 166 | Housekeeping | POST | `/commands/TransitionHousekeepingTask` | Session | task work / repair work, PERM-HK-001, PERM-HK-002, PERM-HK-004 |  |
| 167 | Housekeeping | POST | `/commands/BlockRoom` | Session | PERM-ROOM-002 |  |
| 168 | Housekeeping | POST | `/commands/UnblockRoom` | Session | PERM-ROOM-002 |  |
| 169 | Housekeeping | POST | `/commands/RequestRepair` | Session | PERM-ROOM-001 |  |
| 170 | Housekeeping | POST | `/commands/GenerateStayoverTasks` | Session | PERM-HK-002 |  |
| 171 | Housekeeping | GET | `/rooms/board` | Session | PERM-HK-001 |  |
| 172 | Housekeeping | GET | `/housekeeping/tasks` | Session | PERM-HK-001 |  |
| 173 | Import | POST | `/commands/UploadImportBatch` | Session | PERM-IMP-001 |  |
| 174 | Import | POST | `/commands/CommitImportBatch` | Session | commit permission per master (Owner-only masters), PERM-IMP-001 |  |
| 175 | Import | GET | `/import/masters` | Session | PERM-IMP-001 |  |
| 176 | Import | GET | `/import/batches` | Session | PERM-IMP-001 |  |
| 177 | Import | GET | `/import/batches/{batchId:long}` | Session | PERM-IMP-001 |  |
| 178 | Import | GET | `/import/opening-balances` | Session | PERM-IMP-001 |  |
| 179 | Import | GET | `/import/ledgers` | Session | PERM-IMP-001 |  |
| 180 | Inventory | GET | `/availability` | Session | PERM-RES-001 |  |
| 181 | Inventory | GET | `/room-blocks/history` | Session | PERM-RES-001 |  |
| 182 | Inventory | GET | `/rooms/status` | Session | PERM-RES-001 |  |
| 183 | Inventory | POST | `/commands/QuoteStay` | Session | PERM-RES-002 |  |
| 184 | Inventory | POST | `/commands/CreateGroupBlock` | Session | PERM-RES-002 |  |
| 185 | Inventory | POST | `/commands/ReleaseGroupBlock` | Session | PERM-RES-002 |  |
| 186 | Invoices | POST | `/commands/IssueInvoice` | Session | PERM-INV-002 |  |
| 187 | Invoices | POST | `/commands/RequestCreditNote` | Session | PERM-INV-003, PERM-INV-005 |  |
| 188 | Invoices | POST | `/commands/DecideCreditNote` | Session | PERM-INV-003 |  |
| 189 | Invoices | GET | `/invoices` | Session | PERM-INV-001 |  |
| 190 | Invoices | GET | `/invoices/gstr1` | Session | PERM-INV-001 |  |
| 191 | Invoices | GET | `/invoices/by-source/{sourceKind}/{sourceId:long}` | Session | PERM-INV-001 |  |
| 192 | Invoices | GET | `/invoices/{invoiceId:long}` | Session | PERM-INV-001 |  |
| 193 | Invoices | GET | `/invoices/{invoiceId:long}/document` | Session | PERM-INV-002 |  |
| 194 | Invoices | GET | `/credit-notes/{creditNoteId:long}` | Session | PERM-INV-001 |  |
| 195 | Invoices | GET | `/credit-notes/{creditNoteId:long}/document` | Session | PERM-INV-002 |  |
| 196 | Invoices | GET | `/credit-note-requests` | Session | PERM-INV-001 |  |
| 197 | Maintenance | POST | `/commands/CreateWorkOrder` | Session | PERM-HK-001, PERM-HK-003, PERM-HK-004 |  |
| 198 | Maintenance | POST | `/commands/ConvertRepairToWorkOrder` | Session | PERM-HK-001, PERM-HK-004, PERM-ROOM-001 |  |
| 199 | Maintenance | POST | `/commands/AssignWorkOrder` | Session | PERM-HK-002 |  |
| 200 | Maintenance | POST | `/commands/StartWorkOrder` | Session | PERM-HK-001, PERM-HK-004, PERM-ROOM-001 |  |
| 201 | Maintenance | POST | `/commands/HoldWorkOrder` | Session | PERM-HK-001, PERM-HK-004, PERM-ROOM-001 |  |
| 202 | Maintenance | POST | `/commands/AddWorkOrderPart` | Session | PERM-HK-001, PERM-HK-004, PERM-ROOM-001 |  |
| 203 | Maintenance | POST | `/commands/CompleteWorkOrder` | Session | PERM-HK-001, PERM-HK-004, PERM-ROOM-001 |  |
| 204 | Maintenance | POST | `/commands/VerifyWorkOrder` | Session | PERM-HK-004, PERM-ROOM-002 |  |
| 205 | Maintenance | POST | `/commands/CancelWorkOrder` | Session | PERM-HK-004 |  |
| 206 | Maintenance | POST | `/commands/BlockWorkOrderRoom` | Session | PERM-HK-004, PERM-ROOM-002 |  |
| 207 | Maintenance | POST | `/commands/GeneratePmWorkOrders` | Session | PERM-HK-004 |  |
| 208 | Maintenance | POST | `/commands/SavePmPlan` | Session | PERM-HK-004 |  |
| 209 | Maintenance | POST | `/commands/SaveMaintenanceAsset` | Session | PERM-HK-004 |  |
| 210 | Maintenance | POST | `/commands/SaveMaintenanceVendor` | Session | PERM-HK-004 |  |
| 211 | Maintenance | POST | `/commands/SaveAmc` | Session | PERM-HK-004, PERM-RPT-002 |  |
| 212 | Maintenance | POST | `/commands/ReceiveSpare` | Session | PERM-HK-004 |  |
| 213 | Maintenance | GET | `/maintenance/work-orders` | Session | PERM-HK-001, PERM-HK-003, PERM-HK-004, PERM-RPT-002 |  |
| 214 | Maintenance | GET | `/maintenance/work-orders/{id:long}` | Session | PERM-HK-001, PERM-HK-003, PERM-HK-004, PERM-RPT-002 |  |
| 215 | Maintenance | GET | `/maintenance/registers` | Session | PERM-HK-001, PERM-HK-004, PERM-RPT-002 |  |
| 216 | Maintenance | GET | `/maintenance/amcs` | Session | PERM-HK-004, PERM-RPT-002 |  |
| 217 | Maintenance | GET | `/maintenance/report` | Session | PERM-HK-004, PERM-RPT-002 |  |
| 218 | Messaging | POST | `/commands/SendMessage` | Session | PERM-RES-002 |  |
| 219 | Messaging | POST | `/commands/RunMessagingTriggers` | Session | PERM-RES-002 |  |
| 220 | Messaging | POST | `/commands/SimulateMessageDelivery` | Session | PERM-RES-002 |  |
| 221 | Messaging | POST | `/commands/RetryMessage` | Session | PERM-RES-002 |  |
| 222 | Messaging | POST | `/commands/SetMessagingConsent` | Session | PERM-RES-002, PERM-STY-001 |  |
| 223 | Messaging | POST | `/commands/UpdateMessageTemplate` | Session | PERM-APR-001 |  |
| 224 | Messaging | POST | `/commands/UpdateMessageRule` | Session | PERM-APR-001 |  |
| 225 | Messaging | GET | `/messaging/messages` | Session | PERM-RES-001 |  |
| 226 | Messaging | GET | `/messaging/messages/{id:long}` | Session | PERM-RES-001 |  |
| 227 | Messaging | GET | `/messaging/summary` | Session | PERM-RES-001 |  |
| 228 | Messaging | GET | `/messaging/templates` | Session | PERM-RES-001 |  |
| 229 | Messaging | GET | `/messaging/rules` | Session | PERM-RES-001 |  |
| 230 | Messaging | GET | `/messaging/consents` | Session | PERM-RES-001 |  |
| 231 | Messaging | GET | `/messaging/consent/{guestId:long}` | Session | PERM-RES-001 |  |
| 232 | Ota | POST | `/commands/SaveChannel` | Session | PERM-OTA-002 |  |
| 233 | Ota | POST | `/commands/SetChannelStatus` | Session | PERM-OTA-002 |  |
| 234 | Ota | POST | `/commands/SetChannelStopSell` | Session | PERM-OTA-001 |  |
| 235 | Ota | POST | `/commands/PushChannelRates` | Session | PERM-OTA-001 |  |
| 236 | Ota | POST | `/commands/ImportChannelMessage` | Session | PERM-OTA-001 |  |
| 237 | Ota | POST | `/commands/QueueSimulatedMessage` | Session | PERM-OTA-002 |  |
| 238 | Ota | POST | `/commands/PullChannelMessages` | Session | PERM-OTA-001 |  |
| 239 | Ota | POST | `/commands/AcceptChannelMessage` | Session | PERM-OTA-001 |  |
| 240 | Ota | POST | `/commands/RejectChannelMessage` | Session | PERM-OTA-001 |  |
| 241 | Ota | GET | `/ota/channels` | Session | PERM-OTA-001 |  |
| 242 | Ota | GET | `/ota/messages` | Session | PERM-OTA-001 |  |
| 243 | Ota | GET | `/ota/messages/{id:long}/check` | Session | PERM-OTA-001 |  |
| 244 | Ota | GET | `/ota/channels/{id:int}/push-preview` | Session | PERM-OTA-001 |  |
| 245 | Ota | GET | `/ota/pushes` | Session | PERM-OTA-001 |  |
| 246 | Ota | GET | `/ota/commission` | Session | PERM-OTA-003 |  |
| 247 | Ota | GET | `/ota/reconciliation` | Session | PERM-OTA-003 |  |
| 248 | Payments | POST | `/commands/CreateTill` | Session | PERM-ADM-003 |  |
| 249 | Payments | POST | `/commands/OpenShift` | Session | PERM-SHF-001 |  |
| 250 | Payments | POST | `/commands/SubmitShiftCount` | Session | own-shift scope, PERM-SHF-001 |  |
| 251 | Payments | GET | `/shifts/{shiftId:long}` | Session | own-shift scope, PERM-SHF-001 |  |
| 252 | Payments | GET | `/cashier/collections` | Session | PERM-RPT-002 |  |
| 253 | Payments | GET | `/cashier/desk` | Session | PERM-SHF-001 |  |
| 254 | Payroll | POST | `/commands/SaveDepartment` | Session | PERM-HR-002, PERM-HR-009 |  |
| 255 | Payroll | POST | `/commands/SaveStaff` | Session | PERM-HR-002, PERM-HR-005, PERM-HR-009 |  |
| 256 | Payroll | POST | `/commands/SetStaffActive` | Session | PERM-HR-002 |  |
| 257 | Payroll | POST | `/commands/SetRoster` | Session | PERM-HR-002 |  |
| 258 | Payroll | POST | `/commands/CopyRosterWeek` | Session | PERM-HR-002 |  |
| 259 | Payroll | POST | `/commands/MarkAttendance` | Session | PERM-HR-002, PERM-HR-004 |  |
| 260 | Payroll | POST | `/commands/RequestLeave` | Session | PERM-HR-002, PERM-HR-004 |  |
| 261 | Payroll | POST | `/commands/DecideLeave` | Session | PERM-HR-003 |  |
| 262 | Payroll | POST | `/commands/CancelLeave` | Session | PERM-HR-002, PERM-HR-004 |  |
| 263 | Payroll | POST | `/commands/SetLeavePolicy` | Session | PERM-HR-009 |  |
| 264 | Payroll | POST | `/commands/GiveAdvance` | Session | PERM-HR-007 |  |
| 265 | Payroll | POST | `/commands/SetStatutoryRates` | Session | PERM-HR-009 |  |
| 266 | Payroll | POST | `/commands/PreparePayRun` | Session | PERM-HR-006 |  |
| 267 | Payroll | POST | `/commands/LockPayRun` | Session | PERM-HR-006 |  |
| 268 | Payroll | POST | `/commands/UnlockPayRun` | Session | PERM-HR-009 |  |
| 269 | Payroll | POST | `/commands/IssueBankFile` | Session | PERM-HR-006 |  |
| 270 | Payroll | POST | `/commands/SendPayslip` | Session | PERM-HR-006 |  |
| 271 | Payroll | POST | `/commands/PrepareFnf` | Session | PERM-HR-008 |  |
| 272 | Payroll | POST | `/commands/ConfirmFnf` | Session | PERM-HR-008 |  |
| 273 | Payroll | POST | `/commands/DiscardFnf` | Session | PERM-HR-008 |  |
| 274 | Payroll | GET | `/payroll/staff` | Session | PERM-HR-001, PERM-HR-004, PERM-HR-005 |  |
| 275 | Payroll | GET | `/payroll/staff/{staffId:long}/leave-balance` | Session | PERM-HR-001 |  |
| 276 | Payroll | GET | `/payroll/rates/{month}` | Session | PERM-HR-006 |  |
| 277 | Payroll | GET | `/payroll/runs/{month}` | Session | PERM-HR-006 |  |
| 278 | Payroll | GET | `/payroll/runs/{month}/register.csv` | Session | PERM-HR-006 |  |
| 279 | Payroll | GET | `/payroll/runs/{month}/statutory.csv` | Session | PERM-HR-006 |  |
| 280 | Payroll | GET | `/payroll/run/{runId:long}/bank-files/{issueNo:int}` | Session | PERM-HR-006 |  |
| 281 | Payroll | GET | `/payroll/payslips/{runId:long}/{staffId:long}` | Session | PERM-HR-004, PERM-HR-006 |  |
| 282 | Payroll | GET | `/payroll/board` | Session | PERM-HR-001, PERM-HR-004, PERM-HR-006 |  |
| 283 | Payroll | GET | `/payroll/my/payslips` | Session | PERM-HR-004 |  |
| 284 | Pos | POST | `/commands/CreateOutlet` | Session | PERM-ADM-003 |  |
| 285 | Pos | POST | `/commands/SaveMenuItem` | Session | PERM-ADM-003 |  |
| 286 | Pos | POST | `/commands/SetMenuItemAvailability` | Session | PERM-FOL-002 |  |
| 287 | Pos | POST | `/commands/OpenCheck` | Session | PERM-FOL-002 |  |
| 288 | Pos | POST | `/commands/AddCheckItem` | Session | PERM-FOL-002 |  |
| 289 | Pos | POST | `/commands/SetCheckItemQty` | Session | PERM-FOL-002 |  |
| 290 | Pos | POST | `/commands/SendKot` | Session | PERM-FOL-002 |  |
| 291 | Pos | POST | `/commands/AdvanceKot` | Session | PERM-FOL-002 |  |
| 292 | Pos | POST | `/commands/SettleCheck` | Session | PERM-FOL-002, PERM-PAY-001, own-shift scope |  |
| 293 | Pos | POST | `/commands/VoidCheckItem` | Session | PERM-FOL-002 |  |
| 294 | Pos | POST | `/commands/VoidBill` | Session | PERM-FOL-002, PERM-PAY-001 |  |
| 295 | Pos | POST | `/commands/DecideVoid` | Session | PERM-APR-001 |  |
| 296 | Pos | GET | `/pos/checks/{orderId:long}` | Session | PERM-FOL-002 |  |
| 297 | Pos | GET | `/pos/checks` | Session | PERM-FOL-002 |  |
| 298 | Pos | GET | `/pos/outlets` | Session | PERM-FOL-002 |  |
| 299 | Pos | GET | `/pos/menu` | Session | PERM-FOL-002 |  |
| 300 | Pos | GET | `/pos/kitchen` | Session | PERM-FOL-002 |  |
| 301 | Pos | GET | `/pos/day-summary` | Session | PERM-FOL-002 |  |
| 302 | Pos | GET | `/pos/checks/closed` | Session | PERM-FOL-002 |  |
| 303 | Pos | GET | `/pos/void-requests` | Session | PERM-FOL-002 |  |
| 304 | Pos | GET | `/pos/in-house` | Session | PERM-FOL-002 |  |
| 305 | Pos | GET | `/pos/till` | Session | PERM-FOL-002 |  |
| 306 | Pos | GET | `/pos/close-blockers` | Session | PERM-FOL-002 |  |
| 307 | Reservations | POST | `/commands/CreateReservation` | Session | PERM-APR-001, PERM-RES-002 |  |
| 308 | Reservations | POST | `/commands/AmendReservation` | Session | PERM-APR-001, PERM-RES-003 |  |
| 309 | Reservations | POST | `/commands/CancelReservation` | Session | PERM-RES-004 |  |
| 310 | Reservations | POST | `/commands/MarkNoShow` | Session | PERM-RES-004 |  |
| 311 | Reservations | POST | `/commands/ConfirmHold` | Session | PERM-RES-002 |  |
| 312 | Reservations | GET | `/reservations` | Session | PERM-RES-001 |  |
| 313 | Reservations | GET | `/reservations/{id:long}` | Session | PERM-RES-001 |  |
| 314 | Setup | GET | `/setup/status` | Loopback only | none | SetupEndpoints.IsServerPc |
| 315 | Setup | POST | `/setup/SetupProperty` | Loopback only | none; refused once a property exists | SetupEndpoints.IsServerPc |
| 316 | Stays | POST | `/commands/RegisterGuest` | Session | PERM-GST-002 |  |
| 317 | Stays | POST | `/commands/CheckIn` | Session | PERM-STY-001 |  |
| 318 | Stays | POST | `/commands/CheckoutStay` | Session | PERM-STY-003 |  |
| 319 | Stays | POST | `/commands/MoveRoom` | Session | PERM-APR-001, PERM-STY-002 |  |
| 320 | Stays | POST | `/commands/ExtendStay` | Session | PERM-APR-001, PERM-STY-002 |  |
| 321 | Stays | POST | `/commands/ShortenStay` | Session | PERM-STY-002 |  |
| 322 | Stays | POST | `/commands/EarlyDeparture` | Session | PERM-STY-002 |  |
| 323 | Stays | POST | `/commands/SaveGuestProfile` | Session | PERM-GST-002 |  |
| 324 | Stays | GET | `/guests` | Session | PERM-GST-001, PERM-GST-002, PERM-GST-003 |  |
| 325 | Stays | GET | `/guests/{guestId:long}` | Session | PERM-GST-001, PERM-GST-002, PERM-GST-003 |  |
| 326 | Stock | POST | `/commands/StockReceiveGrn` | Session | PERM-HK-003, PERM-HK-004, PERM-RPT-002 |  |
| 327 | Stock | POST | `/commands/StockIssue` | Session | PERM-HK-003 |  |
| 328 | Stock | POST | `/commands/StockUse` | Session | PERM-HK-003 |  |
| 329 | Stock | POST | `/commands/StockRoomChange` | Session | PERM-HK-003 |  |
| 330 | Stock | POST | `/commands/StockLaundrySend` | Session | PERM-HK-003 |  |
| 331 | Stock | POST | `/commands/StockLaundryReceive` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 332 | Stock | POST | `/commands/StockRecordLoss` | Session | PERM-HK-003 |  |
| 333 | Stock | POST | `/commands/StockCountSubmit` | Session | PERM-HK-003 |  |
| 334 | Stock | POST | `/commands/StockCountDecide` | Session | PERM-HK-004 |  |
| 335 | Stock | POST | `/commands/StockItemCreate` | Session | PERM-HK-004, PERM-RPT-002 |  |
| 336 | Stock | POST | `/commands/StockItemSet` | Session | PERM-HK-004 |  |
| 337 | Stock | POST | `/commands/StockSettingsSet` | Session | PERM-HK-004 |  |
| 338 | Stock | GET | `/stock/items` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 339 | Stock | GET | `/stock/settings` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 340 | Stock | GET | `/stock/alerts` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 341 | Stock | GET | `/stock/laundry` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 342 | Stock | GET | `/stock/counts` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 343 | Stock | GET | `/stock/losses` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 344 | Stock | GET | `/stock/moves` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 345 | Stock | GET | `/stock/consumption` | Session | PERM-HK-003, PERM-RPT-002 |  |
| 346 | Tax | POST | `/commands/SetGstSetting` | Session | PERM-ADM-003 |  |
| 347 | Tax | POST | `/commands/WithdrawGstSetting` | Session | PERM-ADM-003 |  |
| 348 | Tax | GET | `/tax/gst` | Session | PERM-ADM-003, PERM-ADM-003:VIEW |  |
| 349 | WebBooking | GET | `/public/booking/quote` | Anonymous | none (public) | Off unless Hcc:WebBooking:Enabled; per-IP limit; no-store |
| 350 | WebBooking | POST | `/public/booking/requests` | Anonymous | none (public) | Enabled flag, per-IP limit, honeypot, Idempotency-Key, 3 open per phone |
| 351 | WebBooking | GET | `/public/booking/deposit/{token}` | Anonymous | none (256-bit link token) | Enabled flag, per-IP limit |
| 352 | WebBooking | POST | `/commands/ConfirmWebRequest` | Session | PERM-WEB-001 |  |
| 353 | WebBooking | POST | `/commands/DeclineWebRequest` | Session | PERM-WEB-001 |  |
| 354 | WebBooking | POST | `/commands/CreateWebDepositLink` | Session | PERM-WEB-001 |  |
| 355 | WebBooking | POST | `/commands/MarkWebDepositPaid` | Session | PERM-PAY-002, PERM-WEB-001 |  |
| 356 | WebBooking | GET | `/web-booking/requests` | Session | PERM-WEB-001 |  |
| 357 | WebBooking | GET | `/web-booking/requests/{id:long}` | Session | PERM-WEB-001 |  |
| 358 | Yield | POST | `/commands/SaveYieldPlan` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 359 | Yield | POST | `/commands/SaveYieldSeason` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 360 | Yield | POST | `/commands/SetYieldDayOfWeek` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 361 | Yield | POST | `/commands/SaveYieldOccupancyRule` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 362 | Yield | POST | `/commands/SetYieldGuardrails` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 363 | Yield | POST | `/commands/SaveYieldRestriction` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 364 | Yield | POST | `/commands/SetYieldOverride` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 365 | Yield | POST | `/commands/SetYieldForecast` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 366 | Yield | POST | `/commands/PublishYieldTariffs` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 367 | Yield | POST | `/commands/ProposeYieldPublish` | Session | PERM-ADM-003, PERM-APR-001, PERM-RPT-001 |  |
| 368 | Yield | POST | `/commands/DecideYieldProposal` | Session | PERM-ADM-003, PERM-RPT-001 |  |
| 369 | Yield | GET | `/yield/calendar` | Session | PERM-RPT-001 |  |
| 370 | Yield | GET | `/yield/config` | Session | PERM-RPT-001 |  |
| 371 | Yield | GET | `/yield/quote` | Session | PERM-RPT-001 |  |
| 372 | Yield | GET | `/yield/proposals` | Session | PERM-RPT-001 |  |
| 373 | Yield | GET | `/yield/publishes` | Session | PERM-RPT-001 |  |

