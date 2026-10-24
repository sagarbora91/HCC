# D6 · Galaxy Tab S7 acceptance — PHYSICAL device

**Progress:** ALL checks complete (last: #16 Display → Mobile compare, #17 notes). Nothing left except the owner turning the tablet back to landscape. Report was written to disk after the run; no further steps pending.

- **Date:** 24 Oct 2026, ~22:55–23:25 IST · **Agent D-D** (W0-D parallel run)
- **Device:** PHYSICAL Samsung Galaxy Tab S7 SM-T875, serial R52N807PTTE, Android 13, 1600×2560 px at density 380 (≈1078×601 CSS px landscape, 673×1006 portrait), USB debugging, auto-rotate left ON (not changed).
- **APK:** `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`, versionCode 6, SHA-256 `49c184d32d5e7eb099e474b185b9b9926e8bb38f8eb1ff940ccf86eddf75946b` (matches the brief), package `in.hcc.prototype`.
- **Method:** real touch taps (`adb shell input tap/swipe/keyevent`) for every key action; DevTools (port 9233) only to read the page, measure and for date/text fields marked "DOM". Screenshots (`adb screencap`) in `05 Delivery Evidence\APK\Tab-S7\w0d-*.png`.
- **Not touched:** emulators, tablet settings, no other app installed. The CSV file created by the app in `/sdcard/Documents/HCC/` stays on the tablet.

| # | Check | Result | Evidence | Notes |
|---|---|---|---|---|
| 1 | `adb install -r` v6 over existing install, data kept | Pass | `adb dumpsys`: versionCode 6, firstInstall 20:25, lastUpdate 22:54 | Streamed install "Success"; the old session (desktop layout, Auto) was still stored. |
| 2 | Cold start | Pass | `w0d-01-cold-start.png` | `am start -W` after force-stop: LaunchState COLD, **TotalTime 1,609 ms**; staff picker fully painted in desktop layout at ≤ 6 s. |
| 3 | Desktop layout in Auto on tablet widths | Pass | `w0d-02`, `w0d-03-auto-desktop-layout.png` | Auto highlighted, `hcc-display-mode=auto`, no phone shell, centred card on sign-in. |
| 4 | Staff-picker sign-in → Modules Home | Pass | `w0d-02-signin-owner-home.png` | Tap on "Sagar · Owner"; tiles with badges (Front Desk 3, Housekeeping 7, Guests 8). |
| 5 | Hamburger drawer; Android Back closes it | Pass | `w0d-04-drawer-open.png`, `w0d-05-back-closes-drawer.png` | Drawer lists categories + Demo controls / Display mode / Sign out; Back closed it, page unchanged (see T-01 on drawer height). |
| 6 | Android Back: closes app dialog, then walks page history | Pass | `w0d-31-back-after-dialog.png` | Walkthrough dialog closed by Back; further Back moved Arrivals → Cashier → … → Modules. |
| 7 | Back at landing shows "Exit?" confirmation | Pass (cold start) / **Fail (long session, T-05)** | `w0d-32-cold-start-back-exit-prompt.png` | Dialog appears after a cold start and after short navigation; in the long session 24 Back presses on Modules Home did nothing (T-05, not reproduced). |
| 8 | Scenario S0 loaded (Demo controls) | Pass | `w0d-06-scenario-list.png`, `w0d-07-s0-loaded.png` | Business date 24 Oct, 08:00. |
| 9 | Cashier shift ₹10,750 incl. restaurant line (as Cashier Priya) | Pass | `w0d-10-cashier-shift.png` | Opening ₹5,000 + payments ₹7,000 + deposits ₹0 + **restaurant/room-service cash ₹550** − refunds ₹0 − returns ₹0 − expenses ₹1,800 = **₹10,750**; copy says "one shared till"; POSR-F01 ₹550 listed among 6 movements. (D-01 text lives in the settle dialog, not checked here.) |
| 10 | Booking with restriction, Manager override | Pass | `w0d-14-restriction-warning.png`, `w0d-15-override-reason.png`, `w0d-16-booking-confirmed.png`, `w0d-16b-…override-note.png` | Meera (Manager): New reservation 7→8 Nov, Classic 101 auto-assigned → "RESTRICTED · Minimum stay 2 nights … (Diwali weekend)". Real tap ticked "Override restriction", reason "Regular corporate guest" typed on the tablet keyboard, Confirm → HCC-1101 created; details show "Restriction overridden (MIN_STAY YLDR-1): Regular corporate guest". Dates/name/phone set via DOM. Reception-blocked variant: **Not run** (D-C covers it on the emulator). T-04 (cosmetic). |
| 11 | CSV → `Documents/HCC` + share sheet | Pass | `w0d-17-csv-share-sheet.png`, `w0d-18-after-share-back.png` | Reports → Export CSV: `HCC-Demo-daily-2026-10-24_2026-10-24.csv` (816 bytes) at `/sdcard/Documents/HCC/`; Android share sheet (Quick Share, Outlook, Drive, …) opened; Back returned to Reports. Nothing was shared. |
| 12 | Print via Android print service | Pass | `w0d-19-print-service.png`, `w0d-20-android-print-preview.png` | Reports → Print → in-app preview → "Print / save PDF" → PrintSpooler "Save as PDF" with the report; Back returned. T-03 (Letter paper, empty block). |
| 13 | Reset | Pass | `w0d-22-reset-confirm.png`, `w0d-23-after-reset.png` | "Reset your demo?" → Reset demo → S0 reloaded, HCC-1101 gone, drawer ₹10,750 again, back to Owner on Modules. |
| 14 | Rotation to portrait (owner rotated by hand) | Pass | `w0d-28-portrait-home.png`, `w0d-30-portrait-arrivals.png`, `w0d-32-…png` | Landscape→portrait kept session, page, display mode; desktop layout stays (673 px, no page-level sideways scroll). Layout reflows to 2-column tiles. T-01 (top bar uses ~25 % height). Rotation back to landscape left to the owner. |
| 15 | Portrait Arrivals table sideways scroll (Session 8 note) | Not run | `w0d-30-portrait-arrivals.png` | S0 has 0 arrivals so the table is empty; the only table measured (Cashier) was 638 px in 632 px (≈ fits). Needs BASE/FY26 data. |
| 16 | Compare once with Display → Mobile | Pass | `w0d-24-display-mobile-cashier.png`, `w0d-26-display-mode-dialog.png`, `w0d-27-back-to-auto-desktop.png` | Mobile layout on the tablet: full-width phone shell (not a centred frame), bottom tabs Home/Rooms/Approvals/Reports/Menu, same figures (₹10,750, "cash in includes restaurant cash ₹550 (one shared till)"), Count-drawer button; 15 of 17 controls ≥ 44 dp. Switched back to Auto, desktop layout restored. |
| 17 | Touch comfort notes | See below | | |

## Touch-comfort notes (physical)
- Desktop layout on the tablet is usable by finger: every real tap hit its target, including 32 dp controls (T-02), but spacing is tight (toggle row, demo controls) compared with the phone layout.
- Native selects (Preview as, booking selects) open as Android popups and work well. Date inputs use the Android picker; I set dates via DOM to save time.
- The on-screen keyboard appears for text fields; Back dismisses it without leaving the dialog.
- Landscape drawer needs a swipe to reach sub-items once a category is expanded (T-01); portrait top bar is tall (T-01).
- Print creates a second blank WebView target (only matters for DevTools).
- Mobile layout on a 1078 px-wide landscape tablet is stretched edge-to-edge (long cards); desktop layout is the better tablet choice, as decided in Session 7b.

## Summary
Pass 14 · Fail 1 (T-05 long-session Back) · Not run 2 (reception-blocked variant, portrait Arrivals sideways scroll) · plus notes.

## Defects
| ID | Severity | Steps | Expected | Actual | Evidence |
|---|---|---|---|---|---|
| T-01 | S3 | Portrait any page; landscape drawer with a category expanded | Space for content; reachable sub-items | Portrait top bar ≈25 % of height; landscape drawer list only 254 of 601 px | `defects\T-01-…md` |
| T-02 | S3 | Measure controls, desktop layout | ≥ 44–48 dp | 32–38 dp (22 dp selects) | `defects\T-02-…md` |
| T-03 | S4 | Reports → Print → Android print preview | A4, no filler | Letter, empty beige block | `defects\T-03-…md` |
| T-04 | S4 | Book 1 night | "1 night" | "1 nights" | `defects\T-04-…md` |
| T-05 | S3 | Long session with share/print/Reset/Display switch/rotation, then Back at Modules Home | Exit confirmation | 24 Back presses, nothing; not reproduced after cold start | `defects\T-05-…md` |
(D-01 from the lead is not repeated.)
