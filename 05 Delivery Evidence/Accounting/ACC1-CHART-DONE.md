# ACC-1 lane acc1-chart — DONE (27 Oct 2026)

Lane: groups, chart of accounts as an Owner-only master, re-pointed mapping. Branch `claude/acc1-chart` (worktree `E:\Projects\HCC\worktrees\acc1-chart`), base `39930edc`, wave base `claude/acc1` merged at `83be423d` (the SCHEMA 11 re-pins). Never pushed.

## Commits

| commit | what |
|---|---|
| `02611150` | Prototype: Tally groups + §2 built-in ledgers, `accounting-chart.ts`, `ACC_LEDGER_UPSERT` / `ACC_LEDGER_RETIRE` / `ACC_GROUP_UPSERT`, mapping re-points, tests |
| `ffcfe3bd` | Server: `ChartOfAccounts` (groups + §2 table + `EffectiveChart` merge), `ChartRules`, `ChartService` / `ChartQueries`, `SqlChartStore`, `AccountingChartEndpoints`, migrations 4200–4202, accounting golden regenerated, tests |
| `5232c295` | Merge of `claude/acc1` (wave base re-pins) |
| `a34e1921` | Server tests green; RBAC seed renamed `4202_acc_chart_rbac_seed.sql`; migrations locked; existing tests moved to the re-pointed defaults / contract names |
| `e750c54c` | Production sync area `accountingChart` + sync tests |
| (this file) | DONE file |

## Files

Prototype (`03 Prototype`):
- `src/domain/streams/accounting.ts` — my sections only: chart section moved below the coordinator's ACC-1 contract block (it reads `ACC_GROUPS` at module init); `ACC_LEDGERS` (the §2 table as `ACCLedger` rows: group id, `system`, `control`, `gst`, `active`, note), `accGroupDef`, `ACC_CHART` derived from it (`ACCAccount` now carries `group` = legacy group, `groupId`, `system`, `control`, `gst`), `accAccount` (a party id `<control>/<party>` resolves to its control, contract §3), `accLedgerName`; `ACC_DEFAULT_MAP` re-points; new `accCashDiffKey`, `accItcSplit`; `accMapRows` counts `tax:gst-input-sgst` with `tax:gst-input`.
- `src/domain/streams/accounting-chart.ts` (new) — `accChart`, `accGroups`, `accGroupOf/Root/Legacy/Path`, `accGroupSlug`, `accLedger`, `accLedgerNameOf`, `accLedgerTallyName`, `accLedgerNature`, `accLedgerLegacyGroup`, `accLedgerMappedBy`, `accLedgerBalances`, `accLedgerRetireBlocker`, `accLedgerUpsertPlan`, `accGroupUpsertPlan`, `accChartProblems`, `accChartRows`, `accChartHistory`, `accCanEditChart`, `ACC_CHART_FIXED`. No top-level reads of `./accounting` values (import-cycle safe for the integration below).
- `src/services/mock/handlers/accounting-chart.ts` (new) — the three commands, history rows, MASTER_* reasons via `mstFail`.
- `src/services/mock/handlers/index.ts`, `src/services/mock/index.ts` — one appended line each in the ACC-1 blocks.
- `src/clients/sync/accounting-chart.ts` (new) + one line in each ACC-1 block of `src/clients/sync/registry.ts`.
- Tests: `tests/stream-accounting-chart.mjs` (new, 10), `tests/sync-accounting-chart.mjs` (new, 3); updated `tests/stream-gst.mjs`, `tests/stream-expenses-decisions.mjs`, `tests/stream-rbac-matrix.mjs` (see "Existing tests changed").

Server (`08 Production`):
- `src/Hcc.Domain/Accounting/ChartOfAccounts.cs` — `LedgerGroup`, `LedgerGst`, `StoredLedger`; `Groups` (Tally's 28), `LegacyOfPrimary`, `GroupOf`; `All` = the §2 table (same names / notes / gst / control as the prototype — a test parses the prototype source and compares); `LedgerAccount` gains `GroupId`, `System`, `Control`, `Gst`, `TallyName`, `Active`, `EffectiveTallyName` (positional shape unchanged); `Find` resolves party ids to the control (contract §3); `OrderOf` sorts a party with its control; `ControlPart`; `Merge(stored, customGroups)` → `EffectiveChart` (Find / NameOf / TallyNameOf / GroupOf / RootOf / LegacyOf / NatureOf / PathOf / IsUnder / Problems); `DefaultMapping` re-points; `CashDiffKey`, `ItcSplit`, `GstInputSgstKey`; `Effective(...)` and `Resolve(...)` take an optional `EffectiveChart` (null = built-ins, so every existing caller is unchanged).
- `src/Hcc.Domain/Accounting/ChartRules.cs` (new) — `UpsertLedger`, `UpsertGroup`, `RetireBlocker`, `GroupSlug`, `DrCr` (same messages and MASTER_* reasons as the prototype).
- `src/Hcc.Contracts/Accounting/ChartContracts.cs` (new) — requests, results, views.
- `src/Hcc.Application/Accounting/ChartService.cs` (new) — `ChartPermissions.Edit = PERM-ACC-007`, `IChartStore`, `ChartService` (UpsertLedger / RetireLedger / UpsertLedgerGroup, audited, history), `ChartService.UsageAsync` (mapping keys + balances of the ledger and its parties over every voucher up to the business date, locked snapshots as locked), `ChartQueries` (master view + history, PERM-ACC-001).
- `src/Hcc.Infrastructure.SqlServer/Accounting/SqlChartStore.cs` (new).
- `src/Hcc.Api/Endpoints/AccountingChartEndpoints.cs` (new; services built from the composed platform parts, so no DI line) + one appended `api.MapAccountingChartEndpoints();` in the ACC-1 block of `HccComposition.cs`.
- Migrations `4200_acc_ledger_groups.sql`, `4201_acc_ledger.sql`, `4202_acc_chart_rbac_seed.sql` (+ `migrations.lock.json` via `--add`, + `Hcc.slnx` lines).
- Tests: `tests/Hcc.Accounting.Tests/Domain/ChartTests.cs` (new, 11), `Sql/SqlChartStoreTests.cs` (new, 4); updated `Domain/AccountingRulesTests.cs`, `Domain/BooksDec074Tests.cs`, `Sql/MappingAndExportTests.cs`; regenerated `Golden/accounting-golden.json`.

## Built against the contract

- **§1 groups** — `ACC_GROUPS` (coordinator) used as is; custom sub-groups `g-<slug>` under any group, nature and side inherited from the parent; Tally's 28 are never renamed, re-parented or deleted (an edit of one is refused MASTER_INVALID "fixed"); the legacy `ACCGroup` of any group (custom included) = the legacy group of its primary ancestor, so a sub-group of Suspense A/c stays Suspense.
- **§2 ledgers** — every code of the table with its group and control flag (controls 1000, 1030, 1110, 1115, 1120, 1200, 2200, 2400). Built-ins: renamable, re-groupable within the same nature, GST flag / Tally name / note editable, code and control fixed, never deleted. Owner-added: `create:true`, unused 4-digit code (MASTER_CODE_TAKEN otherwise, incl. retired codes), any group, never control; may change nature only while unused (no mapping, zero balance). Names and effective Tally names unique (case-insensitive). Retire refused (MASTER_IN_USE) while a mapping key points at it (defaults + money-master ledgers + Mapping overrides), while it or any party `<code>/<party>` has a non-zero balance (all vouchers up to the business date, locked snapshots as locked), and always for 9999 and 1400; already retired → MASTER_RETIRED. Reactivate = `ACC_LEDGER_UPSERT {code, active:true}`. A built-in edited back to its defaults keeps no stored row.
- **§2 re-points** — `pos:sales` 4200, `line:attrition` 4300 (new in the prototype map: the prototype posts no attrition, the server does), `closing:cash-over` 4950 (new key; `closing:cash-difference` stays 5900 for a short), `tax:gst-input` 1310 + new `tax:gst-input-sgst` 1320, `receipt:udhaar` 1120 unchanged. Locked snapshots untouched (test `[ACC1-CHART] locked snapshots are never re-pointed`). Helpers for the posting split: `accCashDiffKey` / `accItcSplit` (server `ChartOfAccounts.CashDiffKey` / `ItcSplit`); the split itself lives in files other lanes own — see Integration needed 1–2.
- **§7** — PERM-ACC-007 seeded to OWN only (`is_money = 1`); prototype = `mstCanEditMoney` (Owner + admin, the masters framework's money rule); server commands require PERM-ACC-007, reads PERM-ACC-001; `canEdit` in the master view.
- **§8** — `s.accountingX.chart = {ledgers, groups, history}` exactly as typed by the coordinator (no SCHEMA change by this lane; old saves read the built-ins). History rows `ACCChartChange` one per changed field (`created`, `name`, `group`, `gst`, `tallyName`, `note`, `active`, `parent`).
- **§9** — migrations 4200–4202 only (4203–4209 unused).

Interpretations (stated, not silent):
1. "Re-groupable within the same nature" (rulebook §3.4.1) = Tally's nature of a group (Assets / Liabilities / Income / Expenses, + Suspense) = the legacy group of the primary ancestor — not just Dr / Cr, which would let Cash (BS Dr) move into Indirect Expenses (PL Dr).
2. The contract names no key for the cash over; I used `closing:cash-over`. Coordinator: please write it into contract §2.
3. The contract's file name `4202_acc_rbac_chart.sql` was renamed `4202_acc_chart_rbac_seed.sql`: `Hcc.Access.Tests PermissionSeedCoverageTests` only reads `*_rbac_seed.sql` files, so the PERM id would otherwise count as unseeded. **The opening lane's `4221_acc_rbac_opening.sql` needs the same suffix.**
4. GST flags of built-ins (the contract has none): input = 1300, 1310, 1320, 1330; output = 2100, 2110, 2120, 2130, 2140; the rest none.
5. Built-in names follow the contract table, so CSV / Tally ledger names change for renamed codes (e.g. 5030 "Utilities" → "Electricity, water & fuel", 5900 "Cash over / short" → "Cash short", 4100 "Other revenue" → "Other services revenue"). Nothing is live in Tally yet; `tallyName` lets the Owner keep an old name per ledger.
6. Legacy group of 1300 changes Asset → Liability (it now sits in Duties & Taxes, Cr nature, as in Tally). Import / master ledger pickers filtering on Asset / Liability keep offering it.

## Test counts

| suite | before | after |
|---|---|---|
| Prototype `npm test` | 1854 / 1867 at lane start (13 = the SCHEMA 11 bump, re-pinned by the coordinator in `83be423d`); 1867 / 1867 on the wave base | **1870 / 1880** — +13 new tests all pass; the 10 failures are the FY26 history / FY26 hash pins (`[CAT] [FDM] [MFX]×2 [MST] [OPSM] [PPL] [PERF] [POS-MST] [RMS]`), see below |
| `npx tsc --noEmit` | 0 errors | 0 errors |
| `npm run build` | ok | ok (dist/index.html 3220 KB) |
| `dotnet build Hcc.slnx` | ok | ok, 0 warnings / 0 errors |
| `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx` | 57 | **72 / 72** (+11 `Domain/ChartTests`, +4 `Sql/SqlChartStoreTests`) |

Logs (not committed): `.scratch/npm-test-baseline.log`, `.scratch/npm-test-after-final.log`, `.scratch/server-test-3.log`, `.scratch/accounting-tests.trx`, `.scratch/build-3.log`, `.scratch/build-proto.log`.

**Deliberate fixture change — pins left to the integrator.** The FY26 history generator locks its closed days with `accLiveVouchers` (`src/fixtures/history/modules-sales.ts`), so the default re-points (POS sales 4100 → 4200, ITC 1300 → 1310, …) change the locked snapshots inside FY26. BASE is unchanged (`efcc756f…` still passes). New values on this branch: FY26 history `5424daab9fc59a854b8242e456ca99a010a676df71a30e295f4d9a902b8dadf9` (was `f3601522…`), FY26 whole state `2cd1742badbfafd935755d960caadcc7618b42b30de808cef2ca7f2158399e8a` (was `f9eb386f…`). They will move again when the ITC / cash-over split and the parties lane land — re-pin once after the whole wave.

## Existing tests changed (defaults moved)

- `03 Prototype/tests/stream-gst.mjs` — outlet bill credits 4200.
- `03 Prototype/tests/stream-expenses-decisions.mjs` — ITC on 1310 (whole amount until Integration 1), 1300's new name, map row 1310, Tally ledger "Input CGST".
- `03 Prototype/tests/stream-rbac-matrix.mjs` — CMDS rows for the three new commands (Owner).
- `08 Production/tests/Hcc.Accounting.Tests/Domain/AccountingRulesTests.cs` — POS 4200; 1300 name / Liability; ITC 1310; Tally "Input CGST".
- `…/Domain/BooksDec074Tests.cs` — attrition 4300.
- `…/Sql/MappingAndExportTests.cs` — ITC 1310; CSV / Tally ledger name "Electricity, water & fuel".
- `…/Golden/accounting-golden.json` — regenerated with `export-accounting-golden.mjs`. Reason: contract §2 names (CSV / Tally XML ledger names) and the `pos:sales` re-point; `check-goldens.mjs` reports only this golden changed.

## Integration needed (files I do not own — exact edits)

1. **Prototype `accLiveVouchers`** (`src/domain/streams/accounting.ts`, parties lane / integrator):
   - expense ITC split — replace `...(itc?[dr(map('tax:gst-input',miss),itc)]:[])` with `...(itc?accItcSplit(itc).map(([k,p])=>dr(map(k,miss),p)):[])`;
   - cash over — replace `acct=map('closing:cash-difference',miss)` with `acct=map(accCashDiffKey(diff),miss)`;
   - Owner-added ledgers as mapping targets — add `import {accChart} from './accounting-chart';` and, first line of the function, `const known=new Set(accChart(s).filter(l=>l.active).map(l=>l.code));`, then in `map` replace `accAccount(a)` with `known.has(a)`.
   - then in `tests/stream-expenses-decisions.mjs`: `[['5020',50000,0],['1310',9000,0],['1010',0,59000]]` → `[['5020',50000,0],['1310',4500,0],['1320',4500,0],['1010',0,59000]]`, the reversal likewise with sides swapped, `net(vs,'1310')` and `net(vs,'1320')` both 0, Tally regex `-45.00`; the `ACC_MAP_SET tax:gst-input → 5020` check becomes `['5020','5020','1320','1010']` (4 entries — change `entries.length===3` to `4`).
2. **Server `AccountingSource.cs`** (parties lane): in `SourceRules.Expense`, replace `Dr(GstInputKey, itcPaise)` with `Dr(GstInputKey, ChartOfAccounts.ItcSplit(itcPaise).Cgst), Dr(ChartOfAccounts.GstInputSgstKey, ChartOfAccounts.ItcSplit(itcPaise).Sgst)`; in `SourceRules.CashDifference`, over branch, replace `Cr("closing:cash-difference", differencePaise)` with `Cr(ChartOfAccounts.CashDiffKey(differencePaise), differencePaise)`. Tests then: `AccountingRulesTests.Dec135…` and `MappingAndExportTests.Dec135…` 1310 9 000 → 1310 4 500 + 1320 4 500.
3. **Server per-property chart in the postings** (integrator, or ACC-2): `VoucherBuilder.Build` (`Vouchers.cs`) → `ChartOfAccounts.Resolve(mapping, e.MapKey, chart)`, `IAccountingStore.EffectiveMappingAsync` → `Effective(overrides, masterLedgers, chart)`, `AccountingService.SetMappingAsync` → validate the account with `chart.Find(code)` and `Active` instead of the static `ChartOfAccounts.Find`, `VoucherExport` CSV / Tally names → `chart.NameOf` / `chart.TallyNameOf`. The chart comes from `IChartStore.ChartAsync(tx, propertyId, ct)` (register `IChartStore` → `SqlChartStore` in `AddAccountingModule` when doing this). Until then Owner renames / Owner-added ledgers show on the chart master only, not in vouchers or exports (same on the prototype until item 1 and item 4).
4. **Prototype `ACC_MAP_SET`** (`src/services/mock/handlers/accounting.ts`): replace `const acct=accAccount(String(cmd.account??''));` with `const acct=accLedger(s,String(cmd.account??''));` and add `&&acct.active` to the assert (import `accLedger` from `accounting-chart`). Money-master ledger pickers (`money-master-specs.ts` `ledgerOpts`) still list built-ins only (static `ACC_CHART`); switching them to `optionsFrom:s=>accChart(s)…` is a follow-up.
5. **Production chart loader**: add to `src/clients/sync/reads.ts` READS `'GET /accounting/chart/master':ACCOUNTING,` and `'GET /accounting/chart/history':ACCOUNTING,`; in `src/clients/sync/accounting-chart.ts` set `loaders:[{...ACC_CHART_LOADER,reads:['GET /accounting/chart/master','GET /accounting/chart/history']}]`; give the fake servers of the sync tests that assert `srv.unrouted` empty (sync-catalogue, -frontdesk-master, -masters, -money-master, -ops-masters, -people-master, -pos-master, -roommasters, -services, -roles) a route for both GETs (e.g. a default in `_sync-harness.mjs`: `{groups:[],ledgers:[],problems:[],canEdit:false}` and `[]`), and add both reads to `tests/_role-reads.json` via `Hcc.Integration.Tests RoleReadMatrixTests`.
6. **`08 Production/tests/Hcc.Access.Tests/Sql/RbacV2MatrixTests.cs`** `LaterPermissions`: add `["PERM-ACC-007"] = ["A", "—", "—", "—", "—", "—", "—", "—", "—", "—"],` (roles OWN, MGR, ACC, REC, CSH, SUP, HKP, MNT, RST, TAD).
7. **`08 Production/tests/Hcc.Groups.Tests/Sql/GroupBooksSqlTests.cs`**: line 83 `("4000", 0L, 2_700_000L)` → `("4300", 0L, 2_700_000L)`; line 103 `B("4000")` → `B("4300")` (attrition re-point). Not run by this lane.
8. **Fixture pins** — re-pin the FY26 history / FY26 hashes (values above) once after the wave.
9. Observation for the parties lane: `acc.locked_entry.account_code` and `acc.mapping.account_code` are `VARCHAR(4)`; party ids `<code>/<party>` in locked snapshots need a wider column.

## Open items

- No retire / delete of a custom sub-group in ACC-1 (not in the contract); a group in use would need the same in-use rules.
- Ledger opening balance per ledger (rulebook §3.4.2) is the opening lane's entry, not a chart field.
- The Owner's rename of a built-in is not yet carried into vouchers, CSV or Tally names (Integration 3–4).
- The "Accountant proposes chart changes" cell of rulebook §6 is not built (contract §7 seeds Owner only).

## HANDOFF bullet

- **ACC-1 chart lane (27 Oct):** chart of accounts as an Owner-only master — Tally's 28 groups + Owner custom sub-groups `g-<slug>`, the 70 built-in ledgers of contract §2 (group, control, GST flag), Owner overrides and Owner-added ledgers, retire refused while mapped / with a (party) balance / for 9999 and 1400; prototype `accounting-chart.ts` + `ACC_LEDGER_UPSERT` / `ACC_LEDGER_RETIRE` / `ACC_GROUP_UPSERT`; server `ChartOfAccounts` (`EffectiveChart`), `ChartRules`, `ChartService` / `ChartQueries`, `SqlChartStore`, `/accounting/chart/master|history`, `POST /commands/UpsertLedger|RetireLedger|UpsertLedgerGroup`, migrations 4200–4202 (PERM-ACC-007 → Owner); mapping re-points `pos:sales` 4200, `line:attrition` 4300, `closing:cash-over` 4950, ITC 1310 + 1320 (split wired at integration); accounting golden regenerated; tests prototype +13, server Accounting 57 → 72. `05 Delivery Evidence/Accounting/ACC1-CHART-DONE.md`.
