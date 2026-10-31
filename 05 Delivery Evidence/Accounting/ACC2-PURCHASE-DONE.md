# ACC2-PURCHASE DONE

Lane **acc2-purchase** (Opus) · branch `claude/acc2-purchase` · worktree `E:\Projects\HCC\worktrees\acc2-purchase` · base `b78cb069`
(ACC-2 wave setup). Contract: `02 Planning/agents/ACC2-POSTING-CONTRACT.md` §1, §3, §7, §9, §10 (+ the coordinator's ruling of 28 Oct on
vendor opening balances and the TDS event field). Not pushed.

## Commits

```
b1c2e5a0 ACC-2 purchase: migrations 4230-4235 locked; Journal -> Purchase in rulebook month and money-master sync test expectations
0a3a2896 ACC-2 purchase: coordinator ruling T11 - vendor opening balance is the oldest open item (expense 'opening'; server alloc opening = 1, expense_id NULL), tests both sides
263696df ACC-2 purchase: RCM flag + rate stored at entry (re-stamped at approval, both sides); Journal -> Purchase in existing server tests; samples keep their row counts
1a3975ca ACC-2 purchase: server tests (PurchaseRulesTests, PurchaseSqlTests, harness), capital purchases out of budgets (prototype), return not reversible (parity)
b58ce8c8 ACC-2 purchase (server): purchase rules, Purchase voucher type order, RCM / return / vendor-payment sources + events, VendorPaymentCommands, SQL stores, endpoints, master fields, import columns; prototype money-master sync fields
8b781933 ACC-2 purchase: restore original line endings; production sync for vendor payments / returns / purchase fields; migrations 4230-4235 (not yet locked)
d4320e65 ACC-2 purchase (prototype): Purchase vouchers, IGST 1330, ITC guard DEC-143, RCM source, capital purchases + asset register, vendor payments / voids / returns, ageing, master fields, tests
```
(+ the commit of this DONE file.)

## Files

**Prototype (`03 Prototype`)**

| file | what |
|---|---|
| `src/domain/streams/expenses-vendor.ts` (new) | purchase helpers: GST kind from the vendor state, ITC guard (DEC-143), RCM guard, `expPurchaseEntry` / `expPurchaseStamp` / `expPurchaseLines`, vendor payments (`expVendorPayCash`, open bills incl. the opening item, oldest-first allocation, allocation guard, balance, advance), `expVendorAgeing` (0–30 / 31–60 / 61–90 / 90+), returns (`expReturned`, `expReturnMethod`); **local stub `tdsForBill = (s, e) => 0`** |
| `src/domain/streams/accounting-assets.ts` (new) | fixed-asset register: `accAssets`, `accIsFixedAssetLedger`, `accAssetNextNo` (FA-yyyy-nnn), `accAssetFromExpense`, `accAssetRegister`, `accAssetBlocks`, `accAssetsCsv` |
| `src/services/mock/handlers/expenses-vendor.ts` (new) | `EXP_VENDOR_PAY`, `EXP_VENDOR_PAY_VOID`, `EXP_VENDOR_RETURN`; **local no-op stub of the acc2-flows cash guard** |
| `src/clients/sync/expenses-vendor.ts` (new) | production sync: `RecordVendorPayment`, `VoidVendorPayment`, `RecordExpenseReturn`; purchase-field wire; loaders exported, not registered (Integration 4) |
| `src/domain/streams/accounting.ts` | the expense source of `accLiveVouchers` (Purchase type, IGST, TDS, RCM second source, returns), vendor payment / void source, Purchase / Contra in the type order, 4 mapping keys + `vendor` key group, reconcile counts TDS, `accDates`, `accMapRows`, `accSourceOf` |
| `src/services/mock/handlers/expenses.ts` | purchase fields at entry / edit, stamp + asset row at approval, returns Owner-only, reversal mirrors the purchase lines, a return is not reversible / editable |
| `src/domain/streams/money-master-specs.ts` | category fields `itcBlocked`, `rcm`, `rcmBps`, `capital` (+ fixed-asset ledgers offered, validation); vendor `state`, `tdsSection` |
| `src/domain/streams/expenses-scc.ts` | budgets skip capital purchases |
| `src/domain/selectors.ts` | `drawer()` subtracts cash vendor payments (`expVendorPayCash`) |
| `src/clients/sync/accounting.ts`, `money-master.ts` | purchase fields on the expense and master wire |
| append blocks | `services/mock/handlers/index.ts` (ACC2), `services/mock/index.ts`, `clients/sync/registry.ts` |
| tests | `tests/stream-accounting-purchase.mjs` (new, 13); `stream-rbac-matrix.mjs` CMDS (3 commands); `stream-accounting-parties.mjs`, `stream-money-master.mjs` (Journal → Purchase), `sync-money-master.mjs` (wire fields) |

**Server (`08 Production`)**

| file | what |
|---|---|
| `database/migrations/4230…4235` (new) + `Hcc.slnx` + `migrations.lock.json` (`--add`; 102 in step) | see "Server pieces" |
| `src/Hcc.Domain/Expenses/ExpensePurchase.cs` (new) | shared purchase rules (twin of `expenses-vendor.ts`) |
| `src/Hcc.Domain/Accounting/AccountingSource.cs`, `ChartOfAccounts.cs`, `PartyLedgers.cs`, `Vouchers.cs` | rules, keys, vendor party key, type order |
| `src/Hcc.Domain/Expenses/ExpenseMasters.cs`, `MoneyMasters/MoneyMastersRules.cs`, `Import/ImportLists.cs` | master row / field / import rules |
| `src/Hcc.Contracts/Accounting/PurchaseEvents.cs` (new), `AccountingContracts.cs` | events (in `Consumed`), `ExpensePostedEvent` fields |
| `src/Hcc.Contracts/Expenses/VendorPaymentContracts.cs` (new), `ExpensesContracts.cs`, `MoneyMasters/MoneyMastersContracts.cs` | requests / views |
| `src/Hcc.Application/Expenses/VendorPaymentCommands.cs` (new), `ExpenseCommands.cs`, `ExpensePorts.cs`, `Accounting/AccountingEventMapper.cs`, `MoneyMasters/MoneyMastersCommands.cs` | commands, queries, ports, mapper |
| `src/Hcc.Infrastructure.SqlServer/Expenses/SqlVendorPaymentStore.cs` (new; + `SqlFixedAssetStore`), `SqlExpenseStore.cs`, `SqlExpenseRegisterStore.cs`, `MoneyMasters/SqlMoneyMastersStore.cs`, `Import/SqlImportLists.cs`, `Accounting/AccountingVoucherProjector.cs` | SQL adapters, projector cases |
| `src/Hcc.Api/Endpoints/ExpenseVendorEndpoints.cs` (new), `ExpensesEndpoints.cs` (one line), `HccComposition.cs` (ACC-2 block, one line) | endpoints |
| `deployment/import-templates/` README + expense-categories / expense-vendors templates and samples | new columns |
| tests | `Hcc.Accounting.Tests/Domain/PurchaseRulesTests.cs` (new, 7), `Hcc.Expenses.Tests/Sql/PurchaseSqlTests.cs` (new, 9) + harness; `AccountingRulesTests`, `RulebookMonthTests`, `MappingAndExportTests` (Journal → Purchase) |

## What was built (contract §3, §1, §7, §9, §10)

**§3.1 Vendor bill (C1, C5, C9, C10).** `accLiveVouchers` expense section (prototype) and `SourceRules.Expense` (server) read only the fields
stamped on the record: a bill on a **credit method** is a **`Purchase`** voucher (`PUR/yyyymmdd/nnn`, was `Journal`); ITC allowed →
intra 1310 / 1320 halves, **inter → whole GST on 1330** (`tax:gst-input-igst`); **TDS held back** (`tdsPaise`) → Cr 2150
(`tax:tds-payable`) and the funding / vendor credited net (funding entry stays last; `accReconcile` "Posted expenses" counts funding + TDS).
GST kind = sent on the bill, else the vendor master `state` (≠ MH → inter; kept only when sent or inter). **ITC guard** (DEC-143) at entry,
edit and approval: a category flagged `itcBlocked`, or a capital purchase on 1500 / 1540, refuses ITC allowed with the message
"ITC cannot be allowed on <category>: the category is marked blocked (CGST s.17(5)) — DEC-143: …". Purchase / Contra are numbered after
the four old types on both sides (server `VoucherBuilder.TypeOrder` had dropped any Purchase voucher before this lane).

**§3.3 Reverse charge (C8, DEC-145).** A category with `rcm` refuses vendor GST on the bill; at approval `rcm` + `rcmBps` (the bill's
rate, else the category default) are stamped; a second source `{kind:'tax', id:'rcm:<expense>', sub:'rcm'}` (Journal) posts Dr the category
(or 1310 / 1320 — 1330 inter — when ITC is allowed on the RCM GST) · Cr 2140 (`tax:rcm-payable`) at the rate, rounded half away from zero.
Reversals / returns mirror it. Server: event `ExpenseRcmPosted` → `SourceRules.Rcm`.

**§3.4 Capital purchase (C10).** A category with `capital` must map to a Fixed Assets block (master validation both sides; server CHECK
`ledger LIKE '15[0-9]0'`); approval creates the register row `s.accountingX.assets[]` / `acc.fixed_asset` `{no FA-<yyyy>-<nnn>, ledger,
name, cost = bill − ITC, itc, date, vendor, expense}` and stamps `assetNo`; a reversal marks the row disposed at 0 (never deleted).
Register selectors + CSV: `accAssetRegister`, `accAssetBlocks`, `accAssetsCsv`; server `GET /accounting/assets`. No depreciation journal.
Capital purchases do not count against budgets (prototype `expCategorySpend` / `expBudgetRows`).

**§3.2 Vendor payments / advances (C3, C4).** `EXP_VENDOR_PAY {vendor, amount, method, reference, date?, allocations?}` (Owner / Manager /
Accountant): vendor = an active master row (stored by code); never a credit method; bank / UPI need the reference; cash needs the open shift
and leaves the drawer (`drawer()` subtracts `expVendorPayCash`; server pay.cash_movement source `vendor-payment`); allocations oldest open
bill first unless sent (guarded: open bill of this vendor, ≤ its open amount, Σ ≤ payment); the rest is an advance. Posts `Payment`
`{kind:'vendor', id:'vendor-payment:<id>', sub:'vendor-payment'|'vendor-advance'}` Dr 2200/<vendor> (`vendor:payable`) · Cr the tender
(party = the settles-to till / bank). `EXP_VENDOR_PAY_VOID {id, reason}` (Owner): same business day → void, nothing posts (server removes the
unlocked source); a later day → reversing voucher `vendor-payment:<id>/void` on the void date (`voidAt` = the void's business date, as the
Udhaar write-off keeps it); cash comes back into the drawer. Ageing by allocations: `expVendorOpenBills`, `expVendorAgeing` (0–30 / 31–60 /
61–90 / 90+ by bill date; advance; net = the party ledger 2200/<vendor>); server `GET /expenses/vendor-ageing?asOf=&vendor=`.

**Coordinator ruling (28 Oct, adopted exactly).** A vendor's opening balance (the APPROVED opening entry's lines on `2200/<vendor>`,
credit − debit) has no expense record: `expVendorOpenBills` / `expVendorAgeing` list it as the **oldest open item** `{expense:'opening',
date:<opening asOf>, payable, open, …}` (`EXP_OPENING`, `expVendorOpening`), aged from the asOf; `EXP_VENDOR_PAY` allocations may name
`expense:'opening'` and the default oldest-first allocation takes it first (rulebook T11: ₹60,000 = ₹25,000 opening + ₹35,000 of the
₹59,000 bill — tested both sides); the vendor balance / ageing net include the opening (= the party ledger). Server: `ops.vendor_payment_alloc`
has a surrogate key, `expense_id NULL` with `opening = 1` (CHECK + unique per payment); `VendorAllocationDto(ExpenseId 0, Amount, Opening:true)`;
`IVendorPaymentStore.OpeningAsync` reads the approved `acc.opening_entry` / `acc.opening_line`. TDS held on a bill goes out on the expense
event as `tdsPaise` (+ `tdsSection`) — the tds lane's `TdsCalculator.ForBill` fills it at approval (see Integration 1).

**§3.5 Returns (C5).** `EXP_VENDOR_RETURN {expense, amount, gstPaise, reason}` (Owner / Manager / Accountant): a negative bill record
(`sourceRef 'ret:<bill>'`; server `return_of`) through the credit method (the bill's own, else the first credit method), carrying the bill's
GST kind / ITC / RCM / capital flags and a proportional TDS; ≤ the bill less returns so far, GST ≤ the bill's GST left and ≤ the amount. A
Manager's / Accountant's return is Pending and **only the Owner approves it** (`EXPENSE_APPROVE` / `DecideExpense` refuse others); the
Owner's own posts at once. Posts a negative `Purchase` (`sub 'return'`): Dr 2200/<vendor> · Cr the category · Cr the ITC taken. A posted
return is not reversible (server `CK_expense_sign`; prototype parity) and is not editable. Server event `ExpenseReturned` →
`SourceRules.ExpenseReturn`.

**Masters (Owner-only, DEC-058).** Category: `itcBlocked`, `rcm`, `rcmBps`, `capital` (fixed-asset ledgers offered; capital ⇔ 15x0 block;
RCM needs a rate; vehicles / buildings forced ITC-blocked). Vendor: `state` (two letters), `tdsSection` (rent | contractor | professional |
commission; only with `tds`). CSV import templates / samples / README and the server import columns follow.

**§1 / §10.** Mapping keys (both sides): `tax:gst-input-igst` 1330, `tax:rcm-payable` 2140, `tax:tds-payable` 2150, `vendor:payable` 2200;
key group `vendor` ("Vendor payments (payables)") added to `ACC_KEY_PATTERN` / `ChartOfAccounts.KeyPattern`; `PartyContext.For("vendor:payable")`
= the vendor. Server `acc.source` CHECKs widened to `SourceKinds.All` and the voucher types Purchase / Contra (4235).

**§9.** PERM-EXP-004 `expenses.vendor.pay` (Owner, Manager, Accountant; seed 4234). Prototype: role in Owner / Manager / Accountant and
(`payments` or the Accountant role — the Accountant has no `payments` grant in the coarse model). Void and return approval: Owner.

**Server pieces.** Migrations 4230 (ops.expense purchase columns, constraints widened), 4231 (category / vendor columns), 4232
(`ops.vendor_payment` + `_alloc`), 4233 (`acc.fixed_asset`), 4234 (`*_rbac_seed`), 4235 (acc.source kind / type CHECKs). Domain
`ExpensePurchase` (entry, stamp, lines, allocation, ageing buckets, return limits, asset numbers), `SourceRules.Expense` (+`PurchaseLines`),
`Rcm`, `VendorPayment`, `ExpenseReturn`, `BpsOf`, `ItcEntries`. Application `ExpenseCommands` (purchase fields, stamp, asset row, return
approval, RCM / return events), new `VendorPaymentCommands.cs` (+ `VendorPaymentQueries`, ports `IVendorPaymentStore`, `IFixedAssetStore`),
`AccountingEventMapper.Rcm / ExpenseReturn / VendorPayment / VendorPaymentVoid`, projector cases. Contracts `PurchaseEvents` (in
`AccountingEvents.Consumed`), `VendorPaymentContracts.cs`, request / view fields. Infrastructure `SqlVendorPaymentStore`, `SqlFixedAssetStore`,
`SqlExpenseStore` columns, master source / money-master store columns, import columns. Endpoints `ExpenseVendorEndpoints.cs`
(`RecordVendorPayment`, `VoidVendorPayment`, `RecordExpenseReturn`, the three GETs); services registered from `AddExpensesModule`, the
map line in the `HccComposition.cs` ACC-2 block.

**Deviations from the contract wording (no rule changed).** The contract names `exp.expense`, `mst.expense_category`, `mst.expense_vendor`,
`exp.vendor_payment(+_alloc)`; those schemas do not exist — the real tables are `ops.expense`, `ops.expense_category`, `ops.expense_vendor`,
so the new tables are `ops.vendor_payment` / `ops.vendor_payment_alloc` (and `acc.fixed_asset` as named). Returns are expense records
(the contract names no return record type), linked by `sourceRef 'ret:<bill>'` / `return_of`.

## Tests

All through `tools\Invoke-Serialized.ps1 -Label acc2-purchase`; whole logs uncommitted in `E:\Projects\HCC\worktrees\acc2-purchase\.scratch\`
(`tsc.log`, `npm-test.log`, `build.log`, `test-<project>.log`, `trx\`). Final run (`.scratch\all.ps1`) at `b1c2e5a0`:

| suite | before (base `b78cb069`) | after |
|---|---|---|
| `npx tsc --noEmit` | 0 errors | **0 errors** |
| prototype `npm test` | 1948 / 1948 | **1960 / 1961** — +13 `[ACC2-PURCHASE]`; the one failure is `[GOLDEN] accounting-golden.json`, only the four new default mapping keys (`$.defaultMapping.tax:gst-input-igst / tax:rcm-payable / tax:tds-payable / vendor:payable`) — the integrator regenerates the golden once (Integration 6) |
| `dotnet build Hcc.slnx` | 0 warnings | **0 warnings, 0 errors** |
| `Hcc.Accounting.Tests` | 147 | **154 / 154** (+7 PurchaseRulesTests) |
| `Hcc.Expenses.Tests` | 55 | **64 / 64** (+9 PurchaseSqlTests) |
| `Hcc.MoneyMasters.Tests` | 24 | **24 / 24** |
| `Hcc.Import.Tests` | 1968 | **1968 / 1968** |

Existing tests changed because the contract changes them (vendor credit = Purchase, was Journal): `stream-accounting-parties.mjs`,
`stream-money-master.mjs`, `sync-money-master.mjs` (wire fields), `stream-rbac-matrix.mjs` (CMDS: `EXP_VENDOR_PAY` OWN MGR ACC,
`EXP_VENDOR_PAY_VOID` OWN, `EXP_VENDOR_RETURN` OWN MGR ACC); server `AccountingRulesTests`, `RulebookMonthTests` (expense:501 / 502 now
Purchase — the acc2-tests lane owns that file: merge note), `MappingAndExportTests`. The FY26 hash pins did **not** move (the FY26 history has
no vendor-credit bill). Not run here (integrator / gate): the full server suite, `Hcc.Access.Tests`, `Hcc.Platform.Tests` (lock in step),
`Hcc.Integration.Tests`.

## Open items

- **Stubs** (Integration 1–2): `tdsForBill` → 0 (no TDS is held in this lane; every TDS line is built and tested from a record carrying
  `tdsPaise`); the acc2-flows cash guard is a no-op in `EXP_VENDOR_PAY`.
- Capital purchases still count in the closing / expense **reports** (budgets excluded, prototype only) — Integration 7.
- No "put to use" command for the asset register (`putToUse` shown when set) and no disposal / sale entry (rulebook F4) — not in the contract.
- A return against a capital purchase does not reduce the register row's cost.
- No re-allocation command for an existing payment; allocations are fixed at entry (`allocations`), an advance stays unallocated.
- Cash vendor payments are in `drawer()` / `pay.cash_movement`; the prototype cash-book desk list does not show them as their own line.
- The vendor's GST state is the contract's two-letter code; the GSTIN's numeric state is not cross-checked.

## Integration needed

1. **acc2-tds — `tdsForBill`.** `03 Prototype/src/domain/streams/expenses-vendor.ts` line
   `const tdsForBill=(_s:State,_e:Expense|ExpVendorPayment):number=>0;` → delete it and add `import {tdsForBill} from '../tds';`
   (it is called from `expPurchaseStamp` at approval; the record keeps `tdsPaise` + the vendor's `tdsSection`, and every posting / ageing
   line already reads `tdsPaise`). Server: `ExpenseCommands.DecideAsync`, line `long? tds = null; // acc2-tds lane: TdsCalculator.ForBill(...)`
   → `long? tds = TdsCalculator.ForBill(...)` (the column, the event field `tdsPaise` / `tdsSection`, the Cr 2150 line and the reversal /
   return mirroring exist). The 2150 mapping key is **`tax:tds-payable`** (both sides) — the tds lane's remittance template should debit the
   same ledger.
2. **acc2-flows — cash guard.** `03 Prototype/src/services/mock/handlers/expenses-vendor.ts`: replace the stub
   `const cashGuard=(_k:Kit,_x:{party:string;amount:number;date:string}):string=>'';` with the call of `accCashGuard(k.s,{kind:'payment',party,amount,date})`
   (the handler already calls it for cash with `party:'2200/<vendor code>'` and appends its warning to the result). `ExpVendorPayment` has no
   `cashDeductionLost` field in the contract — if the flows lane wants it on the record, the coordinator adds it; the server column
   `ops.vendor_payment.cash_deduction_lost` (4232) and `ops.expense.cash_deduction_lost` (4230) already exist. Server: `VendorPaymentCommands.PayAsync`
   comment "acc2-flows lane: the B10 cash guard … wired at integration" is where `CashGuard` goes.
3. **acc2-manual — `acc.source` CHECKs.** My 4235 widens `CK_acc_source_kind` to `SourceKinds.All` and `CK_acc_source_type` to Purchase /
   Contra (drop + add by name). The manual lane's 4238 widens the same constraint; both orders work if 4238 drops by name with `IF EXISTS`.
4. **Production loaders** (`src/clients/sync/expenses-vendor.ts`): add `expenseVendorLoader` (patch `expensesX` ← `GET /expenses/vendor-payments`)
   and `accountingAssetsLoader` (patch `accountingX` ← `GET /accounting/assets`) to `expensesVendor.loaders` with `reads:[…]`, and in
   `src/clients/sync/reads.ts` READS: `'GET /expenses/vendor-payments':one(wide('PERM-EXP-004'),wide('PERM-ACC-001',true))`,
   `'GET /accounting/assets':` the same, `'GET /expenses/vendor-ageing':` the same; regenerate `tests/_role-reads.json` from the real server
   (`HCC_UPDATE_ROLE_READS=1 … --filter RoleReadMatrix`) and add the fake routes in `tests/_sync-harness.mjs`.
5. **RBAC pins.** `08 Production/tests/Hcc.Access.Tests` RbacV2MatrixTests: add the PERM-EXP-004 row (OWN / MGR / ACC `A`, `P`). Integration
   tests that enumerate commands / endpoints: `RecordVendorPayment`, `VoidVendorPayment`, `RecordExpenseReturn`, `GET /expenses/vendor-payments`,
   `/expenses/vendor-ageing`, `/accounting/assets`.
6. **Goldens / pins (moved on purpose).** `08 Production/tests/Hcc.Accounting.Tests/Golden/accounting-golden.json` — the four new default
   mapping keys (`$.defaultMapping.tax:gst-input-igst | tax:rcm-payable | tax:tds-payable | vendor:payable`) → regenerate once
   (`export-accounting-golden.mjs`); the prototype `[GOLDEN]` drift check fails in this lane only for that. Vendor-credit bills are now
   **Purchase** vouchers (was Journal), so any snapshot / golden holding a vendor-credit expense voucher moves (`rulebook-month.json` if its
   month has one; FY26 history if it has any — the FY26 pin tests stayed green in this lane, so the history has none).
7. **Capital purchases out of expense reports** (contract §3.4 "never to expense reports / budgets"): budgets done (prototype). Not done:
   `closing.ts` `reportPack` expenses tab / `expenseCounts` totals and the server `BudgetAlertAsync` / month budget rows still count a capital
   bill — exclude `e.capital` (prototype) / `capital = 1` (server `LedgerAsync` rows need the column) there; the accounting reconcile check
   "Posted expenses" must then exclude them too (`accReconcile` expenseMove filter `!e.capital`).
8. **UI lane** (`_contract-stubs-2.ts` → real names): commands `EXP_VENDOR_PAY`, `EXP_VENDOR_PAY_VOID`, `EXP_VENDOR_RETURN` (no separate return
   decide command: a pending return is approved with `EXPENSE_APPROVE`, Owner only); selectors `expVendorOpenBills`, `expVendorAllocate`,
   `expVendorAgeing`, `expVendorBalance`, `expVendorAdvance`, `expReturned`, `expIsReturn`, `expGstKindOf`, `expItcBlockRefusal`,
   `expItcBlockedMessage`, `accAssetRegister`, `accAssetBlocks`, `accAssetsCsv`, `ACC_ASSET_CSV_HEADER`; EXPENSE / EXP_EDIT accept `gstKind`
   ('intra'|'inter') and `rcmBps`.

## HANDOFF bullet

- **ACC-2 acc2-purchase (28 Oct 2026, `claude/acc2-purchase`):** purchases per contract §3 on both sides — vendor-credit bills are
  **Purchase** vouchers, inter-state ITC to 1330, ITC refused on s.17(5) categories (DEC-143), RCM second source Dr category / ITC · Cr 2140
  (DEC-145), capital purchases to the 15x0 block + fixed-asset register (FA-yyyy-nnn, CSV), vendor payments / advances / voids
  (`EXP_VENDOR_PAY*`, `RecordVendorPayment` / `VoidVendorPayment`), returns as Owner-approved negative Purchases, ageing by allocations with the
  vendor opening balance as the oldest item (coordinator ruling, T11); masters gain RCM / capital / ITC-blocked and vendor state / TDS section;
  migrations 4230–4235, PERM-EXP-004. Tests: prototype 1960/1961 (golden mapping keys left for the integrator), server Accounting 154,
  Expenses 64, MoneyMasters 24, Import 1968. Stubs to replace: `tdsForBill`, cash guard. `05 Delivery Evidence/Accounting/ACC2-PURCHASE-DONE.md`.
