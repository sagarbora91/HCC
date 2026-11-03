# F7-R2 report hub — DONE (3 Nov 2026, lane R2, branch `claude/f7-r2`)

## Built (prototype only; R2 owns no server code)
- Page `report-hub` "Report library" (Closing & reports): `03 Prototype/src/features/reports/HubPage.tsx` (FitPage + SplitView, search, category filter, favourites, grouped by category), `ReportView.tsx` (parameters panel from `RptDefinition.params`, table with `usePaged`, totals/subtotals, provisional / legacy / money-removed notes, Export Excel / Export CSV / Print buttons, favourite toggle), `ScheduleTabs.tsx` (Scheduled runs: my subscriptions, editor, run history with `held` shown, Run due now; Export log), `mobileHub.tsx` (phone: search, chips, cards, no table), `HubRegister.tsx`, `hub-logic.ts` (pure logic), `hub-engine.ts` (the single seam), styles `src/styles/rpt-hub.css` (tokens only).
- Sub-screens `reports | scheduled | log` in the `// F7-R2` blocks of `subscreens.ts`, `navmap.ts` (PAGE_CATEGORY, blurb, keywords) and `registry.ts`.
- Tests: `tests/stream-rpt-hub.mjs` (+ `tests/rpt-hub-entry.tsx`): 11 tests (role filter, money guard, search, grouping, defaults, schedule text, recipients, registration, hub render owner + reception, phone render, viewer error, CSS tokens).

## Integration needed (f7-int)
1. `src/features/reports/hub-engine.ts`: change the path to `'../../domain/reports/engine'`; delete `src/domain/reports/_contract-stubs-f7.ts`. Names used: `reportCatalogue, canViewReport, canExportReport, runReport, toXlsx, toCsv, toPrintModel, printHtml`; commands `RPT_FAV_TOGGLE{reportId}`, `RPT_SCHEDULE_SAVE{id?,reportId,params,format,cadence,recipients,enabled}`, `RPT_SCHEDULE_DELETE{id}`, `RPT_RUN_DUE`, `RPT_LOG{reportId,params,format,rows,provisional}`. Adjust payload names if R1 differs.
2. With the stubs the hub shows "No reports available" (catalogue empty); once R1 registers reports it fills. `canViewReport` must be true for the roles in the catalogue (stub returns false).
3. `ReportView` run is memoised on the whole state `s` (no per-report dependency list exists); R1 may expose a cheaper key.
4. Windows-only checks left to the PC: `tools/fit-screen-check.mjs` (owner, reception; tab-land, lap-1366 — the hub is picked up from PAGE_CATEGORY + SUB_SCREENS), phone smoke at 360 px, Android print.

## Questions for f7-int / owner (not stated in the catalogue entry)
- Supervisor is treated like Housekeeper / Maintenance for the money guard (masters rule MA-10). Confirm.
- Nav gate is any of `reports, billing, reservations, checkin` (Owner, Manager, Accountant, Reception, Cashier); Housekeeping / Maintenance / Supervisor do not get the page until non-money reports exist for them (widen `HubRegister.tsx` + the `report-hub` line in `tests/stream-rbac-matrix.mjs`). The engine's view right is the real gate; the hub also drops money reports for those roles.
- Edited outside R2's files: one `report-hub` line in `tests/stream-rbac-matrix.mjs` PAGES (required by the RBAC matrix test).
- Test run: full `npm test` 2570/2583 before two fixes (RBAC doc line, CSS `transparent` pairs in the theme audit); the affected suites (rbac-matrix, themes, fit, rpt-hub) pass 151/151 after.
- Scheduling is offered only for reports the user may export (no `report.schedule` code in the prototype `can()` yet).
- Phone schedules are read-only (editing on desktop).
- `RPT_TOO_LARGE` is not yet in `ErrorCode` (R1).
