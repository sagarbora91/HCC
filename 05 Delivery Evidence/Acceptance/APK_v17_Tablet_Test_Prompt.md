# HCC APK v17 — physical tablet test (prompt for a Claude Code session on the laptop)

Copy this file and the APK to the laptop, connect the tablet by USB, open Claude Code in the folder that holds them, and paste:

> Read `APK_v17_Tablet_Test_Prompt.md` in this folder and carry it out from start to finish. Write the report exactly in the format it asks for.

Everything below is for that Claude session.

---

## 0. What this is

You are testing **Hotel Control Centre (HCC)** APK **v17** (demo build, package `in.hcc.prototype`, versionCode 17, versionName `0.1.0-proto`) on a **physical Android tablet** connected to this laptop over USB. The app is an internal hotel-operations prototype: a React single-page web app inside a Capacitor shell, with **fictional demo data** generated on the device (no server, no network needed). Until now it has only been tested on Android emulators (API 35 phone and tablet AVDs); this is the first run on real hardware, so **anything you notice about speed, touch, layout, printing, sharing and the camera is valuable**, not only the listed checks.

The results go into **one Markdown report** (section 9) that the project lead's Claude session will read back on the work PC and act on. Be factual: a FAIL is a FAIL with the evidence; something you did not do is NOT RUN. Never describe a result you did not observe.

Rules on this device:
- Do not uninstall, sign out of, or change anything in other apps. Do not open personal data (photos, messages, accounts). The only Android settings you may change are the ones listed in section 2, and you restore them in section 8.
- Install only the HCC APK named below. Do not download anything.
- The app contains fictional hotel data only; feel free to add rooms, bookings, photos (of anything harmless, e.g. a desk) inside it.
- One device, one app. Stop and ask the user if the device is not a tablet, is not authorised for adb, or the APK is not in this folder.

## 1. Files in this folder

| File | Purpose |
|---|---|
| `HCC-0.1.0-proto-debug-v17.apk` | the build under test (demo data on device; debuggable) |
| `APK_v17_Tablet_Test_Prompt.md` | this prompt |

You create: `HCC_APK_v17_Tablet_Report_<yyyy-mm-dd>.md` (the report, section 9) and a `shots/` folder with every screenshot and logcat excerpt it cites, zipped at the end as `HCC_APK_v17_Tablet_Evidence_<yyyy-mm-dd>.zip`.

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
adb install -r -g HCC-0.1.0-proto-debug-v17.apk
adb shell dumpsys package in.hcc.prototype | grep -E "versionCode|versionName"      # expect versionCode=17 versionName=0.1.0-proto
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

## 4. Checks — themes and bars (section B in the report)

Fresh launch, Owner. For each theme chosen under Appearance & display:

| # | Step | Expect | Evidence |
|---|---|---|---|
| B1 | Light (default): screenshot portrait | top bar #7A1F2B, light icons | B-01 |
| B2 | Choose **Dark**; screenshot | top #AE4155 (API 35+: bottom #201916); the whole UI dark, text readable | B-02 |
| B3 | Rotate landscape → portrait → reverse landscape (3 shots) | colours unchanged after each (V15-1) | B-03..05 |
| B4 | Recents (`KEYCODE_APP_SWITCH`), screenshot, return to the app, screenshot | thumbnail themed; colours unchanged after return | B-06, B-07 |
| B5 | **V16-2:** with Dark still chosen run `cmd uimode night no`, wait, screenshot; then `night yes`, screenshot | bars stay #AE4155 (bottom #201916 on API 35+) both times — no maroon fallback | B-08, B-09 |
| B6 | Choose **High contrast**: screenshot, rotate once, night flip once | top #5E1620 throughout; strong contrast everywhere | B-10..12 |
| B7 | Choose **Light** with `night yes` active, then `night no` | top stays #7A1F2B | B-13, B-14 |
| B8 | Choose **Follow device**: `night yes` → screenshot; `night no` → screenshot; rotate once in each | dark theme (#AE4155) when night is on, light (#7A1F2B) when off, live without restart | B-15..18 |
| B9 | Force-stop (`adb shell am force-stop in.hcc.prototype`), relaunch with Dark chosen | starts dark, no light flash you can catch in a screenshot taken 0.5 s after launch | B-19 |
| B10 | Text size **Extra large** under Appearance & display; open Room board and Approvals & audit | no horizontal scrolling, nothing cut; note how it looks | B-20, B-21 |
| B11 | **OS font 1.3** (`font_scale 1.3`) with "Honour OS font size" ON then OFF; force-stop + relaunch with OFF | ON: text grows; OFF: back to the app's own size immediately and after relaunch | B-22..24 |

Put `font_scale` back to its starting value afterwards. Leave the theme on Light and text size Default for the rest.

## 5. Checks — V15 / V16 items (section C)

| # | Step | Expect | Evidence |
|---|---|---|---|
| C1 | **V15-2 inline form:** landscape, Admin & Setup → Master configuration → Rooms and rate plans → Room types → tap a row (e.g. Classic) so its form opens in the right-hand pane; tap into **Name**, type ` V17`; press hardware **Back** (if the keyboard is up, the first Back only closes it — press again) | "Discard changes?" dialog in the current theme; **Keep editing** leaves the typed text in place | C-01, C-02 |
| C2 | Back again → **Discard** | leaves the form; reopen the row: name unchanged (no " V17") | C-03, C-04 |
| C3 | Open the row again, type into Name, tap the **"Modules Home"** breadcrumb | the same dialog; Discard leaves to Modules | C-05 |
| C4 | Open the row, change nothing, Back | leaves silently, no dialog | C-06 |
| C5 | **V15-3 long titles** (phone layout only): if the device shows the phone layout in portrait, or the user menu offers a phone/compact layout switch, open Appearance & display and Master configuration | title on two lines, nothing cut, no "…" ; "Room board" one line with the business date | C-07, C-08 (or NOT APPLICABLE: desktop layout only) |
| C6 | **V16-1 camera:** Cashier → **Bank deposit** (or the cash-book / deposit form that has a **slip photo** field) → tap the photo field. `adb logcat -c` just before the tap | the **real camera app** opens (not the gallery picker). Take a photo of something harmless (shutter by tap), accept it | C-09 (camera), C-10 (form after) |
| C7 | After C6: logcat must NOT contain `Media capture intent could not be launched`; the form shows the photo as a small compressed image | record the logcat grep result and the image size the app shows if it does | C-10-logcat.txt |
| C8 | Clean-up check: `adb shell run-as in.hcc.prototype ls -la files/Pictures` and `adb shell ls /sdcard/Android/data/in.hcc.prototype/files/Pictures` | folder absent or empty (the full-size capture is deleted after compression); record exactly what the commands print | text in report |
| C9 | Save the deposit with the photo; reopen it | photo still shown | C-11 |
| C11 | **V17-1 insets (seen on the emulators, unconfirmed on hardware):** landscape, Reservations → **New booking**; at step 2 tap into a text field so the keyboard opens, then close the keyboard with Back; screenshot. Repeat in portrait. Then do the same with Customers → a guest → an edit sheet | the modal's footer buttons (Continue / Confirm) stay fully visible and tappable after the keyboard closes; no gap or clipped strip at the bottom; tapping the footer does NOT open "Discard changes?" | C-15..18 |
| C12 | **V17-4:** Room board (phone layout only, if available) | the app-bar date line reads "Business date <d Mon> · Open" in full | C-19 (or NOT APPLICABLE) |
| C10 | **FLAG_SECURE:** Customers → open a guest with an ID (e.g. Ananya Rao) → Profile → **Show ID**; `adb exec-out screencap` while the number is on screen; recents | the screencap is solid black (the file is small); recents card black; after **Hide ID** (or the auto-hide) a normal screenshot works again | C-12 (black), C-13 (recents), C-14 (normal again) |

## 6. Checks — regression on real hardware (section D)

| # | Step | Expect | Evidence |
|---|---|---|---|
| D1 | `pm clear`, launch, time from launch to Modules home (count the loading screen); sign in as Owner, then sign out and in as Reception and Cashier | all three sign in; note the cold-start time and whether the loading screen shows progress | D-01..03 |
| D2 | Rooms: add rooms **401–403** by range (floor 4 or any), then **Retire room 403** | in-app confirm dialog (not a browser alert); list shows the retired count | D-04, D-05 |
| D3 | New booking as Reception for room 401 (tomorrow, 2 nights), guest "Test Guest", review, confirm; booking ID shown | booking created (HCC-11xx) | D-06, D-07 |
| D4 | Arrivals & check-in → guided check-in of that booking (ID type any, skip advance) | "Checked in" message, Room board shows 401 occupied | D-08, D-09 |
| D5 | Folio → **Print / share** | the **Android print dialog** opens (choose Save as PDF, A4, one page) — save to Downloads, note the file name, then delete it | D-10 |
| D6 | Any list with **CSV / export** (e.g. Rooms or Approvals & audit export) | the Android **share sheet** opens with a `.csv`; cancel | D-11 |
| D7 | Rotate during the booking wizard (step 2) | the step survives the rotation with the entered data | D-12 |
| D8 | Hardware Back through: a sheet → the module → Modules home → one more Back | Back closes the sheet, then goes up; on home the app asks or exits cleanly (record which) | D-13 |
| D9 | Scroll long lists (Room board, Approvals & audit, Customers) by swipe (`adb shell input swipe 600 1500 600 500 300`) | smooth, no blank frames or lag you can see in screenshots; note subjective speed | D-14 |
| D10 | Leave the app in the background 2 minutes, return | state kept, no reload to the loading screen | D-15 |
| D11 | Airplane mode on and off (`adb shell cmd connectivity airplane-mode enable` / `disable`) while using the app | the demo app keeps working (it needs no network); note any banner | D-16 |

## 7. Anything else you notice (section E)

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

## 9. The report — `HCC_APK_v17_Tablet_Report_<yyyy-mm-dd>.md`

Use exactly these headings so the lead's session can parse it:

```markdown
# HCC APK v17 — physical tablet report
Date · tester: Claude (laptop session) · PHYSICAL DEVICE · APK HCC-0.1.0-proto-debug-v17.apk (SHA-256 of the file you installed)

## Device
| Model | Manufacturer | Android | API | Fingerprint | wm size | density | css px width | Status-bar case (plugin API 35+ / StatusBar below 35) |

## Install
versionCode / versionName from dumpsys, install command result, launch OK.

## B. Themes and bars
| # | Result (PASS / FAIL / NOT RUN) | Observed (exact colours read, what was on screen) | Evidence files |

## C. V15 / V16 items
same table

## D. Regression
same table (include the cold-start seconds in D1)

## E. Other observations
numbered list, each with a screenshot name

## Defects
### D17-1 (S1–S4) short title
Symptom · Steps to reproduce · Expected · Observed · Evidence · logcat lines (if any)
(S1 = blocks use, S2 = wrong data / money, S3 = feature broken with a workaround, S4 = cosmetic)

## Not run
what and why

## Restored settings
the values you put back

## Files
list of shots/*.png and *.txt, and the zip name
```

Then zip `shots/` and the report into `HCC_APK_v17_Tablet_Evidence_<yyyy-mm-dd>.zip` and tell the user: the report path, the zip path, how many PASS / FAIL / NOT RUN, and the defect list in one line each. The user will copy the report and the zip to the work PC (`05 Delivery Evidence\Acceptance\device-v17\`) for the lead's session to act on.
