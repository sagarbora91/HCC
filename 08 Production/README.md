# HCC production server (Wave 1 · P0 Foundation)

.NET 10 ASP.NET Core + SQL Server 2025 Express (DEC-054). Branch `claude/wave1-p0`; not merged into `main` until the owner says so. Specs: `02 Planning/HCC_Production_Contracts_and_Schema_v1.md` (§2 API, §4 schema, §7 projects), `HCC_Multi_Agent_Plan_v1.md` §4.

## Layout
| Path | Contents |
|---|---|
| `Hcc.slnx` | Solution (warnings as errors, nullable, deterministic; `Directory.Build.props`, central packages `Directory.Packages.props`, lock files) |
| `src/Hcc.Contracts` | Wire contracts: `HccErrorCode` + HTTP status/title catalogue, `ErrorResponse`, API constants. No dependencies. |
| `src/Hcc.Domain` | Rules, no I/O: `Money` (integer paise, half-away-from-zero bps rounding, folio totals, collections/drawer), `Dates` (stay dates, business day, H04 calendar date), `Pricing` (H02 LOS/occupancy), `HccException` |
| `src/Hcc.Application` | Command ports: `CommandContext`, `ICommandRunner`, `ICommandTransaction` (audit + outbox), canonical-JSON payload hash |
| `src/Hcc.Infrastructure.SqlServer` | Connection policy, migration runner + backup gate, idempotent `SqlCommandRunner`, `OutboxConsumer`, SQL value/error adapters |
| `src/Hcc.Api` | Host bootstrap: error middleware, `GET /api/v1/system/health`, `migrate` command |
| `database/migrations` | `0001_platform.sql` (schemas inv/gst/bill/pay/ops/cls/plat; idempotency, outbox, consumer dedup, append-only audit), `0002_core_inventory_billing_payments.sql` (spike-verified inventory/folio/receipt tables). Never edit an applied migration — add `0003_…`. |
| `tests/Hcc.Platform.Tests` | Kernel + golden + SQL integration tests (disposable databases) |
| `tools/golden/export-golden.mjs` | Regenerates `tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json` from the prototype BASE fixture |
| `third-party/` | `packages.json` (every NuGet package, version, licence), `source-reuse.json` (H02/H04 provenance) |
| `ETP_Reuse_Pending.md` | The 6 `ETP-REUSE` stand-ins to replace/align when `V:\ETP` is available |

## Commands (run in this folder)
```
dotnet build Hcc.slnx                              # must report 0 warnings, 0 errors
dotnet test Hcc.slnx                               # all tests; SQL tests need .\SQLEXPRESS (see below)
dotnet test Hcc.slnx --filter "Category!=Sql"      # kernel + golden tests only, no database
dotnet run --project src/Hcc.Api -- migrate        # apply migrations to the Hcc database in appsettings.json
dotnet run --project src/Hcc.Api                   # serve http://localhost:5000/api/v1/system/health (dev)
node tools/golden/export-golden.mjs                # Node 25 (E:\Tools\nodejs); needs 03 Prototype node_modules (esbuild)
```

## SQL test harness
- Each SQL test class creates its own database `HccTest_<UTC yyyyMMddHHmmss>_<8 hex>` on `.\SQLEXPRESS` (override: env `HCC_TEST_SQL_SERVER`), applies the migrations and drops it afterwards (`SINGLE_USER WITH ROLLBACK IMMEDIATE`, only names matching that pattern). No other database (e.g. `EtpReporting`) is touched.
- Leftovers after a crashed run: `sqlcmd -S .\SQLEXPRESS -E -C -Q "SELECT name FROM sys.databases WHERE name LIKE 'HccTest[_]%'"`.
- **One-time permission:** the Windows login running the tests needs `dbcreator` on the instance. On the work PC (25 Oct 2026) `Workpc\Sagar` is a plain login (sysadmin only through elevated `BUILTIN\Administrators`), so the SQL tests fail with a clear message until the owner runs, from an **elevated** prompt:
  `sqlcmd -S .\SQLEXPRESS -E -C -Q "ALTER SERVER ROLE dbcreator ADD MEMBER [Workpc\Sagar];"`
  (or runs `dotnet test` from an elevated prompt). The backup-gate test backs up to `NUL` (nothing written; msdb backup history gets a row).
- Connection strings use `Encrypt=Mandatory; TrustServerCertificate=True` because the local SQL Express certificate is self-signed — **local development only**; production gets a trusted certificate (P7). `sqlcmd` needs `-C` for the same reason.

## Rules carried into P1–P7
Money is `long` paise (`Paise`), rounding half away from zero; dates are `DateOnly` / `DATE`, events `DATETIME2(3)` UTC from an injected `TimeProvider`; every command goes through `ICommandRunner` (Idempotency-Key, audit row mandatory, outbox in the same transaction); `XACT_ABORT ON` + `CATCH → ROLLBACK; THROW` in procedures; locked check-then-insert (no catch-duplicate inside a transaction); `expectedVersion` = base64 rowversion → `STALE_VERSION`.
