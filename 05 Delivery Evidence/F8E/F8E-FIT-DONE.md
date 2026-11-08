# F8E-FIT DONE (lane fit, Sonnet 5.5), 8 Nov 2026
Branch claude/f8e-fit from main 674929a3. Head hash: `git log -1` (this file is in the final commit). Nothing pushed.

## What changed (all under `03 Prototype`)
1. **Folio table, no sideways scroll** - `src/styles/folio.css` (F8E-FIT block at the end, container query on `.table-wrap.folio-table`): below 480 px of its own width each line stacks into a small card (description, then date / net / GST in three columns, action row). `src/features/folio/modals.tsx` BookingModal: `data-label` on the three data cells. Text is not shrunk. Cause seen: the folio pane beside the list at 1024 px is 348 px wide, the table needed 390 at text 1.3.
2. **Check-out sheet does not scroll** - `folio.css`: (a) each settle-sheet row keeps its action on the row's own line (3 columns), (b) the dialog's action bar loses its top margin (>= 760 px), (c) `modals.tsx` CheckoutModal: on a plain (non-split) stay the "Checkout is blocked until" note moves from the right column (which then scrolled, 530 in 432) to the left column, which was empty. Uses the existing two-column `co-grid`.
3. **Phone fit tool** - `tools/fit-screen-check.mjs`: SUBS now carry the sub-screen label; on the phone viewport a sub-screen is opened by clicking the `.m-chips` chip with that label (no chip = skipped); with `--pages` the phone run opens the listed pages directly (the phone shell has no drawer, before this the phone run listed no pages at all). `tools/fit-modals.json` unchanged.

## Seen in a browser (Playwright, real Chrome, port 4187, closed afterwards)
- Folio pane 1024 px, text 1.3: 39 of 40 folios scrolled sideways before (390/348), 0 of 40 after.
- Check-out, split stay (Deccan Pharma company folio, opened through the Split bill dialog): 1024x768 t1.3 sheet list 237 in 174 (scrolled) before; after 188/188, no scroller in the dialog. Also 1440x900 t1.3, 1024x768 t1, 820x1000 t1.3: no scroller. Non-split 1024x768 t1.3: scroller co-side 530/432 before my change 2c, none after.
- Phone tool run (owner, manager, page maintenance, 390x844): 18 checks PASS incl. `maintenance > pm-calendar`; nothing to fix.
- Screenshots: `05 Delivery Evidence/F8E/fit/` (`before-*` / `after-*`: folio pane, folio dialog, check-out 1024x768 t1.3, non-split) and `fit/tool-phone/` (report).

## Tests actually run
- `npx tsc --noEmit` exit 0; `npm run build` OK.
- `HCC_TEST_ONLY="stream-css-tokens|stream-fit|stream-themes|stream-split|stream-f8c" npm test`: 356/356 (first run 353/356 only because the CT-C guard reads committed lines, passes after commit; folio.css change is pure insertion so the f8cAdded.mjs exemption covers it, tokens only).
- fit-screen-check, phone viewport, maintenance only (above).

## Tests NOT run
Full npm test; fit-screen-check on desktop viewports (default five + text 1.3 passes) over all pages; dark / high-contrast themes for the new rules (colours only `var(--line)`, `var(--muted)`); server tests (no server change); non-owner roles on the folio pane.

## Defaults relied on
None (no owner questions touched).

## Stopped
Nothing.
