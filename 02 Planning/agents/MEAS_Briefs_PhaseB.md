# MEAS Phase B briefs — wave F12 (7 Nov 2026)

Base: `claude/meas` head **b017cf13** (Phase A merged, areas `revenue` / `study` allowed, hub filter). Contract: `02 Planning/agents/MEAS-CONTRACT.md`. Phase A DONE files: `05 Delivery Evidence/Measurement/MEAS-*-DONE.md`. Nine lanes run in parallel (B and B2 together); heavy jobs queue through `tools/Invoke-Serialized.ps1`.

## Lane: meas-rpt-daily (Sonnet) — Phase B, PORT 4201
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-rpt-daily, branch claude/meas-rpt-daily.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §3, §4, §6, §8, §10; tools/README-agents.md §1–§4; the catalogue entries below in 02 Planning/HCC_Report_Catalogue_v1.md §3.10 (columns, params, money flags, exception rule per entry); one existing report of the same shape as a pattern (03 Prototype/src/domain/reports/guests/* and 08 Production/src/Hcc.Reporting/Reports/Guests/*); the Phase A DONE files for the data you read. Do not read HANDOFF.md, CLAUDE.md or the Decision Register.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-RPT-DAILY block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-daily\03 Prototype" -Label meas-rpt-daily -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-rpt-daily\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4835–4836: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Reports to build (both sides, same ids, column keys and flags):
- rpt.guests.manager-flash
- rpt.guests.regrets-denials
- rpt.guests.rate-variance
- rpt.rooms.room-discrepancy
- rpt.money.high-balance
- rpt.money.allowances-voids
- rpt.guests.no-show-cancel
- rpt.rooms.inventory-exceptions
- rpt.money.cashier-variance

Data you read: captures from meas-cap-desk (turnaway, walked, bookingExtra) and existing stays / bills / adjustments / cashier / room status data.
Ownership: prototype `03 Prototype/src/domain/reports/meas-daily/*`, `tests/stream-meas-rpt-daily.mjs`, `tests/rpt-golden` additions for your ids; server `08 Production/src/Hcc.Reporting/Reports/MeasDaily/*` (one class per report implementing `IReportSelector` and `IMeasuredReport` with its MeasKey), test files in `tests/Hcc.Reporting.Tests` / `tests/Hcc.Meas.Tests` (add files, not projects).
Append-only: `F12-RPT-DAILY` in 03 Prototype/src/domain/reports/register.ts (imports + register calls), 08 Production/src/Hcc.Api/HccComposition.cs (both F12-RPT-DAILY spots: selectors registered), Hcc.slnx. `registerMeasReport(id,key)` rows exist for all 54 ids in measure.ts (MEAS_REPORT_KEY): confirm yours map to the right item key (contract §1.2); a wrong key is an Integration-needed note, not an edit.
Forbidden: config.ts, measure.ts bodies, types.ts, capture handlers, other lanes' folders, money postings.

Build:
- Each report: params and date class per the catalogue; screen = Excel = PDF text on the FY26 fixture (use the golden harness: tests/rpt-golden/harness.mjs); exception rule from the entry drives the red-row flag; one test per rule at the boundary (just red / just not red); targets read through `measTarget` (prototype) and the same defaults on the server.
- Respect the switch: the report is hidden and refuses `MEAS_OFF` when its item is off (MEAS_REPORT_KEY); module-tied items follow their module.
- Money columns: moneyFlag per column, stored paise, rupees only in display.
- Plan section "domain 1"; catalogue §3.10 entries listed above, columns in entry order.
- Also `measNightAuditLines(s)` in `reports/meas-daily/` (night-audit auto-tick lines, filtered with `measOn`; an off item is skipped, never failed); the existing night-audit code calls it through ONE append in your block only if a seam exists, else list the edit in DONE.
- `meas.daily.blockClose` (default off) is the only way a measurement may block a close: implement the check as a pure function and test both settings.
- Empty-data behaviour: a report with no captured data shows the entry's "Capture needed" note and zero rows, never an error.

Definition of done: all listed ids registered on both sides, ids/columns/flags identical (the existing cross-check test passes), golden harness green for them, tests/stream-meas-rpt-daily.mjs ([F12-RPT-DAILY] names) covering every rule, npx tsc --noEmit, npm test and the server tests green; counts before / after recorded.

Test commands:
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-daily\03 Prototype" -Label meas-rpt-daily -Command "npx tsc --noEmit"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-daily\03 Prototype" -Label meas-rpt-daily -Command "npm test"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-daily\08 Production" -Label meas-rpt-daily -Command "dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-daily\08 Production" -Label meas-rpt-daily -Command "dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --logger trx"

DONE file: 05 Delivery Evidence/Measurement/MEAS-RPT-DAILY-DONE.md (template tools/README-agents.md §4), incl. "Integration needed" and HANDOFF bullet. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-rpt-revenue (Sonnet) — Phase B, PORT 4202
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-rpt-revenue, branch claude/meas-rpt-revenue.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §3, §4, §6, §8, §10; tools/README-agents.md §1–§4; the catalogue entries below in 02 Planning/HCC_Report_Catalogue_v1.md §3.10 (columns, params, money flags, exception rule per entry); one existing report of the same shape as a pattern (03 Prototype/src/domain/reports/guests/* and 08 Production/src/Hcc.Reporting/Reports/Guests/*); the Phase A DONE files for the data you read. Do not read HANDOFF.md, CLAUDE.md or the Decision Register.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-RPT-REVENUE block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-revenue\03 Prototype" -Label meas-rpt-revenue -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-rpt-revenue\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4837–4838: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Reports to build (both sides, same ids, column keys and flags):
- rpt.revenue.demand-calendar
- rpt.revenue.pickup-pace
- rpt.revenue.displacement
- rpt.revenue.overbooking-wash
- rpt.revenue.restriction-effect
- rpt.revenue.rate-parity

Data you read: meas-forecast outputs (events, otbSnapshots, competitors, rateShops, restrictionCounters, `src/domain/forecast.ts`, displacement pure function) — read them, do not change them.
Ownership: prototype `03 Prototype/src/domain/reports/meas-revenue/*`, `tests/stream-meas-rpt-revenue.mjs`, `tests/rpt-golden` additions for your ids; server `08 Production/src/Hcc.Reporting/Reports/MeasRevenue/*` (one class per report implementing `IReportSelector` and `IMeasuredReport` with its MeasKey), test files in `tests/Hcc.Reporting.Tests` / `tests/Hcc.Meas.Tests` (add files, not projects).
Append-only: `F12-RPT-REVENUE` in 03 Prototype/src/domain/reports/register.ts (imports + register calls), 08 Production/src/Hcc.Api/HccComposition.cs (both F12-RPT-REVENUE spots: selectors registered), Hcc.slnx. `registerMeasReport(id,key)` rows exist for all 54 ids in measure.ts (MEAS_REPORT_KEY): confirm yours map to the right item key (contract §1.2); a wrong key is an Integration-needed note, not an edit.
Forbidden: config.ts, measure.ts bodies, types.ts, capture handlers, other lanes' folders, money postings.

Build:
- Each report: params and date class per the catalogue; screen = Excel = PDF text on the FY26 fixture (use the golden harness: tests/rpt-golden/harness.mjs); exception rule from the entry drives the red-row flag; one test per rule at the boundary (just red / just not red); targets read through `measTarget` (prototype) and the same defaults on the server.
- Respect the switch: the report is hidden and refuses `MEAS_OFF` when its item is off (MEAS_REPORT_KEY); module-tied items follow their module.
- Money columns: moneyFlag per column, stored paise, rupees only in display.
- Hub area `revenue` already exists (label "Revenue management"). Displacement is a report shell over the calculator (params: date range, room type, candidate group size and rate).
- Money columns carry money flags; Manager sees them, Housekeeping / Maintenance see no money (DEC-061).
- Empty-data behaviour: a report with no captured data shows the entry's "Capture needed" note and zero rows, never an error.

Definition of done: all listed ids registered on both sides, ids/columns/flags identical (the existing cross-check test passes), golden harness green for them, tests/stream-meas-rpt-revenue.mjs ([F12-RPT-REVENUE] names) covering every rule, npx tsc --noEmit, npm test and the server tests green; counts before / after recorded.

Test commands:
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-revenue\03 Prototype" -Label meas-rpt-revenue -Command "npx tsc --noEmit"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-revenue\03 Prototype" -Label meas-rpt-revenue -Command "npm test"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-revenue\08 Production" -Label meas-rpt-revenue -Command "dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-revenue\08 Production" -Label meas-rpt-revenue -Command "dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --logger trx"

DONE file: 05 Delivery Evidence/Measurement/MEAS-RPT-REVENUE-DONE.md (template tools/README-agents.md §4), incl. "Integration needed" and HANDOFF bullet. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-rpt-guest (Sonnet) — Phase B, PORT 4203
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-rpt-guest, branch claude/meas-rpt-guest.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §3, §4, §6, §8, §10; tools/README-agents.md §1–§4; the catalogue entries below in 02 Planning/HCC_Report_Catalogue_v1.md §3.10 (columns, params, money flags, exception rule per entry); one existing report of the same shape as a pattern (03 Prototype/src/domain/reports/guests/* and 08 Production/src/Hcc.Reporting/Reports/Guests/*); the Phase A DONE files for the data you read. Do not read HANDOFF.md, CLAUDE.md or the Decision Register.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-RPT-GUEST block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-guest\03 Prototype" -Label meas-rpt-guest -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-rpt-guest\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4839–4840: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Reports to build (both sides, same ids, column keys and flags):
- rpt.guests.satisfaction
- rpt.guests.reputation-index
- rpt.guests.service-recovery
- rpt.guests.desk-timing
- rpt.guests.upsell
- rpt.guests.repeat-loyalty

Data you read: captures from meas-cap-guest (surveys, reviewMonths, rootCauses) and meas-cap-desk (preferences, bookingExtra); complaints, stays, folio lines UPG / LCO1 / LCO2.
Ownership: prototype `03 Prototype/src/domain/reports/meas-guest/*`, `tests/stream-meas-rpt-guest.mjs`, `tests/rpt-golden` additions for your ids; server `08 Production/src/Hcc.Reporting/Reports/MeasGuest/*` (one class per report implementing `IReportSelector` and `IMeasuredReport` with its MeasKey), test files in `tests/Hcc.Reporting.Tests` / `tests/Hcc.Meas.Tests` (add files, not projects).
Append-only: `F12-RPT-GUEST` in 03 Prototype/src/domain/reports/register.ts (imports + register calls), 08 Production/src/Hcc.Api/HccComposition.cs (both F12-RPT-GUEST spots: selectors registered), Hcc.slnx. `registerMeasReport(id,key)` rows exist for all 54 ids in measure.ts (MEAS_REPORT_KEY): confirm yours map to the right item key (contract §1.2); a wrong key is an Integration-needed note, not an edit.
Forbidden: config.ts, measure.ts bodies, types.ts, capture handlers, other lanes' folders, money postings.

Build:
- Each report: params and date class per the catalogue; screen = Excel = PDF text on the FY26 fixture (use the golden harness: tests/rpt-golden/harness.mjs); exception rule from the entry drives the red-row flag; one test per rule at the boundary (just red / just not red); targets read through `measTarget` (prototype) and the same defaults on the server.
- Respect the switch: the report is hidden and refuses `MEAS_OFF` when its item is off (MEAS_REPORT_KEY); module-tied items follow their module.
- Money columns: moneyFlag per column, stored paise, rupees only in display.
- A survey row never exposes ID data; consent flag respected in every listing; NPS = %9–10 minus %0–6 on the recommend score; target `meas.target.nps` (30).
- Empty-data behaviour: a report with no captured data shows the entry's "Capture needed" note and zero rows, never an error.

Definition of done: all listed ids registered on both sides, ids/columns/flags identical (the existing cross-check test passes), golden harness green for them, tests/stream-meas-rpt-guest.mjs ([F12-RPT-GUEST] names) covering every rule, npx tsc --noEmit, npm test and the server tests green; counts before / after recorded.

Test commands:
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-guest\03 Prototype" -Label meas-rpt-guest -Command "npx tsc --noEmit"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-guest\03 Prototype" -Label meas-rpt-guest -Command "npm test"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-guest\08 Production" -Label meas-rpt-guest -Command "dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-guest\08 Production" -Label meas-rpt-guest -Command "dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --logger trx"

DONE file: 05 Delivery Evidence/Measurement/MEAS-RPT-GUEST-DONE.md (template tools/README-agents.md §4), incl. "Integration needed" and HANDOFF bullet. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-rpt-hk (Sonnet) — Phase B, PORT 4204
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-rpt-hk, branch claude/meas-rpt-hk.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §3, §4, §6, §8, §10; tools/README-agents.md §1–§4; the catalogue entries below in 02 Planning/HCC_Report_Catalogue_v1.md §3.10 (columns, params, money flags, exception rule per entry); one existing report of the same shape as a pattern (03 Prototype/src/domain/reports/guests/* and 08 Production/src/Hcc.Reporting/Reports/Guests/*); the Phase A DONE files for the data you read. Do not read HANDOFF.md, CLAUDE.md or the Decision Register.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-RPT-HK block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-hk\03 Prototype" -Label meas-rpt-hk -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-rpt-hk\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4841–4842: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Reports to build (both sides, same ids, column keys and flags):
- rpt.rooms.hk-productivity
- rpt.rooms.ooo-days
- rpt.rooms.ppm-compliance
- rpt.rooms.utilities-per-room
- rpt.stock.linen-cost
- rpt.acc.asset-life

Data you read: captures from meas-cap-hk (taskTimes, offline, meters, meterReadings, assetLives, discards).
Ownership: prototype `03 Prototype/src/domain/reports/meas-hk/*`, `tests/stream-meas-rpt-hk.mjs`, `tests/rpt-golden` additions for your ids; server `08 Production/src/Hcc.Reporting/Reports/MeasHk/*` (one class per report implementing `IReportSelector` and `IMeasuredReport` with its MeasKey), test files in `tests/Hcc.Reporting.Tests` / `tests/Hcc.Meas.Tests` (add files, not projects).
Append-only: `F12-RPT-HK` in 03 Prototype/src/domain/reports/register.ts (imports + register calls), 08 Production/src/Hcc.Api/HccComposition.cs (both F12-RPT-HK spots: selectors registered), Hcc.slnx. `registerMeasReport(id,key)` rows exist for all 54 ids in measure.ts (MEAS_REPORT_KEY): confirm yours map to the right item key (contract §1.2); a wrong key is an Integration-needed note, not an edit.
Forbidden: config.ts, measure.ts bodies, types.ts, capture handlers, other lanes' folders, money postings.

Build:
- Each report: params and date class per the catalogue; screen = Excel = PDF text on the FY26 fixture (use the golden harness: tests/rpt-golden/harness.mjs); exception rule from the entry drives the red-row flag; one test per rule at the boundary (just red / just not red); targets read through `measTarget` (prototype) and the same defaults on the server.
- Respect the switch: the report is hidden and refuses `MEAS_OFF` when its item is off (MEAS_REPORT_KEY); module-tied items follow their module.
- Money columns: moneyFlag per column, stored paise, rupees only in display.
- DEC-061: these reports are viewed by Housekeeping / Maintenance in some cases, so every money column is removed for those roles by the engine (money flag per column) — test it for HKP and MNT.
- Utilities per occupied room-night uses meter readings (monotonic) over occupied room-nights from stays.
- Empty-data behaviour: a report with no captured data shows the entry's "Capture needed" note and zero rows, never an error.

Definition of done: all listed ids registered on both sides, ids/columns/flags identical (the existing cross-check test passes), golden harness green for them, tests/stream-meas-rpt-hk.mjs ([F12-RPT-HK] names) covering every rule, npx tsc --noEmit, npm test and the server tests green; counts before / after recorded.

Test commands:
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-hk\03 Prototype" -Label meas-rpt-hk -Command "npx tsc --noEmit"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-hk\03 Prototype" -Label meas-rpt-hk -Command "npm test"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-hk\08 Production" -Label meas-rpt-hk -Command "dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-hk\08 Production" -Label meas-rpt-hk -Command "dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --logger trx"

DONE file: 05 Delivery Evidence/Measurement/MEAS-RPT-HK-DONE.md (template tools/README-agents.md §4), incl. "Integration needed" and HANDOFF bullet. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-rpt-people (Sonnet) — Phase B, PORT 4205
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-rpt-people, branch claude/meas-rpt-people.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §3, §4, §6, §8, §10; tools/README-agents.md §1–§4; the catalogue entries below in 02 Planning/HCC_Report_Catalogue_v1.md §3.10 (columns, params, money flags, exception rule per entry); one existing report of the same shape as a pattern (03 Prototype/src/domain/reports/guests/* and 08 Production/src/Hcc.Reporting/Reports/Guests/*); the Phase A DONE files for the data you read. Do not read HANDOFF.md, CLAUDE.md or the Decision Register.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-RPT-PEOPLE block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-people\03 Prototype" -Label meas-rpt-people -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-rpt-people\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4843–4844: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Reports to build (both sides, same ids, column keys and flags):
- rpt.people.staff-productivity
- rpt.people.attrition
- rpt.people.agent-scorecard
- rpt.people.training
- rpt.people.staff-pulse

Data you read: captures from meas-cap-people (training, exits, pulses) plus existing attendance / overtime / staff master and desk-user audit data.
Ownership: prototype `03 Prototype/src/domain/reports/meas-people/*`, `tests/stream-meas-rpt-people.mjs`, `tests/rpt-golden` additions for your ids; server `08 Production/src/Hcc.Reporting/Reports/MeasPeople/*` (one class per report implementing `IReportSelector` and `IMeasuredReport` with its MeasKey), test files in `tests/Hcc.Reporting.Tests` / `tests/Hcc.Meas.Tests` (add files, not projects).
Append-only: `F12-RPT-PEOPLE` in 03 Prototype/src/domain/reports/register.ts (imports + register calls), 08 Production/src/Hcc.Api/HccComposition.cs (both F12-RPT-PEOPLE spots: selectors registered), Hcc.slnx. `registerMeasReport(id,key)` rows exist for all 54 ids in measure.ts (MEAS_REPORT_KEY): confirm yours map to the right item key (contract §1.2); a wrong key is an Integration-needed note, not an edit.
Forbidden: config.ts, measure.ts bodies, types.ts, capture handlers, other lanes' folders, money postings.

Build:
- Each report: params and date class per the catalogue; screen = Excel = PDF text on the FY26 fixture (use the golden harness: tests/rpt-golden/harness.mjs); exception rule from the entry drives the red-row flag; one test per rule at the boundary (just red / just not red); targets read through `measTarget` (prototype) and the same defaults on the server.
- Respect the switch: the report is hidden and refuses `MEAS_OFF` when its item is off (MEAS_REPORT_KEY); module-tied items follow their module.
- Money columns: moneyFlag per column, stored paise, rupees only in display.
- HR-001 gate and payroll module tie: payroll module off hides the whole pack. Pulse report: department only, minimum 5 answers per department else suppressed; no staff id anywhere.
- Pay amounts are never in these reports; overtime and absenteeism are hours / days.
- Empty-data behaviour: a report with no captured data shows the entry's "Capture needed" note and zero rows, never an error.

Definition of done: all listed ids registered on both sides, ids/columns/flags identical (the existing cross-check test passes), golden harness green for them, tests/stream-meas-rpt-people.mjs ([F12-RPT-PEOPLE] names) covering every rule, npx tsc --noEmit, npm test and the server tests green; counts before / after recorded.

Test commands:
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-people\03 Prototype" -Label meas-rpt-people -Command "npx tsc --noEmit"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-people\03 Prototype" -Label meas-rpt-people -Command "npm test"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-people\08 Production" -Label meas-rpt-people -Command "dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx"
  pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-rpt-people\08 Production" -Label meas-rpt-people -Command "dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --logger trx"

DONE file: 05 Delivery Evidence/Measurement/MEAS-RPT-PEOPLE-DONE.md (template tools/README-agents.md §4), incl. "Integration needed" and HANDOFF bullet. Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-digest (Sonnet) — Phase B, PORT 4206
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-digest, branch claude/meas-digest.
Read first: contract §1, §4, §6, §8; tools/README-agents.md §1–§4; the existing Owner digest code (grep DIGEST in 03 Prototype/src/domain/streams and src/features/overview; and the server digest under 08 Production/src).

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-DIGEST block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-digest\03 Prototype" -Label meas-digest -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-digest\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only none: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Build: an Owner digest block "Measurement" listing the red rows from contract §4 (each report's exception rule) as short lines with the report id as drill target, honouring `measOn` / `measOffKeys` (off items absent; whole block absent when the study/daily/... packs are all off), subscription rows so the Owner can choose which measurement lines appear, and the same block in the server digest builder if it exists (append only). The red-row computation calls the pure exception functions exported by the Phase B report lanes; they are not merged yet, so define a small interface `MeasRedRow {key; reportId; text; severity}` in your own file and a registry `registerMeasRedRows(key, fn)` the report lanes' integrator will wire; ship stub providers for the Phase A captures that already exist (turnaways, high balances from folios) and list the wiring needed in DONE.
Ownership: `03 Prototype/src/features/overview/Digest*` (append inside F12-DIGEST only) and a new `src/domain/streams/measure-digest.ts`, `tests/stream-meas-digest.mjs`, server digest file append. Forbidden: report files, config.ts, measure.ts bodies.
Definition of done: block renders with the Phase A data, switches hide lines, tests cover on/off and the empty case; tsc, npm test green. DONE file 05 Delivery Evidence/Measurement/MEAS-DIGEST-DONE.md. Final report <= 120 words. Trailer: Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-study-a (Sonnet) — Phase B2, PORT 4207
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-study-a, branch claude/meas-study-a.
Read first: 02 Planning/agents/MEAS-CONTRACT.md §1, §3, §4, §6, §8, §9, §10; the plan E:\Projects\HCC\02 Planning\HCC_Measurement_Reports_Plan_MEAS_2026-11-05.md section 7A (port of the study workbooks); 03 Prototype/src/domain/analytics/types.ts; the legacy workbooks 1–7 (revenue/cash monthly, year comparison, index vs base, FY deep dive, occupancy history, operations patterns, rooms and rates, zero-rate, receivables) (READ-ONLY, never copied into the repo, never committed; the Notes & Checks sheets give the definitions); catalogue §3.10 entries below; tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-STUDY-A block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-study-a\03 Prototype" -Label meas-study-a -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-study-a\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4850–4853: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Port these reports (each is a faithful port of the workbook tables, same definitions, as live reports over HCC data plus the imported legacy history `lgc`):
- rpt.study.revenue-monthly
- rpt.study.year-comparison
- rpt.study.index-vs-base
- rpt.study.fy-deep-dive
- rpt.study.occupancy-history
- rpt.study.operations-patterns
- rpt.study.rooms-and-rates
- rpt.study.zero-rate-complimentary
- rpt.study.receivables-bad-debt

How:
- One module per workbook family in `03 Prototype/src/domain/analytics/legacy-*.ts` exporting pure selectors `AnalyticsSelector=(input:AnInput,params:AnParams)=>RptResult` (types.ts); server twin `08 Production/src/Hcc.Reporting/Analytics/Legacy*.cs` with the same method names and the same numbers (parity test on the FY26 fixture).
- Definitions (contract §9): occupancy = distinct room-nights slept ÷ (rooms in service × days); ADR = room charge before GST ÷ charged nights (bill month); FY April–March; room 211 = Suite; halls and duplicate stays already excluded by the loader; COVID window 1 Mar 2020 – 31 Mar 2021 excluded from "normal years"; zero-rate/complimentary per DEC-266 (complimentary stay type; if it does not exist yet, use a clearly named zero-rate flag and list the integration); receivables per DEC-265 (legacy "receivables" = bad debts).
- Legacy rows carry a `legacy` marker; footer "Legacy to 30 Oct 2026 / HCC live". Charts are NOT yours: put the series in the result in a form meas-study-pack's ChartBlock can draw (a `series` block: kind, labels, values in paise or counts); until it merges, tables only.
- Reports registered in `reports/study-a/*` (prototype) with columns from the catalogue entry; money flags; screen = Excel = PDF on the FY26 fixture; the exception rule of each entry tested at the boundary; hidden and `MEAS_OFF` when the item is off.
- Appended registration only inside `F12-STUDY-A` in register.ts and HccComposition.cs.
- Headline numbers per report are written into DONE as a table (FY, billed, room-nights, occupancy, ADR) so the integrator's golden test (tools/meas-legacy-golden.ps1, owned by meas-study-pack) can compare against the workbooks. You do NOT read the legacy workbooks' data for anything but definitions and a spot check; the fixture is FY26 (fictional).
Ownership: `03 Prototype/src/domain/analytics/legacy-*.ts`, `src/domain/reports/study-a/*`, `tests/stream-meas-study-a.mjs`, `08 Production/src/Hcc.Reporting/Analytics/Legacy*.cs` and `Reports/MeasStudyA/*`, test files in Hcc.Reporting.Tests. Forbidden: config.ts, measure.ts bodies, types.ts, ChartBlock, other lanes.
Definition of done: all ids both sides, cross-check + golden harness green, tests per rule, tsc / npm test / server tests green, counts recorded. DONE file 05 Delivery Evidence/Measurement/MEAS-STUDY-A-DONE.md (incl. Integration needed + headline table). Final report <= 120 words. Trailer: Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-study-b (Sonnet) — Phase B2, PORT 4208
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-study-b, branch claude/meas-study-b.
Read first: 02 Planning/agents/MEAS-CONTRACT.md §1, §3, §4, §6, §8, §9, §10; the plan E:\Projects\HCC\02 Planning\HCC_Measurement_Reports_Plan_MEAS_2026-11-05.md section 7A (port of the study workbooks); 03 Prototype/src/domain/analytics/types.ts; the eight "beyond" workbooks (build_*.py and beyond_lib.py under E:\Projects\HCC\Existing Software\Reports\) (READ-ONLY, never copied into the repo, never committed; the Notes & Checks sheets give the definitions); catalogue §3.10 entries below; tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-STUDY-B block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-study-b\03 Prototype" -Label meas-study-b -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-study-b\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4854–4857: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Port these reports (each is a faithful port of the workbook tables, same definitions, as live reports over HCC data plus the imported legacy history `lgc`):
- rpt.study.revenue-kpi
- rpt.study.channel-economics
- rpt.study.pricing-shape
- rpt.study.guest-repeat-value
- rpt.study.guest-origin-plan
- rpt.study.housekeeping-operations
- rpt.study.fnb-halls-ancillary
- rpt.study.finance-cost
- rpt.study.corporate-accounts
- rpt.study.compliance-risk
- rpt.study.forecast-scenarios
- rpt.study.owner-dashboard

How:
- One module per workbook family in `03 Prototype/src/domain/analytics/beyond-*.ts` exporting pure selectors `AnalyticsSelector=(input:AnInput,params:AnParams)=>RptResult` (types.ts); server twin `08 Production/src/Hcc.Reporting/Analytics/Beyond*.cs` with the same method names and the same numbers (parity test on the FY26 fixture).
- Definitions (contract §9): occupancy = distinct room-nights slept ÷ (rooms in service × days); ADR = room charge before GST ÷ charged nights (bill month); FY April–March; room 211 = Suite; halls and duplicate stays already excluded by the loader; COVID window 1 Mar 2020 – 31 Mar 2021 excluded from "normal years"; zero-rate/complimentary per DEC-266 (complimentary stay type; if it does not exist yet, use a clearly named zero-rate flag and list the integration); receivables per DEC-265 (legacy "receivables" = bad debts).
- Legacy rows carry a `legacy` marker; footer "Legacy to 30 Oct 2026 / HCC live". Charts are NOT yours: put the series in the result in a form meas-study-pack's ChartBlock can draw (a `series` block: kind, labels, values in paise or counts); until it merges, tables only.
- Reports registered in `reports/study-b/*` (prototype) with columns from the catalogue entry; money flags; screen = Excel = PDF on the FY26 fixture; the exception rule of each entry tested at the boundary; hidden and `MEAS_OFF` when the item is off.
- Appended registration only inside `F12-STUDY-B` in register.ts and HccComposition.cs.
- Headline numbers per report are written into DONE as a table (FY, billed, room-nights, occupancy, ADR) so the integrator's golden test (tools/meas-legacy-golden.ps1, owned by meas-study-pack) can compare against the workbooks. You do NOT read the legacy workbooks' data for anything but definitions and a spot check; the fixture is FY26 (fictional).
Ownership: `03 Prototype/src/domain/analytics/beyond-*.ts`, `src/domain/reports/study-b/*`, `tests/stream-meas-study-b.mjs`, `08 Production/src/Hcc.Reporting/Analytics/Beyond*.cs` and `Reports/MeasStudyB/*`, test files in Hcc.Reporting.Tests. Forbidden: config.ts, measure.ts bodies, types.ts, ChartBlock, other lanes.
Definition of done: all ids both sides, cross-check + golden harness green, tests per rule, tsc / npm test / server tests green, counts recorded. DONE file 05 Delivery Evidence/Measurement/MEAS-STUDY-B-DONE.md (incl. Integration needed + headline table). Final report <= 120 words. Trailer: Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-study-pack (Opus) — Phase B2, PORT 4209
```
Base: claude/meas b017cf13. Worktree E:\Projects\HCC\worktrees\meas-study-pack, branch claude/meas-study-pack.
Read first: contract §1, §3, §4, §6, §8, §9, §10; plan section 7A; catalogue rpt.study.management-pack; 08 Production/src/Hcc.Reporting (Renderers.cs, MinimalPdfWriter.cs, DocumentModel.cs) and the prototype renderer 03 Prototype/src/app/xlsx.ts + src/domain/reports/print.ts; tools/README-agents.md §1–§4.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-STUDY-PACK block. Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-study-pack\03 Prototype" -Label meas-study-pack -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-study-pack\.scratch\. Several lanes run at once: the lock queues you, so keep jobs few and meaningful (tsc + one npm test + your server tests at each milestone, not after every edit).
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4858–4859: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one. Only if you really need SQL (most report lanes need none: say so in DONE).
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 80 tool turns / about 2.5 h; then DONE and stop.

Build:
1. `ChartBlock`: a result block kind carrying a series (bar, line, stacked bar, heat-map as shaded table). PDF: bars/lines/stacks drawn as PDF shapes in the in-repo MinimalPdfWriter (no PDFsharp unless already referenced — check; no new packages); Excel: native OpenXML chart parts in the existing xlsx writers (both sides) fed from the same series; screen: inline-SVG helpers (reuse the existing ones). Screen = Excel = PDF numbers. Server in `08 Production/src/Hcc.Reporting/Charts/*`, prototype in `03 Prototype/src/domain/reports/charts/*`.
2. rpt.study.management-pack (6.22): one PDF + Excel combining the study reports' series (chapter per family, contents page, "Legacy to 30 Oct 2026 / HCC live" footer) composed from the other study reports through their selectors by id (they are not merged yet: compose by registry lookup, skip absent ids with a note) and the schedule: setting `meas.study.packSchedule` (off / monthly / quarterly) driving a scheduled run through the F7 schedule worker (read how Reporting schedules work in ReportService.cs; append only).
3. `tools/meas-legacy-golden.ps1` (this PC only; legacy data and workbooks never in the repository): runs ported reports on imported `lgc` data FY 2017-18…2026-27 and compares headline totals with the workbooks read by Python under tools/legacy/ (billed per FY, room-nights, occupancy, ADR, bad-debt total, zero-rate counts, repeat %, hall events). Tolerances 0.1 % money, 5 room-nights, 0.5 points percentages. Exit non-zero on mismatch. Write the script and its README paragraph; do not run it against real data in this lane (the integrator runs it once); a dry mode that checks the plumbing on the FY26 fixture is required.
Ownership: `08 Production/src/Hcc.Reporting/Charts/*`, `03 Prototype/src/domain/reports/charts/*`, `reports/study-pack/*`, `tools/meas-legacy-golden.ps1`, `tools/legacy/*`, `tests/stream-meas-study-pack.mjs`, server test files. Append-only: `F12-STUDY-PACK` blocks and the xlsx/pdf writer extension points (list any edit outside your folders in DONE and keep it to one small hook each). Forbidden: config.ts, measure.ts bodies, types.ts, other lanes' report files.
Definition of done: chart parity tests (a bar series renders the same values in the three outputs), pack renders on the fixture, schedule setting honoured, golden script dry mode green, tsc / npm test / Hcc.Reporting.Tests green, counts recorded. DONE file 05 Delivery Evidence/Measurement/MEAS-STUDY-PACK-DONE.md. Final report <= 120 words. Trailer: Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
