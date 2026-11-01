# ACC3-AGEING DONE — outstanding with ageing + GST summary (lane acc3-ageing, 1 Nov 2026)

Branch `claude/acc3-ageing` (cut from `claude/acc3` 829f0d91). Contract `02 Planning/agents/ACC3-REPORTS-CONTRACT.md` §1, §3, §9, §10.

## Commits
- `34f078d1` cherry-pick of acc3-pl `4bc97aa4` (its `AccountingReportContracts.cs` §2 records), so my records append after them without an EOF conflict. The integrator merges acc3-pl first; the identical change merges cleanly.
- `a21fbf72` prototype + server ageing and GST summary, tests.
- The final commit adds this DONE file.

## Files
- Prototype: `03 Prototype/src/domain/streams/accounting-ageing.ts` (`accAgeing`, `accAgeingAllocate`, `accAgeingBucket`), `accounting-gst-summary.ts` (`accGstSummary`), plus one `// ACC-3` export line in `src/services/mock/index.ts`. Tests are in `tests/stream-accounting-ageing.mjs` (6).
- Server:
  - `Hcc.Application/Accounting/AgeingQueries.cs` (`AgeingRules`, `AgeingQueries`, port `IAgeingAllocationSource`)
  - `GstSummaryQueries.cs` (`GstSummaryRules`, `GstSummaryQueries`, port `IGstr1Source`)
  - `Hcc.Infrastructure.SqlServer/Accounting/SqlAccountingStore.Ageing.cs` (`SqlAgeingAllocationSource` over `ops.vendor_payment_alloc`)
  - `Hcc.Api/Endpoints/AccountingAgeingEndpoints.cs` (`GET /accounting/ageing?asOf&side`, `GET /accounting/gst/summary?month`)
  - One `// ACC-3` line in `HccComposition.cs`
  - Records appended to `Hcc.Contracts/Accounting/AccountingReportContracts.cs`
  - Tests in `tests/Hcc.Accounting.Tests/Ageing/AgeingRulesTests.cs` (14)
- Migrations: none. 4262–4265 are unused because the ageing is a selector. No SCHEMA bump.

## Tests (through the lock)
- Prototype `npm test`: 2095 → **2101 / 2101** passed. `npx tsc --noEmit` is clean.
- Server `dotnet build Hcc.slnx`: 0 warnings, 0 errors. `dotnet test tests/Hcc.Accounting.Tests -maxcpucount:1`: 235 → **249 / 249**.

## Golden (ACC-RULEBOOK April 2026, both sides)
- **Receivable on 29 Apr:** Deccan Agro ₹63,000 (₹60,000 + GST) is open in 0–30. T7a paid the opening ₹40,000 oldest-first. On 30 Apr nothing is open (T7b).
- **Payable on 30 Apr:**
  - Morning ₹24,000 in 0–30. T11's ₹60,000 paid the opening ₹25,000 first, then ₹35,000 of the 5 Apr bill.
  - Kaveri ₹42,000 in 0–30.
  - The total ₹66,000 ties to the 2200 trial balance.
- **GST summary April:**
  - Output ₹29,000, all at 5 %: B2B ₹3,000 (on taxable ₹60,000) and B2CS ₹26,000.
  - ITC nil, TCS ₹100, 1340 balance ₹100.
  - Payable ₹29,000, challan ₹28,900.
  - Both ties are true and no differences are listed.

## Rules as built
- Bill ref = the voucher source id/key. An opening voucher's ref is `opening`, which always sorts as the oldest item.
- Credits are applied in this order:
  1. A vendor return or reversal goes to its own bill (prototype only, see open item 3).
  2. ACC-2's explicit allocations win (prototype `vendorPayments[].allocations`; server `ops.vendor_payment_alloc` of payments that are not void).
  3. Whatever is left is applied oldest-first.
- A later `vendor-payment:<id>/void` cancels its own payment.
- Row `open` = the party's ledger balance on asOf. When Σ buckets ≠ open, the row is `flagged` with a `note` (e.g. an unallocated advance).

## Contract deviations / open items (for the coordinator)
1. **Optional fields added.** The ageing row has `flagged?`, `note?`, `advance?`, needed to express "row is flagged". The GST summary output scalars are **tax** paise, and `outputTaxable` (same keys) carries the taxable values. I also added `bookOutput` and `cashPaid` (Dr 1340 by `gst-payment` vouchers in the month). The server `GstSummaryView.Setoff` is a reduced `GstSummarySetoffView`; it does not include the use matrix.
2. **Permission:** the contract says PERM-ACC-003 "as the trial balance", but on the server PERM-ACC-003 is Unlock, and the trial balance uses `AccountingPermissions.View` (PERM-ACC-001). I used View. Please confirm and fix the contract text.
3. **Server returns:** a vendor return / reversal is allocated oldest-first on the server, not to its own bill. The `Voucher` does not carry the original bill id. Fix: either add the bill id to the voucher, or add a store read of `exp.expense.return_of` / `reversal_of` to `IAgeingAllocationSource`.
4. **No credit-terms master:** `due` = the bill date, and ageing counts days from the bill date (rulebook 7.6).
5. **Posted set-off check:** the prototype `gstr3b` tie also compares a posted set-off journal with the computed one. The server only checks output and arithmetic.
6. **GSTR-1 on the server:** the GST summary calls `InvoiceQueries.Gstr1Async` as the caller, which needs PERM-INV-001. An Accountant without it would get a 403 on the summary. Integrator: run it with View only, or grant the permission.
7. **Server ageing load:** it reads all days from the first source (no month-row opening), because it needs bill-level detail.

## Integration needed
- `services/mock/index.ts` and `HccComposition.cs`: I created the `// ACC-3` block myself (it was absent). Other lanes' blocks will conflict trivially; keep one block with every lane's line.
- `AccountingReportContracts.cs`: keep acc3-pl's §2 records first, then mine.

## HANDOFF bullet
- ACC-3 acc3-ageing: outstanding receivables / payables with bill-wise ageing (explicit ACC-2 allocations win, opening = oldest, flagged when not tied), and the GST summary tied to GSTR-1 and the GST month. Prototype `accAgeing` / `accGstSummary`; server `GET /accounting/ageing`, `GET /accounting/gst/summary`. Golden April 2026 passes on both sides.
