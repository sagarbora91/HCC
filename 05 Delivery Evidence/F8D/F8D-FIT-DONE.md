# F8D-FIT DONE (lane f8d-fit, Sonnet, 8 Nov 2026)

Branch claude/f8d-fit from claude/f8c 0af7a992. Head hash: see `git log -1` (commit "F8D-FIT ..."). Production-less dev build (`npm run build`), served on port 5186.

## Changes
- `03 Prototype/tools/fit-modals.json`: new opener "PM plan (calendar occurrence)" (maintenance, tab pm-calendar, an occurrence in Scheduled state, which opens the plan dialog and has no side effect). KDS has no dialog (no `open(`/modal in `src/features/pos/KdsPage.tsx`), so no KDS opener.
- `03 Prototype/tools/fit-screen-check.mjs`: opt-in `phone` viewport (390x844, narrow window, no Capacitor mock), only with `--viewports phone`; phone checks sideways overflow, errors, not-reached (the page scrolls naturally on the phone layout, DEC-213 is for the desktop layout). Limit: the phone shell ignores the sub-screen tab, so the tool only sees the default Maintenance tab on the phone; PM calendar on the phone was therefore checked with a separate Playwright script (below).
- `03 Prototype/tests/stream-fit-f8c.mjs`: +1 test (opener present, phone viewport opt-in).
- No app/CSS change was needed: no violation or defect was found.

## Results (actually run)
1. Themes, `--pages kds,maintenance --roles owner,reception,maint`, five viewports, pass B text scale 1.3 (tab-land, lap-1366), modals included:
   - high-contrast: PASS, 0 failing of 498 (`fit/high-contrast/`)
   - dark: PASS, 0 failing of 498 (`fit/dark/`)
   (light was green in F8C and the code is unchanged.)
2. Phone, PM calendar (owner, `MaintenanceMobile` > "PM calendar" chip), real Chrome: viewed screenshots light 390 px, dark at text 1.3 / 360 px, high-contrast 360 px. No sideways scroll (document width = viewport), no clipped text, no control under 44 px, occurrences wrap, AMC cards stack. Only the chip row scrolls sideways, by design. No defects. Screenshots `fit/phone/pm-calendar-*.png`. Script is not kept in the repo (scratchpad). `fit/phone/fit-check-*.md` (tool run, 2 rows PASS) is the default Maintenance tab only.
3. Modals: `maintenance > modal:PM plan (calendar occurrence)` PASS on all viewports in both themes (inside the 498). Light-theme modal run not repeated.

## Tests
- Filtered `npm test` (stream-fit-f8c, fit-ops, fit, kds, pm-calendar, themes): 306/306 passed (305 + the new test).
- tsc, full npm test, dotnet: NOT run (tools/tests only).

## Defaults relied on
None.
