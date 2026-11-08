# F8C-FIT DONE (lane f8c-fit, Sonnet, 8 Nov 2026)

Branch claude/f8c-fit from main 8f99a17c. Head hash: see `git log -1` (commit "F8C-FIT ...").

## Method used
`03 Prototype/tools/fit-screen-check.mjs` (FIT contract section 8, DEC-213): five viewports (tab-land 1138x640, tab-port 711x1066, lap-1366, lap-1280, desk-1920), pass A text scale 1 (all checks: page/content scroll, sideways, hidden actions, not-converted, cramped panels, font baseline, h1, touch size), pass B text scale 1.3 on tab-land + lap-1366. `kds` is picked up through PAGE_CATEGORY, `maintenance > pm-calendar` through subscreens.ts, so both were already in the lists. Real Chromium (Playwright, channel chrome) on the production build at port 5185.

## Changes
- `03 Prototype/src/features/maintenance/PmCalendar.tsx` (OccButton + day cell, ~lines 24 and 53): occurrence text was clipped at the right of the day cell ("Generator weekly test run (15 min on" cut off, seen in screenshot). Now wraps (white-space normal, overflow-wrap anywhere, height auto; cell minWidth 0, overflow hidden). Text size unchanged.
- `03 Prototype/src/styles/kds.css`: `.kds-bump` lost its own `font-size:15px` (check "font-changed button.btn 15px" on lap-1366, lap-1280, desk-1920: a button font that main never had); ticket header can wrap and ticket text wraps anywhere (robustness at large text).
- `03 Prototype/tools/fit-screen-check.mjs`: new `--theme light|dark|high-contrast` option (default light, output name carries the theme). Two lines changed line endings by accident (no content change).
- `03 Prototype/tests/stream-fit-f8c.mjs` (new, 3 tests [F8C-FIT]): kds + pm-calendar in the fit lists, kds page rules, pm calendar wrap rules, theme option.
- Guard tests `stream-fit-ops.mjs:84` (pm-calendar id) and `stream-fit.mjs` (MaintenanceMobile allow-list) were already fixed by the integrator (F8-INT-DONE.md); verified green, no change needed.

## Fit result (actually run)
Before fix (light, roles owner + maint, 129 checks): 3 failing, all `font-changed button.btn 15px` on kds (lap-1366, lap-1280, desk-1920). Every other row PASS, including text scale 1.3. First pm-calendar look (screenshot tab-land) showed the clipped text, which the check cannot see; fixed by eye.
After fix:
| Page | tab-land | tab-port | lap-1366 | lap-1280 | desk-1920 | 1.3 text (tab-land, lap-1366) |
|---|---|---|---|---|---|---|
| kds (owner, reception, light) | PASS | PASS | PASS | PASS | PASS | PASS |
| maintenance > pm-calendar (owner, maint, light; run before the KDS-only fix, PmCalendar fix included) | PASS | PASS | PASS | PASS | PASS | PASS |
| kds dark (owner, tab-land + lap-1366) | PASS | - | PASS | - | - | not run |
Dark and high-contrast earlier run (before kds font fix): only the same kds button font finding; pm-calendar PASS in both. High-contrast was NOT re-run after the fix; maintenance dark/high-contrast pm-calendar not re-run after the wrap fix (the wrap change is theme independent). Roles not run: housekeeper/supervisor/accountant/techadmin (cannot open kds/maintenance or same page code); modals (`--no-modals`, neither page has a modal opener in fit-modals.json; the KDS bump has no dialog). Phone layout of PmCalendar (`phone` prop in MaintenanceMobile) NOT checked in a browser.
Real-browser look: viewed tab-land owner screenshots of kds and pm-calendar (kds: two columns, tickets fit, nothing scrolls the page; pm-calendar: grid scrolls inside its panel, text wraps). Other viewports judged by the check only.

## Tests run
- `HCC_TEST_ONLY=stream-(fit-f8c|fit-ops|fit|kds|pm-calendar|themes) npm test`: 305/305 passed. `stream-fit-f8c` alone: 59/59 (56 core + 3 new).
- tsc, full npm test, dotnet: NOT run (only CSS/inline-style edits and a test file).

## Defaults relied on
None (no money, no owner question touched).

## Screenshots
`05 Delivery Evidence/F8C/fit/` : `light/` (before the KDS font fix, owner + maint reports + screens), `dark/`, `high-contrast/`, `light-final/` and `dark-final/` (after fix) with the .md/.json reports and `screens-*` PNGs (tab-land and lap-1366, owner).
