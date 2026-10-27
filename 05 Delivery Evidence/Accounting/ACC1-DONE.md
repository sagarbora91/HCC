# ACC-1 — integration DONE (wave `claude/acc1`, 27 Oct 2026)

Integrator (Opus). Worktree `E:\Projects\HCC\worktrees\acc1`, branch `claude/acc1`, start `5f839ebf` (integration notes). Not pushed;
`main` not merged (the coordinator does that before the gate). Inputs: `02 Planning/agents/ACC1_Integration_Notes.md`,
`ACC1_Briefs.md`, `ACC1-POSTING-CONTRACT.md`, the seven lane DONE files (`05 Delivery Evidence/Accounting/ACC1-<LANE>-DONE.md`).

## 1. Merge order and conflicts

| # | lane (tip) | merge commit | conflicts and how they were resolved |
|---|---|---|---|
| 1 | chart `a116863b` | `51eb1091` | none |
| 2 | parties `7458a9ab` | `a026715b` | mock barrel ACC-1 block, `Hcc.slnx`, `migrations.lock.json` — both lanes' lines (4200–4202 then 4210 / 4211 / 4215: already ascending, no `--accept-out-of-order` needed); `stream-gst.mjs`, `stream-expenses-decisions.mjs`, `MappingAndExportTests.cs` — chart re-points (4200, 1310, contract names) **combined** with the party ids (`1000/TILL`, `1120/UD-RAMESH-FICTIONAL`); `accounting-golden.json` taken from chart (regenerated at the end) |
| 3 | opening `c9dc96da` | `d55d9dd9` | handler registry (`ACC1 = {...accountingChart, ...accountingOpening}`), mock barrel, sync registry, `HccComposition.cs`, `stream-rbac-matrix.mjs` CMDS, `Hcc.slnx`, lock — every line kept |
| 4 | statements `fe53f085` | `166c58ac` | mock barrel, `HccComposition.cs` — every line kept |
| 5 | ui `be993743` | `339af1f5` | none |
| 6 | tests `df3244fa` | `05b6c8a7` | mock barrel — every line kept (+ `rulebook-bind`) |
| 7 | tools `2d7f849e` | `5443318c` | none (the dry-run gate report stays as evidence) |

`accounting.ts` merged without textual conflict; one **semantic** clash: both chart and parties exported `accLedgerBalances` (chart:
`(s, code)` → `{balance, own, parties}`; parties: `(s, date)` → `Map`), so `tsc` failed on the mock barrel. Fixed in `8564bbb1`: the
parties' map is `accLedgerBalanceMap` (its only callers are `accPartyBalance` / `accPartyRetireBlocker`).

## 2. Integration checklist — every item with its commit

**Prototype**

| item | commit | notes |
|---|---|---|
| Delete `src/features/accounting/_contract-stubs.ts`, re-point the imports to the mock barrel | `b21836cb` | command shapes moved to `src/features/accounting/commands.ts` (UI-owned types aligned with the handlers); `accLegacyOfGroup` was unused; `[ACC1-UI]` source-discipline test retargeted (stubs gone, names exported by the barrel, commands named in `commands.ts`) |
| Align the UI command payloads with the real handlers (UI changed, not the handlers) | `b21836cb` | new ledger sends `create:true`; RETIRE = `{code, reason?}`; reactivate = `ACC_LEDGER_UPSERT {code, active:true}`; empty Tally name / note sent as `''` (clears); day book reads `ACCDayBook` (`rows`, `ledger`, totals) and `accDayBookCsv(db)`; built-in re-grouping offers only same-nature groups (ruling 2, `accGroupLegacy`); opening pending pill follows ruling 5 ("locks not gated until an entry exists" / "period locks from <asOf> refused") |
| `accounting-reports.ts`: `partyName` → `accPartyName` | `b21836cb` | also the group of a built-in = its Tally group id (`groupId`), as the server now does |
| `ACCSourceKind` gains `'opening'` | `b21836cb` | the cast in `accounting-opening.ts` removed |
| One-liners: ITC split (`accItcSplit` → 1310 / 1320), cash over (`accCashDiffKey` → `closing:cash-over` 4950, short 5900) | `b21836cb` | `stream-expenses-decisions.mjs` follows (1310 4 500 + 1320 4 500, Tally −45.00, `ACC_MAP_SET` 4 entries) in `96a13767` |
| Owner renames / Owner-added ledgers reach postings, `ACC_MAP_SET`, CSV / Tally names | `b21836cb` | `accLiveVouchers` maps only to an **active** ledger of the effective chart (`accChartIndex`, cached per stored chart); `ACC_MAP_SET` validates with `accLedger` + `active`; entry / trial-balance names and legacy groups, Tally `<LEDGERNAME>` (= `tallyName`), Mapping "mapped" read the chart |
| Company retire check (parties DONE) | — | **not applicable — see §6 "Contradicted / blocked"**: no company retire command exists (prototype or server); `accPartyRetireBlocker(s,'1110',id)` is ready |
| `stream-rbac-matrix.mjs` CMDS: every new command once | merges + `b21836cb` | chart's 3 and opening's 3 kept from the merges; `EXP_UD_ADD` rationale notes `kind` / `gstBps` |
| Production sync: chart loader + opening loader registered, READS lines, `_role-reads.json` regenerated, fake routes | `b21836cb`, `96a13767`, `f663c41f` | `GET /accounting/chart/master`, `/chart/history`, `/accounting/opening` in `reads.ts` (PERM-ACC-001) and in the fixture's routes; roles regenerated from the real server (`HCC_UPDATE_ROLE_READS=1 … --filter RoleReadMatrix`: OWN / MGR / ACC 200, the rest 403, + PERM-ACC-007/008/009 grants); default fake routes in `_sync-harness.mjs`; sync tests assert the loaders. **Lane bug fixed:** `accountingOpeningLoader` had `needs:['accountingX']` (its own slice) → "loader order cannot be resolved (needs cycle)" for every role (D5 ×10); removed in `f663c41f` (a patch loader already runs after the owner) |
| Phone smoke 360 px + desktop click-through | `d13871af` | `05 Delivery Evidence/Accounting/ACC1-UI-Smoke.md`; one fix: TB detail toggle 44 → 48 px (`m-accounting2.css`) |
| Re-pin FY26 hashes once at the end | `f663c41f` | see §4 |

**Server**

| item | commit | notes |
|---|---|---|
| `HccComposition.cs`: `services.AddAccountingOpeningModule()` after `AddAccountingModule()`; the four `MapAccounting<Lane>Endpoints()` | merges + `96a13767` | chart, opening, report lines from the merges; services line added |
| `AccountingEndpoints.cs`: `IOpeningEntryStore` to `AccountingQueries`; trial-balance dispatch | `96a13767` | `?from&to&detail` → `AccountingReportEndpoints.TrialBalanceAsync`; `?date&cumulative` unchanged |
| `IReportPartyNames`, `IReportOpeningSource`; `LedgerNames.Group` → Tally group ids | `96a13767` | new `SqlReportPartyNames` (tills / banks `mst.bank_account`, companies `CMP-<id>`, OTA `ota.channel.code`, Udhaar `ops.udhaar_receivable` via `PartyLedgers.Udhaar`, staff `STF-<id>`, vendors `ops.expense_vendor`), registered in `AddAccountingModule`; new `OpeningReportSource` (only an **approved** entry posts, as the prototype) registered in `AddAccountingOpeningModule`; `LedgerNames` takes the property chart (names) and returns `GroupId` |
| `AccountingSource.cs` one-liners (ITC split, cash over) | `96a13767` | |
| `VoucherBuilder`, mapping resolver, `VoucherExport` take the property's chart | `96a13767` | `IAccountingStore.ChartAsync` (default built-ins; `SqlAccountingStore` → `SqlChartStore`), `EffectiveMappingAsync(…, chart, …)`, `VoucherBuilder.Build(…, chart)`, `ChartOfAccounts.Resolve` requires an **active** ledger, `SetMappingAsync` validates against the chart, CSV / Tally names from the chart (Tally name) |
| Migrations 4200–4202, 4210 / 4211 / 4215, 4220, 4221 renamed (ruling 3) | merges + `96a13767` | `4221_acc_rbac_opening.sql` → `4221_acc_opening_rbac_seed.sql` (never applied), lock entry re-added with `migrations-lock.mjs --add` (in step, 96 migrations), `Hcc.slnx` line renamed |
| `Hcc.Access.Tests` RBAC rows PERM-ACC-007 / 008 / 009 | `96a13767` | |
| `Hcc.Groups.Tests` 4000 → 4300 | `96a13767` | also the Tally ledger name of 2000 ("Advance deposits (guest and group)", chart lane's contract name) |
| `OpeningBalanceSqlTests` folding | `96a13767` | the non-detail cumulative TB folds `1110/CMP-DECCAN` / `2400/LN-HDFC` into 1110 / 2400 |
| Un-skip the tests lane's server tests and make them pass | `96a13767` | the 4 red (`acc1-chart`) went green with the merge; the 3 skipped are **implemented**: party balances (party-aware sources as the projector resolves them), opening entry through the rules + lock gate (ruling 5) + cumulative closing, six-column TB / party statement / day book; `RulebookMonthSqlTests` extended: opening through `OpeningBalanceService` (MGR saves, OWN approves), lock with no entry allowed, with a draft refused, after approval allowed, cumulative TB = rulebook closing, six-column TB balanced and no longer pending |
| Contract §2 / §3 / §4 / §5 / §6 / §9 and rulebook §G2 amendments (rulings 1, 3, 4, 5, 6) | `a835c236` | |

## 3. Goldens (regenerated once, at the end of the code changes)

- `08 Production/tests/Hcc.Accounting.Tests/Golden/accounting-golden.json` (`export-accounting-golden.mjs`, in `96a13767`). **Reason:**
  the merge took the chart lane's copy; the regeneration brings the parties lane's party suffixes into the CSV names
  (`Cash in hand · TILL`) together with the chart re-points / names. The trial balances, exceptions and Tally XML in the file are those of
  the merged engine; `GoldenAccountingTests` and the prototype `[GOLDEN]` drift check pass.
- `Golden/rulebook-month.json` (`build-rulebook-month.mjs`): regenerated — **byte-identical** (the data did not change).

## 4. Re-pinned hashes (once, `f663c41f`)

| pin | before | after | reason |
|---|---|---|---|
| BASE | `efcc756f…` | **`efcc756f…` (unchanged)** | the seed is untouched |
| FY26 history `buildHistory(baseFixture())` | `f3601522…` | `e3a74ba3d3dab50209c1be2f0411a75eeeeb73d49936526bf30a11a491986004` | the FY26 history locks its accounting months with `accLiveVouchers` snapshots, which now carry the chart re-points (POS 4200, ITC 1310 / 1320, cash over 4950), party ledger ids (`1000/TILL`, `1030/<bank>`, `1120/UD-…`, `2200/<vendor>`) and the Udhaar credit / company payment vouchers |
| FY26 whole state | `f9eb386f…` | `e7f2ddac3a788cce16953a99814eef041061d02331716e54038d1a2c00585bcb` | same |

Tests re-pinned: [CAT] [FDM] [MFX] ×2 [MST] [OPSM] [PPL] [PERF] [POS-MST] [RMS], comment style `/* ACC-1 integration … */` with the reason.

## 5. Counts before / after

| suite | baseline (main) | after the merges, before the checklist | final |
|---|---|---|---|
| prototype `npm test` | 1867 | 1934 / 1948 (`npm-test-merged.log`: 10 FY26 pins, `[GOLDEN]`, `[ACC1-OPENING]` TB fold, `[ACC1-PARTIES]` 1030 name, `[ACC1-RULEBOOK]` statement name) | __PROTO__ |
| `npx tsc --noEmit` | 0 errors | 1 error (the `accLedgerBalances` clash) | __TSC__ |
| `npm run build` | ok | — | __BUILD__ |
| `dotnet build Hcc.slnx` | 0 warnings | — | __DBUILD__ |
| `dotnet test Hcc.slnx -maxcpucount:1 --logger trx` | 3324 | (lane runs: Accounting 147 / 147 after the checklist) | __DTEST__ |

Expected prototype = 1867 + chart 13 + parties 12 + opening 14 + statements 10 + ui 15 + tests 17 = **1948**, every one green, no skips.
Logs (uncommitted): `E:\Projects\HCC\worktrees\acc1\.scratch\integrator\` (`final-*.log`, `trx-final\`, earlier runs).

## 6. Contradicted / blocked

- **Ruling 7, company retire (stopped on this item):** the ruling and the parties DONE describe a guard on company retire, but **no
  company retire exists** — prototype `GRP_COMPANY_SAVE` has no active flag and `grp.company_account` has no active / retired column on
  the server. There is nothing to attach the guard to; building a company retire command is new scope. The helper is ready
  (`accPartyRetireBlocker(s,'1110',companyId)`; server `PartyLedgers.Company(id)` on 1110). Staff retire: not blocked (ruling 7) — no
  change made. Carried to ACC-2 below.
- Nothing else contradicted the code.

## 7. ACC-2 carry-overs

1. Bad-debt journal A18 (an Udhaar write-off leaves the receivable on `1120/<customer>`).
2. Prototype payroll vouchers (parity with the server: advance given, pay run, net pay paid — T13a–c) — ruling 10.
3. Per-staff advance recovery on the server (the pay run credits the 1200 control with a `no-party` warning today) — ruling 10.
4. Advance applied at check-out (A6a) as a real command (today the "Advance adjusted" method → 2000, placeholder bank `BA-ADV`) — ruling 8.
5. A proper F&B folio line class (POS charge-to-folio / catalogue class) → 4200; today only the ACC-RULEBOOK scenario's Owner override `line:service → 4200` — ruling 9; default mapping unchanged.
6. Udhaar credits raised on the server before migration 4215 have no sale voucher (one-off `UdhaarCreditRaised` publish if any production data exists); prototype rows before ACC-1 book as "other" without GST (CA note).
7. Production sync readers for `/accounting/statement`, `/trial-balance?from`, `/day-book` beyond the 62-day window.
8. Aggregated opening read on the server (reports derive every voucher since the first source today).
9. The Udhaar screen (`features/expenses/Scc.tsx`) must always send `kind` + `gstBps` on a raise (the server stores 0 when only `kind` is sent).
10. Company retire with the 1110 balance guard (§6).
11. Smaller follow-ups: money-master ledger pickers still list the built-ins only (`money-master-specs.ts` `ledgerOpts`); custom sub-group retire; "Accountant proposes chart changes" (rulebook §6); a party-detail Tally export; Drawing from the bank (T16, rulebook B6).

## 8. HANDOFF bullets

- **ACC-1 integrated (27 Oct 2026, `claude/acc1`):** chart, parties, opening, statements, ui, tests, tools merged in that order; the
  UI runs on the real selectors (stubs deleted, command payloads aligned); ITC splits 1310 / 1320 and a cash over posts 4950 on both
  sides; the property's chart (Owner renames / Owner-added / retired ledgers) reaches postings, the mapping check and the CSV / Tally
  names on both sides; server composition registers the opening module, the six-column trial balance on `GET /accounting/trial-balance?from&to&detail`,
  party names (`SqlReportPartyNames`) and the report opening (`OpeningReportSource`); production loaders for the chart master / history and the
  opening entry; `4221_acc_opening_rbac_seed.sql` (renamed); goldens regenerated once; FY26 re-pinned once (history `e3a74ba3…`, FY26
  `e7f2ddac…`, BASE `efcc756f…` unchanged); contract §2–§6 / §9 and rulebook §G2 amended (rulings 1, 3–6). Tests: prototype __PROTO__,
  server __DTEST__. `05 Delivery Evidence/Accounting/ACC1-DONE.md`, smoke `ACC1-UI-Smoke.md`.
- **Gate:** `pwsh -NoProfile -File tools\Start-Gate.ps1 -Repo E:\Projects\HCC\worktrees\acc1 -Wave acc1`, then fast-forward `main`.
- **Open for ACC-2:** §7 above (A18 bad debts, prototype payroll vouchers, per-staff recovery, apply-advance command, F&B folio line
  class, pre-4215 Udhaar back-fill, report readers beyond 62 days, aggregated opening read, Udhaar screen sends `gstBps`, company retire).
