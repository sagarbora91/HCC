# W4_WEB13: browser regression of the production web build vs the real server (APK v12 defect fixes)

Date 26 Oct 2026 (business dates 26-28 Oct in the test DB). Branch `claude/web13` = main f25b519. Build: `npm run build:production` (2.5 MB). API: `dotnet run` Development, `http://127.0.0.1:5600`, serves `dist/production/index.html` itself (web root config), no proxy.
Disposable DB `HccTest_WEB13_20261026154739` on `.\SQLEXPRESS`: migrated 0 -> 2702, **dropped at the end** (verified; `Hcc`, `EtpReporting`, `EtpScratch_*` untouched, API stopped, port 5600 free). Setup scripts from `W4_E2E2\scripts` (copied to scratch, base URL changed): SetupProperty, 11 sample masters (0 errors), GSTIN 27AAAAA0000A1Z2 accepted, PINs for 4 staff, Accountant password (fictional, kept in scratch outside the repo and deleted). Guest-ID key made with `New-HccGuestIdKey.ps1 -Scope User -SkipAcl` (test settings file outside the repo, passed as `Hcc__GuestIdKeys__*`; the key was never printed). The device was registered **in the browser** (Master configuration > Register this device).
Driver: Playwright MCP (Chromium). Desktop 1366x768 and phone 360x800 (auto phone shell). No Android emulator. No product code changed. Evidence: `W4_WEB13\*.png` (numbered in test order), `payroll_*` (fictional bank details), `network_all_since_load.txt` (all requests since the last page reload).

## A. APK v12 fixes re-verified in the browser
| # | Fix | Expected | Actual | Result |
|---|---|---|---|---|
| A1 | APK12-2 throttle message | after repeated wrong PINs the server's throttle text with the wait time | phone 360: 5 x "Sign-in failed. Check the name and the password or PIN.", then "Too many sign-in attempts from this device. Wait 15 minutes, then try again." (60). A correct PIN is refused during the window; it worked again ~16 min later. Same text for the password path (66) | PASS |
| A2 | APK12-3 Reception cash tender | disabled + hint; UPI works | Desktop, no shift open (day 26): Cash option disabled, hint "Cash needs your own open cashier shift - use UPI/card or ask the cashier.", UPI Rs 300 posted (06, 07). Cashier with open shift: Cash enabled, Rs 200 posted. **But** once a shift exists for the day that Reception does not own (day 28) the Cash tender is enabled on desktop and phone and the server answers 403 "Not permitted: Outside your assigned scope." (44, 65). Phone 360 shows no hint at all and Cash enabled even with no Reception shift (43). UPI works on phone | PARTIAL (defect W13-2) |
| A3 | APK12-1 shift wording | clear wording | Cashier shift page: "Shift closed - Cash receipts are closed for today ... No cash shift is open ... Open the shift to start taking cash" (10) | PASS |
| A4 | APK12-6 / -5 offline | stop API: banner + new booking refused; restart: banner clears by itself <= ~30 s | Phone: after the API stop the banner "Server unavailable - read-only. Bookings and payments are disabled ... Nothing is saved on this device." appeared on the next request (not on its own, see W13-6), cached bookings shown "as of 16:08", **New booking disabled** (45, 46). API restarted (listening 16:14:33): banner cleared without pressing Retry within ~6 s of polling, New booking enabled again (47) | PASS |
| A5 | APK12-7 booking refs HCC-xxxx | everywhere | Desktop reservations list, folios list, folio header, arrivals and search by "HCC-1102" (63), refund/credit-note request lines, phone folios list, phone booking header: HCC-xxxx. **Not fixed:** phone Bookings list shows "1 - Walk-in" / "2 - Direct" (35, 42); desktop guest-profile stay history shows "1" (08); POS settle dialog "Room service - 1", toast "Bill Rs 315 posted to folio 1" (11); Day closing "Folio 1 - Arjun Mehta" (corrections); cash book shows "2 - Arjun Mehta" = receipt no and blank "By" (13) | PARTIAL (W13-1, W13-3) |
| A6 | APK12-8 Show ID dialog | title clear of the X, left aligned | Desktop (09) and 360 px (39): title "Passport" clear of the close X, number, "This view was logged. It hides itself in 29 s ...", Hide ID; no overlap | PASS |
| A7 | APK12-4 "Switch user", no DEMO- | profile sheet says "Switch user"; no DEMO- headings | 360 px profile sheet: "Test Owner / Owner. Sign out returns to the staff picker." / "Switch user" (32). No "DEMO-" heading anywhere (38 pages scanned as Owner). **But** choosing another name in "Switch user" does nothing: the session stays Test Owner | PARTIAL (W13-4) |
| A8 | Other leftover demo wording (scan of 38 Owner pages) | none | still present: Messages "Simulate delivery", "sample invoice ... https://demo.invalid/inv/HCC-1101 (simulated link)"; Document capture "Aadhaar (demo)", "Passport (demo)"; Backup "Simulation - no real backup is taken"; OTA/Tariff "Simulated"; Roadmap "prototype data"; Approvals "(simulated - no money moves)"; credit-note dialog "Request a sample credit note" (19); payroll bank-file banner "fictional bank details - simulated transfer - no money moves"; Registration card "NOT A STATUTORY REGISTRATION RECORD"; many "(illustrative - CA to confirm)" lines | FAIL (W13-5, S4) |

## B. Core day, full regression (desktop 1366x768 unless noted)
| # | Role | Step | Expected | Actual | Result |
|---|---|---|---|---|---|
| 1 | Owner | Register device, sign out | staff picker (4 names + password option) | (02) | PASS |
| 2 | Reception | PIN sign-in, home | Front Desk, Cash, HK, Guests, Customers, Revenue, People tiles | (03) | PASS |
| 3 | Reception | New walk-in reservation 26-27 Oct, STD | HCC-1101 confirmed | confirmed, "HCC-1101 - Walk-in" in the list (04) | PASS |
| 4 | Reception | Guided check-in with passport K1234567 | stored, no 503 | checked in, room 101 occupied, registration card masked (05) | PASS |
| 5 | Reception | Charge Rs 500 laundry +18 % | folio Rs 590 | Rs 590 | PASS |
| 6 | Reception | UPI Rs 300 | balance Rs 290 | posted (07) | PASS |
| 7 | Reception | Show ID | view logged | (09) | PASS |
| 8 | Cashier | Open shift | float Rs 25,000 | opened | PASS |
| 9 | Cashier | Cash Rs 200 on folio | posts, drawer Rs 25,200 | posted; cash book shows it (12, 13) | PASS |
| 10 | Cashier | POS room service (paneer + tea) KOT, settle to room | Rs 300 + 5 % = Rs 315 on folio | "POS/2610/0001" Rs 315 posted to the folio | PASS |
| 11 | Cashier / Manager | Expense Rs 450 drawer cash: submit, other user approves | pending then posted, names shown | Manager sees "by Ramesh Patil"; posted | PASS |
| 12 | Manager | Day close 26: post nights, count Rs 24,750, close | closed, 27 Oct opens | closed (14); preview figures: service Rs 800, GST Rs 105, receipts cash 200 / UPI 300 | PASS |
| 13 | Reception | UPI Rs 3,345, checkout, invoice | Departed + invoice in one step | INV/2627/0000001, 1 housekeeping task (15, 16) | PASS |
| 14 | Manager | HK: assign task to Sunil | appears on his list | assigned (Unassigned tasks need a supervisor; Housekeeper sees none until then, D11 carried) | PASS |
| 15 | Housekeeper | Start, tick 3 checklist items, complete | awaiting inspection | done; home shows only HK/Guests/Stock/People tiles (17) | PASS |
| 16 | Manager | Inspect and release | room 101 Ready | board shows 101 Ready | PASS |
| 17 | Manager + Owner | Credit note Rs 500 requested by Manager, Owner approves | own request cannot be approved | "another authorised user must decide"; Owner approved, CN/2627/00000001, credit balance Rs 534.02 (19) | PASS |
| 18 | Cashier, Manager | Refund Rs 200 of UPI receipt: request, other user approves, execute | approve and execute | requested by Ramesh, approved by Anita, executed, folio "Payment refund -Rs 200" | PASS |
| 19 | Manager | Day close 27 (count Rs 24,750) | closed, 28 Oct opens | closed; adjustments -Rs 500, GST -Rs 34.02 | PASS |
| 20 | Accountant | Vouchers + trial balance 26 Oct | balanced, reconciles | 6 vouchers, debits = credits Rs 4,795, 8 reconciliation checks agree (20). Cumulative TB includes imported opening balances: cash Rs 24,750 Dr, bank Rs 3.5 lakh, balanced Rs 16,24,795 (21) (N5 fixed) | PASS |
| 21 | Owner | Reports | figures | history Apr-28 Oct: room revenue Rs 2,800, net Rs 3,100 (= 3,600 - 500 credit), ADR Rs 2,800 (24) | PASS |
| 22 | Owner | Staff: department + 2 staff with joining date 1 Sep, bank details (UI only, no API workaround) | saved | saved (N3 fixed); payroll draft Sept: 4 paid days each, gross Rs 4,000, PF Rs 240, ESIC Rs 28.50, net Rs 3,731.50 (22) | PASS |
| 23 | Accountant | Lock the pay run (maker-checker), download bank file + register | locked, files | locked "bank details as locked"; `payroll_bank_file_sep2026_TEST.csv` (2 rows, total 3731.50) and register CSV saved; second bank-file download asks "may pay staff twice. Generate it again?" (23) | PASS |
| 24 | all six roles | 403/console | 0 | Owner, Reception, Cashier, Manager, Housekeeper, Accountant: **0 permission 403 on any page** (whole-session network log; only 4xx = 401 `/me/capabilities` before sign-in). The only 403s: Reception cash receipt (W13-2, 2) and the wrong-credential sign-in attempts of the throttle test. Console: 0 CSP errors (N8 fixed) | PASS |
| 25 | Reception, Cashier, Housekeeper, Manager | Phone 360: home, bookings, new booking wizard (4 steps), booking sheet, folios, arrivals, payment | usable, no sideways scroll | `scrollWidth` 345/360 (scrollbar) on every screen; HCC-1102 created (41, 42); UPI Rs 100 posted (cash refused W13-2) | PASS (with W13-1/2) |

## Defects (no product code changed)
| ID | Sev | Title | Repro / evidence |
|---|---|---|---|
| W13-1 | S3 | Booking reference still shown as the numeric id in several places (APK12-7 only partly fixed): phone Bookings list ("1 - Walk-in", "2 - Direct"), desktop guest-profile stay history, POS settle dialog and "posted to folio 1", Day closing correction picker ("Folio 1"), cash book entry ("2 - name" = receipt no, "By" blank, no booking ref) | 35, 42, 08, 11, 13; Reception: Bookings tab on a 360 px phone |
| W13-2 | S3 | Cash tender offered to Reception although the server refuses it (APK12-3 only a client approximation): on a day whose cashier shift is open (or carried) but not Reception's, Cash is enabled with no hint and the server returns 403 "Outside your assigned scope" (visible as a 403 in the console). The 360 px phone dialog never shows the hint nor disables Cash | Day 28, Reception > booking > Record payment > Cash Rs 100 (44, 65) |
| W13-3 | S4 | Shift appears "Open" on the new business day (28 Oct, expected Rs 24,750) before any cashier opened it; Day closing shows "Open cashier shift" and "Count & submit" together | Cashier shift / Day closing after the 27 Oct close (62) |
| W13-4 | S3 | Phone profile sheet "Switch user" selector lists all users but does nothing (session stays the signed-in user): either remove it or make it sign out to the staff picker | 360 px profile sheet > pick another name (32) |
| W13-5 | S4 | Prototype wording left in production (list in A8) | Messages, Document capture, Backup & recovery, Roadmap, credit note dialog, payroll bank-file banner |
| W13-6 | S4 | The read-only banner after an API stop only appears on the next request (about 40 s idle: still no banner); recovery is automatic | stop API, wait, observe |
| W13-7 | S4 | After sign-in the header briefly shows "Business date 24 Oct 2026 - Open" ("Loading from the server...") before the real date (28 Oct) | any PIN/password sign-in, desktop and phone |

Not tested: other device sizes, real HTTPS/Windows service, group/OTA/stock modules (covered in E2E2), invoice print/PDF, 100+ concurrent users. Note: the Playwright MCP wrote downloads and its logs to `E:\Projects\HCC\.playwright-mcp` (outside the repo); my payroll downloads were deleted there after copying.
