# FIT layout contract (wave `claude/fit`, prepared 1 Nov 2026) — fit-to-screen desktop and tablet layout

The shared rules and component names every FIT lane builds against. **Copy, never reinterpret.** A lane that needs a change writes it in its DONE file; the coordinator amends this file and tells the other lanes. Owner rulings behind it: **DEC-194** (no page scroll on desktop / tablet), **DEC-195** (page heading 22 px; every other text size and every touch size unchanged), **DEC-196** (sub-screens are page tabs, no fourth menu level), approved in chat 31 Oct – 1 Nov 2026 (**APR-025**). Tablet mockups of the five patterns approved by the owner on 1 Nov 2026 ("mockups: approved"; canvas link in `FIT_Briefs.md`).

## 0. Why (the measurement this wave fixes)

Owner, display Auto, at the Galaxy Tab S7 landscape app area (1138 × 640 CSS px, touch sizes on), `main` c8f91d45: **29 of 34 pages scroll the whole document.** Document height in screens: Messages 164 · Expenses 19 · Direct booking site 14 · Folios 13 · Reservations 13 · Housekeeping 12 · Day closing 11 · Groups 6.7 · Documents 3.8 · Linen & stock 3.5 · Checklists & handover 3.2 · Calendar 2.7 · Accounting 2.5 · Approvals 2.6 · Assistant 2.5 · Cash book 2.4 · Cashier 2.4 · Staff 2.4 · Rooms 2.4 · Reports 2.2 · Access 2.0 · Customers 1.9 · Backup 1.9 · Roadmap 1.7 · Maintenance 1.7 · Restaurant 1.7 · In-house 1.6 · Settings 1.6 · Guest services 1.4. Even the sign-in screen scrolls. Three causes, none of them text size:

1. **Unbounded lists grow the page** (Reservations / Folios / Expenses ~100 rows, Messages ~1,350 rows; Housekeeping columns 7,500 px).
2. **Sections stack vertically** on a wide, short screen (Day closing 7 step cards, Groups' 3,760 px card, Rooms' six floor blocks, Cashier's two cards above a table).
3. **Chrome eats a quarter of the height**: top bar 69 px + page title block (30 px heading, sub-line, 28 px margin) ≈ 165 px of 640 before any work area; the prototype's demo toolbar and the page footer add more.

## 1. The rule (DEC-194)

On the **desktop layout** (`display.isMobile === false`: Windows browser, and the Android app on tablets — short side ≥ 600, `PHONE_SHORT_SIDE`), at every viewport in §8:

- **The document never scrolls vertically or horizontally.** `document.documentElement.scrollHeight <= innerHeight` and `scrollWidth <= innerWidth`.
- **The content area never scrolls.** `.content` has `overflow: hidden`; every page fills it exactly.
- **Only elements marked `data-fit-scroll` may scroll**: list / table panels, a board column, a detail pane, a modal body, a tab bar (sideways). Every such element is keyboard-reachable (`tabIndex=0`, `aria-label`) and keeps its column headings / action bar visible (sticky).
- **Primary actions are always visible**: the page's main button(s) sit in the page header; a form's Save / Cancel sit in a sticky action bar; nothing that commits work is ever below a scroll.
- **The phone layout is out of scope.** No edits to `src/mobile/**`, `src/app/mobile/**`, any `*Mobile.tsx` / `Mobile.tsx`, `styles/mobile*.css`, `styles/m-*.css` rules that only apply under the phone layout. Phones scroll by design.

## 2. What must not change (DEC-195) — the owner's explicit condition

- **No font size is reduced** anywhere except the page heading `h1` in the page header: **30 px → 22 px** (weight 600, letter-spacing −0.4 px). Body, table cells, table headings, labels, inputs, buttons, pills, stats (`money-stat` values included), tabs, menu, dialog text: computed `font-size` identical to `main` at text scale 1 and at every text-size preference (CP-2 `--hcc-text`, CP-4 Android text zoom).
- **Touch sizes stay** (DEC-060): coarse-pointer dense rows / filters / inputs ≥ 44 px, primary actions, hamburger, drawer items, breadcrumb chips, dialog buttons ≥ 48 px; tab buttons in a `FitTabs` bar ≥ 48 px on touch.
- **Density preference stays** (`data-density`): this wave changes the *comfortable* spacing only where §3–§5 say; *compact* keeps being tighter than comfortable.
- **Colours, tokens, themes, contrast** unchanged (`stream-css-tokens-*`, `stream-a11y` stay green).
- **No domain, money, permission or command logic changes.** Layout only. A lane that finds it must change a selector, a stream function, a command or a permission stops and writes it in its DONE file.
- **No new npm package**, one self-contained HTML (D1), CSP unchanged (`stream-csp`).
- **Print** (`zz-print.css`, `tests/check-print-layout.mjs`) unchanged: under `@media print` the shell returns to normal flow (`height: auto; overflow: visible`) and scroll panels print in full.

The check tool (§8) records the computed font sizes of 14 element kinds on `main` (baseline file committed by lane fit-check) and fails the wave if any differs except the page `h1`.

## 3. The shell (lane fit-shell only)

Desktop branch of `src/app/App.tsx` (the `return` after `if(display.isMobile)…`) plus a new stylesheet `src/styles/z-fit.css` (loaded after `z-touch.css`, before `zz-print.css`; `build.mjs` order check). **Do not edit the minified `styles.css`**; override in `z-fit.css`.

| Part | Rule |
|---|---|
| Height chain | `html, body, #root { height: 100% }`; `.app-shell { height: 100dvh; min-height: 0; overflow: hidden }` (fallback `100vh`); `main#main-content { display: flex; flex-direction: column; flex: 1; min-width: 0; min-height: 0 }`. |
| Top bar | **One row, 56 px** (`--fit-topbar: 56px`) at every width ≥ 768; `topbar-left` = menu + breadcrumb (breadcrumb truncates the middle crumbs with an ellipsis, never wraps); `topbar-right` = search, business date chip, user menu button, text size quick, display toggle (desktop only). The coarse-pointer hiding rules of `z-tablet.css` stay. `flex-wrap: nowrap`; overflowing chips hide in priority order (clock → date label text → search label text; icons stay). |
| Demo toolbar (prototype build only) | Collapses into one **`Demo` pill** in the top bar (`.fit-demo-pill`, amber, text "DEMO") that opens a popover holding the existing `DemoToolbar` controls unchanged (scenario, preview-as, next action fault, display mode, reset, walkthrough) and the "Fictional data / local mock service" notes. The demo badge and conn chip move into that popover. Production build: nothing. |
| Sync strip (production build) | `SyncStatus` stays, max 28 px tall, single line, only while not fully synced (as today); it is part of the flex column, not overlaid. |
| Content | `.content { flex: 1; min-height: 0; overflow: hidden; display: flex; flex-direction: column; padding: 12px 16px 12px }` (fine pointer ≥ 1280 px: `16px 24px 16px`); `max-width: 1580px` stays, centred. Its single child is the page root. **Fallback while a page is not converted:** a page root without the class `fit-page` gets `.content > :not(.fit-page) { flex: 1; min-height: 0; overflow: auto }` with `data-fit-scroll="legacy"` added by the shell — so nothing is ever cut off mid-wave; the check tool reports `legacy` scrolling as a FAIL for that page. |
| Page footer | Removed from the work pages. Its two lines move to the user menu (`user-menu` modal) under "About this build". Modules Home keeps a one-line footer inside its own fit page. |
| Modals (all desktop modals, `ModalHost`) | `.modal { max-height: calc(100dvh - 32px); display: flex; flex-direction: column }`; heading fixed; body `.modal-body` (wrap the existing children in the shell's `ModalFrame` if the modal has none) `flex: 1; min-height: 0; overflow: auto; data-fit-scroll`; `.modal-actions` **sticky at the bottom**, always visible, background `var(--c-ffffff)`, top border `var(--line)`. Wide forms (≥ 8 fields) may use two field columns at ≥ 900 px width (`.fit-form-2col`). |
| Sign-in screen | Fits: the user list becomes a 2-column grid at ≥ 900 px (3 at ≥ 1280), list itself `data-fit-scroll` if more users than fit. |
| Modules Home / category pages | Fit: tile grid `repeat(auto-fill, minmax(240px, 1fr))`, tile padding reduced (tile text sizes unchanged); 10 tiles fit 1138 × 640 in 4 + 4 + 2 or 5 + 5. |
| Print | `@media print { .app-shell, main, .content { height: auto; overflow: visible } [data-fit-scroll] { overflow: visible; max-height: none } .fit-tabs-bar { display: none } }`. |

## 4. The components (lane fit-shell writes them; every module lane uses them)

New file `src/components/fit.tsx` (+ styles in `z-fit.css`, class prefix `fit-`). Names and props are fixed here; a module lane never creates its own variant.

```tsx
/** Page root for every desktop page. Header row: h1 (22 px) + one-line sub (ellipsis, full text in title attr) + actions (right). */
export function FitPage(p:{
  title:string; sub?:string;
  actions?:React.ReactNode;          // primary buttons — always visible
  toolbar?:React.ReactNode;          // ONE row under the header: search, filters, date range, StatStrip; wraps to at most 2 rows below 900 px
  tabs?:React.ReactNode;             // a <FitTabs/> bar, rendered between toolbar and body
  layout?:'fill'|'split'|'board'|'steps'|'form'; // body layout, default 'fill'
  children:React.ReactNode;
}):JSX.Element                       // root: <section className="fit-page fit-<layout>" data-fit-page={pageId}>

/** Card that takes all remaining height; header (title, count, actions) fixed; body scrolls; table heads sticky; ShowMore sits in its footer. */
export function ScrollPanel(p:{title?:React.ReactNode; actions?:React.ReactNode; footer?:React.ReactNode; label:string; children:React.ReactNode; grow?:number}):JSX.Element
                                     // body: <div className="fit-panel-body" data-fit-scroll tabIndex={0} aria-label={label}>

/** List + detail side by side. Below 900 px width the detail opens over the list as a slide-over with a Back button (never stacked below). */
export function SplitView(p:{list:React.ReactNode; detail:React.ReactNode|null; empty:React.ReactNode; onCloseDetail:()=>void; ratio?:`${number}fr ${number}fr`; /* default '2fr 3fr' */ label:string}):JSX.Element

/** Sub-screens (DEC-196). Tab bar ≥ 48 px on touch; scrolls sideways if it overflows; role=tablist, arrow keys, Home/End.
 *  Selected tab: remembered per page in sessionStorage `hcc-fit-tab:<page>`; `go(page,{tab})` opens a given tab (existing ctx.go option).
 *  Switching tabs uses no history entry (Android Back leaves the page, as today). */
export function FitTabs<T extends string>(p:{page:string; tabs:{id:T;label:string;count?:number;badge?:'warn'|'danger'}[]; value:T; onChange:(t:T)=>void}):JSX.Element
export function useFitTab<T extends string>(page:string, ids:readonly T[], fallback:T):[T,(t:T)=>void]

/** One-row KPI strip for the toolbar. Same Stat font sizes; less padding; scrolls sideways if too many (never wraps). */
export function StatStrip(p:{items:{title:string;value:string;sub?:string;tone?:'ok'|'warn'|'danger'}[]}):JSX.Element

/** Columns that each scroll on their own (Housekeeping, Maintenance board, POS tickets). */
export function BoardColumns(p:{columns:{id:string;title:string;count?:number;body:React.ReactNode}[]; minColumn?:number /* px, default 240 */}):JSX.Element

/** Wizard / checklist: step rail on the left (status icon + label + count), one step body on the right (Day closing, handover runs). */
export function StepFlow(p:{steps:{id:string;label:string;status:'done'|'current'|'waiting'|'blocked';note?:string}[]; value:string; onChange:(id:string)=>void; children:React.ReactNode}):JSX.Element

/** Long settings forms: section list on the left, one section on the right, Save bar sticky at the bottom. */
export function FormSections(p:{sections:{id:string;label:string;dirty?:boolean}[]; value:string; onChange:(id:string)=>void; actions:React.ReactNode; children:React.ReactNode}):JSX.Element

/** Modal body wrapper used by ModalHost; lanes use it only for modals they own that render their own <dialog> markup. */
export function ModalFrame(p:{heading:React.ReactNode; actions:React.ReactNode; children:React.ReactNode}):JSX.Element
```

`components/ui.tsx` `PageTitle` stays (phone and old code use it) but renders the 22 px header when inside `.content` on the desktop layout; new code uses `FitPage`. `ShowMore` / `usePaged` stay unchanged; inside a `ScrollPanel` the ShowMore row goes in `footer`.

**Sub-screen registry.** New file `src/app/subscreens.ts` (fit-shell creates it; module lanes **append lines only inside their own `// FIT-<LANE>` block**):

```ts
export type SubScreen={page:string;id:string;label:string;keywords?:string[]};
export const SUB_SCREENS:SubScreen[]=[
  // FIT-FRONT
  // FIT-CASH
  // FIT-OPS
  // FIT-GUEST
  // FIT-REVENUE
  // FIT-PEOPLE
  // FIT-ADMIN
];
```

The search palette (`SearchPalette.tsx`) lists every sub-screen as "Page › Tab" and opens it with `go(page,{tab:id})`; the breadcrumb shows "Modules Home › Category › Page › Tab" (the tab crumb is text, not a button). `stream-fit.mjs` asserts every `FitTabs` id used by a page appears in `SUB_SCREENS` and vice versa.

## 5. Patterns and spacing (comfortable density, desktop layout)

| Thing | Before | After | Note |
|---|---|---|---|
| Page heading `h1` | 30 px, margin-bottom 28 px | **22 px**, header row 48 px, margin-bottom 10 px | DEC-195, the only font change |
| Page sub-line | full paragraph under heading | one line, ellipsis, `title` = full text | same font size |
| Card gap | 22–25 px | 12 px | |
| Card section-title padding | 22 px 23 px | 12 px 16 px | |
| Stats block | 4-col grid, 24 px gap + big tiles | `StatStrip` in the toolbar, 10 px gap, tile padding 8 px 12 px | value font unchanged |
| Table cell padding (fine pointer) | 16 px 18 px | 10 px 12 px | coarse pointer: row ≥ 44 px as DEC-060 |
| Table head | scrolls away | `position: sticky; top: 0` inside the panel | |
| Info notes (`.info-note`) | full-width block above the work area | collapsed to a one-line `ⓘ` note with "More" toggle, or moved into a tab's empty area | text unchanged |
| Footnotes | under every card | in the panel footer, one line + "More" | |

Which pattern for which kind of page:

| Kind | Pattern |
|---|---|
| One list / table | `FitPage layout="fill"` + toolbar (search, filters, StatStrip) + one `ScrollPanel` |
| List whose rows open a record | `layout="split"`: `SplitView` (list `ScrollPanel` left, record right, record pane itself `data-fit-scroll`). Rows that open a **modal** today keep the modal (the modal rule of §3 makes it fit); a lane may move it into the split only where §6 says |
| Several sections | `FitTabs` (sub-screens); each tab is itself one of the other patterns and must fit |
| Board / kanban / room grid | `layout="board"`: `BoardColumns`, or a grid in a `ScrollPanel` |
| Wizard / closing / checklist run | `layout="steps"`: `StepFlow` |
| Long settings form | `layout="form"`: `FormSections` |

**Default list scope.** Where a page already has a filter or tab that narrows to current / open items (e.g. a status tab, "Today", "Open"), the lane may make it the default and must keep "All" one tap away; it **never invents a new filter meaning**, and lists every default it changed in its DONE file. Lists that render all rows without `usePaged` (Messages, Booking-site bookings, Expenses requests, Housekeeping cards) adopt `usePaged` + `ShowMore` in the panel footer.

## 6. Page assignments (lane · pattern · sub-screens)

`SUB_SCREENS` ids are kebab-case of the label. Existing local tab state is **replaced by `useFitTab`** with the same tab ids, so `go(page,{tab})` reaches them.

| Page id | Lane | Pattern | Sub-screens / notes |
|---|---|---|---|
| `home`, `cat:*`, sign-in | shell | fit grid | §3 |
| `overview` (Dashboard) | front | tabs Arrivals / Departures / In house become `FitTabs` + `StatStrip`; list in `ScrollPanel` | arrivals, departures, in-house |
| `rooms` | front | board: floor `FitTabs` ("All floors" first) — "All floors" = compact grid of every room in one `ScrollPanel` (tile size unchanged, more columns: `repeat(auto-fill,minmax(150px,1fr))`) | all, one tab per floor (generated; registry gets `rooms` + `all` only) |
| `calendar` | front | fill: one `ScrollPanel` scrolling both ways, room column and date header sticky | — |
| `reservations` | front | fill: toolbar (search, status filter, date range) + `ScrollPanel` table; row opens the existing modal | — |
| `arrivals` | front | fill (already fits; convert to `FitPage`) | — |
| `inhouse` | front | split: in-house stays list left, stay record right (as built; a `SplitView`) | — |
| `guests` (front-desk list) | front | split: guest profiles list left, profile right (as built). Linked as a nav item "Guests" in the Front desk group with the Reservations permission (owner decision after FIT) | — |
| `folios` | cash | split: folio list left, folio right. Built as `FitPage layout="fill"` with a `SplitView` in the body (same result) and sub-screens Folios / Folio requests | folios, requests |
| `cashier` | cash | split: shift summary + count card left (`ScrollPanel`), shift movements table right | — |
| `cashbook` | cash | fill: toolbar = range + StatStrip (`cb-sums`), `ScrollPanel` table | — |
| `pos` | cash | existing view tabs → `FitTabs`; order view = board (menu left, ticket right with sticky Charge / KOT bar) | one per existing view id |
| `expenses` | cash | status tabs → `FitTabs`; each = split (requests list left, request + approval right) | the seven SCC sections as sub-screens: Register, Recurring, Petty cash, Budgets, Udhaar, Vendor payments, Month & tax. The status filter inside the Register keeps its ids All / Draft / Pending / Posted / Rejected / Reversed / Void ("approved / paid" never existed) |
| `housekeeping` | ops | board: `BoardColumns` (4 status columns, each scrolls), toolbar = floor filter + StatStrip | — |
| `maintenance` | ops | existing `MNT_TABS` → `FitTabs`; list tabs = split | existing ids |
| `handover` | ops | existing tabs → `FitTabs`; a checklist run = `StepFlow` (one section at a time) | existing ids |
| `inventory` | ops | existing `TABS` → `FitTabs`; each fill | existing ids |
| `guest-services`, `gsv-my-requests` | guest | existing `gsvTabs` → `FitTabs`; request lists = split | existing ids |
| `messages` | guest | existing tabs → `FitTabs`; each = split (message list, `usePaged`, left; message right) | existing ids |
| `documents` | guest | split (captures list left, capture right) | — |
| `customers` | guest | split: customer list left; Customers 360 profile right with inner `FitTabs` Overview / Stays / Legacy (from W1a) / Money / Notes | overview, stays, legacy, money, notes |
| `yield` | revenue | `FitTabs` per existing section (tariff grid in `ScrollPanel`, sticky first column) | per section |
| `groups` | revenue | existing `TABS` → `FitTabs`; each = split (group list left, group record right — today's 3,760 px card); built as `layout="fill"` with a `SplitView` in the body; DEC-076 credit note on Companies only, "Rooms held" on Blocks only | existing ids |
| `channels` | revenue | `FitTabs` per section (channels, mapping, payouts, logs) | per section |
| `booking-site` | revenue | `FitTabs` (Bookings `usePaged`, Preview; main has no Settings / Rooms & rates screens on this page) | bookings, preview |
| `staff` | people | existing `HrDesk` tabs → `FitTabs`; each fill / split | existing ids |
| `my-hr` | people | fill (fits today; convert) | — |
| `closing` | people | steps: `StepFlow` over the 7 Day-closing steps; step body may hold a `ScrollPanel`; the "Close day" action sits in the step bar, always visible, enabled exactly as today | one per step id (registry: `closing` + each step) |
| `reports` | people | split (built as `layout="fill"` with a `SplitView` in the body): report list (grouped) left, report right (`ScrollPanel`, export buttons in panel header) | — |
| `plans-extras` | people | `FitTabs` per section | per section |
| `accounting` | people | existing sections → `FitTabs` (Day book, Ledgers, Parties, GST, TDS, Reports, … as on main after ACC-2); statusbar + StatStrip in toolbar | per existing section |
| `assistant` | people | chat: conversation `ScrollPanel` (newest at bottom), input pinned at the bottom, side panel for suggestions | — |
| `compliance` | people | `FitTabs`: Calendar / Dashboard (main has no Filings / Licences screens) | calendar, dashboard |
| `syshealth`, `backup`, `roadmap` | admin | fill / tabs per section | per section |
| `prefs` | admin | form: `FormSections` | per section |
| `access` | admin | split: roles / users list left, permissions right | — |
| `settings` (Master configuration, masters) | admin | built as `FitPage` default layout with a `SplitView` (12 groups left, group settings and Lists right; `.fit-page.fit-split` would collide with SplitView's own grid class); existing master list / detail (`mst-list` / `mst-detail`) in the record pane, the master's row list scrolls inside itself; each master's form uses `FormSections` where it has > 1 section | per master group |
| `approvals` | admin | `FitTabs` (Approvals, Audit log, plus "Pending expenses") each split (`layout="fill"` with a `SplitView` in the body) | approvals, audit, pending expenses |
| W1a `legacy-import` (if on main) | admin | form + result `ScrollPanel` | — |

A page that turns out not to fit with its assigned pattern at §8's smallest viewport: the lane may pick another §5 pattern and says why in its DONE file; it may not drop content or shrink text.

## 7. Files, ownership, append-only blocks

| Lane | Owns | Append-only (own block) |
|---|---|---|
| fit-shell | `src/app/App.tsx` (desktop branch + footer move), `src/app/ModalHost.tsx`, `src/app/ModulesHome.tsx` (Home, category pages, sign-in, breadcrumb), `src/app/SearchPalette.tsx`, `src/app/demo/DemoToolbar.tsx`, `src/app/subscreens.ts` (create), `src/components/fit.tsx` (create), `src/components/ui.tsx` (`PageTitle` only), `src/styles/z-fit.css` (create), `build.mjs` (stylesheet order only), the user-menu modal file (About section), `tests/stream-fit-shell.mjs` | — |
| fit-check | `03 Prototype/tools/fit-screen-check.mjs`, `tools/fit-baseline/*.json`, `tests/stream-fit.mjs` (static rules), `05 Delivery Evidence/Fit/**` | — |
| fit-front / cash / ops / guest / revenue / people / admin | the feature folders of their pages in §6 (`src/features/<folder>/**` excluding phone files) and `src/styles/<feature>.css` of those folders (desktop rules only), their tests' page-structure assertions | `SUB_SCREENS` block in `subscreens.ts`; `z-fit.css` **not** — lane CSS goes in the feature's own stylesheet |

Feature folders per lane: **front** `rooms`, `reservations` (desktop files), `guests`, `overview`; **cash** `folio`, `closing/Cashier*` (cashier page only), `cashbook`, `pos`, `expenses`, `money`; **ops** `housekeeping` (Housekeeping page; `Backup` / `Roadmap` there belong to admin), `maintenance`, `handover`, `inventory`; **guest** `services`, `messaging`, `customers`; **revenue** `yield`, `groups`, `channels`; **people** `staff`, `closing` (DayClosing), `reports`, `folio/catalogue.tsx` (plans-extras page component only — coordinate with cash through the DONE file), `accounting`, `assistant`, `compliance`; **admin** `admin`, `masters`, `roommasters`, `prefs` (desktop), `housekeeping/Backup*`, `housekeeping/Roadmap*`.

A file two lanes need (e.g. `folio/catalogue.tsx`): the lane named first in this paragraph owns it; the other writes the exact edit under "Integration needed".

## 8. The check (lane fit-check writes it; the gate runs it)

`03 Prototype/tools/fit-screen-check.mjs` — Playwright by hand like `tools/tablet-masters-check.mjs` (not part of `npm test`; needs `npm run build` and `PORT=4183 node serve.mjs`; Playwright from `PLAYWRIGHT_DIR`, default `E:/ClaudeDev/cache/npm/_npx/9833c18b2d85bc59/node_modules/playwright`, Chrome channel). Runs through the heavy-job lock.

| Viewport id | Size (CSS px) | Device | Touch / native |
|---|---|---|---|
| `tab-land` | 1138 × 640 | Galaxy Tab S7 landscape, app area | `hasTouch`, `isMobile:false`, `window.Capacitor` mock (`isNativePlatform → true`, `screen` 1138 × 711) |
| `tab-port` | 711 × 1066 | Galaxy Tab S7 portrait, app area | same |
| `lap-1366` | 1366 × 657 | 1366 × 768 laptop, browser chrome | fine pointer |
| `lap-1280` | 1280 × 720 | | fine pointer |
| `desk-1920` | 1920 × 969 | 1920 × 1080 monitor, browser chrome | fine pointer |

For each viewport × role (`owner`, `manager`, `reception`, `cashier`, `supervisor`, `housekeeper`, `accountant`, `maint`, `techadmin`; session key `hcc-session`, display mode `auto`, prefs default) × every page the role can open (from `PAGE_CATEGORY` + `home` + its category pages) × every sub-screen in `SUB_SCREENS` for that page:

1. `scrollHeight <= innerHeight` and `scrollWidth <= innerWidth` on the document → else **FAIL page-scroll**.
2. `.content` `scrollHeight <= clientHeight + 1` → else **FAIL content-scroll**; any `[data-fit-scroll="legacy"]` → **FAIL not-converted**.
3. The page root has class `fit-page` → else FAIL not-converted.
4. Every `[data-fit-scroll]` panel is ≥ 160 px tall (≈ 3 table rows at touch size) → else **FAIL cramped**.
5. Every element in `.fit-page-actions` and `.modal-actions` is fully inside the viewport → else **FAIL hidden-action**.
6. Font sizes: computed `font-size` of the first `td`, `th`, `button.btn`, `input`, `select`, `label`, `.pill`, `.tabs button` / `.fit-tabs-bar button`, `.stat-label`, `.money-stat`, `p`, `.muted`, `.drawer a`, `.modal h2` equal the baseline (`tools/fit-baseline/fonts-<viewport>.json`, recorded from `main` c8f91d45 + ACC-2 + W1a by fit-check **before** any module lane merges) → else **FAIL font-changed**. The page `h1` must be 22 px → else FAIL.
7. Touch sizes at `tab-*`: every visible `button`, `input`, `select`, `[role=tab]` ≥ 44 px tall, `.btn.primary`, `.modal-actions .btn`, `[role=tab]` in a `FitTabs` bar ≥ 48 px → else **FAIL touch-size**.
8. A modal sample per lane (the lane lists 2–4 modal openers in its DONE file; fit-check adds them to `tools/fit-modals.json`): open, check 1 and 5, close.
9. Also at text scale 1.3 (CP-2 largest) on `tab-land` and `lap-1366`: checks 1, 2, 5 only (panels may scroll more; the page still may not).

Output: `05 Delivery Evidence/Fit/fit-check-<yyyyMMdd-HHmm>.md` (a table page × viewport with PASS / the FAIL kinds and the measured numbers, per-role summary) + `fit-check-<stamp>.json` + screenshots of every page at `tab-land` and `lap-1366` for the owner (`Fit/screens-<stamp>/`). Exit 0 only when everything passes.

`tests/stream-fit.mjs` (in `npm test`, static, no browser): every page component registered in `App.tsx` / `registry.ts` (desktop) renders a root with class `fit-page` (render each with the existing test harness, check the root class); every `useFitTab` page id + tab ids ⊂ `SUB_SCREENS` and vice versa; `z-fit.css` loads after `z-touch.css` and before `zz-print.css`; no file under the phone paths of §1 changed versus the wave base (git diff in the test, skipped with a message when git is unavailable).

## 9. Done means

- Check tool: **0 FAIL** on all five viewports × nine roles × every page and sub-screen; owner screenshots produced.
- `npx tsc --noEmit` clean, `npm test` all green (count ≥ main's + new FIT tests; tests asserting old page structure are updated to open the right tab, never deleted or weakened — each such update listed in the DONE file), `npm run build` clean, 0 warnings, `check-print-layout.mjs` and `check-tablet-back.mjs` pass.
- Server: no change (`08 Production` untouched; the gate still runs the server suite).
- Phone layout byte-identical in behaviour: no diff under §1's phone paths.
- APK v18 (W0) is built from the merged `main` after this wave, and its tablet report includes the fit check at `tab-land` / `tab-port` on the emulator.

## 11. Coordinator amendments (1 Nov 2026, after the fit-shell review)

Source: `05 Delivery Evidence/Fit/FIT-shell-review-20261101.md` §6. These override anything above that says otherwise.

**Decision numbers:** the FIT rules are **DEC-194** (no page scroll), **DEC-195** (no smaller text; h1 22 px) and **DEC-196** (sub-screens as tabs), approved as **APR-025**. DEC-178…180 are legacy-data rulings; never cite them for FIT.

1. **Modals.** Every rule for desktop dialogs targets the class **`.fit-modal`**, which `ModalHost` adds on the desktop layout. Never use `.modal:not(.m-sheet)` or a bare `.modal`: phone sheets and some phone paths (Day pack) share `.modal`. `ModalHost` already gives the scrolling `.modal-body`, so `ModalFrame` is **only for stand-alone dialogs** that render outside `ModalHost`, and then it renders the `.modal fit-modal` shell itself. The stand-alone dialogs `DayPack`, `ServerDayPack` and the guest-ID dialog (`guestid-ui`) are converted to `ModalFrame` by **fit-shell**; on the phone they keep their current look.
2. **Sub-screens are desktop only.** Search-palette entries for sub-screens, the `tab` in `go(page,{tab})` and the history `tab` field apply on the desktop layout only. The phone keeps its own tab ids and its Back behaviour (`onPop` keeps `setTabKey('')`).
3. **Unconverted pages.** The shell puts `data-fit-scroll="legacy"` **on the page's own root element**, never in a wrapper div (a wrapper remounts the page). CSS selector: `.app-shell .content > :not(.fit-page)`.
4. **Notes and footnotes.** New component **`FitNote`** (`text`, optional `lines` = 1): shows one line with a **More** button that opens the full text in place. Built by fit-shell, then used by every lane. A `title`-only tooltip is not allowed, because touch cannot reach it.
5. **Nested tabs** (for example Customers: split view with Overview / Stays / … inside): the breadcrumb shows **one** tab crumb, the outer sub-screen; inner tabs are not in the breadcrumb or the search palette.
6. **Heading baseline.** The page h1 goes from 30 px to 22 px; the Modules Home and category headings go from **26 px** to 22 px. The font baseline compares per element kind, so the check does not change.
7. **Ownership.** `tools/cssTextScale.mjs` (`readStyleSources`, the CSS bundle order) belongs to fit-shell, together with `build.mjs`.
8. **User button on touch tablets.** The 56 px top bar shows the user button on every desktop-layout device, touch tablets included, as in the approved mockups. fit-shell lifts `z-tablet.css`'s coarse-pointer hiding of `.current-user` inside `.app-shell .topbar` only. "About this build" and "Sign out" stay in the user menu.
9. **Header actions never wrap.** The header row stays one row (48 px). The page's primary action is always visible; extra actions go into a **More** menu (`FitPage` prop `actions` = primary first, the rest overflow).
10. **Tab keyboard.** Arrow keys move focus and select (automatic activation), as built; Home / End go to the first / last tab.
11. **Below 900 px**, `.fit-cols` stacks, but its panels keep a definite height (no `flex:1 1 0` inside an auto-height parent). A table panel never collapses below the 160 px panel minimum at `tab-port`. fit-shell fixes this in `arrivals` first; the guide shows the pattern.
