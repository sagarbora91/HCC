# ACC-2 — integration DONE (INTERIM — stopped by the owner)

> **INTERIM — stopped by the owner (usage limit), 28 Oct 2026.** The integration is part-way through. Nothing below the "Done" lists has been
> built or tested; the last server batch is **uncompiled work in progress** (committed so nothing is lost). Not pushed; `main` not merged.

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

## 3. Server — WORK IN PROGRESS (committed uncompiled in the INTERIM commit)

Edited, **not yet built**: CashParty `ledger` + SqlFlowsStore reader (manual vouchers, vendor payments, cash advances); ManualVoucherService
real cash guard + `SaveInTransactionAsync`; new `ManualVoucherPorts` (IGstManualVouchers + ITdsRemittancePoster, tender on `1030/<bank>`);
`GstManualVoucherDraft.Tender`; vendor-payment cash guard + `MarkCashDeductionLostAsync`; ExpenseCommands TDS at approval (`TdsService`) +
transport flag; TdsCalculator payee column + `RoundRupee`; SqlTdsStore `payee_type`; TdsService compliance card hook; `AddAccountingTdsModule`;
money-master `payeeType` / `transport` (rules, contracts, store, ExpenseCategoryRow); SqlReportPartyNames 1210 + loans; manual store 1210
parties + `TenderPartyAsync`; HccComposition services lines (manual, tds, gst, flows, ledger month store); payroll advance `Funding` / `TillId`,
drawer movement source `staff-advance`, cash guard (`GiveAdvanceAsync`, `SetAdvanceFundingAsync`); migrations **4255** (hr.advance funding) and
**4256** (vendor payee_type, category transport) written but **not yet in `migrations.lock.json` / `Hcc.slnx`**.
Known to check first: `MoneyMasterValues.Expenses` name in `PayrollService.Pay.cs` (grep did not find it — use the constant VendorPaymentCommands uses).

## 4. Remaining (exact next steps)

1. `dotnet build Hcc.slnx` (through Invoke-Serialized) and fix the WIP batch; `node tools/migrations-lock.mjs --add --accept-out-of-order "ACC-2 integration: rulings 5, 9, 10"` for 4255 / 4256 + two `Hcc.slnx` lines.
2. Server rest: ruling 7 (1210 control + `fnf-recoverable` → 1210 in ChartOfAccounts / PartyLedgers / mapper); ruling 6 (pay-run date only when month-end unlocked; GstMonthService month-lock blocker: pay run locked and paid; GST pay challan cap); `AccountingService.UnlockAsync` refuses inside a locked month; ChannelView `ItcAllowed`; expense view `cash_deduction_lost`; server budget alert excludes capital; RbacV2MatrixTests rows PERM-ACC-010…013, PERM-EXP-004 (check 4234 grants ACC — ruling 11).
3. Prototype sync: READS lines (manual, gst locks/month, tds, vendor payments/ageing, assets, ota payouts, statement, day-book), register the loaders (write purchase's two), remove the `as unknown as ReadRoute` cast, frontdesk CHECKOUT → `SettleFolioToOta`, channel save → `SetChannelItc`, `GRP_COMPANY_SAVE` → `SetCompanyActive`, `money/wire.ts` receipt kinds + depositSource, `cash_deduction_lost` wire, company payment `tdsPaise`, admin.ts TDS rates, HR_ADVANCE_ADD sends `funding` / `tillId`, money-master wire `payeeType` / `transport`; fake routes; `HCC_UPDATE_ROLE_READS=1 … --filter RoleReadMatrix` to regenerate `_role-reads.json`; sync tests (manual / gst loaders = 1).
4. Ruling 11: handlers already accept the Accountant (purchase `payer`, flows CHN_PAYOUT); `EXP_VENDOR_PAY_VOID` stays Owner (contract §3.2) — confirm and record.
5. Contract §2 / §5 and rulebook B8 amendments (rulings 1, 9).
6. Un-skip the tests lane's 7 server tests, make the 15 lane-dependent ones pass; regenerate `rulebook-month.json` (`build-rulebook-month.mjs`) and `accounting-golden.json` once.
7. Re-pin FY26 / history / [PERF-R] once at the very end (BASE `aa7464e7…` unchanged).
8. UI smoke (desktop + 360 px) → `ACC2-UI-Smoke.md`; full `npm test`, `npm run build`, full `dotnet test Hcc.slnx -maxcpucount:1 --logger trx`; then complete this file.
