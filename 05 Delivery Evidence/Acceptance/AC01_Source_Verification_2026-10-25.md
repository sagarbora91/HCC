# AC-01 compatibility source verification — 25 October 2026

## Outcome
Pending Intl/clone fixes reviewed and extended. Source verification passes; **AC-01 remains open for a rebuilt APK test on the affected WebView**. No emulator or physical device was attached when `adb devices -l` was checked. The owner-paused emulator was not restarted. No APK was rebuilt or installed by this task.

## Changes
- Retained explicit minimum/maximum currency fraction digits and the guarded JSON-state clone fallback from the pending work.
- Replaced all application `.at(-1)` calls with `.slice(-1)[0]` and literal `replaceAll` calls with `split/join`, including FY26 fixture generation, command auditing, messaging, rate selection, print selection and exports. These were additional unguarded runtime dependencies left by the initial fix.
- Clarified clone scope: JSON-shaped persisted state only; undefined object fields are omitted in fallback mode. It is not a general-purpose structuredClone polyfill.
- Added `03 Prototype/tests/stream-compat.mjs`: bundles the real mock service for Chrome 83 syntax, runs it in an isolated Node VM with structuredClone, Array.at and String.replaceAll unavailable, and models the reported legacy Intl option rejection. No global API changes affect the regular test runner.

## Verification
- TypeScript check: passed.
- Full regression: **492/492 passed**, including the first two new compatibility tests. Log: `05 Delivery Evidence/Assembly/compat-tests-2026-10-25.log`.
- Added a third parity/permission test after the full run began; then ran the final focused suite: **3/3 passed**. Log: `05 Delivery Evidence/Assembly/compat-focused-2026-10-25.log`. The final suite now contains 493 tests, but a full 493-test run was not performed; do not report 493/493 from this evidence.
- Compatibility checks: FY26 >1,000 bookings loads; missing APIs do not prevent night posting or assistant commands; original state stays unchanged; idempotent replay returns existing state; forbidden payment preserves input; fallback/native night-post results match after JSON serialization; whole-rupee, fractional, negative and zero formatting match expected INR output.
- Production HTML build: passed, reported 1,880 KB. `03 Prototype/dist/index.html` SHA-256: `9dd453b09e479fa41b530c37fed3951e5b234e719752885556665650559c6e96`.
- Source search: no remaining `.at(` or `replaceAll` calls under `03 Prototype/src`; structuredClone is only used behind its availability guard.
- Git whitespace check on prototype changes: passed.

## Intermittent test investigation
The previously recorded 489/490 failure did not name its failed test, so its cause cannot be established. A potential timing-sensitive check was located in `tests/stream-history-c.mjs`: median generation <=1,500 ms, report <=1,000 ms and command <=500 ms. This run measured 628/487/134 ms and passed. No thresholds were relaxed. If it fails again, retain the failing assertion and machine/load context rather than simply rerunning until green.

## Remaining device verification
This VM harness tests application behavior under selected missing APIs; it is not an actual Chrome 83 engine, DOM, React UI, native bridge or APK test. Follow the existing Android build procedure with a new versionCode and test cold start, FY26 load, sign-in, booking/check-in, cash/closing and export on the affected WebView when device testing can resume. Confirm no further compatibility errors before closing AC-01. Keep v6 evidence distinct from the new build. Remaining W0-D role, flow and tablet issues are outside this source-verification result.
