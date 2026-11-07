# MEAS-STUDY-B DONE

Lane meas-study-b / Sonnet 5.5 / branch `claude/meas-study-b` / worktree `E:\Projects\HCC\worktrees\meas-study-b` / base `claude/meas` b017cf13 (briefs commit b89cc237).

## Commits
See `git log --oneline b89cc237..HEAD` (two feature commits plus this DONE commit). Nothing pushed.

## Files
Prototype: `src/domain/analytics/beyond-lib.ts` (maths, context, State loader), `beyond-revenue.ts`, `beyond-guest.ts`, `beyond-ops.ts`, `beyond-finance.ts`; `src/domain/reports/study-b/index.ts` (12 registrations + `registerMeasReport`); `tests/stream-meas-study-b.mjs`.
Server: `08 Production/src/Hcc.Reporting/Analytics/Beyond{Lib,Revenue,Guest,Ops,Finance}.cs`, `Reports/MeasStudyB/StudyBReports.cs`; tests `Hcc.Reporting.Tests/StudyBParityTests.cs`, `Goldens/study-b-an.json`.
Append-only: `register.ts` and `HccComposition.cs` inside `F12-STUDY-B`; `EngineTests.cs` ServerCatalogue gets one `F12-STUDY-B` block.
Outside my row (regenerated, see Integration): `src/domain/reports/catalogue.json` and `Hcc.Reporting.Tests/catalogue.json` via `node tools/rpt-catalogue.mjs`.

## What was built
12 reports `rpt.study.*` (revenue-kpi, channel-economics, pricing-shape, guest-repeat-value, guest-origin-plan, housekeeping-operations, fnb-halls-ancillary, finance-cost, corporate-accounts, compliance-risk, forecast-scenarios, owner-dashboard), area `study`, switches `meas.study.<key>`, one section per workbook sheet (sections[0] = first sheet). Pure selectors `AnalyticsSelector` with integer half-up maths; C# twin has the same method names (`BeyondRevenue.RevenueKpi` ...). `anInputFromState` builds AnInput from bookings; money columns flagged (DEC-061 via the engine's money gate); repeat list uses mask `guestName`. Legacy marker = `src` column (Legacy / HCC live / Legacy + live) and about.legacy; footer text in dateBasis. Chart series are extra sections keyed `series:<name>` with columns label / value and a title prefix `[bar]`, `[line]` (for meas-study-pack ChartBlock; tables only until it merges). No SQL, no migration, SCHEMA 15.

## Tests
- `tsc --noEmit` clean. `HCC_TEST_ONLY=study-b npm test`: 68/68 (13 new `[F12-STUDY-B]`: registration, hand-computed occupancy/ADR/RevPAR, net ADR, repeat boundary 3 stays, break-even, win-back 12 months, COVID capacity, dashboard 12 months, three-way screen = Excel = print on FY26 for owner and accountant, DEC-061, MEAS_OFF, golden freshness).
- `dotnet test tests/Hcc.Reporting.Tests`: 86/86 (3 new: all 12 reports reproduce the prototype golden cell for cell; ids; definitions).
- Full `tsc` + `npm test` were queued behind other lanes at the 80-turn cap; log will be `.scratch/npmtest-full.log` (uncommitted). The integrator must run the full suite; only study-b-filtered and `Hcc.Reporting.Tests` runs are confirmed green.

## Headline table (golden fixture, fictional; rpt.study.revenue-kpi, KPI by FY; paise)
| FY | room revenue billed | room-nights | occupancy % | ADR |
|---|---|---|---|---|
| 2024-25 | 89420000 | 282 | 15.5 | 299064 |
| 2025-26 | 101880000 | 304 | 13.9 | 305030 |
| 2026-27 | 47930000 | 148 | 11.7 | 292256 |
| Total | 239230000 | 734 | 13.9 | 300163 |
For the legacy golden the integrator compares billed (room revenue before GST), room-nights, occupancy, ADR per FY against the workbooks; the same selectors give them.

## Open items (honest limits)
- Day-level only: AnInput has dates, not timestamps. Turnaround = days between stays; check-in/out vs standard needs optional `inTime/outTime` on stays (empty otherwise); upgrades need optional `bookedType`; origin / nationality / meal plan need optional stay fields; hall events, expenses, audit counts and licences come through `params.extras` (`BeyondExtras`). Prototype supplies expenses and audit counts from State; halls and licences are empty until their modules feed them. Not ported: workbook template sheets, GST by slab (effective % only, slab needs line tax rates), per-guest names/phones in the repeat list (customer key only, masked), Incident Register.
- Commission % is a parameter (default 15) until the channel master carries it; comp-set inputs are parameters. Modal rate ties pick the lowest rate (workbook: first seen).
- Zero-rate/complimentary (DEC-266) is not in these 12 (belongs to study-a). Complimentary flag exists on AnStay only.

## Integration needed
1. Server data source: implement `IAnalyticsInputSource` (`Hcc.Reporting.Reports.MeasStudyB`) in SQL: live stays/bills/receipts + `lgc` history + rooms master (halls and duplicates excluded, 211 = Suite, `legacy` marker, `legacyUntil` 2026-10-30) and register it before `AddStudyB` (the default `EmptyAnalyticsInputSource` is TryAdd, so a real one wins). Until then the reports render empty tables.
2. Regenerate `catalogue.json` (both copies) with `node tools/rpt-catalogue.mjs` after merging all report lanes (merge conflicts expected there).
3. `EngineTests.ServerCatalogue()` carries my `F12-STUDY-B` block; keep it when merging other lanes' blocks.
4. Catalogue status lines: set the 12 entries to built from code (not edited by me).
5. meas-study-pack: draw `series:*` sections via ChartBlock.

## HANDOFF bullet
MEAS study-b: 12 `rpt.study.*` reports (revenue KPI, channel, pricing, guest repeat/origin, HK ops, F&B/halls, finance, corporate, compliance, forecast, owner dashboard) ported as pure selectors in TS and C# with identical numbers (golden JSON parity, 86 server + 68 stream tests); server needs an SQL `IAnalyticsInputSource` (live + lgc) before real data shows.
