# APK v11 emulator report

Date 26 Oct 2026 · tester: Claude (Sonnet 5.5) · EMULATED, not a physical device. Screenshots in `v11\`.

## Build and devices
- APK `05 Delivery Evidence\APK\HCC-0.1.0-proto-debug.apk`, SHA-256 `D5D2F9DD...`, package `in.hcc.prototype`, **versionCode 11** (`dumpsys package`), versionName 0.1.0-proto, installed with `adb install -r` on both devices.
- Phone: AVD `codex_api35_google` (Android 15 / API 35, 1080x2400, 420 dpi, 412 css px wide), cold boot (`-no-snapshot`). Method: real `adb input` taps, Back/Escape key events, screencap, uiautomator dump for system sheets, plus WebView DevTools (read the DOM, programmatic clicks for long forms).
- Tablet: AVD `hcc_tablet_api35` created in this run (`avdmanager`, `system-images;android-35;google_apis;x86_64`, device Pixel Tablet, 2560x1600, 320 dpi = 1280x744 css px landscape / 800x1224 portrait, coarse pointer). The phone emulator was killed before it booted. Both emulators were killed at the end.

## Phone (412 px) results
| # | Check | Result | Evidence / notes |
|---|---|---|---|
| P1 | Clean install (uninstall + install) starts, versionCode 11 | Pass | `p-01b-loading`, `p-02-signin`; cold launch `am start -W` 1.9 s; sign-in picker "Who is using this device?" |
| P2 | Default data is FY26 (Apr-Oct history) after sign-in; business date 24 Oct | Pass | Demo controls show "Scenario: FY26"; 1,279 bookings |
| P3 | Loading screen with progress visible | Not seen | FY26 build finished before the first screenshot (about 2 s); no defect, seen in v10 |
| P4 | Sign in / preview as Owner, Reception, Cashier, Housekeeping (also Supervisor, Accountant, Manager) | Pass | tiles and bottom tabs differ per role (Housekeeping: My tasks / Report repair / Menu); `p-04` |
| P5 | All 11 Owner module tiles open and show a page | Pass | Dashboard, Front Desk, Cash & Billing, HK & Maint, Guests, Customers, Revenue, Stock, People, Accounts, Admin |
| P6 | No sideways scroll | Pass | scrollWidth 412 = viewport on every page visited |
| P7 | Cards not tables; 48 px targets | Pass | no interactive element under 47.5 px on module pages (only the Customers search input reports an inner 21 px field inside a larger wrapper) |
| P8 | 16 px text on buttons (PB-3) | Partial | Sheet buttons are 16 px, but see V11-1: DEMO pill 14, bottom tab labels 13, booking-wizard room-type chips 14 px and 44 px high |
| P9 | Back: sheet closes first; then page history; Modules Home shows "Exit?" with Stay | Pass | long walk of visited pages (about 12 Backs) then the exit prompt `p-18`; Stay returns |
| P10 | Booking flow: New booking wizard 4 steps, confirm, check-in wizard 6 steps, checked in | Pass | HCC-1106, Room 105, 2 nights, Rs 2,000/night; real keyboard typing works; Back with keyboard open hides it only |
| P11 | Housekeeping (Ravi): tick 3 checklist items by real tap, Complete cleaning gives "Awaiting inspection" | Pass | `p-14`..`p-16` |
| P12 | V10-1 phone invoice / folio preview shows CGST and SGST split | **Pass** | `p-08-folio-preview`, `p-29-invoice-sample`: Net 2,000, CGST 50 @2.5 %, SGST 50 @2.5 %, total 2,100; sample invoice DEMO/2026-27/0007 same split |
| P13 | V10-2 ID footnote only for ID-view roles | **Pass** | Accountant guest profile: "ID (masked): Restricted", no Show ID and no logged-footnote; Reception check-in form shows the DEC-068 footnote |
| P14 | PB-1 Escape closes the top dialog (focus on BODY) | **Pass** | focus forced to BODY, ESCAPE key event closed the booking sheet and the folio-preview/print chain |
| P15 | PB-2 "Request refund" disabled on a locked cash-book day | **Pass** | Owner locked 23 Oct; HCC-H1248 receipt H-RC-02310: button disabled, reason shown `p-12-refund-disabled-locked` |
| P16 | PB-2 expense "Reverse..." disabled on a locked day | **Pass** | EXP-H0492 and EXP-H0491 (23 Oct) disabled with the "Owner must reopen" message; 24 Oct expenses still enabled `p-13` |
| P17 | GST figures look like real GST | **Pass** | rooms <= Rs 7,500 at 5 % (2,000 gives tax 100; 4,500 gives 225; 3,000 gives 150; 4 nights x 2,200 gives 9,240 incl.); Master configuration > Money and tax: 5 % rooms <= 7,500, 18 % above, 5 % restaurant, 18 % laundry/banquet/other, CGST + SGST halves, "illustrative - CA to confirm" |
| P18 | Print / share opens Android print, paper ISO A4 | Pass | uiautomator: "Paper size: ISO A4", 1/1 `p-30` |
| P19 | Room above 7,500 shows 18 % on a real folio | Not run | no stay in the fixture has a tariff above Rs 7,500; the rule was seen in the master only |

Phone counts: **Pass 15, Partial 1 (P8), Fail 0, Not run or not seen 2 (P3, P19)**.

## Tablet (1280x744 landscape / 800x1224 portrait, coarse pointer)
| # | Check | Result | Evidence / notes |
|---|---|---|---|
| T0 | versionCode 11 installed, app opens, sign-in picker, desktop layout (not the phone shell) | Pass | `t-01-signin`, `t-02-owner-landscape`: hamburger drawer, module grid, tables |
| T-01 | Compact header (DEC-059) | Pass | header 65 px = 8.7 % of landscape height and 5.3 % of portrait height (v8 portrait was 85 px; Tab S7 earlier 248 px / 25 %); drawer items 48 px, 9 of 11 modules visible before scrolling `t-03-drawer`, `t-17-portrait-home` |
| T-02 | Touch sizes (DEC-060): dense rows/filters 44, primary actions, menu, drawer, dialog buttons 48 | Pass | Reservations filters/search/rows 44; New reservation 48; hamburger 48x48; drawer 48; booking sheet buttons all 48; New-reservation form inputs/selects/radios/buttons 48; nothing under 44 on any page visited |
| T-03 | Print preview paper | Pass | folio preview > Print: "ISO A4", 1/1 `t-07-print` |
| T-04 | "1 night" wording | Pass (phone) | phone cards "1 night"; tablet table shows "2 nights . 1 guest" `t-04-reservations` |
| T-05 | Back, long session (print, CSV share, display-mode switch Auto-Mobile-Auto, 6+ rotations, many modules) | Pass, not reproduced | 32 consecutive Backs each moved one page back, then the "Exit?" prompt; 6 more Backs toggled the prompt; the app stayed alive |
| T-R | Rotation portrait/landscape keeps state | Pass | booking sheet open + typed search "Ananya" survived rotation 0, 1, 0, 1, 0, 3, 0; no reload, no sign-in; layout stays desktop at 800 px |
| T-S | CSV share sheet from Cash book | Pass | "Sharing 1 file cash-book-2026-10-11_2026-10-24.csv" `t-09` |
| T-D | Display mode Mobile on tablet then back to Auto | Pass | phone shell shown and restored |
| T-PB1 | PB-1 Escape closes the dialog (focus on BODY) | Pass | |
| T-PB2 | PB-2 refund (HCC-H1248, desktop) and expense Reverse disabled on locked 23 Oct | Pass | `t-14`, `t-15`; reason in the button title and on the sheet |

Tablet counts: **Pass 11, Fail 0, Not run 0** (physical Tab S7 finger comfort is still untested; this is emulator touch only).

## Defects and observations (none blocking)
| ID | Severity | Summary | Steps |
|---|---|---|---|
| V11-1 | S4 | PB-3 incomplete on phone: some button-like controls are under 16 px / 48 px | Phone, Reception > Front Desk > Reservations > New booking: room-type chips ("Any type", "Classic . sleeps 2") 14 px and 44 px high; DEMO pill 14 px; bottom tab labels 13 px |
| V11-2 | S4 | Module tile badge overlaps the title on tablet | Tablet landscape Modules Home: the "2" badge covers the end of "Accounts & Reports" `t-02` |
| V11-3 | S4 | Phone folio-preview line card: CGST/SGST values not right-aligned like "Net" | `p-08-folio-preview` |
| V11-4 | S4 | Phone booking sheet bottom bar: "Add charge" label touches its button border | `p-06-booking` |
| V11-5 | S4 | Tablet New-reservation date fields clip the year ("10/26/2026" cut) | `t-16-new-reservation` |
| V11-6 | S4 observation | After printing, a blank `about:blank` WebView stays registered (DevTools lists it); the UI is fine | |
| V11-7 | S4 observation | Back after a role switch can land on a page of the previous role: it shows "Nothing here for this user" (safe, no money shown) | Phone: Owner opens Cash & Billing, switch to Ravi, Back x3 |

No S1-S3 defects. V10-1 (S3), V10-2, PB-1 and PB-2 are verified fixed; PB-3 is mostly fixed (V11-1 remainder).

## Not checked
- Physical Tab S7 and real finger comfort, real wireless printing, the printed PDF content.
- The loading-screen progress bar (finished too fast to catch), an 18 % room on a real folio (no such tariff), phone landscape, 360 px width, full RBAC per role beyond the tile lists.
- Demo data left on the AVDs is discarded (emulators killed, no snapshot saved).
