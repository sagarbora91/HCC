# FIT-fit-admin-b DONE

Lane **fit-admin-b** (phase B) · model Sonnet · branch `claude/fit-admin-b` · worktree `E:\Projects\HCC\worktrees\fit-admin-b` · base `3dac5afb` (claude/fit head). Nothing pushed.

Pages: `syshealth`, `prefs` (Appearance & display), `access`, `backup`, `roadmap`, `legacy-import`.

## Commits

See `git log --oneline 3dac5afb..HEAD` (first: `6f8969dc` conversion + tests; later: layout fixes after the first check, DONE file).

## Files

Prototype (all inside the lane's ownership):
- `src/features/admin/{SystemHealth,AccessControl,LegacyImport}.tsx` (`legacyImportModel.ts` untouched)
- `src/features/housekeeping/{Backup,Roadmap}.tsx` (`BackupStatus.tsx` untouched: it is the Overview card, not a page)
- `src/features/prefs/PrefsPage.tsx` (`parts.tsx`, `MenuPrefs.tsx`, `register.tsx`, mobile page untouched)
- `src/app/subscreens.ts` (the `// FIT-ADMIN` block only, append-only; 13 lines)
- **new** `src/styles/fit-admin-b.css` (see "Why a new stylesheet")
Tests: `tests/stream-fit-admin-b.mjs` (new, 7 `[FIT-ADMIN-B]` tests); updated `tests/stream-prefs.mjs`, `tests/stream-themes.mjs` (listed below).

Phone: nothing under `src/mobile`, `src/app/mobile`, no `*Mobile.tsx`. Exports the phone pages import (`sysWhen`, `sysRetryCommand`, `SYS_RETRY_DONE`, `legacyWhen`, `useLegacyOwner`, `MOTION_OPTIONS`, `DENSITY_OPTIONS`, `SAMPLE_LINE`, `TemplatesTab`, `LegacyResultView`, `RunsTable`, `MatchPill`) keep their names and signatures.

### Why a new stylesheet
`tests/stream-css-tokens-c.mjs` ([CT-C]) freezes sixteen stylesheets against commit 799e4cc, line for line, including `admin.css`, `housekeeping.css` and `prefs.css`: appending even one rule to them fails three [CT-C] tests. So every desktop rule of this lane lives in `fit-admin-b.css` (every selector starts with `.app-shell`, no font-size, no colour; a test enforces it). **Other lanes will hit the same wall if they append to `admin.css`, `closing.css`, `folio.css`, `guests.css`, `history.css`, `housekeeping.css`, `reservations.css`, `prefs.css`, `restriction.css` or the `m-*` sheets listed there: they need their own new sheet.**

## What was built (per page)

| Page | Pattern | Sub-screens (SUB_SCREENS, `// FIT-ADMIN`) | Default tab / filter (old → new) |
|---|---|---|---|
| `syshealth` | fill: `FitPage` + `fit-cols`: Parked events `ScrollPanel` (left, Retry per row), Backups `ScrollPanel` (right); descriptions in the panel footers as `FitNote`; the Demo / Checked pill in the header | none | — |
| `prefs` | form: `FitPage layout="form"` + `FormSections`; one card at a time; the old **Reset** card became the sticky action bar (same sentence + `DEVICE_ONLY` line + same button and handler, visible in every section) | `text`, `appearance`, `display`, `navigation`, `alerts` | all sections stacked → `text` first (no list filter involved) |
| `access` | tabs: `FitTabs` replaces the local `Tabs`; Users = one `ScrollPanel` (New user in the panel header); Role templates = two panels side by side (Permission matrix scrolls both ways with its sticky heads; Edit role template + Module access on the right); Effective access = filters row + `ScrollPanel`; Exceptions = table panel + Add exception form panel (submit button in the panel footer, always visible); Policy history = Change requests + Policy versions panels; Sign-in & devices (production build only) = `ScrollPanel` around `CredentialsPanel`. The two banners (pending policy requests, approval notice) sit in the toolbar row. | `users`, `role-templates`, `effective-access`, `exceptions`, `policy-history`, `sign-in-devices` | local state `'Users'` → `useFitTab('access',…,'users')` (same first tab) |
| `backup` | tabs: the simulation banner and the three facts stay in the toolbar; `FitTabs` Backup history / Restore procedure, each one `ScrollPanel` | `history`, `restore` | both cards stacked → `history` |
| `roadmap` | fill: one `ScrollPanel` (sticky table heads), the info note in the footer as `FitNote` (2 lines) | none | — |
| `legacy-import` | form + result: left `ScrollPanel` "Run" (folder, parts, totals file, dry run; **Run button in the panel footer**, always visible), right column = result `ScrollPanel` (when a result exists) above the Runs `ScrollPanel` | none | — |

Content: nothing dropped. Section headings and descriptions that sat in `.section-title` moved into the `ScrollPanel` title and (descriptions) into the panel footer `FitNote`, text unchanged. `GuardedButton` reasons, lock notes (`System health is for the Owner and the Tech admin.`, `Legacy import is for the Owner.`), `adm-lock` notes and every permission / command are untouched. No selector, stream, command, permission, fixture or type change.

Prefs at the largest text size: the page was checked at text scale 1.3 on `tab-land` and `lap-1366` (checks 1, 2, 5): the section list on the left and one section scrolling on the right never push the document; the sticky bar (note + Reset) stays in view.

## Tests

All through the lock (label `fit-admin-b`), output in `<worktree>\.scratch\`.

- `npx tsc --noEmit`: clean.
- `npm test`: **2240 / 2248** (claude/fit base 2194 + the 7 new + fit-check's tests merged in; the 8 failures are the `[FIT]` harness items above, none from this lane: 1 phone-file list from the ACC-3 merge + 7 `data-fit-page` pages including `arrivals`).
- Updated tests (assertions kept, only made to open the section that now holds the text; none deleted or loosened):
  - `tests/stream-prefs.mjs` `[PREFS] desktop page renders every card in order…`: the desktop page now shows one section at a time, so the test renders each of the five sections through the remembered tab (`sessionStorage hcc-fit-tab:prefs`, a stub in the test's `renderPrefs(…, section)`), asserts every card heading, `Reset this device` and the sticky bar in each section, and runs the same joined-markup assertions (theme radiogroup, no APK-only toggle, start-page options, housekeeper list without money pages) on the five sections together.
  - `tests/stream-themes.mjs` `[THEME] Appearance & display: the Theme row…`: the desktop render opens the `appearance` section (same assertions).
- New: `tests/stream-fit-admin-b.mjs` (`[FIT-ADMIN-B]`): one `fit-page` root per page, lock notes inside a `fit-page`, `SUB_SCREENS` ↔ `useFitTab` ids and defaults, every access tab / prefs section / backup tab renders its panels, legacy-import form and runs, the CSS guard (every selector `.app-shell`, no font-size, no colour).
- **Not green, not mine:** `[FIT] page <id> renders a fit-page root` fails for every converted page, `arrivals` (fit-shell's reference page) included: `tests/stream-fit.mjs` renders pages without a `FitNavContext`, so `FitPage` has no `data-fit-page` (`nav.page||undefined`) and its `data-fit-page="<id>"` assertion fails. And `[FIT] no phone-layout file changed since the wave base` lists `accounting/Mobile.tsx`, `mobile/frontdesk/Arrivals.tsx`, `m-accounting2.css`: they arrive with the ACC-3 merge into `claude/fit`, not from this lane (`git diff 3dac5afb..HEAD` shows no phone file). See "Integration needed".

## Check (`node tools/fit-screen-check.mjs --pages syshealth,prefs,access,backup,roadmap,legacy-import`)

Through the lock, port 4201, all five viewports, all nine roles, text scale 1 and 1.3 (tab-land, lap-1366), every sub-screen. Reports: `05 Delivery Evidence\Fit\fit-admin-b-check\` (first run) and `fit-admin-b-check2\` (after the fixes below); screenshots stay out of git.

**Result of the final run** (`fit-admin-b-check2it-check-20261101-1838.md`): 696 checks, **no** `page-scroll`, `content-scroll`, `not-converted`, `cramped`, `hidden-action`, `touch-size` or `h1` finding at any viewport, any role, either text scale (tab-land and tab-port are fully clean, including text 1.3 on tab-land and lap-1366). The 42 remaining findings are all `font-changed` on the three fine-pointer viewports (lap-1366, lap-1280, desk-1920): `prefs › navigation` (27: select 13 px, label 14 px), `access › role-templates` (6: input 10 px), `access › exceptions` (6: select 12 px), `legacy-import` (3: label 14 px). They are the baseline's first-of-kind artifact (Integration needed 3): this lane adds no font-size, and those controls have the same computed size on `main`; the check now measures them because the Demo toolbar's 11 px select / label / input are no longer rendered in the bar.

First run (`fit-admin-b-check\`, 42 findings, same kinds) led to these layout fixes, all in the final run: the Prefs page header was squeezed by the `.fit-form` class collision; the Retry button of System health was cut off (wider parked-events column); Legacy import's Run button and Exceptions' Add button were below the panel scroll (moved to the panel footers); the Parts checkboxes stacked above their text.

## Modal openers (for `tools/fit-modals.json`)

```json
{"page":"access","opener":"New user","lane":"fit-admin-b","roles":["owner"]},
{"page":"access","opener":"Edit","lane":"fit-admin-b","roles":["owner"]}
```
(`New user` is in the Users panel header, `Edit` is the first row's action; both open the `admin-user` modal that fit-admin-a owns. The other five pages open no modal.)

## Open items

- `sign-in-devices` is registered in `SUB_SCREENS` (the registry test needs every `useFitTab` id listed) but the tab exists only in the production build; in the demo build the search palette entry "Access control › Sign-in & devices" opens the Users tab.
- The footnotes / descriptions that moved into `FitNote` use the `.footnote` size of that component (the shell's choice, contract §11.4); the Prefs reset note is a plain 13 px paragraph as before.
- First-of-kind font artifact of the check (see above): not a real change.

## Integration needed

1. **fit-shell / `FitPage layout="form"`:** `FitPage` adds the class `fit-form` to its root and `FormSections` uses `.fit-form` for its own grid, so a page with `layout="form"` gets the grid on the page root (header squeezed into the 240 px column; seen on `prefs`). This lane overrides it with `.app-shell .fit-page.fit-form{display:flex;flex-direction:column;grid-template-columns:none;gap:12px}` in `src/styles/fit-admin-b.css`. Proper fix in `z-fit.css`: scope the grid to `.fit-page>.fit-form` (or give the page layout a different class). Other `layout="form"` pages (settings in fit-admin-a) need the same.
2. **fit-check / `tests/stream-fit.mjs`:** the page harness must wrap the page in `FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:0,setTabCrumb(){}}}` so `data-fit-page` is set; today every converted page fails `[FIT] page … renders a fit-page root`.
3. **fit-screen-check baseline:** the measured "first `select` / `label` / `input`" moved from the Demo toolbar (11 px) to the page's own control now that the Demo toolbar is a popover. The reported `font-changed select 13px (was 11px)`, `label 14px`, `input 10px`, `select 12px` on `prefs › navigation`, `legacy-import`, `access › role-templates`, `access › exceptions` at the three fine-pointer viewports are those controls' unchanged sizes (this lane adds no `font-size`); the baseline should record those kinds per page, or measure inside `.content`.
4. `tools/fit-modals.json`: append the two openers above.
5. CT-C freeze (above): tell every lane that appending to a frozen sheet fails three tests.
6. `admin/modals.tsx` / `admin/parts.tsx` (admin-a's): no edit needed from this lane.

## HANDOFF bullet

FIT fit-admin-b: System health, Appearance & display (FormSections, 5 sections, Reset in the sticky bar), Access control (6 page tabs, split panels), Backup (2 tabs), Roadmap and Legacy import converted to the fit components; layout only, no font or logic change; desktop CSS in the new `src/styles/fit-admin-b.css` because `admin/housekeeping/prefs.css` are frozen by the [CT-C] tests.
