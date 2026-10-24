# M2/M3 progress checkpoint (24 Oct 2026) — stopped at owner's request

## Done
- **M1 foundation** — see `M1/M1_Delivery_Summary.md`.
- **M2/M3 built by six parallel streams and merged into one build in `03 Prototype`:**
  - **A — Calendar & reservations:** 14-day calendar, availability search with exclusion reasons, 2-hour holds (confirm/expire), room capacity, complete booking form, printable confirmation.
  - **B — Guests & check-in:** guest profiles (masked ID, duplicate warning, party, registration card), 6-step guided check-in including walk-in, in-house stays with room-segment history.
  - **C — Payments & folio:** mixed tenders, refunds (request → approve → execute; executor ≠ requester), company-credit checkout (scenario S4), correction reversals, invoice and credit-note numbers. "Post stay charges" button removed.
  - **D — Housekeeping:** reject reason, stayover tasks, repair requests to Maintenance, dated room blocks, backup simulation page, roadmap page.
  - **E — Access & configuration:** 45-action × 9-role policy engine with effective-access explanations, user exceptions, approvals & audit inbox, effective-dated tariffs/tax, impact previews.
  - **F — Cashier & closing:** cashier shift with difference approval, expense draft/reverse, resumable day close (scenario S5), closing records, 6 report tabs with drill-down/CSV/print, full 3-day guided walkthrough.
- **Integration fixes:**
  - blocks judged by tonight's date;
  - expired blocks released on next day;
  - refund executor must differ from requester;
  - policy approvals no longer block day close;
  - booking dialog cleanup;
  - SCHEMA = 3.
- **Checks in the cloud workspace:** type check passed; 155/155 tests passed; build passed (699 KB).

## Not done yet (next session)
1. Browser walkthrough of all 12 S0 steps through the UI (BL-023 / AC-001).
2. Responsive and accessibility check at 1440 / 1024 / 390 px for every page and role, with screenshots (BL-035 / AC-023).
3. Click through every page as each of the 10 demo users, looking for crashes.
4. Update the acceptance matrix and decision register, and write the M3 delivery summary.
5. Optional clean-up: delete `tests/.generated/domain.mjs` (stale).

## To run on this PC
In `03 Prototype`: `npm install` (first time), then `npm test`, `npm run build`, `npm start` → http://127.0.0.1:4173
