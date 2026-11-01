# FIT-fit-check DONE

Lane fit-check / Sonnet / branch `claude/fit-check` / worktree `E:\Projects\HCC\worktrees\fit-check` / base 541bbff7.

## Commits
- 322a65f9 FIT baseline from 541bbff7 (only `03 Prototype/tools/fit-baseline/fonts-<viewport>.json`, five files)
- f496a7a9 FIT check: hardened `fit-screen-check.mjs`, `fit-modals.json`, `tests/stream-fit.mjs`
- (next) baseline report + this file

## Files
- `03 Prototype/tools/fit-baseline/fonts-{tab-land,tab-port,lap-1366,lap-1280,desk-1920}.json` (owner + housekeeper, every page the role can open, the drawer and the user-menu modal; sets of values seen per element kind)
- `03 Prototype/tools/fit-screen-check.mjs`, `03 Prototype/tools/fit-modals.json`, `03 Prototype/tests/stream-fit.mjs`
- `05 Delivery Evidence/Fit/fit-check-baseline-20261101-0005.{md,json}` (screenshots in `screens-20261101-0005/` are NOT committed)

## What was built
Tool, contract 8: options `--pages --roles --viewports --out --no-shots --text-scale --no-scale-pass --no-modals --record-baseline`. Default run = text 1 (all checks, 5 viewports) + text 1.3 on tab-land and lap-1366 (checks 1, 2, 5). Role detection = the drawer's `.drawer-sub[data-page]` list (every category expanded) + Dashboard + Modules Home + `cat:*` pages; a page in the drawer that lands on Modules Home is FAIL `not-reached`. Sub-screens: sets `sessionStorage hcc-fit-tab:<page>` then opens the page by `history.pushState({hcc:'page',page,idx})` + popstate (leaves via Modules Home first so the page remounts). Touch viewports use the Capacitor mock. FAIL kinds: page-scroll, page-sideways, content-scroll, not-converted, cramped (tab bars `.fit-tabs-bar` / `data-fit-scroll="x"` exempt), hidden-action, font-changed (14 kinds incl. `.drawer a`, `.modal h2` in a "(chrome)" row), h1 (must be 22 px on a `fit-page`), touch-size, modal-clipped, opener-missing, not-reached, page-error. Report: page x viewport table, per-role summary, FAIL kinds, all failures.
`fit-modals.json` entries: `{page, opener | selector, label?, roles?, tab?}`; starts with 4 shell samples (reservations "+ New reservation", expenses "+ Add expense", access "New user", user menu).
`tests/stream-fit.mjs`: 46 tests `[FIT]`: parsers/comparer, SUB_SCREENS vs useFitTab/FitTabs (skips until subscreens.ts exists), bundle order of z-fit.css (skips until it exists), phone paths unchanged since `FIT_BASE` (default 541bbff7; set env FIT_BASE after `main` is merged again), and one test per desktop page (45 pages, rendered with the harness): skipped with a message until the markup contains `fit-page`; then root must be the only top-level element, class `fit-page`, `data-fit-page=<id>`. `FIT_ENFORCE_ALL=true` makes all mandatory.

## Tests
`npm test` through the lock: 2141/2141 (46 new [FIT]; 43 SKIP messages for unconverted/missing items). Baseline run: 1669 checks, 1515 failing (expected): page-scroll 1135, not-converted 1100, hidden-action 104; font-changed 0, touch-size 0, page-error 0, not-reached 0 at the base. No tsc run needed (no src change).

## Open items / notes
- Font baseline holds the set of sizes seen per element kind across pages (first visible match per page), not per page.
- `h1` 22 px is only enforced on pages that already have a `fit-page` root.
- Modal rows also report the page-scroll of the page behind the modal.

## Integration needed
- fit-shell: its `fit.tsx` uses key `hcc-fit-tab:<page>` (`fitTabKey`) - matches the tool. Confirm `go(page,{tab})`/mount reads sessionStorage at mount (the tool relies on remount via Modules Home). Page root must be the only child of `.content` with `fit-page` + `data-fit-page`. Tab bars should carry `fit-tabs-bar` (cramped exemption). Shell's own sample modal openers: edit `fit-modals.json` if its names differ.
- Integrator: append lane modal openers to `tools/fit-modals.json`; set `FIT_ENFORCE_ALL=true`; set `FIT_BASE` if main is merged again.

## HANDOFF bullet
FIT check tool + font baseline (541bbff7) + `tests/stream-fit.mjs` on `claude/fit-check`; baseline report `05 Delivery Evidence/Fit/fit-check-baseline-20261101-0005.md` (1515/1669 failing, as expected).
