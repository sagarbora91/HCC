# CU lane admin-ui - AUDIT (Opus, 11 Nov 2026)

Branch `claude/cu-admin-ui`, head 9be76438, base c45bbce3. Read: CU-CONTRACT §1, §2, §3.8; `Admin/ADM-UX-DONE.md`; `cu-admin-ui-DONE.md`; `git diff c45bbce3..HEAD`.

## Verdict: FIX

The drawer part is correct. The top-bar part hides the business date with no other place to see it in desktop display mode on a phone. That breaks the brief's "other roles keep exactly what they have" spirit for cashiers and front desk, and contradicts the DONE file's "all reachable elsewhere".

## What I checked

- **Drawer (NavDrawer.tsx:22-23, 31).** The category is dropped only when `permission('admin')` is true, the same condition that renders the shortcut. So no role can lose the category without gaining the shortcut. The shortcut opens `cat:admin` -> `CategoryPage` (ModulesHome.tsx:59). It uses exactly the drawer's filter (`navFilterModules(...)` + `pagesOfCategory` + `permission(n.p)`), so the list is role-filtered and complete by construction. That includes later additions mapped to `admin` in `PAGE_CATEGORY` (syshealth, legacy-import, prefs, access, settings, approvals, backup, roadmap, and the MEAS/F8 entries that use the same map). The special rules in `navFilterModules` (legacy-import not for Tech admin, etc.) apply to both. Custom roles follow the same rule: no loss.
- **Tests (`tests/stream-cu-admin-ui.mjs`).** Test 1 fails on the base (two entries). Test 3 compares, per seeded role, the reachable set (every category opened + the shortcut page) with the allowed set. That is the old drawer's reach, so it proves "no loss" for the seeded roles. See note 4 for gaps.
- **CSS scope.** The selectors are `.app-shell.fit-shell ...`, which is only the desktop shell (App.tsx:288). MobileShell has no `fit-shell`, so the normal phone shell is untouched. The block is `max-width:480px`, so tablets are not affected. Print pages are wider than 480 px, so print is not matched. No colours are used, so the dark and high-contrast themes are unaffected. No hex.
- **Reachability in desktop mode at 360 x 740 (browser, Playwright, Cashier "Priya", FY26).** The display toggle, text size, user switch ("Preview as") and Sign out are all in the user menu. It opens from the drawer via the closed "Demo & display" > "Display mode" (3 taps), and Sign out is also directly in the drawer foot. Switching back to Mobile works from there. **The business date / day Open-Closed is shown nowhere:** not in the top bar, the drawer head or foot, the footer, or the user menu.
- **Scope and line endings.** 4 files. `git diff --stat` equals `--ignore-cr-at-eol`. Only `v22-fixes.css` gained rules, as an appended marked block. No server change, no SCHEMA or APK bump.
- `npx tsc --noEmit`: clean. Filtered `npm test` (`stream-(cu-admin-ui|adm-ux|navmap|fit)`) via Invoke-Serialized: 294/294 passed, 0 failed (exit 0).

## Findings

1. **FIX - `03 Prototype/src/styles/v22-fixes.css` line 23 hides `.topbar-right>.today`.** In desktop display mode at <= 480 px, the simulated business date and the Open/Closed state disappear from every page. Nothing else on the screen shows them. Cashiers and front desk read the business date to know which day they are posting into, especially after a day close. In the browser, the compact chip "24 Oct 2026 · Open" (the FIT squeeze already drops the words "Business date" and the clock) fits next to the menu, a short crumb, the search icon and the DEMO pill: the bar stays 360/360 with no overflow.
   What right looks like: do not hide the date chip. Keep it, at its normal text size (DEC-213: never shrink text). Let the crumb take the shrink (ellipsis) first. If the space is still short, the DEMO pill may drop its word and keep the icon (demo build only; production has no pill). If a date chip truly cannot fit, show the business date and Open/Closed in the drawer head instead, and say so in the DONE file. Add a test assertion that `.today:not(.fit-clock)` is not hidden by the block.
2. **NOTE - `cu-admin-ui-DONE.md` "What changed" and "Defaults used"** say the date, the user chip, the text size and the display toggle are "all reachable elsewhere". This is not true for the business date (finding 1). Correct the claim when fixing.
3. **NOTE - v22-fixes.css lines 25-27 (breadcrumb).**
   - Only the last crumb is shown. On a tabbed page, that last crumb is the tab label, so the bar reads "> Folio requests" and the page name ("Guests & folios") is gone from the bar. The heading below still shows it, so this is acceptable.
   - The last `li` keeps its leading chevron, so the bar shows an orphan ">". Hide `.crumbs li:last-child>svg:first-child` when it is the only visible item.
   - Line 27 (`li:not(:first-child):not(:last-child){min-width:0}`) is dead code: those items are already `display:none`.
4. **NOTE - `tests/stream-cu-admin-ui.mjs` lines 34 and 66-72.**
   - The per-role "no loss" test only iterates the roles that have a seeded BASE user: Owner, Manager, Reception, Cashier, Supervisor, Housekeeper, Accountant, Maintenance, Tech admin.
   - Restaurant and custom roles are not exercised. The code path is role-agnostic, so this is not a defect, but a Restaurant user and a custom role (e.g. a Manager-based copy) would make the test match the brief.
   - The CategoryPage check matches labels as plain text, which a blurb could also satisfy. Matching on a per-tile marker would be stricter.
   - Test 5 only checks the CSS block textually. It does not assert what stays visible in the bar.
5. **NOTE - display mode is behind the closed "Demo & display" summary** (NavDrawer.tsx foot, unchanged by this lane). Now that the top-bar toggle is gone on phones, a user in desktop mode needs to know to open that summary to get back to Mobile. Consider labelling the summary "Display & demo" or moving the "Display mode" button outside the `<details>`. This is not a blocker: it is reachable in 3 taps and was verified.
