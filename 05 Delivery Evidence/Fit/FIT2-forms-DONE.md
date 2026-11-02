# FIT2-forms-DONE (wave FIT-2, lane forms, branch claude/fit2-forms)

Layout/read-only display only. No text sizes changed, no phone code, no money or permission logic touched.

## Per page

- **Prefs (Appearance & display)** - the only page that uses `FormSections`. Each section passes `summary` (new pure helper `prefsRailSummary` in `PrefsPage.tsx`, values from the same prefs state the section edits):
  - Text size - `Large (115 %)`
  - Appearance - `Dark, Compact` (+ `, Reduced motion` when motion is forced on)
  - Device & display - `Auto` / `Desktop` / `Mobile`
  - Start & navigation - `<start page label or Modules Home>, <n> rows`
  - Alerts - `6.5 s` / `Until dismissed`
  The whole configuration is now readable from the rail with no click.
- **Master configuration (settings)** - uses SplitView, not FormSections. The rail button keeps the existing count summary and gets a second one-line "headline values" row (`cfgHeads` in `HotelSettings.tsx`, read from `cfgDefs` + `cfgDisplay` + `cfgMoneyMask`, max 3 values, secrets and Owner-only values never shown, money masked as before). Keys per group: property (name, print format, document language), rooms (yield floor/ceiling), frontdesk (check-in, check-out, deposit), money (currency, invoice prefix, discount limit), pos (outlets), hkm (dirty alert, rooms per attendant, PM lead), users (active users, PIN lock), messages (sender, marketing), data (backup time, kept, restore drill). people and compliance fall back to the first three visible settings of the group; modules shows none (its count summary already says "n of m on"). The list keeps the full height and scrolls in its own panel. Style: `src/styles/fit2-forms.css` (single line, ellipsis, no font size; reuses `.cfg-item small`). The master row-list cap in fit-admin-a.css is untouched.
- **Backup restore procedure (backup)** - FitTabs + ScrollPanel, not FormSections: the two tabs now carry counts (history rows, restore steps) so the size of each is visible without a click. Nothing else changed.
- **Access edit-user / role forms** - do not use FormSections (FitTabs + SplitView panels); no change.

No new sub-screens, so `subscreens.ts` is unchanged.

## Tests
`tests/stream-fit-forms.mjs` (3 tests): prefs rail values and has-summary markup; settings headline values per group (no secret, max 3, modules none); backup tab render + stylesheet has no font-size.

## Check results
tsc clean; npm test 2349/2349 (2346 + 3 new); npm run build clean; fit-screen-check --pages prefs,settings,backup (tab-land + lap-1366, no scale pass, no modals): PASS, 0 failing of 190 (report fit-check-20261102-0649.md). Rooms group shows no headline line (its only visible settings are the tariff panel). Not done: full 5-viewport run, text-scale pass, modal samples.
