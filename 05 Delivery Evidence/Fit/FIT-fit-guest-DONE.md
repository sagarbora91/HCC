# FIT-fit-guest DONE

Lane **fit-guest** (phase B) · model Sonnet · branch `claude/fit-guest` · worktree `E:\Projects\HCC\worktrees\fit-guest` · base `3dac5afb` (claude/fit after fit-shell and fit-check). Nothing pushed.

## Commits

```
af78c31e FIT guest: ShowMore status assertion tolerant of the page-size preference
890c796b FIT guest: page root split-class override, test fixes
ba3f6244 FIT guest: triggers and documents use one panel each (160 px minimum); test harness externals
aa315152 FIT guest: guest-services, gsv-my-requests, messages, documents, customers converted to FitPage patterns; SUB_SCREENS; guest-fit.css; tests
(this file: the final commit)
```

## Files

Prototype (all inside the lane's ownership, contract §7 / briefs "Phase B as run"):
- `03 Prototype/src/features/services/GuestServices.tsx` (`guest-services`, `gsv-my-requests`; the phone `Mobile.tsx` untouched)
- `03 Prototype/src/features/messaging/Messages.tsx` (desktop page only; `MsgMessagesMobile` untouched), `Documents.tsx` (desktop page only), `shared.tsx` (new `MsgSimulatedNote`, the FitNote form of the demo "Simulated" / production "Messaging is off" notes)
- `03 Prototype/src/features/customers/Customers.tsx`
- `03 Prototype/src/styles/guest-fit.css` (new; every selector starts with `.app-shell`; no font-size, no colour literal)
- `03 Prototype/src/app/subscreens.ts` (my `// FIT-GUEST` block only: 4 + 4 lines)
- Tests: `tests/stream-fit-guest.mjs` + `tests/fit-guest-entry.tsx` (new, 16 tests `[FIT-GUEST]`); two updated tests (below).

No change to selectors, stream functions, commands, handlers, permissions, fixtures, `types.ts` / `SCHEMA`, money or GST code. Nothing under `src/mobile`, `src/app/mobile` or any `*Mobile.tsx`.

## What was built, per page

| Page | Pattern | Sub-screens (`SUB_SCREENS`) | Default tab / filter (old → new) | Check |
|---|---|---|---|---|
| `guest-services` | `FitPage` + `StatStrip` (the 4 tiles) + `FitTabs`; each table in a `ScrollPanel` whose head holds the existing filter chips (Open / Overdue / Done / All …); primary action of the open tab in the header; the recovery-discount footnote is a `FitNote` in the Complaints footer | `requests`, `complaints`, `lost-found`, `reports` (old ids were the labels `Requests` … `Lost & found`) | Requests → Requests (unchanged); filters unchanged (Open / Open / Held) | PASS at all five viewports, nine roles, text scale 1 and 1.3 (report `05 Delivery Evidence/Fit/fit-check-20261101-1719.md`); 4 tabs also PASS |
| `gsv-my-requests` | fill: filter chips in the toolbar, request cards (`gsv-cards` grid) in one `ScrollPanel`, the info note is a `FitNote` in its footer | none (no tabs) | filter Open → Open | PASS at all five viewports, nine roles, text scale 1 and 1.3 (report `05 Delivery Evidence/Fit/fit-check-20261101-1719.md`) |
| `messages` | `FitPage` + `StatStrip` (5 counters) + `FitTabs`; Compose first, Simulate delivery second (§11.9); **Outbox adopts `usePaged` + `ShowMore`** (100 rows, ShowMore in the panel footer; 1,350 rows on FY26); Triggers = one panel holding Pending triggers + Trigger rules (two stacked panels were each < 160 px at 640 px high); Templates grid and Consent table each in a panel; simulated / "Messaging is off" notes are `FitNote`s (`MsgSimulatedNote`) | `outbox`, `triggers`, `templates`, `consent` | Outbox → Outbox (Tech admin in production: Templates, tabs Triggers + Templates only — unchanged) | PASS at all five viewports, nine roles, text scale 1 and 1.3 (report `05 Delivery Evidence/Fit/fit-check-20261101-1719.md`); 4 tabs also PASS |
| `documents` | `layout="split"` (`SplitView` 1fr 1fr): left = ONE panel (step 1 sample documents with Extract, then Capture history); right = step 2 Review and confirm (`MsgDocReview`); below 900 px the review slides over the list with Back, and Back is remembered per capture | none | — | PASS at all five viewports, nine roles, text scale 1 and 1.3 (report `05 Delivery Evidence/Fit/fit-check-20261101-1719.md`) |
| `customers` | `layout="split"`: left `ScrollPanel` customer list (`usePaged`, ShowMore + the fictional-data `FitNote` in the footer, search in the toolbar); right = Customers 360 record pane. Header + three facts, then inner `FitTabs` **Overview** (Profile: details, duplicates, Edit / Registration card) · **Stays** (stay rows → booking dialog) · **Legacy** (W1a old-Lodge stays, read only, no folio link) · **Money** (lifetime spend, average tariff, bills; only with the `billing` permission, as before) · **Notes & requests** (guest requests + complaints) | none (inner tabs are not sub-screens, contract §11.5; the bar is built with `page={INNER_PAGE}` so `stream-fit.mjs` does not list it) | History tab → Overview tab; on wide screens the first customer is still open on arrival, on narrow screens the list shows first and the customer slides over it | PASS at all five viewports, nine roles, text scale 1 and 1.3 (report `05 Delivery Evidence/Fit/fit-check-20261101-1719.md`) except the `label` baseline artifact (below) |

**Phone:** `CustomersPage` / `CustomerPanel` dispatch on `display.isMobile`; the phone path is the old code verbatim (`CustomersPhonePage`, `CustomerPanelPhone`, `History` with `part='all'`), because the phone shell has no separate Customers page. `guest-services`, `gsv-my-requests`, `messages`, `documents` have their own phone pages (untouched).

**Layout decisions vs the contract text:** Messages / Documents rows keep opening their existing dialogs (`msg-message`, …) instead of a split record pane (§5: "rows that open a modal keep the modal"); Documents is a split because its record (the review form) is inline. ID images: the Customers page renders no image element; the guest-ID reveal path is untouched.

## Existing tests updated (never deleted or loosened)

- `tests/stream-customers-legacy.mjs` "Customers 360 desktop: Legacy badge rows …": the legacy rows now live on the Legacy inner tab, so the test renders `CustomerPanel` with the new optional prop `initialTab:'legacy'` for the rows (old bill number, amount, no `<button>` in the legacy list) and still asserts "(3 legacy)" on the page list; the no-money-for-housekeeper assertion is unchanged.
- `tests/stream-wd5-gaps.mjs` "Phone Messages … Tech admin": desktop parity regex `role="tab" aria-selected="true" class="active">Templates` became `data-tab="templates" aria-selected="true"[^>]*class="active">Templates` (FitTabs markup).

## Tests

All through the lock (label `fit-guest`), output in `E:\Projects\HCC\worktrees\fit-guest\.scratch\`.

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean (run on the first conversion commit; later edits were confined to one component, CSS and tests, and `npm test` bundles them) |
| `npm test` (last full run, commit `890c796b`) | 2256 tests, **2248 passed, 8 failed**: 1 = my own assertion on the ShowMore text (`Showing 100 of …`; fixed afterwards by accepting any page size, re-run queued behind the lock, see below), 7 = `[FIT]` tests of fit-check's `stream-fit.mjs` that fail for every converted page and are not caused by this lane (Integration needed 3). All 16 other `[FIT-GUEST]` tests, the two updated tests and everything on `main` pass. |
| `npm run build` | clean (same run) |
| `node tools/fit-screen-check.mjs --pages guest-services,gsv-my-requests,messages,documents,customers` (server `PORT=4196`, through the lock) | 423 checks, 12 failing, all `font-changed label 14px (was 11px)` on Customers at lap-1366 / lap-1280 / desk-1920 (a baseline artifact, Integration needed 2); 0 page-scroll, 0 content-scroll, 0 cramped, 0 touch-size, 0 hidden-action, 0 not-converted; text scale 1.3 at tab-land and lap-1366: all PASS. Report `05 Delivery Evidence/Fit/fit-check-20261101-1719.md` (+ `.json`; screenshots in `screens-20261101-1719/`, kept out of git). Earlier run (31 failing: Triggers and Documents stacked panels < 160 px, the page-root `.fit-split` collision, Customers narrow list 109 px) is what the fixes in commits `ba3f6244` and `890c796b` removed. |
| `check-print-layout`, `check-tablet-back`, `check-overlay-compat` | not run (integrator's list; no file they exercise is in this lane's ownership) |

## Open items

- `label` font artifact on Customers (see Integration needed 2): the check reports `font-changed label 14px (was 11px)` at the three fine-pointer viewports; no font was changed.
- Customers Overview / Money content keeps today's data only; "Notes" in the contract is delivered as "Notes & requests" (guest requests and complaints), because the page has no free-text notes field of its own.

## Integration needed

1. **`FitPage layout="split"` root collides with `.fit-split` (z-fit.css line 115).** `FitPage` gives its root the class `fit-split`, and `z-fit.css` styles `.fit-split` as the `SplitView` grid, so the page root itself becomes a 2-column grid (header and body side by side on wide screens, a stretched header on narrow ones). My pages work around it in `guest-fit.css` (`.app-shell .fit-page.fit-split{display:flex;flex-direction:column;grid-template-columns:none;gap:10px}`). Proper fix (fit-shell): scope the grid rules to `.fit-page-body>.fit-split`, or rename the page-root class; every lane that uses `layout="split"` is affected.
2. **`tools/fit-screen-check.mjs` / `tools/fit-baseline/fonts-*.json` (fit-check):** `label` is measured on the first visible `label` in `.content`; Customers has the search box (`label.search`, 14 px) while the baseline `label` entry (11 px) was the Demo toolbar's field. Exclude or re-record, as FIT-fit-shell-DONE item 1 says for `arrivals`.
3. **`tests/stream-fit.mjs` (fit-check):** `[FIT] page <id> renders a fit-page root` fails for every converted page including `arrivals` because the harness gives `HccContext.page` but `FitPage` reads `data-fit-page` from `FitNavContext.page`; either wrap the harness render in `FitNavContext` `{desktop:true,page:id,…}` or let `FitPage` fall back to `useHcc().page`. And `[FIT] no phone-layout file changed since the wave base` compares with `FIT_BASE` 541bbff7, which pre-dates the ACC-3 merge (it lists `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css`): set `FIT_BASE` to the claude/fit head after the merge. Neither is caused by this lane (`git diff claude/fit..HEAD` touches no phone file).
4. **`tools/fit-modals.json`** — modal openers of this lane:
   - `{"page":"guest-services","opener":"New request","lane":"fit-guest","roles":["owner","manager","reception"]}`
   - `{"page":"messages","opener":"Compose","lane":"fit-guest","roles":["owner","manager","reception"]}`
   - `{"page":"gsv-my-requests","opener":"Log found item","lane":"fit-guest","roles":["housekeeper","supervisor"]}`
5. `tests/stream-fit.mjs` GENERATED / inner tabs: Customers' inner `FitTabs` deliberately has no `SUB_SCREENS` entry (§11.5); if the static comparer is extended to flag `<FitTabs page={…}>` with a variable, keep `INNER_PAGE` exempt.

## HANDOFF bullet

FIT lane fit-guest DONE (claude/fit-guest, Sonnet): guest-services (tabs Requests / Complaints / Lost & found / Reports + StatStrip), gsv-my-requests (fill), messages (tabs Outbox / Triggers / Templates / Consent, Outbox on usePaged), documents (split: samples + history | review), customers (split + Customers 360 inner tabs Overview / Stays / Legacy / Money / Notes & requests; phone path unchanged). Prototype tests and check numbers in `05 Delivery Evidence/Fit/FIT-fit-guest-DONE.md`; check: 423 checks, only the `label` font artifact on Customers fails.
