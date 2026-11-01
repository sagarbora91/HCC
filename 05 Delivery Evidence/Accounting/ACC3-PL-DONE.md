# ACC3-PL DONE

Lane acc3-pl / Opus / branch `claude/acc3-pl` / worktree `E:\Projects\HCC\worktrees\acc3-pl` / base 829f0d91.

## Commits
- 4bc97aa4 report contract records PlView, BalanceSheetView, CashRatioView (contract §2) — first commit, for acc3-ageing to append to
- 4429144d P&L, balance sheet, cash ratio selectors + server PlQueries / endpoints + tests
- cafbcdc9 PlQueries using Hcc.Application.Billing (Caller)
- (this file)

## Files
- Prototype: new `03 Prototype/src/domain/streams/accounting-pl.ts`; `src/services/mock/index.ts` (new `// ACC-3` block + one export line); test `tests/stream-accounting-pl.mjs`.
- Server: `Hcc.Contracts/Accounting/AccountingReportContracts.cs` (appended §2 records — the file already existed from ACC-1); new `Hcc.Application/Accounting/PlQueries.cs` (`PlQueries` + pure `PlReports`); new `Hcc.Api/Endpoints/AccountingPlEndpoints.cs`; `Hcc.Api/HccComposition.cs` (new `// ACC-3` block + `api.MapAccountingPlEndpoints();`); test `tests/Hcc.Accounting.Tests/Pl/PlReportsTests.cs`.
- Not created: `SqlAccountingStore.Pl.cs` and migrations 4260–4261 — not needed (reports read the existing voucher derivation; no SQL view).

## What was built
- `accPl(s,from,to)` / `GET /accounting/pl?from&to` → `ACCPl` / `PlView`: revenue (4000/4100/4200/4300, 4900/4910 as negative lines), other income (4950/4960), expense buckets by code per contract §2, Owner-added ledgers by Tally group (Direct Expenses/Purchases → consumption, Indirect Expenses → other, Sales/Direct Incomes → revenue, Indirect Incomes → other income), unknown → other + warning; `byTallyGroup` by primary Tally group (income Cr−Dr, expense Dr−Cr).
- `accBalanceSheet(s,asOf)` / `GET /accounting/balance-sheet?asOf` → `ACCBalanceSheet` / `BalanceSheetView`: Owner's funds = 3100 + profit (P&L 1 April→asOf, line `PL`) − 3000 (+ `PL-EARLIER` line if earlier-year P&L is unclosed); 2400/2410 non-current; Duties & Taxes netted (net Dr → current asset "GST input credit"); fixed assets 15xx at ledger balance; contra-balances move to the other side's current block; `suspense` (9999) / `openingDifference` (1400) ≠ 0 → provisional.
- `accCashRatio(s,fy)` / `GET /accounting/cash-ratio?fy` → bps, flagged > 500.
- Provisional = opening pending (`accRptOpeningPending` / `OpeningPending`) or the range's active dates mix locked and unlocked days. Permission: `AccountingPermissions.View` as the trial balance.
- Helpers `accPlFyStart`, `accPlFyOf` (named so to avoid the existing `accFyStart` of accounting-opening.ts).

## Tests
- Prototype `npm test` (through the lock): before 2095 (main) → after **2101/2101** (+6 `[ACC3-PL]`); `npx tsc --noEmit` clean.
- Server `dotnet build Hcc.slnx`: 0 warnings, 0 errors. `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1`: before 235 → after **240/240** (+5 in `Pl/`).
- Golden (both suites): April 2026 profit ₹1,40,400 (sections 5,80,000 / 1,01,000 / 2,64,500 / 17,360 / 56,740); balance sheet ₹79,27,900 both sides. Prototype reads through the pay-run date (30 Apr or 1 May, as the rulebook test does).

## Open items / contract notes
1. **Contract vs rulebook (1340):** contract §2 says Duties & Taxes is "net of 1310–1350"; rulebook §8.5 shows duties ₹75,000 gross and 1340 GST cash ledger ₹100 as a separate asset (the golden ₹79,27,900 needs this). Implemented the rulebook: 1340 is NOT netted, it stays a current asset. Coordinator to amend contract §2.
2. **`accAssetWdv` does not exist** in `accounting-assets.ts` (base 829f0d91). Fixed assets use the 15xx ledger balances (= WDV, since depreciation F2 credits the blocks) — postings only, per §1. If acc3-carry adds `accAssetWdv`, the two should agree; no change needed here.
3. Optional `warnings?: string[]` / `Warnings` added to the P&L shape (contract: "goes to other with a warning" had no field).
4. Cash ratio definition: Receipt/Payment vouchers only, the money side (Dr of cash/bank ledgers on receipts, Cr on payments); cash = 1000/1005. Cash sales booked as Sales vouchers are not counted — CA to confirm.
5. Server `PlQueries` re-implements the opening load of `AccountingReportQueries.LoadAsync` (private there) without the month-balance store; reads every day from the start. Integrator may expose a shared loader.

## Integration needed
- None beyond the two appended blocks: `03 Prototype/src/services/mock/index.ts` and `08 Production/src/Hcc.Api/HccComposition.cs` had no `// ACC-3` block; this lane created it (other lanes append inside it — expect a trivial merge).
- acc3-ui / acc3-exports: names are `accPl`, `accBalanceSheet`, `accCashRatio` (types `ACCPl`, `ACCBalanceSheet`, `ACCCashRatio`).

## HANDOFF bullet
- ACC-3 acc3-pl: P&L (ICAI vertical + Tally groups), optional balance sheet and cash-ratio report on both sides (`accounting-pl.ts`, `PlQueries.cs`, `GET /accounting/pl|balance-sheet|cash-ratio`); golden April 2026 profit ₹1,40,400 and balance sheet ₹79,27,900 green in both suites (prototype 2101, Hcc.Accounting.Tests 240). Contract §2 1340-netting note to fix.
