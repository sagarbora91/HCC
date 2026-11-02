# FIT-fit-admin-a DONE

Lane **fit-admin-a** · model Sonnet · branch `claude/fit-admin-a` · worktree `E:\Projects\HCC\worktrees\fit-admin-a` · base `3dac5afb` (claude/fit). Nothing pushed.

## Commits

```
1cb6edc4 FIT admin-a: Master configuration (split) and Approvals & audit (tabs) on FitPage, sub-screens, tests
14c4c2d4 FIT admin-a: audit filters in a side panel (cramped fix)
bdaf257a FIT admin-a: settings uses default FitPage layout (fit-split class collides with SplitView)
ce7e04da tablet-masters-check: go Back to the group list before picking another group (SplitView slide-over)
(+ this DONE file: the final commit)
```

## Files

Prototype:
- `src/features/admin/HotelSettings.tsx` (Master configuration page; `CfgGroupDetail`, `CfgRow`, `Tariffs`, `GstPanel` bodies unchanged except the production-only `ServerDevicePanel`, see below)
- `src/features/admin/ApprovalsAudit.tsx` (Approvals & audit)
- `src/app/subscreens.ts` (append-only, inside `// FIT-ADMIN`: 12 `settings` + 3 `approvals` lines)
- `src/styles/z-tablet-masters.css` (3 desktop rules guarded by `.app-shell`; `m-config.css` is pinned by the CT-B colour-token tests, so the lane rules live in this sibling file)
- Not touched (nothing needed): `src/features/masters/**`, `roommasters/**`, `PolicyLimitEditor`, `modals.tsx`, `parts.tsx`, `config/**`. The masters keep their own list / form (`MasterList` + `MasterForm`) and now sit inside the scrolling record pane of the page.

Tests: `tests/stream-fit-admin-a.mjs` + `tests/fit-admin-a-entry.tsx` (5 tests `[FIT-ADMIN-A]`).
Tools: `tools/tablet-masters-check.mjs` (updated, see "Existing tests / tools updated").
Evidence: `05 Delivery Evidence/Fit/fit-check-20261101-1358.md` (first run), `fit-check-20261101-1413.md` (final run, settings + approvals) and their `.json`; screenshots stay untracked.

## What was built

### `settings` — Master configuration
- **Pattern:** `FitPage` (default `fill` layout) with the search box in the toolbar and a **`SplitView`** (ratio `1fr 3fr`): the 12 groups with live summaries in a `ScrollPanel` (left), the selected group's settings and Lists (masters) in the scrolling record pane (right). Below 900 px the group opens over the list with Back to list (SplitView slide-over; Android Back closes it first). Narrow screens start on the group list, wide screens start with "Property and branding" open, as before.
- **Sub-screens:** the selected group is the page's sub-screen, remembered with `useFitTab('settings', …)`: `property, rooms, frontdesk, money, pos, hkm, people, compliance, modules, users, messages, data` (12 `SUB_SCREENS` lines, labels = group names, so the search palette offers "Master configuration › Money and tax"). There is no `FitTabs` bar (the groups are the list); consequently no tab crumb in the breadcrumb.
- `cfgNav.group` (set by "Manage list…" `MstLink` and "also in Master configuration" links) still opens that group (also on narrow screens, where the group opens straight away).
- Default changed: none (default group `property`, as before).
- Nested masters: `MasterList` / `MasterForm` untouched; the old `.mst-split` stacking rule at ≤ 1040 px and the 240 px input minimum still apply inside the pane.
- **Moved, not hidden:** the production-only `ServerDevicePanel` (server address, device registration) used to sit above the whole page; it now shows at the top of the **Data and system** group's pane (production build only; the prototype build never showed it). Move it back to a page-level slot if the owner prefers it on every group.
- **Why not `layout="split"`:** `FitPage layout="split"` adds the class `fit-split` to the page root, and `SplitView` uses the same class for its own grid, so the page header and toolbar became grid cells. Settings therefore uses the default layout (see Integration needed).

### `approvals` — Approvals & audit
- **Pattern:** `FitTabs` with three sub-screens, each a single panel that fits: **Approvals** (inbox table, status tabs Pending / Approved / … in the toolbar, footnote as `FitNote`), **Pending expenses** (own tab), **Audit log** (log in a `ScrollPanel`, filters in a side `ScrollPanel` via `fit-cols`, "Load more" + count in the panel footer). Contract §6 names two tabs (approvals, audit); the page had three stacked sections and two panels stacked would be < 160 px each at 640 px, so the expenses section is a third tab (`expenses`). Nothing dropped. The tab bar shows counts (pending requests, pending expenses) as warn badges.
- A role without the audit permission still sees the lock message (in a panel, no filters).
- Default tab: `approvals` (first section, as before). Default status tab Pending, unchanged.

## Existing tests / tools updated

- `tools/tablet-masters-check.mjs` (a by-hand Playwright check, asserted the old always-visible group list): `group()` helper clicks "Back to list" first when the SplitView slide-over is open (portrait, < 900 px), then picks the group. Assertions untouched.
- No test under `tests/` needed an update (the harness pages render without a nav context and keep their text; the owner-default tab shows the same sections as before).

## Tests

All through the lock (label `fit-admin-a`, output in `.scratch\`):

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | **2242 / 2246**: all `[FIT-ADMIN-A]` and every pre-existing test pass; 4 failures are the shared `[FIT]` harness tests, see below |
| `npm run build` | built, 0 warnings |
| `node tools/tablet-masters-check.mjs` | ALL OK (portrait 800 x 1280 and landscape 1280 x 800: inputs >= 254 px, no sideways scroll) |
| `node tools/fit-screen-check.mjs --pages settings,approvals` (all five viewports, nine roles, text 1 and 1.3, every sub-screen) | final run `fit-check-20261101-1413.md`: **0 layout failures** (no page-scroll, content-scroll, not-converted, cramped, hidden-action, touch-size, h1); only the `font-changed label` finding below |

The 4 failing tests, none caused by this lane:
1. `[FIT] no phone-layout file changed since the wave base`: lists `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css`, changed by the merges of `claude/fit` (ACC-3) since `FIT_BASE=541bbff7`; the integrator sets `FIT_BASE` to the new main.
2. `[FIT] page settings|arrivals|approvals renders a fit-page root`: the harness renders pages without a `FitNavContext`, and `FitPage` takes `data-fit-page` from it, so the attribute is missing (`arrivals`, the shell's reference page, fails the same way). With the context (`tests/stream-fit-admin-a.mjs` does it) both my pages render `data-fit-page`. Fix is in `tests/stream-fit.mjs` (wrap each page in `FitNavContext.Provider value={{desktop:true,page:id,…}}`) or `FitPage` (fall back to `useHcc().page`).

`font-changed label 14px (was 11px)` (78 findings, only on fine-pointer viewports lap-1366 / lap-1280 / desk-1920, `settings` for owner and techadmin): the check compares the page's **first `<label>`**; the baseline's 11 px is the Audit-style `.adm-filters label` from another page, while the first label on Master configuration is the search box (`label.cfg-search`, body 14 px, unchanged by this lane: computed 14 px before and after). It is the same baseline artifact fit-shell reported for `arrivals`. The tab viewports (coarse) do not show it. Needs a per-page or per-selector baseline in the check tool.

## Open items

- Master list inside the pane: a master with very many rows makes the whole record pane scroll (rows list and form scroll together). Acceptable; a nested scroller per list would need a second `data-fit-scroll` region.
- Visual review of `tab-port` slide-over done only through the check (document fits, targets 44/48 px); no emulator.

## Integration needed

1. **`src/components/fit.tsx` / `z-fit.css` (fit-shell, class clash):** `FitPage layout="split"` renders `<section class="fit-page fit-split">` and `.fit-split{display:grid;grid-template-columns:var(--fit-split-cols,…)}` (the SplitView root rule) then applies to the page root too, breaking the header and toolbar. The component guide's pattern 2 (`layout="split"` + `SplitView`) hits it. Rename the page layout class (e.g. `fit-layout-split`) or scope the SplitView rule to `.fit-page-body>.fit-split`. This lane avoids it by not passing `layout`.
2. **`tests/stream-fit.mjs` (fit-check):** give the page harness a `FitNavContext` with the page id (above), set `FIT_BASE` to the merged main.
3. **`tools/fit-modals.json` — modal openers for this lane:**
   - `{"page":"settings","opener":"Change…","lane":"fit-admin-a","roles":["owner","techadmin"]}` (first setting row of the default group opens the `cfg-setting` sheet)
   - `{"page":"approvals","opener":"Approve","lane":"fit-admin-a","roles":["owner","manager"]}` is **not** a modal (it commits at once); no second opener on this page.
   - Optional (needs the rooms group, i.e. `sessionStorage hcc-fit-tab:settings = rooms`): `Schedule tariff` (`admin-tariff` modal).
4. Nothing needed in `admin/modals.tsx`, `parts.tsx` or `masters/**`.

## HANDOFF bullet

FIT lane admin-a: `settings` (Master configuration) = `FitPage` + `SplitView` (12 groups left, group pane right; group = sub-screen via `useFitTab`, 12 `SUB_SCREENS`), `approvals` = `FitTabs` Approvals / Pending expenses / Audit log (expenses split into its own tab; audit filters in a side panel). Check: 0 layout failures on all five viewports and nine roles (only the shared `label` baseline artifact on fine pointers). Open for the integrator: the `fit-split` class clash in FitPage / SplitView, `stream-fit.mjs` harness nav context + `FIT_BASE`.
