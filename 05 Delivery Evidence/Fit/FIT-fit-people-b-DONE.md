# FIT-fit-people-b DONE

Lane **fit-people-b** (phase B) · model Opus · branch `claude/fit-people-b` · worktree `E:\Projects\HCC\worktrees\fit-people-b` · base `3dac5afb` (head of `claude/fit`, fit-shell + fit-check merged, main after ACC-3). Nothing pushed.

## Commits

```n240bd313 FIT people-b: Reports FitPage stays fill (fit-split root class collides with SplitView); accounting in-page action bars sticky in the section panel
dcc3e8ef FIT people-b: lane CSS in accounting.css / assistant.css / catalogue.css (m-* sheets stay byte-identical: CT-B/C token round-trip and the FIT phone-path rule)
c2e74d11 FIT people-b: accounting (FitTabs), reports (split), plans-extras (FitTabs), assistant (chat) on FitPage; sub-screens; lane tests
(this file + check report: the final commit)
```

## Files

Prototype (all in this lane's ownership):
- `03 Prototype/src/features/accounting/Accounting.tsx` (`AccountingPage` only; `ACC_TAB_IDS` exported)
- `03 Prototype/src/features/reports/Reports.tsx` (`ReportsPage` only), `03 Prototype/src/features/reports/serverBits.tsx` (`ServerCollections` desktop branch, production build only)
- `03 Prototype/src/features/folio/catalogue.tsx` (`PlansExtrasPage` only; the charge / discount / meal-plan dialogs untouched)
- `03 Prototype/src/features/assistant/Assistant.tsx` (`AssistantPage` only; `SimLabel` folded into a `FitNote`; `AssistantMobile` untouched)
- `03 Prototype/src/app/subscreens.ts` — new block `// FIT-PEOPLE-B` appended at the end of the registry (the brief's seven blocks have no people-b block), 24 lines
- New stylesheets (desktop only, every rule under `.app-shell`, no font size): `03 Prototype/src/styles/accounting.css`, `assistant.css`, `catalogue.css`, `reports.css`

Tests: `03 Prototype/tests/stream-fit-people-b.mjs` (7 tests `[FIT-PEOPLE-B]`), `03 Prototype/tests/fit-people-b-entry.tsx`.
Evidence: `05 Delivery Evidence/Fit/fit-people-b-check/fit-check-20261101-1516.md` (+ `.json`, screenshots `screens-20261101-1516/`, untracked like fit-shell's); first run `fit-check-20261101-1440.*`.

Not touched: any `m-*.css` (the CT-B / CT-C token round-trip tests and `[FIT]` phone-path rule forbid it — that is why the lane CSS is in four new feature sheets), `Mobile.tsx`, `src/mobile/**`, `src/app/mobile/**`, selectors, commands, permissions, fixtures, money / GST code.

## What was built (per page)

| Page | Pattern | Sub-screens (`SUB_SCREENS`) | Default tab / filter | Check (fit-check-20261101-1516) |
|---|---|---|---|---|
| `accounting` | `FitPage` fill + `FitTabs` over the **existing 21 sections with their existing ids** (vouchers, manual, gst, tds, assets, tb, statements, pl, bs, ageing, gstsum, cashratio, exports, daybook, chart, parties, opening, exceptions, recon, mapping, tally); header actions = **Lock period** (first, the committing action) + Export CSV; toolbar = business-date picker + day-status pill + lock line (`#acc-lock-why` kept for the button's `aria-describedby`) and a `StatStrip` (the four former Stats, same texts); each section's existing content in one `ScrollPanel`; the generic-export note and the footnote in a `FitNote` in the panel footer. In-page action bars (Opening balances Add line / Save draft / Submit, reopen form) are sticky at the panel bottom. Roles without the Tally export (Manager, Cashier) never land on `tally` (asked or remembered → Vouchers); the tab is not shown to them, exactly as before. | 21 (all ids above) | unchanged: Vouchers | 0 page-scroll / content-scroll / cramped / hidden-action / touch-size, all 5 viewports × owner, manager, accountant (+ cashier, reception … see "not available"), every section, text 1 and 1.3. Only `font-changed` artifacts — see below |
| `reports` | split: `SplitView` (ratio 1fr 3fr) — grouped report list left (Summary: History summary · Reports: the five report tabs · Reference: Report dictionary), the open report right in a `ScrollPanel` whose **header holds Export CSV / Export Excel / Print** (same role rule RPT-003, Cashier none); date range, presets, month, validation and the masked-scope note in the toolbar; the info note in a `FitNote` footer. The FY **History summary** card (it stood above the filters) is the list's first entry. Below 900 px the report slides over the list; Back to list / Android Back. | — (contract: none) | unchanged: Daily operations, today | PASS everywhere (0 failures) |
| `plans-extras` | `FitPage` + `FitTabs` per section; CSV in the header; From / To in the toolbar; each section a `ScrollPanel` with its title and its "Manage list…" link in the panel header; the meal-plan GST note in a `FitNote`. **Phone keeps the old stacked markup** (this page has no phone version: `display.isMobile` branch, byte-identical). | service-charges, stays-by-meal-plan, discounts-by-reason | new tabs, default Service charges (was: all three stacked) | 0 layout failures; only `font-changed label` artifact |
| `assistant` | chat: `fit-cols` — **Conversation** `ScrollPanel` (thread is the panel body, no inner scroller; newest at the bottom via the existing scroll-to-end), **question box pinned in the panel footer**, Clear my history in the panel header; side `ScrollPanel` "Suggested questions" holding the suggestion chips, Daily briefing and Anomaly flags; the simulation label is a `FitNote` in the toolbar; the badge in the header. | — | — | 0 layout failures; only `font-changed label` artifact |
| (`reports`, production build, Cashier) `ServerCollections` | desktop branch on `FitPage` fill (date in the toolbar, report in a `ScrollPanel`); phone markup unchanged | — | — | not checkable (prototype build) |

### The remaining check failures are measurement artifacts, not font changes

Check report: 808 rows, 264 FAIL, **every one `font-changed`** of three kinds (0 page-scroll, 0 content-scroll, 0 not-converted, 0 cramped, 0 hidden-action, 0 touch-size, 0 modal failures; text scale 1.3 all PASS):

1. `font-changed select 12px (was 11px)` — accounting, fine-pointer viewports only. The first visible `select` is the business-date picker (`.acc-date select{font-size:12px}` in `m-accounting.css`, unchanged, 12 px on the old page too). The fine-pointer baseline has only `11px` because it was recorded when the first `select` was the Demo toolbar's (now in the Demo pill popover) — the same artifact fit-shell reported for `label`.
2. `font-changed label 14px (was 11px)` — plans-extras (From / To labels) and assistant (`label.ai-sr`), fine-pointer only. Same cause (baseline label = Demo toolbar `.demo-field` 11 px); fit-shell DONE item 1.
3. `font-changed td 10px (was 11px)` — accounting › Opening balances only: the first `td` there is the `td.muted` "No lines yet." row (existing `.muted` 10 px rule). The baseline was recorded on default tabs only, so it has never seen this tab.

`git diff 3dac5afb -- "03 Prototype/src/styles"` = four new sheets, 40 lines, **no `font-size`** (the lane test asserts it). No font was changed.

### Modal openers for `tools/fit-modals.json` (all PASS in the lane run, 5 viewports)

```json
{"page":"accounting","selector":"button.acc-link","label":"accounting voucher","lane":"fit-people-b","roles":["owner","accountant"]},
{"page":"accounting","tab":"manual","opener":"New voucher","lane":"fit-people-b","roles":["owner"]},
{"page":"accounting","tab":"chart","opener":"Add ledger","lane":"fit-people-b","roles":["owner"]},
{"page":"reports","opener":"Print","lane":"fit-people-b","roles":["owner","manager","accountant"]}
```
(Added to a local copy for the run, then reverted — the file is fit-check's.)

## Tests

Through `E:\Projects\HCC\worktrees\fit\tools\Invoke-Serialized.ps1` (label `fit-people-b`), logs in `E:\Projects\HCC\worktrees\fit-people-b\.scratch\`:

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | **2242 / 2248**; all 7 `[FIT-PEOPLE-B]` pass. The 6 failures are fit-check's `stream-fit.mjs` harness and fail on `claude/fit` too: `[FIT] no phone-layout file changed since the wave base` (lists `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css` — changed by the ACC-3 merge after base `541bbff7`, none by this lane) and `[FIT] page <id> renders a fit-page root` for reports, **arrivals** (fit-shell's), accounting, assistant, plans-extras (`root needs data-fit-page` — the harness renders pages without `FitNavContext`, so `data-fit-page` is empty; see Integration needed). |
| `npm run build` + `PORT=4199 node serve.mjs` + `node tools/fit-screen-check.mjs --pages accounting,reports,plans-extras,assistant --out "05 Delivery Evidence/Fit/fit-people-b-check"` | see above: only the three font artifacts |

Existing tests that asserted old structure: none needed changes (`stream-accounting-ui*.mjs` find every section label in the tab bar; `sync-reports.mjs` "Loading the report" still renders). Tests updated: none. New: `stream-fit-people-b.mjs` — fit-page root per page, accounting ids = SUB_SCREENS + default + `go` tab + Lock first + StatStrip + `#acc-lock-why`, Tally rule (manager asked `tally` → Vouchers; accountant keeps it; cashier / reception / housekeeper "Not available"), plans-extras tabs + phone markup unchanged, reports split + exports in panel header + Cashier no exports, assistant chat structure, CSS scoped to `.app-shell` with no font size.

## Open items

- The search palette lists "Accounting › Tally export" for every role that opens Accounting (palette filters by page, not tab); a Manager who picks it lands on Vouchers. Acceptable; a per-tab role filter would be a palette (fit-shell) change.
- Accounting at 1138 × 640: the StatStrip (30 px values, DEC-195) makes the toolbar ~110 px, so the section panel body is ~170–200 px — passes the 160 px rule, but tight; the Opening balances sticky action bar takes ~60 px of it.
- Reports `History summary` now sits in the report list instead of above the filters (content kept, one tap away).
- Production-only `ServerCollections` / `ServerHistorySummary` not browser-checked (prototype build only).

## Integration needed

1. **fit-shell (`src/components/fit.tsx`)**: `FitPage layout="split"` gives the page root the class `fit-split`, the same class as `SplitView`'s grid, so the whole page becomes a two-column grid (seen on Reports in run 1440). This lane uses the default `fill` layout for Reports. Fix: root class `fit-page fit-layout-<layout>` (and any `.fit-split` page-level CSS), or drop the modifier; `FIT_Component_Guide.md` §2 shows `layout="split"` — every lane following it hits this.
2. **fit-check (`tests/stream-fit.mjs`)**: the page harness renders without `FitNavContext`, so `data-fit-page` is never set → every converted page fails (arrivals too). Wrap `pages[id]()` in `<FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:0,setTabCrumb:()=>{}}}>` (import from `../../src/components/fit`). And `FIT_BASE` must move past the ACC-3 merge (or exclude its files) — the phone-path test fails on the base for `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css`.
3. **fit-check (`tools/fit-baseline/fonts-*.json`)**: re-record `select` and `label` inside `.content` (fine-pointer baselines hold only the Demo toolbar's 11 px), and allow `td` 10 px (`td.muted`, existing rule) or record non-default tabs.
4. **`tools/fit-modals.json`**: append the four openers above.
5. **`src/app/subscreens.ts`**: this lane's block `// FIT-PEOPLE-B` was appended after `// FIT-ADMIN` (the registry had no people-b block); other twelve-lane blocks appended at the end will conflict textually — keep all.
6. New stylesheets `src/styles/{accounting,assistant,catalogue,reports}.css` load in name order before the `m-*` sheets; their `.app-shell` selectors outrank the module rules, so order does not matter.

## HANDOFF bullet

FIT phase B lane fit-people-b DONE (claude/fit-people-b, Opus): Accounting = FitTabs over the 21 existing sections (ids kept, Lock first, StatStrip toolbar, sticky in-page action bars), Reports = split (grouped list, exports in the report panel header, History summary as a list entry), Plans & extras = 3 FitTabs (phone unchanged), Assistant = chat (pinned question box, side panel). tsc clean, npm 2242/2248 (6 pre-existing fit-check harness failures), check: 0 layout failures at 5 viewports × roles × text 1 / 1.3; only baseline font artifacts (select/label/td). Integration: FitPage `layout="split"` class collides with SplitView.
