# F12-MEAS-SETTINGS DONE

Lane meas-settings · Opus · branch `claude/meas-settings` · worktree `E:\Projects\HCC\worktrees\meas-settings` · base tag `meas-setup` (3040fa7f).

## Commits
See `git log --oneline meas-setup..claude/meas-settings` (f1bcbb71 build, c76249f7 panel + pinned config tests, fa5ffe71 egress-ui / RBAC table, then the DONE commit).

## Files
Prototype (`03 Prototype`)
- `src/domain/streams/measure.ts` (bodies): `MEAS_PACKS`, `MEAS_ITEMS` (56), `MEAS_TARGETS` (13, M-9), `measOn`, `measTarget`, `measOffReason`, `measIsCore`, `measPackOn`, `measPackCount`, `measGuard` (throws `MEAS_OFF`), `MEAS_REPORT_KEY` + `registerMeasReport`, `measReportOn`, `measReportGuard`, `measFilter`, `measScheduleSkips`, `measOffKeys`, `measSettingDefs` + the `registerSettingsGroup({id:'measure',…})` call.
- `src/domain/streams/config.ts`: `'measure'` in `CfgGroupId` and one `CFG_GROUPS` row (Software and security, `money:false`).
- `src/features/admin/config/MeasurePanel.tsx` (new): `MeasurePanel` (pack rows, Power button, "n of m on" pill, expandable items with report id, governing permission, "Capture needed", "Core — always on") and `MeasureTargets` (editor with "FY 2025-26 actual" hint; rupees in, paise stored).
- `src/domain/reports/engine.ts`: import line, `reportCatalogue` filter `measReportOn(s,d.id)&&`, `runDef` first line `measReportGuard(s,def.id)`. `catalogue.ts` unchanged (see Integration needed 3).
- `src/services/mock/handlers/meas-settings.ts` (new handler file, as every lane's block expects): `MEAS_PACK_SET`, `MEAS_ITEM_SET`.
- Append-only: `handlers/index.ts` F12-SETTINGS (import + spread), `services/mock/index.ts` F12-SETTINGS (`export * from measure`).
- Tests: `tests/stream-meas-settings.mjs` (14 `[F12-SETTINGS]` tests); `tests/stream-config.mjs` pinned group list 13 → 14 and search count 13 → 14; `tests/stream-egress-ui.mjs` search count 13 → 14; `tests/stream-rbac-matrix.mjs` CMDS rows for the two new commands (outside the literal ownership row, needed for the new commands).

Server (`08 Production`)
- `src/Hcc.Application/Configuration/MeasurementCatalogue.cs` (static key table, targets, `Definitions()`, `Provider()`), `MeasurementGate.cs` (`IMeasurementGate`, `MeasurementGate`, `IMeasuredReport`, `HccMeasGateMetadata`).
- `src/Hcc.Api/Endpoints/MeasurementRouteFilters.cs` (new): `RequireHccMeasurement(key)` (Hcc.Application has no ASP.NET reference, so the route helper lives beside `RequireHccModule`).
- `src/Hcc.Reporting/ReportRunner.cs` (the one small edit): optional ctor param `IMeasurementGate? measurement = null`; `CatalogueAsync` skips an off `IMeasuredReport`; `RunAsync` calls `EnsureOnAsync` before the permission check. `ReportCatalogue.cs` unchanged (not needed: the runner reads the selector).
- `src/Hcc.Api/HccComposition.cs` F12-SETTINGS services: `IMeasurementGate` singleton; the `measure` provider registered only when `ConfigCatalog.Group("measure")` exists.
- `database/migrations/4802_meas_rbac_seed.sql` (PERM-MEAS-001 OWN A / MGR V / TAD V; PERM-MEAS-002 OWN, MGR, REC, MNT), `4803_meas_settings_seed.sql` (no value rows: defaults live in code; refuses if a core item is stored off). Lock: `--add --accept-out-of-order` with reason; `migrations-lock.mjs` in step (224).
- `Hcc.slnx` F12-SETTINGS: project line `tests/Hcc.Meas.Tests` + the two migration `File` lines.
- `tests/Hcc.Meas.Tests` (new project, 10 tests): defaults, item off, pack off, core, module tie, MEAS_OFF details, unknown key, counts, owner-only definitions, route metadata, parity with `measure.ts`.

## What was built
- Contract §1.1/§1.2: group `measure` "Measurement reports"; 6 pack + 56 item switches (default on), 13 targets (M-9: occupancy 35, ADR 205000 paise, NPS 30, repeat 25, late departure 10, OT 10, high balance 500000, cash variance 10000, check-in 8, check-out 6, absenteeism 5, attrition 30, loss 2), `meas.study.packSchedule` (monthly), `meas.daily.blockClose` (off), `meas.regrets.mandatory` (off), `yld.overbook.<type>` (0, one per room type in the state), `yld.restrictionsEnforce` (off), `ffe.reservePct` (3). All Owner, approver Owner, `money:true` (so DEC-058 denies Tech admin; Manager has no configuration rights), immediate, exported / imported in the configuration JSON, history in `configHistory` (feeds `rpt.admin.config-history`).
- Contract §1.3: `measOn` = module (items tied to maintenance / inventory / payroll) AND (core OR (pack AND item)). Core = flash, inventory, and high-balance while Groups & companies (company credit) is on.
- Contract §1.4: `MEAS_PACK_SET` writes one change row per item whose effective state changes (core never); `MEAS_ITEM_SET` refuses `MEAS_PACK_OFF` for item-on under an off pack and VALIDATION for a core item off. Pack / item settings are module-store definitions, so `SETTING_SET`, `CFG_IMPORT`, `CFG_REVERT` and `CFG_DECIDE` run the same rules.
- Contract §1.5: prototype `reportCatalogue` hides and `runReport` / `runDef` refuses (`MEAS_OFF`) ids in `MEAS_REPORT_KEY` (seeded with the 54 report ids from §3.10; lanes add more with `registerMeasReport`). Server `MeasurementGate` + `IMeasuredReport` honoured by `ReportRunner`.
- Capture gating helper: `measGuard(s,key)` (capture lanes call it first in each command and before showing a form).

## Tests
All through `E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1 -Label meas-settings` (logs in `.scratch\`), except one early `npx tsc --noEmit` run directly (green; repeated through the lock).
- `dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --logger trx`: 0 → 10 passed, 0 failed.
- `dotnet test tests/Hcc.Reporting.Tests -maxcpucount:1 --logger trx` (runner edit): 83 passed, 0 failed (unchanged count; optional gate param, existing tests unaffected).
- `npx tsc --noEmit`: clean. `npm test`: 2882 → 2896 passed, 0 failed (+14 `[F12-SETTINGS]`). First full run had 12 expected failures: pinned group count in `stream-egress-ui.mjs` (13 → 14) and the RBAC command table in `stream-rbac-matrix.mjs` (MEAS_PACK_SET / MEAS_ITEM_SET added as OWN, PERM-MEAS-001); both test files updated (pinned / documentation rows only), then green. One run returned exit 75 twice (lock busy) and was retried.
- Fit-screen check: not run (no page owned; the panel is a section of the existing settings FitPage once mounted).

## Open items
- Contract §1.5 says `Guid propertyId`; the code's `IModuleGate` uses `int`, so `IMeasurementGate` uses `int` (mirror). Server refusal is FORBIDDEN with `details.reason = MEAS_OFF` and `details.key` (HccErrorCode has no MEAS_OFF), like MODULE_OFF.
- Contract §1.1 says `store:{kind:'value'}`; the prototype has no such kind. Targets / other settings use `kind:'config'` (= `s.configX.values`); pack / item switches use a module store reading the same `configX.values` keys so the rules run on every path.
- Percent targets and `ffe.reservePct` are stored as whole percent integers (35, not basis points), as the contract's numbers read.
- The night-audit auto-tick skip for off items belongs to meas-rpt-daily (`measNightAuditLines` should filter with `measOn`).

## Integration needed
1. `03 Prototype/src/domain/errors.ts`: add `|'MEAS_OFF'|'MEAS_PACK_OFF'` to `ErrorCode` and `MEAS_OFF:'Measurement switched off',MEAS_PACK_OFF:'Pack switched off'` to `errorTitle` (measure.ts casts until then).
2. `03 Prototype/src/features/admin/HotelSettings.tsx` (after line 86 `{g==='modules'&&<CfgModulesPanel/>}`) and `src/mobile/admin/Settings.tsx` (after line 48): `{g==='measure'&&<MeasurePanel/>}` with `import {MeasurePanel} from './config/MeasurePanel';` (mobile: `'../../features/admin/config/MeasurePanel'`). Until then the group shows its settings in the generic list.
3. `03 Prototype/src/features/reports/hub-engine.ts` line 9: `allReports().filter(d=>measReportOn(s,d.id)&&canViewReport(s,uid,d))` (import `measReportOn` from `../../domain/streams/measure`). The search palette report entries, phone report tabs and scheduled runs should filter the same way (`measReportOn` / `measScheduleSkips`); the Owner digest uses `measOn` / `measOffKeys`.
4. Server `src/Hcc.Domain/Configuration/ConfigCatalog.cs`: add group `measure` "Measurement reports" (section Software and security, money false) after `data`; then the provider registers itself and `MeasurementGate` reads stored values (until then everything reads on). Optional: `HccErrorCode.MEAS_OFF`.
5. Report id pattern: `catalogue.ts` `ID` and `ReportCatalogue.IdPattern` do not allow areas `revenue` / `study` (catalogue §3.10) — report lanes / integrator.
6. `Hcc.slnx`: the F12-SETTINGS block sits in the `/database/` folder, so the `Hcc.Meas.Tests` project line is listed there (builds normally); move it to `/tests/` when sorting.

## HANDOFF bullet
F12 meas-settings: Master configuration group `measure` with 6 pack / 56 item switches, 13 M-9 targets and the yield / FF&E / study-pack settings (Owner only); `measOn` (module AND pack AND item, core always on), `measGuard` (MEAS_OFF), `MEAS_PACK_SET` / `MEAS_ITEM_SET` (MEAS_PACK_OFF), report hub / run filter, server `MeasurementGate` + `IMeasuredReport` in `ReportRunner`, migrations 4802 / 4803, new `Hcc.Meas.Tests`. Integration items in `05 Delivery Evidence/Measurement/MEAS-SETTINGS-DONE.md`.
