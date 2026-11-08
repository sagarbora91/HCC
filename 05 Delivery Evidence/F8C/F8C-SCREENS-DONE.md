# F8C-SCREENS DONE (lane f8c-screens, Sonnet 5.5), 8 Nov 2026

Branch claude/f8c-screens (from main 8f99a17c). Head hash: see `git log -1` (this file is in the same commit as the code). Nothing pushed.

## How the screens were seen
Built the prototype (`npm run build`), served `dist/index.html` on port 5184 with a scratch server, and loaded the golden stay into the browser's IndexedDB. The golden stay was **created by running the real commands** (a Node script bundling `src/services/mock`, same steps as `tests/stream-f8-int.mjs`: Deccan Agro company, BOOK, REGISTER, CHECKIN, SF_FOLIO_OPEN room to B, UPI 1,000, POST_NIGHTS, laundry 500, food 1,200, cash 850, COMPANY_CREDIT_REQUEST, APPROVAL, CHECKOUT). **No seed or fixture was touched** (BASE / FY26 pins unchanged). Further runtime states: single folio, split open, credit pending, ready, checked out, Udhaar folio, company limit lowered. Real Chromium via Playwright, desktop 1440x900, tablet 1024x768 and 820x1180, light / dark / high contrast (desktop folio), text scale 1.0 and 1.3 (set through `--hcc-text`), automatic overflow scan (elements right of the dialog edge, clipped-x) plus console / pageerror capture.

## Numbers seen (all match golden)
Folio A 1,850 (net 1,700 + tax 150), folio B 9,450 (net 9,000 + 450), stay total 11,300, outstanding after the 1,000 advance 10,300; invoice A DEMO/2026-27/0001 net 1,700 CGST 75 SGST 75 total 1,850; invoice B /0002 GSTIN 27AAPFU0939F1ZV net 9,000 CGST 225 SGST 225 total 9,450. Limit lowered to 9,000: request / checkout refused with the D7 text. Credit note on A refused (SFQ-20), on B offered at 5 %.

## Defects found and fixed (UI only)
1. **The split screens had no stylesheet at all** (no `sf-*` rule anywhere). Unstyled: label and sub-text ran together ("A . Golden Split GuestBalance due"), edit rows were a flat flex row with a 130 px reason box, checkbox text under its box, invoice rows unspaced. Added the `F8C-SCREENS` block at the end of `03 Prototype/src/styles/folio.css` (tokens only, no hex; the dialog row needed `.summary-list>.sf-folio-edit` because `.summary-list>div` out-ranks a single class).
2. Folio bar wording "room post here" -> "new room charges post here"; after check-out it showed a stale "extras post here" and only folio A's invoice number in the header; now shows the folio's own invoice number (`Split.tsx` SfFolioBar).
3. Check-out settle sheet repeated three totals the check-out summary already shows (charges, net payments, deposit) and pushed the dialog into a scroll; kept only "Outstanding (sum of folios)" (`Split.tsx` SfCheckoutSheet).
4. Split dialog offered "Company account" although the stay already has a company folio (domain refuses `SF_COMPANY_FOLIO`) and listed the company again with "left"; now the kind and company list drop out. Companies are also limited to the booking's own company. Empty states added for "no company account" and "no credit guest". The info note now follows the chosen payer kind (company / Udhaar / other person).
5. Credit-note dialog on folio A said "GST at the stay's room-night rate (5 %)" (it read the whole booking, not the folio) and invited a request that the command then refuses. It now reads the folio view, says "the rate of folio B" on a split stay, and for a mixed-rate folio with no room night shows the SFQ-20 text up front and disables Submit (`modals.tsx` CreditNoteModal; imports `gstAdjustmentMixed`, `GST_ADJUSTMENT_MIXED_MSG` from `domain/gst`).
6. a11y: routing controls now in `role=group` "Routing of folio B", buttons "Save routing of folio B" / "Remove empty folio B" (before: several identical "Save routing" buttons).

## Not fixed / observations (no money-logic change made)
- `handlers/folio-split.ts` / company credit refusal text says "used ₹0" for the limit although folio B's own balance 9,450 is the amount asked; arithmetic is consistent (limit 9,000, needs 9,450) but "used" is confusing: wording only, owner/Opus to decide.
- Folio B (company) still offers "Record payment" in the folio pane and the sheet; the dialog note says a company folio is "not paid in cash". The domain accepts a bank / UPI / card payment (sheet text says so). Not changed.
- The discount dialog on folio A was not checked for the same up-front warning as the credit note (the command refuses it with the SFQ-20 text).
- At 1024 px the folio line table inside the narrow folio pane runs right of the pane (the pane clips; "Correct" column cut). It is the ordinary folio table, not split-specific; not changed.
- The check-out dialog still scrolls inside its body at 1440x900 for a split stay (guest-feedback block below the sheet; its own layout is rough: comment and complaint category fields run on one line). Pre-existing single-folio markup, not changed.
- Mobile / phone screens, `kds` and `pm` pages were not part of this lane and were not looked at. Phone check-in routing editor (other lane) untouched, `clients/sync/*` untouched.
- Console: no errors or page errors in any run.

## Files changed
- `03 Prototype/src/features/folio/Split.tsx` (switcher/bar wording and invoice, settle sheet, split dialog kinds / filters / empty states / aria)
- `03 Prototype/src/features/folio/modals.tsx` (CreditNoteModal, marked F8C)
- `03 Prototype/src/styles/folio.css` (F8C-SCREENS block at the end)
- `03 Prototype/tests/stream-split-ui.mjs` (+1 test `[F8C-SCREENS]`: every `sf-*` class used by Split.tsx has a rule in folio.css, tokens only, no duplicate totals, folio-specific accessible names)

## Tests actually run (through Invoke-Serialized, label f8c-screens)
- `npx tsc --noEmit`: exit 0 (after the last edit).
- `HCC_TEST_ONLY=stream-split node tests/run.mjs`: 71/71 passed (includes the new test and the 7 `[F8-SF-UI]` tests).
- `HCC_TEST_ONLY=stream-f8 node tests/run.mjs`: 74/74 passed (before the last two small edits; the info-note ternary and empty states came after).
- `npm run build`: OK (4934 KB).
## Tests NOT run
Full `npm test`; `tools/fit-screen-check.mjs` (the modal fit check was not run); server tests (no server code touched); phone / mobile viewports.

## Defaults relied on
SFQ-1 (max 4 folios), SFQ-20 (mixed-rate folio refuses discount / credit note: the UI now says so up front), SFQ-13 (company credit approval), SFQ-8. No owner answers assumed beyond the stated defaults.

## Screenshots
`05 Delivery Evidence/F8C/screens/before/` (unstyled originals: d1440 folio modal, folio B, split dialog, checkout, invoice A/B, tablet 820 checkout) and `.../after/` (d1440 / t1024 / t820 light + dark, hc for the folio; split dialog, checkout, limit refusal, credit note A warning and B, Udhaar split and check-out). Names: `<viewport>-<theme>-s<scale>-<state>-<screen>.png`. The before set is smaller than the after set (the first look was exploratory).
