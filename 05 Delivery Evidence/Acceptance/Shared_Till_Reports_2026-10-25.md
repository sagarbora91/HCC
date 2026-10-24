# Shared-till reporting and POS wording — 25 October 2026

Completed source fixes for the cash-report discrepancy observed in BL023 and POS wording defect D-01.

## Changes
- Expenses & cash now includes POS cash-sale receipts in cash inflows and signed POS cash-void receipts in outflows, filtered by receipt date. Guest payment/deposit behavior is retained.
- Drill-down rows retain the POS receipt ID, check ID, method and signed amount. The same report pack feeds screen totals and CSV export.
- UPI/card receipts and room-folio charges do not add drawer cash. No receipt or financial-history migration was performed.
- Updated report dictionary and settlement-dialog wording to describe the shared front-desk till.
- Other guest-folio revenue/receipt reports were not converted into consolidated POS reports by this change.

## Verification
Typecheck and HTML build passed; prototype whitespace check passed. The initial full suite passed 499/500: all 497 pre-existing tests and two new tests passed. The new void scenario incorrectly expected an above-limit void to execute without approval. Its focused rerun then exposed a missing kitchen-send prerequisite before settling another fixture check. Corrected those test steps to follow existing application rules; no approval/kitchen rule or production assertion was weakened. The final focused suite passed **3/3**. A final full 500-test run was not performed.

Verified: BASE cash in ₹7,550 includes restaurant cash ₹550 once; expected drawer ₹10,750; pending void leaves cash unchanged; approved void adds −₹550 outflow; folio transfer and UPI settlement add no cash; historical 1 April and 23 October flows reconcile to stored expected closing cash; inclusive multi-day sums and empty ranges; CSV amount and drill-down receipt reference.

Logs: `05 Delivery Evidence/Assembly/report-till-tests-2026-10-25.log` (initial full run) and `report-till-focused-2026-10-25.log` (final focused checks).

Built HTML SHA-256: `879ce6805db071c124530193e795bb41c631901ce0d70a067aaf0b20a44ad9d6`.

No browser/device walkthrough or APK rebuild performed. D-01 is source-fixed; confirm its wording on the rebuilt APK during remaining acceptance. This does not close W0-D.
