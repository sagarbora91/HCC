# F5-A1 DONE — acc4-year + acc4-bank

Lane A1 / Opus / branch `claude/f5-a1` / worktree `/home/user/wt-f5-a1` / base `fd9a0aec` (claude/f5 = main + F5 contracts). Contract `02 Planning/agents/ACC4-CONTRACT.md`.

## Commits
```
4b975259 F5-A1: prototype year lock, carry-forward, reopen + bank reconciliation domain and handlers
aa986034 F5-A1: stream-acc4-bank tests (golden reconciliation to zero, refusals, B8, layouts)
9892151f F5-A1: server year lock rules + service, bank reconciliation rules, YearLock/ and Bank/ tests
4cdab928 F5-A1: migrations 4280-4289, 4293 (...) + layout import template
ab22e53a F5-A1: carry-overs C1/C4 (returns allocate to their own bill first, server) and C6 test
(+ this DONE file)
```

## Files
- Prototype (new): `src/domain/streams/accounting-year.ts`, `accounting-bank.ts`, `src/fixtures/ext/bank-layouts.ts`, `src/services/mock/handlers/accounting-year.ts`, `accounting-bank.ts`; tests `tests/stream-acc4-year.mjs`, `tests/stream-acc4-bank.mjs`, fixture `tests/fixtures/bank-april-2026.csv` (fictional).
- Prototype (shared, `// F5-A1` blocks): `src/domain/types.ts` (slices `accYearX`, `accBankX`), `src/domain/streams/accounting.ts` (import + `accVouchersUpTo` adds approved year-close vouchers), `src/services/mock/handlers/index.ts` (registration + year-lock guard wrapper), `src/services/mock/index.ts` (exports), `src/domain/streams/rbac.ts` (5 helpers).
- Server (new): `src/Hcc.Application/Accounting/YearLock.cs` (rules, `IYearLockStore`, `YearLockService`), `BankRecon.cs` (rules: parse / match / settlement / recon / clearing / CSV); migrations `4280`–`4289`, `4293` (+ `migrations.lock.json`, `Hcc.slnx` F5-A1 block); `deployment/import-templates/bank-statement-layouts.csv`.
- Server (edited): `Hcc.Infrastructure.SqlServer/Accounting/SqlAccountingStore.Ageing.cs` (C1/C4, F5-A1 block).
- Server tests: `Hcc.Accounting.Tests/YearLock/YearLockTests.cs`, `Bank/BankReconTests.cs`, `Carry/Acc4CarryTests.cs`, csproj links the prototype CSV as `Bank/bank-april-2026.csv`; `Hcc.Expenses.Tests/Sql/PurchaseSqlTests.cs` (+1 F5-A1 fact).

## What was built
- **Year (§1):** `ACC_YEAR_PREVIEW` (checklist: opening approved, every activity day locked and FY ended, every active month GST-locked, 1400 / 9999 zero, 1010 / 1020 zero, nothing pending), `ACC_YEAR_CARRY_FORWARD` (draft, generated from the closing TB with party detail; P&L and 3000 closed to 3100; `ACC_CF_UNBALANCED`), `ACC_YEAR_LOCK` (Owner; `ACC_YEAR_NOT_READY`; draft must equal a fresh regeneration; approves it as the fy+1 opening), `ACC_YEAR_REOPEN` (Owner, reason ≥ 10, latest locked year only, refused once fy+1 has a locked month → `ACC_YEAR_REOPEN_BLOCKED`; `provisionalExports`), `ACC_YEAR_RELOCK` (regenerates, old → superseded, fy+1 opening date flagged for re-export, DEC-077). The approved carry-forward posts as an opening-kind voucher `JRN/<1 Apr>/YC` (sub `year-close`), so fy+1 opens exactly on the carry-forward lines and fy+1's P&L / day book never see it. After a lock every command whose `date` / `month` falls in the year is refused `ACC_YEAR_LOCKED` (day unlock and month unlock included); `ACC_MANUAL_SAVE` keeps `refersToFy` (G4). Server mirror: `YearLockRules` + `YearLockService` (same figures; codes as `details.reason`).
- **Bank (§2):** layout master (seed Generic, A23; `BANK_LAYOUT_UPSERT` Owner), `BANK_IMPORT` (CSV with any delimiter / date format / debit-credit or amount + Dr/Cr; formula guard; `BANK_DUP_FILE`, `BANK_BALANCE_BREAK` with the row, locked-year rows refused, overlapping rows skipped unless kept), `BANK_AUTO_MATCH` (rule 1 ref ±7 days, rule 2 amount + 0…+3 days single candidate, ambiguous left, rule 3 settlement rows proposed), `BANK_MATCH` (manual, `BANK_MATCH_SUM`), `BANK_UNMATCH` (until month lock), `BANK_IGNORE`, `BANK_CREATE_VOUCHER` (charges / interest as approved manual vouchers, 9999 refused), `BANK_SETTLEMENT_POST` (B8: Dr bank net · Dr 5080 MDR(+GST unless ITC → 1310/1320) · Cr 1010/1020 gross; `BANK_SETTLEMENT_SUM`). Selectors: `accBankClearedDate`, `accBankClearing` (FIFO open tender days, amber > 3, red > 7), `accBankRecon` + `accBankReconCsv`. Server mirror: `BankReconRules`.
- **Carry-overs:** C1 / C4 server — a posted return allocates explicitly to its own bill (ageing and Tally share `SqlAgeingAllocationSource`). C6 — verified the rate is already read on the voucher date on both sides (`TdsCalculator.ForBill` / `tdsForBill`); test added. C5 — already in place (`GstMonthService.MonthAsync` shows the pay-run blocker, line ~370). C2, C3 — not done (see open items).

## Tests
- `node tests/_one.mjs tests/stream-acc4-year.mjs` 5/5; `tests/stream-acc4-bank.mjs` 5/5. Golden: April 2026 as the FY → carry-forward = §8.5 (Dr = Cr = ₹79,27,900; 3100 ₹57,95,400 Cr; profit ₹1,40,400; drawings ₹50,000); bank statement reconciles to difference 0 (books ₹5,01,561 + cheque ₹45,000 not presented = statement ₹5,46,561), 1010 / 1020 zero.
- `dotnet test Hcc.Accounting.Tests --filter YearLock|Bank|Acc4Carry` 16/16; `ManualVoucherSqlTests|OpeningBalanceSqlTests` 3/3 (migrations apply on the SQL test server); `TallyFileSqlTests|Ageing` 20/20; `Hcc.Expenses.Tests PurchaseSqlTests` 9/9. `dotnet build Hcc.slnx` 0 warnings, 0 errors. `node tools/migrations-lock.mjs` in step (181).
- Full prototype run: `npx tsc --noEmit` clean; `npm test` 4 passed, 11 failed — all 11 the RBAC command matrix missing the new commands; fixed (13 rows in `tests/stream-rbac-matrix.mjs`, F5-A1 block), that suite then 62/62. Full suite not re-run after the fix.

## Open items
- **C2** (server GST tie should compare the *posted set-off journal*) and **C3** (`AgeingQueries` reads the as-of window) not done — ACC-3 C9 `StaleSetoff` already compares the stored set-off record; the journal comparison and the windowed ageing read are left for f5-int (4290–4292 unused).
- Server **SQL stores and endpoints** for `IYearLockStore` and the bank tables are not written (rules + service + migrations are); no `HccComposition` / `clients/sync/registry.ts` lines yet. Prototype is complete end to end.
- Prototype error codes are message prefixes (`ACC_YEAR_LOCKED: …`) on existing `ErrorCode`s (no change to `errors.ts`); server uses `details.reason`.
- Prototype file hash = FNV-1a 64 (server SHA-256). Month-lock check counts only months with activity; credit-note "pending" check reads pending approvals whose kind mentions credit.
- Exports do not yet print `ACC_PROVISIONAL_TEXT`: selector `accYearProvisional(s,date)` / `YearLockRules.Provisional` exist.
- Migration 4288 adds template `bank-settlement`; the C# manual-voucher template list is unchanged (prototype uses `journal` for B8).

## Integration needed
- U1: UI reads `accYearPreview`, `accYearOf`, `accYearSlice`, `accBankLayouts`, `accBankRecon`, `accBankClearing`, `accBankAutoMatch`, `accBankBookLines`; permissions `rbacCanYearPrepare / YearLock / BankReconcile / BankLayouts / SeeClearing`. Fixture `tests/fixtures/bank-april-2026.csv` is created here (U1's row in F5_Cloud_Lanes) — reuse, do not duplicate.
- f5-int: wire exports (`accounting-exports.ts`, `accounting-tally.ts`, server export endpoints) to stamp `PROVISIONAL — year reopened` when `accYearProvisional` / `YearLockRules.Provisional` is true; SQL `IYearLockStore` + bank store + endpoints + `HccComposition.cs` F5-A1 block; server posting paths call `YearLockRules.GuardPost` (manual vouchers, day unlock, month unlock); permission-matrix test rows for PERM-ACC-014…017.

## HANDOFF bullet
- **F5-A1 (acc4-year + acc4-bank, 2 Nov 2026):** year lock / carry-forward / reopen / relock and bank reconciliation (layout master, CSV import, auto + manual match, B8 settlement, clearing view, reconciliation report) on the prototype end to end, server rules + service + migrations 4280–4289 / 4293; goldens: April 2026 → FY27 opening = §8.5, bank April statement → difference 0. C1/C4/C6 done (C5 already in place); C2/C3 and server SQL stores / endpoints left to f5-int (`05 Delivery Evidence/F5/F5-A1-DONE.md`).
