# F8 SF-UI — DONE (lane sf-ui, Sonnet) — 8 Nov 2026

Branch claude/f8-sf-ui, base f8-setup + sf-domain step 1 (e6021c85, merged first).

## Built (all in 03 Prototype)
- `src/features/folio/SplitLogic.ts` (new, pure): sfSwitcher, sfCurrent, sfWithFolio, sfOpen, sfFolioOfModal, sfSettleSheet (one row per folio, Σ balances, blockers), sfInvoiceRows, sfFreeRoutes, command builders for SF_FOLIO_OPEN / SF_ROUTE_SET / SF_FOLIO_REMOVE, sfDocBooking.
- `src/features/folio/Split.tsx` (new): SfSwitcher (desktop button strip "A · Guest", "B · Deccan Agro (company)" with balance; phone chips), SfFolioBar, SplitBillModal ('sf-split': payer kind guest/company/Udhaar, company picker with "₹ left" from grpCredit, routes room/extras, reason, route editing, remove empty folio), SfRoutingEditor, SfCheckoutSheet (one sheet, every folio), SfInvoiceList.
- `src/features/folio/SplitRegister.tsx` (new) + F8-SF-UI lines in `src/app/registry.ts` (import + registration). Phone `payment` is wrapped (SfMmPayment) so MmPayment (not in my row) shows the folio's balance and sends PAYMENT {folio}.
- `src/features/folio/modals.tsx` (marked F8-SF-UI): BookingModal shows the chosen folio (lines/receipts/totals via sfView) and passes the folio to payment/charge/discount/credit note/invoice; PaymentModal, ChargeModal (charge + discount), CreditNoteModal add `folio`; CheckoutModal shows the one sheet for a split stay; InvoiceModal switches folio, lists invoice numbers per folio, prints per folio.
- `src/mobile/money/Folio.tsx`, `Checkout.tsx`: chips per folio, folio actions, one settle-all sheet, invoice per folio.
- `src/features/guests/CheckIn.tsx`: one F8-SF-UI line (routing editor in the last check-in step). NOTE: the brief says features/reservations; the CHECKIN dialog actually lives in features/guests.
- `src/domain/reports/documents/folio.ts`: folioDocument / creditNoteDocument draw from sfView; booking param may be "<id>#B" (doc registry index.ts untouched).
- A single-folio stay is unchanged (switcher/sheet render nothing; no `folio` on any command).
- Folio letter reaches PAYMENT/CHARGE/DISCOUNT/CREDIT_NOTE/invoice dialogs through `open({..., room:<letter>})` (Modal type has only id/room).

## Tests (actually run)
- `tests/stream-split-ui.mjs`: 7 tests [F8-SF-UI].
- `npx tsc --noEmit` via INV: exit 0, no errors.
- `HCC_TEST_ONLY=stream-split node tests/run.mjs` via INV: 70/70 passed (18 base + 7 sf-domain + 7 sf-ui ... filter includes base tests). Full `npm test` NOT run (brief rule 3: relevant tests only).
- `npm run build` via INV: built dist/index.html (4857 KB).
- Fit check `node tools/fit-screen-check.mjs --pages folios --no-modals` (PORT=4273, five viewports, own serve.mjs): PASS, 0 failing of 150. Report 05 Delivery Evidence/Fit/fit-check-20261108-0641.md. The split-folio dialogs/sheets were NOT fit-checked (--no-modals) and there is no seeded split stay, so the switcher strip itself was not exercised by the fit run; no browser look at the screens was done.

## Defaults relied on
SFQ-4 (billing permission, no new one), SFQ-1 (max 4 folios), SFQ-8 (₹0 folio no invoice), SFQ-13 (Manager ≤ ₹10,000 per company folio) — all enforced by sf-domain commands, not by the UI.

## Not done / deviations / integration needed
- Switcher is a pressed-button strip, not FitTabs: FitTabs is page-level (breadcrumb/crumb), and the strip sits inside a dialog/record pane.
- OTA credit and the company-credit panel of groups/credit.tsx are not used on split stays (own per-folio buttons in the sheet; OTA "move to channel" is not offered on split stays).
- Phone check-in (mobile/frontdesk/Arrivals.tsx) has no routing editor; Split bill is reachable from the phone folio sheet. Needs an F8-SF-UI line there (outside my row).
- subscreens.ts block left empty (no new sub-screens).
- Mobile Payment.tsx untouched (wrapper instead); a proper `folio` prop there would be cleaner.
- Refund/correction dialogs and deposit panel unchanged (folio comes from the receipt/line; deposit stays on A).
- Screens were not visually checked in a browser.
