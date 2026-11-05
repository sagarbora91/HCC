# MEAS briefs — wave F12 (5 Nov 2026)

Base: `claude/meas` setup commit **tagged meas-setup; hash in MEAS-LEDGER.md**. Contract: `02 Planning/agents/MEAS-CONTRACT.md`. Template: `E:\Projects\HCC\02 Planning\HCC_Wave_Plan_F1-Fn_2026-11-02.md` Appendix A (instantiated per lane below). Ledger: `05 Delivery Evidence\Measurement\MEAS-LEDGER.md`. **No lane is launched without the owner's "go".** Lanes are cut as worktrees `E:\Projects\HCC\worktrees\meas-<lane>` on `claude/meas-<lane>`, each with the `03 Prototype\node_modules` junction.

## Lane: meas-settings (Opus) — Phase A, PORT 4191
```
Base: claude/meas setup commit tagged meas-setup; hash in MEAS-LEDGER.md. Worktree E:\Projects\HCC\worktrees\meas-settings, branch claude/meas-settings.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §2, §5, §6, §8; tools/README-agents.md §1–§4; the files in your ownership row. Do not read HANDOFF.md, CLAUDE.md or the Decision Register — the rules you need are here and in the contract.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-SETTINGS block (list below). Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-settings\03 Prototype" -Label meas-settings -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-settings\.scratch\.
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards / no sideways scroll; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4802–4809: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one.
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/measure.ts` (bodies), `src/domain/streams/config.ts` (the `measure` group only), `src/features/admin/config/MeasurePanel*`, `src/domain/reports/catalogue.ts` + `engine.ts` (ONE filter call each, listed in DONE), `tests/stream-meas-settings.mjs`, server `Hcc.Application/Configuration/Measurement*`, the new `Hcc.Meas.Tests` project (+ its `Hcc.slnx` line), `Hcc.Reporting` `ReportCatalogue` / `ReportRunner` (the `IMeasuredReport` honour, one small edit).
Append-only: `F12-SETTINGS` in handlers/index.ts, mock/index.ts, registry.ts, HccComposition.cs (services + endpoints), Hcc.slnx. Migrations: 4802–4809. Forbidden: any capture UI; report files; money postings; `types.ts`.

Build (contract sections):
- Contract §1 in full: group `measure`, all pack / item / target / other settings with the frozen keys, `MEAS_ITEMS`, `measOn`, `measTarget`, `registerMeasReport(id,key)` + `MEAS_REPORT_KEY`, `MEAS_PACK_SET` (one change row per item), the "Core — always on" rule.
- Surfaces: hub / catalogue filter, search palette, phone tabs, scheduled runs, digest hook (`measOn` exported for the digest lane), capture-form gating helper `measGuard(k,key)` that throws `MEAS_OFF`.
- Panel UI (desktop `FitPage` section + phone cards): pack rows with Power button and "n of m on" pill, expandable items with report id, governing permission and "Capture needed" note; targets editor with the "FY 2025-26 actual" hint.
- Server: `IMeasurementGate`, `MeasurementGate`, `MeasurementCatalogue` (static key table), `RequireHccMeasurement`, `HccMeasGateMetadata`, `IMeasuredReport` honoured by `ReportRunner` / `ReportCatalogue`; migrations 4802 RBAC seed (PERM-MEAS-001 / 002) and 4803 settings seed.
- Configuration export / import carries the switches; parity test: TypeScript key table = C# `MeasurementCatalogue`.

Definition of done: every surface hides an off item; the server refuses with `MEAS_OFF`; tests for the three-level rule (module off, pack off, item off, core always on, enabling an item under an off pack refused); the pinned config-group tests updated for the new group. Tests tests/stream-meas-settings.mjs ([F12-SETTINGS] names) for every guard; npx tsc --noEmit, npm test and your server tests green; counts before / after recorded.

Test commands:
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-settings\03 Prototype" -Label meas-settings -Command "npx tsc --noEmit"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-settings\03 Prototype" -Label meas-settings -Command "npm test"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-settings\08 Production" -Label meas-settings -Command "dotnet test tests/<Project> -maxcpucount:1 --logger trx"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-settings\03 Prototype" -Label meas-settings -Command "node tools/fit-screen-check.mjs --pages <pages>"   (PORT=4191; only if you own a page)

DONE file: 05 Delivery Evidence/Measurement/MEAS-SETTINGS-DONE.md (template tools/README-agents.md §4). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Lane: meas-cap-desk (Sonnet) — Phase A, PORT 4192
```
Base: claude/meas setup commit tagged meas-setup; hash in MEAS-LEDGER.md. Worktree E:\Projects\HCC\worktrees\meas-cap-desk, branch claude/meas-cap-desk.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §2, §5, §6, §8; tools/README-agents.md §1–§4; the files in your ownership row. Do not read HANDOFF.md, CLAUDE.md or the Decision Register — the rules you need are here and in the contract.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-CAP-DESK block (list below). Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-desk\03 Prototype" -Label meas-cap-desk -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-cap-desk\.scratch\.
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards / no sideways scroll; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4810–4814: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one.
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/features/reservations/Turnaway*`, `src/features/folio/Upsell*`, `src/features/customers/Preferences*`, `src/domain/streams/turnaway.ts`, `src/services/mock/handlers/meas-desk.ts`, `src/fixtures/ext/meas-desk.ts`, `tests/stream-meas-desk.mjs`, server `Hcc.Operations/Meas/Desk*` + test files in the project meas-settings creates.
Append-only: `F12-CAP-DESK` in handlers/index.ts, mock/index.ts, registry.ts, HccComposition.cs, Hcc.slnx. Migrations: 4810–4814. Forbidden: `config.ts`, `measure.ts` bodies, any report file, folio money logic (item codes UPG / LCO1 / LCO2 go through the existing add-line command only).

Build (contract sections):
- §2 rows: `MEAS_TURNAWAY_LOG` (one button on the reservation search "Could not book": reason, arrival, nights, type, quoted rate, channel; 10-second form), `MEAS_WALKED_LOG`, `MEAS_ALLOWANCE_REASON_SAVE`, `MEAS_BOOKING_EXTRA_SET`, `MEAS_PREFERENCE_SET` (+ "known guest" badge on the arrivals list with stays and last visit; blacklist with reason + approver).
- Folio items `UPG`, `LCO1` (Rs 300 after 14:00), `LCO2` (Rs 600 after 18:00); booked type kept when the assigned type differs; waiver needs Manager.
- Every command first checks `measOn` (stub until meas-settings merges) and refuses `MEAS_OFF`; idempotent on client id; audited.
- Server mirror tables `meas.turnaway`, `walked`, `allowance_reason`, `booking_extra`, `customer_preference` with RBAC; sync `AREAS` entry.
- Phone forms at 360 px, 48 px targets.

Definition of done: commands and tests green; phone forms at 360 px; fit-check of any new page at the five viewports. Tests tests/stream-meas-desk.mjs ([F12-CAP-DESK] names) for every guard; npx tsc --noEmit, npm test and your server tests green; counts before / after recorded.

Test commands:
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-desk\03 Prototype" -Label meas-cap-desk -Command "npx tsc --noEmit"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-desk\03 Prototype" -Label meas-cap-desk -Command "npm test"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-desk\08 Production" -Label meas-cap-desk -Command "dotnet test tests/<Project> -maxcpucount:1 --logger trx"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-desk\03 Prototype" -Label meas-cap-desk -Command "node tools/fit-screen-check.mjs --pages <pages>"   (PORT=4192; only if you own a page)

DONE file: 05 Delivery Evidence/Measurement/MEAS-CAP-DESK-DONE.md (template tools/README-agents.md §4). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-cap-hk (Sonnet) — Phase A, PORT 4193
```
Base: claude/meas setup commit tagged meas-setup; hash in MEAS-LEDGER.md. Worktree E:\Projects\HCC\worktrees\meas-cap-hk, branch claude/meas-cap-hk.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §2, §5, §6, §8; tools/README-agents.md §1–§4; the files in your ownership row. Do not read HANDOFF.md, CLAUDE.md or the Decision Register — the rules you need are here and in the contract.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-CAP-HK block (list below). Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-hk\03 Prototype" -Label meas-cap-hk -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-cap-hk\.scratch\.
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards / no sideways scroll; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4815–4819: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one.
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/housekeeping.ts` and `maintenance.ts` (append inside `F12-CAP-HK` only), `src/features/maintenance/Meters*`, `src/features/housekeeping/Inspect*`, `src/services/mock/handlers/meas-hk.ts`, `src/fixtures/ext/meas-hk.ts`, `tests/stream-meas-hk.mjs`, server `Hcc.Operations/Meas/Hk*`.
Append-only: `F12-CAP-HK` in handlers/index.ts, mock/index.ts, registry.ts, HccComposition.cs, Hcc.slnx; inside housekeeping.ts / maintenance.ts. Migrations: 4815–4819. Forbidden: `config.ts`, report files, anything that shows money to Housekeeping or Maintenance (DEC-061).

Build (contract sections):
- Task times: the existing start / complete / inspect actions call `measRecordTaskTime` (startedAt, finishedAt, inspectedAt, inspectResult, rejectReason, attendant) into `measX.taskTimes`.
- `MEAS_OFFLINE_SET` (reason OOO / OOS / house-use / renovation, expected return, work-order link) for `roomoffline`.
- Meters: `MEAS_METER_SAVE` master (electricity main, water, LPG cylinders, diesel / DG hours), `MEAS_READING_ADD` (PERM-MEAS-002; monotonic per meter else `MEAS_READING_NOT_MONOTONIC`; photo optional) + daily 08:00 checklist line for module 19.
- `MEAS_ASSETLIFE_SET` (expected life, condition grade, replacement cost; Owner / Accountant), `MEAS_DISCARD_ADD` (linen discard reason).
- Server tables `meas.task_times`, `room_offline`, `meter`, `meter_reading`, `asset_life`, `discard`; sync area.

Definition of done: readings validated (monotonic, photo optional); DEC-061 money mask kept; phone forms at 360 px; module off (maintenance / inventory) hides the matching captures. Tests tests/stream-meas-hk.mjs ([F12-CAP-HK] names) for every guard; npx tsc --noEmit, npm test and your server tests green; counts before / after recorded.

Test commands:
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-hk\03 Prototype" -Label meas-cap-hk -Command "npx tsc --noEmit"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-hk\03 Prototype" -Label meas-cap-hk -Command "npm test"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-hk\08 Production" -Label meas-cap-hk -Command "dotnet test tests/<Project> -maxcpucount:1 --logger trx"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-hk\03 Prototype" -Label meas-cap-hk -Command "node tools/fit-screen-check.mjs --pages <pages>"   (PORT=4193; only if you own a page)

DONE file: 05 Delivery Evidence/Measurement/MEAS-CAP-HK-DONE.md (template tools/README-agents.md §4). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-cap-guest (Sonnet) — Phase A, PORT 4194
```
Base: claude/meas setup commit tagged meas-setup; hash in MEAS-LEDGER.md. Worktree E:\Projects\HCC\worktrees\meas-cap-guest, branch claude/meas-cap-guest.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §2, §5, §6, §8; tools/README-agents.md §1–§4; the files in your ownership row. Do not read HANDOFF.md, CLAUDE.md or the Decision Register — the rules you need are here and in the contract.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-CAP-GUEST block (list below). Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-guest\03 Prototype" -Label meas-cap-guest -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-cap-guest\.scratch\.
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards / no sideways scroll; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4820–4824: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one.
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/services.ts` (append inside `F12-CAP-GUEST` only), `src/features/services/Survey*`, `src/features/customers/Loyalty*`, website module page `survey` (off when the website module is off), `src/services/mock/handlers/meas-guest.ts`, `src/fixtures/ext/meas-guest.ts`, `tests/stream-meas-guest.mjs`, server `Hcc.Operations/Meas/Guest*`.
Append-only: `F12-CAP-GUEST` in handlers/index.ts, mock/index.ts, registry.ts, HccComposition.cs, Hcc.slnx; inside services.ts. Migrations: 4820–4824. Forbidden: `config.ts`, report files, storing any ID data or photo with a survey.

Build (contract sections):
- `MEAS_SURVEY_ADD`: five scores 1–5, recommend 0–10, comment, consent flag; channels: QR card opening a LAN page (no login, rate-limited), desk entry on behalf of the guest (PERM-MEAS-002); WhatsApp link only when F9 egress is on (not built here).
- `MEAS_REVIEW_SAVE` (monthly per platform: count, average, 12-month score, responded, themes).
- `MEAS_ROOTCAUSE_LIST_SAVE` / `MEAS_ROOTCAUSE_SET` on complaints; `MEAS_LOYALTY_SET` tag on the customer.
- Server tables `meas.survey`, `review_month`, `root_cause`; sync area.

Definition of done: a survey cannot store ID data; consent enforced; the QR page honours module + item off; phone form at 360 px. Tests tests/stream-meas-guest.mjs ([F12-CAP-GUEST] names) for every guard; npx tsc --noEmit, npm test and your server tests green; counts before / after recorded.

Test commands:
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-guest\03 Prototype" -Label meas-cap-guest -Command "npx tsc --noEmit"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-guest\03 Prototype" -Label meas-cap-guest -Command "npm test"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-guest\08 Production" -Label meas-cap-guest -Command "dotnet test tests/<Project> -maxcpucount:1 --logger trx"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-guest\03 Prototype" -Label meas-cap-guest -Command "node tools/fit-screen-check.mjs --pages <pages>"   (PORT=4194; only if you own a page)

DONE file: 05 Delivery Evidence/Measurement/MEAS-CAP-GUEST-DONE.md (template tools/README-agents.md §4). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-cap-people (Sonnet) — Phase A, PORT 4195
```
Base: claude/meas setup commit tagged meas-setup; hash in MEAS-LEDGER.md. Worktree E:\Projects\HCC\worktrees\meas-cap-people, branch claude/meas-cap-people.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §2, §5, §6, §8; tools/README-agents.md §1–§4; the files in your ownership row. Do not read HANDOFF.md, CLAUDE.md or the Decision Register — the rules you need are here and in the contract.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-CAP-PEOPLE block (list below). Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-people\03 Prototype" -Label meas-cap-people -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-cap-people\.scratch\.
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards / no sideways scroll; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4825–4829: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one.
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/staff.ts` (append inside `F12-CAP-PEOPLE` only), `src/features/staff/Training*`, `src/features/staff/Pulse*`, `src/services/mock/handlers/meas-people.ts`, `src/fixtures/ext/meas-people.ts`, `tests/stream-meas-people.mjs`, server `Hcc.Operations/Meas/People*`.
Append-only: `F12-CAP-PEOPLE` in handlers/index.ts, mock/index.ts, registry.ts, HccComposition.cs, Hcc.slnx; inside staff.ts. Migrations: 4825–4829. Forbidden: `config.ts`, report files, payroll calculations.

Build (contract sections):
- `MEAS_TRAINING_SAVE` (staff, course, date, hours, certificate no, valid to, evidence via the LF documents store, mandatory flag) + certificate-expiry alert (30 days) through the existing alerts.
- `MEAS_EXIT_SET` (exit reason list + date).
- `MEAS_PULSE_ADD`: anonymous five-question form, department only, **no staff id stored**, quarter key.
- Server tables `meas.training`, `staff_exit`, `pulse`; sync area.

Definition of done: HR-001 gate; payroll module off hides all three; a test proves no staff id is stored with a pulse answer. Tests tests/stream-meas-people.mjs ([F12-CAP-PEOPLE] names) for every guard; npx tsc --noEmit, npm test and your server tests green; counts before / after recorded.

Test commands:
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-people\03 Prototype" -Label meas-cap-people -Command "npx tsc --noEmit"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-people\03 Prototype" -Label meas-cap-people -Command "npm test"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-people\08 Production" -Label meas-cap-people -Command "dotnet test tests/<Project> -maxcpucount:1 --logger trx"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-cap-people\03 Prototype" -Label meas-cap-people -Command "node tools/fit-screen-check.mjs --pages <pages>"   (PORT=4195; only if you own a page)

DONE file: 05 Delivery Evidence/Measurement/MEAS-CAP-PEOPLE-DONE.md (template tools/README-agents.md §4). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```

## Lane: meas-forecast (Opus) — Phase A, PORT 4196
```
Base: claude/meas setup commit tagged meas-setup; hash in MEAS-LEDGER.md. Worktree E:\Projects\HCC\worktrees\meas-forecast, branch claude/meas-forecast.
Read first (only these): 02 Planning/agents/MEAS-CONTRACT.md §1, §2, §5, §6, §8; tools/README-agents.md §1–§4; the files in your ownership row; the plan §4 (domain 2). Do not read HANDOFF.md, CLAUDE.md or the Decision Register — the rules you need are here and in the contract.

Rules (8 lines):
1. Edit only the files in your ownership row; shared files only by appending inside your // F12-FORECAST block (list below). Anything else -> DONE "Integration needed" with the exact edit.
2. Every build / test / check through pwsh -NoProfile -File "E:\Projects\HCC\worktrees\meas\tools\Invoke-Serialized.ps1" -WorkingDirectory "E:\Projects\HCC\worktrees\meas-forecast\03 Prototype" -Label meas-forecast -Command "..."; exit 75 = busy -> retry the same call; never filter output; logs in E:\Projects\HCC\worktrees\meas-forecast\.scratch\.
3. Run only your test project(s) and npm test; never dotnet test Hcc.slnx; no emulator.
4. Money = integer paise; GST from the dated master; simulated clock; fictional data; Owner-only money settings (DEC-058); Housekeeping / Maintenance see no money (DEC-061); every new desktop page is a FitPage (DEC-194..196), never a smaller font; phone 48 px / 16 px / cards / no sideways scroll; colours as tokens; no new npm packages; SCHEMA stays 15.
5. Migrations only 4830–4834: node "08 Production/tools/migrations-lock.mjs" --add --accept-out-of-order + a Hcc.slnx line inside your block; never edit an applied one.
6. STOP and write DONE if: contract differs from code / DEC; you must touch money, GST, a posting, a permission, a setting or types.ts beyond your row; a test fails twice after a fix; exit 75 five times; a file outside your row needs a change.
7. Commit after every coherent step (<= 1 h uncommitted); never push; never bare git stash; never touch .claude/launch.json.
8. Caps: <= 60 tool turns / about 2 h; then DONE and stop.

Ownership: `src/domain/streams/yield.ts` (append inside `F12-FORECAST` only), `src/domain/forecast.ts`, `src/features/yield/Events*`, `RateShop*`, `Displacement*`, `src/services/mock/handlers/meas-forecast.ts`, `src/fixtures/ext/meas-forecast.ts`, `tests/stream-meas-forecast.mjs`, server `Hcc.Yield/Meas*` (or the existing yield project).
Append-only: `F12-FORECAST` in handlers/index.ts, mock/index.ts, registry.ts, HccComposition.cs, Hcc.slnx; inside yield.ts. Migrations: 4830–4834. Forbidden: `config.ts` (the `yld.overbook.*` and `yld.restrictionsEnforce` keys are declared by meas-settings; you read them), report files, changing yield guard behaviour when the new settings are at their defaults.

Build (contract sections):
- 2.2 real forecast curve: same-night occupancy for the last two years by weekday from `lgc` history + FY26 bookings, pickup pace, event uplift; Owner override kept; replaces the simulated curve only behind `meas.rev.forecast`.
- 2.1 `MEAS_EVENT_SAVE` / `_DELETE` master; 2.3 `MEAS_OTB_SNAPSHOT` (Monday 06:00 scheduled run + back-fill from bookings createdAt / cancelledAt); 2.4 displacement calculator (pure function); 2.6 restriction counters + `yld.restrictionsEnforce`; 2.7 `MEAS_COMPETITOR_SAVE` / `MEAS_RATESHOP_ADD` entry form; overbooking uses `yld.overbook.<type>`.
- Server tables `meas.event`, `otb_snapshot`, `competitor`, `rate_shop`, `restriction_counter`; the scheduled job runs through the F7 schedule worker.
- Money: integer paise; the displacement and forecast rate maths are explained in DONE for the integrator.

Definition of done: forecast equals the legacy study FY 2025-26 occupancy within 2 points when back-tested on the FY26 fixture; yield guards unchanged at defaults (existing yield tests green). Tests tests/stream-meas-forecast.mjs ([F12-FORECAST] names) for every guard; npx tsc --noEmit, npm test and your server tests green; counts before / after recorded.

Test commands:
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-forecast\03 Prototype" -Label meas-forecast -Command "npx tsc --noEmit"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-forecast\03 Prototype" -Label meas-forecast -Command "npm test"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-forecast\08 Production" -Label meas-forecast -Command "dotnet test tests/<Project> -maxcpucount:1 --logger trx"
  ...Invoke-Serialized.ps1 -WorkingDirectory "E:\Projects\HCC\worktrees\meas-forecast\03 Prototype" -Label meas-forecast -Command "node tools/fit-screen-check.mjs --pages <pages>"   (PORT=4196; only if you own a page)

DONE file: 05 Delivery Evidence/Measurement/MEAS-FORECAST-DONE.md (template tools/README-agents.md §4). Write it even if you stop early.
Final report to the coordinator: <= 120 words — DONE path, commit hash, counts, open items. No code.
Commit trailer (every commit): Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Phase B, B2 and C — outlines (full briefs are written when Phase A is merged into `claude/meas`, from the same template)

| Lane | Model | Builds | Owns | Migr. |
|---|---|---|---|---|
| meas-rpt-daily | Sonnet | catalogue 1.1, 1.2, 1.3–1.9 (+ `measNightAuditLines`) | `reports/meas-daily/*` both sides | 4835–4836 |
| meas-rpt-revenue | Sonnet | 2.1, 2.3, 2.5, 2.6, 2.7 + hub area `revenue`; 2.4 report shell | `reports/meas-revenue/*` | 4837–4838 |
| meas-rpt-guest | Sonnet | 3.1, 3.2, 3.3, 3.5, 3.6, 3.7 | `reports/meas-guest/*` | 4839–4840 |
| meas-rpt-hk | Sonnet | 4.1–4.6 | `reports/meas-hk/*` | 4841–4842 |
| meas-rpt-people | Sonnet | 5.1–5.5 | `reports/meas-people/*` | 4843–4844 |
| meas-digest | Sonnet | Owner digest "Measurement" block, subscription rows | `features/overview/Digest*` (append) | — |
| meas-study-a | Sonnet | 6.1–6.9 | `domain/analytics/legacy-*.ts`, `reports/study-a/*` | 4850–4853 |
| meas-study-b | Sonnet | 6.10–6.21 | `domain/analytics/beyond-*.ts`, `reports/study-b/*` | 4854–4857 |
| meas-study-pack | **Opus** | `ChartBlock`, 6.22 pack, schedule, `tools/meas-legacy-golden.ps1` | `Hcc.Reporting/Charts/*`, `reports/study-pack/*` | 4858–4859 |
| meas-review | Sonnet (read-only) | findings per Phase B lane | review files | — |
| f12-int | **Opus** | merge order settings → captures → forecast → reports → study-a → study-b → study-pack → digest; golden once; catalogue statuses; FY26 re-pin once; full suites; builds; fit-check; MEAS-DONE.md; gate; ff main; tag | — | 4860–4869 |
| f12-docs | Sonnet | HANDOFF, CLAUDE.md, DEC rows (from DEC-267), catalogue counts, memory, graph + vault | docs | — |

Phase B/B2 lanes: reports are the catalogue §3.10 entries; screen = Excel = PDF on the FY26 fixture, exception rules tested, money flags per column, `registerMeasReport` + `IMeasuredReport` for the switch, DEC-061 columns removed for Housekeeping / Maintenance.
