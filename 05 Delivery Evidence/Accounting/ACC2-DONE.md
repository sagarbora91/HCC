# ACC-2 — integration DONE

Integrator: Opus. Worktree `E:\Projects\HCC\worktrees\acc2`, branch `claude/acc2` (start `4af78bb9`). Inputs: `02 Planning/agents/ACC2_Integration_Notes.md`,
`ACC2_Briefs.md`, `ACC2-POSTING-CONTRACT.md`, the eight lane DONE files.

## 1. Merges — all eight done, in order

| # | lane (tip) | merge commit | conflicts and how they were resolved |
|---|---|---|---|
| 1 | purchase `aacf5b63` | `b9b8f68d` | none |
| 2 | manual `f67fd5bd` | `085c8c6c` | append blocks (sync registry, handlers ACC2 object, mock barrel, HccComposition, events Consumed, projector), rbac CMDS, Hcc.slnx, lock (purchase kept, 4238–4241 re-added with `migrations-lock --add`), voucher type order kept once (both sides) |
| 3 | tds `7c9bdd28` | `e0d6805a` | append blocks, slnx, lock (4242–4245 --add); `tax:tds-payable` 2150 kept once (purchase line), `tax:tds-receivable` 1360 added — both sides |
| 4 | gst `78d2f6fb` | `2ec77900` | append blocks, rbac, slnx, lock (4246–4249 --add); server DefaultMapping `tax:gst-input-igst` / `tax:rcm-payable` kept once |
| 5 | flows `525fc44c` | `22e60363` | append blocks, lane sections, rbac, slnx, lock (4250–4254 --add); `accMapRows` merged by hand; EXPENSE_APPROVE keeps the purchase stamp + flows' cash note; GRP_COMPANY_PAYMENT keeps TDS check + cash guard; ExpenseCommands takes both optional ports |
| 6 | parity `0ee739b6` | `bc008a3c` | accounting.ts imports / default map / KEY_LABEL + ACC_KEY_PATTERN (payroll + vendor groups); HR_ADVANCE_ADD = parity funding + flows guard; lock (4257, 4258 --add) |
| 7 | ui `3f748bc7` | `d471b1a2` | none |
| 8 | tests `35462c61` | `1db811cc` | tests lane's version wins for accounting-rulebook.ts, _rulebook-month.mjs, stream-accounting-rulebook.mjs, RulebookMonthTests.cs; purchase's "expense:501/502 are Purchase" re-applied in RulebookMonthTests |

After the merges: `tsc` 1 error (manual's `ACCCashGuardResult` clashed with flows'), `dotnet build` 0 errors / 0 warnings, `npm test` **2024 / 2076** (log
`.scratch\integrator\npm-test-merged.log`: FY26 pins, [PERF-R], [GOLDEN], and the rulebook scenario not loading — DEPOSIT_APPLY without `source:'opening'`).

## 2. Checklist and rulings — DONE (prototype)

| item | commit |
|---|---|
| Lane stubs replaced: `tdsForBill` (purchase → tds, `.paise`), `accManualSave` (tds, gst → manual's API), cash guards (purchase vendor payment, manual vouchers → flows' `accCashEnforce` / `accCashGuard`); `cashDeductionLost` on `ExpVendorPayment` and `ACCManualVoucher` (re-checked at approval) | `d41b6e08` |
| Ruling 4: rulebook scenario sends `DEPOSIT_APPLY {source:'opening'}` | `d41b6e08` |
| Ruling 6 (prototype): pay-run / net-pay booked on the period's last day while it is unlocked, else the act's date (stamped `lockedBookOn` / `bookOn`); month lock needs the pay run locked and paid | `d41b6e08` |
| Ruling 7 (prototype): 1210 control, staff parties, `payroll:fnf-recoverable` → 1210 | `d41b6e08` |
| Ruling 9 (prototype): `MnyVendor.payeeType` (PAN hint), TDS on the payee column, rupee rounding, TDS remittance marks the TDS compliance card | `d41b6e08` |
| Ruling 10 / 12 (prototype): `MnyCategory.transport` (+ master field), casts removed | `d41b6e08` |
| `CMP_PROFILE_SET` keeps the three switches (Owner changes them); `ACC_UNLOCK` refused inside a locked month; `ACC_GST_PAY` refuses a challan beyond what the month owes (rulebook §8.6 test); loan parties in `accPartyLedgers` / `accPartyName` | `d41b6e08` |
| UI stubs file deleted; `src/features/accounting/acc2-views.ts` = UI payload types + thin adapters over the real exports; `[ACC2-UI]` tests retargeted; `ACC_GST_PAY.rcm` sent in paise | `005fe99a` |
| Pickers on `grpActiveCompanies`; `MOVE_KIND['staff-advance']`; `books.tsx` on the production report hooks; `mnySettlement` new receipt kinds + vendor payments | `9f8e5a1f` |

Lane tests adjusted with the rulings (gst T4/T19 now driven, manual real guard, tds payee/rounding, parity/chart 1210, rulebook vendor code + till party).
Last scratch run of the accounting stream files: all green except what is listed under "Remaining"; `tsc` 0 errors at `9f8e5a1f`.


## 3. Server and sync — DONE (1 Nov 2026)

| item | where it landed | commit |
|---|---|---|
| WIP batch compiles (Caller `using`, `MoneyMasterValues` in `Hcc.Contracts.MoneyMasters`); migrations 4255 / 4256 locked (`--accept-out-of-order`) and in `Hcc.slnx` | ManualVoucherPorts.cs, PayrollService.Pay.cs, lock, slnx | `b19c8c6c` |
| Ruling 7: 1210 control (`ChartOfAccounts.All`, `PartyLedgers.Controls`), `payroll:fnf-recoverable` → 1210; migration **4259** sets the catalogue row's control flag | ChartOfAccounts.cs, PartyLedgers.cs, 4259 | `98156833` |
| Ruling 6: pay-run / net-pay date = the period's last day only while that day is not period-locked (`IAccountingDayHooks`); the month lock waits for the pay run locked and paid (`IPayRunMonthCheck` / `PayRunMonthCheck`, wired in AccountingGstEndpoints) | PayrollService(.Pay).cs, GstMonthService.cs | `98156833` |
| GST payment challan capped at what the month still owes (`IGstManualVouchers.PaidForAsync`), as the prototype | GstMonthService.cs, ManualVoucherPorts.cs | `98156833` |
| `AccountingService.UnlockAsync` refuses a day inside a locked month (optional `IGstMonthStore`) | AccountingService.cs | `98156833` |
| Channel view `ItcAllowed`; expense view `CashDeductionLost`; capital purchases out of the budget alert (server) and out of report expenses + the reconcile check (prototype `reportExpenses` / `expCapitalIds`) | OtaQueries / SqlOtaStore, ExpenseCommands / SqlExpenseStore, closing.ts, accounting.ts | `e74ea1f0` |
| **4254 made conditional**: acc2-purchase's 4230 already adds `ops.expense.cash_deduction_lost`; both ran unconditionally (duplicate column). 4254 was never applied outside lane test databases; its lock hash was updated in place (recorded here for the gate) | 4254, migrations.lock.json | `e74ea1f0` |
| RBAC matrix rows PERM-ACC-010 / 011 / 012, PERM-EXP-004 (ACC 'A'; ruling 11 seed is 4234) | RbacV2MatrixTests.cs | `e74ea1f0` |
| Production sync: READS for manual, GST month / locks, statement, day book, TDS (ACC-013), vendor payments / ageing / assets (EXP-004 or ACC-001), OTA payouts (OTA-004 view); loaders registered and casts removed (the OTA payouts loader sits in the channels area, which owns `channelsX`); CHECKOUT `otaCredit` sends `SettleFolioToOta` first; `SetChannelItc`; `SetCompanyActive`; receipt kinds `deposit-apply` / `ota-credit` + `depositSource:'opening'`; company payment `tdsPaise` both ways; `SetTdsRate` from SETTING_SET `tds.*`; HR_ADVANCE_ADD `funding` / `tillId`; vendor `payeeType`, category `transport`; expense `cashDeductionLost`; `_role-reads.json` regenerated from the real server; fake routes | src/clients/sync/*, tests/_sync-harness.mjs, tests/_role-reads.json | `112fbbbd`, `0f7b78ed`, `58ca3136` |
| Contract §2 (`voucherType`) and §5 (payee column, rupee rounding, TDS card) amended; rulebook B8 settlement → Journal | ACC2-POSTING-CONTRACT.md, HCC_Accounting_Rulebook_v1.md | `112fbbbd` |
| Import: `transport` (categories) and `payeeType` (vendors) in the server lists, templates, samples and README; `payeeType` is a money field (MA11) | ImportLists.cs, SqlImportLists.cs, deployment/import-templates | `58ca3136`, `94f73468` |
| The tests lane's **7 skipped server tests un-skipped**: OTA party per channel, T11, T14 / T16–T18, T19, the GST set-off of April (§8.6), the full month from the engine only (expected.full ₹84,27,500), and `RulebookFullMonthSqlTests` (all 26 steps through the outbox on its own database, opening, cumulative TB = expected.full, six-column TB, month lock refused until the set-off then 30 days locked, day unlock refused inside the locked month, B9 cash refusal). The 15 lane-dependent tests pass | RulebookMonthTests.cs, RulebookMonthSqlTests.cs | `0f7b78ed`, `94f73468` |
| Tests moved with the rulings: TdsTests (payee column, rupees), ChartTests / PartyLedgerTests (1210), BooksDec074Tests + AdvanceFnfAccountingTests (negative F&F → 1210), ModuleGateHttpTests (RecordOtaPayout / SetChannelItc follow the OTA module switch), sync tests (vendor-payments loader, optional wire fields) | tests | `0f7b78ed`, `58ca3136`, `94f73468` |
| `rulebook-month.json` regenerated with `build-rulebook-month.mjs` (no data change); `accounting-golden.json` regenerated once (`[GOLDEN]` drift check green) | Golden/ | `0f7b78ed`, `58ca3136` |
| Re-pinned **once**: FY26 history `ac6c8544…` → `794297a2…`; FY26 whole state `787656dd…` → `26c7f239…`; FY26 bookings `66e81952…` → `8b253c91…`; [PERF-R] `acd252d0…`→`d536e7f4…`, `a25f322e…`→`14a5817d…`, `4e71ef62…`→`ca11ac10…`, `f8f86a7b…`→`c5d2c195…`, `59535232…`→`a6c52262…` — exactly parity's values (payroll vouchers in FY26 locked months, room-service lines kind food); **BASE `aa7464e7…` unchanged** | tests/*.mjs | `58ca3136` |
| UI smoke desktop + 360 px | `05 Delivery Evidence/Accounting/ACC2-UI-Smoke.md` | final commit |
| `main` (c8f91d45, docs only) merged: **clean, no conflicts**; all suites re-run after it | | `868616a7` |

Ruling 11 note: `EXP_VENDOR_PAY` and `CHN_PAYOUT` accept Owner / Manager / Accountant by role (no `payments` grant to the Accountant); the server seeds PERM-EXP-004 for ACC (4234). The ruling's list also names `EXP_VENDOR_PAY_VOID`, but contract §3.2 and 4234 say **Owner-only**: kept Owner-only — coordinator to confirm (DEC-175 says so).

## 4. Tests (whole suites, through the lock, never filtered)

| suite | before (after the merges, 28 Oct) | after (1 Nov, final) | log |
|---|---|---|---|
| `npx tsc --noEmit` | 1 error | 0 errors | `.scratch\integrator\final-tsc.log` |
| `npm test` | 2024 / 2076 | **2076 / 2076** | `.scratch\integrator\final-npm-test.log` |
| `npm run build` | — | OK (dist 3537 KB) | `.scratch\integrator\final-npm-build.log` |
| `dotnet build Hcc.slnx` | 4 errors (WIP) | **0 errors, 0 warnings** | `.scratch\integrator\final-dotnet-build.log` |
| `dotnet test Hcc.slnx -maxcpucount:1` | first full run 3543 / 3558 (8 failed, 7 skipped) | **3572 / 3572** (35 projects, 0 skipped; 3413 baseline + 159 ACC-2) | `.scratch\integrator\final-dotnet-test.log`, trx in `.scratch\integrator\trx-final\` |

## 5. Open items / carry-overs to ACC-3

- Coordinator to confirm `EXP_VENDOR_PAY_VOID` Owner-only (ruling 11 vs contract §3.2).
- The server GST month **view** (`MonthAsync`) shows only the "not over / set-off" blockers; the pay-run blocker (ruling 6) is enforced by `LockMonthAsync` (the prototype view shows it).
- Hub production load of the dated `acc.tds_rate` versions into `configX.changes` (TDS DONE item 5, display only) not wired.
- Lane items carried: no asset "put to use" / disposal (F4); a return against a capital purchase does not reduce the register cost; no re-allocation of an existing vendor payment; vendor GST state not cross-checked with the GSTIN; server Udhaar B9 total per credit (not per customer); TDS remittance per base row (194-I(a)/(b), 194J(a)/(b) not split); Form 168 figures not stored; rulebook B11 (loans only through the bank) not a guard; server set-off figures frozen vs live in the prototype until the month lock; a dated GST-on-advances switch.
- CA to confirm: TDS rates, rounding and catch-up (DEC-173); cash limits and section numbers (B9 / B10, OP-10).

## 6. HANDOFF bullet (for the coordinator)

- **W2 ACC-2 integrated on `claude/acc2` (1 Nov 2026):** eight lanes merged + coordinator rulings 1–13 on both sides (manual vouchers + loans, purchases / vendor payments / returns / asset register, TDS by payee type in whole rupees, GST set-off + month lock that waits for the pay run, OTA credit + payouts, deposit apply, cash guards B9 / B10, salary advances paid from the drawer, 1210 recoverable from leavers). Migrations 4230–4259 (4254 made conditional; 4259 = 1210 control). Prototype **2076/2076**, server **3572/3572**, 0 warnings, both builds; FY26 / [PERF-R] re-pinned once (BASE unchanged); `main` merged clean. Rulings proposed as DEC-165…177 (APR-022). Next: gate `acc2`, fast-forward `main`, then W1a LG or ACC-3. Evidence `05 Delivery Evidence/Accounting/ACC2-DONE.md`, `ACC2-UI-Smoke.md`.

## Decision Register entries proposed (coordinator rulings 1–13, 28 Oct 2026)

For the coordinator to paste into `02 Planning/HCC_Decision_Register.md` §2 (status "Decided by the lead", source L, approval row APR-022 proposed:
"28 Oct 2026 · Lead (coordinator rulings inside the owner's ACC-2 go; `02 Planning/agents/ACC2_Integration_Notes.md`) · ACC-2 integration questions
raised by the lanes · DEC-165…177 — Owner may revisit any of them"). DEC-161…164 are the legacy-data decisions; not edited here.

| ID | Topic | Decision | Status | Src | Affects |
|---|---|---|---|---|---|
| DEC-165 | Manual voucher type field and card / UPI settlement (ACC-2) | `ACC_MANUAL_SAVE` carries the voucher type as **`voucherType`** (the command's own `type` is the command name; server `SaveManualVoucher {voucherType}`), contract §2 amended. A card / UPI settlement with an MDR line (rulebook T14b) is a **Journal**: Contra stays strictly cash / bank ledgers only; rulebook B8 amended | Decided by the lead (28 Oct 2026; APR-022) | L | Accounting (manual vouchers), rulebook B8 |
| DEC-166 | Loans as party ledgers (ACC-2) | "Add loan" `ACC_LOAN_ADD {code, name, lender, accountLast4, kind}` creates the party **`2400/<CODE>`** (kind secured / unsecured); loan names are listed with the parties (`accPartyLedgers` / `accPartyName`, server `SqlReportPartyNames` from `acc.loan`) | Decided by the lead (28 Oct 2026; APR-022) | L | Accounting (chart, parties, reports) |
| DEC-167 | OTA-paid stay at check-out (ACC-2) | Check-out of an OTA-collected stay is `CHECKOUT {otaCredit:true}` → receipt kind **`ota-credit`** (Journal Dr 1115/<channel> · Cr guest ledger); the production client sends `SettleFolioToOta` before `CheckoutStay` | Decided by the lead (28 Oct 2026; APR-022) | L | Front desk, Channels, Accounting |
| DEC-168 | Applying an advance at check-out (ACC-2) | `DEPOSIT_APPLY {booking, amount, source:'receipts'|'opening'}` — `opening` applies an advance carried in the opening balance (server tender "Opening advance applied"); folio totals count `deposit-apply` as paid | Decided by the lead (28 Oct 2026; APR-022) | L | Billing, Accounting |
| DEC-169 | Salary advance funding (ACC-2) | `HR_ADVANCE_ADD` has **`funding`** = a payment method (default Cash). Drawer cash leaves the till of the open shift (cash movement source `staff-advance`, label "Salary advance") and passes the B10 cash guard ("deduction lost" above ₹10,000 a day to one person); a non-cash method is simulated (no money moves). Server `GiveAdvanceRequest {Funding, TillId}`, `hr.advance` funding columns (migration 4255); advances before ACC-2 stay bank transfers | Decided by the lead (28 Oct 2026; APR-022) | L | Payroll, Cashier, Accounting |
| DEC-170 | Pay-run voucher date and the month lock (ACC-2) | The pay-run journal and the net-pay payment are dated the **pay period's last day while that day is not period-locked**, else the business date of the lock / bank file (the narration carries the actual date). The accounting **month lock needs the month's pay run locked and paid** (its first bank file made) when the property has staff | Decided by the lead (28 Oct 2026; APR-022) | L | Payroll, Accounting (month lock) |
| DEC-171 | Negative full and final (ACC-2) | A full and final that leaves the leaver owing posts to **1210 Recoverable from leavers**, now a **control ledger** (group loans-advances) with one party per staff member (`1210/STF-<id>`); mapping key `payroll:fnf-recoverable` → 1210 on both sides (rulebook D4; migration 4259) | Decided by the lead (28 Oct 2026; APR-022) | L | Payroll, Accounting (chart) |
| DEC-172 | Vendor opening balance in allocations (ACC-2) | A vendor's opening balance is the **oldest open item** (`{expense:'opening'}`) in ageing and in oldest-first payment allocation | Decided by the lead (28 Oct 2026; APR-022) | L | Expenses (vendor payments), Accounting |
| DEC-173 | TDS rate column and rounding (ACC-2) | The TDS rate column **follows the payee**: `MnyVendor.payeeType` `'individual'|'other'` (default other; the master suggests individual when the PAN's 4th letter is P or H); the profile's `tdsDeductorType` stays, unused. **TDS rounds to the nearest rupee** (half away from zero). A TDS remittance marks the month's TDS compliance card filed with the challan. The catch-up of earlier untaxed bills on crossing the threshold is kept. Contract §5 amended; server `ops.expense_vendor.payee_type` (migration 4256) — CA to confirm the rates | Decided by the lead (28 Oct 2026; APR-022) | L | Expenses, Accounting (TDS), Compliance |
| DEC-174 | Goods transport cash limit (ACC-2) | Expense categories gain **`transport`** (both sides; server `ops.expense_category.transport`, migration 4256) so the ₹35,000 cash-payment limit (old s.40A(3)) applies to goods-carriage hire instead of ₹10,000 — VERIFY section numbers (CA) | Decided by the lead (28 Oct 2026; APR-022) | L | Masters (expense categories), cash guard |
| DEC-175 | Accountant and vendor payments / OTA payouts (ACC-2) | The Accountant is **not** given the coarse `payments` permission (it would open guest receipts); the handlers check the role: `EXP_VENDOR_PAY` and `CHN_PAYOUT` accept Owner / Manager / Accountant; the server seeds **PERM-EXP-004 for ACC** (migration 4234); PERM-OTA-004 as it is (ACC 'V'). `EXP_VENDOR_PAY_VOID` stays **Owner-only** as contract §3.2 says (the ruling's list named it; kept Owner — confirm) | Decided by the lead (28 Oct 2026; APR-022) | L | Expenses, Channels, RBAC |
| DEC-176 | Retired company accounts (ACC-2) | `GRPCompany.active?: boolean` (false = retired, `SetCompanyActive`, migration 4253); pickers list active companies only (`grpActiveCompanies`), history keeps the retired ones | Decided by the lead (28 Oct 2026; APR-022) | L | Groups / company credit |
| DEC-177 | Server event and field names (ACC-2) | Kept as the lanes built them: `advanceRecoveries` (per staff on the pay-run event), `tdsPaise` (company payment), `LockedOn` / `PaidOn` (pay-run events), `ChargeKind.Food` (folio F&B line, migration 4258), the OTA channel on `PartyContext` | Decided by the lead (28 Oct 2026; APR-022) | L | Accounting events (server) |
