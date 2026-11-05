# F12-MEAS-CAP-PEOPLE DONE

Lane meas-cap-people / Sonnet 5.5 / branch `claude/meas-cap-people` / worktree `E:\Projects\HCC\worktrees\meas-cap-people` / base tag `meas-setup` (3040fa7f).

## Commits
See `git log --oneline meas-setup..HEAD` (commit hash reported to the coordinator).

## Files
Prototype (`03 Prototype`):
- `src/domain/streams/staff.ts` (appended `F12-CAP-PEOPLE` block at the end: `hrMeas*` helpers, `HR_MEAS_*` constants)
- `src/services/mock/handlers/meas-people.ts` (MEAS_TRAINING_SAVE, MEAS_EXIT_SET, MEAS_PULSE_ADD), `src/fixtures/ext/meas-people.ts`
- `src/features/staff/Training.tsx` (`MeasTrainingModal`, `MeasExitModal`), `src/features/staff/Pulse.tsx` (`MeasPulseModal`)
- `src/clients/sync/meas-people.ts` (commands only, no loader: `measX` is shared by six lanes)
- `tests/stream-meas-people.mjs` (10 tests, `[F12-CAP-PEOPLE]`)
- append-only blocks: `handlers/index.ts`, `services/mock/index.ts`, `clients/sync/registry.ts`
Server (`08 Production`): `database/migrations/4825_meas_people_training.sql`, `4826_meas_people_exit_pulse.sql` (+ `migrations.lock.json`, `Hcc.slnx` lines in the `F12-CAP-PEOPLE` block), `src/Hcc.Operations/Meas/PeopleRules.cs`, `tests/Hcc.Operations.Tests/Meas/PeopleRulesTests.cs`.

## What was built
- `MEAS_TRAINING_SAVE` (staff, course, date, hours, certificate no., valid to, evidence ref, mandatory flag; new or edit by `id`). HR-001 gate = `hrCanManage` (Owner, Manager). A valid-to date writes/updates/removes an existing desk reminder `RMD-MEAS-<trainingId>` (role Manager, lead 30 days), which is the "certificate-expiry alert through the existing alerts". `hrMeasTrainingExpiring(s,days)` and `hrMeasMandatoryLapsed(s)` are pure selectors for reports/digest.
- `MEAS_EXIT_SET` (reason from a fixed list of 11 + date; empty reason clears). Stored in `s.measX.exit{staffId}`; the staff record and payroll are not touched.
- `MEAS_PULSE_ADD`: four 1-5 scores + recommend 0-10, department id chosen on the form, quarter key = FY quarter (`2026-27-Q2`). The record has exactly: id, quarter, dept, five scores. No staff id, user id or name; the form idempotency key is a random uuid.
- Every command calls `hrMeasGuard` first: Payroll module off, or `measOn` false for `meas.people.training | attrition | pulse` => `MEAS_OFF` (before the permission check). Data is kept.
- Server: `meas.training` (+ `meas.training_audit`), `meas.staff_exit`, `meas.pulse` (no actor column, no audit table, by design); `PeopleRules` mirrors the prototype rules; sync mapper sends no identity for the pulse.

## Tests
Before/after counts are in the section below (filled from the lock-serialized runs; logs in `.scratch\`).
- `node tests/_one.mjs tests/stream-meas-people.mjs` (through Invoke-Serialized): 10 tests.
- `npx tsc --noEmit`: clean.
- `npm test` and `dotnet test tests/Hcc.Operations.Tests -maxcpucount:1 --logger trx`: see RESULTS.

## Open items
- Server endpoints/handlers (`SaveMeasTraining`, `SetMeasExit`, `AddMeasPulse`), the `IMeasurementGate` call, HR-001 check and the QR/login-free routes are not written (MeasurementGate belongs to meas-settings and does not exist in the base). Only the tables and the pure rules exist; the sync mapper names the intended commands.
- `Hcc.Meas.Tests` does not exist yet; the C# tests sit in `tests/Hcc.Operations.Tests/Meas/` (move when meas-settings creates the project).
- The people pack is hidden by the payroll module off because `hrMeasPeopleOn` checks `modulesOff` itself; once `measOn` really enforces module+pack+item the double check is harmless.
- `MEAS_OFF` is not yet in the `ErrorCode` union (`src/domain/errors.ts`); the handlers throw `HccError` with that code via a cast.
- Evidence is a free reference string (document number/file name); no upload control (LF documents store picker is not wired).

## Integration needed
1. `03 Prototype/src/features/staff/register.tsx`: import `MeasTrainingModal, MeasExitModal` from `./Training` and `MeasPulseModal` from `./Pulse`; add to `modals`: `'meas-training':MeasTrainingModal,'meas-exit':MeasExitModal,'meas-pulse':MeasPulseModal`; add a "Training" and "Staff pulse" entry on the HR desk / My HR that opens them.
2. `03 Prototype/src/domain/errors.ts`: add `'MEAS_OFF'|'MEAS_PACK_OFF'|'MEAS_READING_NOT_MONOTONIC'` to `ErrorCode` and `errorTitle` (meas-settings).
3. `03 Prototype/src/services/mock/commands.ts` audit row: for `cmd.type==='MEAS_PULSE_ADD'` write `actor:'Anonymous'` instead of `user.name`, otherwise the command log (`s.audit`) links a pulse answer to the signed-in person. The pulse slice itself is anonymous.
4. `MeasurementCatalogue` / `MEAS_ITEMS` must carry `meas.people.training` (module `payroll`), `meas.people.attrition`, `meas.people.pulse` (key names are used verbatim by `HR_MEAS_KEYS`).
5. Report lanes: show a pulse department only with at least three answers in the quarter (stated in the migration comment); read `hrMeasSlice(s).training/exit/pulse`.
6. Server: write the three command endpoints with `RequireHccMeasurement` and HR-001; `AddMeasPulse` must not read the caller into the row.

## HANDOFF bullet
F12 cap-people (Sonnet): MEAS_TRAINING_SAVE (certificate-expiry reminder at 30 days through the desk reminders), MEAS_EXIT_SET, anonymous MEAS_PULSE_ADD (no staff id stored, test-proven); modals in `features/staff/Training.tsx` and `Pulse.tsx` still to be registered; migrations 4825/4826 (`meas.training`, `staff_exit`, `pulse`); server endpoints owed.

## RESULTS
- `tests/stream-meas-people.mjs`: 10/10 pass (single-file run through the lock).
- `npx tsc --noEmit`: clean (exit 0).
- `npm test`: 2883/2894 passed; the 11 failures are ALL `[RBAC]` in `tests/stream-rbac-matrix.mjs` (outside my row): "command without a documented role set: MEAS_EXIT_SET" (and the per-role cases that follow it). Cause is only the missing CMDS rows below; with them added locally the suite file was queued but the lock stayed busy (exit 75 x3), so the green re-run is not confirmed. The edit was reverted (rule 1). Baseline count before my lane: 2894 - 10 own tests = 2884 total entries (2883 pass + 11 RBAC = 2894).
- `dotnet test tests/Hcc.Operations.Tests -maxcpucount:1 --logger trx` (4 new tests in `Meas/PeopleRulesTests.cs`): queued behind other lanes at the time of writing, result not confirmed. Lane stopped at the tool-turn cap.

### Integration needed (extra, from the npm test)
`03 Prototype/tests/stream-rbac-matrix.mjs`, in `CMDS` after `DIGEST_REDRAFT` (line ~100), add:
`MEAS_TRAINING_SAVE:C('OWN MGR','F12 cap-people HR-001 (approve)'),MEAS_EXIT_SET:C('OWN MGR','F12 cap-people HR-001 (approve)'),MEAS_PULSE_ADD:C('OWN MGR REC CSH SUP HKP ACC MNT TAD','F12 cap-people: any signed-in user, anonymous'),`
(every MEAS lane adds its own commands the same way; PAYLOADS entries may be needed if a role is refused by validation before the permission).
