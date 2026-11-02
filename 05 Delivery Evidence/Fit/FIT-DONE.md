# FIT-DONE — wave W2a FIT (fit-to-screen desktop and tablet layout) · integrator

Integrator: fit-integrator (Opus) · branch `claude/fit` · worktree `E:\Projects\HCC\worktrees\fit` · 2 Nov 2026. Rulings DEC-194 (no page scroll), DEC-195 (no smaller text; h1 30 → 22 px), DEC-196 (sub-screens as tabs), APR-025. Nothing pushed. The gate (`tools\Start-Gate.ps1`) is the coordinator's.

## Summary

All eleven phase-B lanes are merged into `claude/fit` (order front-a, front-b, cash-a, cash-b, ops, guest, revenue, people-a, people-b, admin-a, admin-b); the only conflicts were the `SUB_SCREENS` tails in `src/app/subscreens.ts` (union kept, `// FIT-ADMIN-A` / `// FIT-ADMIN-B` markers added; no duplicate page/id). The shell defects the lanes reported are fixed, the review's blocker and should-fix items are resolved, the check tool now compares fonts **element by element against main**, and the full check passes:

- **Full `node tools/fit-screen-check.mjs`: PASS — 0 failing of 5,512 checks** (5 viewports × 9 roles × 52 pages × 128 sub-screens at text scale 1, text scale 1.3 on tab-land + lap-1366, 602 modal samples). Report `05 Delivery Evidence/Fit/fit-check-20261101-2326.md` / `.json`.
- Every page fits at tab-land: **document height 1.0 screen on every page** (was up to 163.7 screens).
- `npx tsc --noEmit` clean · `npm test` **2339 / 2339** (main 2176 + 163 FIT tests; `FIT_ENFORCE_ALL` on, no skips) · `npm run build` and `npm run build:production` clean, 0 warnings · `tests/check-print-layout.mjs`, `tests/check-tablet-back.mjs`, `tests/check-overlay-compat.mjs`, `tools/tablet-masters-check.mjs` all **PASS**.
- Phone layout untouched: `git diff main -- "03 Prototype/src/mobile" "03 Prototype/src/app/mobile"` and every `*Mobile.tsx` / `styles/mobile*.css` / `styles/m-*.css`: **empty**.

## Before / after — document height in screens at tab-land (1138 × 640), worst role

Before = `fit-check-baseline-20261101-0005.json` (541bbff7, no FIT code). After = `fit-check-20261101-2326.json` (worst over the page and all its sub-screens). 1 = fits.

| Page | Before | After | | Page | Before | After |
|---|---|---|---|---|---|---|
| home, 10 category pages | 1 | 1 | | groups | 6.7 | 1 |
| overview | 3.2 | 1 | | channels | 2.9 | 1 |
| rooms | 2.4 | 1 | | booking-site | 13.8 | 1 |
| calendar | 2.7 | 1 | | inventory | 3.5 | 1 |
| reservations | 12.9 | 1 | | staff | 2.9 | 1 |
| arrivals | 1 | 1 | | my-hr | 3 | 1 |
| folios | 13.4 | 1 | | closing | 12.2 | 1 |
| cashier | 3.2 | 1 | | reports | 2.2 | 1 |
| cashbook | 3.1 | 1 | | accounting | 2.6 | 1 |
| pos | 1.8 | 1 | | assistant | 2.5 | 1 |
| expenses | 26.3 | 1 | | compliance | 3.8 | 1 |
| housekeeping | 13.7 | 1 | | syshealth | 1.2 | 1 |
| maintenance | 2.5 | 1 | | plans-extras | 3.9 | 1 |
| handover | 3.3 | 1 | | legacy-import | 1.4 | 1 |
| inhouse | 1.6 | 1 | | prefs | 3.4 | 1 |
| guest-services | 1.4 | 1 | | access | 2.1 | 1 |
| gsv-my-requests | 1 | 1 | | settings | 1.6 | 1 |
| messages | 163.7 | 1 | | approvals | 2.6 | 1 |
| documents | 3.8 | 1 | | backup | 1.9 | 1 |
| customers | 2 | 1 | | roadmap | 1.7 | 1 |
| yield | 1.4 | 1 | | | | |

(Before-run "1" rows on home / category / arrivals still failed then for other reasons: not converted, hidden actions.)

## What the integrator changed

**Shell (fit-shell's files, layout only)**
- *Class collisions:* `FitPage layout="split|board|steps|form"` keeps `fit-<layout>` on the page root (tests unchanged), and every component grid rule in `z-fit.css` (`.fit-split`, `.fit-board`, `.fit-steps`, `.fit-form`, incl. their < 900 px rules) is scoped with `:where(:not(.fit-page))` — no added specificity, so lane rules keep winning as before. Verified in the browser (probe of the page root + body child, tab-land and tab-port): every page root is a flex column; Customers / In house / Folios / Settings `SplitView`, Closing `StepFlow`, Prefs `FormSections` and Housekeeping / POS `BoardColumns` lay out as grids inside the body. Lane workarounds removed: `closing-fit.css` (fit-steps override), `guest-fit.css` (fit-split override), `fit-admin-b.css` (fit-form override). Lanes that chose `layout="fill"` as a workaround (folios, groups, reports, settings, approvals) keep it — their `SplitView` lives in the body, so the result is identical; not changed.
- *HeaderActions:* overflow is measured on the item boxes (a right-aligned row spills to the left, which `scrollWidth` never counted), so trailing actions really move into **More**. The check tool opens More (and a closed record pane via `prep`) before looking for a modal opener.
- *StatStrip (review B1 + S1):* `items[].value` accepts a node; a value is a plain count (`.stat>strong`, 35 / 30 px) unless the item or the strip says `money` (`.money-stat`); `variant="small"` renders the old `.small-stats` tiles (span + strong, 25 / 23 px, one sideways row) with the old extra class (`hov-stats`, `msg-stats`, `yld-stats`, `mnt-stats`). Call sites: Dashboard (counts plain, `%` back in its 20 px span, Payment receipts money), Maintenance report (MTTR + Parts cost money, PM % span), Guest services / Groups / Accounting / POS day summary / Booking site = money (as their old `Stat` markup), Expenses / Housekeeping (both views) / Inventory / Maintenance work orders / Handover dashboard / Messages / OTA channels / Tariff & yield = small.
- *FormSections:* the save bar is `.fit-form-actions` (own flex row) instead of `.modal-actions`, whose ≤ 760 px rule shrank the Prefs "Reset this device" button to 10 px at tab-port (main 16 px).
- *FitNote* gains an optional `className` (used to keep a moved note's old size); *`useWideSplit(fallback)`* exported for list/record pages (review N4: Groups now follows rotation; the other three local copies — front-b, guest, admin-a — are left as they are).
- *DEC-195 restorations* (one section of `z-fit.css`, values copied from the main rule): OTA channels and Tariff & yield tab labels 12 px and the Compliance view tabs 13 px at fine pointer (touch stays 16 px); channels "Simulated" note 12 px, rate-calendar legend 11 px; Prefs reset note 11 px; Dashboard welcome line / New reservation button sizes of `.welcome p` / `.welcome .btn` per breakpoint. Compliance: the `padded` class was removed from `.cmp-fit-body` (its `.padded p` 11 px shrank 12 / 13 px compliance text).
- front-b's two inline styles (review N8) moved into `z-fit.css` (scoped to `guests` / `inhouse` / `reservations`); the Reservations toolbar stays one row ≥ 900 px (status buttons scroll sideways).

**Lanes' files (review items)**
- *S2 Messages:* `<MsgSimulatedNote/>` ("Simulated" demo / "Messaging is off" production) sits at the top of the page body on every tab, for every role (the Tech admin's Triggers / Templates included); removed from the Outbox footer.
- *S3 palette:* `subscreens.ts` exports `SUB_SCREEN_GATES` (+ `subScreenOpen`) with each page's own tab condition — HrDesk tabs (incl. DEC-058 payroll / advances / F&F / statutory rates and the payroll module switch), Accounting Tally export, Expenses vendor payments, OTA commission / payouts, POS day summary, Messages outbox / consent (Tech admin), Handover dashboard / templates, Yield publish requests, Access sign-in & devices (production). `SearchPalette` drops a sub-screen the role's page would not show.
- Groups list buttons renamed `grp-list-row` (fit-revenue's `.app-shell .grp-row` also restyled the rooming-list rows `.grp-row` of the group-block dialog); `grp-list-row` and Reports' `.f-report-item` use `var(--c-ffffff)` instead of `transparent` (the dark-theme unresolved-pair budget of `stream-themes` was 51 / 50 after the merge).
- admin-a's edits outside its ownership accepted: `tools/tablet-masters-check.mjs` (Back to list below 900 px; passes) and `src/styles/z-tablet-masters.css` (three layout-only rules).

**Tests and check scripts**
- `tests/stream-fit.mjs`: harness wraps pages in `FitNavContext` (desktop, page id), `FIT_BASE` = `3dac5afb`, `FIT_ENFORCE_ALL` on by default.
- Shell DONE's three fixes: `tests/print-fixture.tsx` `uid:'owner'`; `tests/check-tablet-back.mjs` 48 px for primary / page-title buttons, 44 px dense controls (DEC-060), drawer nav measured with the Wave C foot; `tests/check-overlay-compat.mjs` closes phone sheets with `.modal-close, .m-sheet-head .m-icon`.
- Tests updated to the merged markup (none deleted or weakened): `stream-fit-front-b` (folios has cash-a's sub-screens), `stream-fit-guest` / `stream-fit-ops` / `stream-fit-revenue` (small StatStrip markup), `stream-fit-shell` (scoped grid selectors + an assertion that all four are scoped; StatStrip money / small variants; z-fit sizes: h1 / inherit / kbd outside the restoration section, the restoration sizes listed exactly; FormSections bar class).
- `tools/fit-modals.json`: every lane's modal openers (36 entries, settings `Change…` with `prep:.cfg-item` for the < 900 px list).
- `tools/fit-screen-check.mjs`: `guests` visited as an extra page; fonts measured on **every visible element inside `.content`** (the Demo pill popover and top bar are outside, so the `label` / `select` false positives are gone); new kinds `.stat>strong` (plain count, review B1), `.small-stats strong`, `.small-stats span`; the baseline `tools/fit-baseline/fonts-<viewport>.json` was **re-recorded on main 649e4982** (an archive of main's `03 Prototype` built in `.scratch\mainsrc`, roles owner / manager / reception / housekeeper / maint / accountant, every local tab and every Master configuration group clicked) per page, with each text's size; a size a page never had on main passes only when every element of it shows a text that main showed at that same size (same text at another size = **font-changed**, reported with the old size). `tools/fit-baseline/allow.json` lists the remaining new elements with their reason (new FitTabs bars at the standard tab size, booking-site's new StatStrip, panel-head counts). The 10 px `td.muted` of Opening balances is on main's accounting page and needs no allowance. The legacy fallback branch was not removed from the shell (no page uses it any more; it is harmless and the shell tests cover it).

## Review items

| Item | Resolution |
|---|---|
| B1 StatStrip counts as money-stat | Fixed in the shell (`money` per item / strip, value node) + Dashboard and Maintenance report call sites; check kind `.stat>strong` added; check PASS |
| S1 small-stats → StatStrip font change | `StatStrip variant="small"` = old markup and fonts; all seven call sites switched; check PASS |
| S2 Messages note dropped on 3 tabs | Note on every tab, every role (page body top) |
| S3 palette lists tabs a role cannot open | `SUB_SCREEN_GATES` + palette filter |
| S4 subscreens merge / outside ownership | Union kept, `FIT-ADMIN-A` / `FIT-ADMIN-B` markers; admin-a's two files accepted; `stream-wd5-gaps.mjs` (guest + revenue) merged cleanly and passes |
| N1 Dashboard Front desk list | Confirmed layout-only (review); kept |
| N2 Day closing copy / gating | Accepted (review) |
| N3 Yield publish requests tab, Booking site tabs | Accepted (review); contract correction below |
| N4 900 px media hook copies | `useWideSplit` in the shell; Groups uses it (rotation fixed); other three copies left (follow-up) |
| N5 production ServerDevicePanel moved to "Data and system" | Kept as admin-a built it — owner to confirm |
| N6 duplicated desktop render code | Follow-up (list below) |
| N8 inline styles | Moved to `z-fit.css` |
| Lane shell defects (class collision, HeaderActions, harness, FIT_BASE, label baseline, guests page, modal openers) | Fixed (above) |
| Expenses register tight spot (~168 px at tab-land) | ≥ 160 px in the final check (no `cramped`) |

## Deviations from the contract (§6) — to correct in the contract

- **Expenses:** sub-screens are the seven SCC sections (Register, Recurring, Petty cash, Budgets, Udhaar, Vendor payments, Month & tax); the status filter inside the Register keeps its ids **All / Draft / Pending / Posted / Rejected / Reversed / Void** ("approved / paid" never existed).
- **Folios:** tabs Folios / Folio requests (the contract listed none); the record pane is the same folio as the booking dialog.
- **Compliance:** tabs Calendar / Dashboard (main has no Filings / Licences screens).
- **Booking site:** tabs Bookings / Preview (main has no Settings / Rooms & rates screens on this page).
- **Approvals:** a third tab "Pending expenses". **Yield:** "Publish requests" tab only while a request exists. **Groups:** the DEC-076 credit note on Companies only, "Rooms held" on Blocks only.
- Several split pages use `layout="fill"` with a `SplitView` in the body (folios, groups, reports, settings, approvals) — same result after the class fix.
- `guests` (front-desk guest list) has **no nav item and App maps it to Modules Home on main and on FIT** (not reachable in the app); its fit-page root is covered by the static tests only, the browser check cannot open it. Owner / coordinator: keep, link it, or drop it in a later wave.

## Not fixed / follow-ups

- Duplicated desktop render copies to keep in step (review N6): cash-a `CashBook` desktop day card / range view; ops `MntReportsFit` vs `MntReportsTab`, `HovTemplatesFit` vs `HovTemplatesBody`, `HovDashboardBody fit`; people-a `ReopenForm` / `ReopenedDay` desktop branches; guest `CustomerPanelFit` vs `CustomerPanelPhone`.
- Three local copies of the 900 px hook (front-b `useWideSplit.ts`, guest `useWide`, admin-a `useNarrow900`) could use the shell's `useWideSplit`.
- admin-a open item: a master with very many rows scrolls the whole record pane.
- Day pack (Day closing, after a closed day) is not reachable in the demo seed, so it is not in the modal sample (shell verified it by render).
- Lanes' leftover static servers (ports 4191, 4195, 4197) are still running (not started by the integrator).
- Lane worktrees are left in place (coordinator).

## Screenshots for the owner

`E:\Projects\HCC\worktrees\fit\05 Delivery Evidence\Fit\screens-final\<page>\` — 52 page folders, every page and sub-screen at **tab-land** and **lap-1366** for Owner and Housekeeper (from the final check), plus **tab-port** Owner shots of the key pages (reservations, guests, closing, housekeeping, prefs, accounting, folios, settings, customers, inhouse, handover, overview, rooms, expenses, pos, messages). 30 MB, untracked (the check keeps screenshots out of git). The raw final-run folder is `05 Delivery Evidence\Fit\screens-20261101-2326\`.

## HANDOFF bullet

W2a FIT integrated on `claude/fit` (2 Nov 2026, integrator Opus): eleven lanes merged; shell fixes (component grids scoped away from the page root, HeaderActions More menu, StatStrip plain / money / small variants, FormSections bar), review B1 / S1–S4 resolved, palette hides tabs a role cannot open, check tool compares fonts element by element with main (baseline re-recorded on main 649e4982). Full fit check PASS 0 / 5,512 (every page 1.0 screen at tab-land, was up to 163.7); prototype 2339 / 2339, tsc + both builds clean, print / tablet-back / overlay / tablet-masters checks PASS; phone paths unchanged. Next: coordinator gate (`tools\Start-Gate.ps1 -Wave fit`), owner look at `05 Delivery Evidence\Fit\screens-final\`.
