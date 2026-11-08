# F8-QW-ADV DONE - Cashier step "Advances held"

Branch `claude/f8-qw-adv` (from tag f8-setup 037bd7bc). Lane Sonnet. Prototype only, no server work, no migrations, SCHEMA stays 16.

## Built
- `03 Prototype/src/domain/streams/advances.ts` (new, pure): `advHeld(s, uid?)` (line 38), `advTotals`, `advCsv`, `ADV_RED_DAYS = 90`. One row per booking holding money. Advance = max(0, paid - posted total), i.e. `-totals(b).balance`; deposit = `totals(b).deposit`; refunded = refund receipts; age = whole days from the oldest advance / deposit receipt to the business date; folio letters from `sfReceiptFolio` (deposits carry no folio, so A). With a `uid`, only a role with the `payments` permission (the Cashier page gate) gets rows.
- `03 Prototype/src/features/cashier/AdvancesHeld.tsx` (new): table with red rows, CSV through the existing `saveCsv`, per-row actions that only open existing dialogs: `refund` (REFUND_REQUEST, one button per refundable receipt), `payment` (the deposit-return dialog), `refund-execute` (REFUND_EXECUTE; hidden for the requester, who sees "another user executes it"), and a status line for refunds awaiting approval.
- `03 Prototype/src/features/closing/Cashier.tsx`: F8-QW-ADV blocks (import, rail item, panel); step 'advances-held' appended after 'credit-guests' in `CASHIER_STEPS`.
- `src/app/subscreens.ts` F8-QW-ADV block (sub-screen 'advances-held'); `src/services/mock/index.ts` F8-QW-ADV block (export of advances.ts). `registry.ts` untouched (no modal registered).
- `tests/stream-advances-view.mjs`: 8 tests named `[F8-QW-ADV]` (figures on a fixture, deposit-only row, red boundaries at 89/90/91 days and the three red statuses, REFUND_REQUEST -> approval -> execute with the requester refused (FORBIDDEN, DEC-031), deposit-return and folio A / B letters, role gate across all users, CSV, wiring).

## Existing file outside the ownership row (one line)
`tests/stream-fit2-closing.mjs` line 57 pinned the cashier rail labels (five steps); extended with 'Advances held'. Unavoidable consequence of the new step.

## Commands ACTUALLY run (all through Invoke-Serialized, label f8-qw-adv)
- `npx tsc --noEmit`: exit 0, no output.
- `npm test` (full): 3173/3174 passed; the single failure was the FIT2-CLOSING rail test above (fixed afterwards).
- `HCC_TEST_ONLY='fit2-closing|advances-view' npm test`: 69/69 passed (includes all 8 [F8-QW-ADV] tests and the fixed FIT2-CLOSING test).
- NOT re-run: the full `npm test` after the last two small edits (rail test label, CSV button class). Only the focused run above covers the rail test.
- `npm run build` + `node serve.mjs` (PORT=4281) + `node tools/fit-screen-check.mjs --pages cashier`: first run FAIL 20 of 899, all `font-changed button.btn 10px` on the new step (the CSV button used `small-btn`); changed to plain `btn`; second run PASS 0 of 899.

## Fit check
Second run after the CSV button fix: PASS, 0 failing of 899 (9 roles, 5 viewports, text scale 1 and 1.3), report `05 Delivery Evidence/Fit/fit-check-20261108-0617.md` (the failed first run report was deleted).

## Defaults relied on
- QWQ-5 (red = Cancelled / No show / Departed with money held, or an advance older than 90 days; strictly older, day 90 is not red, day 91 is). An old deposit alone is red only through the status rule.
- Guest names are shown as the Cashier page already shows them (payments permission); no extra masking. Accountant has no `payments` permission today, so does not see the step.

## Not done / not verified
- Split stays: folio letters per receipt are shown; the SF routing for payments is not built in this branch (sf-domain lane), so the B/C/D case is tested by setting `receipt.folio` directly. One row per booking, not one per folio.
- Departed stays: a refund is limited by the existing rule (credit balance); the view lists the money but REFUND_REQUEST may refuse with its own message.
- No phone-specific cards: the Cashier has no phone page; the table sits in a scrolling panel like the other cashier tables.
- Production mode: the view reads `s.bookings` receipts and approvals already loaded for the Folios page; not verified against the production sync, no server work done.
