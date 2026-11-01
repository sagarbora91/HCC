# FIT-fit-shell DONE

Lane **fit-shell** (phase A) · model Opus · branch `claude/fit-shell` · worktree `E:\Projects\HCC\worktrees\fit-shell` · base `541bbff7` (claude/fit setup head), resumed at `7d4c3803` after the PC restart, `claude/fit` merged in (ACC-3 + review + contract §11 + phase-B lane split). Nothing pushed.

## Commits

```
1e491b3e FIT shell: z-fit.css, fit components, sub-screen registry, 56 px top bar + Demo pill, fit content, modal sticky actions, Home/category/sign-in fit, arrivals reference page
7d4c3803 FIT shell: tests stream-fit-shell.mjs, component guide, LF line endings restored, vh fallback before dvh
2efaf76e FIT shell: position:relative on fit containers (B2 start) + lane check evidence
1f754403 Merge claude/fit (ACC-3, review, contract §11) into claude/fit-shell
ff81fc80 FIT shell review fixes: .fit-modal scope + stand-alone dialogs on ModalFrame (B1), .app-shell containing block (B2), ... tests
e9138604 Merge claude/fit (phase B lane split) into claude/fit-shell
(this file + guide + evidence: the final commit)
```

## Files

Prototype (all in the fit-shell ownership of contract §7 / §11):
- `03 Prototype/src/styles/z-fit.css` (created), `03 Prototype/build.mjs` + `03 Prototype/tools/cssTextScale.mjs` (bundle order, §11.7)
- `03 Prototype/src/components/fit.tsx` (created: FitPage, ScrollPanel, SplitView, FitTabs, useFitTab, StatStrip, BoardColumns, StepFlow, FormSections, ModalFrame, **FitNote**; header **More** menu)
- `03 Prototype/src/app/App.tsx` (desktop branch: FitTopbar, FitContent, FitNavContext, footer removed), `ModalHost.tsx` (`.fit-modal`, About this build), `ModulesHome.tsx` (Home / category / sign-in / breadcrumb tab crumb), `SearchPalette.tsx` (sub-screens, desktop only), `demo/DemoToolbar.tsx` (Demo pill), `subscreens.ts` (created), `components/ui.tsx` (PageTitle only)
- Stand-alone dialogs converted to `ModalFrame` per §11.1: `src/features/exports/DayPack.tsx`, `src/features/exports/ServerDayPack.tsx`, `src/clients/sync/guestid-ui.tsx`
- Reference page: `src/features/guests/CheckIn.tsx` (`ArrivalsPage` only; CRLF kept: `git diff --numstat claude/fit -- CheckIn.tsx` = 18 / 11)

Tests: `03 Prototype/tests/stream-fit-shell.mjs`, `03 Prototype/tests/fit-shell-entry.tsx`.
Docs: `02 Planning/agents/FIT_Component_Guide.md`.
Evidence: `05 Delivery Evidence/Fit/fit-shell-check/` (check reports, Day pack check). Screenshot folder `fit-shell-check/screens-20261101-1312/` is left untracked (the check tool keeps screenshots out of git).

Phone paths: `git diff claude/fit..HEAD -- "03 Prototype/src/mobile" "03 Prototype/src/app/mobile"` and every `*Mobile.tsx`: **empty**.

## What was built

Shell (§3), components (§4), spacing (§5), registry (§4), reference page `arrivals` — as in the first two commits (see the review's deliverable checklist). The resume work fixed every item of `FIT-shell-review-20261101.md` and contract §11:

| Item | Fix |
|---|---|
| **B1** modal rules leaked into Day pack / server Day pack / guest-ID dialog and the phone | Every desktop dialog rule now targets `.modal.fit-modal` / `.fit-modal-overlay` (no `.modal:not(.m-sheet)`, no bare `.modal`). `ModalHost` adds `fit-modal` on the desktop layout only (phone sheets never). The three stand-alone dialogs render through `ModalFrame dialog={…}`: on the desktop shell they get `.modal fit-modal` + scrolling `.modal-body` + sticky actions; without the desktop shell (phone) the markup is byte-identical to before. Print keeps the styles.css `.modal{padding:15px}`. Verified: Day pack rendered at lap-1366 / tab-land (framed: body scrolls 2214/625, actions sticky and in view, document padding 30 px kept) and at 390 px phone (computed style identical with and without `z-fit.css`; markup has no `fit-` class) — `fit-shell-check/daypack/daypack-check.json` + 6 PNGs. The live app shows the Day pack only after a day is closed, so it was rendered from the FY26 scenario's last closed date in the real stylesheet bundle. |
| **B2** Assistant scrolled the document | `.app-shell{position:relative}` (shell is the containing block, its `overflow:hidden` clips absolute descendants) + the earlier `position:relative` on content / panels. Check: `assistant` no `page-scroll` at any viewport (only `not-converted`, expected). |
| S1 `fit-cols` collapsed below 900 px | Stacked columns are grid rows `minmax(160px,1fr)`; a column with two panels splits its row; panels min 160 px. Verified at tab-port with a Booking ID lookup open: table panel 384 px, lookup 374 px, document 1066/1066. |
| S2 palette sub-screens on the phone | `paletteEntries(isMobile)`: sub-screens only on the desktop layout. |
| S3 legacy wrapper remounted pages | No wrapper: `FitContent` sets `data-fit-scroll="legacy"` on the page's own root (MutationObserver), CSS `.app-shell .content>:only-child:not(.fit-page)`. A page that still returns a **fragment** has no single root: `.content` itself is marked legacy and scrolls (`.content:has(>:nth-child(2))`) — such pages report `not-converted` and also `content-scroll` until converted. |
| S4 footnote title-only ellipsis | New `FitNote({text,lines?})`: one line + More / Less in place (More only when cut); prints in full. Arrivals uses it. |
| S5 phone Back / history tab | `go()` writes the history `tab` on desktop only; `onPop` does `setTabKey(display.isMobile?'':(st.tab??''))`. |
| S6 CRLF | `CheckIn.tsx` rebuilt from `claude/fit`'s CRLF file + the arrivals hunk; test asserts CRLF. |
| S7 DEC numbers | DEC-194 / 195 / 196 (APR-025) everywhere in code, tests and guide. |
| M1 | `ModalFrame` inside ModalHost: plain wrappers, no second scroll box / tab stop. |
| M2 | Not changed: only `.modal-heading` is sticky; modals that open with a bare `<h2>` scroll it away (acceptable; add `.modal-body>h2:first-child` if the owner notices). |
| M3 | `ScrollPanel`: the card is `aria-labelledby` its title; only the scroll body has `aria-label`. |
| M4 | `SplitView`: no `aria-live`; focus moves to the record pane when a record opens. |
| M5 / §11.5 | Only the page's own bar (FitPage `tabs` slot) sets the breadcrumb crumb; a `FitTabs` inside the page body is inner and never does. |
| M6 | A tab opened by `go(page,{tab})` is written to `sessionStorage`. |
| M7 | `FitTopbar`: one ResizeObserver for the bar's life; a cheap re-measure on render. |
| M8 | Print resets `.fit-board`, `.fit-stats`, `.fit-step-rail`, `.fit-form-nav`, `FitNote` overflow. |
| M9 | z-fit.css header comment names the copied 12 px kbd. |
| M10 | `.fit-stat.ok` border. |
| M11 / §11.8 | The 56 px bar shows the user button on touch tablets (`display:inline-flex!important` inside `.app-shell .topbar.fit-topbar` only); About this build + Sign out stay in the user menu. Verified tab-land / tab-port: user button 48 px, in view, bar 56 px. |
| M12 | Evidence committed. |
| §11.9 header actions | One row, never wraps; the first action stays, trailing ones move into a **More** menu (`HeaderActions` in `FitPage`, re-measured when the header width changes). Arrivals: Walk-in (primary) first, Print second. |
| §11.10 | Arrow keys move + select, Home / End — as built. |

Decisions taken in the lane: arrow-key tabs activate on focus (automatic); the bar's separate Sign-out button is gone (Sign out is in the user menu); legacy fallback = attribute on the root (fragment pages: on `.content`); stand-alone dialogs pass `heading` / `actions` as complete elements so their phone markup stays identical.

### Page table

| Page | Pattern | Sub-screens | Default tab / filter | Check (home, arrivals, assistant; owner + reception + housekeeper) |
|---|---|---|---|---|
| `home`, `cat:*` | fit grid (`FitGrid`) | — | unchanged | PASS at all five viewports, text 1 and 1.3 |
| sign-in | fit (2 / 3 columns, list scrolls) | — | unchanged | document fits at tab-land, tab-port, lap-1366 |
| `arrivals` | fill (`FitPage` + toolbar + `ScrollPanel`, side column `fit-cols`) | — | unchanged | tab-land, tab-port PASS; fine-pointer viewports: only the `label` font artifact below |
| `assistant` (not converted; fit-people-b's) | legacy | — | — | only `not-converted` — no page-scroll (B2 fixed) |

## Tests

All through `tools\Invoke-Serialized.ps1` (label `fit-shell`), output in `E:\Projects\HCC\worktrees\fit-shell\.scratch\`:

| Command | Result |
|---|---|
| `npx tsc --noEmit` | clean |
| `npm test` | **2194 / 2194** (main 2176 + 18 `[FIT-SHELL]`; fit-check's `[FIT]` tests come with its merge) |
| `npm run build`, `npm run build:production` | clean, 0 warnings |
| `node tests/check-print-layout.mjs` | **FAIL, pre-existing** (not FIT): `tests/print-fixture.tsx` gives the report modal no `uid`, so since ACC-3's `rbacCanExportReports(s,uid)` the print modal renders "not available for your role" and 0 rows. With `uid:'owner'` added to a scratch copy of the fixture the check passes (rows 1 / 62, white page, no buttons, width 794 ≤ 794) on this branch. |
| `node tests/check-tablet-back.mjs` | **FAIL, pre-existing**: landscape drawer nav 301 px < 55 % of 601 (the drawer foot has held "Change my password" since Wave C), and on Reservations 212 of 213 controls are 44 px (DEC-060 dense rows are 44; the check wants 48). Identical numbers on the base build (`fit-check` worktree `dist`, 541bbff7). With those two assertions logged instead of asserted, everything else passes on this branch: top bar ≤ 150 px with 48 px buttons, drawer opens / Back closes it, Back unwinds history, exit prompt, exhausted-history fallback, new navigation cancels the fallback. |
| `node tests/check-overlay-compat.mjs` | **FAIL, pre-existing**: on the 390 px phone the demo sheet is an `m-sheet` whose close button is `.m-sheet-head .m-icon`, not `.modal-close` (same on the base build). With that selector widened in a scratch copy it passes: drawer, demo sheet, search, booking modal and exit prompt overlays all cover the viewport exactly, phone and tablet (673 × 1006) — so the desktop `.fit-modal-overlay` padding does not change the overlay box. |
| `node <fit-check>/tools/fit-screen-check.mjs --pages home,arrivals,assistant --roles owner,reception,housekeeper` (server `PORT=4190`) | report `05 Delivery Evidence/Fit/fit-shell-check/fit-check-20261101-1312.md`: 99 checks, 13 failing = `not-converted` (assistant, expected) and `font-changed label 14px (was 11px)` at lap-1366 / lap-1280 / desk-1920 (artifact, see Integration needed). 0 page-scroll, 0 content-scroll, 0 cramped, 0 touch-size, 0 hidden-action; modal sample (New reservation, Add expense, New user, user menu) PASS at all five viewports; text 1.3 PASS except `not-converted`. |

Tests updated (not loosened): ScrollPanel markup (labelledby), ModalFrame (plain wrappers), desktop-dialog CSS selectors (`.modal.fit-modal`), legacy fallback (attribute, no wrapper), palette (desktop-only entries), arrivals (FitNote, primary first), useFitTab (asked tab remembered). New: B1 stand-alone dialogs (phone markup unchanged, desktop framed, real `DayPackSheet` both ways), S2 palette on the phone, S5 phone `onPop`, FitNote / More menu / user button on touch / M8 / M10.

## Open items

- `assistant` and every other page except Home, category pages, sign-in and `arrivals` are `not-converted` until phase B.
- Fragment pages (several root elements) scroll `.content` until converted (`content-scroll` + `not-converted`).
- M2 (bare `<h2>` modal headings scroll away) left as the review allows.
- The Day pack could not be opened in the live demo (it appears only after the day is closed); verified by rendering the real component in the real stylesheet bundle at both layouts.

## Integration needed

1. **fit-check tool (`tools/fit-screen-check.mjs`, owner fit-check)** — compatibility: `sessionStorage` key `hcc-fit-tab:<page>` matches `useFitTab` (`fitTabKey`), the `pushState` + `popstate` navigation reaches `onPop` (desktop re-reads the tab, `tab` absent → the remembered one), root detection `.content.firstElementChild.fit-page` and `[data-fit-scroll="legacy"]` both work with the new legacy mark. **One mismatch:** font kind `label` — on `main` at fine-pointer viewports the first visible `label` is the demo toolbar's `.demo-field` (11 px); the toolbar now sits in the Demo pill popover, so the first visible label is the page's own (`label.search` / `.ai-sr`, 14 px, also 14 px on base). Fix: measure `label` inside `.content` (or exclude `.demo-toolbar, .fit-demo-pop`) and re-record that one kind, or whitelist it. No font changed.
2. **`03 Prototype/tests/print-fixture.tsx`** (not FIT-owned; pre-existing ACC-3 break): `const context={s,uid:'owner',permission:()=>true,closeModal:()=>{},display:{isMobile:false}} as any;`.
3. **`03 Prototype/tests/check-tablet-back.mjs`** (pre-existing): accept 44 px dense controls (DEC-060: `need = matches('.btn.primary, .page-title .btn') ? 48 : 44`) and measure the drawer nav after the slide-in with the Wave C foot (e.g. `nav.height > height*.45` or `drawer-nav` + `drawer-foot` ≥ 90 %).
4. **`03 Prototype/tests/check-overlay-compat.mjs`** (pre-existing): close the phone sheet with `.modal-close, .m-sheet-head .m-icon`.
5. `tools/fit-modals.json` — modal openers of this lane: Reservations "+ New reservation", user menu (`.current-user-btn`), the Demo pill popover (`.fit-demo-pill`), Day pack from Day closing once a day is closed (`button` "Day pack (A4 PDF)").
6. Phase-B lanes: read `FIT_Component_Guide.md` again — `FitNote`, header actions order (primary first, More menu), `fit-cols` stacking, `.modal.fit-modal` (never a bare `.modal`), inner `FitTabs` never take the crumb, test harness `FitNavContext` needs `desktop:true`. The registry has the seven brief blocks; twelve-lane names add their own block at the end as the briefs say.

## HANDOFF bullet

FIT phase A lane fit-shell DONE (claude/fit-shell, Opus): shell (56 px bar, Demo pill, fit content, legacy mark on the page root), `components/fit.tsx` (+ FitNote, header More menu), `.fit-modal` desktop dialogs incl. Day pack / server Day pack / guest-ID through ModalFrame (phone unchanged), sub-screen registry + palette (desktop only), Home / category / sign-in / `arrivals` fit; review B1/B2/S1–S7/M1–M12 and contract §11 done. Prototype 2194/2194, tsc + both builds clean; check: home + arrivals fit at five viewports, assistant no page-scroll. Pre-existing check failures (print fixture uid, tablet-back 44 px / drawer foot, overlay phone close selector) listed for the integrator.
