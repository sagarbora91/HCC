# ACC-2 integration notes (coordinator → integrator, 28 Oct 2026, ~19:00)

All eight lanes are done. Each DONE file in `E:\Projects\HCC\worktrees\acc2-<lane>\05 Delivery Evidence\Accounting\ACC2-<LANE>-DONE.md` has the exact edits; this file is the merged checklist plus the coordinator's rulings. ACC-1's notes (`ACC1_Integration_Notes.md`) describe the method.

## Lanes, last commits, counts in their own worktrees

| lane | last commit | prototype | server (projects run) | migrations | notes |
|---|---|---|---|---|---|
| purchase | aacf5b63 | 1960/1961 ([GOLDEN] only) | Acc 154, Expenses 64, MoneyMasters 24, Import 1968 | 4230–4235 | fixed the Purchase/Contra type-order drop; returns = expense records `return_of` |
| manual | f67fd5bd | 1962/1962 | Acc 156 | 4238–4241 | exports `accManualSave(k,input)`; 4238 re-creates both `acc.source` CHECKs |
| tds | 7c9bdd28 | 1958/1958 | Acc 155, Groups 46, Access 97 | 4242–4245 | golden regenerated (tax:tds-* keys) |
| gst | 78d2f6fb | 1970/1970 | Acc 159 | 4246–4249 | 4247 / 4249 conditional |
| flows | 525fc44c | 1961/1962 ([GOLDEN] only) | Acc 163, Ota 46, Groups 47 | 4250–4254 | **merge after manual** (needs 'ota' source kind) |
| parity | 0ee739b6 | 1944/1960 (9 FY26 pins + [PERF] + [PERF-R]×5 + [GOLDEN], on purpose) | Acc 155, Payroll 64, Pos 68, Catalogue 15, Groups 45, Closing 68 | 4257, 4258 | touched many shared files for the `food` kind |
| ui | 3f748bc7 | 1972/1972 | — | — | stubs `_contract-stubs-2.ts`; invented names listed in its DONE |
| tests | 35462c61 | 1937/1967 (waits for lanes) | Acc 153/175 (15 lane-dependent, 7 skipped) | — | full §8.2 month |

**Merge order:** purchase, manual, tds, gst, flows, parity, ui, tests. Expect overlaps in: `accounting.ts` (every lane has its own section in `accLiveVouchers`; purchase and manual both re-order the voucher types — keep one), the four `// ACC-2` append blocks, `stream-rbac-matrix.mjs` CMDS (keep every row once), `migrations.lock.json` / `Hcc.slnx` (purchase 4235 and manual 4238 both widen the `acc.source` CHECKs — keep both, 4238 drops and re-creates so order is safe), `ChartOfAccounts.DefaultMapping` + `ACC_DEFAULT_MAP` (gst `tax:*`, tds `tax:tds-*`, flows `receipt:ota*` / `tax:tcs` — keep all), the accounting golden (take any, regenerate once at the end), the tests lane's `_rulebook-month.mjs` / `stream-accounting-rulebook.mjs` (tests lane's version wins over parity's and purchase's edits; then re-apply purchase's "expense:501/502 are Purchase" and parity's T13a-in-cash if the tests lane did not already), `RulebookMonthTests` / `build-rulebook-month.mjs` / `RulebookMonthSqlTests` (drop the `line:service → 4200` override everywhere), `CMP_PROFILE_SET` (tds and gst both fix it — one fix: keep `gstOnAdvances`, `hotelDeductsTds`, `tdsDeductorType`).

## Coordinator rulings (apply; amend the contract / rulebook where named)

1. `ACC_MANUAL_SAVE` carries `voucherType` (contract §2 amended); the MDR settlement T14b is a **Journal** (rulebook B8 amended; Contra stays cash/bank only).
2. `ACC_LOAN_ADD {code, name, lender, accountLast4, kind}` → `2400/<CODE>`; loan party names in `accPartyLedgers` / `accPartyName` and `SqlReportPartyNames` (manual DONE item 3).
3. OTA check-out = `CHECKOUT {otaCredit:true}` → receipt kind `ota-credit`; server sync sends `SettleFolioToOta` before `CheckoutStay`.
4. `DEPOSIT_APPLY {booking, amount, source:'receipts'|'opening'}`; `totals()` counts `deposit-apply` as paid.
5. `HR_ADVANCE_ADD` has `funding` (default Cash); **server cash advances are still missing** — integrator completes: `GiveAdvanceRequest.Funding`, drawer movement kind `staff-advance`, the cash guard (parity DONE open item 1).
6. Pay-run vouchers dated the period's last day when that day is unlocked, else the business date of the lock; the month-lock blocker (gst's `accMonthLockBlocker` / server) gains "the month's pay run must be locked and paid".
7. Negative F&F posts to **1210/<leaver>**: make 1210 a control ledger (group `loans-advances`, parties = staff) in the chart on both sides (parity DONE open item 2).
8. Vendor opening balance = oldest open item `{expense:'opening'}` in ageing / allocations (built).
9. **TDS rate column follows the payee**: add `payeeType:'individual'|'other'` on `MnyVendor` (default `'other'`, hint from the PAN's 4th letter P/H = individual) and make `tdsForBill` / `TdsCalculator.ForBill` read it; `tdsDeductorType` stays as an unused field; contract §5 amended. **TDS rounds to the nearest rupee** (contract §5 amended). TDS remittance marks the month's TDS compliance card done with the challan (as gst does for PF / ESIC / PT). Catch-up of earlier untaxed bills on crossing the threshold is correct — keep.
10. Add `transport?: boolean` on `MnyCategory` (both sides) so the ₹35,000 cash limit applies (flows DONE).
11. **Accountant and vendor payments / OTA payouts:** do NOT grant the coarse `payments` permission to the Accountant (it would open guest receipts); the handlers `EXP_VENDOR_PAY`, `EXP_VENDOR_PAY_VOID`, `CHN_PAYOUT` accept role ∈ Owner / Manager / Accountant (prototype) and the server grants PERM-EXP-004 to ACC as well (seed row) — PERM-OTA-004 as is.
12. `GRPCompany.active?: boolean` added to the type (flows reads it through a cast today).
13. Server event / field names stay: `advanceRecoveries`, `tdsPaise`, `LockedOn` / `PaidOn`, `ChargeKind.Food`, OTA channel on `PartyContext`.

## Checklist after the merges (every item with its commit in ACC2-DONE.md)

Prototype
- Delete `src/features/accounting/_contract-stubs-2.ts`; re-point imports to the real selectors; rename or provide the UI's invented names (`accManualList`, `accManualProblems`, `expVendorOpenBills`, `expVendorAllocate`, `chnOpenReceivable`, `expPurchaseItcGuard`, … — list in the UI DONE) by thin wrappers over the lanes' exports; retarget the one UI test that referenced the stubs name.
- Replace every lane stub: `tdsForBill` (purchase → tds; it returns an object: use `.paise`), `accManualSave` (tds, gst → manual's export), cash-guard no-ops (manual, purchase → flows' `accCashGuard`); add `cashDeductionLost?` to `ACCManualVoucher` if the guard trips on manual payments.
- `CMP_PROFILE_SET` keeps the three switches (one fix).
- `ACC_UNLOCK` of a day inside a locked month is refused (gst item 2).
- Pickers use `grpActiveCompanies`; `MOVE_KIND['staff-advance']` label in `Cashier.tsx`; `books.tsx` on the production hooks; `mnySettlement` handles the new receipt kinds; capital purchases excluded from expense reports where budgets already exclude them (small; say what you did).
- Production sync: READS lines for the manual, gst, tds, vendor-payment, asset-register, payouts, statement / day-book loaders; regenerate `_role-reads.json`; remove the `as unknown as ReadRoute` cast; frontdesk CHECKOUT sends `SettleFolioToOta`; channel save sends `SetChannelItc`; `GRP_COMPANY_SAVE` sends `SetCompanyActive`; widen receipt kinds in `money/wire.ts`; read `cash_deduction_lost` on the wire; company-payment `tds` in `money.ts` / `wire.ts`; TDS rate changes in `admin.ts`.
- Contract §2, §5 and rulebook B8 amendments (rulings 1, 9).
- **Re-pin FY26 once at the very end** (parity moved them: history `794297a2…`, FY26 `26c7f239…`, plus the [PERF-R] values in parity's DONE — recompute after everything is merged; BASE must stay `aa7464e7…`); regenerate `accounting-golden.json` once.
- UI smoke: desktop click-through of every new tab / form with the real handlers and the phone views at 360 px → `05 Delivery Evidence/Accounting/ACC2-UI-Smoke.md`.

Server
- `HccComposition.cs`: `services.AddAccountingFlowsModule()`, the gst / manual / tds / purchase / flows endpoint lines (append block), `IGstManualVouchers` adapter and `ITdsRemittancePoster` over `ManualVoucherService`, `IReportPartyNames` extended with loans, month-store composition line.
- `Hcc.Access.Tests` `RbacV2MatrixTests` rows: PERM-ACC-010 / 011 / 012 / 013, PERM-EXP-004 (+ ACC grant per ruling 11).
- Channel view exposes `itc_allowed`; `GiveAdvanceRequest.Funding` + drawer movement + cash guard (ruling 5); `1210` control + parties (ruling 7); `payeeType` column + calculator (ruling 9); `transport` column (ruling 10).
- Migrations: 4230–4235, 4238–4241, 4242–4245, 4246–4249, 4250–4254, 4257, 4258 (+ any new ones for rulings 5, 7, 9, 10 in 4255 / 4256 / 4259); `migrations-lock.mjs --add --accept-out-of-order` as the merge order needs; every file in `Hcc.slnx`; `PermissionSeedCoverage` happy (`*_rbac_seed.sql`).
- Un-skip the tests lane's 7 server tests once their commands exist (T11 vendor payment, T14a / T14b / T16 / T17 / T18 manual vouchers, T19 OTA payout) and make the 15 lane-dependent ones pass.
- Regenerate `rulebook-month.json` with `build-rulebook-month.mjs` after the override is dropped.

## Suites and evidence

Through `tools/Invoke-Serialized.ps1` with `-Label acc2-integrator`: `npx tsc --noEmit`, `npm test` (expect 1948 + purchase 13 + manual 14 + tds 10 + gst 22 + flows 14 + parity 13 + ui 24 + tests 19 ≈ 2077, every one green, no skips), `npm run build`, `dotnet build Hcc.slnx`, then the **full** `dotnet test Hcc.slnx -maxcpucount:1 --logger trx` (3413 baseline + the lanes' new tests, all green). Never filter output; logs under `E:\Projects\HCC\worktrees\acc2\.scratch\integrator\`.

Then `05 Delivery Evidence/Accounting/ACC2-DONE.md` (merge order and conflicts, every checklist item with its commit, counts before / after, re-pinned hashes with reasons, the ACC-3 carry-overs, the HANDOFF bullets). The coordinator merges `main` in, runs `tools\Start-Gate.ps1 -Repo E:\Projects\HCC\worktrees\acc2 -Wave acc2` and fast-forwards `main`.
