# FIT-fit-cash-b DONE

Lane **fit-cash-b** (phase B) · model Sonnet 5.5 · branch `claude/fit-cash-b` · worktree `E:\Projects\HCC\worktrees\fit-cash-b` · base `3dac5afb` (head of `claude/fit`). Pages `pos`, `expenses`. Nothing pushed.

## Commits

```
2f7981d4 pos and expenses converted to FitPage (tabs, board columns, sticky KOT bar, register + approvals panels), sub-screens, stylesheet, tests
caaf4621 layout fill (the split / board root classes collide with SplitView / BoardColumns), stylesheet renamed fit-cash-b.css
c02d6744 expenses actions in the header, register top row, KOT columns paged, 86 list as popover
6c2b0962 POS summary header button + note, 86 popover min height
ff8d57ec summary note in the panel, compact register foot
79e8d66e register lists popover in the filter row, header keeps Add expense + Ledger CSV
(this file + check evidence: the final commit)
```

## Files

Prototype (ownership of the brief only):
- `src/features/pos/PosPage.tsx` (converted; `PosAvailability` gets `data-fit-scroll` on the desktop layout only), `src/features/pos/parts.tsx` and `src/features/pos/modals.tsx`: **unchanged** (the sticky bar is CSS only)
- `src/features/expenses/Expenses.tsx` (converted), `src/features/expenses/Scc.tsx` (`ExpBanners` gets an optional `fit` flag, the phone keeps the old markup)
- `src/styles/fit-cash-b.css` (new, desktop rules only, every selector under `.app-shell`, no font-size, no colour literal)
- `src/app/subscreens.ts`: appended my own `// FIT-CASH-B` block (10 lines) at the end of the registry (the brief's seven blocks have no `FIT-CASH-B`; the twelve-lane rule says add one)
- Tests: `tests/stream-fit-cash-b.mjs` + `tests/fit-cash-b-entry.tsx` (8 tests, `[FIT-CASH-B]`)
- Evidence: `05 Delivery Evidence/Fit/fit-cash-b-check/` (check report md + json of the final run, `npm-test.txt`)
- `src/features/money/**`: not touched (no page of this lane renders from it)

Phone paths: nothing under `src/mobile`, `src/app/mobile`, any `*Mobile.tsx`, `styles/m-*.css` was edited (`PosMobile.tsx` still imports `PosAvailability` / `PosMasterLinks` from `PosPage.tsx`, both unchanged in behaviour).

## What was built

### `pos` (Restaurant & room service)

- Pattern: `FitPage` (fill) + page tabs + three scrolling columns. Sub-screens (registry `pos`): `orders`, `kitchen`, `summary` (the old local `useState` view, same ids). The Day summary tab is still absent for the Restaurant role (DEC-079 D1); `go('pos',{tab:'summary'})` for that role falls back to Orders.
- **Orders**: one card, grid `220 | 1fr | 360` (≥ 900 px) = tables / menu / check. Each column is `data-fit-scroll`, focusable, labelled. Portrait (< 900 px): tables + menu side by side on top, the check full width below, each at least 160 px high. The check column is a flex column; **`.pos-actions` (Send KOT / Settle / Print / Void bill) is `position:sticky; bottom:0`** with a background, so the KOT and Settle bar stays visible while the lines scroll. `Open check` (no check yet) is sticky too.
- **Kitchen**: station chips in a row, `BoardColumns` (Preparing / Ready / Served, each column scrolls), the 86 "Sold out today" list is the old `<details>` and opens as a popover over the bottom of the board (min 160 px, scrolls, keyboard reachable). Each column pages its cards (`usePaged` + Show more: the Served column holds every KOT, ~17,000 px before).
- **Day summary**: Print summary is a header action; the "Settled bills…" line is a `FitNote` in the toolbar; the four stats are a `StatStrip`; the tables + top items sit in one `ScrollPanel`; the "One shared till…" note is the last item inside the panel.
- Header: the "KOT printer & card terminal simulated" chip is the header action; the four "Manage list…" links (Owner / Tech admin) are in the toolbar; the "business day is closed" note is a `FitNote` there.
- Default tab: Orders (unchanged).

### `expenses` (Expenses & approvals)

- Pattern: `FitPage` (fill) + `StatStrip` (the five old small stats) + **page tabs = the SCC sections**, ids = the existing `EXP_SECTIONS` ids: `register`, `recurring`, `petty`, `budgets`, `udhaar`, `vendors`, `month` (registry `expenses`). `vendors` stays hidden for users without the vendor books (falls back to Register; before, the hidden section id fell through to Month & tax).
- **The expense status tabs keep their ids and order exactly** (`All, Draft, Pending, Posted, Rejected, Reversed, Void`, the existing `role=tab` buttons, not a `FitTabs`): the brief's `all / pending / approved / paid / rejected` ids do not exist in the code; the real status values are above. They sit in the register card's top row next to its title.
- **Register** = `fit-cols`: left, the register card (title + status tabs, then the filter row Mode / Month / Search + a "Lists & photos" popover holding the Categories… / Vendors… / Funding sources… links and the "Bill photos up to 4 MB" hint, then the table body that scrolls with sticky heads, foot with Show more + the 200-entry cap note as `FitNote`); right, "Approval requests" in a `ScrollPanel`. Every SCC row feature stays in the row (bill photo, Text, Edit…, Void…, Return…, Approve & post, Reject…, Reverse…, GST / ITC / TDS / RCM lines); every approval-request action stays in the right panel. Header: **+ Add expense** (primary) and **Ledger CSV** (deciders only; register tab only).
- The other six sections render their existing cards unchanged inside one `ScrollPanel` each (the card chrome is flattened by CSS); `ExpBanners` (recurring nudge, month lock / unlock / stale) is one `FitNote` line per banner on the desktop layout (text unchanged, "Post now" button inside).
- Default tab: Register (the old default section) / status All (old default).

### Defaults / structure changes (old → new)

| Page | Old | New |
|---|---|---|
| `pos` | local state `orders` | `useFitTab('pos',…,'orders')`, same default; remembered per browser session |
| `expenses` | local state section `register`, status `All` | `useFitTab('expenses',…,'register')`, same default; the status filter stays local state `All` |
| `expenses` | Approval requests card below the register | right column of the register tab |
| `expenses` | Pending / Drafts etc. stats as a 5-up grid | `StatStrip` in the toolbar (same values) |

## Tests

All through the lock (label `fit-cash-b`), output in `.scratch\` (not committed; a copy of the `npm test` output is in the evidence folder).

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | **2245 / 2249**: 8 new `[FIT-CASH-B]` pass, every older test passes. The 4 failures are not caused by this lane and also fail without it: `[FIT] no phone-layout file changed since the wave base` (the base `541bbff7` is older than the ACC-3 merge: `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css` differ from it; none are mine), and `[FIT] page <id> renders a fit-page root` for `arrivals`, `expenses`, `pos`: the harness in `tests/stream-fit.mjs` renders pages without `FitNavContext`, so `FitPage` has no `data-fit-page` (the check wants it). Needs the harness to wrap pages in `FitNavContext` (`{desktop:true,page:id,…}`), see Integration needed |
| `npm run build` | clean |
| `node tools/fit-screen-check.mjs --pages pos,expenses` (server `PORT=4194`, all five viewports, all nine roles, text 1 and 1.3, sub-screens + modal sample) | `05 Delivery Evidence/Fit/fit-cash-b-check/fit-check-20261101-1925.md`: 521 checks, 27 failing, **all 27 = `font-changed` artefact** (see below); 0 page-scroll, 0 content-scroll, 0 cramped, 0 hidden-action, 0 touch-size, 0 not-converted; text 1.3: all PASS; modal sample (+ Add expense) PASS at all viewports |

`font-changed` artefact (27 = 9 findings × lap-1366 / lap-1280 / desk-1920 for kitchen `label 12px (was 11px)` and petty `select 12px (was 11px)`): the check measures the first visible `label` / `select`; the baseline's 11 px came from the demo toolbar, which now sits in the Demo pill (same artefact fit-shell listed for `label`). On `pos › kitchen` the first label is a "Sold out today" checkbox label and on `expenses › petty` the first select is the Category select — both are existing 12 px rules in `m-pos.css` / `styles.css` that I did not touch (this lane sets no `font-size` anywhere; a test asserts it). No font changed. Fix is in the check tool (measure inside `.content` and re-record `label` and `select`).

Tests updated: none (no existing test asserted the old pos / expenses structure; `stream-pos*`, `stream-expenses-*`, `sync-*` all pass untouched).

## Open items

- `FitPage layout="split"` and `layout="board"` put the classes `fit-split` / `fit-board` on the **page root**, which collide with the `SplitView` / `BoardColumns` classes of the same name in `z-fit.css` (`.fit-split{display:grid…}`, `.fit-board{grid-auto-flow:column…}`): a page with `layout="split"` renders its header, toolbar, tabs and body as grid columns. The check does not see it (everything still fits). I use `layout="fill"` for both pages; the guide's examples 2 and 4 should be fixed by fit-shell / the integrator (rename the root classes or scope the component rules to `.fit-page-body >`).
- `HeaderActions` (fit.tsx): the overflow test uses `scrollWidth > clientWidth` on a `justify-content:flex-end; overflow:hidden` row, so content that overflows to the left (start) side is never detected and the primary action is clipped (seen on `expenses` at tab-port with six header items). I kept the header to two items to stay clear; the shell should fix the detection (e.g. measure the children's total width).
- The POS toolbar row holds only the four "Manage list…" links (Owner / Tech admin) and costs 44 px on every POS tab; fine at all viewports (columns ≥ 380 px at tab-land), but could move into the user's Settings menu if the owner wants more room.
- The expense register at tab-land (touch sizes) has 168 px of table (about 3 rows), the contract minimum is 160; the other tabs and larger viewports have far more.
- The Android emulator / physical tablet were not used (no emulator in any lane).

## Integration needed

1. **`tools/fit-modals.json`** (integrator): append
   - `{"page":"pos","selector":".pos-spot","label":"POS table (open check)","lane":"fit-cash-b","roles":["owner","manager","reception","cashier"]}` (opens the `pos-open` dialog on an empty table; page tab `orders`, the default)
   - `{"page":"expenses","selector":".exp-register td .text-btn","label":"expense row Text","lane":"fit-cash-b","roles":["owner","manager","accountant"]}` (opens `exp-text`)
   - `{"page":"expenses","opener":"+ Add expense","lane":"fit-cash-b","roles":["owner","manager","reception","cashier"]}` (already in the shell sample)
2. **`tests/stream-fit.mjs`** (fit-check): wrap each harness page in `FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:1,setTabCrumb(){}}}` so `data-fit-page` is present (otherwise the test fails for every converted page, `arrivals` included); and bump `FIT_BASE` so the phone-file test ignores the ACC-3 merge.
3. **`tools/fit-screen-check.mjs`** (fit-check): measure `label` and `select` inside `.content` and re-record those two baseline kinds (artefact above).
4. **`z-fit.css` / `fit.tsx`** (fit-shell): the root-class collision and the `HeaderActions` overflow detection (open items).
5. **`styles/m-pos.css`, `styles/m-expenses-scc.css`** (phone-path files, untouched): they hold desktop rules too (`.pos-layout`, `.exp-filters` …) which my sheet overrides with a higher specificity; the integrator may leave it that way.
6. `fit-cash-b.css` sorts before `z-touch.css`, so FIT-SHELL's bundle-order test (`z-touch, z-fit, zz-print`) stays valid; every rule has the `.app-shell` guard.

## HANDOFF bullet

FIT lane fit-cash-b DONE (claude/fit-cash-b, Sonnet): `pos` (tabs orders / kitchen / summary; tables | menu | check columns that scroll on their own with the Send KOT / Settle bar sticky; kitchen `BoardColumns` paged; summary strip + panel) and `expenses` (page tabs = the seven SCC sections, status tab ids unchanged, register + Approval requests side by side, banners as one-line notes) converted to `FitPage`; 10 sub-screens in `// FIT-CASH-B`, `fit-cash-b.css`, 8 `[FIT-CASH-B]` tests. tsc clean, npm test 2245/2249 (4 failures outside the lane: stream-fit harness has no `FitNavContext`, phone-file base), build clean; check on pos + expenses: only the known first-label/select font artefact remains. Shell issues found: `FitPage layout="split"/"board"` root classes collide with SplitView/BoardColumns; `HeaderActions` misses left overflow.
