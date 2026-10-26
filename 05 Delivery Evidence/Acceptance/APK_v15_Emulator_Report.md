# APK v15 emulator report (retest of the V14 defects: demo APK on phone and tablet, server APK on the tablet)

Date 27 Oct 2026 · tester: Claude (Sonnet 5.5 agent for the Fable 5.1 lead) · EMULATED, not physical devices · branch `claude/apk15` (= main 2909f6d = APK v14 + the V14 fix round fix14-a/b/c) · screenshots in `v15\` (`p-*` phone, `t-*` tablet, `t-2x` server APK on the tablet). The only change besides this report is `versionCode 14 -> 15` in `04 Android\android\app\build.gradle`. No product code changed.

## Builds (versionCode 15, `dumpsys package` on both AVDs)
| APK | Package | SHA-256 | Size | Notes |
|---|---|---|---|---|
| `APK\HCC-0.1.0-proto-debug-v15.apk` | `in.hcc.prototype` | `1A3CE282A734457959C427B15F10561EBC7617594DC6544D4C12D0984AE387E6` | 6,175,080 | `assembleDebug`, demo data on device, version name `0.1.0-proto` |
| `APK\HCC-0.1.0-server-debug-TESTCA-v15.apk` | `in.hcc.prototype.server` | `FCCA8841E296669EB8DF4FA73A5E966D5BB1CFA4E1B03D6D64C90BBE6D01FF34` | 6,264,987 | `assembleProductionDebug` with a THROWAWAY test CA (CN "HCC APK15 Throwaway Test CA", 30 days, SHA-256 35:59:69:FA:...:A2:95), HTTPS only, debuggable. Test use only |
| `productionRelease` | `in.hcc.server` | not built | - | needs the hotel release keystore; nothing in the release config changed since v12/v13 |

- JDK 21.0.12.1, Gradle `--offline` (no Norton workaround needed), node 25.9.0. Sequence: `npm run build` (2,817 KB) + `npm run build:production` (3,060 KB) in `03 Prototype`; `npm ci` in `04 Android`; `copy-web.mjs` + `cap sync android`; `assembleDebug` (27 s); `build-production.mjs --ca <test CA>` + `assembleProductionDebug` (13 s). The StatusBarColor plugin is in both builds.
- `*.apk` is git-ignored, so the APKs stay untracked in `05 Delivery Evidence\APK\` (like v14).
- The test CA, the server certificate/PFX (CurrentUser store + scratch folder) and the generated Owner credentials were created in the session scratch folder and deleted at the end; nothing secret is committed.

## Server set-up (server APK)
Disposable DB `HccTest_APK15_20261027031045` on `.\SQLEXPRESS` (migrated 0 -> 3800 with `Hcc.Api migrate`), API `dotnet run -c Release` with `ASPNETCORE_ENVIRONMENT=Development`, Kestrel HTTP `127.0.0.1:5211` (setup script) and HTTPS `0.0.0.0:5443` from the throwaway PFX (Kestrel endpoint env vars, no machine-store certificate needed); setup with the W4_E2E2 scripts (SetupProperty, 11 sample masters incl. POS, GSTIN); the sample floors list was NOT imported, so the server has 0 floors. DB dropped (verified none left), API stopped, ports 5211/5443 closed, emulator killed. Other sessions' `HccTest_20261026122622_...` DB was left untouched.

## V14 defect -> v15 result
| V14 | Sev | Result in v15 | Evidence |
|---|---|---|---|
| V14-1 themed discard dialog | S4 | **Fixed on phone sheets.** Master row sheet (Room types > Suite, name edited) and the New booking sheet: Android Back and the X open the themed in-app "Discard changes?" dialog (Keep editing / Discard); Keep editing keeps the typed value and the step, Discard closes the sheet and the row is unchanged; no native dialog. **Partly on the tablet:** the desktop layout shows the master form inline (no sheet), and Back with a dirty inline form leaves the page without any prompt (see V15-2) | p-12, p-13, p-21; t-08 |
| V14-2 status bar colour | S3 | **Fixed (API 35 phone and tablet, demo and server APK) with one exception, V15-1.** Phone: Dark status bar #AE4155 (was #7A1F2B), High contrast #5E1620, Follow device live both ways (`cmd uimode night yes` -> #AE4155, `no` -> #7A1F2B), bottom gesture strip follows the surface (#201916 dark, #FFFFFF light/HC). Tablet and server APK: Dark #AE4155 / #201916 on first selection and after a cold start. Exception: after a screen rotation the bars fall back to maroon (V15-1) | p-04..p-07; t-09, t-14, t-26, t-27 |
| V14-3 tablet portrait master form | S3 | **Fixed.** Portrait (800 x 1224 css px): the list is full width and the form sits below it; Code/Name inputs are 330 px wide (was 26 px), a real keyboard entry ("ish" appended to "Classic") worked; landscape (1280 css px) shows list and form side by side. No horizontal scroll (scrollWidth = viewport) | t-03..t-07 |
| V14-4 truncated labels at Extra large | S4 | **Fixed.** Extra large (body 18.2 px): the app bar title wraps to two lines ("Appearance & display"), the date line is dropped, tab labels read "Approve" (Owner) and "Rooms / Arrivals / Stays / Folios / Menu" (Reception), no horizontal scroll (scrollWidth 412). The labels return to "Approvals" / "Bookings" at Default | p-22..p-24 |
| V14-5 new rooms and New booking | S3 | **Fixed.** Rooms 401-403 added by range on 24 Oct (24 -> 27 rooms). New booking 24-27 Oct (business date + 2 nights): 13 available including 401, 402, 403; the excluded list now says "Group hold - GB-4 Sahyadri Infra site team" on the ORIGINAL rooms 303, 304, 307 (not on 401-403). After retiring 403 (26 rooms, 1 retired): 12 available (401, 402), and 403 is listed under excluded with the reason "Retired". Booking HCC-1107 on room 401 for 24-27 Oct confirmed and checked in. The room board shows 26 rooms (no 403). The board has no separate hold legend on the business date (GB-4 starts 25 Oct); the hold shows in the New booking exclusions and on Groups ("unpicked block rooms are held ... Group hold") | p-09..p-11, p-15..p-19, p-25..p-28 |
| V14-6 Retire confirmation | S4 | **Fixed.** Retire opens the in-app confirm "Retire Room 403?" (Back / Retire) with the reactivate hint; retiring then shows "Room 403 retired - old records keep the name". Same dialog on the tablet (floor "Annexe v15", themed in Dark) | p-14, p-15; t-13 |
| V14-7 uniform list tools | S4 | **Fixed.** "Paste rows" is present on Room types and on Rooms; the Rooms list says "Rooms are listed by floor and room number, so they have no manual order (no move arrows)". (Demo build only; the server has the older import, as the fix notes said.) Room types keep reorder arrows | p-08, p-20; t-03 |
| V14-8 README SDK path | S4 | Fixed in the README (E:\Android\Sdk, JDK 21); the build steps in it ran unchanged | - |

Counts: 7 fixed, 1 with a residual (V14-1 on the tablet inline form, new V15-2); V14-2 fixed except after rotation (V15-1). None still open as originally reported.

## New defects and observations
| ID | Sev | Summary | Repro / evidence |
|---|---|---|---|
| V15-1 | S3 (API 35+ only) | The status bar and the strip behind the gesture bar go back to maroon (#7A1F2B) after a **screen rotation** (portrait <-> landscape) although the Dark theme stays on. The manifest keeps `orientation|screenSize` in `configChanges`, so the activity is not recreated; the plugin's window background is simply lost when the insets change. Re-applying fixes it at once (`StatusBarColor.setColors({top:'#ae4155',bottom:'#201916'})` from the console gave #AE4155 / #201916), a cold start applies the right colour, and changing the theme applies it. Same on the demo and the server APK. The Galaxy Tab S7 (Android 13, below API 35) uses the old StatusBar path and is not affected; Android 15/16 tablets and phones are | Dark theme, rotate with `adb shell settings put system user_rotation 1` then `0`: t-15, t-16 (maroon) vs t-17 (after a manual re-apply), t-28 (server APK). Suggested fix: call `styleStatusBar(theme)` again on `resize` / `orientationchange` (or `onConfigurationChanged` in `MainActivity`) |
| V15-2 | S4 | Tablet / desktop layout: a dirty inline master form (Room types > Classic, name edited) is dropped silently by the Android Back button (it navigates up the page chain, 1 press per page; the typed text is lost without a "Discard changes?" prompt). The phone sheets are guarded (V14-1) | t-08 |
| V15-3 | S4 (cosmetic) | At default text size the phone app bar still truncates long page titles ("Appearance & d...") next to the DEMO button and avatar; at Extra large it wraps correctly | p-04 vs p-22 |

No S1 or S2 defects found.

## Regression (phone `codex_api35_google`, 412 css px, 1080x2400, API 35)
| # | Check | Result | Evidence |
|---|---|---|---|
| R1 | Fresh install (pm clear), "Who is using this device?", sign-in as Owner Sagar, Modules page, bottom tabs | Pass | p-01, p-02 |
| R2 | Role preview through the DEMO sheet: Reception tabs (Rooms, Arrivals, Stays/Bookings, Folios, Menu) | Pass | p-24 |
| R3 | New booking wizard as Reception, 4 steps incl. room 401 (24-27 Oct, Rs 3,000/night, Rs 9,000 + GST), confirmed HCC-1107 | Pass | p-25..p-27 |
| R4 | Guided check-in 6 steps (verify guest with driving-licence number, room, tariff confirm, advance skip, check in) for HCC-1107 room 401 | Pass | p-28 |
| R5 | Folio preview for HCC-1041: CGST/SGST columns, "Not yet issued", disclaimers; Print / share opens the Android print service with Paper size "ISO A4" and a 1-page preview | Pass | p-29, p-30 |
| R6 | OS font size Largest (`font_scale 1.3`), "Honour the phone's system font size" ON: TextZoom `{systemScale 1.3, honour true, zoom 130}`, body 18.2 px, no horizontal scroll | Pass | p-31 |
| R7 | Honour OFF at 1.3: zoom 100, body 14 px at once; after `force-stop` + relaunch still OFF (zoom 100); ON again -> zoom 130; `font_scale 1.0` -> zoom 100, honour true | Pass | p-32 |
| R8 | Android Back on the folio preview returns to the app after the print dialog; booking sheet Back chain asks to discard (V14-1) | Pass | p-13 |
| R9 | Theme preference `system` kept in `hcc-prefs-1` across relaunch | Pass | - |

Tablet `hcc_tablet_api35` (2560x1600, 1280x744 css, demo APK): T1 cold start + Owner sign-in, desktop layout (Pass); T2 Master configuration > Rooms and rate plans > Room types with the list and form side by side in landscape (Pass); T3 portrait form (V14-3, Pass); T4 Dark theme bar colours (Pass, then V15-1 after rotation); T5 text size Large (body 16.1 px, no overflow) in Dark (Pass); T6 add floor "Annexe v15" and retire it with the themed confirm in Dark + Large (Pass); T7 cold start in landscape keeps Dark and the right bar colours (Pass).

## Server APK on the tablet AVD (HTTPS to `https://10.0.2.2:5443`, test CA, app data cleared first)
| # | Check | Result | Evidence |
|---|---|---|---|
| S1 | Server address screen, Test connection: "Found WORKPC - version 0.1.0 at https://10.0.2.2:5443", Save and continue | Pass | t-20, t-21 |
| S2 | Owner password sign-in, "Register this device?" -> registered "Tablet emulator v15", Owner home "Welcome, Test Owner", "Server data as of ..." | Pass | t-22, t-23 |
| S3 | Master lists load from the server: Rooms and rate plans shows 0 floors, 3 room types, 6 rooms (server data, not the demo fixture) | Pass | t-24 |
| S4 | Add floor "V15 test floor" (V15, 10) -> "V15 test floor added", list shows 1 floor; SQL check: `mst.floor` row `V15 / V15 test floor / sort 10 / active` | Pass | t-25 |
| S5 | Appearance & display: Dark saved, status bar #AE4155 and bottom strip #201916; after `force-stop` and relaunch still signed in and Dark, bars correct (cold start) | Pass | t-26, t-27 |
| S6 | Rotate with Dark on | **V15-1**: bars maroon | t-28 |

Difference from v14: V14-2 (bar colour) is fixed on the server APK, and the Dark theme survives the relaunch with the right bar colours; the rotation residual is new. Server connection, registration, lists and the floor add behave as in v14.

## Not tested
Physical devices (the Galaxy Tab S7 is Android 13, where the old StatusBar path applies, so V14-2 / V15-1 there are not covered); the `productionRelease` APK (no keystore, not built); server APK on the phone AVD and the phone layout of the server lists; staff PIN sign-in and the Housekeeper DEC-061 view on the server APK; V15-1 on the phone AVD (found on the tablet, demo and server APK; not re-run on the phone); rotation on the phone; text size Extra large and High contrast / Follow device on the tablet (only Dark + Large); Rooms "Renumber" and "Set out of order" actions; Paste rows apply (only presence was checked); HK task checklist, refund, day close, CSV share, 360 px width, offline banner (unchanged since v13); real Wi-Fi address; `LocalMachine\My` certificate hosting.
