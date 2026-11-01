# ACC3-UI DONE (lane acc3-ui, branch claude/acc3-ui)

## Files
- new `03 Prototype/src/features/accounting/reports3.tsx`: PlView, BalanceSheetView, OutstandingView, GstSummaryView, CashRatioView, ExportsView, TallyMastersPanel, ExportBar (desktop tables, phone cards via `phone` prop).
- new `acc3-views.ts`: contract §2/§3/§5 types (copied) and STUB selectors (`accPl`, `accBalanceSheet`, `accCashRatio`, `accAgeing`, `accGstSummary`, `accTallyMasters`, `accTallyVouchers`) plus stub export builders (`accReportTable`, `accReportCsv`, `accReportXlsx`, `accReportsZip`) over the existing trial balance.
- edited `Accounting.tsx`, `Mobile.tsx` (tabs/chips P&L, Balance sheet, Outstanding, GST summary, Cash ratio, Exports; masters panel above the Tally voucher export), `navmap.ts` (one keywords line).
- new `tests/stream-accounting-ui3.mjs` (11 tests, `[ACC3-UI]`).

## Carry-over screens (after merging claude/acc3-carry 4e990121; real commands, no stubs)
- new `carry-ui.tsx`: dialogs `acc-asset-put`, `acc-asset-dispose` (ACC_ASSET_PUT_TO_USE / ACC_ASSET_DISPOSE, Owner + Accountant), `exp-vendor-realloc` (EXP_VENDOR_PAY_REALLOCATE, allocations editor over the vendor's open bills plus the payment's own), `acc-tds-form168` (ACC_TDS_FORM168, one amount per company of the groups master), `acc-gst-advances` (ACC_GST_ADVANCES_SET {on, from}, Owner). Registered in `accounting/register.tsx` (desktop; phone uses the same modals).
- buttons: asset register rows (`tds.tsx` AssetsView, table + phone cards), "Re-allocate…" on vendor payments (`expenses/vendors.tsx`, table + phone), a Remittances list with "Form 168 figures…" in the TDS tab, a "GST on advances" button in the GST month actions (Owner).
- The firm-profile checkbox in compliance/modals.tsx still saves the old way without a date; the integrator may drop it in favour of the new dialog.
- Tests: prototype 2121/2121 (+4 carry-UI tests here, plus the carry lane's).

## Integration needed
- Delete the stub bodies in `acc3-views.ts`; re-export the real selectors from services/mock (names as contract). `reports3.tsx` imports only from `./acc3-views`, so one file changes.
- `accCashRatio(s, fy)` and `accTallyMasters(s, fy)` take `fy` as the FY start date (`2026-04-01`); adapt if the real selector wants `2026-27`.
- Export builders: contract §5 gives no names; UI uses `accReportTable(s,kind,args)` -> `accReportCsv/Xlsx(table)` and `accReportsZip(tables)`. Map to acc3-exports' real functions.
- Tally tab still downloads vouchers via existing `accTallyXml` + `ACC_TALLY_EXPORT` (keeps the export log); swap to `accTallyVouchers` when acc3-tally lands. The masters panel uses the stub.
- No new CSS: existing classes only; masking = role gate (books roles + `reports`), export buttons need `rbacCanExportVouchers`, masters need `rbacCanTally`. Phone 360 px / 48 px not browser-measured in this lane.
- Real P&L "by Tally group" and ageing bills come from the lanes; stubs return one "opening" bill per party and zero GST.

Tests: prototype 2095 (main) -> 2106 (+11), tsc clean, build not run (lock busy for long; integrator runs it).

## HANDOFF bullet
ACC-3 UI: six report tabs + Exports + Tally masters on desktop and phone, built against stubbed contract selectors.
