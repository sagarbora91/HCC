# F8-QW-PM-DONE (lane qw-pm, Sonnet) - 8 Nov 2026

Branch claude/f8-qw-pm. Label QW-PM.

## Built
- `03 Prototype/src/domain/streams/pm-calendar.ts` (new, pure, names `pmc*`): pmcMonth (plan occurrences per day: Done / Overdue / Due / Scheduled, projected from nextDue by everyDays, work-order link, asset + register link number/status only), pmcState (same boundaries as mntPlanState / mntPmDue: before the business date = Overdue, up to date + 3 = Due), pmcAmcCards (Active / Expiring within 30 days / Expired / Not started, no contract value), pmcTemplates / pmcMissingTemplates (QWQ-6 CCTV and DG shift check, Morning / Evening / Night).
- `03 Prototype/src/features/maintenance/PmCalendar.tsx` (new): month grid (desktop) / day list (phone), text state labels, AMC cards, Manager button "Add standard CCTV / DG checklist" (three HOV_TEMPLATE_SAVE commands, stable keys, disabled once all three exist). Click: opens the work order, or runs the existing MNT_PM_GENERATE, or opens the plan.
- Shared files, inside `F8-QW-PM` markers: `Maintenance.tsx` (tab 'pm-calendar' after 'pm'), `MaintenanceMobile.tsx` (renders PmCalendar phone), `app/subscreens.ts` (sub-screen id), `services/mock/index.ts` (export).
- `03 Prototype/tests/stream-pm-calendar.mjs`: 6 tests [F8-QW-PM].

## Run (actually)
- `npx tsc --noEmit`: clean.
- `HCC_TEST_ONLY=stream-(pm-calendar|maintenance|handover)`: 93/93 passed.
- Full `npm test`: **3170/3172 passed, 2 failed** (log `.scratch/npmtest.log`), both guard tests in files outside my row, see below.
- Fit check of `maintenance` (PORT 4282): **NOT RUN to a result.** The first attempt had no server/build (ERR_CONNECTION_REFUSED); the rerun (build + serve.mjs + check, one serialized job) stayed queued behind the lock and I stopped it at my turn cap. Integrator: run `npm run build`, `PORT=4282 node serve.mjs`, then `node tools/fit-screen-check.mjs --pages maintenance`.

## Integration needed (the 2 failing tests)
1. `tests/stream-fit-ops.mjs:84` [FIT-OPS] expects the maintenance tab ids exactly `orders,pm,assets,spares,vendors,reports`; now also `pm-calendar` (listed last in the subscreens F8 block). Update the expected list.
2. `tests/stream-fit.mjs:~88-110` [FIT] "no phone-layout file changed since the wave base" flags `MaintenanceMobile.tsx` (brief allows an F8-QW-PM block there). Add it to the allow-list.
No migrations used (4481-4484 unused); no server change; BASE and FY26 untouched; SCHEMA stays 16.

## Defaults relied on
QWQ-6 (checks: CCTV recorder + cameras, DG diesel level, DG weekly test run, battery / UPS, fire panel normal; three shifts; only when the Manager presses the button). Three templates (one per shift) because a template has a single shift.

## Not done / unverified
- No production seed migration, no server change. Browser/phone rendering not looked at by eye; styles are inline with tokens (no new stylesheet, no hex). Asset-to-register link matches by exact asset name only.
