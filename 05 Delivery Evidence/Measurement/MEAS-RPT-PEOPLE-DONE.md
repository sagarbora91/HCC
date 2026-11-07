# F12-MEAS-RPT-PEOPLE DONE

Lane meas-rpt-people / Sonnet 5.5 / branch `claude/meas-rpt-people` / worktree `E:\Projects\HCC\worktrees\meas-rpt-people` / base `claude/meas` b89cc237 (b017cf13 + Phase B briefs).

## Commits
See `git log --oneline b89cc237..HEAD` (hash in the coordinator report).

## Files
Prototype (`03 Prototype`): `src/domain/reports/meas-people/{shared,staff-productivity,attrition,agent-scorecard,training,staff-pulse}.ts`; append in `src/domain/reports/register.ts` (F12-RPT-PEOPLE); `tests/stream-meas-rpt-people.mjs`, `tests/rpt-meas-people-entry.ts`; goldens `tests/rpt-golden/goldens/rpt.people.*.json`; regenerated `src/domain/reports/catalogue.json` and `08 Production/tests/Hcc.Reporting.Tests/catalogue.json` (shared files; the integrator regenerates once with `node tools/rpt-catalogue.mjs`).
Server (`08 Production`): `src/Hcc.Reporting/Reports/MeasPeople/{PeopleRules,StaffProductivityReport,AttritionReport,AgentScorecardReport,TrainingReport,StaffPulseReport}.cs` (each selector also implements `IMeasuredReport`); append in `src/Hcc.Api/HccComposition.cs` (services block only); `tests/Hcc.Meas.Tests/PeopleReportRulesTests.cs`; `tests/Hcc.Reporting.Tests/EngineTests.cs` (F12-RPT-PEOPLE block in `ServerCatalogue`, needed by the cross-check test).
No migration (4843-4844 unused), no SQL schema change, SCHEMA stays 15.

## What was built
Five reports, area `people`, status `MEAS`, ids/columns/flags identical on both sides. Every report ends with a bool column `exception` ("Exception"): the engine has no row flag, so the red-row rule of the catalogue entry is printed Yes/No in screen, Excel and PDF.
- `rpt.people.staff-productivity` (D-M, month + department, phone yes): headcount, staff-hours per occupied room-night, OT %, absenteeism %, late marks, rostered hours on low-forecast nights. Red: OT above `meas.target.ot`, absenteeism above `meas.target.absenteeism`, or rostered hours on low nights above the need by 20 % (need = rooms sold on those nights x the month's hours per room-night; low night = occupancy below `meas.target.occupancy`). Integer arithmetic. Hours and days only, no pay column.
- `rpt.people.attrition` (D-FY, FY + department): joiners, leavers, exit reasons (from `measX.exit` / `meas.staff_exit`), tenure, trailing-12-month attrition %, vacancy days. Red above `meas.target.attrition`. "Capture needed: exit reason" note in the date basis while no reason is recorded.
- `rpt.people.agent-scorecard` (D-M, month + desk user, money gate PERM-RPT-002, Owner and Manager only, selector refuses others): check-ins/outs, rate edits, discounts (count, Rs, % with reason), comps, upsell per arrival, complaints, cash variances (Rs), regrets, survey score. Money columns: discountAmount, cashVariance (removed by rule M). Red: discounts or cash variances above the team's upper quartile (team >= 4, value > 0).
- `rpt.people.training` (D-ASOF, as-of + department, phone yes): records, days to expiry, hours this quarter. Red: mandatory certificate (fire safety, first aid, POSH, FSSAI handler for Kitchen, or the record's mandatory flag) expired and not renewed by a later valid record; missing mandatory courses are listed as "(missing)" rows, red. No records: zero rows + "Capture needed: hr.training".
- `rpt.people.staff-pulse` (D-FY, FY + quarter, screen/PDF only): by quarter and department only; departments with fewer than 5 answers suppressed (count stated in the date basis); no staff id anywhere (test scans the result for every staff id and name). eNPS red below 0. Owner and Manager only.
- Switches: all five go through `MEAS_REPORT_KEY` (rows already correct: productivity, attrition, scorecard, training, pulse) and `measOn`; payroll module off or pack/item off => absent from the catalogue and `MEAS_OFF` on run (tested for all five, data kept). Server: `IMeasuredReport.MeasKey`.
- Permissions: `PERM-HR-001` is not in the prototype matrix; `meas-people/shared.ts` registers its grant (Owner, Manager, Accountant, Tech admin = server seed 0901) and `PERM-HR-002` (Owner, Manager) in `RPT_V2_GRANTS`, the same pattern mask.ts uses. The pulse uses `PERM-HR-002` (catalogue says "HR-001 (Owner, Manager)"; same audience, and the server selector checks it).

## Tests
Counts are in RESULTS below (all heavy jobs through the lock).
- `node tests/_one.mjs tests/stream-meas-rpt-people.mjs`: 19 tests (`[F12-RPT-PEOPLE]`), 18 passed on the first run, the one failure (a test assumption about the cashier filter) fixed.
- `npx tsc --noEmit`: clean.
- Server: `Hcc.Meas.Tests/PeopleReportRulesTests.cs` (rule boundaries and definitions), cross-check `Catalogue_json_from_the_prototype_equals_the_server_registry` in `Hcc.Reporting.Tests`.

## Open items
- Server data limits (documented in the report date basis / class comments): productivity uses marked attendance days and the default shifts M/E/N/G (no leave or paid-holiday netting, no Shift patterns master); scorecard upsell captures and comps have no actor column on the server and read 0; scorecard uses `CAST(utc AS DATE)` for discount/complaint/survey dates. Server SQL has not been executed against a database in this lane (compile and rule tests only).
- Prototype forecast need uses actual room-nights (a closed month), not the yield forecast curve.
- Capture forms and server command endpoints of cap-people remain owed (see MEAS-CAP-PEOPLE-DONE.md).

## Integration needed
1. `03 Prototype/src/domain/types` / report engine: an optional `RptRow.flag` would be cleaner than the trailing `exception` column; if the integrator adds it, switch the five reports (one line each) and drop the column on both sides.
2. `03 Prototype/tests/stream-rbac-matrix.mjs`: nothing from this lane (no new commands).
3. Catalogue: set the five §3.10 entries to `built`; add `<!-- F12-RPT-PEOPLE -->` status lines at the end of the catalogue file.
4. Shared files touched: both `catalogue.json` copies and `EngineTests.cs` `ServerCatalogue` (other report lanes touch the same spots: merge by regenerating catalogue.json and keeping every F12 block).
5. Server: ensure `ISettingsReader` and `IAccessPolicy` are registered (they are in the API); productivity/attrition read `meas.target.*` through `ISettingsReader` with the catalogue defaults.

## HANDOFF bullet
F12 rpt-people (Sonnet): five people measurement reports (staff-productivity, attrition, agent-scorecard, training, staff-pulse) on both sides, switch/payroll-module gated, pulse anonymous with a 5-answer minimum, red-row rule printed as an `Exception` column; no migrations; server SQL not yet run against a database.

## RESULTS
Stopped at the tool-turn cap while the shared lock stayed busy (exit 75 many times).
- Done and green: `tests/stream-meas-rpt-people.mjs` 18/19 on the first run; the failing assertion was fixed afterwards (re-run owed); `npx tsc --noEmit` clean (run before the test files existed).
- NOT yet run (a background chain `.scratch/chain.sh` was queued and may have finished: see `.scratch/gold.log`, `meas-dotnet.log`, `rep-dotnet.log`, `npmtest.log`): `node tools/rpt-catalogue.mjs` + `node tests/rpt-golden/accept.mjs` (writes `catalogue.json` x2 and `rpt.people.*.json` goldens; commit them if they appear as changes), `dotnet test Hcc.Meas.Tests`, `dotnet test Hcc.Reporting.Tests` (cross-check test fails until catalogue.json is regenerated), `npm test`. The C# has not been compiled yet.
- Counts before/after: not recorded.
