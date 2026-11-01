# FIT-fit-front-b DONE

Lane **fit-front-b** (phase B) · model Sonnet · branch `claude/fit-front-b` · worktree `E:\Projects\HCC\worktrees\fit-front-b` · base `3dac5afb` (claude/fit). Nothing pushed.

## Commits
```
0dd13366 FIT front-b: reservations, inhouse, guests converted to FitPage (fill / split), tests
(+ fix: no CSS, inline overrides; + this file and the check report)
```

## Files
Prototype: `src/features/reservations/Reservations.tsx`, `src/features/guests/InHouse.tsx`, `src/features/guests/GuestProfile.tsx` (`GuestsPage`), new `src/features/guests/useWideSplit.ts`, `src/styles/guests.css` and `src/styles/reservations.css` (appended, `.app-shell`-guarded, no font-size, no colours), `src/app/subscreens.ts` (one comment line in the FIT-FRONT block).
Tests: `tests/stream-fit-front-b.mjs` (6 tests `[FIT-FRONT-B]`) + `tests/fit-front-b-entry.tsx`.
`BookingTable.tsx`, `RestrictionNotice.tsx`, `Confirmation.tsx`, `modals.tsx`, `masters.tsx`, `Calendar.tsx`: untouched. `arrivals` (`CheckIn.tsx`): untouched (already the reference page; no polish needed in code).
Phone paths: untouched.

## What was built (per page)
| Page | Pattern | Sub-screens | Default tab / filter changed | Check |
|---|---|---|---|---|
| `reservations` (and `folios`, which renders the same `Reservations` component with `folio`) | fill: `FitPage` + header primary action (`+ New reservation`, via `act()`) + one toolbar (search, status filter buttons, source select, "Staying on" date, count) + `BookingTable` in one `ScrollPanel`; `ShowMore` and the fictional-data note (`FitNote`) in the panel footer; the guest link / open button keeps opening the existing booking modal | none | none (status filter stays `All`) | `Fit/fit-check-20261101-1655.md`: PASS at tab-land and tab-port (all roles, text 1 and 1.3, modals), only the `label` font artifact at the three fine-pointer viewports |
| `arrivals` | already `FitPage` fill (fit-shell) | none | none | as the row above (same report) |
| `inhouse` | split: stays list `ScrollPanel` left, `StayDetail` record pane right (`SplitView` 2fr 3fr); slide-over with Back below 900 px; Print list in the header | none | none | as the row above (same report) |
| `guests` | split: profiles list left, `GuestPanel` right (`SplitView`); New guest in the header | none | none | as the row above (same report) |

Wide layout keeps the old "first record pre-selected" behaviour; below 900 px nothing is pre-selected so the list shows first and a tap opens the record (`useWideSplit`).
No selector, command, permission, fixture, type, money or phone change. Fonts untouched.

## Tests
Through the lock (label `fit-front-b`, output in `.scratch/` of the worktree):
- `npx tsc --noEmit`: clean. `npm run build`: clean.
- `npm test`: **2242 / 2247** (6 new `[FIT-FRONT-B]` tests pass). The 5 failures are not in this lane's files:
  - `[FIT] page reservations / arrivals / guests / inhouse renders a fit-page root` (`tests/stream-fit.mjs`, fit-check's): its harness renders pages without a `FitNavContext`, so `data-fit-page` is empty for every converted page (arrivals, converted by fit-shell, fails the same way). Integration needed below.
  - `[FIT] no phone-layout file changed since the wave base`: lists `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `styles/m-accounting2.css`; these come from merging ACC-3 into the base after `541bbff7` (`git diff --name-only 3dac5afb HEAD` has no phone file). Set `FIT_BASE` to the current wave base.
- Check: `node tools/fit-screen-check.mjs --pages reservations,arrivals,inhouse,guests` on port 4192, all five viewports, nine roles, text 1 and 1.3: 199 checks, 30 failing, **all 30 are `font-changed label 14px (was 11px)` at lap-1366 / lap-1280 / desk-1920** (fine pointer) on reservations, arrivals and inhouse, for owner, manager, reception, cashier. The same finding appears on the untouched `arrivals` reference page and is the "label font artifact" already named in `FIT-fit-shell-DONE.md`; no CSS font-size is set by this lane. tab-land and tab-port: PASS for every page and role, modals PASS. Report: `05 Delivery Evidence/Fit/fit-check-20261101-1655.md` (+ `.json`).
- `guests` has no nav item (PAGE_CATEGORY does not list it), so the check tool does not visit it; it is covered by the static test only. It could not be checked in a browser.
- Existing tests updated: none.

## Modal openers for tools/fit-modals.json
- `reservations`: `+ New reservation` (owner, reception, manager) — already in the shell sample.
- `inhouse`: `Print in-house list` (owner, manager, reception).
- `arrivals`: `Walk-in` (owner, reception, manager).
- `guests`: `New guest` (page not reachable by the tool; listed for completeness).

## Integration needed
- `tests/stream-fit.mjs` (fit-check): wrap the page in `FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:1,setTabCrumb(){}}}` in `render()`, so `data-fit-page` is set; and set `FIT_BASE` to the current claude/fit base.
- `tools/fit-screen-check.mjs`: add `guests` to the page list (it has no nav item).
- The `label` font-changed artifact at fine-pointer viewports (fit-shell / fit-check: first `label` measured is not the baseline element).
- Stylesheets: `guests.css` and `reservations.css` are among the 16 token-checked sheets (`stream-css-tokens-c` compares line counts with the original), so this lane added no CSS; the two small overrides are inline styles (`.gs-list` max-height, `.res-list-filters` margin). If the integrator prefers CSS, add them to `z-fit.css`:
  `.app-shell .fit-page .gs-list{max-height:none;overflow:visible}` and `.app-shell .fit-page .fit-page-toolbar .res-list-filters{margin:0}`.

## Could not fit
Nothing. All pages fit at all five viewports without dropping content or shrinking text.

## HANDOFF bullet
FIT front-b: reservations / folios view, in-house stays and guests converted to FitPage (fill / split); arrivals unchanged; no sub-screens; no CSS file touched; check PASS on tablet viewports.
