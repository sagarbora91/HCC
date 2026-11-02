# HCC APK v19 — physical tablet test (prompt for a Claude Code session on the laptop)

Copy this file and the APK to the laptop, connect the tablet by USB, open Claude Code in the folder that holds them, and paste:

> Read `APK_v19_Tablet_Test_Prompt.md` in this folder and carry it out from start to finish. Write the report exactly in the format it asks for.

Everything below is for that Claude session.

---

## 0. What this is

> **Status:** the v19 APK is built on the hotel PC and has **not been built yet** (prepared 2 Nov 2026 in the cloud). Do not start until the APK `HCC-0.1.0-proto-debug-v19.apk` is in this folder. If `dumpsys` shows versionCode other than 19, stop and tell the user.

You are testing **Hotel Control Centre (HCC)** APK **v19** (demo build, package `in.hcc.prototype`, versionCode 19, versionName `0.1.0-proto`) on a **physical Android tablet** connected to this laptop over USB. The app is an internal hotel-operations prototype: a React single-page web app inside a Capacitor shell, with **fictional demo data** generated on the device (no server, no network needed). Until now it has only been tested on Android emulators (API 35 phone and tablet AVDs); this is the first run on real hardware, so **anything you notice about speed, touch, layout, printing, sharing and the camera is valuable**, not only the listed checks.

The results go into **one Markdown report** (section 9) that the project lead's Claude session will read back on the work PC and act on. Be factual: a FAIL is a FAIL with the evidence; something you did not do is NOT RUN. Never describe a result you did not observe.

Rules on this device:
- Do not uninstall, sign out of, or change anything in other apps. Do not open personal data (photos, messages, accounts). The only Android settings you may change are the ones listed in section 2, and you restore them in section 8.
- Install only the HCC APK named below. Do not download anything.
- The app contains fictional hotel data only; feel free to add rooms, bookings, photos (of anything harmless, e.g. a desk) inside it.
- One device, one app. Stop and ask the user if the device is not a tablet, is not authorised for adb, or the APK is not in this folder.

## 1. Files in this folder

| File | Purpose |
|---|---|
| `HCC-0.1.0-proto-debug-v19.apk` | the build under test (demo data on device; debuggable) |
| `APK_v19_Tablet_Test_Prompt.md` | this prompt |

You create: `HCC_APK_v19_Tablet_Report_<yyyy-mm-dd>.md` (the report, section 9) and a `shots/` folder with every screenshot and logcat excerpt it cites, zipped at the end as `HCC_APK_v19_Tablet_Evidence_<yyyy-mm-dd>.zip`.

## 2. Set-up and device facts

Tools: `adb` (Android platform-tools). If `adb` is not on PATH, look in `%LOCALAPPDATA%\Android\Sdk\platform-tools\` (Windows) or `~/Library/Android/sdk/platform-tools/` (macOS) and use the full path. If adb is missing entirely, tell the user how to get platform-tools and stop.

```bash
adb devices -l                       # exactly one device, state "device" (not "unauthorized" — then the user taps Allow on the tablet)
adb shell getprop ro.product.model ; adb shell getprop ro.product.manufacturer
adb shell getprop ro.build.version.release ; adb shell getprop ro.build.version.sdk ; adb shell getprop ro.build.fingerprint
adb shell wm size ; adb shell wm density
adb shell settings get system font_scale ; adb shell settings get system accelerometer_rotation ; adb shell settings get system user_rotation
adb shell cmd uimode night            # current night mode
```
Record all of this in the report's "Device" table, including the **css px width** = physical width ÷ (density ÷ 160). Note the API level: on **API 35+** the status bar is painted by the app's own `StatusBarColor` plugin; **below API 35** (Android 13 / 14) the standard StatusBar plugin sets the colour and the bottom navigation bar is NOT painted by the app — so on Android 13/14 only the **top** status bar colour is expected to follow the theme. Say which case applies.

Remember the starting values of `font_scale`, `accelerometer_rotation`, `user_rotation` and the night mode: you put them back in section 8.

Install and verify:
```bash
adb install -r -g HCC-0.1.0-proto-debug-v19.apk
adb shell dumpsys package in.hcc.prototype | grep -E "versionCode|versionName"      # expect versionCode=19 versionName=0.1.0-proto
adb shell pm clear in.hcc.prototype                                                  # fresh demo data
adb shell monkey -p in.hcc.prototype -c android.intent.category.LAUNCHER 1          # launch
```

## 3. How to drive and observe the app

- **Screenshots:** `adb exec-out screencap -p > shots/<name>.png`. Name them `<section>-<nn>-<what>.png` (e.g. `B-03-dark-after-rotation.png`). Look at every screenshot you take (open the image) before you judge a check.
- **Taps / typing:** `adb shell input tap X Y`, `adb shell input text 'Hello'` (spaces as `%s`), `adb shell input keyevent KEYCODE_BACK` (hardware Back), `KEYCODE_APP_SWITCH` (recents), `KEYCODE_HOME`. Find coordinates from a screenshot. `adb shell uiautomator dump /sdcard/ui.xml && adb pull /sdcard/ui.xml` gives the native view tree, but the app is a WebView, so screenshots are your main sense.
- **Rotation:** `adb shell settings put system accelerometer_rotation 0` then `adb shell settings put system user_rotation 1` (landscape), `0` (portrait), `3` (reverse landscape). Wait 1.5 s before a screenshot.
- **System night mode:** `adb shell cmd uimode night yes` / `no`. Wait 1.5 s.
- **System font size:** `adb shell settings put system font_scale 1.3` / `1.0`.
- **Logcat:** `adb logcat -c` to clear, then after a step `adb logcat -d > shots/<name>-logcat.txt`; grep it for `Capacitor`, `Media capture`, `chromium`, `Uncaught`, `Refused`, `E/`.
- **Pixel colours** (status-bar checks): read pixels from the PNG. Windows PowerShell:
  ```powershell
  Add-Type -AssemblyName System.Drawing
  $b=[System.Drawing.Bitmap]::FromFile((Resolve-Path shots\B-01.png)); $c=$b.GetPixel(40,12); '#{0:X2}{1:X2}{2:X2}' -f $c.R,$c.G,$c.B; $b.Dispose()
  ```
  macOS/Linux with Python: `python3 -c "from PIL import Image;im=Image.open('shots/B-01.png');print('#%02X%02X%02X'%im.getpixel((40,12))[:3])"` (install Pillow if needed). Read the **top bar** at about (40, 12) — avoid the clock and icons — and, on API 35+ only, the **bottom strip** at (width/2, height-8). Record the exact values.

Expected theme colours (top status bar): **Light #7A1F2B**, **Dark #AE4155**, **High contrast #5E1620**. Expected bottom strip on API 35+ only: Light #FFFFFF, Dark #201916, High contrast #FFFFFF. Small deviations (±2 per channel) from display colour management are fine; say what you read.

Where things are in the app (Owner role sees everything): the first screen is **"Who is using this device?"** → pick **Owner** (demo sign-in, no password). **Modules** home lists module tiles; a **user menu** (avatar, top right) has **Appearance & display** (text size, density, theme Light / Dark / High contrast / Follow device, "Honour OS font size") and sign-out. **Admin & Setup → Master configuration** holds the masters (Rooms and rate plans → Room types etc.). **Rooms** (floors and rooms, add by range, retire), **Reservations / New booking**, **Arrivals & check-in**, **Room board**, **Cashier** (bank deposit with slip photo), **Customers** (profile → Show ID), **Approvals & audit**. On a wide screen the app uses its **desktop layout** (side navigation, tables, breadcrumb "Modules Home"); on a narrow screen the **phone layout** (bottom tab bar, app bar with a title). A tablet in landscape should show the desktop layout; in portrait it may still be desktop — record which you see at each orientation.

## 4. Checks — FIT layout (section B in the report)

FIT removed page scrolling at tablet landscape: a page fits the screen, lists scroll inside their own panel, sub-screens are tabs, h1 is 22 px. Owner role, landscape (`user_rotation 1`), Galaxy Tab S7 (about 1138 x 640 css px expected; record yours).

| # | Step | Expect | Evidence |
|---|---|---|---|
| B1 | Landscape. Visit each of: Overview, Rooms, Reservations, Arrivals & check-in, Folios, Cashier, Cash book, Expenses, Housekeeping, Customers, Messages, Approvals & audit, Reports, Accounting, Master configuration. Swipe the page body (`adb shell input swipe 600 400 600 100 300`) | the page itself does not scroll; header and tabs stay put; a long list scrolls only inside its panel; nothing cut at the right or bottom edge | B-01..15 |
| B2 | On three pages, compare the page title with body text | h1 is visibly about 22 px (not 30), smaller than before but still the largest text; body text not reduced | B-16 |
| B3 | On pages with tabs (e.g. Reservations, Accounting, Master configuration, Cashier): tap each tab | tabs render in one row (or scroll sideways inside the tab strip), switch instantly, selected tab clearly marked, content fits without page scroll | B-17..19 |
| B4 | **V17-1 (modal footer after rotation):** landscape, Reservations, New booking; open step 2, tap a text field (keyboard opens), close the keyboard with Back, screenshot. Rotate to portrait, then back to landscape, screenshot each time. Repeat with Customers, a guest, an edit sheet | the footer buttons (Continue / Confirm) stay fully visible and tappable; no gap or clipped strip at the bottom; the modal body scrolls inside the modal; tapping the footer does not open "Discard changes?" | B-20..25 |
| B5 | Portrait: repeat B1 on four pages | the app may show the desktop or phone layout; record which; no horizontal scroll; nothing cut | B-26..29 |
| B6 | Extra large text (Appearance & display) on Reservations and Room board, landscape | no horizontal scroll, nothing cut, lists still scroll in their panel | B-30, B-31 |
| B7 | Open items from the FIT report: Reservations list shows about 5.8 rows at tablet landscape; compact stat strips on Dashboard and Accounting (last card edge cut?); Approvals list rows | note what you see; a cut last stat card or a list showing under 4 rows is an S4 | B-32..34 |

Put text size back to Default afterwards.

## 5. Checks — room keys (section C in the report)

The key encoder is a **mock** (no real door lock is touched). Role Owner or Reception.

| # | Step | Expect | Evidence |
|---|---|---|---|
| C1 | Front desk group, **Room keys** page, landscape then portrait | page lays out without page scroll in landscape; the mock-encoder badge is shown; the list of in-house stays scrolls in its panel; phone layout shows cards, no sideways scroll | C-01, C-02 |
| C2 | Open a checked-in booking (create one as in F3/F4 if needed) and open its **key panel** ("Room key · room NNN") | panel shows the encoder badge, key counts, Active cards, Action, and Key log for this booking; footer visible | C-03 |
| C3 | Issue a key (mock), then a duplicate, then cancel/extend if offered | key log gains a line each time; counts update; messages clear; no crash | C-04..06 |
| C4 | Rotate with the key panel open | panel survives; footer still visible (V17-1) | C-07 |
| C5 | Keyboard/Back: press hardware Back with the panel open | panel closes (or asks first, record which); no exit from the app | C-08 |

## 5b. Checks — defects needing a device (section D in the report)

From `F2-apk19-fix-DONE.md`. A source fix exists for each; only a device can confirm. `adb logcat -c` before each step.

| # | Step | Expect | Evidence |
|---|---|---|---|
| D-V17-1 | **Insets lost until a rotation (S3).** Cold start, then without rotating: open a modal, a dialog and cause a toast. Also after `cmd uimode night yes/no` and after opening and closing the keyboard. Read `window.innerHeight` is not available, so judge by clipping: footer, toast and dialog buttons fully visible | no clipping at any time; no layout jump when you later rotate (previously innerHeight flipped 744/800 on the tablet AVD) | D-01..04 |
| D-V17-2 | **resize at idle (S4).** Leave the app idle on Overview for 30 s, `adb logcat -d` | no steady stream of resize or status-bar repaint lines (before: about 20 a second). Note whether USB debugging itself was attached; if possible repeat after `adb disconnect`/unplugged over Wi-Fi adb | D-05 (logcat) |
| D-V17-3 | **Transient wrong bar colours (S4).** Switch Dark to Light to Dark; flip night mode; rotate. Take screenshots at 0.3 s, 1 s and 3 s | bar colours match the theme (Light #7A1F2B, Dark #AE4155, High contrast #5E1620; bottom strip on API 35+ #FFFFFF / #201916 / #FFFFFF) at 1 s at the latest; record any wrong colour and how long it lasted | D-06..08 |
| D-V17-4 | **Room board date line (S4, phone layout).** If the tablet shows the phone layout (portrait or compact switch), open Room board | app-bar line reads "Business date <d Mon> · Open" with the status word intact; a long date ellipsises, the status word never does | D-09 (or NOT APPLICABLE) |
| D-V15-4 | **"Approvals" cut mid-word (S4).** Phone layout, 360 px width if the device or a narrow window allows (`adb shell wm size 800x1280` then restore with `wm size reset`; restore in section 8) | bottom tab labels do not break mid-word; "Approvals" whole | D-10 (or NOT APPLICABLE) |
| D-V16-3 | **DEMO pill at 412 px (observation).** Phone layout near 412 px | note whether the DEMO pill is shown or hidden; no fix expected, owner decides | D-11 |

## 5c. Checks — themes, bars, camera (section E in the report)

Carried from v17; all PASS expected, run quickly.

| # | Step | Expect | Evidence |
|---|---|---|---|
| E1 | Themes Light, Dark, High contrast, Follow device: screenshot each in portrait and landscape; `cmd uimode night no/yes` once under Dark | top bar #7A1F2B / #AE4155 / #5E1620; Follow device follows night; Dark stays dark through night flips | E-01..08 |
| E2 | Cashier, Bank deposit, slip photo field | the real camera app opens; photo accepted; logcat has no `Media capture intent could not be launched`; `run-as in.hcc.prototype ls -la files/Pictures` empty or absent | E-09, E-10 |
| E3 | Customers, a guest with an ID, Show ID; `adb exec-out screencap` | screencap solid black while the number shows, recents black, normal after Hide ID | E-11..13 |
| E4 | Master configuration, Rooms and rate plans, Room types, open a row, type ` V19`, hardware Back | "Discard changes?" dialog; Keep editing keeps text; Discard leaves and reverts | E-14, E-15 |

## 6. Checks — regression on real hardware (section F)

| # | Step | Expect | Evidence |
|---|---|---|---|
| F1 | `pm clear`, launch, time from launch to Modules home (count the loading screen); sign in as Owner, then sign out and in as Reception and Cashier | all three sign in; note the cold-start time and whether the loading screen shows progress | D-01..03 |
| F2 | Rooms: add rooms **401–403** by range (floor 4 or any), then **Retire room 403** | in-app confirm dialog (not a browser alert); list shows the retired count | D-04, D-05 |
| F3 | New booking as Reception for room 401 (tomorrow, 2 nights), guest "Test Guest", review, confirm; booking ID shown | booking created (HCC-11xx) | D-06, D-07 |
| F4 | Arrivals & check-in → guided check-in of that booking (ID type any, skip advance) | "Checked in" message, Room board shows 401 occupied | D-08, D-09 |
| F5 | Folio → **Print / share** | the **Android print dialog** opens (choose Save as PDF, A4, one page) — save to Downloads, note the file name, then delete it | D-10 |
| F6 | Any list with **CSV / export** (e.g. Rooms or Approvals & audit export) | the Android **share sheet** opens with a `.csv`; cancel | D-11 |
| F7 | Rotate during the booking wizard (step 2) | the step survives the rotation with the entered data | D-12 |
| F8 | Hardware Back through: a sheet → the module → Modules home → one more Back | Back closes the sheet, then goes up; on home the app asks or exits cleanly (record which) | D-13 |
| F9 | Scroll long lists (Room board, Approvals & audit, Customers) by swipe (`adb shell input swipe 600 1500 600 500 300`) | smooth, no blank frames or lag you can see in screenshots; note subjective speed | D-14 |
| F10 | Leave the app in the background 2 minutes, return | state kept, no reload to the loading screen | D-15 |
| F11 | Airplane mode on and off (`adb shell cmd connectivity airplane-mode enable` / `disable`) while using the app | the demo app keeps working (it needs no network); note any banner | D-16 |

## 7. Anything else you notice (section G)

Write down every oddity, even small: touch targets too small, text cut, a dialog off-centre, keyboard covering a field, slow screens, colour that looks wrong, a wrong word. Each with a screenshot.

## 8. Restore the device

```bash
adb shell settings put system font_scale <starting value>
adb shell settings put system user_rotation <starting value>
adb shell settings put system accelerometer_rotation <starting value>
adb shell cmd uimode night <starting value: yes / no / auto>
adb shell cmd connectivity airplane-mode disable
```
Delete the PDF you saved in D5 (`adb shell ls /sdcard/Download | grep -i hcc`, then `adb shell rm`). Leave HCC installed (the user may keep using it) unless the user asks otherwise. Do not run `pm clear` at the end: the lead may want to look at the data.

## 9. The report — `HCC_APK_v19_Tablet_Report_<yyyy-mm-dd>.md`

Use exactly these headings so the lead's session can parse it:

```markdown
# HCC APK v19 — physical tablet report
Date · tester: Claude (laptop session) · PHYSICAL DEVICE · APK HCC-0.1.0-proto-debug-v19.apk (SHA-256 of the file you installed)

## Device
| Model | Manufacturer | Android | API | Fingerprint | wm size | density | css px width | Status-bar case (plugin API 35+ / StatusBar below 35) |

## Install
versionCode / versionName from dumpsys, install command result, launch OK.

## B. FIT layout
| # | Result (PASS / FAIL / NOT RUN) | Observed (exact colours read, what was on screen) | Evidence files |

## C. Room keys (mock encoder)
same table

## D. Device-only defects (V17-1..4, V15-4, V16-3)
same table

## E. Themes, bars, camera, ID

same table

## F. Regression
same table (include the cold-start seconds in F1)

## G. Other observations
numbered list, each with a screenshot name

## Defects
### D19-1 (S1–S4) short title
Symptom · Steps to reproduce · Expected · Observed · Evidence · logcat lines (if any)
(S1 = blocks use, S2 = wrong data / money, S3 = feature broken with a workaround, S4 = cosmetic)

## Not run
what and why

## Restored settings
the values you put back

## Files
list of shots/*.png and *.txt, and the zip name
```

Then zip `shots/` and the report into `HCC_APK_v19_Tablet_Evidence_<yyyy-mm-dd>.zip` and tell the user: the report path, the zip path, how many PASS / FAIL / NOT RUN, and the defect list in one line each. The user will copy the report and the zip to the work PC (`05 Delivery Evidence\Acceptance\device-v19\`) for the lead's session to act on.
