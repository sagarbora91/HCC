# Wave 4 end-to-end smoke test: production web build vs real .NET server

Date 26 Oct 2026 (business dates 26-28 Oct in the test database). Branch `claude/w4-e2e` (= `claude/wave4-integration` 47b6fa2).
Build: `npm run build:production` (dist/production/index.html, 2448 KB); API `dotnet run --project src/Hcc.Api` (Development, HTTP 127.0.0.1:5000); disposable DB `HccTest_E2E_20261026091907` on `.\SQLEXPRESS` (dropped at the end; EtpReporting / EtpScratch untouched).
Driver: Playwright (Chromium) on desktop width 1366x850, one phone-width shot (390 px). Evidence: `05 Delivery Evidence\Acceptance\W4_E2E\*.png` (numbered in test order), `console_errors_session.txt`, `scripts\` (test-only helpers: same-origin proxy, setup/import/POS seed; no credentials).
Fictional credentials were generated at run time and kept outside the repo.

## How it was hosted (finding H1)
`Hcc.Api` does NOT serve the web app (no static files, no CORS). The client's "same origin" default therefore needs a reverse proxy/static host in front. For the test a tiny node script served `index.html` and proxied `/api` to the API on one origin (`scripts\proxy.mjs`). Nothing in P7 `deployment\` or the READMEs says how the production HTML is delivered to browsers on the LAN (only the APK has a server-address screen). Severity S2 for a "web" deployment (documentation/product gap), none for the APK path.

## Setup steps
| # | Step | Expected | Actual | Result |
|---|---|---|---|---|
| S1 | Create DB, `migrate` | schema applied | Schema 0 -> 2701, 43 migrations | PASS |
| S2 | `GET setup/status`, `POST setup/SetupProperty` (loopback) | property + Owner | setupRequired true; property 1, Owner 1, business date 2026-10-26, 2 settings seeded | PASS |
| S3 | Import samples: room-types, rooms, tariffs, seasons, staff, ledgers, opening-balances (as-at 2026-10-25), till-cash, guests (upload then commit) | all committed | 9/9 batches 0 errors; debits = credits (16,20,000); till FD-1 ₹25,000 | PASS |
| S4 | SetCredential for 5 staff, RegisterDevice | credentials set | PIN for MGR/REC/CSH/HKP, password for ACC; device token issued | PASS |
| S5 | POS outlets/menu | needs data | No import template or UI for outlets/menu; seeded via `CreateOutlet`/`SaveMenuItem` API (see D6) | PASS with workaround |
| S6 | Guest-ID encryption key | n/a in docs | Check-in with ID number failed until `Hcc:GuestIdKeys` was configured (see D3) | workaround |

## Core day walk-through
| # | Step | Expected | Actual | Result |
|---|---|---|---|---|
| 1 | Owner password sign-in | home with modules | 11 module tiles, "Server data as of" strip (01) | PASS |
| 2 | Rooms board (Owner) | 6 rooms, Ready | 101-103 STD, 201-202 DLX, 301 STE all Ready (02) | PASS |
| 3 | New reservation: availability, quote, create | 6 available, rack ₹2,800 STD, ₹5,600 + GST; booking created | matches; 301 shows ₹7,500; booking "Confirmed" HCC-1101 room 101 (03, 04) | PASS |
| 4 | Guided check-in with registration (ID type/number validation, tariff, GST) | validation, ₹5,600 + 5 % = ₹280 | DL pattern validated; ID stored once key configured; GST ₹280; checked in (05, 06) | PASS (after D3) |
| 5 | Post a charge | laundry ₹500 + 18 % = ₹590 | ₹500 / ₹90 / 18 % (07) | PASS |
| 6 | Take a payment | UPI ₹300 receipt, balance ₹290 | receipt shown, balance ₹290 (07). Receipt line shows "by" with no name (D8) | PASS |
| 7 | POS order to room (room service, KOT, settle to folio) | ₹340 + 5 % = ₹357 on folio | bill POS/2610/0001 ₹357 posted to folio line "Room service ₹340 / ₹17 / 5 %" (08, 08b) | PASS (outlets seeded via API) |
| 8 | Expense (Owner submits, Manager approves) | self-approval refused; manager approves and posts | "Your request - another approver needed"; Manager "Approve & post" -> Posted ₹450 (09, 12). Submitter shown as "by 1" to other users (D8) | PASS |
| 9 | Cash shift: open (API), count & close as Cashier | closed, difference nil | Opening via API only: UI showed "Shift closed" with no open control (D4). Count ₹25,000 -> closed, difference ₹0 (13b, 15) | PASS with D4 |
| 10 | Shorten stay to 27 Oct | departure changed | "Departure changed to 27 Oct. Availability revalidated." | PASS |
| 11 | Day close 26 Oct (Manager): preflight, post room nights, close | room night ₹2,800 +₹140; closing record; next day opens | Blocked first by pending expense and not-yet-posted night, cleared; closed; business date 27 Oct opened; closing snapshot with SHA-256; GST ₹247 = 140 + 90 + 17 (10, 18) | PASS |
| 12 | Check-out | balance cleared, room dirty, invoice issued | Card ₹3,587 posted; checkout failed on invoice (no hotel GSTIN) AFTER the stay was already Departed (D1); after fixing GSTIN and issuing invoice by API: INV/2627/0000001 ₹3,887, list shows Departed + invoice (19_checkout_done, 19_checkout_invoice_issued) | FAIL (D1, D2) |
| 13 | Housekeeping flow (Housekeeper): task, start, checklist, complete | task visible, completes | Task was created unassigned and invisible to the Housekeeper until the Owner assigned it; then start -> 3 checklist ticks -> Awaiting inspection (20, 20b, 21) | PASS (assignment is manual) |
| 14 | Manager inspect & release | room Ready | Room 101 Ready (22) | PASS |
| 15 | Day close 27 Oct, next day opens | closed, 28 Oct open | shift auto-opened for 27 Oct, count ₹25,000, closed, business date 28 Oct (23) | PASS |

## Role views and read-only mode
| # | Step | Expected | Actual | Result |
|---|---|---|---|---|
| R1 | Manager home and actions | operational modules | 11 tiles incl. Admin & Setup; approvals, day close, inspections all worked (11, 12) | PASS |
| R2 | Reception (PIN) | room board, bookings | **Room board empty (0 rooms)**: `GET /rooms/board` returns 403 for PERM-HK-001, which REC does not hold (16, 17) | FAIL (D5) |
| R3 | Cashier (PIN) | folios, take payments | **"1 area could not load"; Guests & folios empty**: reservations list 403 (PERM-RES) so the Cashier cannot see any folio (13, 14) | FAIL (D5) |
| R4 | Housekeeper (PIN) | tasks only, no money | 4 tiles, no money pages (20) | PASS |
| R5 | PIN sign-in in a browser | works on a registered device | Works once `hcc-device-token` is in localStorage (set by the test); without registration only the password form exists. The browser has no screen to register a device (APK only) | PASS with note (D7) |
| R6 | Stop API: banner, actions refused, last data shown | read-only banner, disabled actions, cached data | "Server unavailable - read-only ... Showing data as of ..." with all 6 rooms; "Confirm reservation" disabled (24, 25) | PASS |
| R7 | Restart API | recovers | Session survived the restart, banner gone after Retry/refresh, fresh data (26) | PASS |
| R8 | Phone width 390 px | usable layout | Room board renders as cards (27) | PASS (one spot check) |

## GST spot checks
| Case | Expected | Actual | Result |
|---|---|---|---|
| STD ₹2,800 | 5 % = ₹140 | ₹140 | PASS |
| STE rack ₹7,500 (quote 30 Oct) | 5 % | taxBps 500, ₹375 | PASS |
| STE tariff ₹8,000 (quote 1 Nov) | 18 % | taxBps 1800, ₹1,440 | PASS |
| DLX ₹4,500 | 5 % | ₹225 | PASS |
| Laundry ₹500 | 18 % | ₹90 | PASS |
| Room service / food ₹340 | 5 % | ₹17 | PASS |
| Folio total | 2,800+140 + 590 + 357 = ₹3,887 | ₹3,887 (invoice total 388,700 paise) | PASS |
| Seasons: imported "Diwali peak +15 %" 5-12 Nov | DLX ₹4,500 -> ₹5,175 | QuoteStay 5 Nov returns ₹4,500 (season not applied) | Unclear, see D9 |

Totals: 37 checks: **31 PASS, 3 FAIL (checkout+invoice, Reception board, Cashier folios), 1 unclear (season quote), 2 PASS with workaround (POS seed, ID key).** Of the PASSes, 2 carry a note (cash shift D4, PIN browser D7).

## Defects
| ID | Sev | Title | Repro / evidence |
|---|---|---|---|
| D1 | **S1** | Checkout is not atomic with invoicing: stay becomes Departed, then `IssueInvoice` fails (409 "Record the hotel's GSTIN") and the UI reports an error while still showing "In house" until reload. The guest is checked out with no invoice and the UI has no "Issue invoice" action for a departed folio. | Setup without GSTIN (documented as optional) -> check in -> settle -> Checkout -> toast "Action not allowed now: Record the hotel's GSTIN". `GET /reservations/1` = Departed; `GET /invoices/by-source/folio/1` = 404 (19_checkout_done.png). Fix: validate invoice prerequisites before `CheckoutStay`, or make the invoice step retryable from the folio. |
| D2 | **S2** | Master configuration: second edit of a setting always fails "Record changed ... Reload and try again" (STALE_VERSION), even after a full page reload. Also `brand.gstin` accepts a number with a bad check character, which then blocks invoicing ("check character is wrong"). The UI note says the GSTIN is "never validated". | Master configuration > Property and branding > GSTIN: save a value, then change it again. Workaround used: `SetSetting` with the row's current `version`. |
| D3 | S2 | Guest-ID encryption key is not part of the install/deployment guide: without `Hcc:GuestIdKeys` check-in with an ID number is refused (503 "Full ID numbers cannot be stored ..."), and the 503 flips the whole app into the read-only banner. | Check in without the key (05_checkin_step.png). Key set from the module README procedure, then fine. |
| D4 | S2 | After the till-cash import the first day's shift is already "Closed" (counted opening); the Cashier shift page shows "Shift closed - cash receipts are closed for today" with no Open button, and cash cannot be taken on go-live day from the Cashier's screen. `OpenShift` works through the API; the only UI button is "Open cashier shift" inside Day closing. | Fresh install after import, sign in as Cashier > Cash & Billing > Cashier shift (13_cashier_home.png). |
| D5 | **S1** | Role grants do not cover the screens' loaders: Reception gets 403 on `/rooms/board` (PERM-HK-001) so the front desk's room board is empty; Cashier gets 403 on `/reservations` (PERM-RES) so the folio list is empty and payments cannot be taken. The client also fires many GETs the user cannot see (stock/*, yield, cashbook, handover templates, closing records) -> console noise and "1 area could not load". | Sign in as `priya.reception` / `ramesh.cashier`; see 14, 16, 17 and `console_errors_session.txt`. Either extend the REC/CSH grants (decision for owner: role matrix v2 D1-D7) or gate the loaders by `requires` and give those roles a board source. |
| D6 | S2 | POS outlets and menu have no import template and no setup screen; the screen says "Load a demo scenario" in production. A new hotel cannot sell from the POS without API calls. | Restaurant & room service on a fresh install. |
| D7 | S3 | Browser has no way to register a device, so reception/cashier/housekeeping cannot PIN-sign-in from a PC browser (only APK flow, `hcc-device-token`). Probably by design (DEC-062) but undocumented for web. | Sign-in screen shows only Login/Password. |
| D8 | S3 | Display names missing: receipts show "by" with no name; the expense register shows "by 1" instead of the submitter's name for other users. | 07 and 12 screenshots. |
| D9 | S3 | Seasons import (Diwali +15 %) did not change `QuoteStay` for 5 Nov (still BAR ₹4,500). May need a yield-rule step, or seasons only affect a different rate plan. Needs a product decision, not tested further. | `scripts` + `QuoteStay` roomId 4, 5-6 Nov. |
| D10 | S3 | Production build still shows prototype wording: footer "Local prototype - Fictional records only - Scenario SERVER", "Prototype M1", "Fictional property", "simulated clock", and Master configuration says the demonstration watermark is "always printed" on invoices. | Footer on every screen. |
| D11 | S3 | Housekeeping task created at checkout is unassigned; a Housekeeper sees "No tasks are assigned to you" until a supervisor assigns it. Expected by design (assign step) but no notification/hint. | 20_housekeeper_tasks.png. |
| H1 | S2 | API does not host the web app (see "How it was hosted"). | n/a |

Console: 401 on `/me/capabilities` before sign-in is expected. Server errors seen: 503 (ID key), 403 (role gaps), 409 (invoice). No 5xx apart from the 503.

## Not tested / limits
- Cash tender on a folio from the UI (Cashier cannot see folios, D5; Owner has no shift): cash movement only checked through shift count (expected ₹25,000 unchanged because UPI/card were used).
- Company credit, credit notes, refunds, groups, OTA, booking website, payroll, stock, reports, accounting vouchers.
- Real LAN HTTPS (Development HTTP used; `appsettings.Production.json` / certificate path not exercised), Windows service install, backup scripts.
- Android APK (browser only), tablet layouts; phone width spot check only (one screen).
- Concurrency / multi-user races; browsers other than Chromium.
- Seasons/yield behaviour (D9) and Reception's "New reservation" path (board is empty for REC but the dialog was not exercised as REC).
