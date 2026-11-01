# ACC3-CARRY DONE

Lane acc3-carry (Opus) · branch `claude/acc3-carry` · worktree `E:\Projects\HCC\worktrees\acc3-carry` · base `829f0d91` (claude/acc3 = main).
Contract: `02 Planning/agents/ACC3-REPORTS-CONTRACT.md` §7, §9, §10. Carry-overs from `ACC2-DONE.md` §5.

## Commits
- 15ca82f3 ACC-3 acc3-carry: C1-C11 carry-overs on both sides
- 464d68af prototype green (RBAC rows, TDS sub-section text, SetGstOnAdvances body)
- (this file + server test fix) — see `git log 829f0d91..HEAD`

## Per item

| # | Status | Prototype | Server | Tests |
|---|---|---|---|---|
| C1 put to use / disposal | Done (rulebook F4, see ruling 1) | `ACC_ASSET_PUT_TO_USE {asset,date}`, `ACC_ASSET_DISPOSE {asset,date?,proceeds,method?,reason?}` (handlers/accounting-assets.ts); `accAssetDepreciationFactor` (F2 ≥180 days full, else ½), `accFyEnd`, `accAssetWdv(s,asOf)` / `accAssetWdvTotal` (block book value from the vouchers — for acc3-pl's balance sheet), `accAssetDisposalFlag` | `AssetService` (SetAssetPutToUse, DisposeFixedAsset; endpoints in ExpenseVendorEndpoints), `AssetRules`; sale = manual Receipt Dr tender · Cr block via `IGstManualVouchers`; migration 4270 | proto C1 ×2; server `CarryRuleTests.C1…`, Expenses `CarrySqlTests.C1…` |
| C2 return vs capital purchase | Done | `accAssets` returns cost net of posted returns (`returned` field; stored row keeps the purchase cost — derived so both approval paths work) | `FixedAssetView.CostPaise` net, `OriginalCostPaise`, `ReturnedPaise` (SQL computes) | proto C2; Expenses `C2…` |
| C3 re-allocate payment | Done | `EXP_VENDOR_PAY_REALLOCATE {payment, allocations[]}` Owner/Manager/Accountant; `reallocations[]` history on the payment; `expVendorReallocationRefusal` | `ReallocateVendorPayment` (PERM-EXP-004), audit row `ops.vendor_payment_realloc` (4271, append-only) | proto C3; Expenses `C3…` |
| C4 GSTIN vs state | Done (warning) | `expVendorGstinWarning`, `expPurchaseEntry().warning`, appended to EXP_VENDOR_PAY text | `VendorGstin.Warning`, `VendorPaymentResult.Warning`; `ExpenseVendorRow.Gstin` | proto C4; server `C4…`, Expenses `C3…C4` |
| C5 B9 per customer | Done | already per customer (1120/UD-<mobile|name>) — test added | `SqlFlowsStore` Udhaar day total across the customer's credits (same mobile, else same name); scope per credit | proto C5; server `C5…` (SQL) |
| C6 TDS per sub-section | Done | remit/outstanding/deductee pool keyed by code (`rent:plant` = 194-I(a), `professional:technical` = 194J(a)); `tdsOldOf`, `tdsCodeLabel` | `TdsService.Outstanding/OldOf/LabelOf/Narration`, `TdsReports.Deductees` by code; CHECK widened (4272) | proto C6; server `C6…` |
| C7 Form 168 on remittance | Done | `ACC_TDS_REMIT {…, form168}` + `ACC_TDS_FORM168 {remittance, form168}`; `accountingX.tdsForm168`; `tdsForm168Of`; receivable report reads them by default | `acc.tds_remit.form168_json` (4272; the contract's `acc.tds_remittance` is this table), `SetTdsForm168`, `TdsRemittanceView.Form168`, receivable `Form168` | proto C7; server `C7…` |
| C8 B11 loans via bank | Done | `accLoanCashRefusal` (guards) called from `accManualProblems` | `CashGuard.LoanCashRefusal` in `ManualVoucherRules.Problems` | proto C8; server `C8…` |
| C9 live set-off | Done (staleness, see ruling 3) | (already live) | `MonthAsync` shows the stale-set-off blocker and the pay-run blocker (`IPayRunMonthCheck.ViewBlockerAsync`); `LockMonthAsync` refuses a stale set-off | server `C9…` |
| C10 dated advances switch | Done | `CMPProfile.gstOnAdvancesFrom`; `ACC_GST_ADVANCES_SET {on, from?}` (default business date); `accGstOnAdvanceOn`; profile save keeps the date | `cmp.profile.gst_on_advances_from` (4273), `SetGstOnAdvances {On, From}`, projector skips deposits before it, `AccountingMonthLocksView.GstOnAdvancesFrom` | proto C10; server `C10…` (SQL) |
| C11 TDS rates → configX | Done | `tdsRatesToChanges`; sync patch loader `accountingTdsRatesLoader` (configX ← GET /accounting/tds) | rates already on `TdsStateView` (asserted) | proto C11; server `C7…C11` |

## Rulings for the coordinator (contract vs rulebook / RBAC)
1. **C1 disposal**: contract says profit/loss to 4960 / 5400; rulebook F4 says Cr block with the sale value and a "short-term capital gain / loss — CA" flag, no automatic entry. Rulebook beats contract → built F4; flag stored (`disposed.flag` / `disposal_flag`). Confirm.
2. **C1 permission**: contract §9 says PERM-ACC-004, which is the Owner-only ledger mapping (DEC-063 G11). Used **PERM-ACC-010** (Owner + Accountant, manual vouchers) instead; prototype checks role Owner/Accountant. Confirm.
3. **C9**: server set-off sources stay as posted; "live until the lock" = the month view shows live figures plus a blocker when the posted set-off no longer matches, and the lock refuses until it is re-posted (SetoffAsync already replaces). No auto re-post.
4. **C8**: any cash line on a voucher touching 2400 is refused (contract), no ₹20,000 threshold (rulebook B11 text) — stricter.
5. **C6**: a remittance now covers exactly its code; a base-code remittance no longer covers variant bills (old data: none known).
6. `EXP_VENDOR_PAY_VOID` untouched (Owner-only, DEC-175).

## Files touched outside the ownership table (one-line hooks, no other ACC-3 lane owns them)
`accounting-manual.ts` (B11 call + `accManualIsCash`), `ManualVoucherService.cs` (B11 line), `compliance.ts` (type field), handlers `compliance.ts` (keep the date on profile save), `accounting.ts` (optional `ACCAsset` fields, `tdsForm168`), `expenses-scc.ts` (`reallocations?`), `ExpenseMasters.cs` + `SqlExpenseRegisterStore.cs` (vendor GSTIN), `SqlFlowsStore.cs` (C5 SQL), `AccountingVoucherProjector.cs` (C10 date arg), `TdsCalculator.cs` (pool by code), tests `stream-rbac-matrix.mjs` (4 CMDS rows), `stream-accounting-tds.mjs` (deductee label now `194-I(b)`), `TdsSqlTests.cs` (decorator forwards 2 new store methods), Expenses harness (PERM-ACC-010 grants). `handlers/index.ts` gained an `// ACC-3` block.

## Tests (through the lock)
- Prototype `npm test`: before 2095 → **2106/2106**; `npx tsc --noEmit` clean.
- Server `dotnet build Hcc.slnx`: 0 errors, 0 warnings.
- `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1`: **244/244** (ACC-2 goldens / rulebook month green; no golden regenerated).
- `dotnet test tests/Hcc.Expenses.Tests -maxcpucount:1`: **67/67**.
- Hcc.Payroll.Tests not touched (only `PayRunMonthCheck` in Accounting gained a read path).

## Migrations
4270 fixed_asset put-to-use/disposal columns · 4271 ops.vendor_payment_realloc · 4272 tds_remit sub-section CHECK + form168_json · 4273 cmp.profile.gst_on_advances_from. Locked with `--accept-out-of-order` (below the 46xx LG block, as contract §10 numbers them) + Hcc.slnx. 4274–4276 unused. No SCHEMA bump.

## Open items
- UI: no screen yet for put-to-use / disposal, re-allocation, Form 168 entry, or the switch date — acc3-ui / integrator (commands exist on both sides with sync mappings).
- Server F4 block balance reads projected sources; in the Expenses harness (no projector) the flag fires — production fine.
- CA to confirm: F4 treatment, sub-section split, B11 threshold.

## Integration needed
- acc3-ui: asset register actions (ACC_ASSET_PUT_TO_USE / ACC_ASSET_DISPOSE), payment "Re-allocate" (EXP_VENDOR_PAY_REALLOCATE), TDS remittance Form 168 inputs (ACC_TDS_FORM168), GST-on-advances date field (`from`).
- acc3-pl: balance sheet may use `accAssetWdv(s, asOf)` / `accAssetWdvTotal` from accounting-assets.ts.
- tests/_role-reads.json unchanged (reuses GET /accounting/tds).

## HANDOFF bullet
- **ACC-3 acc3-carry (1 Nov 2026):** ACC-2 carry-overs C1…C11 on both sides — asset put-to-use / disposal (F4 flag, PERM-ACC-010), returns reduce asset cost, vendor-payment re-allocation, GSTIN/state warning, Udhaar B9 per customer, TDS per sub-section + Form 168 on the remittance, B11 loans only via bank, stale set-off + pay-run blocker in the month view, dated GST-on-advances switch, TDS rate versions into configX. Migrations 4270–4273. Prototype 2106/2106, Accounting 244/244, Expenses 67/67. Rulings to confirm in ACC3-CARRY-DONE.md.
