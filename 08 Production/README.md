# HCC production server (Wave 1 · P0 foundation + P1–P5 integrated)

.NET 10 ASP.NET Core + SQL Server 2025 Express (DEC-054). Branches `claude/wave1-p0` (foundation) → `claude/wave1-integration` (P1 access, P2 inventory & reservations, P3 stays & housekeeping, P4 billing & cashier, P5 web client boundary merged and wired); not merged into `main` until the owner says so. Specs: `02 Planning/HCC_Production_Contracts_and_Schema_v1.md` (§2 API, §4 schema, §7 projects), `HCC_Multi_Agent_Plan_v1.md` §4.

## Layout
| Path | Contents |
|---|---|
| `Hcc.slnx` | Solution (warnings as errors, nullable, deterministic; `Directory.Build.props`, central packages `Directory.Packages.props`, lock files) |
| `src/Hcc.Contracts` | Wire contracts: `HccErrorCode` + HTTP status/title catalogue, `ErrorResponse`, API constants. No dependencies. |
| `src/Hcc.Domain` | Rules, no I/O: `Money` (integer paise, half-away-from-zero bps rounding, folio totals, collections/drawer), `Dates` (stay dates, business day, H04 calendar date), `Pricing` (H02 LOS/occupancy), `HccException` |
| `src/Hcc.Application` | Command ports: `CommandContext`, `ICommandRunner`, `ICommandTransaction` (audit + outbox), canonical-JSON payload hash |
| `src/Hcc.Infrastructure.SqlServer` | Connection policy, migration runner + backup gate, idempotent `SqlCommandRunner`, `OutboxConsumer`, SQL value/error adapters |
| `src/Hcc.Api` | Host: `HccComposition` (platform + every module's DI and routes, shared by Program.cs and the end-to-end tests), error middleware, `migrate` command, `Endpoints/<Module>Endpoints.cs`. Every module route sits in `MapHccSecured()` (P1 session required; 401 `UNAUTHENTICATED` otherwise; actor from the session only) |
| `database/migrations` | One folder, block-numbered, applied in ascending order (gaps allowed, versions unique): `0001_platform`, `0002_core_inventory_billing_payments` (P0), `0100_access_identity` + `0101_access_rbac_seed` (P1, DEC-062/063), `0200_inventory_reservations` (P2), `0300_stays_housekeeping` (P3), `0400_billing_payments` (P4), `0500_closing_reporting` (P6), `0600_tax_gst_master` + `0601_guest_id_documents` (Wave 1 C). Each agent adds the next number in its own block (`0102`, `0201`, …). Never edit an applied migration. (The 0200–0400 header comments still say "database/modules" — left as written so the checksums stay as the agents shipped them.) |
| `tests/Hcc.Platform.Tests` | Kernel + golden + SQL integration tests (disposable databases), migration order + cross-module FK check |
| `tests/Hcc.{Access,Inventory,Stays,Billing}.Tests` | Module tests (P1–P4) |
| `tests/Hcc.Closing.Tests` | P6: daily-pack/closing rules (no database) and SQL with the real composition: S0 three-day walkthrough (drawer ₹10,725 → ₹12,425), crash-resume at every step, late-write barrier across P2–P4, concurrent closes, snapshot immutability, DEC-065 cash-book lock |
| `tests/Hcc.GstIds.Tests` | Wave 1 C: GST master golden rules + SQL (room 105 ₹4,265 incl. ₹265 GST, BASE drawer ₹10,725), guest ID encryption / masking / audited reveal / rotation / no-leak scans (DEC-068/069). Module notes: `src/Hcc.Application/Tax/README.md`, `src/Hcc.Application/GuestIds/README.md` (legal note) |
| `tests/Hcc.Integration.Tests` | End to end over HTTP with the real composition: sign-in (Owner password; Reception/Housekeeper PIN on a registered device) → book → register → check in → room nights → cash receipt on own shift → checkout → housekeeping task → inspect → Ready (room 105: ₹4,200 incl. ₹200 GST at the DEC-069 master's 5 %); company credit inside checkout with rollback |
| `tools/golden/export-golden.mjs` | Regenerates `tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json` from the prototype BASE fixture |
| `src/Hcc.Operations` | (P7) `ScriptBackupGate`: the pre-migration backup runs `Backup-HccDatabase.ps1` |
| `deployment/windows` | (P7) service install, LAN HTTPS certificate, backup/differential/restore-drill scripts + Task Scheduler, Inno Setup template, `INSTALL.md` (owner), `ANDROID_SERVER_ADDRESS.md`; `tests\Test-HccDeployment.ps1` (dry runs, also run by `Hcc.Operations.Tests`) |
| `third-party/` | `packages.json` (every NuGet package, version, licence), `source-reuse.json` (H02/H04 provenance) |
| `ETP_Reuse_Pending.md` | The `ETP-REUSE` stand-ins (P0–P4) to replace/align when `V:\ETP` is available |

## Commands (run in this folder)
```
dotnet build Hcc.slnx                              # must report 0 warnings, 0 errors
dotnet test Hcc.slnx                               # all tests; SQL tests need .\SQLEXPRESS (see below)
dotnet test Hcc.slnx --filter "Category!=Sql"      # kernel + golden tests only, no database
dotnet run --project src/Hcc.Api -- migrate        # apply migrations to the Hcc database in appsettings.json
dotnet run --project src/Hcc.Api                   # serve http://localhost:5000/api/v1/system/health (dev)
node tools/golden/export-golden.mjs                # Node 25 (E:\Tools\nodejs); needs 03 Prototype node_modules (esbuild)
```

## Production hosting (P7)
`Hcc.Api` runs as Windows service `HccServer` (`UseWindowsService`); in Production it refuses to start unless `appsettings.Production.json` is valid (integrated auth, no `TrustServerCertificate`, full backup folder, certificate thumbprint). LAN HTTPS on `Hcc:Hosting:HttpsPort` (5443) with a machine-store certificate; plain HTTP only on 127.0.0.1:5080 for first-time setup. **First-time setup (W3-b):** `POST /api/v1/setup/SetupProperty` (Idempotency-Key; loopback only; refused once any property exists) creates `inv.property` (state default Maharashtra, optional GSTIN, address — migration `1900_property_setup`), the first Owner (DEC-062 password) and seeds Master configuration `hotel`/`brand.address`/`brand.gstin`/`checkin`/`checkout` in one audited transaction; `GET /api/v1/setup/status`; script `deployment/windows/Initialize-HccProperty.ps1`. `/auth/bootstrap-owner` remains for an existing property without users. `GET /api/v1/system/discovery` (no auth) returns product/serverName/version. Owner steps: `deployment/windows/INSTALL.md`.

## SQL test harness
- Each SQL test class creates its own database `HccTest_<UTC yyyyMMddHHmmss>_<8 hex>` on `.\SQLEXPRESS` (override: env `HCC_TEST_SQL_SERVER`), applies the migrations and drops it afterwards (`SINGLE_USER WITH ROLLBACK IMMEDIATE`, only names matching that pattern). No other database (e.g. `EtpReporting`) is touched.
- Leftovers after a crashed run: `sqlcmd -S .\SQLEXPRESS -E -C -Q "SELECT name FROM sys.databases WHERE name LIKE 'HccTest[_]%'"`.
- **One-time permission:** the Windows login running the tests needs `dbcreator` on the instance. On the work PC (25 Oct 2026) `Workpc\Sagar` is a plain login (sysadmin only through elevated `BUILTIN\Administrators`), so the SQL tests fail with a clear message until the owner runs, from an **elevated** prompt:
  `sqlcmd -S .\SQLEXPRESS -E -C -Q "ALTER SERVER ROLE dbcreator ADD MEMBER [Workpc\Sagar];"`
  (or runs `dotnet test` from an elevated prompt). The backup-gate test backs up to `NUL` (nothing written; msdb backup history gets a row).
- Connection strings use `Encrypt=Mandatory; TrustServerCertificate=True` because the local SQL Express certificate is self-signed — **local development only**; production gets a trusted certificate (P7). `sqlcmd` needs `-C` for the same reason.

## Rules carried into P1–P7
Command results are JSON objects (records), never bare scalars (`CK_idempotency_result` ISJSON). Authorization: `IAccessPolicy` scope `null` = property-wide only, `AccessScopes.Any` = holds it at any scope (then check the record), `Shift/HkTask/Room` = own/assigned via the owning module's `IAccessScopeResolver`. Money is `long` paise (`Paise`), rounding half away from zero; dates are `DateOnly` / `DATE`, events `DATETIME2(3)` UTC from an injected `TimeProvider`; every command goes through `ICommandRunner` (Idempotency-Key, audit row mandatory, outbox in the same transaction); `XACT_ABORT ON` + `CATCH → ROLLBACK; THROW` in procedures; locked check-then-insert (no catch-duplicate inside a transaction); `expectedVersion` = base64 rowversion → `STALE_VERSION`.
