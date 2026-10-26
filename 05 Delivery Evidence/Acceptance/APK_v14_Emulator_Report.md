# APK v14 emulator report (demo APK on phone and tablet, server APK on the tablet against the real server)

Date 27 Oct 2026 · tester: Claude (Fable 5.1 lead, Sonnet 5.5 agent) · EMULATED, not physical devices · branch `claude/apk14` (= main deddbf9, masters wave MA-0..MA-11, customisation Part 1, colour tokens and themes) · screenshots in `v14\` (`p-*` phone, `t-*` tablet, `t-2x/3x` server APK on the tablet). The only change besides this report is `versionCode 13 -> 14` in `04 Android\android\app\build.gradle`. No product code changed.

## Builds (versionCode 14, `dumpsys package` on both AVDs)
| APK | Package | SHA-256 | Size | Notes |
|---|---|---|---|---|
| `APK\HCC-0.1.0-proto-debug-v14.apk` | `in.hcc.prototype` | `D77CA044A1A66476628BEB2F7DF04E5BE2823C66BCD7BB5883F4A4A864835F48` | 6,170,032 | `assembleDebug`, demo data on device, version name `0.1.0-proto` |
| `APK\HCC-0.1.0-server-debug-TESTCA-v14.apk` | `in.hcc.prototype.server` | `C69843B81F88B8E75CBD3F7258A5C8DE9962FEB59DF759D3A3E9244819357AC9` | 6,260,019 | `assembleProductionDebug` with a THROWAWAY test CA (CN "HCC APK14 Throwaway Test CA", 30 days, SHA-256 3F:17:46:71:...:19:3B), HTTPS only, debuggable. Test use only |
| `productionRelease` | `in.hcc.server` | not built | - | needs the hotel release keystore; not built here (v12/v13 already verified the release path with a throwaway keystore; nothing in the release config changed) |

- JDK 21.0.12.1 (`C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot`), Gradle with `--offline` (cache was filled, no Norton trust-store workaround needed), node 25.9.0.
- Sequence followed: `npm run build`, `npm run build:production` (03 Prototype; web builds 2,809 KB / 3,052 KB), `npm ci` in `04 Android`, `copy-web.mjs` + `cap sync android` (7 plugins incl. Preferences), `assembleDebug` (BUILD SUCCESSFUL, 1m18s), `build-production.mjs --ca <test CA>` + `assembleProductionDebug` (BUILD SUCCESSFUL).
- `*.apk` is git-ignored (`git ls-files "05 Delivery Evidence/APK"` lists only PNG and one `.md`), so the APKs stay untracked. Older APKs are not in this worktree (they live in the main checkout, untouched).
- The test CA key, server certificate/PFX and the generated credentials were created in the session scratch folder (outside the repo) and deleted at the end. Nothing secret is committed.
- Android SDK on this PC is `E:\Android\Sdk` (the task text said `C:\Users\Sagar\AppData\Local\Android\Sdk`, which does not exist; `ANDROID_HOME` is already set to the E: path). `04 Android\README.md` still says the C: path (documentation only).

## Server set-up (for the server APK)
Disposable DB `HccTest_APK14_20261027012921` on `.\SQLEXPRESS` (migrated 0 -> 3800 with `Hcc.Api migrate`), API `dotnet run -c Release` (Development) with Kestrel HTTP `127.0.0.1:5211` (setup scripts) and HTTPS `0.0.0.0:5443` from the throwaway PFX; setup with the W4_E2E2 scripts (SetupProperty, 11 sample masters incl. POS, GSTIN, staff PINs; the sample floors list was NOT imported, so the server has 0 floors). DB dropped, API stopped, emulators killed at the end (two other sessions' `HccTest_2026102612...` / `...200351...` DBs were left untouched).

## Phone AVD `codex_api35_google` (412 css px, 1080x2400), demo APK
Driving: real taps via `adb input` for sheets/Back/system, DOM clicks through the debuggable web view (CDP) for fields and list buttons. Owner "Sagar" unless stated.

### New v14 checks
| # | Check | Result | Evidence |
|---|---|---|---|
| P1 | Master configuration > Rooms and rate plans > Lists block > **Floors**: open, add "Ground floor", edit sheet, **retire** (toast "retired - old records keep the name"), **reorder** with the arrow buttons, **Paste rows** preview (1 to add, 1 to change, 1 refused; nothing applied) | Pass | p-01..p-10 |
| P2 | Reorder shows on the room board: floor chips "Floor 1, Floor 3, Floor 2", retired floor gone | Pass | p-08, p-12-room-board-new-floor-order |
| P3 | **Room types**: add "Family room" (4 adults, Rs 3,500 rate), reorder, retire. No "Paste rows" button on this list | Pass (note V14-7) | p-19..p-23 |
| P4 | **Rooms**: Add rooms range `401-403` (Floor 3, Deluxe) -> "Added 401-403 (3 rooms)", list 24 -> 27 rooms, room 403 retired (26 rooms, 1 retired). Renumber and Set out of order panels present; no reorder/paste on Rooms | Pass | p-13..p-18 |
| P5 | **Payment methods** (Money and tax): add "Wallet app" (kind wallet, settles to bank, ledger 1030), reorder, retire, Paste rows preview (1 to add, 1 unchanged, 1 refused) | Pass | p-24..p-30 |
| P6 | **Outlets** (Restaurant and POS): add "Rooftop cafe" (bar, 18:00-23:30), reorder, retire. Paste rows button present but its preview was not run | Partial (paste preview not run) | p-31..p-34 |
| P7 | **Menu items**: duplicate code/name refused with clear messages; add "Jalebi plate" (Restaurant, veg), reorder, retire (21 + 1 retired), Paste rows preview (1 to add, 1 refused); a wrong column header is refused with "Required column category is missing" | Pass | p-35..p-39 |
| P8 | Appearance & display: text size **Extra large** (1.3, body 18.2 px). Room board and booking wizard readable, no horizontal page scroll (scrollWidth 412 = viewport); chip rows scroll sideways as designed | Pass with note (V14-4: some titles truncate) | p-41..p-43b |
| P9 | Theme **Dark**: app bar, cards, status chips (Ready/Occupied/Dirty with labels), dashboard, breadcrumbs readable; FAB and bottom tabs fine. **Status bar stays maroon #7A1F2B** while the app bar is #AE4155 | Partial (V14-2) | p-44..p-47 |
| P10 | Theme **High contrast**: white surfaces, thick outlines, chips readable. Status bar #7A1F2B instead of #5E1620 | Partial (V14-2) | p-48, p-49 |
| P11 | Theme **Follow device**: `adb shell cmd uimode night yes` -> `data-theme=dark` live (prefers-color-scheme true); `night no` -> light again; stored value stays `system` | Pass | p-50, p-51 |
| P12 | OS font size **Largest** (`font_scale 1.3`), "Honour the phone's system font size" ON: activity recreated, TextZoom `{systemScale 1.3, honour true, zoom 130}`, body 18.2 px, Modules page readable, no horizontal scroll (the app returns to the Modules page after the recreate) | Pass | p-52 |
| P13 | Honour switch OFF at 1.3: zoom 100, body 14 px at once; after `force-stop` + relaunch still OFF (checkbox unticked, zoom 100); ON again -> zoom 130; `font_scale` reset to 1.0 -> zoom 100, honour true | Pass | p-53..p-55 |
| P14 | Sign out and sign in as Meera (Dark + Large set before): theme dark and `--hcc-text` 1.15 unchanged, prefs JSON identical | Pass | p-56..p-59 |
| P15 | Android Back closes a clean master sheet and stays on Master configuration; on a sheet with typed text, Back/Cancel opens a native "Discard the details you entered?" confirm (OK discards) | Pass with note (V14-1) | p-10b, p-11, p-30b |
| P16 | **Housekeeper with `masters` granted** (Role templates > Housekeeper > Masters, done on the desktop layout of the phone, then back to Mobile): Master configuration shows all 12 groups "Read only"; rooms group says "Tariffs and the GST master are hidden - this role does not see hotel money (DEC-061)", no "Open Tariff & yield", no "Schedule tariff", rate rounding shows "Rs ••", 0 rupee amounts on the page; Room types form has no Amount/From fields (money masked "Rs ••"); list rows still editable (Add room type, move arrows) | Pass | p-60..p-68 |

### Regression from the v12/v13 lists that still applies
| # | Check | Result | Evidence |
|---|---|---|---|
| P17 | Role switch through the DEMO sheet (Reception, Housekeeper, Maintenance, Owner): tiles and bottom tabs per role (Reception: Rooms/Arrivals/Bookings/Folios; Housekeeper: My tasks/Report repair; Maintenance: Work orders/My repairs) | Pass | p-70..p-72 |
| P18 | New booking wizard 4 steps as Reception, including the NEW room 401 (1-3 Dec, Rs 3,000/night, Rs 6,000 + GST); confirmed as HCC-1186; booking sheet opens | Pass | p-73..p-78 |
| P19 | Guided check-in 6 steps (verify guest with driving-licence format check, room, tariff confirm, advance, check in) for Ishaan Bose, room 103, HCC-1049 | Pass | p-79..p-85 |
| P20 | Folio preview: CGST/SGST columns, "Not yet issued", disclaimers; Print / share opens the Android print service with "ISO A4" (uiautomator) | Pass | p-88..p-90 |
| P21 | Back chain from the folio preview closes sheet after sheet to Modules, then the "Exit Hotel Control Centre?" prompt (Stay / Exit) | Pass | p-91, p-92 |
| P22 | Phone landscape (2400x1080): phone shell, 3-column tiles, bottom tabs, no clipping (the CDP viewport read 0x0 in this state, a tooling quirk; judged from the screenshot) | Pass | p-93 |

Phone counts: 22 checks; pass 19 (two with a note), partial 3 (P6, P9, P10), fail 0.

## Tablet AVD `hcc_tablet_api35` (2560x1600, 1280x744 css), demo APK
| # | Check | Result | Evidence |
|---|---|---|---|
| T1 | Cold start, Owner sign-in, desktop layout (hamburger, breadcrumb, module grid), not the phone shell | Pass | t-01, t-02 |
| T2 | Master configuration > Rooms and rate plans: **Lists** block with chips (Floors, Amenities, Room types, Rooms, Occupancy..., Meal plans) beside the tariff table; two-pane list + form | Pass | t-03, t-04 |
| T3 | Floors: Add floor form in the right pane, "Annexe tablet" added, move arrows present | Pass | t-05, t-06 |
| T4 | Appearance & display: theme Dark + text size Large (1.15, body 16.1 px), no horizontal scroll | Pass | t-07, t-08 |
| T5 | Payment methods Add form in Dark + Large: readable, labels and hints visible | Pass | t-09 |
| T6 | **Rotation to portrait (800 css px) with the form open** | **Fail (V14-3)**: the form pane is squeezed to a strip at the right edge, the Code input is 26 px wide at x=717 of 800, the title wraps one word per line; the form cannot be used | t-10 |
| T7 | Back to landscape: typed values ("TABPAY", "Tablet pay") and Dark theme kept | Pass | t-11 |
| T8 | Room board in Dark + Large on the tablet (floor groups, cards, status dots with labels) | Pass | t-12 |

Tablet counts: 7 pass, 1 fail.

## Server APK on the tablet AVD (HTTPS to `https://10.0.2.2:5443`, test CA)
Phone-sized server APK check was not run (see Not tested); the tablet AVD hosted this section after the demo checks (app data cleared first, because the install kept the v13 registration).
| # | Check | Result | Evidence |
|---|---|---|---|
| S1 | Server address screen, Test connection: "Found WORKPC - version 0.1.0 at https://10.0.2.2:5443", Save and continue | Pass | t-21, t-22 |
| S2 | Owner password sign-in, "Register this device?" -> registered "Tablet emulator v14", Owner home with "Server data as of ..." | Pass | t-23..t-25 |
| S3 | Master configuration lists load **from the server**: Rooms and rate plans shows 3 room types, 6 rooms, 0 floors (server data, not the demo fixture); "Server and this device" card shows address and registration | Pass | t-26, t-27 |
| S4 | Add floor "Ground floor" (G, 10) from the app -> "Ground floor added", list shows 1 floor; SQL check: `mst.floor` row `G / Ground floor / sort 10 / active` | Pass | t-28 |
| S5 | Appearance & display on the server APK: Dark + Large saved; after `force-stop` and relaunch still signed in, Dark kept | Pass | t-29..t-31 |
Server counts: 5 pass, 0 fail.

## What changed since v13 (APK v13 = main f25b519)
- Masters wave (MA-0..MA-11): Master configuration now has a **Lists** block in every group (floors, rooms with range add / renumber / out of order, room types, amenities, meal plans, extras, discount reasons, payment methods, bank accounts, expense categories and vendors, outlets, tables, menu categories and items, booking sources ... ) with add, edit, retire, reorder and **Paste rows** (CSV preview before applying); server payment methods through `mst.payment_method`; DEC-061 money mask in the hub.
- Customisation Part 1: per-device **Appearance & display** page (text size 90-130 %, density, motion, start page, page size, toast time), **themes** light / dark / high contrast / follow device on colour tokens, status-bar colour per theme (native, see V14-2), Android **TextZoom** plugin and the "Honour the phone's system font size" switch.
- Build: `versionCode 14`; JDK 21; SDK path is `E:\Android\Sdk`.

## New defects and observations (no product code changed)
| ID | Sev | Summary | Repro / evidence |
|---|---|---|---|
| V14-1 | S4 | A master sheet with typed text, closed with Cancel or the Android Back button, asks "Discard the details you entered?" through the browser `confirm()`: an unthemed native Android dialog (teal CANCEL / OK) that also blocks the web view (same family as APK13-6) | Paste rows, type, Cancel or Back (p-10b, p-30b) |
| V14-2 | S3 | The Android status bar does not follow the theme. Dark theme: status bar stays #7A1F2B while the app bar is #AE4155; High contrast: #7A1F2B instead of #5E1620; the bottom gesture-bar strip also stays maroon. `StatusBar.setBackgroundColor` has no effect on this API 35 emulator (targetSdk 36 edge-to-edge): calling the plugin directly with #00ff00 changed nothing. The "status bar colour changes" part of the CP-5 spec does not hold on Android 15+ | Pixel sample of p-44 at (10,10) = #7A1F2B vs (10,150) = #AE4155; also seen on the tablet (t-08, t-12) and the server APK |
| V14-3 | S3 | Tablet in **portrait** (800 css px): the Master configuration lists use a three-column layout (settings groups, list, form); the form pane is squeezed to about 120 px, the input is 26 px wide and clipped at the right edge, so adding or editing a master row is impossible until the tablet is turned back to landscape (typed values are kept). The page itself does not scroll sideways (scrollWidth 800) so the clipping is silent | Master configuration > Money and tax > Payment methods > Add, rotate (`user_rotation 1`) (t-10) |
| V14-4 | S4 | Extra large text: some labels truncate with "..." - app-bar title "Room boa...", subtitle "Business date 2", bottom tab "Appro...", wizard step label "Dates & pa...". Nothing overlaps and nothing scrolls sideways | p-42, p-43 |
| V14-5 | S3 (needs a developer look) | Rooms added in Master configuration (401, 402, 403) show on the room board as Ready, but New booking for 24-26 Oct lists them under "excluded - see why" with "Booked - another reservation" (also the retired 403), while for 1-3 Dec they are available and bookable (HCC-1186 on room 401). The reason is the "defensive" fallback in `resExclusion` (available() is false with no clash found), probably a group-block hold from the FY26 history that is allocated by room type, or another hold provider; the reason text hides the real cause and a retired room should not be listed at all | New booking, default dates, "14 rooms excluded" list (p-73; the list text was read via the DOM) |
| V14-6 | S4 (observation) | "Retire" on a list row acts at once with no confirmation (only a toast "... retired - old records keep the name"); the row moves to the Retired tab. Acceptable if reinstating is easy, but one stray tap on the bottom of an edit sheet retires the row | p-07 |
| V14-7 | S4 (observation) | The list tools are not uniform: Floors, Payment methods, Outlets, Menu items have Paste rows + reorder; Room types has reorder but no Paste rows; Rooms has range add / Renumber / Out of order but no Paste rows and no reorder | p-13, p-19 |
| V14-8 | S4 (observation, doc) | `04 Android\README.md` gives the Android SDK as `C:\Users\Sagar\AppData\Local\Android\Sdk`; on this PC it is `E:\Android\Sdk`. The README build steps otherwise worked unchanged (JDK 21, `npm ci`, `--offline`) | - |

No S1 or S2 defects found. Counts by severity: S3 x3 (V14-2, V14-3, V14-5), S4 x5 (V14-1, 4, 6, 7, 8).

## Not tested
Physical devices; the `productionRelease` APK (no keystore here; not built, not installed); server APK on the phone AVD and the phone layout of the server lists; staff PIN sign-in, sign-out/in and the Housekeeper DEC-061 view on the server APK (done on the demo APK only); text-size Extra large and High contrast on the tablet; Follow device on the tablet; outlets Paste rows preview; Paste rows apply (only previews were run, nothing was applied); Rooms "Renumber" and "Set out of order" actions; HK task checklist, refund and day-close, CSV share, 360 px width, offline banner (all unchanged since v13 and not in the v14 diff area); real Wi-Fi address; `LocalMachine\My` certificate hosting.
