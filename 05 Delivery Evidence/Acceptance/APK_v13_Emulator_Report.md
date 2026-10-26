# APK v13 emulator report (demo APK and server APK against the real server)

Date 26 Oct 2026 · tester: Claude (Sonnet 5.5) · EMULATED, not physical devices · branch `claude/apk13` (= main f25b519 + `versionCode 13`) · screenshots in `v13\` (a-* demo, s-* server APK phone, t-* server APK tablet, r-* release). No product code changed; the only commit besides this report is `versionCode 13`.

## Builds (versionCode 13, `dumpsys package` on both AVDs)
| APK | Package | SHA-256 | Notes |
|---|---|---|---|
| `APK\HCC-0.1.0-proto-debug.apk` | `in.hcc.prototype` | `3869D3CA4B7A1C37...` | `assembleDebug`, demo data on device |
| `APK\HCC-0.1.0-server-debug-TESTCA.apk` | `in.hcc.prototype.server` | `B83E118303C22E2C...` | `productionDebug` with a THROWAWAY test CA (CN "HCC APK13 Throwaway Test CA", 30 days, SHA-256 0F:AF:C0:EB...), HTTPS only, debuggable. Test use only |
| `productionRelease` (not copied) | `in.hcc.server` | `210627E0206D5D14...` | throwaway keystore; `build-production.mjs --verify`: "not debuggable, allowBackup=false, no cleartext"; installed on the phone AVD: launches, `adb run-as in.hcc.server` -> "package not debuggable" |

`npm ci` was run in `03 Prototype` and `04 Android` first; `git status` clean after the three builds (cap sync did not drop the Preferences plugin). CA, server certificate, keystore and credentials were created in the session scratch folder / `%TEMP%` outside the repo and deleted at the end. Nothing secret is committed; `*.apk` is git-ignored.

## Server set-up
- Disposable DB `HccTest_APK13_20261026155159` on `.\SQLEXPRESS` (0 -> 2702, 45 migrations); API `dotnet run` (Development) on Kestrel HTTPS `https://0.0.0.0:5443` from a throwaway PFX; guest-ID key from `New-HccGuestIdKey.ps1 -Scope User -SkipAcl` into a scratch settings file, passed as env vars. Port 5600 and the WEB13 DB were not touched.
- Setup with the W4_E2E2 scripts (base URL changed): SetupProperty, 11 sample masters (0 errors), GSTIN 27AAAAA0000A1Z2 accepted, Owner password, 4 PINs + 1 accountant password (fictional). For the payroll test two fictional staff and two departments were added through the API (`SaveDepartment`, `SaveStaff`) because a fresh DB has no HR staff.
- Session expiry was tested by restarting the API with `Hcc__Access__IdleTimeout=00:01:00` (then restarted again with the default).
- DB dropped at the end (verified: no `HccTest_APK13*` left), API stopped, both emulators killed.
- Environment note: free RAM fell to about 1.8 GB mid-run (other agents); the phone emulator once went adb-offline and was restarted (app data survived); not an app defect.

## Phone AVD `codex_api35_google` (412 css px, 1080x2400), server APK
Flow: fresh install, address `https://10.0.2.2:5443` Test connection (found WORKPC 0.1.0) -> Owner password -> register device "Phone emulator" -> staff PIN sign-ins (Priya, Ramesh, Anita, Sunil) and Owner password sign-in on the registered device. Driving: real taps via adb for system/drawer parts, DOM clicks through the debuggable web view for fields and buttons (as in v12).

### v12 defect fixes re-tested
| ID | v12 defect | v13 result | Evidence |
|---|---|---|---|
| APK12-1 | wording "cash shift is closed, open next business day" | Pass: Reception/Cashier now see "Cash needs your own open cashier shift - use UPI/card or ask the cashier." and the cashier page says "No cash shift is open, so cash receipts are closed. Open the shift..." | s-10, s-14 |
| APK12-2 | throttling message hidden | Pass: 5 wrong PINs -> "Sign-in failed...", from the 6th: "Too many sign-in attempts from this device. Wait 15 minutes, then try again." | s-39 |
| APK12-3 | Reception offered Cash that the server refuses | Pass on PHONE: Cash tender disabled (greyed) with the hint, UPI deposit of 1,000 posted ("Receipt recorded once"). NOT fixed on the tablet/desktop layout, see APK13-3 | s-10, s-11 |
| APK12-4 | "Preview as (data)" / demo text in profile sheet | Pass: "Switch user" list, "Owner. Sign out returns to the staff picker.", no DEMO or "Preview as" anywhere | s-04 |
| APK12-5 | New booking enabled offline | Pass: with the API stopped the banner "Server unavailable - read-only. Bookings and payments are disabled..." shows and the New booking button is disabled | s-12 |
| APK12-6 | banner stays until Retry | Pass: API restarted (listening 16:04:42); with no tap the banner was gone at the next check (<= 75 s) and New booking enabled again | s-13 |
| APK12-7 | booking reference shows "1" instead of HCC-xxxx | Partial: HCC-1101 shows in the confirm dialog, check-in header, Folios list, folio header, invoice and dashboard, but the phone Reservations list card and the Room-board room sheet still show "1 - Direct" / "1 - open folio" (APK13-1). Tablet table shows HCC-1101 | s-07, s-12 |
| APK12-8 | Show ID dialog layout | Pass: title left aligned, close X in its own corner, 16 px text, "This view was logged. It hides itself in 27 s and is not stored on this device." (reached via Folios > Departed > More > Guest profile > ID section > Show ID) | s-34 |
| APK12-9 | README npm ci | Verified: README now says `npm ci` before every build; followed, Preferences plugin present | - |
| APK12-10 | HK task could not be created | Pass: see below | - |

### Items v12 could not finish
| # | Check | Result | Evidence |
|---|---|---|---|
| P1 | Booking HCC-1101 (Reception) -> guided check-in with driving licence ID -> checked in room 101 | Pass | s-07..s-09 |
| P2 | Cashier (Ramesh) opens shift; cash deposit 500; cash payment 1,180 | Pass | s-15 |
| P3 | Refund: Ramesh requests Rs 180 from receipt 3; Anita approves; executed (as Anita, cash, shared shift) -> "Refund executed and linked to the original receipt", balance due 180, net paid 1,000 | Pass | s-17, s-18, s-20 |
| P4 | Day close 26 Oct (Anita): post room nights (1), count and submit cash 26,500 = expected, "Close 26 Oct" -> record "net revenue 3,800, receipts 1,000, counted 26,500", business date rolls to 27 Oct | Pass | s-21, s-22 |
| P5 | Close 27 Oct the same way; business date 28 Oct; pay 2,940 by UPI; return deposit 1,500 by UPI; Checkout checklist all Done -> checkout, tax invoice INV/2627/0000001 issued by the server (CGST/SGST split correct: laundry 9 %, rooms 2.5 %) | Pass | s-24..s-26 |
| P6 | Housekeeping: Checkout-clean task created; Anita assigns it to Sunil; Sunil "My tasks" shows 1, Start cleaning, tick 3 checklist items, Complete cleaning -> "Awaiting inspection" | Pass (APK12-10 resolved) | s-28..s-31 |
| P7 | Print from the server APK: folio invoice "Print / share" opens the Android print dialog with the A4 preview, "ISO A4", 1/1 page | Pass | s-27 |
| P8 | Session expiry: idle timeout 1 min; next request -> sign-in screen "Your session has ended. Sign in again."; session items gone; IndexedDB `hcc-server-cache.slices` has 0 rows (cache deleted) | Pass | s-38 |
| P9 | Phone landscape (866x364 css px): no horizontal scroll (scrollWidth 866), phone shell, bottom tabs, wizard usable but only about 2 rows visible above the buttons | Pass with note | s-36, s-37 |
| P10 | Payroll prepare/lock on the phone | Not offered by design: phone says "Running, locking, unlocking and the bank file are done on the tablet or desktop" (tested on the tablet, T5) | s-35 |
| P11 | Demo APK: cold start, loading screen, then "Who is using this device?" with HCC Demo Residency | Pass (smoke only) | a-01 |
| P12 | Release APK: installs (v13), launches, `run-as` refused | Pass | r-01 |

Phone counts: 12 re-test items + 11 further items; pass 20, partial 2 (APK12-7, landscape note), not offered 1 (payroll, covered on the tablet).

## Tablet AVD `hcc_tablet_api35` (2560x1600, 1280x744 css landscape), server APK
| # | Check | Result | Evidence |
|---|---|---|---|
| T1 | Fresh install, address, Test connection over TLS, Owner password sign-in, register "Tablet emulator" | Pass | t-02, t-03 |
| T2 | Desktop layout (hamburger drawer, module grid with HK badge 2), not the phone shell | Pass | t-03 |
| T3 | Reservations table shows HCC-1101 / HCC-1102 (APK12-7 fixed on desktop layout); New reservation (Reception) created HCC-1102 | Pass | t-08..t-10 |
| T4 | Payroll Sept 2026: Owner "Run payroll" -> Draft (2 staff, net 4,491.67, PF/ESIC computed, 26 unmarked days warning); Owner's "Lock pay-run" is disabled ("You prepared this draft. Maker-checker..."); Accountant Meena locks it after the confirm -> "Locked", "locked by Meena Rao" | Pass | t-04, t-05, t-07 |
| T5 | Rotation with the payment dialog open and a typed amount: portrait 800 px keeps the desktop layout, value kept, back to landscape keeps it | Pass | t-13, t-14 |
| T6 | Reception cash deposit on the desktop layout | Fail (APK13-3): Cash is offered, no hint; the server refuses with "Not permitted: Outside your assigned scope." | t-11, t-12 |
Tablet counts: 5 pass, 1 fail. Emulator killed.

## New defects and observations (no product code changed)
| ID | Sev | Summary |
|---|---|---|
| APK13-1 | S4 | Booking reference still shown as "1" on the phone Reservations list card ("1 - Direct") and on the Room-board room sheet ("Kavita Test, 1 - open folio"); also receipts/requests show bare ids ("receipt 3", "1 - receipt 3"). The tablet table shows HCC-1101 correctly |
| APK13-2 | S3 | Dashboard (phone, Anita) shows a BACKUP card "Backup failed - check the office NAS ... 1 failed run in the last 3 nights - simulated" on a fresh server with no backup configured: demo/simulated content in the production build. Misleading to staff |
| APK13-3 | S4 | APK12-3 fix is phone-only: the tablet/desktop payment dialog still offers Cash to Reception without the shift hint; the refusal text "Not permitted: Outside your assigned scope." does not say what to do (use UPI/card or ask the cashier) |
| APK13-4 | S4 | Guests & Services > Document capture on the server APK lists demo samples ("Aadhaar-style card (demo)", "Aditi Nair - arriving today", "Dairy bill (demo)") that are not server data (s-33 was not kept in the repo evidence set if missing; see `v13\s-33-doc-capture-demo.png`) |
| APK13-5 | S4 | Checkout checklist text says "allocates a sample invoice number once" although the server issues a real invoice number (INV/2627/0000001) |
| APK13-6 | S4 | Lock pay-run uses the browser `confirm()` -> native unthemed Android dialog (teal CANCEL / OK); it also blocks the web view (the debug bridge hung until tapped). Cosmetic, but staff see a different look than the rest of the app |
| APK13-7 | S4 | Phone landscape: the booking wizard leaves only about 2 visible rows between the header and the fixed buttons; usable but cramped |
| APK13-8 | S4 | After a closed day the refund executed by the Manager used the shared open cashier shift (Ramesh's); "Kavita Test - by" in the cash movement list shows no user name after "by" (cashier page, s-15) |

No S1 or S2 defects found.

## Not tested
Physical devices; release APK against the server (only installed/launched, not signed in); `LocalMachine\My` thumbprint hosting and the Windows service path; real Wi-Fi LAN address; absolute (12 h) session lifetime; device-registration removal and address-change flow; tablet offline banner and tablet housekeeping/refund/day-close (done on the phone); payslip print/bank file download on the tablet; 360 px phone width; CSV share; wrong-PIN throttle after the 15-minute window; second-day cashier shift opening (the shift for 27/28 Oct carried over automatically in the closing flow).
