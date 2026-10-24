# HCC parallel build — brief for every stream agent (read fully before coding)

Hotel Control Centre (HCC): internal hotel-operations app for an Indian hotel owner. Interactive **prototype**: React 19 + TypeScript, ONE self-contained HTML (`npm run build` → `dist/index.html`), fictional data, in-browser mock service, no backend. Desktop (Windows) and Android phone from the SAME HTML; a display toggle (Auto/Desktop/Mobile) chooses the desktop shell or the mobile shell. 16 agents work in parallel, each in its own git worktree. Your result is merged with the others afterwards, so **file ownership is strict**.

## Where you work
- Your worktree: `/home/claude/wt/<stream>/` (branch `s/<stream>`). App code: `/home/claude/wt/<stream>/03 Prototype` (note the space — quote paths). Specs: `/home/claude/wt/<stream>/02 Planning/`.
- `node_modules` is a symlink — never run `npm install`/`npm ci`.
- Never touch `/home/claude/hcc` (main checkout) or other worktrees. Never `git stash`. Never push.
- When done: `git add -A && git commit -m "<stream>: <summary>"` in your worktree (commit only your files).

## Read first (in your worktree)
- `02 Planning/HCC_Mobile_Version_Plan_v1.md` (mobile design, screens M01–M24, Android rules, §11 approved decisions — printing stays available on phone, DEC-047).
- `02 Planning/HCC_Prototype_Spec_v1.md`, `HCC_Business_Workflow_Spec_v1.md`, `HCC_RBAC_Matrix_v1.md` — skim the parts relevant to you.
- Code: `src/app/App.tsx`, `src/app/context.tsx` (useHcc: s, uid, user, run, open, go, display, setDirty…), `src/app/registry.ts` (FeatureRegistration incl. `mobile:{pages,modals}`), `src/app/mobile/*` (shell, tabs, shared components ListCard/KpiCard/ChipFilter/StickyFooter/NumberStepper/Stepper/Fab), `src/components/ui.tsx`, `src/services/mock/commands.ts` (execute + Kit), one existing stream as a pattern (e.g. `src/features/closing/*`, `src/services/mock/handlers/closing.ts`, `src/domain/streams/closing.ts`, `src/fixtures/ext/closing.ts`, `tests/stream-f.mjs`), `src/styles/mobile.css`.

## Architecture rules (must follow)
- All mutations go through `run(cmd)` → `execute(state, uid, cmd)`; your command handlers live in YOUR `src/services/mock/handlers/<x>.ts`, return the audit/summary text, use the Kit (`req(perm)`, `fail(code,msg)`, `assert`, `id(prefix)`, `open()` for day-closed checks, `b()`, `financial()`…). Typed `HccError` codes from `src/domain/errors.ts`. Idempotency keys are handled by execute.
- Pure helpers/selectors/types in YOUR `src/domain/streams/<x>.ts`. Your state slice is the pre-declared optional field `s.<key>X` (type `<PREFIX>State` in your domain file — extend that type freely). Absent slice = empty (old saves).
- Handlers may read and (carefully) change core state (bookings, folio lines, rooms, tasks, expenses, approvals) when your module needs it, following existing rules: money = integer paise, synthetic 10% tax via `taxRateFor`/existing line shape (snapshot `taxBps` on lines), simulated clock `s.clock` (never `Date.now()`), business date `s.date`, day-closed rule, approvals cannot be self-approved. **Never break existing behaviour or tests.**
- Permissions: use the existing coarse `Permission` values only (`reservations, checkin, billing, payments, checkout, housekeeping, inspect, expenses, approve, close, reports, admin`). Don't add new permission values. Housekeepers/maintenance must never see money.
- Exported names from your domain file MUST start with your prefix (shared namespace via `services/mock/index.ts`). Command types MUST start with `<PREFIX>_`. CSS classes MUST start with your prefix (`<prefix>-`). Modal type IDs MUST start with `<prefix>-`.
- Fixtures: fictional data only (Indian context, fictional names/companies/vendors), added in YOUR `src/fixtures/ext/<x>.ts` `extendBase()` (runs for every scenario). Keep existing fixture figures unchanged (tests depend on them: occupancy 8, collections ₹21,500, drawer ₹10,200, ₹4,400 S0 journey…). Don't post money into existing bookings in the fixture.
- Integrations (OTA, WhatsApp/SMS/email, OCR, AI, website, POS hardware, bank) are **simulated only**: no network calls, clearly labelled "Simulated" in the UI.
- Keep the build ONE self-contained HTML: no CDN, no external fonts, no new npm packages. Icons: `lucide-react` (already installed, v0.468 — check the icon exists).
- Theme (approved, Maroon & Sand): primary `#7a1f2b` (dark `#5e1620`, tint `#f5e4e6`), background `#faf6f0`, sand tints `#f6efe6`/`#efe4d6`, text `#2b1d16`, muted text `#6b5547`, lines `#e6d9c8`/`#cdb9a3`. Status colours keep their hue AND a text label (ready/ok green text `#2e6b34` on `#eaf4e4`, info blue, warning amber, error `#9a3b2a`-ish). All text must meet WCAG AA (≥4.5:1). Reuse existing classes (`card`, `section-title`, `btn`, `btn primary`, `pill`, `form`, `modal-heading`, `modal-actions`, `summary-list`, `table-wrap`, `info-note`, `stats/stat`, `small-stats`) wherever possible.

## Desktop + mobile (both required for every screen you own)
- Desktop page = component in `registration.pages`; phone page = component in `registration.mobile.pages` (same page ID). Phone modal overrides go in `registration.mobile.modals` (dialogs already render as full-screen sheets on phones — override only when the phone needs a different flow, e.g. a step-by-step wizard).
- Phone rules: touch targets ≥ 48 px, body text ≥ 16 px, labels ≥ 14 px, no hover-only or drag-only features, **tables become cards** (use `ListCard`), one primary action per page (`Fab` or a sticky footer button), correct `inputMode` (`decimal` money, `tel` phone, `numeric` counts) and `enterKeyHint`, no page-level horizontal scroll at 360 px. Sheets: put primary actions in `.modal-actions` (it is sticky at the bottom on phones). Forms in sheets: call nothing extra — typing marks the sheet dirty automatically.
- Printing stays available on phone (wireless printers): keep print buttons; make print layouts work at A4 and on a phone print preview.

## Done means
1. `npx tsc --noEmit` clean, `npm test` ALL pass (existing 170 + yours), `npm run build` OK — run them in `/home/claude/wt/<stream>/03 Prototype`. The CPU is shared by 16 agents: run the full suite only a few times, not after every edit.
2. Tests: add meaningful domain/command tests in YOUR test file (rules, permissions, idempotency, edge cases, figures).
3. Visual check with Playwright (pre-installed: `require('playwright')`, `chromium.launch()` with no options; run node scripts from a scratch dir such as `/tmp/claude-0/<stream>/`, passing the HTML path as an argument). Open `file://<worktree>/03 Prototype/dist/index.html`. Desktop 1366×800; phone 390×844 with `isMobile:true,hasTouch:true` and `context.addInitScript(()=>localStorage.setItem('hcc-display-mode','mobile'))`. Switch demo user via the demo controls (desktop: `select[aria-label="Preview as demo user"]`; phone: DEMO pill → first select in the sheet). Demo user IDs: owner, manager, reception, cashier, supervisor, housekeeper, housekeeper2, accountant, maint, techadmin. Check: your pages render for the right roles, no console/page errors, `document.documentElement.scrollWidth <= innerWidth` on phone, main action works end-to-end. Look at at least one desktop and one phone screenshot of each of your pages.
4. Commit in your worktree.

## Final answer (your return value — keep it short, ≤ 25 lines)
Stream, what you built (screens desktop/mobile, commands, fixtures), test count added, tsc/test/build results, files touched outside your owned list (should be none), any shared change you NEEDED but did not make (exact file + change), known gaps.
