# ACC2-TDS DONE

Lane **acc2-tds** (Opus) · branch `claude/acc2-tds` · worktree `E:\Projects\HCC\worktrees\acc2-tds` · base `b78cb069` (ACC-2 wave setup). Not pushed.
Contract: `02 Planning/agents/ACC2-POSTING-CONTRACT.md` §4 (A9), §5, §9, §10. Rulebook §2.9, §4, §5 A9 / C9 / E5 / E6, §8.2 T7a / T7b, §11 S-13 / S-17; DEC-146, DEC-058.

## Commits

```
d54035de ACC-2 tds (prototype): dated TDS-rate table, tdsForBill, ACC_TDS_SETTINGS + ACC_TDS_REMIT, deductee report, GRP_COMPANY_PAYMENT tds -> Dr 1360, Form 168 reconciliation, sync area; default map keys; accounting golden regenerated
8cbdb3f8 ACC-2 tds (server): TdsRates + TdsCalculator, TdsService, SqlTdsStore, migrations 4242-4245, company payment rule Dr 1360, AccountingTdsEndpoints, tests
20efb571 ACC-2 tds: Groups test projector assertion; Access RBAC v2 matrix row PERM-ACC-013
(+ this DONE file)
```

## Files

**Prototype (`03 Prototype`)**
- new `src/domain/tds.ts` — `TDS_SECTIONS`, `TDS_SEED` (15 dated keys `tds.<row>.<field>`, from 2026-04-01, every row "CA to confirm", s.393 text + old section + source S-13), `tdsValueOn` (same walk as `gstValueOn` over `s.configX.changes`), `tdsRateBps`, `tdsTable`, `tdsSettings`, `tdsForBill`, `tdsTaxable`, `tdsCompanyExpected` (A9 check figure), `tdsReceivableProblem`, `TDS_SECTION_OPTIONS`, keys `ACC_TDS_PAYABLE_KEY` / `ACC_TDS_RECEIVABLE_KEY`. Imports only gst.ts + money-master (no accounting.ts → no cycle for the purchase lane).
- new `src/domain/streams/accounting-tds.ts` — re-exports tds.ts; registers the TDS rows in Master configuration › Money and tax (`registerSettingsGroup({id:'tds',group:'money'})`, Owner-only, dated, illustrative); `tdsPeriod`, `tdsRemittances`, `tdsRemitNarration`, `tdsBills`, `tdsPayableOutstanding`, `tdsDeducteeReport` + `tdsDeducteeCsv`, `tdsReceivableReport` + `tdsReceivableCsv`, `tdsSectionChoices`.
- new `src/services/mock/handlers/accounting-tds.ts` — `ACC_TDS_SETTINGS`, `ACC_TDS_REMIT`, **local stub `accManualSave`** (see Integration).
- new `src/clients/sync/accounting-tds.ts` — area `accounting-tds`: `ACC_TDS_SETTINGS → SetTdsSettings`, `ACC_TDS_REMIT → RecordTdsRemittance`; `accountingTdsLoader` exported, not registered (Integration).
- new `tests/stream-accounting-tds.mjs` (10 tests, `[ACC2-TDS]`).
- edited: `src/domain/streams/accounting.ts` (ACC_DEFAULT_MAP `tax:tds-receivable` 1360, `tax:tds-payable` 2150; the company-payment section of `accLiveVouchers` gains the 1360 line; `accMapRows` counts it), `src/domain/streams/groups.ts` (`GRPCompanyPayment.tds?`, `grpPaid` adds it), `src/services/mock/handlers/groups.ts` (`GRP_COMPANY_PAYMENT` `tds`), ACC-2 append lines in `handlers/index.ts`, `services/mock/index.ts`, `clients/sync/registry.ts`; `tests/stream-rbac-matrix.mjs` (CMDS `ACC_TDS_SETTINGS` OWN, `ACC_TDS_REMIT` OWN ACC).
- `08 Production/tests/Hcc.Accounting.Tests/Golden/accounting-golden.json` regenerated (only `defaultMapping` gains the two keys).

**Server (`08 Production`)**
- new `src/Hcc.Domain/Accounting/TdsRates.cs` (sections, seed, `ValueOn`, `RateBps`, `ValidateValue`, `Fy`), `TdsCalculator.cs` (**`TdsCalculator.ForBill(TdsBill, TdsVendor?, IEnumerable<TdsPriorBill>?, TdsSettings?, overrides?) → TdsResult(Paise, Section, TaxablePaise, BasePaise, Bps, Reason)`**, `PayableKey` / `ReceivableKey`, `CompanyExpected`, `ReceivableProblem`, `TdsReports.Deductees`).
- new `src/Hcc.Contracts/Accounting/TdsContracts.cs`; `src/Hcc.Application/Accounting/TdsService.cs` (+ `ITdsStore`, `ITdsRemittancePoster`, `AccountingTaxPermissions.Tax` = PERM-ACC-013, `ForBillAsync` for the purchase lane); `src/Hcc.Infrastructure.SqlServer/Accounting/SqlTdsStore.cs`; `src/Hcc.Api/Endpoints/AccountingTdsEndpoints.cs` (+ one `// ACC-2` line in `HccComposition.cs`).
- edited: `AccountingSource.cs` (`SourceRules.CompanyPayment(..., long tdsPaise = 0)`), `ChartOfAccounts.cs` (two default keys), `AccountingEventMapper.cs` (passes `e.TdsPaise`, narration), `AccountingContracts.cs` (`CompanyPaymentRecordedEvent(..., long TdsPaise = 0)`), `CompanyContracts.cs` (`RecordCompanyPaymentRequest.TdsPaise`, `CompanyPaymentResult.TdsPaise`, `CompanyPaymentView.TdsPaise`, all defaulted), `Groups/CompanyCredit.cs` (TDS settles the receivable, guards, event, audit), `SqlCompanyCreditStore.cs` (`tds_paise` read / insert).
- migrations **4242** `grp_company_payment_tds` (tds_paise, ≤ 20 % check), **4243** `acc_tds_rate` (acc.tds_rate overrides + acc.tds_setting switches), **4244** `acc_tds_remit` (append-only, manual_voucher_id linkable once), **4245** `acc_tds_rbac_seed` (PERM-ACC-013 OWN, ACC); `migrations.lock.json` via `--add`; `Hcc.slnx` lines.
- tests: new `tests/Hcc.Accounting.Tests/Domain/TdsTests.cs` (7), `Sql/TdsSqlTests.cs` (1), `tests/Hcc.Groups.Tests/Sql/CompanyPaymentTdsSqlTests.cs` (1); `tests/Hcc.Access.Tests/Sql/RbacV2MatrixTests.cs` one `LaterPermissions` row PERM-ACC-013.

## What was built

- **§5 rate table (dated)** — rows rent (₹50,000 / month; 10 % land-building, 2 % plant via `rent:plant`), contractor (₹30,000 single / ₹1,00,000 FY; 1 % individual / 2 % other), professional (₹50,000 FY; 10 %, technical 2 % via `professional:technical`), commission (₹20,000 FY; 2 %). Owner edits them in Master configuration (`SETTING_SET tds.*`, dated, Tech admin / Manager / Accountant refused); server: `SetTdsRate` (PERM-CFG-003, never backdated, reason) into `acc.tds_rate`.
- **§5 `tdsForBill`** — 0 unless `hotelDeductsTds` and the vendor has `tds` + a known `tdsSection`; taxable = amount − GST (Circular 23/2017); threshold on the vendor's total for the period (month for rent, FY otherwise, plus the single-bill test for contractors) with this bill included; the crossing bill also carries the period's earlier untaxed bills (old 194C/J/H proviso); round(base × rate) half away from zero in paise, capped at the bill. Vendor is credited net (amount − TDS); Cr 2150 via `tax:tds-payable` is the purchase lane's posting.
- **§4 switches** — `ACC_TDS_SETTINGS {hotelDeductsTds?, tdsDeductorType?}` Owner only, written on `complianceX.profile` (other profile fields kept); defaults on / 'other'. Server `SetTdsSettings` → `acc.tds_setting`.
- **§5 remittance (E5)** — `ACC_TDS_REMIT {month, section, amount, method, challan, date?}` Owner / Accountant: guards (month ≤ business month, payment date within month … business date, bank method only, challan 3–40, amount ≤ held back − remitted incl. pending); saves manual template `tds-remittance` Payment Dr 2150 · Cr 1030/<bank> (Owner approved, Accountant pending). Server `RecordTdsRemittance` → `acc.tds_remit` + `ITdsRemittancePoster`.
- **Deductee report** — vendor, PAN, section, bills, taxable, TDS, remitted (approved remittances allocated per section + month, oldest bill first), pending; FY ('2026-27') or month ('2026-04'); CSV. Server `GET /accounting/tds/deductees?period=`.
- **§4 A9** — `GRP_COMPANY_PAYMENT {…, tds?}`: amount + tds ≤ receivable, tds ≤ 20 % of the sum; Receipt Dr tender (amount) · Dr 1360 (tds) · Cr 1110/<company> (amount + tds); rulebook figure tested: Deccan Agro taxable ₹60,000 → ₹6,000 (T7b; `tdsCompanyExpected` / `TdsCalculator.CompanyExpected`), ₹40,000 → 0 (T7a). **Form 168 reconciliation** `tdsReceivableReport(s, fy, form168?)` by company and month, the 1360 ledger beside it, Form 168 difference, CSV; server `GET /accounting/tds/receivable?fy=`.
- Names agreed with the coordinator: server `TdsCalculator.ForBill(...)`; events carry `tdsPaise` (`CompanyPaymentRecordedEvent.TdsPaise`).

## Tests

All through `tools\Invoke-Serialized.ps1` (label acc2-tds); logs in `E:\Projects\HCC\worktrees\acc2-tds\.scratch\` (uncommitted).

| suite | before | after |
|---|---|---|
| prototype `npm test` | 1948 / 1948 | **1958 / 1958** (+10 `[ACC2-TDS]`) |
| `npx tsc --noEmit` | 0 errors | 0 errors |
| `dotnet build Hcc.slnx` | 0 warnings | 0 warnings, 0 errors |
| `Hcc.Accounting.Tests` | 147 | **155 / 155** (+7 TdsTests, +1 TdsSqlTests) |
| `Hcc.Groups.Tests` | 45 | **46 / 46** (+1 CompanyPaymentTdsSqlTests) |
| `Hcc.Access.Tests` | 97 | **97 / 97** (matrix row added, count unchanged) |

Commands: `npx tsc --noEmit`, `npm test` (`.scratch\proto.ps1`); `dotnet build Hcc.slnx` (`.scratch\build.ps1`); `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1 --logger trx`, same for `tests/Hcc.Groups.Tests`, `tests/Hcc.Access.Tests`. Hcc.Platform.Tests (migration lock) not run — the lock was written by `migrations-lock.mjs --add`. FY26 hash pins: not moved (no TDS in the history; company payments without `tds` post as before).

## Open items

1. **`tdsDeductorType` semantics (contract vs law, flagged, built as the contract says):** the contract makes the firm profile's `tdsDeductorType` the rate column. Under old 194C the 1 % / 2 % column follows the **deductee** (the contractor is an individual / HUF or not), not the hotel. Suggest a vendor-level `deducteeType` later (CA to confirm); default here 'other' (the higher rate).
2. TDS rounded to the paisa (contract); old s.288B rounded TDS to the rupee — CA to confirm.
3. The catch-up rule (crossing bill carries the earlier untaxed bills) and the cap at the bill amount are my reading of the provisos — CA to confirm.
4. Remittances are per base row (rent covers `rent:plant`; professional covers technical) — the challan codes 194-I(a)/(b) and 194J(a)/(b) are distinct in practice; split if the CA wants it.
5. A remittance does not mark the compliance "TDS" card done (the gst lane does that for statutory remittances); interest / late fee is a separate 5400 entry (message says so); E6 year-end Dr 3000 · Cr 1360 stays a manual journal.
6. Form 168 figures are passed to the report (not stored); a stored per-company figure would need a table.
7. Server deductee report and `ForBillAsync` read the purchase lane's columns (`ops.expense.tds_paise`, `ops.expense.tds_section`, `ops.expense_vendor.tds_section`) dynamically (`COL_LENGTH`) — until merged they return nothing. If the purchase lane names them differently, change `SqlTdsStore` (three SQL constants).
8. Note the expense table is `ops.expense` (the contract says `exp.expense`).

## Integration needed

1. **Manual lane:** delete the local `accManualSave` in `03 Prototype/src/services/mock/handlers/accounting-tds.ts` and import the manual lane's (same arguments: `(k, {date, type:'Payment', narration, reference, lines, template:'tds-remittance'})` → the voucher; adapt if its signature differs). Its posting section of `accLiveVouchers` then books the remittance. Server: register an `ITdsRemittancePoster` that calls `ManualVoucherService` save with template `tds-remittance`, lines Dr `voucher.PayableLedger` (2150) · Cr 1030/<bank the tender settles to>, inside the given transaction, returning the numeric manual voucher id (`SqlTdsStore.LinkVoucherAsync` parses it as a number). `AccountingTdsEndpoints` picks it up from DI (`sp.GetService<ITdsRemittancePoster>()`).
2. **Purchase lane:** replace its `tdsForBill` stub `(s,e)=>0` by `tdsForBill(s,{vendor:e.vendor,amount:e.amount,gstPaise:e.gstPaise??0,date:e.date,exclude:e.id}).paise` (returns `{paise, section, …}`; store `tdsPaise` / `tdsSection` on the expense at posting, then Cr `map('tax:tds-payable')` and the vendor net). Use the existing default keys `tax:tds-payable` (2150) / `tax:tds-receivable` (1360) — drop any duplicate key the lane added in `ACC_DEFAULT_MAP` / `ChartOfAccounts.DefaultMapping`. Vendor master field `tdsSection` options = `TDS_SECTION_OPTIONS`. Server: `TdsCalculator.ForBill(...)` or `TdsService.ForBillAsync(tx, propertyId, vendor, amountPaise, gstPaise, date, excludeExpenseId)`.
3. **`src/services/mock/handlers/compliance.ts` `CMP_PROFILE_SET`** rebuilds the profile and drops the ACC-2 switches. In the `x.profile={…}` literal, after `...(lodgingExpiry?{lodgingExpiry}:{}),` add:
   `...(x.profile?.gstOnAdvances!==undefined?{gstOnAdvances:x.profile.gstOnAdvances}:{}),...(x.profile?.hotelDeductsTds!==undefined?{hotelDeductsTds:x.profile.hotelDeductsTds}:{}),...(x.profile?.tdsDeductorType?{tdsDeductorType:x.profile.tdsDeductorType}:{}),`
4. **`src/clients/sync/money.ts`** `GRP_COMPANY_PAYMENT` send body: add `tdsPaise:Number(m.tds??0)`; **`src/clients/sync/money/wire.ts`**: `SrvCompanyPayment` gains `tdsPaise?:number`, and `companyPaymentFromServer` adds `...(p.tdsPaise?{tds:paise(p.tdsPaise,'tdsPaise')}:{})`.
5. **`src/clients/sync/admin.ts`** `SETTING_SET`: before the GST branch, `if(isTdsKey(key)){await c.api.command('SetTdsRate',{key,value:Number(cmd.value),effectiveFrom:cmd.effectiveFrom||null,reason:reason||'Owner change'});return;}` (import `isTdsKey` from the mock barrel); the hub's production load should add the `acc.tds_rate` versions (`GET /accounting/tds` `.rates`) to `configX.changes` as it does for GST.
6. **Loader:** add `accountingTdsLoader` to `accountingTds.loaders`, `'GET /accounting/tds'` to `reads.ts` READS (PERM-ACC-013), regenerate `tests/_role-reads.json` (OWN / ACC 200, the rest 403), fake route in `_sync-harness.mjs`.
7. UI lane: TDS tab on `tdsTable`, `tdsDeducteeReport` / `tdsDeducteeCsv`, `tdsReceivableReport` / `tdsReceivableCsv`, `ACC_TDS_REMIT`, `ACC_TDS_SETTINGS` (Owner settings), company payment `tds` field (`tdsCompanyExpected` as a hint).
8. Goldens: `accounting-golden.json` regenerated here (defaultMapping only); the integrator regenerates once after the merges anyway. Migrations 4242–4245 sit after purchase / manual in the merge order — no out-of-order flag needed for this lane.

## HANDOFF bullet

- **ACC-2 tds (lane acc2-tds, 28 Oct 2026):** dated TDS-rate table (rent / contractor / professional / commission, every row "CA to confirm", Owner-editable in Master configuration; server `TdsRates` + `acc.tds_rate`), `tdsForBill` / `TdsCalculator.ForBill` with the FY / month threshold (purchase lane calls it), firm switches `ACC_TDS_SETTINGS`, remittance `ACC_TDS_REMIT` (manual template tds-remittance, stubbed until the manual lane merges), deductee report + CSV, company payment net of TDS (Dr 1360, A9: Deccan Agro ₹60,000 → ₹6,000) with the Form 168 reconciliation; migrations 4242–4245, PERM-ACC-013. Prototype 1958 / 1958, server Accounting 155, Groups 46, Access 97. `05 Delivery Evidence/Accounting/ACC2-TDS-DONE.md`.
