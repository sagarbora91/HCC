# F5 integration — phase 1 (2 Nov 2026)

Branch `claude/f5` (worktree `/home/user/wt-f5`). Integrator: Opus.

## Merge
- `claude/f5-e1` (egress) merged earlier; `claude/f5-a1` (ACC-4 year lock + bank reconciliation) merged in `21173405`.
- Conflicts resolved keeping both lanes' blocks:
  - `03 Prototype/src/services/mock/handlers/index.ts`: both imports kept; A1's `registry0` + `yearGuard` wrapper kept, E1's `...egress` added to `registry0` (before `...catalogue`, which stays ahead of A1's year / bank handlers).
  - `03 Prototype/src/services/mock/index.ts`: both export blocks kept.
  - `08 Production/database/migrations.lock.json`: A1's lock taken, then `node tools/migrations-lock.mjs --add --accept-out-of-order` appended E1's 4730–4734 (below A1's highest); lock in step, 186 migrations.

## Fixes
- **Server year lock on every posting path** (`6e208a26`). `Hcc.Application/Accounting/YearLockedCommandRunner.cs` wraps the platform runner (`SqlPlatform.Commands`, so every service — business-day guarded or not — passes it). Mirror of the prototype `yearGuard`: payload `Date` / `BusinessDate` / `VoucherDate` / `PostingDate` ("yyyy-MM-dd" or DateOnly) and `Month` ("yyyy-MM") are checked; a payload without a date is checked on the property's business date (folio, POS, invoices). Refusal = `YearLockRules.Locked(fy)` → INVALID_TRANSITION with `details.reason = ACC_YEAR_LOCKED` (contract §1.2). SQL port `Hcc.Infrastructure.SqlServer/Accounting/SqlYearLockGuard.cs` reads `acc.v_locked_year` (migration 4283) in the writer's transaction. Year-end commands (`YearLockService`) do not use the runner. Cost: one small read per command.
- Tests: `tests/Hcc.Accounting.Tests/YearLock/YearLockPostingSqlTests.cs` (payload date reading; undated post on a locked business date, manual voucher, day unlock refused; 31 Mar in / 1 Apr out).

## Egress gate check
- The `EgressGateHandler` is attached only to its own named client (`EgressKeys.ClientName`, `AddHccEgress`); no existing HttpClient is wrapped yet. Architecture tests pass on the merged code.

## Counts
- `npx tsc --noEmit` clean; `npm test` **2499/2499**.
- `dotnet build Hcc.slnx` 0 warnings, 0 errors.
- `dotnet test`: Accounting **320/320**, Egress **16/16**, Architecture **3/3**, Configuration **35/35** (full server suite not run in phase 1).

## Open (A1 follow-ups still for f5-int final)
- Exports stamp `PROVISIONAL — year reopened` (`accounting-exports.ts`, `accounting-tally.ts`, server export endpoints); permission-matrix rows PERM-ACC-014…017; `MarkReexportAsync` server no-op.

## What U1 must build
- **Accounting tabs:** Year end (preview checklist, carry-forward draft, lock / reopen / relock, provisional mark), Bank reconciliation (layout master, CSV import, auto / manual match, ignore, create voucher, B8 settlement, recon report + CSV), Clearing (1010 / 1020 open tender days, amber > 3, red > 7) — plus phone cards for each.
- **Master configuration › Internet** group driven by `NET_SETTING_DEFS`, saving through `CFG_NET_SET {setting, value, reason}`.
- **System health › Egress** block (gate state, held queue, audit).
- **Banners:** year reopened / provisional; internet off / held.
- **Held-queue badge** (egress outbox held count).
- **Capacitor** `allowNavigation` in `04 Android` matching the allowed egress hosts.
- **Tests:** `tests/stream-accounting-ui4.mjs`, `tests/stream-egress-ui.mjs`, `golden4`.
