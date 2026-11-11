# CU lane admin-ui - DONE (11 Nov 2026)

Branch `claude/cu-admin-ui` from c45bbce3. UI + CSS only, prototype only; no server change, no migration, no SCHEMA / APK bump.

## What changed
- `03 Prototype/src/app/NavDrawer.tsx`: for users with `permission('admin')` (Owner, Tech admin) the old "Admin & Setup" category entry is dropped from the phone drawer; the top "Admin & Setup" shortcut stays and is highlighted on the Admin page and on any admin-category page. The shortcut opens `cat:admin` (CategoryPage), which lists every admin-category page the role may open (Administration, Approvals & audit, Master configuration, Backup, System health, Legacy import, Access, Appearance, Roadmap ...). Other roles: unchanged (no shortcut; the category appears only if they have an admin-category page, e.g. Appearance & display for every role).
- `03 Prototype/src/styles/v22-fixes.css`: appended block `CU-ADMIN-UI` (`@media(max-width:480px)`, desktop layout top bar): tighter padding; business date, user chip, text-size and display toggle hidden (all remain in the drawer / user menu: "Demo & display > Display mode", "Change my ...", Sign out, Appearance page); search shows icon only; breadcrumb shows the current page only (parents are in the menu) so it is no longer truncated; DEMO pill keeps its text and is fully visible; demo popover fits the width. Tokens only, no hex.
- `03 Prototype/tests/stream-cu-admin-ui.mjs`: 5 new tests.

## Tests (via Invoke-Serialized, `HCC_TEST_ONLY=stream-(cu-admin-ui|adm-ux|navmap|nav-anyof|admin-ui|mobile-admin|fit)`, + base tests)
- 318 / 318 passed, 0 failed (includes the 5 new [CU-ADMIN-UI], the 7 [ADM-UX], [NAV], navmap, mobile-admin and every stream-fit* guard incl. the FIT phone-file allowlist; no allowlist line needed).
- `npx tsc --noEmit`: clean.
- Full `npm test` NOT run (filtered run only; touched no shared prototype logic).

## Verified in a browser (Playwright, `npm start` on port 4391, stopped)
Owner, display mode Desktop, 360x740: no bar overflow, DEMO pill fully inside (right edge 352 of 360), crumb not clipped on Modules Home, Admin & Setup, Master configuration. Drawer shows a single Admin & Setup entry; it opens the admin page list.

## NOT verified
Other roles / other deep pages' top-bar at 360 dp (only the three above); the `tools/fit-screen-check.mjs` run (not needed by the guard); real device; dark / high-contrast themes; text scale 1.3 on the 360 top bar.

## Defaults used
- Hiding user chip / text size / display toggle on <= 480 px desktop mode (reachable via the user menu / Appearance page). Breadcrumb collapses to the current crumb only (superseded in Fix round).
- Threshold 480 px (covers 360 dp phones; tablets unaffected).

## Open questions
None. Shared files touched: `v22-fixes.css` (appended marked block). No other shared file.

## Fix round (audit a39b55af)
1. Business date + Open/Closed chip is now visible at <= 480 px desktop mode, full text size (only the words "Business date" and the chip's calendar icon are dropped; clock chip stays hidden). The DEMO pill drops its word (icon + chevron). Crumb ellipsizes first. Browser check at 360 px: Owner and Cashier (Preview-as), pages Modules Home, Guests & folios, Master configuration: no bar overflow, chip "24 Oct 2026 · Open" visible, pill right edge 352/360. A very long page name ("Master configuration") still ellipsizes in the crumb by design.
2. DONE claims corrected above (business date was not reachable elsewhere).
3. Stray ">" before the single crumb removed; dead rule removed. On a tabbed page the bar still shows the tab name only (the page name is in the heading) because page + tab does not fit beside the date chip without cutting both.
4. Test: per-role reachability now also covers a Restaurant user and a custom role ("Duty Manager", Manager's grants); the admin-page check matches by `data-page` (a `pageId` prop added to `Tile` in `src/app/ModulesHome.tsx`, renders `data-page` on the tile button; no visual change); new CSS assertions: date chip not hidden, forced visible, no font-size shrinking except the DEMO pill.
Tests after fix round: 318 / 318 passed (same filtered set), tsc clean.
Extra file touched: `03 Prototype/src/app/ModulesHome.tsx` (data attribute only).
