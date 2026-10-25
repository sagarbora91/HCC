# Wave 1 parallel rules (P1–P5) — contracts freeze

25 Oct 2026, lead. Base: branch `claude/wave1-p0` at the commit that adds this file (P0 foundation; 54/54 tests incl. SQL on `.\SQLEXPRESS`). The owner started Wave 1 ahead of formal prototype acceptance; nothing here is merged into `main` until the owner says so.

## Frozen (do not edit — list any change you need under "Requested changes" in your final report)
- `src/Hcc.Contracts/*.cs` (error codes, `ErrorResponse`, `ApiConventions`, `SystemContracts`)
- `src/Hcc.Domain/{Money,Dates,Errors,Pricing,ThirdParty}/*` (paise kernel, half-away-from-zero rounding, business dates, H02/H04 ports)
- `src/Hcc.Application/Commands/*` (`CommandContext`, `ICommandRunner`, `ICommandTransaction`, canonical JSON) and `src/Hcc.Application/Access/IAccessPolicy.cs`
- `src/Hcc.Infrastructure.SqlServer` platform parts (connection policy, migration runner, backup gate, command runner, outbox consumer)
- `database/migrations/0001_platform.sql`, `0002_core_inventory_billing_payments.sql` (applied migrations are never edited)
- `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `tests/Hcc.Platform.Tests/*`

## Ownership per agent
Each module lives in its own folder inside the layered projects (contracts §7) plus its own test project:
`src/Hcc.Contracts/<Module>/`, `src/Hcc.Domain/<Module>/`, `src/Hcc.Application/<Module>/`, `src/Hcc.Infrastructure.SqlServer/<Module>/`, `src/Hcc.Api/Endpoints/<Module>Endpoints.cs`, `tests/Hcc.<Module>.Tests/`.

| Agent | Module folder(s) | Migrations | Contracts (§3) / permissions |
|---|---|---|---|
| P1 Access | `Access` | `0100`–`0199` | users, roles, grants, sessions, `IAccessPolicy` implementation, `/me/capabilities`, DEC-040; PERM coverage from `07 Integration Lab/evidence/permission-coverage.*` |
| P2 Inventory & reservations | `Inventory`, `Reservations` | `0200`–`0299` | GetAvailability, QuoteStay, CreateReservation, AmendReservation, Cancel/NoShow; holds, group blocks (explicit block context), `RESTRICTED` |
| P3 Stays & housekeeping | `Stays`, `Housekeeping` | `0300`–`0399` | RegisterGuest, CheckIn, CheckoutStay (Dirty + outbox `StayDeparted`), TransitionHousekeepingTask, maintenance blocks |
| P4 Billing & cashier | `Billing`, `Payments` | `0400`–`0499` | PostRoomNight, RecordReceipt, Request/Decide/ExecuteAdjustment (DEC-041), tills + shifts per DEC-052 shared till, refunds under row lock |
| P5 Web client boundary | `03 Prototype/src/clients/*` (new) | none | typed feature clients, mock adapter over `execute()`, HTTP adapter for availability → quote → book → booking details; build-time exclusion of demo controls |

## Shared files — add only your own lines
- `Hcc.slnx`: one `<Project>` line for your test project (and new projects if any) + your migration `<File>` lines.
- `src/Hcc.Api/Program.cs`: one `api.Map<Module>Endpoints();` line and one DI registration line per module.
- `third-party/packages.json`: only if you add a package (avoid; ask first in your report).

## Cross-module rules
- Authorization: call `IAccessPolicy.RequireAsync(...)` with the PERM id from contracts §3 in every handler. P2–P4 test with a fake policy in their own test project; P1 provides the real one.
- Use the tables from `0002` as they are. If you need a column/table another module owns, add it in YOUR migration block only if it is yours by the table above; otherwise define an interface in your Application folder, stub it, and list it under "Requested changes".
- Every command goes through `ICommandRunner` (Idempotency-Key, mandatory audit row, outbox in the same transaction). `expectedVersion` = base64 rowversion → `STALE_VERSION`. Money `long` paise; `DateOnly`/`DATE`; event times from the injected `TimeProvider`.
- Golden figures: port the prototype's rules and assert the prototype's figures (BASE fixture; `tools/golden/export-golden.mjs`).
- `dotnet build` 0 warnings; `dotnet test` all pass on `.\SQLEXPRESS` (disposable `HccTest_*` databases only; never touch `EtpReporting`/`EtpScratch_*`).
- ETP is not on this PC: keep `// ETP-REUSE:` markers for anything ETP should provide and add them to `ETP_Reuse_Pending.md` (your own lines).

## Open owner decisions that affect Wave 1
- Production sign-in: password per user vs PIN + device binding on shared tablets (contracts §8 Q4). P1 implements a credential abstraction with PBKDF2 passwords now and PIN behind the same interface, pending the ruling.
- Closed-date corrections: adjustment-only vs controlled reopen (DEC-028) — P6, not now.
