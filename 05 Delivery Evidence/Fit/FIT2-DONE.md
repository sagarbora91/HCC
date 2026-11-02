# FIT2-DONE — wave FIT-2 integrated on `claude/fit` (2 Nov 2026, integrator Opus)

Owner rule (contract §12; DEC-194 / 195 / 196 stay): *a list scrolls inside its own panel; a task never scrolls — it is split into sub-screens that each fit, and a fixed status rail beside the active sub-screen shows every step's result, so reviewing needs no click and doing needs no scroll.*

## Merges

Order shell (940b65d6) → lists (e555cc82) → folios-appr (1e347be4) → closing (b973ffd5) → expenses (c9e0996d) → accounting (d86586ee) → forms (435f047f), each `--no-ff`. **No conflicts** (only the closing lane touched `src/app/subscreens.ts`, under `// FIT2-CLOSING`). The 226 lane screenshots committed on the lists / folios-appr / expenses / accounting branches were kept out of the merged tree (copied to `E:\Projects\HCC\worktrees\_fit-evidence-stash\<lane>\`); the lanes' text check reports and DONE notes are kept.

## What changed, per page

| Page | Change (lane) |
|---|---|
| Shell (`fit.tsx`, `z-fit.css`) | `StepFlow` / `FormSections` `summary` = result rail; `SplitView` list-only until a record is open (`emptyPane`); `FitTabs` More ▾ menu; `StatStrip` compact / collapsible / collapseOnTablet; `FitNote` measured More (shell). **Integrator:** rail result wraps to two lines beside the sub-screen (was cut to one line with "…" at tab-land: "Not counted · expected ₹…", "Blocked by arrivals resolv…"); rail with summaries ~300 px from 1024 to 1279 px (was 240 px) |
| Day closing | rail shows each step's result: preflight "N to resolve – titles", rooms posted, cash count / difference, close blockers, net receipts, next-day float, reopen state (closing) |
| Cashier shift | two columns with four scroll panels → four sub-screens (Expected cash, Count & result, Cash movements, Cash out) with a result rail (closing) |
| Checklists & handover | each run in the rail shows % / done / mandatory / overdue or submitted; dashboard strip collapses on tablet (closing) |
| Appearance & display (prefs) | rail shows the current value of every section (forms) |
| Master configuration | each group row shows up to three headline values, secrets / Owner-only never, money masked (forms). **Integrator:** "Back to list" on a wide screen now really closes the open group (list full width) — before it was a no-op there |
| Backup | tab counts (history rows, restore steps) (forms) |
| Folios | list full width until a folio is picked (shell default) |
| Approvals | inbox = list (What / Kind / Requested by / Amount / Age-expiry / Status) + request pane with sticky Approve / Reject; logic unchanged (folios-appr). **Integrator:** seeded-row render test (the demo seed has no pending approval) |
| Expenses | one filter row (status select with counts), compact strip, register takes the rest of the height, approval requests in a pane only when asked; Add expense = `FormSections` with result rail (expenses). **Integrator:** dialog has a set height and the sections fill it, so the action bar stays in view at text scale 1.3 (it was 47 px below the dialog at tab-land 1.3); rail results wrap to three lines; below 900 px the sections stack above the fields (the third was cut off sideways at tab-port) and the dialog uses the taller screen |
| Accounting | compact collapse-on-tablet strip; 21 views via FitTabs More (accounting) |
| Messages, Guest services, Dashboard, Yield, OTA channels | compact collapse-on-tablet strips; Yield notes show 2 lines (lists). **Integrator (Messages):** on the Outbox the "Simulated" note shares the panel footer with "Show more" (other tabs keep it above the panel) |
| Groups | block list full width, 2–3 blocks per row, no auto-selected block (lists). **Integrator:** the panel head "Blocks · 8" (a repeat of the tab right above it) is gone; the rows get that line |
| Reservations, Outbox, lane tables | cell padding 6 px, Show more shares the footer line (lists) |
| POS day summary | strip compact; settled-bills note moved into the summary footer; manage links on one row (lists) |
| Document capture | **Integrator:** `emptyPane="show"` — step 2 "Review and confirm" stays beside step 1 (it would otherwise vanish at ≥ 900 px). The other list-only pages (Folios, Groups, Reports, Guests, In-house, Customers, Settings) had only a "Pick a …" hint in the empty pane; Reports and Settings show a record by default anyway |

## Rows visible in the list panel (Owner, measured in the browser; full rows / rows incl. the partly visible one)

| Page | tab-land 1138×640 before lists lane* | after lanes (integrator start) | final | lap-1366 after lanes → final |
|---|---|---|---|---|
| Messages › Outbox | ≈ 3.5 | 4 / 4.3 | **4 / 4.8** | 5 / 5.6 → **6 / 6.2** |
| Reservations | ≈ 5 | 5 / 5.8 | 5 / 5.8 (unchanged) | 7 / 7.5 (unchanged) |
| Groups › Blocks (2 per row) | — | 3 / 3.6 lines = 6–7 of 8 blocks | **4 / 4 lines = all 8 blocks** | 3 lines (8 blocks, 2.7 per row; all visible) |
| Groups › Companies | — | 3 / 3 (all) | 3 / 3 (all) | 3 / 3 |

\* lists lane note. Row height at tab-land is 57 px = the 44 px touch target + 6 px padding, so rows were not made shorter. Reservations not raised: its toolbar (search, 8 status chips, Source / Staying-on selects with their labels above) uses the full width at 1138 px, so neither moving the labels beside the selects nor merging the tab bar with the strip fits without wrapping; merging tabs + strip on Messages / Groups does not fit either (strip ≈ 690 / 1050 px + tabs ≈ 430 / 610 px > 1106 px).

## Checks (all through `tools\Invoke-Serialized.ps1`)

- `npx tsc --noEmit` clean.
- `npm test` (`FIT_ENFORCE_ALL=1`): **2373 / 2373 passed** (FIT-1 had 2339; the lanes' own counts were from runs cut short by the accounting test, below). Found and fixed: the accounting lane's `tests/stream-fit2-accounting.mjs` ran at import and called `process.exit(0)`, which ended `npm test` part-way (every stream file sorted after it never ran, no totals line — the "cut off" output); rewritten as `register({test,assert})`.
- `npm run build` and `npm run build:production` clean.
- **Full `node tools/fit-screen-check.mjs`** (5 viewports × 9 roles × every page and sub-screen at text 1, text 1.3 on tab-land + lap-1366, modal samples): **PASS — 0 failing of 5,666** (`fit-check-20261102-0726.md` / `.json`). Fonts equal to the baseline; **no `allow.json` entry was needed** (rail summaries use the rail button's own text sizes). After the Settings "Back to list" fix: re-built (tsc, 2373 / 2373, both builds clean) and --pages settings over every viewport / role / text scale / modal: **PASS — 0 failing of 850** (`fit-check-20261102-0829.md`).
- Browser measurement of the Add expense dialog (Owner; tab-land, lap-1366, lap-1280, tab-port; text 1 and 1.3): dialog inside the screen, Cancel / Save draft / Submit inside the dialog and the viewport in all 8 cases, no sideways clipping, no document scroll.

## Screenshots for the owner

`E:\Projects\HCC\worktrees\fit\05 Delivery Evidence\Fit\screens-final2\<page>\` — Owner at tab-land and lap-1366 for closing, cashier, handover, prefs, settings, backup, folios, approvals, expenses (+ Add expense dialog), accounting, messages, groups, guest-services, overview, yield, channels, reservations, pos, documents; tab-port for closing, prefs, expenses (+ dialog). Looked at by the integrator: closing (tab-land, tab-port), cashier, handover, prefs (tab-land, lap-1366), settings, folios, approvals, expenses + dialog (tab-land, tab-port, 1.3), accounting, pos, overview, backup, documents, messages, groups.

## Not done / open

- Reservations rows at tab-land stay ≈ 5.8 (see above).
- Compact stat strips with long sub-labels (Dashboard, Accounting) at tab-land: the last figure is partly behind the expand toggle (e.g. Dashboard "Payment receipts ₹21,500", Accounting "Exceptions 0"); the figure stays readable and expanding shows all, but the card edge is cut. A shell follow-up could wrap the compact strip to a second line instead.
- Approvals: no pending approval in the demo seed, so the list rows are covered by the seeded render test only (`[FIT2-FOLIOS-APPR] approvals list rows with a seeded pending request`), not by the browser check.
- Lane worktrees and branches left in place; Start-Gate not run (coordinator).
