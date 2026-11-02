# FIT-fit-people-a DONE

Lane **fit-people-a** (phase B, twelve-lane split) · model Opus · branch `claude/fit-people-a` · worktree `E:\Projects\HCC\worktrees\fit-people-a` · base `3dac5afb` (head of `claude/fit` after fit-shell + fit-check). Nothing pushed.

## Commits

```
(git log --oneline 3dac5afb..HEAD — this file is in the last commit)
83fb894f FIT people-a: staff, my-hr, Day closing (StepFlow), compliance on FitPage; sub-screens; tests
6fccad1d FIT people-a: tests forget the remembered step for default renders; reopen-form heading match
416dced2 FIT people-a: closing page root stays a flex column (layout=steps class collision), compliance tools in the panel head, my-hr 2+1 panels on tablet portrait
(final)  FIT people-a: compliance renders its tab from useFitTab; DONE file + check evidence
```

## Files

Prototype (ownership: `src/features/staff/**`, `src/features/closing/{DayClosing,ClosingReport,Corrections,ReopenDay}.tsx`, `src/features/compliance/**`, their stylesheets):
- `03 Prototype/src/features/staff/HrDesk.tsx`, `Leave.tsx`, `MyHr.tsx` (desktop branches only; every `phone` branch unchanged)
- `03 Prototype/src/features/closing/DayClosing.tsx` (rewritten as StepFlow), `Corrections.tsx`, `ReopenDay.tsx` (desktop branch only; `phone` look unchanged). `ClosingReport.tsx` untouched.
- `03 Prototype/src/features/compliance/CompliancePage.tsx` (desktop page only; `ComplianceMobile` unchanged)
- `03 Prototype/src/app/subscreens.ts` — new `// FIT-PEOPLE-A` block at the end (append-only; the block did not exist)
- New stylesheets (desktop only, every rule under `.app-shell`, no font size, tokens only): `src/styles/staff-fit.css`, `src/styles/closing-fit.css`, `src/styles/compliance-fit.css`. Why new files: `tests/stream-fit.mjs` treats every `src/styles/m-*.css` as a phone file (check 4), so the module sheets `m-staff*.css` / `m-compliance*.css` cannot take desktop rules; `closing.css` is shared with fit-cash-a, so my rules sit in `closing-fit.css` to avoid a merge conflict.

Tests: `03 Prototype/tests/stream-fit-people-a.mjs` (8 tests `[FIT-PEOPLE-A]`), `03 Prototype/tests/fit-people-a-entry.tsx`; updated `03 Prototype/tests/stream-reopen.mjs` (see Tests).
Evidence: `05 Delivery Evidence/Fit/fit-people-a-check/fit-check-20261101-1601.md` + `.json` (screenshots stay in the lane's `.scratch\c3\screens-20261101-1601\`, not committed).

Phone paths: no file under `src/mobile`, `src/app/mobile`, `*Mobile.tsx`, `styles/m-*.css`, `styles/mobile*.css` touched (`git diff 3dac5afb..HEAD --name-only`).

## What was built — per page

| Page | Pattern | Sub-screens (`SUB_SCREENS`) | Default tab / filter (old → new) | Check |
|---|---|---|---|---|
| `staff` (Staff & payroll, `HrDesk`) | `FitPage` + `FitTabs` (the old HR tabs, same ids, role list unchanged → DEC-058 gating untouched); each tab = one `ScrollPanel` (fill), two / three panels side by side where a tab had several cards (`fit-cols`): Staff = counters row + staff table (filters + Add in the fixed head) + bank-detail changes beside it; Roster; Attendance (chip view row + register / month / Past days + corrections beside); Leave (chip view row; calendar = month bar + one panel; approvals; report; settings = 3 panels side by side; holidays); Payroll (month, status, Run / Lock / Unlock in the fixed head); Advances; Full and final; Statutory rates | people, roster, attendance, leave, payroll, advances, fnf, settings | unchanged (role's first tab, Staff); inner chip views (Attendance Today, Leave calendar) unchanged | PASS everywhere except the `font-changed` artifact on staff › leave (see below) |
| `my-hr` (My shifts & pay) | fill: today banner row + three panels side by side (week + this month · my leave · latest payslip + payslips, "Payslip PDF" in the panel footer); 2 + 1 at 600–899 px | — | — | PASS (all viewports, all roles, text 1 and 1.3) |
| `closing` (Day closing) | steps: `StepFlow` over 7 steps — the 6 old step cards + **Reopen & corrections** (Owner's reopen form / reopened-day panel + closed-date corrections side by side). Earlier closing records moved beside the Closing summary (step 5). Each step = `ScrollPanel` with the step's buttons in its always-visible footer. **"Close <date>" is the first header action on every step**, enabled exactly as before (`busy||!canClose||!!blockers.length`, same `DAY_CLOSE` command); once the day is closed the header's first action is **Open next business day** (same rule `busy||!closed||!canClose`, same `NEXT_DAY`). The business-date banner + read-only note are the toolbar | preflight, post-room-nights, cash-shift, close-business-date, closing-summary, open-next-business-day, reopen-and-corrections | new: the first step not done; **Reopen & corrections** while a day is reopened (today cannot close until it is closed again) | PASS except the `font-changed select` artifact on step 7 |
| `compliance` | `FitPage` + `FitTabs` Calendar / Dashboard (the SCC views on main; contract's "Filings / Licences" do not exist on main). Header: Action Center; toolbar: FY, firm line, Firm profile, Manage compliance; the export tools (CSV, Print month, Evidence ZIP, GSTR-1, CA pack) in the panel's fixed head; Calendar = month-tab row + one panel (heading, control note, strip, counters, filter, cards); Dashboard = one panel. The shared UI store (`cmpUiSet`) still drives the "Open →" jumps; `useFitTab` and the store are kept in step | calendar, dashboard | unchanged (Calendar) | PASS except the `font-changed` artifact |

Texts added (layout guidance only, flagged for review): step 4 "Close <date> is the first button in the page header; it stays there on every step."; step 6 "Open next business day is the first button in the page header."; rail notes ("5 to resolve", "8 to post", "5 blocking", "24 Oct reopened"). Info notes / section descriptions became `FitNote` (one line + More, same 10 px size as the old `.section-title p` / `.info-note`).

## Tests

All through `E:\Projects\HCC\worktrees\fit\tools\Invoke-Serialized.ps1 -Label fit-people-a` (scripts in `.scratch\`):

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | **2243 / 2249** — the 6 failures are not this lane's: `[FIT] no phone-layout file changed since the wave base` (files from ACC-3 on the base: `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css` — fails on the base too) and `[FIT] page <id> renders a fit-page root` for **arrivals** (fit-shell's page — fails on the base too), closing, staff, my-hr, compliance: the `stream-fit.mjs` harness renders pages without `FitNavContext`, so `FitPage` cannot write `data-fit-page` (see Integration needed 1). All 8 `[FIT-PEOPLE-A]` tests pass. |
| `node tools/fit-screen-check.mjs --pages staff,my-hr,closing,compliance` (all 5 viewports, all 9 roles, text 1 + 1.3; server `PORT=4198`) | report `05 Delivery Evidence/Fit/fit-people-a-check/fit-check-20261101-1601.md`: 654 checks, 36 failing — **all `font-changed select` / `label` at the fine-pointer viewports**, an artifact of the baseline (below). 0 page-scroll, 0 content-scroll, 0 not-converted, 0 cramped, 0 hidden-action, 0 touch-size; text 1.3 all PASS. |

**Font artifact:** `tools/fit-baseline/fonts-*.json` holds `select: [11px]` and `label: [11px]` only — the demo toolbar's controls, recorded before the tool measured inside `.content`. The selects / labels flagged are the pages' own, at their main sizes (no font rule in this lane): compliance FY label + select 12 px (`.cmp-fy` in `m-compliance-scc.css`), leave month jump select / labels 14 px, the closed-date correction selects 12 px. Same issue fit-shell reported for `label`.

Tests updated (not loosened):
- `tests/stream-reopen.mjs` `[REOPEN] Screens`: the desktop render takes a step (wrapped in `FitNavContext`, as `go('closing',{tab})` does). The reopened-day panel is asserted on the default step (new extra assertion: default = Reopen & corrections while a day is reopened); the preflight line "Reopened day closed again" on step `preflight`; "Closed again" (earlier records) on step `closing-summary`. A render without a step clears `hcc-fit-tab:closing` (Node 25 has a global `sessionStorage`, so a remembered step would otherwise leak between renders).

## Open items

- The two shell defects below are worked around in this lane's CSS / layout; the shell fix is the integrator's.
- Modal openers were not run through the check (they need `tools/fit-modals.json`, fit-check's file).
- The reopened-day panel's "Post on <date>" charge form sits low in its panel; its action bar is sticky inside the panel (`closing-fit.css`), but the check never runs with a reopened day (FY26 has none), so that state is unchecked in the browser.

## Integration needed

1. **`03 Prototype/tests/stream-fit.mjs`** (fit-check): the page harness must provide the desktop shell's nav so `FitPage` writes `data-fit-page`. In the generated `fit-entry.tsx`: add `import {FitNavContext} from '../../src/components/fit';` and render `<HccContext.Provider value={ctx}><FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:1,setTabCrumb:noop}}>{pages[id]()}</FitNavContext.Provider></HccContext.Provider>`. Fixes arrivals (fit-shell) and every converted page.
2. **`src/components/fit.tsx` / `z-fit.css`** (fit-shell): `FitPage layout="steps"` gives the page root the class `fit-steps`, which is also StepFlow's grid class, so the whole page becomes a two-column grid (header/toolbar/body squeezed into 260 px). Rename the root class (e.g. `fit-layout-steps`) or scope the grid to `div.fit-steps`. This lane overrides it for `[data-fit-page="closing"]` in `closing-fit.css`; fit-ops (handover) will hit it too. Same risk for `layout="form"` (`.fit-form` grid).
3. **`HeaderActions` in `fit.tsx`** (fit-shell): the actions row is `justify-content:flex-end` with `overflow:hidden`, so overflow goes to the **left** and `scrollWidth` never exceeds `clientWidth` — items are never moved into More (measured: compliance at tab-port had buttons at x = −429). Measure `el.firstElementChild.getBoundingClientRect().left < el.getBoundingClientRect().left` (or use `justify-content:flex-start` inside a right-aligned wrapper). This lane keeps one header action on compliance to stay clear of it.
4. **Font baseline** (fit-check): re-record `select` and `label` inside `.content` (or whitelist), as for fit-shell's `label` item.
5. **`tools/fit-modals.json`** — modal openers of this lane:
   - `{"page":"staff","tab":"people","opener":"Add staff member","lane":"fit-people-a","roles":["owner","manager","accountant"]}`
   - `{"page":"my-hr","opener":"Request leave","lane":"fit-people-a","roles":["housekeeper","manager"]}`
   - `{"page":"closing","opener":"Preview report","lane":"fit-people-a","roles":["owner","manager"]}`
   - `{"page":"compliance","opener":"Action Center","lane":"fit-people-a","roles":["owner","accountant"]}`
6. Merge note: `closing.css` untouched (fit-cash-a may edit it); the three new stylesheets sort before `z-*.css`, every rule out-ranks the z-fit utility rules by specificity.

## HANDOFF bullet

FIT lane fit-people-a DONE (claude/fit-people-a, Opus): Staff & payroll (8 HR tabs as page tabs, DEC-058 gating unchanged), My shifts & pay (3 panels), Day closing (StepFlow, 7 steps incl. Reopen & corrections; "Close <date>" always in the header with its old rule), Compliance (Calendar / Dashboard tabs) fit at all five viewports for all roles, text 1 and 1.3 — only the baseline `select`/`label` font artifact remains. tsc clean, 8 new tests; two shell defects (layout="steps" class collision, header More never triggering) and the stream-fit harness nav listed for the integrator.
