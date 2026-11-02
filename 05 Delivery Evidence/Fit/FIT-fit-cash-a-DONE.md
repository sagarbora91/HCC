# FIT-fit-cash-a DONE

Lane **fit-cash-a** (phase B) · model Opus · branch `claude/fit-cash-a` · worktree `E:\Projects\HCC\worktrees\fit-cash-a` · base `3dac5afb` (claude/fit head). Nothing pushed.

## Commits

```
d21ed01a FIT cash-a: test syntax fix
a742b7ee FIT cash-a: folios layout fill + tabs row with totals, folio CSS in folios.css (folio.css frozen by CT-C), test harness fixes
a49c608b FIT cash-a: folios split + sub-screen tabs, cashier split, cash book fill (layout only)
(+ the commit of this file and the check evidence)
```

## Files

Prototype (ownership of the twelve-lane table):
- `03 Prototype/src/features/folio/Folios.tsx` — page converted.
- `03 Prototype/src/features/closing/Cashier.tsx` — page converted.
- `03 Prototype/src/features/closing/CashOut.tsx` — `useCashOutActs` (the three cash-out buttons, extracted unchanged), `CashOutActions` (page header), `CashOutPanel fit` variant (ScrollPanel without the buttons). Phone and old desktop markup unchanged (tested).
- `03 Prototype/src/features/cashbook/CashBook.tsx` — range view + day view (`DayCard` desktop branch) converted; `CashOutDay inPanel`. Phone branches unchanged (tested).
- `03 Prototype/src/app/subscreens.ts` — `// FIT-CASH-A` block appended at the end (2 lines; the twelve-lane block did not exist).
- Stylesheets, all **new** files (desktop only: every selector under `.app-shell`, no font size, tokens only): `src/styles/folios.css`, `src/styles/cashier.css`, `src/styles/cashbook.css`. Why new: `folio.css` is frozen by the CT-C token round-trip tests (`stream-css-tokens-c.mjs` compares it with its original; an appended block failed three tests on the first run), `m-cashbook.css` is a phone-path file for the `[FIT]` phone test, and `closing.css` belongs to fit-people-a. The bundle takes every `src/styles/*.css` by name (`tools/cssTextScale.mjs styleOrder`), so no build edit is needed.
- `node tools/themes.mjs --write`: no change to `themes.css`.

Not touched: `CashModal.tsx`, `ShiftCount.tsx`, `deposit.tsx`, `modals.tsx` (not needed), `catalogue.tsx`, any phone file.

Tests: `03 Prototype/tests/stream-fit-cash-a.mjs` (9 tests `[FIT-CASH-A]`), `03 Prototype/tests/fit-cash-a-entry.tsx`.
Evidence: `05 Delivery Evidence/Fit/fit-cash-a-check/` (check reports `.md` + `.json`, screenshots, `probe/` = interactive probe script, results, 18 PNGs).

## What was built (per page)

| Page | Pattern | Sub-screens | Default tab / filter (old → new) | Notes |
|---|---|---|---|---|
| `folios` | **tabs + split**: `FitTabs` Folios / Folio requests; Folios = `SplitView` (folio list `ScrollPanel` left, folio record right, ratio 3fr 2fr). FitPage `layout` stays `fill` (Integration needed 3). | `folios`, `folio-requests` | no tab → **Folios**; filter unchanged (`All`) | The record pane renders **the booking/folio dialog body itself** (`BookingModal`), so every action of the full-width folio is there (Cancel / no-show, Checkout, Move / extend, Request credit note, Issue invoice, booking action slots — Print confirmation, Guest profile, Stay details, Registration card, Guest request, Log complaint, Extra person, Meal plan —, Add charge, Discount, Record payment, Correct line, Request refund, the folio's approval rows, invoice / folio preview); its `.modal-actions` bar sticks to the bottom of the pane. The row's guest button opens the record in the pane (row highlighted); the arrow button still opens the same folio as a dialog. Below 900 px the record slides over the list with Back to list. Toolbar (Folios tab): search + six filters; the four `small-stats` totals sit as a one-row strip at the right of the tab row (both tabs). Footnote → `FitNote`, Show more → list panel footer. |
| `cashier` | **split** (`fit-cols cashier-cols`, 2fr / 3fr; stacked below 900 px) | — | — | Left: "Expected cash" (status banner + formula + footnote) and the count / difference approval / shift result panel (Submit count, Accept / Reject, Open my cashier shift, Continue to day closing stay in a sticky action bar at the panel bottom). Right: "Today's cash movements" table and "Banking, transfers and drawings" (`CashOutPanel fit`). Header: the cash-out buttons the role may use (Bank deposit = Cashier / Manager, Till transfer, Owner drawing — unchanged rules) + Day closing; overflow goes to More (§11.9); the disabled reason shows at the top of the banking panel. |
| `cashbook` | **fill** (+ side panel) | — | — | Range view: header = CSV / Excel; toolbar = From / To + the range sums strip; body = days table `ScrollPanel` (Show more in footer) and "By account · settles to" in its own scroll box beside it. Day view (Open day): header = CSV / Excel + Day closing; toolbar = All days, the date, status pill, day sums strip; body = Entries table and a "Day lock" panel whose **head holds Lock this day / Reopen…** (always visible; enabling unchanged), with the reconciliation line, lock note, why-text, history and the day's banking records in its body. |

### Contract deviations (and why)

1. **folios has two sub-screens** (contract §6 says "—"). The "Folio requests" card (refunds, company credit, corrections, credit notes, discounts waiting for a decision or execution) does not fit under the folio list at 1138 × 640 without making both panels cramped (< 160 px), and putting it only in the record pane's empty state would hide it whenever a folio is open. A sub-screen tab keeps it one tap away with a count (warn badge when something is pending) and searchable as "Guests & folios › Folio requests". §6 last paragraph (another §5 pattern, reason given).
2. **No `StatStrip` for the sums.** `StatStrip` renders `.stat-label` (11 px, 10 px at some widths) while the cash book's `cb-sums` labels are 12 px. To keep every font size (DEC-195) the existing `cb-sums` / `small-stats` markup is kept and laid out as one sideways-scrolling strip (lane CSS: padding and flex only). Same function as a StatStrip, fonts unchanged, existing test assertions on that markup still hold.
3. **cashier** right column holds two panels (movements + banking / transfers): the cash-out records card had to stay visible; the cash-out buttons moved to the page header so that panel's head is one row and its body ≥ 160 px.

## Tests

All through `E:\Projects\HCC\worktrees\fit\tools\Invoke-Serialized.ps1` (label `fit-cash-a`); scripts and full logs in `E:\Projects\HCC\worktrees\fit-cash-a\.scratch\` (`tsc.ps1`, `test.ps1`, `check.ps1`, `all.ps1`, `probe.ps1`).

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` at base `3dac5afb` | 2239 / 2241 — 2 pre-existing failures: `[FIT] no phone-layout file changed since the wave base` (ACC-3 files `features/accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `styles/m-accounting2.css` differ from 541bbff7 — not this lane) and `[FIT] page arrivals renders a fit-page root` (harness has no FitNavContext) |
| `npm test` at HEAD | **2245 / 2250**: all 9 `[FIT-CASH-A]` pass; failing = the 2 pre-existing + `[FIT] page folios / cashier / cashbook renders a fit-page root` with the **same harness cause as arrivals** ("root needs data-fit-page" — the root has `class="fit-page …"`; my own test renders the pages inside `FitNavContext` and sees `data-fit-page`). Fix = Integration needed 1. |
| `npm run build` | clean (inside `check.ps1`) |
| `node tools/fit-screen-check.mjs --pages folios,cashier,cashbook` (5 viewports, 9 roles, text 1 + 1.3, modal sample; `PORT=4193`) | `05 Delivery Evidence/Fit/fit-cash-a-check/fit-check-20261101-1729.md` (+ `.json`, `screens-20261101-1729/`): 290 checks, 42 failing, **all `font-changed label` at the fine-pointer viewports** (lap-1366 / lap-1280 / desk-1920: cash book date `label` 13 px, folios `label.search` 14 px vs baseline `11px`). Known baseline artifact (fit-shell DONE, Integration needed 1): on `main` the first visible `label` at fine pointer was the demo toolbar's 11 px `.demo-field`, so those baselines hold only 11 px; the touch baselines (demo toolbar hidden) already list 13 px and 14 px for these same labels. This lane sets no font size. **0 page-scroll, 0 content-scroll, 0 not-converted, 0 cramped, 0 hidden-action, 0 touch-size; text 1.3 all PASS.** First run `fit-check-20261101-1641.md` found folios `cramped 151 / 34` (Integration needed 3) — fixed in a742b7ee. |
| Probe (`fit-cash-a-check/probe/probe.mjs`, through the lock): folio opened in the record pane ("In house" filter, first row) and the cash-book day view, owner + cashier at tab-land, tab-port, lap-1366 | document never scrolls (640/640, 1066/1066, 657/657; width = viewport), `.content` never scrolls, panels 303–667 px (≥ 160), Folio preview and Lock this day inside the viewport, 0 page errors. `probe-20261102.json` + PNGs (incl. `*-folio-open-bottom.png`: record scrolled to the end, sticky bar still in view). |

Existing tests updated: **none needed** (cash-book sums keep their markup, so `stream-exports` still matches `Carried to next day</span><strong>₹5,000<`; `stream-a11y`'s `Open day` aria-label kept; `stream-money-master` still finds `<MnySettlesTo` and the settles-to card on desktop and phone).

New `tests/stream-fit-cash-a.mjs`: harness builds; one fit-page root + `data-fit-page` for all three pages (inside `FitNavContext`), no PageTitle inside; folios tab ids = SUB_SCREENS, default Folios, `go(page,{tab})` opens and remembers Folio requests, tab row holds the totals strip; every folio row keeps the dialog opener, record pane = `BookingModal`, footnote kept; cashier columns, header buttons per role (Owner: Till transfer + Owner drawing, no Bank deposit per `CX_BANK_ROLES`; Manager: Bank deposit), buttons not repeated in the panel, four scroll panels; phone `CashOutPanel` and old desktop panel markup unchanged; cash book toolbar / table / settles-to scroll box / exports in header, phone `CashBookView` unchanged; day view Lock / Reopen in the Day lock head, entries table, All days in the toolbar, phone `DayCard` unchanged; CSS desktop-only, no font, no hex.

## Modal openers for `tools/fit-modals.json`

```json
{"page":"folios","selector":"button.icon-btn[aria-label^='Open folio for']","label":"folio dialog","lane":"fit-cash-a","roles":["owner","manager","reception","cashier","accountant"]},
{"page":"cashier","opener":"Till transfer","lane":"fit-cash-a","roles":["owner","manager","reception","cashier"]},
{"page":"cashier","opener":"Owner drawing","lane":"fit-cash-a","roles":["owner"]}
```
Till transfer is enabled only from the user's own open shift (on the demo seed the owner's header button is enabled; for other roles it may be disabled — the tool should skip a disabled opener).

## Open items

- The check tool does not open interactive states (record pane, day view); covered by the probe above, not by the gate's check.
- `[FIT] page … renders a fit-page root` stays red for converted pages until Integration needed 1 is applied.
- At tab-land the record pane is 430 px wide: the folio's heading + booking actions fill its first screen and the folio table scrolls below (the sticky Folio preview bar stays in view). The arrow button gives the wider dialog when wanted.

## Integration needed

1. **`03 Prototype/tests/stream-fit.mjs` (fit-check)** — the page harness renders pages without `FitNavContext`, so `FitPage` emits no `data-fit-page` and every converted page fails `[FIT] page <id> renders a fit-page root` ("root needs data-fit-page"; already failing for `arrivals` on the claude/fit head; now also `folios`, `cashier`, `cashbook`). In the generated entry: `import {FitNavContext} from '../../src/components/fit';` and render `<HccContext.Provider value={ctx}><FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:0,setTabCrumb:()=>{}}}>{pages[id]()}</FitNavContext.Provider></HccContext.Provider>`.
2. **`tools/fit-modals.json`** — append the openers above.
3. **`src/components/fit.tsx` (fit-shell)** — `FitPage layout="split"` gives the root `class="fit-page fit-split"`, and `.fit-split` is also `SplitView`'s grid class in `z-fit.css`, so a split page turns its own root into a two-column grid (header, toolbar and tabs land in grid cells; seen on folios in the first check run, cramped 151 / 34). This lane uses `layout` `fill` with a `SplitView` in the body. Fix: a prefixed layout class (`'fit-page fit-layout-'+layout`, CSS and the stream-fit-shell assertion `class="fit-page fit-split"` updated) or rename SplitView's class; tell the other split lanes.
4. **`tools/fit-screen-check.mjs` / `tools/fit-baseline/*.json` (fit-check)** — re-record the `label` kind at the fine-pointer viewports measured inside `.content` (those baselines still hold only the demo toolbar's 11 px), or every page with a search box or form label fails `font-changed label` there.

## HANDOFF bullet

FIT phase B lane fit-cash-a DONE (claude/fit-cash-a, Opus): `folios` = sub-screen tabs Folios / Folio requests, Folios a split (list left, the full booking/folio record with every action right), `cashier` = split (expected cash + count with sticky actions left, movements + banking right, cash-out buttons in the header), `cashbook` = fill (range + sums strip, days table, settles-to beside; day view with Lock / Reopen in the panel head). Layout only, no font change, phone markup unchanged. Prototype 2245/2250 (5 = pre-existing phone diff + harness data-fit-page, fix listed); check: 0 page-scroll / cramped / hidden-action, only the known fine-pointer `label` baseline artifact; FitPage `layout="split"` class clash reported to fit-shell.
