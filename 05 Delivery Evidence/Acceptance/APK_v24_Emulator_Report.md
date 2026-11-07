# APK v24 emulator report (5 Nov 2026, versionCode 25, versionName 24)

Branch claude/adm-ux (no source change, nothing committed or pushed; `capacitor.settings.gradle` reverted after cap sync, `git status` clean apart from this untracked evidence).

## Builds
Folder `E:\Projects\HCC\Builds\2026-11-05c\`

| File | SHA-256 | Manifest (aapt) |
|---|---|---|
| HCC-24-vc25-demo-debug.apk | 32cab811fccac18e7b654fb0efc0391927fcf2dd348ff3ddaaedac2006a7442b | in.hcc.prototype, versionCode 25, versionName 24 (dumpsys on both AVDs too) |
| HCC-24-vc25-server-debug.apk | 872500a3df6be9e9cce1967e0da536e5511b2a8300e895d05a9de92bc2db55e4 | in.hcc.prototype.server, versionCode 25, versionName 24-server |
| HCC-Test-CA-throwaway.cer | da467d0f5226af75ce7998ddc61acfbd79e5b5d9f96d2e24a2a0b03d3d2bed04 | copy of the 2026-11-05 test CA |

SHA256.txt is in the folder. Pipeline as v23 via `tools\Invoke-Serialized.ps1`: `npm run build`, `npm run build:production`, `npm ci --prefer-offline`, `copy-web.mjs`, `cap sync android`, `gradlew assembleDebug --offline`, `build-production.mjs --ca ... --assemble`. Server APK was only built and version-checked, not installed.

## Checks (screenshots in `05 Delivery Evidence\Acceptance\v24\`, `admux-*` phone, `tab-admux-*` tablet)
Phone = codex_api35_google (1080x2400, Owner Sagar). Tablet = hcc_tablet_api35 (2560x1600, dark theme, Owner).

| # | Check | Phone | Tablet |
|---|---|---|---|
| 1a | Hamburger shows 'Admin & Setup' with hint 'Modules on/off, users, roles, audit'; opens Admin & Setup area, Administration reachable | PASS (03, 04, 06) | PASS (tab-02, tab-03) |
| 1b | Modules Home tile caption says modules on/off | PASS: "Modules on/off, users, roles, audit, backup" (19) | PASS (tab-01) |
| 1c | Manager (Meera): entry absent | PASS (20) | not run |
| 2a | Tap On/Off pill of Restaurant & POS switches it off, toast "switched off - data kept" stays ~8 s | PASS: toast present in every shot from 1 s to ~8 s, gone by the next (timed shots, ~1.4 s apart; about 8-9 s, +/- 1.4 s) | PASS: same timing (shots 19:14:17-19:14:24.8 show it, 19:14:26.3 does not; tab-04) |
| 2b | Tap pill again: switches on, toast "switched on" | PASS (08) | PASS (tab-05) |
| 2c | Tap the empty part of the row toggles | PASS: Groups & companies, tap right of the Switch button, went Off with toast, tapped again, back On (09) | not run |
| 3 | Core row shows lock + reason, not tappable | PASS: Room board, Arrivals & check-in, Guest register, In-house list show "Always on - the hotel cannot run without it." with a lock; tap changed nothing (11, 12) | not run |
| 4 | Messages off (confirm lists Templates & rules, OK); Messages on: toast says Templates & rules stays off, row still Off showing "Stayed off when the part it needs was switched back on - switch it on here." with Switch on; tapping turns it on | PASS (15, 16, 17, 18) | not run |
| 5 | Display mode = Desktop on the phone AVD | PASS: Administration > Modules & features works, pill off/on with toast works (22-27). Layout is cramped at 360 dp (breadcrumb truncated to "M... > A...", DEMO pill clipped at right edge, Sagar row tall), cosmetic, desktop layout on a phone | n/a |
| 6 | Regression: launch, Counter bill, Folio payment open, no FATAL/ANR | PASS: app launches (01), Counter bills New bill form opens (28), Guests & folios > HCC-1041 > Record payment dialog opens (29); 0 FATAL EXCEPTION / ANR in logcat | PASS: launches, 0 FATAL/ANR (Counter/Folio not exercised on tablet) |

Both emulators were shut down; display mode on the phone AVD was set back to Mobile.

## Not verified
- Server APK on any device or emulator, its pages, egress-proof, physical Galaxy Tab S7 (no physical device was attached; every adb call used `emulator-5554` only).
- Check 1c, 2c, 3, 4 on the tablet; Counter bill and Folio payment on the tablet; no bill was issued and no payment was posted (dialogs only).
- Toast duration is measured to about +/-1.4 s from timed screenshots, not from the source timer.
- Manager check for the Admin tile on Modules Home (only the hamburger was checked).

## Defects found
None. Observations: (a) Desktop display mode on a 360 dp phone is cramped (see check 5; layout only, not a regression of this build). (b) In the phone hamburger the 'Admin & Setup' row sits tight against 'Modules Home' (hint text is two lines); no clipping.
Emulator demo data left as is: Restaurant & POS, Groups & companies, Messages and Templates & rules were all switched back On.
