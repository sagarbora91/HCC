# F12 MEAS-CAP-DESK DONE

Lane meas-cap-desk · Sonnet 5.5 · branch claude/meas-cap-desk · worktree E:\Projects\HCC\worktrees\meas-cap-desk · base tag meas-setup (3040fa7f).

## Commits
See `git log --oneline meas-setup..HEAD` (prototype rules + handlers + sync mapping; UI; server rules, migrations, tests; this file).

## Files
Prototype: `src/domain/streams/turnaway.ts`, `src/services/mock/handlers/meas-desk.ts`, `src/fixtures/ext/meas-desk.ts`, `src/features/reservations/TurnawayModal.tsx` + `TurnawayRegister.tsx`, `src/features/folio/UpsellModal.tsx`, `src/features/customers/PreferencesModal.tsx`, `tests/stream-meas-desk.mjs`.
Append-only F12-CAP-DESK blocks: `handlers/index.ts`, `mock/index.ts`, `clients/sync/registry.ts` (inline SyncArea `meas-desk`, commands only), `Hcc.slnx` (3 migration lines).
Server: `08 Production/src/Hcc.Operations/Meas/DeskRules.cs`, migrations 4810 / 4811 / 4812 (locked with `--add --accept-out-of-order`), `tests/Hcc.Meas.Tests/DeskRulesTests.cs`.

## What was built
- Commands (all refuse `MEAS_OFF` first, idempotent on key, audited): `MEAS_TURNAWAY_LOG` (Reception / Manager / Owner), `MEAS_WALKED_LOG` (Manager / Owner), `MEAS_ALLOWANCE_REASON_SAVE` (Owner; defaults in fixtures, slice stays absent until first write so seed pins do not move), `MEAS_BOOKING_EXTRA_SET` (booked type kept; UPG paid posts through the existing CHARGE handler, free needs a Manager / Owner approver; LCO1 Rs 300 from 14:00, LCO2 Rs 600 from 18:00 pre-GST paise, GST 18 % "other" category; waiver needs Manager + reason, nothing posted), `MEAS_PREFERENCE_SET` (preferences by Customer 360 key; blacklist = reason + Owner approval, Reception refused, clear needs a reason).
- Pure helpers: `mdOn`, `mdKnownGuest` (stays + last visit, blacklist flag), `mdBookedType`, `mdTypeDiffers`, `mdLateCheckoutItem`, `mdLeadDays`.
- MEAS_OFF: the guard calls `measOn` (stub true until meas-settings merges) AND reads the stored switch `configX.values[key]===false`, so the rule is real and tested today.
- UI (modals, 48 px forms, same component on phone and desktop): `meas-turnaway`, `meas-upsell`, `meas-prefs`; `TurnawayButton`, `UpsellButton`, `KnownGuestBadge` hide when off / role not allowed.
- Server: schema `meas`; tables `turnaway`, `walked`, `allowance_reason`, `booking_extra`, `booking_extra_log`, `customer_preference`, `customer_preference_log`; `DeskRules` (twin of the prototype rules).

## Tests
Before: prototype 2884 passed (setup base, `npm test` run before my files existed; a run during development showed only the RBAC matrix gaps below).
After: prototype 2900/2900 passed (2884 + 16 new) with the RBAC matrix edit from Integration item 1 applied locally; without that edit 11 [RBAC] tests fail by design (the registry has commands the matrix does not list yet). `[F12-CAP-DESK]` tests in tests/stream-meas-desk.mjs (guards: roles, MEAS_OFF x5, idempotency, validation, booked type, UPG/LCO bands and GST, waiver, blacklist approval, absent slice); `npx tsc --noEmit` clean; server `dotnet test tests/Hcc.Meas.Tests`: 6 passed (project csproj created locally to run them, see Integration).
Commands: Invoke-Serialized.ps1 (label meas-cap-desk) with `npx tsc --noEmit`, `npm test`, `dotnet test tests/Hcc.Meas.Tests -maxcpucount:1 --logger trx`. Exit 75 seen twice, retried.

## Open items
- Server command service / SQL store / endpoints (LogTurnaway, LogWalkedGuest, SaveAllowanceReason, SetBookingExtra, SetCustomerPreference) are NOT built: they need Hcc.Contracts / Hcc.Application / Hcc.Api, outside this lane's row, and `IMeasurementGate` from meas-settings. The sync mapping already names these commands. `DeskRules` + tables are ready for them.
- Server RBAC for the mirror tables (new permission ids) needs PERM-MEAS-* from 4802 (meas-settings); migration 4813 / 4814 left unused.
- Phone sheet / fit-check not run (no page owned; modals only). Known-guest badge and "Could not book" button are not yet mounted (see Integration).
- `ErrorCode` has no `MEAS_OFF`; commands throw `FORBIDDEN` with message `MEAS_OFF: ...` and `details.reason='MEAS_OFF'`.

## Integration needed
1. `03 Prototype/tests/stream-rbac-matrix.mjs` (CMDS, before the `// LF-money` comment) add:
   `...Object.fromEntries(['MEAS_TURNAWAY_LOG','MEAS_BOOKING_EXTRA_SET','MEAS_PREFERENCE_SET'].map(c=>[c,C('OWN MGR REC','F12 cap-desk')])), MEAS_WALKED_LOG:C('OWN MGR','F12 cap-desk'), MEAS_ALLOWANCE_REASON_SAVE:C('OWN','F12 cap-desk (DEC-058)'),`
   Without it 11 [RBAC] tests fail (verified green with the edit applied locally, not committed).
2. `src/app/registry.ts`: `import {registration as measDesk} from '../features/reservations/TurnawayRegister';` and add `measDesk` to `registrations` (dialogs + booking-dialog action button).
3. Mount `<TurnawayButton/>` (TurnawayModal.tsx) in the reservation search header (Reservations.tsx) and `<KnownGuestBadge b={booking}/>` (PreferencesModal.tsx) in the arrivals list row; an Owner screen for allowance reasons.
4. Server: add `tests/Hcc.Meas.Tests` csproj (meas-settings creates it; my test file DeskRulesTests.cs only needs a reference to Hcc.Operations) and the shared `measX` sync loader (five lanes write that slice).
5. `ErrorCode`: add `MEAS_OFF` and switch `guard()` in handlers/meas-desk.ts to it.

## HANDOFF bullet
F12 cap-desk: turn-away / walked / allowance reasons / upgrade + late check-out (UPG LCO1 LCO2 via CHARGE) / preferences + blacklist commands with MEAS_OFF, tests [F12-CAP-DESK], migrations 4810-4812 (meas schema), DeskRules.cs; server command service + endpoints and UI mounting owed to the integrator (DONE file Integration needed).
