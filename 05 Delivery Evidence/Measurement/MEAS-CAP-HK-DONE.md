# F12-MEAS-CAP-HK DONE

Lane meas-cap-hk / Sonnet 5.5 / branch `claude/meas-cap-hk` / worktree `E:\Projects\HCC\worktrees\meas-cap-hk` / base tag `meas-setup` (3040fa7f). Status: built, one integration step owed (below). Nothing pushed.

## Commits
See `git log --oneline meas-setup..HEAD` (WIP commit 02fcaded, then the final commit named in the report).

## Files
Prototype: `src/domain/streams/housekeeping.ts` and `maintenance.ts` (appended `// F12-CAP-HK` blocks only), `src/services/mock/handlers/meas-hk.ts`, `src/fixtures/ext/meas-hk.ts`, `src/clients/sync/meas-hk.ts` (new, needed by the registry block), `src/features/maintenance/MetersModal.tsx`, `src/features/housekeeping/InspectOffline.tsx`, `tests/stream-meas-hk.mjs`; append-only F12-CAP-HK lines in `handlers/index.ts`, `mock/index.ts`, `clients/sync/registry.ts`.
Server: `database/migrations/4815_meas_hk_schema.sql` (+ lock entry, out-of-order accepted, + `Hcc.slnx` line), `src/Hcc.Operations/Meas/HkRules.cs`, `tests/Hcc.Meas.Tests/Hk/HkRulesTests.cs`.

## What was built
- Task times: a `TASK` wrapper in `meas-hk.ts` (the F12 spread comes after `housekeeping`) runs the real handler, then `measRecordTaskTime` writes startedAt / finishedAt / inspectedAt / inspectResult / rejectReason / attendant into `measX.taskTimes`. Skipped (never failed) when `meas.hk.productivity` is off. The handler file `handlers/housekeeping.ts` is untouched.
- `MEAS_OFFLINE_SET` (Owner, Manager, Supervisor; reason OOO / OOS / house-use / renovation, expected return, work-order link, clear), `MEAS_METER_SAVE` (Owner, Manager; four default meters electricity, water, LPG cylinders, DG hours), `MEAS_READING_ADD` (Owner, Manager, Maintenance = PERM-MEAS-002; monotonic incl. back-dated neighbours, equal allowed, photo optional, `MEAS_READING_NOT_MONOTONIC`), `MEAS_ASSETLIFE_SET` (Owner, Accountant; paise), `MEAS_DISCARD_ADD` (Owner, Manager, Supervisor).
- Every capture calls `measOn` first and throws `MEAS_OFF`; idempotent on `cmd.key`; audited by the returned text.
- DEC-061: `measAssetLifeView` removes the replacement cost for Maintenance, Housekeeping, Supervisor; no capture form shows money to them.
- `measMetersDue(s)` = the daily 08:00 checklist line for module 19 (selector only).
- Server: tables `meas.task_times`, `room_offline`, `meter`, `meter_reading`, `asset_life`, `discard` (property_id, client_id unique indexes, checks); `HkRules` twin of the rules.

## Tests
- `npx tsc --noEmit`: clean.
- `npm test` (whole suite): 2895 / 2907 pass; **before this lane 2883 (2883 pass), after 2907 (+24 `[F12-CAP-HK]`, all pass)**. The 12 failures are all caused by this lane's new commands / slice and sit in files outside my row (see Integration needed).
- Server: `HkRulesTests` (9 tests / 15 cases, names `F12_CAP_HK_*`) copied temporarily into `Hcc.Operations.Tests` and run through the lock; result: 15 passed, 0 failed. The copy was removed.
- Command lines: `Invoke-Serialized.ps1 ... -Label meas-cap-hk -Command "& '<worktree>\.scratch\<script>.ps1'"` (tsc, `npm test`, `dotnet test tests/Hcc.Operations.Tests --filter F12_CAP_HK`).

## Open items
- `measOn` is the setup stub (always true) until meas-settings merges; the MEAS_OFF tests assert only when `M.measOn` exists and answers false, so they pass vacuously today. Re-run after the merge.
- `MEAS_OFF` and `MEAS_READING_NOT_MONOTONIC` are thrown as `HccError` with the code cast (they are not in `ErrorCode`); the UI `errorTitle` has no title for them.
- Server HTTP endpoints, store and the sync loaders are NOT built (tables + rules only); the five commands are listed `unmapped` in `src/clients/sync/meas-hk.ts` with the reason. The scheduled server-side `RecordTaskTime` hook is also pending.
- UI is written but not mounted (no phone / 360 px or fit-check run, since nothing is mounted); modal components only, no new page.

## Integration needed
1. `tests/stream-rbac-matrix.mjs` `CMDS`: add MEAS_OFFLINE_SET (Owner, Manager, Supervisor), MEAS_METER_SAVE (Owner, Manager), MEAS_READING_ADD (Owner, Manager, Maintenance), MEAS_ASSETLIFE_SET (Owner, Accountant), MEAS_DISCARD_ADD (Owner, Manager, Supervisor), with payloads. This fixes the 11 RBAC failures (command without a documented role set + the per-user cascade).
2. `tests/stream-perf-store.mjs` line ~125: the TASK command now also changes slice `measX`; add it to the expected list.
3. `src/domain/errors.ts`: add `MEAS_OFF`, `MEAS_PACK_OFF`, `MEAS_READING_NOT_MONOTONIC` to `ErrorCode` and `errorTitle`; export `measOn` from `services/mock/index.ts` (meas-settings).
4. `src/features/maintenance/register.tsx`: modals `meas-reading` -> `MeterReadingModal`, `meas-meters` -> `MetersModal`, `meas-assetlife` -> `MeterAssetLifeModal`; `src/features/housekeeping/register.tsx`: `meas-offline` -> `InspectOfflineModal`, `meas-discard` -> `InspectDiscardModal`; show `<InspectTimes taskId>` on the inspection card; handover checklist (module 19) line from `measMetersDue`.
5. `Hcc.Meas.Tests` (meas-settings) must reference `Hcc.Operations` so `tests/Hcc.Meas.Tests/Hk/HkRulesTests.cs` compiles (the file is already in the folder).
6. Server wiring: `IMeasurementGate` on the capture endpoints (keys `meas.hk.ooo`, `.utilities`, `.assets`, `.linen`, `.productivity`), 4815 is the only migration used (4816-4819 free).

## HANDOFF bullet
F12 meas-cap-hk: task-time recording (TASK wrapper), room offline reasons, meters + monotonic readings (PERM-MEAS-002), asset life with the DEC-061 cost mask, linen discards; migration 4815 + `HkRules`; 24 new prototype tests. Endpoints / sync loaders and UI mounting owed at integration; RBAC matrix and PERF slice tests need the new commands / `measX` added.
