# W0-D · Prototype acceptance (browser walkthrough, emulator, Galaxy Tab S7) — agent brief

Wave 0 of `02 Planning/HCC_Multi_Agent_Plan_v1.md`, last step (APR-005). You work **in the main checkout** `V:\Codex\Hotel Control Centre` (the Android build environment, trust store and Gradle caches live only there). No other agent is writing code now. Owner order: **emulator first**; the owner connects the Samsung Galaxy Tab S7 (SM-T875, Android 13, serial R52N807PTTE) by USB for the device part.

## Read first
`CLAUDE.md`, `HANDOFF.md` (Sessions 7, 7b, 8, 9b), `04 Android/README.md` (build env: `JAVA_HOME` = Android Studio jbr, `ANDROID_SDK_ROOT` = `ANDROID_HOME`, private trust store `04 Android\.build\hcc-cacerts.jks` via `JAVA_TOOL_OPTIONS` for the session only, `--offline` works), `05 Delivery Evidence/APK/HCC_APK_Emulator_Evidence.md`, `02 Planning/HCC_Acceptance_Matrix.md`, `02 Planning/HCC_Mobile_Version_Plan_v1.md` §AC-M05…M08, the S0 demo guide in `03 Prototype/src/app/demo/DemoModals.tsx` (drawer path ₹10,750 → ₹11,250 → ₹12,750 → ₹12,450 after DEC-052).

## Steps
1. **Build:** `npm run build` in `03 Prototype` (expect tsc clean, 490/490 tests first) → `node scripts\copy-web.mjs` → `npx cap sync android` → `gradlew assembleDebug` with **versionCode 6** (current 5 was built before the till/restriction merge). Copy to `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`; record size + SHA-256.
2. **BL-023 browser walkthrough (desktop 1366 px):** run the 15 S0 guide steps 24→26 Oct (scenario S0) end to end through the UI with the browser pane (`.claude/launch.json` → `hcc-prototype`), including settling the two open restaurant checks before closing 24 Oct. Every "expected" figure must match; record step-by-step results and screenshots in `05 Delivery Evidence\Acceptance\`.
3. **D5 emulator (`codex_api35_google`, phone layout) — label EMULATED:** install; all 9 roles (+ second housekeeper): bottom tabs / drawer show only permitted pages, housekeeping/maintenance see no money; AC-M05 (reception booking → check-in, incl. a restricted 7→8 Nov stay showing the warning), AC-M06 (₹3,400 split ₹2,000 cash + ₹1,400 UPI, deposit return, shift close; figures = S0 oracle), AC-M07 (housekeeper cleans Room 105, 48 px targets, no money), AC-M08 (manager closes the day — open restaurant checks block first; report totals equal desktop); Reset; back button; CSV + print; restaurant settlement on the phone POS screen.
4. **D6 Tab S7 (desktop layout in Auto) — label PHYSICAL:** when `adb devices` shows the tablet (if empty, ask the owner to re-plug and accept "Allow USB debugging"): `adb install -r`, cold start time, sign-in + Modules Home + hamburger drawer, CSV to `Documents/HCC` + share sheet, print via Android print service, Reset, cashier shift ₹10,750 with restaurant line, a booking with restriction override as Manager, rotation, touch comfort notes; compare once with Display → Mobile. Screenshots to `05 Delivery Evidence\APK\Tab-S7\` (prefix `w0d-`).
5. **Record:** update `02 Planning/HCC_Acceptance_Matrix.md` (each AC row Passed / Failed / Not run with evidence path and emulated vs physical), a new `05 Delivery Evidence\Acceptance\W0-D_Acceptance_Report.md` (environment, build hashes, results, defects with steps to reproduce), and a short HANDOFF section.

## Rules
- Do not fix product code. If something fails, record it as a defect (severity, steps, screenshot); a one-line obvious fix is allowed only if it is in evidence/docs. Code fixes go back to the lead.
- Never disable certificate verification; keep the trust store out of git (it is ignored). Bump versionCode only once (6).
- Fictional data only; don't install anything else on the tablet; don't change tablet settings beyond what the owner already enabled.
- Commit on `main` only your evidence/doc files and the versionCode change (message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`); never push.

## Done
Final answer ≤ 25 lines: build (versionCode, SHA-256), BL-023 result, D5 and D6 tables (pass/fail counts), defects list, what could not be run and why.
