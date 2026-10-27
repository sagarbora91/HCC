# ACC-1 integration notes (coordinator → integrator, 27 Oct 2026, ~21:00)

All seven lanes are done. Each lane's DONE file in its worktree under `05 Delivery Evidence/Accounting/` has the exact edits; this file is the merged checklist plus the coordinator's rulings on the contract points the lanes raised.

## Lanes and their last commits

| lane | branch | last commit | prototype | server acc tests | notes |
|---|---|---|---|---|---|
| chart | `claude/acc1-chart` | a116863b | 1870/1880 (10 FY26 pins moved) | 72/72 | golden regenerated |
| parties | `claude/acc1-parties` | 7458a9ab | 1869/1879 (10 FY26 pins moved) | 76/76 | golden regenerated; migrations 4210, 4211, 4215 |
| opening | `claude/acc1-opening` | c9dc96da | 1881/1881 | 71/71 | migrations 4220, 4221 |
| statements | `claude/acc1-statements` | fe53f085 | 1877/1877 | 66/66 | no migrations |
| ui | `claude/acc1-ui` | be993743 | 1869/1882 (13 pre-re-pin failures) | — | stubs file to delete |
| tests | `claude/acc1-tests` | df3244fa | 1870/1884 (waits for lanes) | 86/94 (waits for chart) | ACC-RULEBOOK scenario |
| tools | `claude/acc1-tools` | 2d7f849e | gate dry run PASS at 93389988 | 3324/3324 | `tools/Run-Gate.ps1` etc. |

Every lane merged `claude/acc1` at 83be423d (the re-pinned base), so merge them into `claude/acc1` in this order: **chart, parties, opening, statements, ui, tests, tools**. Expect overlaps in `accounting.ts` (chart constants vs parties' `accLiveVouchers`; opening's two one-liners), the three append-only blocks, `HccComposition.cs`, `AccountingQueries.cs` (parties one line, opening one call, statements), `Hcc.slnx`, `migrations.lock.json`, `clients/sync/accounting.ts`, `services/mock/index.ts`, `fixtures/ext/index.ts`, and the accounting golden JSON (take either, regenerate at the end).

## Coordinator rulings on the contract points raised

1. **Cash over key** = `closing:cash-over` → 4950 (chart lane's choice). Add to contract §2. A short stays `closing:cash-difference` → 5900.
2. **"Same nature"** for re-grouping a system ledger = the legacy nature of the group (Asset / Liability / Income / Expense / Suspense), as the chart lane read it.
3. **RBAC seed file names** must match `*_rbac_seed.sql` (PermissionSeedCoverage test): chart already uses `4202_acc_chart_rbac_seed.sql`; **rename opening's `4221_acc_rbac_opening.sql` → `4221_acc_opening_rbac_seed.sql`** and fix its entries in `migrations.lock.json` and `Hcc.slnx` (the migration is unapplied anywhere, so renaming is allowed; use `migrations-lock.mjs` to re-add, `--accept-out-of-order` where needed).
4. **Seeded till party id** is `1000/TILL` (the seeded row's code), not `TILL-MAIN`; the Udhaar table is `ops.udhaar_receivable`. Amend contract §3 / §4 to the facts.
5. **Opening gating** (rulebook G2 vs contract §5): with **no opening entry at all, locks are not gated**; the "opening balances pending" flag stays visible. Gating applies once an entry exists (draft, or approved with 1400 ≠ 0) for dates ≥ asOf. Reason: DEC-148 says the Manager enters the openings once the software is functional, so the trial period must still lock days. Amend rulebook §G2 and contract §5 wording; the opening lane built it this way.
6. **Statements**: the opening voucher dated ON `from` counts as opening (rulebook §7.2 wins over the contract's "before"); day-book order = date, then voucher number; "opening pending" also while 1400 ≠ 0 on an approved entry. Amend contract §6.
7. **Staff retire** is **not** blocked by a 1200 balance (full-and-final needs the exit first); company retire **is** blocked by a non-zero 1110 balance (exact edit in the parties DONE file).
8. **Advance applied at check-out (A6a)** has no command of its own; the tests lane's "Advance adjusted" payment method (ledger 2000, placeholder bank row `BA-ADV`) is accepted for the ACC-1 scenario; a real command is an ACC-2 item.
9. **Folio F&B lines** are kind `service` → 4100 today; rulebook A3 wants 4200. ACC-1 ships with the scenario's Owner mapping override `line:service → 4200` only inside the ACC-RULEBOOK scenario; a proper F&B line class (POS charge-to-folio / catalogue class) is an **ACC-2 item**. Do not change the default mapping of `line:service`.
10. **Prototype payroll vouchers**: the prototype derives none (the server does). Accepted for ACC-1; **ACC-2 item** (parity), together with per-staff advance recovery on the server (today one total → 1200 control with a no-party warning).

## Integration checklist (apply after the merges, before the suites)

Prototype
- Delete `src/features/accounting/_contract-stubs.ts`; re-point every import in `src/features/accounting/*` to the real selectors (`accChart`, `accGroups`, `accChartHistory`, `accLegacyOfGroup` or the equivalent names the chart lane exported — check its DONE), `accPartyLedgers`, `accPartyName`, `accOpening`, `accOpeningPending`, `accStatement`, `accTrialBalance6`, `accDayBook`, the three CSV builders, the day-book row type. Align the command payloads the UI assumed (listed in the UI DONE) with the real handlers — change the UI, not the handlers, unless a handler is plainly wrong.
- `accounting-reports.ts`: `partyName` → `accPartyName`.
- `accounting.ts`: add `'opening'` to `ACCSourceKind`; apply the chart lane's two one-liners in `accLiveVouchers` (ITC split via `accItcSplit` → 1310 / 1320; cash over via `accCashDiffKey` → `closing:cash-over` → 4950, short → 5900); Owner renames / Owner-added ledgers must reach postings, `ACC_MAP_SET` validation and the CSV / Tally names (use `accChart(s)` instead of the static `ACC_CHART` where a name or existence is looked up).
- Company retire check (parties DONE exact edit). `tests/stream-rbac-matrix.mjs` CMDS: every new command listed once (chart, opening, parties' `EXP_UD_ADD` fields).
- Production sync: register the chart loader (`ACC_CHART_LOADER`, two lines in `reads.ts` READS) and the opening loader (`GET /accounting/opening`), then regenerate `tests/_role-reads.json`; fake-server routes in the sync tests that assert no unrouted calls (chart DONE lists them).
- Phone smoke of the accounting pages at 360 px and a desktop click-through of every new tab (the UI lane could not do it): record it in `05 Delivery Evidence/Accounting/ACC1-UI-Smoke.md`.
- **Re-pin once at the end:** FY26 history and FY26 hashes ([CAT], [FDM], [MFX] ×2, [MST], [OPSM], [PPL], [PERF], [POS-MST], [RMS]) after every lane is merged and the one-liners applied; BASE must stay `efcc756f…` (if BASE moves, something changed the seed — find out why before re-pinning). Keep the comment style `/* ACC-1: … */` with the reason.

Server
- `HccComposition.cs`: `services.AddAccountingOpeningModule();` after `AddAccountingModule()`; the four `MapAccounting<Lane>Endpoints()` lines in the `// ACC-1` block.
- `AccountingEndpoints.cs`: pass `sp.GetService<IOpeningEntryStore>()` to `AccountingQueries`; route `GET /accounting/trial-balance` with `from` to `AccountingReportEndpoints.TrialBalanceAsync`, old `?date&cumulative` form unchanged (statements DONE has the line).
- Register `IReportPartyNames` (parties) and `IReportOpeningSource` (opening) for the report queries; `LedgerNames.Group` → the Tally group ids from the chart.
- `AccountingSource.cs`: the chart lane's two one-liners (`ChartOfAccounts.ItcSplit` in `Expense`, `CashDiffKey` in `CashDifference`); `VoucherBuilder`, the mapping resolver and `VoucherExport` take the property's chart (Owner renames / added ledgers).
- Migrations: 4200–4202 (chart), 4210, 4211, 4215 (parties), 4220, 4221 → renamed per ruling 3 (opening); `node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order` where the merge order requires; every file in `Hcc.slnx`; `migrations.lock.json` consistent.
- `Hcc.Access.Tests` `RbacV2MatrixTests.LaterPermissions`: rows for PERM-ACC-007, 008, 009. `Hcc.Groups.Tests` `GroupBooksSqlTests` lines ~83 and ~103: `4000` → `4300` (attrition). `OpeningBalanceSqlTests`: two asserts may need control folding after parties.
- Regenerate `accounting-golden.json` once at the end (state the reason in the DONE file); the tests lane's `Golden/rulebook-month.json` regenerates with its `build-rulebook-month.mjs`.
- The tests lane's 4 red server tests and 3 skipped ones should go green / un-skipped once chart, parties and opening are in — un-skip them and fix what they find.

## Suites and evidence

Through `tools/Invoke-Serialized.ps1` with `-Label acc1-integrator`: `npx tsc --noEmit`, `npm test` (expect 1867 + the lanes' new tests: chart 13, parties 12, opening 14, statements 10, ui 15, tests 17 — every one green, no skips), `npm run build`, `dotnet build Hcc.slnx`, then **the full** `dotnet test Hcc.slnx -maxcpucount:1 --logger trx` (3324 baseline + the lanes' new server tests; `Hcc.Expenses.Tests`, `Hcc.Access.Tests`, `Hcc.Groups.Tests` included). Never filter output; logs under `E:\Projects\HCC\worktrees\acc1\.scratch\integrator\`.

Then write `05 Delivery Evidence/Accounting/ACC1-DONE.md`: merge order and conflicts resolved, every checklist item with its commit, counts before / after both suites, the re-pinned hashes with reasons, the ACC-2 carry-overs (rulings 8–10 and the lanes' open items: bad-debt journal A18, prototype payroll vouchers, per-staff advance recovery, pre-4215 Udhaar credits without a sale voucher, production sync readers for the three report endpoints beyond the 62-day window, aggregated opening read, UI must always send `gstBps` on an Udhaar raise), and the HANDOFF bullets. Amend `ACC1-POSTING-CONTRACT.md` (rulings 1, 4, 6) and `HCC_Accounting_Rulebook_v1.md` §G2 (ruling 5) in the same wave.

The coordinator then runs `tools\Start-Gate.ps1 -Repo E:\Projects\HCC\worktrees\acc1 -Wave acc1` and fast-forwards `main`.
