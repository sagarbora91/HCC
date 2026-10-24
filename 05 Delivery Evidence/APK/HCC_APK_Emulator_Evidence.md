# HCC Android debug APK — emulator evidence (24 Oct 2026) — EMULATED, not a physical device

Build: `HCC-0.1.0-proto-debug.apk` (versionName 0.1.0-proto, versionCode 2, 6.1 MB, `in.hcc.prototype`, Capacitor 8.5.3, targetSdk 36), built from the 1,822 KB prototype with FY26 data.
Emulator: AVD `codex_api35_google` (Android 15, Google, 1080×1920). Owner device for the physical run: Samsung Galaxy Tab S7 (SM-T875), Android 13 — **not yet tested**.

| Check (plan D5) | Result | Evidence |
|---|---|---|
| Installs and starts | PASS — `adb install -r` Success, `am start -W` TotalTime ≈ 2.9 s cold start on the emulator | 01-launch.png |
| Opens in Mobile mode with FY26 data | PASS — phone layout, Overview 35 % (8 of 23), receipts ₹21,500, History summary FY occupancy 56 % | 01-launch.png, 06-reports.png |
| Status bar | PASS after fix — maroon bar with light icons (window background set to maroon) | 06-reports.png |
| Back button: page → previous page | PASS — Rooms tab, Back → Home | 02-rooms-tab.png, 03-back-to-home.png |
| Back button: landing → exit confirmation | PASS — "Exit Hotel Control Centre?" with Stay / Exit; Back again = Stay | 04-exit-confirm.png, 05-stay-after-back.png |
| Back button: sheet closes first | PASS — booking wizard / report sheet closed by Back (seen while testing) | 08 / 09 |
| CSV export | PASS — file written to `/sdcard/Documents/HCC/HCC-Demo-daily-2026-10-24_2026-10-24.csv` and Android share sheet opened | 08-csv-share-sheet.png |
| Print | PASS — report print sheet → Android print service ("Select a printer", preview, Save as PDF possible) via `@capgo/capacitor-printer`; share-sheet fallback is unit-tested | 09-print.png, 10-print-service.png |
| Data survives app restart | PASS — scenario S1 loaded, app force-stopped and restarted, still S1 (IndexedDB); FY26 restored afterwards | 12, 13, 14 |
| Landscape | PASS — usable, no overlap or sideways scroll | 15-landscape.png |
| No horizontal scroll | PASS in the checked screens | all |
| 9 roles: tabs and More | NOT RUN on the emulator (same web code passed the 360 px browser smoke for all 10 demo users, see History\HCC_History_Smoke_Phase_C.md) | — |
| AC-M05 booking→check-in, AC-M06 ₹3,400 split payment / deposit return / shift close, AC-M07 housekeeper cleans 105, AC-M08 manager closes the day | NOT RUN on the emulator (covered by the unit tests and the browser walkthrough; to be done on the Tab S7) | — |
| Reset works | NOT RUN separately (scenario reload was used) | — |

Build notes: Norton SSL scanning blocked Gradle's downloads until a private Java trust store containing Norton's root was used for the build session (`04 Android\.build\hcc-cacerts.jks`; see `04 Android\README.md`). `ANDROID_SDK_ROOT` in this user's environment points to another SDK (`V:\Co work\Projects\Retail\.android-build\sdk`) and must be set equal to `ANDROID_HOME` for the build session.

## Physical device — Samsung Galaxy Tab S7 (SM-T875), Android 13, 1600×2560 at density 380 (24 Oct 2026, USB debugging)
APK versionCode 4 installed with `adb install -r` (Success), cold start ≈ 2.0 s.
- Owner decision implemented: inside the app **phones keep the phone layout; tablets get the desktop layout** (device's shorter side < 600 CSS px = phone). On the Tab S7 (short side ≈ 673 px, any rotation) the app opens in the desktop layout with the Auto display mode selected — `Tab-S7\01-launch-desktop-layout.png`, `Tab-S7\02-auto-selected.png`.
- Back button on the tablet at the Overview landing page → "Exit Hotel Control Centre?" (Stay/Exit) — `Tab-S7\03-back-exit-confirm.png`.
- Not yet checked on the tablet: CSV export/print on the desktop layout, 9 roles, AC-M05…M08, long-run use. Emulator tablet-size run was not completed (the emulator had crashed); the physical tablet replaces it.
