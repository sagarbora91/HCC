# Wave 4 end-to-end re-run (E2E2): production web build vs real .NET server after fixes F1-F3

Date 26 Oct 2026 (business dates 26-29 Oct in the test database). Branch `claude/w4-e2e2` (= `claude/wave4-integration` d267036).
Build: `npm run build:production` (dist/production/index.html, 2.5 MB). API: `dotnet run --project src/Hcc.Api` (Development, `http://127.0.0.1:5211`, no proxy).
Disposable DB `HccTest_E2E2_20261026103341` on `.\SQLEXPRESS`: **dropped at the end** (verified, no `HccTest_E2E2%` left; `Hcc`, `EtpReporting`, `EtpScratch_*` untouched). API stopped.
Driver: Playwright (Chromium) via the Playwright MCP, desktop 1366x850, phone 360x740 spot checks. Evidence: `W4_E2E2\*.png` (numbered in test order), `console_errors_session.txt`, `payroll_register_sep2026.csv`, `payroll_bank_file_sep2026_TEST.txt` (fictional accounts), `scripts\` (test-only helpers, no credentials/keys).
Credentials (fictional) and the guest-ID key were kept in `%TEMP%\hcc-e2e2` (outside the repo, deleted/not committed). Guest-ID key made with `New-HccGuestIdKey.ps1 -Scope User -SkipAcl` into a TEST settings file, passed to the API as `Hcc__GuestIdKeys__*` environment variables.

## Hosting (H1) and setup
| Step | Expected | Actual | Result |
|---|---|---|---|
| API serves the web app (`Hcc:Hosting:WebRoot`) | `GET /` = index.html, no proxy | 200, 2.5 MB, sign-in screen, same origin, no CORS needed (01) | PASS (H1 fixed) |
| Guest-ID key script | key installed, never printed | Works; installed `dpapi-user` key in the test settings file, offline copy written | PASS (see N9 for a small script bug) |
| Migrate | schema applied | 0 -> 2702, 45 migrations | PASS |
| SetupProperty + import 11 masters incl. `pos-outlets`, `pos-menu` | all committed | 11/11 batches 0 errors; POS tables T1-T6, room service, 5 menu items visible in the POS (09a) | PASS (D6 fixed) |
| Valid hotel GSTIN 27AAAAA0000A1Z2 | accepted | accepted | PASS |
| SetCredential x5, device | ok | PIN x4, password x1 | PASS |

## D1-D6 re-verification (plus D7, D8, D10)
| ID | Check | Expected | Actual | Result |
|---|---|---|---|---|
| D1 | Check-out with valid GSTIN | stay Departed AND invoice issued | Card payment, "Confirm checkout" -> Departed + INV/2627/0000001 in one step (39, 40) | **FIXED** |
| D1 | Check-out with hotel GSTIN removed (Owner blanked it in Master configuration) | stay Departed + clear warning + retry action | Departed, banner "The tax invoice was not issued: ... GSTIN is not recorded ... then issue the invoice from the departed stay's folio", "Issue invoice" button shown (42). After GSTIN set again, "Issue invoice" -> INV/2627/0000002 (43). One expected 409 + 404 in console | **FIXED** |
| D2 | Second (and third, fourth) edit of a setting | saves | GSTIN edited 4 times in one session without STALE_VERSION ("2 changes ... 4 changes") | **FIXED** |
| D2 | Bad GSTIN (27AAAAA0000A1Z3) | refused | Dialog shows "The hotel GSTIN check character is wrong; check the number for a typing error." and Save is disabled (11) | **FIXED** |
| D3 | Check-in with ID (driving licence) | stored, no 503 | 4 check-ins with ID numbers, no 503, no read-only banner (07) | **FIXED** |
| D4 | Cashier opens the first shift | "Open cashier shift" on the Cashier shift page | Button present; shift opened, expected cash EUR/INR 25,000 (15, 15b) | **FIXED** |
| D5 | Reception sees rooms | board with 6 rooms | Board shows all 6 rooms with guests (19); New reservation as Reception works (20) | **FIXED** |
| D5 | Cashier sees folios, takes CASH | folio list + cash receipt | 3 in-house folios, CASH Rs 200 posted on folio 1, drawer expected changes (16, 17) | **FIXED** |
| D5 | 403 count per role | 0 | Reception 0, Cashier 0, Manager 0, Housekeeper 0, Accountant 0, Owner 0. The only 4xx in the whole session: 401 `/me/capabilities` before sign-in, 409/404 around the expected invoice-retry, 422 on SaveStaff (empty department) and PreparePayRun ("No staff to pay") | **FIXED** |
| D7 | PIN sign-in in a browser | way to register the device | Owner: Master configuration > "Register this device"; sign-in then shows staff picker + PIN (12, 13) | **FIXED** |
| D8 | Display names | names not ids | Receipt/expense show "by Ramesh Patil" to the Owner/Cashier; still ids or blanks for some viewers (N6) | PARTIAL |
| D10 | Prototype wording | gone | Footer and "Fictional property" gone; much "simulated/demo/sample" wording remains (N7) | PARTIAL |

## New areas
| # | Step | Expected | Actual | Result |
|---|---|---|---|---|
| 1 | Walk-in booking, guided check-in, charge Rs 500 laundry +18 %, UPI Rs 300 | folio Rs 590, balance Rs 290 | as expected (04-08) | PASS |
| 2 | POS room service to folio (paneer + tea) | Rs 300 + 5 % = Rs 315 on folio | "Bill Rs 315 posted to folio 1" (09) | PASS |
| 3 | Company booking (Corporate, company + GSTIN 27CCCCC2222C1Z8), check-in, check-out | B2B tax invoice with buyer GSTIN | INV/2627/0000003 `supplyType B2B`, recipient "Shah Logistics Pvt Ltd" GSTIN 27CCCCC2222C1Z8, CGST/SGST 2 x Rs 140 per night (44; API read) | PASS |
| 4 | Create company account (Groups & corporate > New company) as Manager and Owner | account created | "Not available on the server yet: GRP_COMPANY_SAVE" (33) | **FAIL (N1)** |
| 5 | Refund: Cashier requests Rs 50 on a cash receipt; own-request cannot be approved; Manager approves; executed | approve and execute | Pending "another authorised user must decide" (18); Manager approved (31); Manager executed (Cashier/Manager both can); folio shows "Payment refund -Rs 50", status Executed | PASS |
| 6 | Credit note: Manager requests Rs 500 on INV/2627/0000001; Owner decides | requester cannot approve; Owner approves | Own request shown as "another user must decide" (41); Owner Approve (45, 46); credit applied to invoice lines; day-28 report shows adjustments -Rs 500, GST -Rs 30.08 | PASS |
| 7 | Group block (STD x2, DLX x1, 12-13 Nov) | block created, rooms held | "Group block created", 0 of 3 picked up, held per type (34) | PASS |
| 8 | OTA: save channel + connect + inbound message (simulated), accept | accepted -> reservation | UI has no way to add a channel (N2); done with `SaveChannel` / `SetChannelStatus` / `ImportChannelMessage` via API; Inbox showed the message with pre-check "Can accept - Room 201"; Accept -> reservation HCC-1105 room 201 (48-50) | PASS with workaround (N2) |
| 9 | Payroll: staff | add staff | Add form department list empty (no department UI, N3); after `SaveDepartment` via API + Refresh, 2 staff added in the UI (52) | PASS with workaround |
| 10 | Roster change, attendance | saved | Future shift changed to Night; attendance Present / Late saved with "11:05 - Test Owner" (54, 56) | PASS |
| 11 | Pay run prepare, lock, downloads | draft -> locked by the other payroll user -> bank file | UI staff cannot be back-dated (no joining date, N3), so 2 staff with joining 1 Aug were made via API. Prepared by Owner for September: 4 paid days (26 unmarked days counted absent), gross Rs 5,866.67, PF Rs 352, net Rs 5,514.67 (59). Locked by Accountant (maker-checker, 61). Register CSV and bank file download (`bank-transfer-2026-09-1.csv`, totals Rs 5,514.67) | PASS with workaround |
| 12 | Stock GRN / issue | quantities move | No item master UI/import (N4); 2 items via API; GRN 60 towels from the UI, issue 10 to floor 1 -> store 50, F1 10, value Rs 10,800 (68, 69) | PASS with workaround |
| 13 | Day close 26, 27, 28 (Manager/Owner) | closed, next day opens | 26: room Rs 12,600, GST Rs 735 (5 % of 12,600 + 18 % of 500 + 5 % of 300), cash expected Rs 24,750 = 25,000 + 200 - 450 counted exactly; 27 and 28 closed; 29 Oct open; closing records listed (37, 38, 70) | PASS |
| 14 | Reports page | figures present | History: Oct room revenue Rs 25,200, ADR Rs 3,150, 8 room-nights; 28 Oct net receipts Rs 26,865 = 6,335 + 5,880 + 5,880 + 8,820 - 50 (66) | PASS |
| 15 | Accounting vouchers, trial balance, reconciliation | balanced | 26 Oct: 9 vouchers, debits = credits Rs 15,085, CGST 367.50 + SGST 367.50 = day-close GST Rs 735; 28 Oct incl. payroll journal: debits = credits Rs 39,257.75; reconciliation 8/8 agree (62-65, 71). But imported opening balances are missing (N5) | PASS with defect (N5) |
| 16 | Phone width 360 px: Reception (reservations, room board, folios, booking sheet) and Cashier (home, shift, folios) | usable, no horizontal scroll | Bottom nav, card lists; `scrollWidth` = viewport on every screen; "Record payment" label clipped in the booking sheet (N11) (21-27) | PASS |
| 17 | Housekeeper | only housekeeping modules, tasks | 4 tiles, no money pages, no 403; "No tasks are assigned to you" - checkout tasks still unassigned (D11 unchanged) (72) | PASS (D11 open) |

Totals: 17 areas + 8 D-checks: **all D1-D6 fixed; 1 area FAIL (company account save), 5 PASS with an API workaround** (OTA channel, departments/staff, pay run staff, stock item, plus the earlier POS-free items now fine).

## Defects and observations
| ID | Sev | Title | Repro / evidence |
|---|---|---|---|
| N1 | S2 | "New company" cannot be saved: "Not available on the server yet: GRP_COMPANY_SAVE" (Manager and Owner). Company credit accounts and company-linked group blocks cannot be set up from the UI | Revenue & Sales > Groups & corporate > New company (33, 33_..DEFECT) |
| N2 | S2 | OTA "Channels & mapping" tab is empty and has no "Add channel" action, so a real hotel cannot create a channel in the UI (server `SaveChannel` exists) | Revenue & Sales > OTA channels > Channels & mapping on a fresh install (36, 47) |
| N3 | S2 | HR setup gaps: no department editor (staff form department list empty; submit gives a 422 with no visible message), no joining-date field, and PreparePayRun failure ("No staff to pay for this month") shows no message in the UI | People & Payroll > Staff > Add staff; Payroll > Run payroll on a fresh install (58) |
| N4 | S2 | No stock item master (no import template, no "new item" UI): Receive stock / Issue dropdown is empty; items only via `StockItemCreate` API | Stock & Purchasing > Receive stock on a fresh install (67) |
| N5 | S2 | Imported opening balances (`opening-balances.csv`, Rs 16.2 lakh each side) do not appear in the trial balance (cash in hand shows Rs 250 Cr instead of Rs 24,750 Dr; "Cumulative" equals "Day movement") | Accounting > Trial balance after the 26 Oct close (64, 65) |
| N6 | S3 | Display names still missing for some viewers: Manager sees "by another user" (expense) and "requested by" blank (refund); Owner sees "requested by 3" on an executed refund; Accountant sees "Prepared by 1" on the pay run | 30, 31, Payroll as Accountant |
| N7 | S3 | Prototype wording in production: "simulated clock", "Demo: interrupt after N" button in Day closing, "sample credit note ... DEMO-CN", "demonstration receipt / no money moves", "Demo transaction reference", OTA "Simulated" banners | 37, 41, 08a, 35 |
| N8 | S3 | The API's CSP blocks the `data:` web-app manifest and a `data:` stylesheet (3 console errors on every load; no PWA manifest) | `console_errors_session.txt` |
| N9 | S3 | `New-HccGuestIdKey.ps1` refuses an offline-copy folder whose path merely starts with the server folder's path (`...\hcc-e2e2-offline` vs `...\hcc-e2e2`): `StartsWith` without a trailing separator | Run with the settings file in `X\hcc-e2e2` and the copy in `X\hcc-e2e2-offline` |
| N10 | S3 | Cash refund could be executed on 28 Oct although no cashier shift was open that day; the expected drawer cash dropped by Rs 50 (Day closing showed the shift "Open") - confirm intended | Folio > Execute refund after the 27 Oct close |
| N11 | S3 | At 360 px the "Record payment" button label is clipped in the booking sheet | 21_phone360_booking_sheet_truncated_button.png |
| D11 | S3 | (carried) Checkout housekeeping tasks stay unassigned; Housekeeper sees none | 72 |
| D9 | S3 | (carried, not retested) seasons not applied in QuoteStay | - |

Not tested: read-only banner after API stop (passed in run 1, not repeated), invalid-ID-number validation message, group-hold display on the room board, Android APK, real HTTPS/Windows service, concurrent users, MOBILE (non-360) tablet layouts.
