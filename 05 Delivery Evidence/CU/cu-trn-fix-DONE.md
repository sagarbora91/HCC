# CU lane trn-fix - DONE (11 Nov 2026)

Branch `claude/cu-trn-fix` (base c45bbce3). Findings F14, F17, F8, F19 of `06 Training/TRAINING_NOTES.md`.

## What changed (all under `03 Prototype`)

- **F14 cashier expected cash.** Cause: `shiftExpected` (the page) summed `shiftMovements`, which left out three drawer-cash sources that `drawer(s)` (the figure `SHIFT_SUBMIT` / `SHIFT_CLOSE` compare against) already counted: cash vendor payments (out), cash counter bills, cash hall advances / balances less refunds. Now `shiftMovements` and `shiftMovementsByDate` add one signed movement per source and date (kinds `vendor-payment`, `counter-bill`, `hall-receipt`, only when not zero), taken from the same helpers `drawer()` uses; `shiftExpected` puts counter / hall in `other` and vendor payments in `transfers`. The close command's rule is unchanged. `consistent` is true again. The cash book and the shift report follow, because they read the same movements.
  Files: `src/domain/streams/closing.ts`, `src/domain/selectors.ts` (two helpers now exported: `hallDrawerCash`, `lfCounterCash`), `src/features/closing/Cashier.tsx` (row label "... Owner drawings and vendor payments (net)", movement-kind names, footnote now includes "other").
- **F17 hall terms.** Cause: `s.hallX.templates` is empty until the first save; the Templates tab and `hallTncFor` read the stored list only, while the server-side handlers use `hallTemplates()` (seeded). Both now use `hallTemplates()`. Files: `src/features/hall/HallPage.tsx`, `src/features/hall/logic.ts`. The Owner's existing `HALL_TNC_TEMPLATE_SAVE` path is untouched and now reachable.
- **F8 wake-up Done.** I could not reproduce the refusal with the stock roles (Reception already holds `reservations`); the refusal appears only when that coarse grant is missing. `gsvCanWork` now also lets Reception, Manager and Owner work a front-desk Wake-up call whatever their coarse grants. Housekeeper / Maintenance rules unchanged. File: `src/domain/streams/services.ts`. Cashier still sees the banner Done button and is still refused (see open questions).
- **F19 OTA inbox.** FY26 only (BASE untouched): `scenarioPatches.FY26` in `src/fixtures/ext/channels.ts` moves the pending inbox rows that arrive before 27 Oct (CHM-F2..F6) and the matching outbox rows 4 days on (29-31 Oct, same length of stay) and makes the Classic party two adults (it was three; Classic holds two). Every pending new-booking row now passes the accept pre-check on 24 Oct and on 26 Oct. BN-88231 (CHM-F1) is unchanged.
- **Re-pins (intended, FY26 state changed):** the whole-state FY26 hash `ae519b00...` to `008af5db...` in `tests/stream-perf-store.mjs` and `tests/stream-masters-fixture.mjs` (the FY26 bookings hash is unchanged). No SCHEMA bump needed (new/changed values only inside existing optional slices), no migration.
- **New tests:** `tests/stream-cu-trn-fix.mjs` (14 tests).

## Tests run

- `node tests/_one.mjs tests/stream-cu-trn-fix.mjs`: 14 passed, 0 failed. With the four source files restored to base, 10 of the 14 fail (so they test the change).
- `npx tsc --noEmit`: clean.
- Full `npm test` once (shared code touched), through the lock: 3347 / 3350 passed. The 3 failures: `[MFX]` and `[PERF]` FY26 hash pins (expected; re-pinned afterwards, then `tests/stream-perf-store.mjs` + `tests/stream-masters-fixture.mjs` re-run: 33 passed, 0 failed); `[TRN] production bundle contains no part of the training driver` - **pre-existing and unrelated**: the production bundle contains the string `TRAINING` from `MEAS_TRAINING_SAVE` (MEAS wave), not from this lane. Not fixed here.

## Not verified

- No browser / screenshot check of the Cashier, Hall Templates tab or desk banner (logic and domain tests only). The server (`08 Production`) was not touched and no server tests were run. No second full `npm test` after the re-pin.
- The F14 test builds the three cash sources by direct state edits (counter bill, hall receipts, vendor payment), not through their UI commands; the live vendor / hall / counter commands were not driven end to end.
- The original F14 figures (Rs 9,625 vs Rs 30,255 on 26 Oct) were not reproduced as such; the gap is explained by the three missing sources.
- F8 could not be reproduced with stock roles (see above).

## Training steps affected (media NOT regenerated)

- Cashier "Expected cash" screen: the formula label row text changed and, on a day with counter / hall / vendor cash, the figure now matches the count the close uses (chapter 19 can type the page figure and submit; `closeday` may use either).
- Hall > Templates tab now lists General / Wedding / Corporate (chapter 15); the quotation dialog shows the seeded terms.
- Chapter 8: Reception can click Done on the wake-up banner (the script uses Meera, Manager, which still works).
- Chapter 14: OTA inbox rows CHM-F2..F5 now show arrivals 29-31 Oct; BN-88231 unchanged. Slides quoting those dates need a check.

## Defaults used

- Vendor payments grouped under the existing "transfers" part and counter / hall under "other", so no new field on `shiftExpected`.
- Reception, Manager and Owner (not Cashier) may complete a front-desk wake-up call.
- Inbox rows shifted by a flat 4 days; Classic party set to two adults.

## Open questions

- Cashier is shown the wake-up banner (`RMD_FRONT_DESK_ROLES` includes Cashier) but cannot mark Done; hide the Done button for Cashier, or allow it?
- The `[TRN]` production-bundle test needs a fix by whoever owns MEAS (rename the command string or loosen the check).

## Shared files touched

`src/domain/selectors.ts` (exports only), `src/domain/streams/closing.ts`, `src/domain/streams/services.ts`, `tests/stream-perf-store.mjs` and `tests/stream-masters-fixture.mjs` (FY26 hash re-pin on one line each). Not touched: `pos.ts`, `accounting.ts`, `lfmoney.ts`, hall payment code, HANDOFF, CLAUDE.md, migrations, `v22-fixes.css`.

## Fix round (audit f3171139, verdict FIX)

- **FIX 1.** The role-name rule in `gsvCanWork` is removed; it decides by grants only again (in step with the server's PERM-GSV-002).
- **FIX 2 (the real F8 defect).** The wake-up banner offered Done to the Cashier (training chapter 8 starts as Priya), who the command refuses. `DeskAlerts.tsx`, `DeskAlertsPage.tsx` (wake detail) and `alerts/Mobile.tsx` now show Done and Snooze only when `gsvCanWork(s,uid,request)` is true; otherwise the text "Ask Reception to make the call". Tests: Reception, Manager, Owner with stock grants can complete; Cashier is false and refused; Reception with `reservations` revoked is false and refused; a source check that the three views use `gsvCanWork`. (The views are not rendered in a test; no browser check.)
- **FIX 3.** New F14 tests through real commands, on BASE, plain and with Cash renamed to "Till cash" in the payment-method master: `CTR_BILL_ISSUE` (cash), `QUO_ACCEPT` + `HALL_ADVANCE_RECEIVE` (cash and UPI), `EXP_VENDOR_PAY` (cash and Bank transfer). Page figure = `drawer()` to the paise, the movement sums equal the cash amounts only, and `SHIFT_SUBMIT` with the page figure closes the shift. The earlier direct-state tests are kept.
- **Note 5 applied:** "Other cash movements" now shows the sign of the amount (no "+ -Rs x"), same in the footnote; the phone formula text names vendor payments.
- **Open (audit notes 4, 6, 7, left alone):** 4 the production sync mapper sends `templates:[]`, so the production Templates tab shows the seeded "OWNER TO REPLACE" terms; 6 each source is one netted movement per day; 7 CHM-F5 amount unchanged after the party cut, and any later lane changing FY26 must re-pin the two hash lines.
- **New finding for the pay-b lane (not fixed, hall payment code is not mine):** `HALL_ADVANCE_RECEIVE` demands a reference for a renamed Cash method ("Till cash"), i.e. it recognises cash by the literal name "Cash". The test passes a reference in that variant.
- `tests/stream-training.mjs` untouched. FY26 state not changed in this round, so no re-pin.
- Tests this round: tsc clean; `stream-cu-trn-fix` 18/18; with cashbook, channels, fit2-closing, hall, lf-alerts, masters-fixture, perf-store, qw-alerts, services: 122 passed, 0 failed. Full `npm test` not re-run.
