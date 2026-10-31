# ACC2-PARITY DONE

Lane **acc2-parity** (Opus) · branch `claude/acc2-parity` · worktree `E:\Projects\HCC\worktrees\acc2-parity` · base `b78cb069` (ACC-2 wave setup).
Contract: `02 Planning/agents/ACC2-POSTING-CONTRACT.md` §8 (payroll, F&B line class, Udhaar back-fill, report readers, aggregated opening), §10 (migrations 4255–4259), plus the **coordinator rulings of 28 Oct** (below), adopted exactly.

## Coordinator rulings adopted

1. `HR_ADVANCE_ADD` takes **`funding`** (a payment-method name, default **`'Cash'`**); the advance voucher credits that method's ledger (Cash → `1000/<till>`, a bank method → `1030/<bank>`). Prototype: a cash advance needs the open shift, never takes the drawer below zero, and leaves the drawer (cash movement kind `staff-advance` in `cxDeskMovements` → `drawer()`, `shiftMovements`, `shiftExpected().transfers`, the cash book). Advances stored before ACC-2 (no `funding`) stay bank transfers (BASE `HR-ADV-1`, FY26 `HR-ADV-H1`).
2. Pay-run vouchers (**pay run** and **net pay paid**) are dated the **pay period's last day** whenever the lock / the bank file comes later; the narration carries the real date (`… · locked 2026-10-24`, `… · paid 2026-10-24`). Both sides: prototype `accPayrollDate`; server `PayrollPostedEvent.BusinessDate` / `PayrollPaidEvent.BusinessDate` = `min(act date, Month.Last)` with `LockedOn` / `PaidOn` carried for the narration. The unlock reversal stays dated the unlock day; F&F stays on its confirmation day.
3. Server: per-staff recovery goes out on `PayrollPosted` as `advanceRecoveries: [{staffId, paise}]` (`AdvanceRecoveryLine(StaffId, Paise)`); the **folio charge kind for F&B is `food`** (server too: `ChargeKind.Food`, migration 4258).

## Commits

```
efe8fbf9 ACC-2 parity: test expectations for the rulings (cash advance leaves the drawer, food charge kind, mappedBy line:food, advanceRecoveries wire)
3ecb6d68 ACC-2 parity: coordinator rulings — HR_ADVANCE_ADD funding (default Cash …), pay run / net pay dated the period's last day …, server folio charge kind food (migration 4258) …, advanceRecoveries wire {staffId, paise}
761cd9ba ACC-2 parity server: per-staff advance recovery on PayrollPosted (Cr 1200/STF-<id>), reversal keeps parties, line:food default; BackfillUdhaarCredits; acc.ledger_month_balance (migration 4257) …; reports accept any range; tests
f5de8c70 ACC-2 parity: production report readers for /accounting/statement, /trial-balance?from, /day-book over any range (sync/accounting-reports.ts + hooks)
6eeee7b7 ACC-2 parity: prototype payroll vouchers … mirroring the server keys; Line.kind food …; ACC-RULEBOOK line:service override removed; tests stream-accounting-parity
```
(+ the commit carrying this file and the last POS test fix.)

## Files

**Prototype (`03 Prototype`)**
- new `src/domain/streams/accounting-payroll.ts` — `accPayrollSources`, `accPayRunLines`, `accFnfLines`, `accPayrollDate`, `accPayrollDates`, `accPayrollKeyUses`, `ACC_PAYROLL_MAP`, `ACC_PAYROLL_FUNDING`.
- `src/domain/streams/accounting.ts` — import; `ACC_DEFAULT_MAP` + `line:food` and the payroll keys; `KEY_LABEL` + `payroll`; `ACC_KEY_PATTERN` + `payroll`; the **ACC-2 parity section** of `accLiveVouchers` (after "end Wave B1", before the cash difference); `accDates` / `accMapRows` one line each; `accReconcile` service check adds the `food` lines.
- `src/domain/streams/staff.ts` — optional `HRAdvance.funding`, `HRRun.lockedOn`, `HRBankFile.on`, `HRUnlock.on / lockedOn / book`, `HRFnf.confirmedOn`; `HRRunBook` + `hrRunBook(run)`; `hrOn`.
- `src/services/mock/handlers/staff.ts` — `HR_ADVANCE_ADD` `funding` (ruling 1); `HR_PAYROLL_LOCK` stamps `lockedOn`; `HR_PAYROLL_UNLOCK` keeps `{on, lockedOn, book}`; `HR_PAYROLL_BANK_FILE` stamps `on`; `HR_FNF_CONFIRM` stamps `confirmedOn`.
- `src/domain/streams/cashout.ts` (`staff-advance` drawer movement), `src/domain/streams/closing.ts` (`transfers` adds `staff-advance`; the report's service revenue keeps `food` lines), `src/domain/gstr1.ts`, `src/domain/streams/catalogue.ts`, `src/clients/sync/money/wire.ts` (`LINE_KIND.food`).
- `src/services/mock/handlers/pos.ts` (charge-to-folio → `food`), `handlers/catalogue.ts` (extra of GST class `food` → `food`).
- `src/fixtures/history/generate.ts` (FY26 room-service folio lines are `food`), `src/fixtures/ext/accounting-rulebook.ts` (`line:service → 4200` override **removed**).
- `src/services/mock/index.ts` — one line in the `// ACC-2` block.
- new `src/clients/sync/accounting-reports.ts`, `src/clients/sync/accounting-reports-hooks.ts`; `src/clients/sync/index.ts` one export line.
- tests: new `tests/stream-accounting-parity.mjs` (10), `tests/sync-accounting-reports.mjs` (3); adjusted, each marked `ACC-2`: `tests/_rulebook-month.mjs` (`PROTOTYPE = DRIVEN + T13a`), `tests/stream-accounting-rulebook.mjs`, `tests/stream-accounting-chart.mjs`, `tests/stream-accounting-parties.mjs`, `tests/stream-history-c.mjs`, `tests/stream-pos.mjs`, `tests/stream-staff.mjs`, `tests/sync-accounting.mjs`.

**Server (`08 Production`)**
- `src/Hcc.Contracts/Accounting/AccountingContracts.cs` — `PayrollPostedEvent` + `AdvanceRecoveries` (`AdvanceRecoveryLine(StaffId, Paise)`), `LockedOn`; `PayrollPaidEvent` + `PaidOn`.
- `src/Hcc.Domain/Accounting/AccountingSource.cs` — `PayRunFigures.AdvanceRecoveries: (StaffId, Paise)[]?`; `SourceRules.PayRun` credits `payroll:advances` per staff (party `STF-<id>`, staff-id order; an unnamed rest stays on the control); `SourceRules.AdvancesKey`; `PayRunReversal` keeps each entry's party.
- `src/Hcc.Application/Payroll/PayrollService.Pay.cs` (per-staff recoveries, period-end dating), `src/Hcc.Application/Accounting/AccountingEventMapper.cs` (passes the split, narrations `· locked / · paid`, line label `food`).
- F&B charge kind: `src/Hcc.Domain/Billing/BillingRules.cs` (`ChargeKind.Food`, code `food`), `src/Hcc.Application/Pos/BillingPosFolioPort.cs`, `src/Hcc.Application/Catalogue/CatalogueCommands.cs` (extras of class food; meal-plan lines stay service, as the prototype), `src/Hcc.Domain/Reporting/DailyPackCalculator.cs` (food inside service revenue), `src/Hcc.Domain/Groups/GroupRules.cs` (routes as extras), `src/Hcc.Domain/Accounting/Reconciliation.cs`, `src/Hcc.Infrastructure.SqlServer/Accounting/SqlAccountingStore.cs` (operational figures), `src/Hcc.Domain/Tax/GstMaster.cs`, `src/Hcc.Domain/Accounting/ChartOfAccounts.cs` (`line:food → 4200`).
- new `src/Hcc.Contracts/Accounting/AccountingParityContracts.cs`, `src/Hcc.Application/Accounting/UdhaarCreditBackfill.cs`, `src/Hcc.Infrastructure.SqlServer/Accounting/SqlUdhaarBackfillStore.cs`, `src/Hcc.Api/Endpoints/AccountingParityEndpoints.cs`.
- new `src/Hcc.Application/Accounting/LedgerMonthBalances.cs`, `src/Hcc.Infrastructure.SqlServer/Accounting/SqlLedgerMonthBalanceStore.cs`; `src/Hcc.Application/Accounting/AccountingReportQueries.cs`; `src/Hcc.Api/Endpoints/AccountingReportEndpoints.cs`.
- `src/Hcc.Api/HccComposition.cs` — one line in the `// ACC-2` block.
- migrations `4257_acc_ledger_month_balance.sql`, `4258_bill_charge_kind_food.sql` (+ `migrations.lock.json` via `--add`, `Hcc.slnx`); 4255, 4256, 4259 unused (the pay-run tables needed nothing: recoveries per staff already live in `hr.advance_recovery`; the split travels in the event).
- tests: new `Hcc.Accounting.Tests/Domain/PayrollPartyTests.cs` (5), `Sql/MonthBalanceTests.cs` (2), `Sql/UdhaarBackfillTests.cs` (1), `Hcc.Payroll.Tests/Sql/PerStaffRecoveryTests.cs` (1); adjusted `Sql/ReportQueriesSqlTests.cs` (366-day refusal → any range), `Sql/SqlChartStoreTests.cs` (4200 mapped by `line:food`, `pos:sales`), `Hcc.Pos.Tests/Sql/PosCommandTests.cs` (folio line kind `food`).

## What was built

1. **Prototype payroll vouchers (contract §8, rulebook D1–D4, T13a–c)** — server keys / sides:
   - `advance:<id>` Payment on the advance date: Dr `payroll:advances` (1200/<staff>) · Cr `funding:<funding>` (Cash → 1000/<till>; bank → 1030/<bank>).
   - `payroll:<run>[/lock<n>]` Journal dated the period's last day (or the lock day if earlier): Dr 5100 gross · Dr 5110 employer PF · Dr 5110 employer ESIC · Cr 2300 net · Cr 2310 PF · Cr 2320 ESIC · Cr 2330 PT · **Cr 1200/<staff> per staff** · Cr 2340 other.
   - `payroll-paid:<run>[/lock<n>]` Payment (first bank file of the lock; re-issue is no second payment), dated as the pay run: Dr 2300 · Cr 1030/<bank>.
   - `payroll-unlock:<run>/lock<n>` Journal on the unlock date: exact swap of that lock's PayRun (bank-file flag; payment kept).
   - `fnf:<id>` + `fnf-paid:<id>` on the confirmation date; a negative net → Dr `payroll:fnf-recoverable` (1200/<leaver>, as the server).
   - Payroll keys in the mapping table (group "Payroll"), Owner-re-pointable. T13a / T13b / T13c reproduce the rulebook entries exactly (`stream-accounting-parity.mjs`; the ACC-RULEBOOK scenario now books T13a in cash).
2. **F&B line class** — `food` from POS charge-to-folio and extras of GST class `food` (prototype and server); `line:food → 4200` both sides; `service` lines unchanged; reports keep food inside "service revenue"; reconciliation checks both keys; the ACC-RULEBOOK override is gone (4200 still ₹1,60,000).
3. **Server per-staff recovery** — `advanceRecoveries` → one `Cr 1200/STF-<id>` per staff; no `no-party` warning on 1200 for new pay runs (older events keep the control line + warning); reversals keep parties.
4. **`BackfillUdhaarCredits`** — `POST /commands/BackfillUdhaarCredits` (Owner, PERM-ACC-003, idempotent): publishes `UdhaarCreditRaised` (dated the credit's date) for credits with neither the event in `plat.outbox` nor the source `udhaar:<id>`; kind absent → other revenue, no GST (CA note).
5. **Report readers beyond 62 days** — `loadAccStatement / loadAccTrialBalance6 / loadAccDayBook` (one request, any range, no device derivation, contract §6 shapes); hooks `useAccStatement / useAccTrialBalance6 / useAccDayBook / useMayReadAccReports`.
6. **Aggregated opening read** — `acc.ledger_month` + `acc.ledger_month_balance` (4257): a closed month whose every source day is locked is summed once from the lock snapshots (lazily, fingerprint = count + Σ lock ids → rebuilt after an unlock / re-lock); opening = stored months + days after them; the three report endpoints accept any range.

## Tests (all through `tools/Invoke-Serialized.ps1`, label `acc2-parity`; logs in `.scratch/`)

- Prototype `npx tsc --noEmit`: clean. `npm test`: **before 1948/1948 → after 1944/1960** (+12 new: 10 `[ACC2-PARITY]`, 3 `[ACC2-PARITY-SYNC]`, minus nothing removed). The **16 failures are all expected and the integrator's**: the FY26 hash pins `[CAT] [FDM] [MFX]×2 [MST] [OPSM] [PPL] [POS-MST] [RMS] [PERF]`, `[PERF-R]`×5 (FY26 report-pack pins) and `[GOLDEN] accounting-golden.json` (default mapping gained `line:food` + payroll keys). BASE hash unchanged.
- Server `dotnet build Hcc.slnx`: 0 warnings, 0 errors. `dotnet test` (projects touched): `Hcc.Accounting.Tests` 155/155 (was 147; +8), `Hcc.Payroll.Tests` 64/64 (was 63; +1), `Hcc.Pos.Tests` 68/68, `Hcc.Catalogue.Tests` 15/15, `Hcc.Groups.Tests` 45/45, `Hcc.Closing.Tests` 68/68. Not run (integrator's full suite): the other 29 projects — the `food` charge kind may touch `Hcc.Billing.Tests` / `Hcc.Invoices.Tests` / `Hcc.Platform.Tests` goldens only if they post POS-to-folio or food extras.
- `node tools/migrations-lock.mjs`: in step (98 migrations).

## Open items

1. **Server cash advances**: the server `GiveAdvanceRequest` has no funding yet (it publishes `AdvanceFunding = "Bank transfer"`); ruling 1 is built on the prototype. Server parity needs `Funding` on the request, the till cash movement (`ICashDrawerPort`) and the §7 cash guard (flows lane) — next wave or the integrator.
2. **D4 negative F&F**: rulebook says → 1210; server (DEC-074) and now the prototype map `payroll:fnf-recoverable → 1200/<leaver>`. Left as built (Owner-re-pointable); CA / coordinator to rule.
3. Pay-run vouchers dated at month end land in a **locked** month-end day as drift when that day was locked before the pay run (prototype: drift warning; server: source on a locked date, kept out of the snapshot). The month lock of acc2-gst should require the pay run (or the Owner unlocks the day) — coordinator to sequence.
4. Old prototype unlocks (before ACC-2) carry no `book` / `lockedOn`: not re-derived live (none in the fixtures).
5. Production accounting screens still derive the books from the 62-day slice until the UI wiring below is applied.

## Integration needed

1. **FY26 pins move** (payroll vouchers in the FY26 locked months; room-service lines `food`); re-pin once at the end. At this lane's head: FY26 history `ac6c8544…` → `794297a22d186238f0edc5a8841db3fb8e92f2d0d89dc0170335ba6aea828476`; FY26 whole state `787656dd…` → `26c7f239ffb1e4934fe8f5b75e0c2f7b2bb6e9577e1e50ecc0d2cc29e0e8b6b1`; `[PERF-R]` (`tests/stream-perf-reports.mjs`): 04-01..10-24 `acd252d0980fa9df` → `d536e7f498c474a9`, 07-01..07-31 `a25f322e76d994e2` → `14a5817de8d17a45`, 10-20..10-24 `4e71ef6202dd0836` → `ca11ac1026b2e09f`, 04-01..10-23 `f8f86a7bff682aae` → `c5d2c1956ec7d71f`, 04-10 `59535232569a3822` → `a6c522621789680a`. (Other lanes will move them again.)
2. **`accounting-golden.json`** regenerate (the server `GoldenAccountingTests` checks golden ⊆ server default mapping; the server has every key).
3. **acc2-tests**: the `line:service` override is gone from `src/fixtures/ext/accounting-rulebook.ts`; the scenario's day-8 `HR_ADVANCE_ADD` (no `funding` → Cash) now books T13a exactly (Dr 1200/HR-S07 · Cr 1000/TILL, out of the drawer). My edits to `tests/_rulebook-month.mjs` (`PROTOTYPE = [...DRIVEN,'T13a']`) and `tests/stream-accounting-rulebook.mjs` (`model(PROTOTYPE)`, 1000 = 40,300, Staff-07 5,000 Dr) are minimal — acc2-tests' version wins. T13b / T13c can be driven (`HR_PAYROLL_RUN` April + `HR_PAYROLL_LOCK` by the other payroll user + `HR_PAYROLL_BANK_FILE`): both vouchers are dated 30 April. The server `RulebookMonthSqlTests` / `build-rulebook-month.mjs` still set the `line:service` override (acc2-tests' files) — no longer needed once POS-to-folio posts `food`.
4. **UI** (`03 Prototype/src/features/accounting/books.tsx`, production branches as in `features/reports/Reports.tsx`):
   ```tsx
   import {useAccTrialBalance6,useAccStatement,useAccDayBook} from '../../clients/sync/accounting-reports-hooks';
   // TrialBalanceView
   const local=useMemo(()=>accTrialBalance6(s,range.from,range.to,detail),[s,range,detail]);
   const srv=process.env.HCC_BUILD==='production'?useAccTrialBalance6(range.from,range.to,detail):undefined; // eslint-disable-line react-hooks/rules-of-hooks
   const tb=srv?.data??local;
   // StatementView
   const local=useMemo(()=>account?accStatement(s,account,range.from,range.to):null,[s,account,range]);
   const srv=process.env.HCC_BUILD==='production'?useAccStatement(account,range.from,range.to,!!account):undefined; // eslint-disable-line react-hooks/rules-of-hooks
   const st=account?(srv?.data??local):null;
   // DayBookView
   const local=useMemo(()=>accDayBook(s,range.from,range.to),[s,range]);
   const srv=process.env.HCC_BUILD==='production'?useAccDayBook(range.from,range.to):undefined; // eslint-disable-line react-hooks/rules-of-hooks
   const db=srv?.data??local,rows=db.rows;
   ```
   plus a loading / "as of" note while `srv.status!=='ready'`. And `src/features/closing/Cashier.tsx` `MOVE_KIND`: add `'staff-advance':'Salary advance'`. The advance form (`features/staff`) should offer the payment method (`funding`, default Cash).
5. Optional: `src/clients/sync/reads.ts` READS + `tests/_role-reads.json` may list `GET /accounting/statement`, `GET /accounting/day-book` (PERM-ACC-001); the hooks use the trial-balance rule meanwhile. Production sync for `HR_ADVANCE_ADD` should send `funding` once the server takes it (open item 1).
6. Optional server composition: `services.AddSingleton<ILedgerMonthBalanceStore>(sp => new SqlLedgerMonthBalanceStore(sp.GetRequiredService<SqlPlatform>().Connections));` (the report endpoint builds it itself otherwise).
7. Merge notes: `accounting.ts` — my section sits between "end Wave B1" and the cash difference; `AccountingSource.cs` `PayRun` / `PayRunFigures` / `PayRunReversal` rewritten; `AccountingReportQueries` constructor gained two trailing optional parameters; migrations 4257 / 4258 are in the lock after 4221 (other lanes' 423x–425x will need `--accept-out-of-order` or re-adding in merge order).

## HANDOFF bullet

- **ACC-2 parity (acc2-parity)**: the prototype books payroll like the server — advance (`HR_ADVANCE_ADD funding`, Cash by default, out of the drawer), pay run with per-staff advance recovery 1200/<staff> and net pay both dated the period's last day, unlock reversal, F&F — T13a–c exact; F&B on folios is kind `food` → `line:food` 4200 on both sides (server `ChargeKind.Food`, migration 4258; ACC-RULEBOOK override dropped); server pay runs send `advanceRecoveries [{staffId, paise}]`; `BackfillUdhaarCredits` (Owner, idempotent); `acc.ledger_month_balance` (migration 4257) feeds the report opening and statement / six-column TB / day book read any range (production readers `sync/accounting-reports.ts`). FY26 pins move (integrator re-pins). Open: server cash advances, D4 1210 vs 1200, month-end pay run vs an already locked day.
