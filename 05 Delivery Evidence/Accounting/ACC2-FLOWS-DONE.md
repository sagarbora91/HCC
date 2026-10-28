# ACC2-FLOWS DONE

Lane **acc2-flows** · model Opus · branch `claude/acc2-flows` · worktree `E:\Projects\HCC\worktrees\acc2-flows` · base `b78cb069` (ACC-2 wave setup).
Contract: `02 Planning/agents/ACC2-POSTING-CONTRACT.md` §4 (deposit apply, OTA), §7 (cash guards), §8 (Udhaar reversal, company retire), §9, §10;
rulebook §4.14, §5 A6a, A13, A17b, B9, B10; DEC-150, DEC-157. Coordinator ruling of 28 Oct (names left open) adopted — see §"Coordinator ruling".

## Commits

```
64594fc3 acc2-flows: cash guard wired into RecordReceipt / POS settle / PayUdhaar / expense approve, deduction-lost flag, server tests (FlowsTests, OtaPayoutSqlTests, CompanyRetireSqlTests), sync ApplyDeposit till + source
89a45766 acc2-flows: coordinator ruling (DEPOSIT_APPLY source receipts|opening, deposit-apply counts as paid); server services (ApplyDeposit, SettleFolioToOta, RecordOtaPayout, SetChannelItc, ReverseUdhaarPayment, SetCompanyActive), SqlFlowsStore, CashGuardService, OtaPayoutEndpoints, company active; FlowsTests
dd39dc8a acc2-flows (server): CashGuard, flows SourceRules (DepositApplied, OtaCredit, OtaPayout, Udhaar reversal), receipt kinds, default mapping, events, projector cases, migrations 4250-4254
de7c0a35 acc2-flows: prototype tests (13), production sync accounting-flows.ts, cash limits in the hub, RBAC rows; chart retire test follows the receipt:ota default (1960/1961: only the integrator's accounting golden)
2cc941b9 acc2-flows: deposit apply, OTA credit + payout, cash guards, Udhaar reversal, company retire (prototype domain + handlers)
```
(plus the commit adding this DONE file)

## Files

**Prototype — new (lane-owned)**
- `03 Prototype/src/domain/streams/accounting-guards.ts` — `accCashGuard`, `accCashEnforce` (throws `AccCashLimitError`, reason `CASH_LIMIT`), `accCashFlows`, `accCashNote`, `accCashDeductionLost`, dated limits `ACC_CASH_SEED` / `accCashLimitOn` (`cash.receiptLimit` ₹2,00,000 · `cash.paymentLimit` ₹10,000 · `cash.transportLimit` ₹35,000, paise; "VERIFY section numbers"), registered in Master configuration › Money and tax (Owner-only, dated).
- `03 Prototype/src/services/mock/handlers/accounting-flows.ts` — `DEPOSIT_APPLY`, `EXP_UD_PAY_REVERSE`.
- `03 Prototype/src/clients/sync/accounting-flows.ts` — production sync (DEPOSIT_APPLY → `ApplyDeposit`, CHN_PAYOUT → `RecordOtaPayout`, EXP_UD_PAY_REVERSE → `ReverseUdhaarPayment`; `OTA_PAYOUTS_LOADER` exported, registered at integration).
- `03 Prototype/tests/stream-accounting-flows.mjs` — 14 tests `[ACC2-FLOWS]`.

**Prototype — edited (lane sections; ACC-2 append blocks)**
- `src/domain/streams/accounting.ts` — default mapping `receipt:ota` 1115, `receipt:ota-commission` 5070, `tax:tcs` 1340; receipts of kind `deposit-apply` (Journal Dr 2000 · Cr 1100) and `ota-credit` (Journal Dr 1115/<channel> · Cr 1100); the OTA payout section (kind `ota`, sub `ota-payout`); `accDates` + `accMapRows` know payouts and the new kinds; `accOtaChannelOf`; Udhaar reversal narration; `accSourceOf` for kind `ota`.
- `src/domain/streams/channels.ts` — `CHNChannel.itcAllowed?` (missing from the contract types; see Open items), payouts section: `chnChannelOfBooking`, `chnOtaCreditRefusal`, `chnOtaCredits`, `chnOtaOpen`, `chnOtaUnpaid`, `chnPayouts`, `chnPayoutProblem`.
- `src/services/mock/handlers/channels.ts` — `CHN_PAYOUT`; `CHN_CHANNEL_SAVE {itcAllowed}` (Owner only).
- `src/services/mock/handlers/folio.ts` — `CHECKOUT {otaCredit:true}` → receipt kind `ota-credit`; `PAYMENT` cash guard.
- `src/domain/streams/groups.ts` — `GRPCompany.active?`, `grpCompanyActive`, `grpActiveCompanies(s, keep?)`, `grpRetireBlocker`.
- `src/services/mock/handlers/groups.ts` — `GRP_COMPANY_SAVE {active}` + retire guard (MASTER_IN_USE), `GRP_BLOCK_CREATE` refuses a retired company (MASTER_RETIRED), `GRP_COMPANY_PAYMENT` cash guard.
- `src/domain/streams/expenses-scc.ts` — `ExpUdSettlement.reversalOf? / reason? / reversedBy?`.
- `src/services/mock/handlers/index.ts`, `src/services/mock/index.ts`, `src/clients/sync/registry.ts` — the ACC-2 append lines.

**Prototype — edited outside the strict ownership table (needed to compile / function; small, all marked `ACC-2 flows`)**
- `src/domain/types.ts` — `Receipt.kind` widened (`'deposit-apply'|'ota-credit'`), `Receipt.depositSource?:'opening'` (coordinator ruling).
- `src/domain/selectors.ts` — `totals()`: `deposit-apply` counts as paid (ruling) and, unless `depositSource:'opening'`, leaves the deposit; `ota-credit` settles the balance like company credit.
- `src/domain/streams/folio.ts` — receipt labels for the two kinds.
- `src/services/mock/handlers/pos.ts` (POS_SETTLE cash guard), `handlers/expenses.ts` (EXPENSE_APPROVE B10 guard + `cashDeductionLost`; EXP_UD_PAY B9 guard), `handlers/staff.ts` (HR_ADVANCE_ADD guard on `cmd.funding`), `handlers/reservations.ts` (`accountOf` refuses a retired company: BOOK / BOOKING_COMPANY).
- `tests/stream-rbac-matrix.mjs` — three CMDS rows (DEPOSIT_APPLY OWN MGR REC CSH, CHN_PAYOUT OWN MGR ACC, EXP_UD_PAY_REVERSE OWN).
- `tests/stream-accounting-chart.mjs` — the 1115 retire assertions: 1115 is now the default ledger of `receipt:ota`, so the party-balance case maps that key elsewhere first, and the "unmapped → retirable" check does the same.

**Server — new**
- `src/Hcc.Domain/Accounting/CashGuard.cs` (limits, `Receipt` / `Payment` rules, messages identical to the prototype, `Refusal` = VALIDATION + `details.reason` CASH_LIMIT, `Rs` Indian grouping).
- `src/Hcc.Domain/Ota/OtaPayoutRules.cs` (statement arithmetic, same messages as `chnPayoutProblem`).
- `src/Hcc.Contracts/Accounting/FlowsContracts.cs` (requests / results / events: `ApplyDepositRequest{…, Source}`, `SettleFolioToOtaRequest`, `RecordOtaPayoutRequest`, `SetChannelItcRequest`, `ReverseUdhaarPaymentRequest`, `SetCompanyActiveRequest`, `OtaPayoutView`, `OtaPayoutRecordedEvent`, `UdhaarPaymentReversedEvent`).
- `src/Hcc.Application/Accounting/CashGuardService.cs` (`CashParty`, `ICashFlowReader`, `ICashDeductionMarker`, `CashGuardSettings.Provider`, `CashGuardService`).
- `src/Hcc.Application/Billing/DepositFlows.cs` (`ApplyDeposit`, `SettleFolioToOta`), `src/Hcc.Application/Ota/OtaPayouts.cs` (`RecordOtaPayout`, `SetChannelItc`, list), `src/Hcc.Application/Expenses/UdhaarReversal.cs` (`ReverseUdhaarPayment`), `src/Hcc.Application/Groups/CompanyRetire.cs` (`SetCompanyActive`).
- `src/Hcc.Infrastructure.SqlServer/Accounting/SqlFlowsStore.cs` (all the lane's SQL).
- `src/Hcc.Api/Endpoints/OtaPayoutEndpoints.cs` (`AddAccountingFlowsModule`, `MapOtaPayoutEndpoints`; services built from the platform until the DI line is added).
- Migrations `4250_acc2_flows_receipt_kinds.sql`, `4251_ota_payout.sql`, `4252_exp_udhaar_reversal.sql`, `4253_grp_company_active.sql`, `4254_exp_cash_deduction_lost.sql` (locked with `migrations-lock.mjs --add`, listed in `Hcc.slnx`).
- Tests `tests/Hcc.Accounting.Tests/Domain/FlowsTests.cs`, `tests/Hcc.Ota.Tests/Sql/OtaPayoutSqlTests.cs`, `tests/Hcc.Groups.Tests/Sql/CompanyRetireSqlTests.cs`.

**Server — edited**
- `Hcc.Domain/Accounting/AccountingSource.cs` (ACC-2 flows block: `DepositApplied`, `OtaCredit`, `OtaPayout`, `UdhaarPaymentReversal`, keys; `FolioReceipt` delegates the two kinds), `ChartOfAccounts.cs` (3 default keys), `Hcc.Domain/Money/FolioTotals.cs` (`ReceiptKind.DepositApply / OtaCredit`, `FolioReceipt.FromOpening`, `OpeningAdvanceTender`), `Hcc.Domain/Billing/FolioStatement.cs` (opening flag from the tender).
- `Hcc.Contracts/Accounting/AccountingContracts.cs` (`OtaPayoutRecorded`, `UdhaarPaymentReversed` + Consumed), `Hcc.Contracts/Groups/CompanyContracts.cs` (`CompanyView.Active`).
- `Hcc.Application/Accounting/AccountingEventMapper.cs` (receipt labels; `OtaPayout`, `UdhaarPaymentReversed`), `Hcc.Infrastructure.SqlServer/Accounting/AccountingVoucherProjector.cs` (ota-credit channel party via `PartyContext.Channel`; two new event cases).
- Cash guard wired (optional ctor param `CashGuardService? cashGuard = null`, DI injects it once registered): `Billing/BillingService.cs` (RecordReceipt, cash payment / deposit), `Pos/PosService.cs` (settle, cash), `Expenses/ExpenseRegisters.cs` (PayUdhaar, cash), `Expenses/ExpenseCommands.cs` (Decide approve, drawer cash → warning in the audit + `cash_deduction_lost`).
- `Payments/CashierService.cs` (`udhaar-reversal` movement: kind payment, negative), `Groups/CompanyAccounts.cs` (`CompanyRow.Active`, book view), `Groups/CompanyCredit.cs` (`CompanyCreditCheck.NewAsync` refuses a retired company, MASTER_RETIRED), `Infrastructure.SqlServer/Groups/SqlCompanyStore.cs` (reads `active`), `Hcc.Api/HccComposition.cs` (ACC-2 block: `api.MapOtaPayoutEndpoints();`).

## What was built

**§4 Deposit apply (A6a)** — `DEPOSIT_APPLY {booking, amount, source?}`: payments permission, in-house stay, open shift (Reception / Cashier their own, PERM-PAY-001 scope S), ≤ the balance due; source `receipts` (default) ≤ the deposit receipts held on the folio; source `opening` (Manager / Owner; server: PERM-PAY-001 + PERM-ACC-008) ≤ the credit balance of 2000 Advance deposits (an advance carried in the opening balance, rulebook T5a; the receipt carries `depositSource:'opening'`, server tender `Opening advance applied`). Receipt kind `deposit-apply` counts as paid (ruling). Voucher: Journal Dr 2000 (`receipt:deposit`) · Cr 1100. Server `ApplyDeposit {folioId, amountPaise, tillId, expectedVersion?, source?}` publishes `ReceiptRecorded` kind `deposit-apply` → `SourceRules.DepositApplied`.

**§4 OTA credit (A13)** — `CHECKOUT {id, otaCredit:true}`: the balance becomes a receipt kind `ota-credit` (Journal Dr 1115/<channel> · Cr 1100), refused unless the booking's source is an OTA and its channel is **Connected**. Server: `SettleFolioToOta {folioId, expectedVersion?}` (PERM-STY-003) runs before `CheckoutStay` (the channel = the `ota.message` New that created the reservation); `ReceiptRecorded` kind `ota-credit` → `SourceRules.OtaCredit` with the channel code on `PartyContext.Channel` → `1115/<code>`.

**§4 OTA payout (DEC-150)** — `CHN_PAYOUT {channel, statementRef, gross, commission, commissionGst, tcs, net, bank, bookings?}` (Owner / Manager / Accountant; prototype: role + payments-or-reports; server PERM-OTA-004): gross = net + commission + commissionGst + tcs; gross ≤ the channel's open receivable (prototype: the 1115/<channel> ledger balance; server: OTA credits on the channel's folios − payouts); active bank account; one statement per channel; named bookings must have an OTA credit. Posts Receipt (kind `ota`, sub `ota-payout`, key `ota-payout:<id>`): Dr 1030/<bank> net · Dr 5070 commission (+ GST) · Dr 1340 TCS · Cr 1115/<channel> gross; with the channel's `itcAllowed` (Owner setting) the commission GST goes to 1310 / 1320 half each. Server: `RecordOtaPayout`, `SetChannelItc`, `GET /ota/payouts`, table `ota.payout` (+ `ota.payout_booking`, append-only), event `OtaPayoutRecorded` → `SourceRules.OtaPayout`.

**§7 Cash guards (B9, B10)** — one guarded call per command: folio `PAYMENT` (cash tenders of payment / deposit; party = the booking, summed per day and over the booking), `POS_SETTLE` (the bill on its own), `EXP_UD_PAY` (party `1120/UD-…`, summed over the credit), `GRP_COMPANY_PAYMENT` (only if a drawer method — companies pay non-cash today), `EXPENSE_APPROVE` (payment; party the vendor; posts with the warning and `cashDeductionLost:true`), `HR_ADVANCE_ADD` (only when `cmd.funding` is drawer cash; prototype advances are bank transfers). Receipts ≥ limit refused (`AccCashLimitError`: code VALIDATION, `reason` CASH_LIMIT, message cites s.186 / old 269ST + "VERIFY section numbers"); payments > limit warned (old 40A(3)); transport limit when the category carries `transport`. Manual Payment / Receipt and vendor payments call the same `accCashEnforce` (their lanes); `accCashFlows` already counts `accountingX.manual` and `expensesX.vendorPayments` rows. Server: `CashGuard` + `CashGuardService` in RecordReceipt, POS settle, PayUdhaar, expense approve; limits are dated Master-configuration settings (`CashGuardSettings.Provider`, group money).

**§8 Udhaar reversal (A17b)** — `EXP_UD_PAY_REVERSE {receivable, settlement, reason}` (Owner): a negative settlement dated the business date (`reversalOf`, `reason`; original `reversedBy`); same day nets the day to zero, a later day is the reversing entry; cash leaves the drawer (open shift, cash book open); outstanding restored; voucher = negative Receipt (Dr 1120/<customer> · Cr tender, narration "Udhaar payment reversed (A17b)"). Refuses twice, a reversal, a written-off credit. Server `ReverseUdhaarPayment` (PERM-MST-002), migration 4252, cash movement `udhaar-reversal`, event `UdhaarPaymentReversed` → `SourceRules.UdhaarPaymentReversal`.

**§8 Company retire (DEC-157)** — `GRP_COMPANY_SAVE {…, active}` (default true; a new company cannot start retired): retiring refuses open stays billed to it, open blocks, an open company-credit receivable and a non-zero `1110/<company>` balance (`accPartyRetireBlocker`) — MASTER_IN_USE; retired companies are refused by BOOK / BOOKING_COMPANY / GRP_BLOCK_CREATE (MASTER_RETIRED); `grpActiveCompanies(s, keep)` for pickers. Server `SetCompanyActive` (PERM-GRP-002), `grp.company_account.active` (+ retired_by / utc / reason), book view `active`, `CompanyCreditCheck.NewAsync` refuses a retired company.

## Coordinator ruling (28 Oct) — adopted

1. The OTA check-out is `CHECKOUT {otaCredit:true}` producing receipt kind `ota-credit`, refused unless the booking's source is a connected channel. (Server: the sync sends `SettleFolioToOta` right before `CheckoutStay` — see Integration item 3.)
2. `DEPOSIT_APPLY {booking, amount, source?:'receipts'|'opening'}`; `'opening'` is Manager / Owner; `totals()` counts `deposit-apply` as paid (prototype `selectors.ts`, server `FolioTotals`).
3. Server: the OTA channel travels on `PartyContext.Channel` (projector resolves `1115/<channel code>`); the payout event is `OtaPayoutRecorded`.

## Tests

All through `E:\Projects\HCC\worktreescc2	ools\Invoke-Serialized.ps1 -Label acc2-flows`, full logs in `<worktree>\.scratch\` (uncommitted).

| suite | command | before | after |
|---|---|---|---|
| prototype tsc | `npx tsc --noEmit` | clean | clean |
| prototype | `npm test` (whole suite) | 1948 / 1948 | **1961 / 1962** — +14 `[ACC2-FLOWS]`; the 1 failure is `[GOLDEN] accounting-golden.json …` (three new default mapping keys; regenerating the golden is the integrator's, Integration item 1) |
| server build | `dotnet build Hcc.slnx` | 0 warnings | 0 warnings, 0 errors |
| Hcc.Accounting.Tests | `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx` | 147 | **163 / 163** (+16 `FlowsTests`) |
| Hcc.Ota.Tests | same | 43 | **46 / 46** (+3 `OtaPayoutSqlTests`) |
| Hcc.Groups.Tests | same | 45 | **47 / 47** (+2 `CompanyRetireSqlTests`) |

The server golden test (`GoldenAccountingTests`) still reads the committed golden and passed: the prototype-side drift check is the one that flags the three mapping keys. Full `Hcc.slnx` suite not run (integrator / gate).

## Open items

- **acc.source CHECK**: the OTA payout source is kind `ota`; `acc.source.source_kind` accepts it only after the manual lane's migration 4238 (contract §1). Until merged, projecting `OtaPayoutRecorded` on SQL fails the CHECK (the lane's SQL tests build the voucher in memory). Merge order manual → flows resolves it.
- `VoucherBuilder.TypeOrder` (server) and the prototype `accLiveVouchers` type order still list only Sales / Receipt / Payment / Journal (Purchase / Contra vouchers would be dropped) — coordinator / purchase / manual lanes.
- `MnyCategory.transport?` is not in the contract types (purchase lane owns `money-master.ts`): the guard reads it loosely; the server passes `transport:false` until the expense category carries the flag.
- `CHNChannel.itcAllowed` was not in the coordinator's types; added in `channels.ts` (optional). Server `ChannelView` does not expose `itc_allowed` yet (Integration item 4).
- PERM-OTA-004 vs the prototype's §9 equivalent: the contract maps OTA payouts to `permission('payments')` + role, but the prototype Accountant holds only `reports`; implemented as role ∈ Owner / Manager / Accountant and (payments or reports), matching PERM-OTA-004 holders.
- Server cash guard not wired for company payments (UPI / card / bank only on the server) and staff advances (server advances are bank transfers) — nothing cash to guard; prototype calls are in place.
- Server Udhaar party total for B9 is per credit (the server cannot recompute the prototype's UD-<mobile|slug> grouping in SQL cheaply); prototype totals per customer per day and per credit.
- Prototype `mnySettlement` lists the new kinds under "Not settled through an account" as outflows (Integration item 10).
- FY26 hash pins: the lane does not change fixtures or the generator — pins unaffected. The accounting golden changes (three default mapping keys) — Integration item 1.

## Integration needed

1. **Accounting golden**: regenerate `08 Production/tests/Hcc.Accounting.Tests/Golden/accounting-golden.json` (`node "08 Production/tests/Hcc.Accounting.Tests/Golden/export-accounting-golden.mjs"`): `$.defaultMapping` gains `receipt:ota` 1115, `receipt:ota-commission` 5070, `tax:tcs` 1340 (the only prototype failure on this branch, `[GOLDEN] accounting-golden.json …`).
2. **DI** — `08 Production/src/Hcc.Api/HccComposition.cs`, services list, after `services.AddAccountingOpeningModule();` add:
   `services.AddAccountingFlowsModule();                // ACC-2 flows: SqlFlowsStore ports, CashGuardService (+ settings provider) → injected into BillingService / PosService / ExpenseCommands / ExpenseRegisterCommands`
3. **CHECKOUT {otaCredit}** — `03 Prototype/src/clients/sync/frontdesk.ts`, CHECKOUT mapper, before `await c.api.command('CheckoutStay',…)` add:
   `if(c.cmd.otaCredit){const f=c.ids.server('folio',id);if(f===undefined)throw new HccClientError('INVALID_TRANSITION','This booking has no folio on the server yet.');await c.api.command('SettleFolioToOta',{folioId:f,expectedVersion:c.ids.version('folio',id)??null},{step:'ota'});}`
   and pass no `companyCreditApprovalId` when `otaCredit` is set.
4. **Channel ITC** — server `Hcc.Contracts/Ota` `ChannelView` + `SqlOtaStore` select: add `ItcAllowed` (`ota.channel.itc_allowed`, migration 4251); `03 Prototype/src/clients/sync/channels/ota.ts` `channelFromServer`: `...(c.itcAllowed?{itcAllowed:true}:{})`; CHN_CHANNEL_SAVE mapper: after `SaveChannel`, when `m.itcAllowed!==undefined` send `SetChannelItc {channelId:serverId(m.channel,'Channel'), itcAllowed:m.itcAllowed===true}` (`{step:'itc'}`).
5. **Company active** — `03 Prototype/src/clients/sync/money.ts` GRP_COMPANY_SAVE mapper (existing company): after `SaveCompany`, when `m.active!==undefined` send `SetCompanyActive {companyId:serverId(id,'Company'), active:m.active!==false}` (`{step:'active'}`); `money/wire.ts` `SrvCompany` gains `active?:boolean` and `companyFromServer` adds `...(c.active===false?{active:false}:{})`.
6. **Receipt kinds on the wire** — `03 Prototype/src/clients/sync/money/wire.ts`: `const RECEIPT_KIND=['payment','deposit','deposit-return','refund','company-credit','deposit-apply','ota-credit'];` and in `receiptFromServer`: `if(r.kind==='deposit-apply'&&r.tender==='Opening advance applied')out.depositSource='opening';`.
7. **OTA payouts read** — `03 Prototype/src/clients/sync/reads.ts`: `'GET /ota/payouts':<the OTA money-view rule used for PERM-OTA-004>`; `tests/_role-reads.json` route + per-role codes; then in `sync/accounting-flows.ts` set `loaders:[{...OTA_PAYOUTS_LOADER,reads:['GET /ota/payouts']}]` (default fake route in `tests/_sync-harness.mjs`: `'GET /ota/payouts':()=>[]`).
8. **Expense flag on the wire** — server `ExpenseRecord` / expense view: read `ops.expense.cash_deduction_lost` (migration 4254); prototype expense mapper sets `cashDeductionLost:true`.
9. **UI (acc2-ui)** — company pickers: `src/features/groups/credit.tsx` lines 21 and 55 `grpSlice(s).companies` → `grpActiveCompanies(s,b.company?.account)` / `grpActiveCompanies(s)`; `src/features/groups/modals.tsx` line 76 `x.companies.map(` → `grpActiveCompanies(s,co).map(`. Deposit-apply / OTA payout / Udhaar reversal screens call `DEPOSIT_APPLY`, `CHECKOUT {otaCredit}`, `CHN_PAYOUT` (`chnOtaUnpaid`, `chnPayoutProblem`), `EXP_UD_PAY_REVERSE`; the cash-guard toast reads `e.reason==='CASH_LIMIT'` and the summary text's warning; the badge reads `Expense.cashDeductionLost` / `accCashDeductionLost(s)`.
10. **mnySettlement** — `03 Prototype/src/domain/streams/money-master.ts` line ~206: `if(r.kind==='payment'||r.kind==='deposit'||r.kind==='company-credit'||r.kind==='ota-credit')add(r.method,r.amount,0);else if(r.kind!=='deposit-apply')add(r.method,0,r.amount);`
11. **Merge order** — after manual (4238 widens `acc.source.source_kind` with `ota`); migrations 4250–4254 need `--accept-out-of-order` only if a higher block was locked first. Remove the local stub-free notes: `accCashEnforce` is real (no stub to delete).

## HANDOFF bullet

- **ACC-2 flows (lane acc2-flows)**: `DEPOSIT_APPLY {booking, amount, source receipts|opening}` (Journal Dr 2000 · Cr 1100; counts as paid), OTA credit at `CHECKOUT {otaCredit}` (Dr 1115/<channel> · Cr 1100) and `CHN_PAYOUT` from the statement (Receipt Dr 1030/bank net · Dr 5070 · Dr 1340 TCS · Cr 1115 gross; ITC on commission GST per channel), cash guards B9 refused (CASH_LIMIT) / B10 warned + `cashDeductionLost` with dated Owner limits `cash.*`, `EXP_UD_PAY_REVERSE` (A17b), company retire (`active`, DEC-157). Server: `ApplyDeposit`, `SettleFolioToOta`, `RecordOtaPayout`, `SetChannelItc`, `ReverseUdhaarPayment`, `SetCompanyActive`, `GET /ota/payouts`, migrations 4250–4254. DONE: `05 Delivery Evidence/Accounting/ACC2-FLOWS-DONE.md`.
