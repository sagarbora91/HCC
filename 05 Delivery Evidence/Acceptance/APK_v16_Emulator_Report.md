# APK v16 emulator report (V15 fixes + Wave D device items: demo APK on phone and tablet, server APK on the tablet)

Date 27 Oct 2026 · tester: Claude (Sonnet 5.5 agent for the Fable 5.1 lead) · EMULATED, not physical devices · branch `claude/apk16` (= main 63d4b1a) · screenshots in `v16\` (`p-*` phone, `t-*` tablet, `t-5x..8x` server APK on the tablet). The only product change is `versionCode 15 -> 16` in `04 Android\android\app\build.gradle` (versionName unchanged). No product code changed; one throw-away manifest experiment for V16-1 was built, used for two checks and reverted (see V16-1).

## Builds (versionCode 16, `dumpsys package` on both AVDs)
| APK | Package | SHA-256 | Size | Notes |
|---|---|---|---|---|
| `APK\HCC-0.1.0-proto-debug-v16.apk` | `in.hcc.prototype` | `8AB0FC8A32613CAB7A77CB6850F57C33993A9AB995FB6C647FDC74215C83D228` | 6,186,688 | `assembleDebug`, demo data on device, version name `0.1.0-proto` |
| `APK\HCC-0.1.0-server-debug-TESTCA-v16.apk` | `in.hcc.prototype.server` | `7738C90CBDBEE48CD20496BBC412B5B582F46FCEB04D0C00B04BFF42AFE25D4F` | 6,278,415 | `assembleProductionDebug` with a THROWAWAY test CA (CN "HCC APK16 Throwaway Test CA", 30 days, SHA-256 2A:74:65:18:...:CB:1E:44), HTTPS only, debuggable. Test use only |
| `productionRelease` | `in.hcc.server` | not built | - | needs the hotel release keystore; nothing in the release config changed |

- JDK 21.0.12.1, Gradle `--offline`, node 25.9.0. Sequence: `npm run build` (2,843 KB) + `npm run build:production` (3,091 KB, "CSP meta") in `03 Prototype` (test suites not run: the lead ran 1774/1774); `npm ci` in `04 Android`; `copy-web.mjs` + `cap sync android`; `assembleDebug` (1 m); `build-production.mjs --ca <test CA>` + `assembleProductionDebug` (12 s).
- `*.apk` is git-ignored, so the APKs stay untracked in `05 Delivery Evidence\APK\`.
- The test CA (made with openssl in the session scratch folder: CA key, server certificate for 10.0.2.2 / 127.0.0.1 / localhost, PFX), the generated Owner and staff credentials, the guest-ID key settings file and its offline copy were deleted at the end; nothing secret is committed.

## Server set-up (server APK)
Disposable DB `HccTest_APK16_20261027105303` on `.\SQLEXPRESS`, migrated 0 -> 4030 with `Hcc.Api migrate` (82 migrations; the Wave D migration lock caused no complaint), API `dotnet run -c Release --no-build` with `ASPNETCORE_ENVIRONMENT=Development`, Kestrel HTTP `127.0.0.1:5211` (setup scripts) and HTTPS `0.0.0.0:5443` from the throwaway PFX (Kestrel endpoint env vars; no machine-store certificate), a User-scope guest-ID key from `New-HccGuestIdKey.ps1 -Scope User -SkipAcl` handed to the API as `Hcc__GuestIdKeys__*` variables. Setup with the W4_E2E2 scripts (SetupProperty, 11 sample masters incl. POS, GSTIN 27AAAAA0000A1Z2, `SetCredential` for 5 staff = Owner-set credentials, one registered device). The sample floors list was NOT imported (0 floors). Other sessions' `HccTest_20261026122622_...` and `HccTest_20261027035805_...` databases were left alone.

## V15 item -> v16 result
| V15 item | Result in v16 | Evidence |
|---|---|---|
| V15-1 status bar after rotation, **phone**, Dark | **Fixed.** Colours read from the screenshot pixels: Dark top #AE4155, bottom strip #201916 after each of 6 rotations (landscape r=1, portrait r=0, reverse landscape r=3, ...), and after recents (Dark top #AE4155 / #201916) | p-14-dark-portrait, p-15-rot1..6, p-16-recents, p-17-after-recents |
| V15-1 phone, High contrast | **Fixed.** Top #5E1620, bottom #FFFFFF after 6 rotations and after recents | p-18-hc-portrait (+ rotation checks, shots deleted as identical), p-20-hc-after-recents |
| V15-1 phone, Follow device | **Fixed.** `cmd uimode night yes` -> #AE4155 / #201916 (theme dark); `night no` -> #7A1F2B / #FFFFFF (theme light); rotation under Follow keeps the right colours | p-27-follow-day, p-28-follow-night-yes, p-29-follow-night-no |
| V15-1 tablet, Dark | **Fixed.** Cold start #AE4155 / #201916; 6 rotations (landscape/portrait/reverse) all #AE4155 / #201916; after recents the same | t-02, t-03-dark-rot1..6, t-05-dark-after-recents |
| V15-1 tablet, High contrast | **Fixed.** #5E1620 / #FFFFFF after 6 rotations and after recents | t-06-hc, t-07-hc-rot1..6, t-08-hc-after-recents |
| V15-1 tablet, Follow device | **Fixed.** night no -> #7A1F2B / #FFFFFF; yes -> #AE4155 / #201916 (twice); portrait day #7A1F2B, portrait night #AE4155 / #201916; back to landscape night still right | t-09, t-10, t-11, t-12 |
| V15-1 server APK on the tablet | **Fixed.** Dark #AE4155 / #201916 on selection, after `force-stop` + relaunch, and after 4 rotations | t-57-server-dark, t-58-server-relaunch, t-59-server-rot1..4 |
| V15-1 residual (new) | **System night mode flipped while the app theme is fixed to Dark: bars fall back to maroon.** After `cmd uimode night no` with Dark chosen the top bar AND the bottom strip read #7A1F2B and stay so (`night yes` again does not restore them) until a rotation or a cold start. Follow device is not affected. See V16-2 | p-22, p-23, p-24, p-30, p-25 (restored by rotation) |
| V15-2 tablet desktop layout, Master configuration > Rooms and rate plans > Room types > Classic: type into Name, hardware Back | **Fixed.** First Back hides the soft keyboard (Android), the next Back opens the themed (Dark) "Discard changes?" dialog; **Keep editing** keeps "ClassicXV16" in the field; Back again -> **Discard** leaves the page and the row reads "Classic" again when reopened | t-25-back-dirty-discard-dialog-dark, t-26-keep-editing-text-kept, t-27-after-discard |
| V15-2 clean form, Back | **Fixed.** Back on an untouched inline form leaves with no prompt | (checked by script `dlg=false`, page moved up the chain) |
| V15-2 breadcrumb with a dirty form | **Fixed.** Tapping the "Modules Home" breadcrumb with "ClassicXV16" typed opens the same Discard dialog; Discard leaves (Modules Home) | t-30-breadcrumb-dirty-dialog, t-31-breadcrumb-after-discard |
| V15-2 / V14-1 phone master sheet (Room types > Suite) | **No regression.** Back (after the keyboard) opens the themed dialog; Keep editing keeps "SuiteXV16ZZ"; Back + Discard closes the sheet and drops the unsaved "ZZ" (list keeps "SuiteXV16"). Note: in the first attempt a tap on the shifted Save button (the soft keyboard had just closed) saved "SuiteXV16" - a test slip, not a defect; the second attempt is the evidence | p-31-suite-sheet, p-32-phone-discard-dialog, p-33-phone-keep-editing, p-35-phone-after-discard |
| V15-3 phone, Default text size, Appearance & display | **Fixed.** App-bar title "Appearance &" / "display" on two lines, nothing cut, no business-date line on that page (`m-longtitle`); Modules home keeps the date line. At 412 css px the DEMO pill still shows the word "DEMO" (it is icon-only only at <= 400 px: at 360 px, density 480, it is a 34 px icon pill) | p-03-appearance-title, p-13-appearance, p-90-360-appearance, p-08 text |
| V15-3 two more long titles | **Fixed.** "Master configuration" two lines, no date line; "Approvals & audit" two lines | p-08-master-title, p-09-approvals-title, p-12b |
| V15-3 short title | **OK.** "Room board" one line with "Business date 24 Oct · Open" | p-10-roomboard-title |
| V14-4 at Extra large | **Still holds.** Body 18.2 px, `scrollWidth` = 412 on Room board and Approvals & audit, tab label "Approve", no horizontal scroll | p-11-xl-roomboard, p-12b-xl-approvals |
| V15-4 5-tab labels at 360 px (still open, S4) | **Confirmed on a device.** At 360 css px and Default size "Approvals" breaks mid-word ("Approval" / "s") in the bottom bar (Roboto on the AVD, not only Segoe UI in Chrome) | p-90-360-appearance |

## Wave D device items
| Item | Result | Evidence / notes |
|---|---|---|
| FLAG_SECURE on "Show ID" - **demo APK** (phone) | **Enforced.** Customers > Ananya Rao > Profile > Show ID: `adb exec-out screencap` of that screen is solid black (23 KB, status bar clock only); the recents thumbnail of the app is black while the number is shown; after "Hide ID" a normal screenshot works again (215 KB, all colours present); other screens screenshot normally throughout | p-40-profile-id-masked, p-41-show-id-screencap, p-42-show-id-recents-thumbnail, p-43-after-hide-id-normal |
| FLAG_SECURE on "Show ID" - **server APK** (tablet) | **Enforced.** Customers > Rahul Deshmukh (DL ID saved through `SaveGuestProfile`, checked in room 101 from the UI) > Profile > Show ID: dialog "DL MH24...  This view was logged. It hides itself in 27 s ..." is present in the web view, but the screencap is entirely #000000 (24 KB) and the recents card is black; the dialog closes by itself, afterwards a normal screenshot works | t-80-server-show-id-screencap-blocked, t-81-server-show-id-recents, t-82-server-after-idhide-normal |
| CSP meta in the production page (server APK) | **Page loads and works with the CSP.** The DOM carries `<meta http-equiv="Content-Security-Policy">` (script and style by SHA-256, `default-src 'none'`, `connect-src 'self' https:`, `object-src 'none'`, `base-uri 'none'`); address screen, test connection, sign-in, device registration, staff PIN sign-in, change-PIN, 6 pages (Reservations, Room board, Cashier, Master configuration, System health, Appearance), master list save, new reservation + check-in were used with a `securitypolicyviolation` listener installed: **0 violations**; `adb logcat` shows no CSP / "Refused to" / Uncaught chromium lines (only `tile_manager ... tile memory limits exceeded`, a GPU tile warning on the 2560 x 1600 emulator) | t-50..t-58 |
| Slip-photo capture (Cashier > Bank deposit > photo, phone, demo APK) | **FAILS as built (V16-1).** Tapping the photo field never opens the camera app: Capacitor logs `Media capture intent could not be launched. Falling back to default file picker.` and the Android photo picker opens ("No photos or videos"). The fallback works: a 3000 x 2000 test JPEG (717 KB) pushed to `/sdcard/Pictures` was picked and compressed to 1280 x 853, 98 KB in the form. With a diagnostic build that only adds `<queries><intent><action android:name="android.media.action.IMAGE_CAPTURE"/></intent></queries>` the emulator camera opens (`com.android.camera2`), the photo is taken and accepted, the form shows the compressed image (960 x 1280, 18 KB) and `Android/data/in.hcc.prototype/files/Pictures/` is **gone** afterwards (it held a 0-byte `JPEG_...jpg` while the camera was open) | p-50-bank-deposit-form, p-51-form-filled, p-52-camera-launch, p-53-photo-picker-with-image, p-54-slip-compressed-in-form; with the diagnostic manifest: p-55-camera-with-queries-fix, p-56-camera-review, p-57-photo-in-form |
| Must-change sign-in (server APK, tablet) | **Works.** The Owner-set credentials (SetCredential = the same state as an Owner reset) return `mustChangeCredential:true` from the API for every staff user; in the APK, staff PIN sign-in as Priya Kulkarni opens "Choose your own PIN ... Nothing else opens until you do" (Current PIN / New PIN / again); a new 6-digit PIN was accepted and the app opened; afterwards the new PIN signs in (HTTP 200) and the old PIN is refused (403). Untouched staff (Ramesh) still have the flag | t-61-staff-picker, t-62-must-change, t-64-after-change |
| Owner password reset itself (Master configuration UI) | **Not run** (state produced with the setup script's `SetCredential`, same server rule) | - |

## Regression
**Phone `codex_api35_google` (412 css px, 1080 x 2400, API 35), demo APK**
| # | Check | Result | Evidence |
|---|---|---|---|
| R1 | `pm clear`, "Who is using this device?", sign-in as Owner / Reception / Cashier, Modules home | Pass | p-03 (Reception), p-06.. |
| R2 | Rooms 401-403 added by range (24 -> 27 rooms, 15 + 9 + 3 groups listed) | Pass | p-62-rooms-added |
| R3 | New booking wizard as Reception: 13 available rooms including 401, 402, 403 (24-26 Oct), room 401 selected, guest, review (Rs 2,000/night, Rs 4,000 + GST), confirmed HCC-1105; the wizard's own Discard dialog on Back | Pass | p-63-booking-step2b, p-66, p-67, p-68, p-70 |
| R4 | Guided check-in (6 steps: guest + driving-licence ID, room, tariff confirmation, advance skipped, check in) -> "Checked in to room 401. Room board updated." | Pass | p-69, p-73 |
| R5 | Folio preview for HCC-1105 and Print / share: Android print service, "Select a printer", paper size ISO A4, 1-page preview | Pass | p-75-folio-preview, p-76-print-dialog |
| R6 | Retire Room 403: in-app confirm "Retire Room 403?" (Back / Retire); "Room 403 retired · old records keep the name", list "26 rooms · 1 retired" | Pass | p-77, p-78 |
| R7 | OS font 1.3 (`font_scale 1.3`), Honour ON: body 18.2 px, `scrollWidth` 412; Honour OFF: 14 px at once and after `force-stop` + relaunch; `font_scale 1.0` | Pass | p-80, p-81, p-82 |
| R8 | Density 480 (360 css px): Appearance & display title two lines, icon-only DEMO pill, no horizontal scroll (360) | Pass (V15-4 label wrap seen) | p-90-360-appearance |

**Tablet `hcc_tablet_api35` (2560 x 1600, 1280 css px), demo APK**
| # | Check | Result | Evidence |
|---|---|---|---|
| T1 | Cold start with Dark + Large text kept from v15 data (theme dark, body 16.1 px, `scrollWidth` 1280), bars #AE4155 / #201916 | Pass | t-02, t-32 |
| T2 | Master configuration desktop layout: list and form side by side, Room types, Classic form | Pass | t-20..t-22 |
| T3 | Add floor "V16 test floor" -> "V16 test floor added" ("4 floors · 1 retired"); Retire with the themed confirm "Retire V16 test floor?" in Dark; list "3 floors · 2 retired" | Pass | t-40..t-43 |
| T4 | Text size Large (pref 1.15, body 16.1 px) survives `force-stop` and cold start | Pass | t-32 |

**Server APK on the tablet (HTTPS to `https://10.0.2.2:5443`, test CA, app data cleared first)**
| # | Check | Result | Evidence |
|---|---|---|---|
| S1 | Server address screen, Test connection: "Found WORKPC · version 0.1.0 at https://10.0.2.2:5443", Save and continue | Pass | t-50, t-51 |
| S2 | Owner password sign-in, "Register this device?" -> registered ("Front desk tabletTablet emulator v16", the default text was not cleared), Owner home "Welcome, Test Owner", "Server data as of ..." | Pass | t-52, t-53, t-54 |
| S3 | Lists load from the server: Rooms and rate plans shows 0 floors, 3 room types, 6 rooms | Pass | t-55 |
| S4 | Add floor "V16 server floor" (V16, 10) -> "V16 server floor added"; SQL: `mst.floor` row `V16 / V16 server floor / sort 10 / active` | Pass | t-56 |
| S5 | Dark saved, bars #AE4155 / #201916; after `force-stop` and relaunch still signed in, Dark, bars correct; 4 rotations keep them | Pass | t-57, t-58, t-59 |
| S6 | New reservation (room 101, Rahul Deshmukh) HCC-1101 + guided check-in "Checked in to room 101"; sign-out via the drawer, staff PIN sign-in, Owner password sign-in from the picker | Pass | t-61, t-62, t-80 |
| S7 | Customers lists the guest, Profile tab masked ID "XXXX-XXXX-2345", Show ID (FLAG_SECURE, see Wave D table) | Pass | t-80..t-82 |

## New defects
| ID | Sev | Summary | Repro / evidence / suggested cause |
|---|---|---|---|
| V16-1 | S3 | **The slip-photo camera never opens on Android 11+: the fallback file picker opens instead.** The bank-deposit photo field (`<input type="file" accept="image/*" capture="environment">`) calls Capacitor's `showImageCapturePicker`, which uses `Intent(ACTION_IMAGE_CAPTURE).resolveActivity(packageManager)`; with targetSdk 36 and no package-visibility declaration that returns null (log: `Media capture intent could not be launched. Falling back to default file picker`). The compressed photo still works from the gallery, but the designed camera path and the "original is deleted after compression" clean-up (`Pictures/`) are not exercised, and a photo taken in the camera app first stays in the public gallery. Every Android 11+ device is affected, demo and server APK | p-52, p-53 (picker). Fix verified with a throw-away build: add `<queries><intent><action android:name="android.media.action.IMAGE_CAPTURE"/></intent></queries>` to `04 Android\android\app\src\main\AndroidManifest.xml` -> camera opens, photo accepted, form shows 960 x 1280 / 18 KB, `Pictures/` removed (p-55, p-56, p-57). The change was NOT kept in this branch |
| V16-2 | S3 (API 35+) | **Status bar and gesture strip go back to maroon (#7A1F2B top and bottom) after the system night mode changes while the app theme is fixed to Dark.** Same family as V15-1: `uiMode` is in `configChanges`, so the activity is not recreated and the plugin's window background is lost; the web view's `resize` / `orientationchange` hook does not fire. Phone, demo APK: Dark chosen, `cmd uimode night no` -> bars #7A1F2B / #7A1F2B, `night yes` again does not restore them, a rotation or a cold start does (p-25: #AE4155 after rotation). Follow device is fine (the theme change re-applies the colours). Real-world trigger: Android's scheduled dark mode flipping at sunset while the hotel app is set to Dark. Not re-run on the tablet | p-22, p-23, p-24, p-30-dark-chosen-night-*. Suggested fix: also call `styleStatusBar(theme)` on the `matchMedia('(prefers-color-scheme: dark)')` change event, on `visibilitychange` / `focus`, or from `MainActivity.onConfigurationChanged` for `uiMode` |
| V16-3 | S4 (observation) | The V15-3 brief says the DEMO pill is icon-only on phones, but the rule is `<= 400 px`; on the 412 css px phone AVD the pill still reads "DEMO" (the title is still two clean lines). At 360 px it is icon-only. Only a defect if 412 px phones are meant to get the icon | p-13-appearance (412) vs p-90-360-appearance |

Still open from before: V15-4 (S4) now seen on a device at 360 px (see table). No S1 or S2 defects found.

## Not run
Physical devices (Galaxy Tab S7, Android 13); `productionRelease` APK (no keystore); server APK on the phone AVD and the phone layout of the server lists; slip-photo flow in the server APK (same component, not run); Owner password reset through the Master configuration UI (state made with `SetCredential`); Housekeeper DEC-061 view and the other staff roles on the server APK; V16-2 on the tablet; text size Extra large and "Honour OS font" on the tablet; clean-form Back and breadcrumb prompts on the phone sheet (tablet only); rotation prompts for the phone wizard; Rooms Renumber / Out of order; Paste rows apply; HK task checklist, refund, day close, CSV share; offline banner; real Wi-Fi address; System health page content (only opened); `LocalMachine\My` certificate hosting.

## Cleanup done
API stopped (ports 5211 / 5443 closed); DB `HccTest_APK16_20261027105303` dropped and verified (only the two other sessions' `HccTest_*` databases remain); scratch CA, server certificate/PFX, guest-ID key file + offline copy, generated Owner / staff credentials and PIN, diagnostic APK and test JPEG deleted (nothing was added to a Windows certificate store); emulators closed (`adb emu kill`, no emulator process left); the `<queries>` experiment was reverted. Nothing secret committed.
