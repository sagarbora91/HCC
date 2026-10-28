# ACC2-PARITY DONE

Lane **acc2-parity** (Opus) · branch `claude/acc2-parity` · worktree `E:\Projects\HCC\worktrees\acc2-parity` · base `b78cb069` (ACC-2 wave setup).
Contract: `02 Planning/agents/ACC2-POSTING-CONTRACT.md` §8 (payroll, F&B line class, Udhaar back-fill, report readers, aggregated opening), §10 (migrations 4255–4259).

## Commits

COMMITS_PLACEHOLDER

## Files

**Prototype (`03 Prototype`)**
- new `src/domain/streams/accounting-payroll.ts` — the payroll sources (`accPayrollSources`, `accPayRunLines`, `accFnfLines`, `accPayrollDates`, `accPayrollKeyUses`, `ACC_PAYROLL_MAP`, `ACC_PAYROLL_FUNDING`).
- `src/domain/streams/accounting.ts` — import; `ACC_DEFAULT_MAP` + `line:food` and the payroll keys; `KEY_LABEL` + `payroll`; `ACC_KEY_PATTERN` + `payroll`; the **ACC-2 parity section** in `accLiveVouchers` (after "end Wave B1", before the cash difference); `accDates` + payroll dates; `accMapRows` + payroll uses; `accReconcile` service check adds the `food` lines.
- `src/domain/streams/staff.ts` — optional business-date stamps `HRRun.lockedOn`, `HRBankFile.on`, `HRUnlock.on / lockedOn / book`, `HRFnf.confirmedOn`; `HRRunBook` + `hrRunBook(run)` (figures per lock, recoveries per staff); `hrOn`.
- `src/services/mock/handlers/staff.ts` — `HR_PAYROLL_LOCK` stamps `lockedOn`; `HR_PAYROLL_UNLOCK` keeps `{on, lockedOn, book}` of the undone lock; `HR_PAYROLL_BANK_FILE` stamps `on`; `HR_FNF_CONFIRM` stamps `confirmedOn`.
- `src/services/mock/handlers/pos.ts` (POS charge-to-folio → `kind:'food'`), `handlers/catalogue.ts` (`CAT_CHARGE` of GST class `food` → `kind:'food'`).
- `src/domain/streams/closing.ts` (report "service revenue" keeps `food` lines, as the server), `src/domain/gstr1.ts` (food line = a service line for the SAC), `src/domain/streams/catalogue.ts` (`catChargeSplit` counts food lines), `src/clients/sync/money/wire.ts` (`LINE_KIND.food`).
- `src/fixtures/history/generate.ts` — FY26 room-service POS lines are `food` (as the live command).
- `src/fixtures/ext/accounting-rulebook.ts` — the ACC-1 `ACC_MAP_SET line:service → 4200` override **removed**.
- `src/services/mock/index.ts` — one line in the `// ACC-2` block (`export * from accounting-payroll`).
- new `src/clients/sync/accounting-reports.ts` (production readers) + `src/clients/sync/accounting-reports-hooks.ts` (React hooks); `src/clients/sync/index.ts` one export line.
- tests: new `tests/stream-accounting-parity.mjs` (10), new `tests/sync-accounting-reports.mjs` (3); adjusted (behaviour moved on purpose, each marked `ACC-2 parity`): `tests/_rulebook-month.mjs` (`HCC_STEP['T13a-bank']`, `PROTOTYPE`), `tests/stream-accounting-rulebook.mjs`, `tests/stream-accounting-chart.mjs`, `tests/stream-accounting-parties.mjs`, `tests/stream-accounting-reports.mjs`, `tests/stream-history-c.mjs`, `tests/stream-pos.mjs`, `tests/sync-accounting.mjs`.

**Server (`08 Production`)**
- `src/Hcc.Contracts/Accounting/AccountingContracts.cs` — `PayrollPostedEvent` gains `AdvanceRecoveries` (`AdvanceRecoveryLine(StaffId, AmountPaise)[]`, optional).
- `src/Hcc.Domain/Accounting/AccountingSource.cs` — `PayRunFigures.AdvanceRecoveries: (StaffId, Paise)[]?`; `SourceRules.PayRun` credits `payroll:advances` per staff (party `STF-<id>`, staff-id order; an unnamed rest stays on the control); `SourceRules.AdvancesKey`; `PayRunReversal` keeps each entry's party.
- `src/Hcc.Domain/Accounting/ChartOfAccounts.cs` — `line:food → 4200` default.
- `src/Hcc.Application/Payroll/PayrollService.Pay.cs` — the lock publishes the per-staff recoveries; `src/Hcc.Application/Accounting/AccountingEventMapper.cs` passes them.
- new `src/Hcc.Contracts/Accounting/AccountingParityContracts.cs`, `src/Hcc.Application/Accounting/UdhaarCreditBackfill.cs`, `src/Hcc.Infrastructure.SqlServer/Accounting/SqlUdhaarBackfillStore.cs`, `src/Hcc.Api/Endpoints/AccountingParityEndpoints.cs` (`POST /commands/BackfillUdhaarCredits`).
- new `src/Hcc.Application/Accounting/LedgerMonthBalances.cs`, `src/Hcc.Infrastructure.SqlServer/Accounting/SqlLedgerMonthBalanceStore.cs`; `src/Hcc.Application/Accounting/AccountingReportQueries.cs` (month opening, any range); `src/Hcc.Api/Endpoints/AccountingReportEndpoints.cs` (composes the month store).
- `src/Hcc.Api/HccComposition.cs` — one line in the `// ACC-2` block (`api.MapAccountingParityEndpoints();`).
- migration `database/migrations/4257_acc_ledger_month_balance.sql` (+ `migrations.lock.json` via `--add`, `Hcc.slnx`). 4255, 4256, 4258, 4259 unused (the pay-run table needed nothing: recoveries per staff were already in `hr.advance_recovery`; the per-staff split travels in the event payload).
- tests: new `tests/Hcc.Accounting.Tests/Domain/PayrollPartyTests.cs` (4), `Sql/MonthBalanceTests.cs` (2), `Sql/UdhaarBackfillTests.cs` (1), `tests/Hcc.Payroll.Tests/Sql/PerStaffRecoveryTests.cs` (1); adjusted `Sql/ReportQueriesSqlTests.cs` (the 366-day refusal → any range).

## What was built

1. **Prototype payroll vouchers (contract §8, rulebook D1–D4, T13a–c)** — same source keys, mapping keys and sides as the server:
   - `advance:<id>` Payment on the advance date: Dr `payroll:advances` (1200/<staff>) · Cr `funding:Bank transfer` (the bank it settles to) — HCC advances are bank transfers (server `PayrollService.AdvanceFunding`).
   - `payroll:<run>[/lock<n>]` Journal on the lock's business date: Dr 5100 gross · Dr 5110 employer PF · Dr 5110 employer ESIC · Cr 2300 net · Cr 2310 PF (ee + er) · Cr 2320 ESIC (ee + er) · Cr 2330 PT · **Cr 1200/<staff> per staff** · Cr 2340 other.
   - `payroll-paid:<run>[/lock<n>]` Payment on the date of the lock's first bank file (a re-issue is not a second payment): Dr 2300 · Cr bank.
   - `payroll-unlock:<run>/lock<n>` Journal on the unlock date: the exact swap of that lock's PayRun (bank-file flag in the narration; the payment voucher is kept).
   - `fnf:<id>` Journal + `fnf-paid:<id>` Payment (net > 0) on the confirmation date; a negative net debits `payroll:fnf-recoverable` (1200/<leaver>, as the server).
   - Payroll keys in the mapping table (group "Payroll"), Owner-re-pointable.
2. **F&B line class** — `Line.kind 'food'` from POS charge-to-folio and catalogue extras of GST class `food`; `line:food → 4200` default (prototype and server); `service` lines unchanged; the revenue report keeps food inside "service revenue" (as the server's report, which still sees a service charge), the reconciliation checks both keys; the ACC-RULEBOOK override is gone (its 4200 still ₹1,60,000).
3. **Server per-staff advance recovery** — `PayrollPostedEvent.AdvanceRecoveries` → `PayRunFigures.AdvanceRecoveries` → one `Cr 1200/STF-<id>` per staff; the 1200 control no longer carries the `no-party` warning for new pay runs (old events keep the control line + warning).
4. **`BackfillUdhaarCredits`** (Owner, PERM-ACC-003, idempotent) — publishes `UdhaarCreditRaised` (dated the credit's own date) for every Udhaar credit with neither the event in `plat.outbox` nor the sale source `udhaar:<id>`; kind absent → other revenue, no GST (CA note).
5. **Report readers beyond 62 days** — `loadAccStatement / loadAccTrialBalance6 / loadAccDayBook` read `/accounting/statement`, `/trial-balance?from&to&detail`, `/day-book` for ANY range (one request, no device derivation), mapped to the contract §6 shapes; hooks `useAccStatement / useAccTrialBalance6 / useAccDayBook / useMayReadAccReports`.
6. **Aggregated opening read** — `acc.ledger_month` + `acc.ledger_month_balance` (migration 4257): a closed month whose every day with sources is locked is summed once from the lock snapshots (lazily, on the first report that needs it; fingerprint = count + Σ lock ids of the month's current locks, so an unlock / re-lock rebuilds it); the report opening = stored months + the days after them. The three report endpoints accept any range (366-day cap removed).

## Tests

COUNTS_PLACEHOLDER

## Open items

1. **T13a cash vs bank**: the rulebook pays the ₹5,000 advance in cash (Cr 1000); HCC (server and now the prototype) books every advance as a bank transfer (`PayrollService.AdvanceFunding`). A cash advance needs a funding field, the drawer movement and the §7 cash guard (flows lane) — not built here. The ACC-RULEBOOK scenario therefore books `Dr 1200/HR-S07 · Cr 1030/BA-HDFC` (`tests/_rulebook-month.mjs` `HCC_STEP['T13a-bank']`); acc2-tests decides how the full month encodes T13a.
2. **D4 negative F&F**: the rulebook says "negative net → 1210"; the server (as built in DEC-074) and now the prototype map `payroll:fnf-recoverable → 1200` on the leaver's party. Left as built (owner-re-pointable mapping); CA / coordinator to rule.
3. The server still publishes POS charge-to-folio and catalogue extras as `service` charges (`BillingPosFolioPort`, `CatalogueCommands`); only the default `line:food → 4200` exists on the server. A server `food` charge kind (bill.charge kind CHECK, reports, GSTR-1) is a later wave; until then production F&B-on-folio lines book to 4100 unless the Owner maps them.
4. Old prototype unlocks (before ACC-2) carry no `book` / `lockedOn`: their PayRun and reversal are not re-derived live (locked snapshots keep them). No fixture has one.
5. The production accounting screens still derive the books from the 62-day slice until the UI wiring below is applied.

## Integration needed

1. **FY26 hash pins move** (payroll vouchers enter the FY26 locked months; room-service folio lines are `food`). Do not re-pin per lane; the integrator re-pins once. New values at this lane's head: PINS_PLACEHOLDER
2. **`accounting-golden.json`** must be regenerated (default mapping gains `line:food` and the payroll keys; the server `GoldenAccountingTests` checks golden ⊆ server mapping — the server has every key).
3. **acc2-tests** (ACC-RULEBOOK): the `line:service` override is gone in `src/fixtures/ext/accounting-rulebook.ts`; the scenario now books the day-8 advance (`advance:<id>`, Dr 1200/HR-S07 · Cr 1030/BA-HDFC). My edits to `tests/_rulebook-month.mjs` (`HCC_STEP`, `PROTOTYPE`, `model()` reading `HCC_STEP`) and `tests/stream-accounting-rulebook.mjs` (`model(PROTOTYPE)`, 1030 4,40,000, Staff-07 5,000 Dr) are minimal; acc2-tests' version wins at merge. T13b / T13c can now be driven in the prototype (`HR_PAYROLL_RUN` for April after 30 April, `HR_PAYROLL_LOCK` by the other payroll user, `HR_PAYROLL_BANK_FILE`). The server `RulebookMonthSqlTests` / `build-rulebook-month.mjs` still set the `line:service` override (acc2-tests' files).
4. **UI (acc2-ui / integrator)** — `03 Prototype/src/features/accounting/books.tsx`, production branches (pattern of `features/reports/Reports.tsx`):
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
   plus a "loading / as of" note when `srv.status!=='ready'` (as the Reports screen).
5. **Optional**: `src/clients/sync/reads.ts` READS + `tests/_role-reads.json` may list `GET /accounting/statement` and `GET /accounting/day-book` (PERM-ACC-001, same rule as the trial balance); the hooks gate on the trial-balance rule meanwhile.
6. **Server composition (optional)**: `services.AddSingleton<ILedgerMonthBalanceStore>(sp => new SqlLedgerMonthBalanceStore(sp.GetRequiredService<SqlPlatform>().Connections));` — the report endpoint falls back to building it itself.
7. Merge notes: `accounting.ts` — my section sits between "end Wave B1" and the cash difference; `accDates` / `accMapRows` / `accReconcile` one line each. `AccountingSource.cs` `PayRun` and `PayRunFigures` rewritten (other lanes add new rules elsewhere in `SourceRules`). `AccountingReportQueries` constructor gained two trailing optional parameters.

## HANDOFF bullet

- **ACC-2 parity (acc2-parity)**: the prototype books payroll like the server (advance, pay run with per-staff advance recovery 1200/<staff>, net pay at the first bank file, unlock reversal, F&F) — T13b / T13c exact; F&B on folios is `Line.kind 'food'` → `line:food` 4200 (ACC-RULEBOOK override dropped); server pay runs credit 1200/STF-<id> per staff (no more no-party warning), `BackfillUdhaarCredits` (Owner, idempotent), month rows `acc.ledger_month_balance` (migration 4257) feed the report opening, and statement / six-column TB / day book read any range (production readers `sync/accounting-reports.ts`). FY26 pins move (integrator re-pins). Open: T13a cash advance, D4 1210 vs 1200, server food charge kind.
