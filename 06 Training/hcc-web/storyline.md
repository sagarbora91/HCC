# HCC training storyline (from plan TRN §3 and §4, kept current; planner Fable, 7 Nov 2026)

## 3. The storyline (fixed facts — put them in `steps.json.guest` and reuse verbatim)

Business date **Sat 24 Oct 2026** (the fixture's date; the FY26 history ends 23 Oct). Clock starts 08:00.

| Record | Facts |
|---|---|
| Main guest | **Mr. Rahul Deshmukh**, Pune, mobile 98XXXXXX01 (dummy), Aadhaar masked `XXXX XXXX 4321`, purpose business, car MH 12 AB 1234. Booking source Direct (phone). Arrival 24 Oct, departure 26 Oct (2 nights), **Deluxe 203, CP**, 2 adults (Mrs. Sneha Deshmukh), rate ₹3,000 + 5 % GST per night, advance ₹2,000 UPI at booking. Requests a wake-up call 06:30, two extra towels; a laundry charge ₹500 (18 %); dinner from room service ₹640 (5 %); one complaint (AC noise) → repair → service-recovery discount ₹300 approved by Manager; room moved 203 → 207 after the AC fault (second night). Check-out 26 Oct, balance paid by card, GST invoice printed, key card returned. |
| Walk-in | **Ms. Anita Rao**, Hyderabad, walk-in 24 Oct for 1 night, Classic 105, EP, pays cash in full at check-in; her counter bill for a friend's lunch (counter bill, paid now). |
| Company guest | **Shree Agro Industries** (GSTIN 27AABCS1234A1Z5, fictional) books **Mr. Vikram Jadhav** 24–25 Oct, Suite 206, on company credit (credit limit ₹50,000) — teaches company booking, credit limit, company-credit check-out, receivables in Accounting. |
| Group | **Patil wedding block**, 6 rooms 30 Oct–1 Nov, rooming list, block pickup — teaches Groups & corporate (create, hold rooms, pickup) without disturbing the main story's rooms. |
| Hall event | **Kulkarni engagement**, Hall A on 7 Nov 2026, 120 plates, enquiry → quotation with T&C → revision → confirmed with ₹20,000 advance → event-day bill at 18 %. |
| OTA | One inbound BookNest booking (fixture) applied, commission shown, payout received. |
| Restaurant | Table check T4: 2 × masala dosa, 2 × tea, KOT, settle UPI; one void with Manager approval; Rahul's room-service dinner posted to folio 203. |
| Housekeeping | 203 cleaned after the move, inspected by Neha; stayover service 206; room 108 blocked for deep clean and unblocked; lost & found: a charger found in 105 after Anita leaves. |
| Maintenance | Work order "AC noisy, room 203" raised from the complaint, assigned to Joseph, spare "AC capacitor" used, closed; room downtime 203 for 4 hours; preventive plan "Geyser service" shown; one asset with AMC. |
| Reminders | Wake-up 06:30 for 203 on 25 Oct; desk reminder "Collect ID copy from 206" with alert day. |
| Messages | Booking confirmation template to Rahul (simulated send), consent shown. |
| Stock | Linen issue 20 towels to floor 2, laundry batch out/in, GRN for 50 soaps from vendor "Sai Suppliers", monthly count. |
| Expenses | "Plumber visit ₹1,200, GST bill, ITC allowed" entered by Reception, approved by Manager; petty-cash float; an Udhaar entry; a recurring template (electricity) shown; month lock mention. |
| People | New joiner **Sunil Pawar, Room attendant** added, roster for the week, attendance marked, leave request by Kavya (1 CL) approved by Meera, payroll run for October (locked runs Apr–Sep exist), advance ₹2,000 to Ravi, payslip; Ravi's "My shifts & pay". |
| Day close | 24 Oct: pre-close list (pending expense approved, no pending credit note), night posting, close; 25 Oct opens; owner digest for 24 Oct next morning. |
| Accounting | Ledger of Rahul's receipts, party statement Shree Agro, journal voucher (owner's drawing), contra (cash to bank ₹20,000), loan entry shown, trial balance, day book, P&L and balance sheet for Oct, GST set-off for Sep + month lock, TDS on a contractor payment, Tally export file, bank reconciliation of one statement line, year-end screen (explain, do not run). |
| Admin | New user "Sunil" (Housekeeper, PIN), custom role "Night manager" from Manager base with Accounts view, switch **Reservations** sub-function off and on again (show the hidden page and the refused command), audit tab; Master configuration groups (Money and tax Owner-only), Masters: add floor 4? **No** — show room master edit of 203's amenities and the CSV import template instead; Approvals & audit; Backup now + restore drill; System health; Legacy import page (dry run button, explain only); Internet access; Roadmap. |

Operators: Reception = Aarav; Cashier = Priya; Supervisor = Neha; Housekeepers = Ravi, Kavya; Accountant = Farhan; Maintenance = Joseph; Manager = Meera; Tech admin = Lata; Owner = Sagar; Restaurant = the FY26 restaurant user if present, else Priya (say so in deviations).

Deviations to expect (write them down as found): fixture rooms differ from Hotel Parth; the demo has no sign-in; e-mail/WhatsApp/OCR are "not configured" simulations; Key Agent is the mock encoder; legacy import is explained, not run; year end is explained, not run.

---

## 4. Chapter table and coverage matrix (desktop deck)

Each lesson = one screen action per step, 3–8 steps per lesson, chapters sized as below (±20 %). **Every row of the feature catalogue and every page id appears in the "Covers" column; the executor ticks this table in `coverage.md` when the chapter replays cleanly.**

| Ch | Title (as role) | Pages | Covers (sub-functions, functions) | Steps |
|---|---|---|---|---|
| 0 | Welcome: Modules Home and the Dashboard (Aarav) | home, overview, prefs | tiles, breadcrumb, business date & clock, user chip, Ctrl+K search + quick actions, Appearance & display (text size, theme, start page), hamburger menu, help/shortcuts | 10 |
| 1 | Reservations and the Calendar (Aarav) | reservations, calendar | new reservation (Rahul), hold, confirm, amend dates, company booking (Vikram, Shree Agro), calendar grid, search, cancel a fixture booking, no-show mention | 22 |
| 2 | Arrivals, registration and check-in (Aarav) | arrivals, guests, keys, documents | expected arrivals, booking-ID lookup, registration card (every field), ID type + masked number, photo, check-in Rahul; walk-in Anita; Guest register profile; ID capture page; Room keys: issue, key log, re-issue lost card, cancel | 28 |
| 3 | The Room board (Aarav, Neha) | rooms | statuses and colours, filters, floor view, quick actions, block / unblock, room info drawer | 10 |
| 4 | In-house guests and guest services (Aarav) | inhouse, guest-services, gsv-my-requests, desk-alerts, messages | extend stay, room move 203→207, requests (towels) with SLA, complaint + recovery, lost & found, my requests view (Ravi), reminders + wake-up, messages: template, simulated send, consent, rules | 26 |
| 5 | The folio: charges, discounts and corrections (Priya) | folios, plans-extras | folio view, add catalogue charge (laundry), free-text charge, meal plans & extras page, discount request → Manager approval, correction, guest rate change (CTL-01 rule), company credit flag | 20 |
| 6 | Restaurant and room service (Restaurant user, Priya) | pos | outlet, open table check, items, KOT, room-service check posted to 203, settle UPI, void with approval, menu & prices, KOT/bill numbers | 18 |
| 7 | Cashier: shift, payments, cash book (Priya) | cashier, cashbook | open shift, receive advance UPI, card payment, drawer count, bank drop, owner drawing, close shift with difference; cash book day page, lock / reopen | 20 |
| 8 | Check-out, invoice, credit note, counter bills (Priya, Meera) | folios, counter-bills, approvals | settle Rahul (card), GST invoice (5 % split CGST/SGST, number series), print/PDF, duplicate; Vikram on company credit; refund; Owner-only credit note; counter bill paid now and Udhaar; Approvals queue | 24 |
| 9 | Housekeeping (Neha, Ravi) | housekeeping | board, assign task, start/finish, inspection pass/fail, stayover, block 108 / unblock, report repair from a room, complaints and lost & found entries, money masked (DEC-061) | 18 |
| 10 | Maintenance (Joseph, Neha) | maintenance | work order from the repair request, assign, spare part, vendor, close; room downtime; assets, AMC, preventive plan; masked money | 16 |
| 11 | Checklists and handover (Aarav, Neha) | handover | shift checklist complete, handover note, desk handover read | 8 |
| 12 | Customers 360 (Aarav, Meera) | customers | search by mobile, profile, stay history, tags, notes, preferences, consent, merge duplicates, legacy stays tab | 12 |
| 13 | Tariff, yield and the tariff card (Meera, Sagar) | yield, tariff-card | seasons, rate plans, restrictions, guardrails, override, publish; tariff card draft → propose → publish, rate sheet print | 16 |
| 14 | Groups, companies, OTA and website (Meera) | groups, channels, booking-site | group block + rooming list + pickup; company credit limit; OTA inbound applied, commission, payout; website request; guest feedback & QR | 20 |
| 15 | Hall booking (Meera) | hall-booking | venues & menus, enquiry, quotation + T&C, revision, discount, confirm, advance, event, event-day bill 18 % | 18 |
| 16 | Linen, stock and purchasing (Neha, Farhan) | inventory | linen issue, laundry batch, GRN, count, purchases & vendor payments, asset register (from Accounting), masked money for Supervisor | 16 |
| 17 | Expenses (Aarav, Meera, Sagar) | expenses | add expense with bill photo + GST + ITC, approve / reject, petty-cash float, Udhaar, recurring, budgets (Owner), edit/void with reason, month lock / Owner unlock | 16 |
| 18 | Staff, leave and payroll (Meera, Sagar, Ravi) | staff, my-hr | staff register, roster, attendance, leave request + approval (quota re-check), payroll run, advance, payslip, F&F mention, statutory (PF/ESIC/PT) view, My shifts & pay | 22 |
| 19 | Day close (Meera) | closing | pre-close checks, night posting, close day, reopen, closing reports, next day | 14 |
| 20 | Reports and the Report hub (Meera, Sagar) | reports, report-hub, owner-digest, revenue-category | reports page, hub search/run/filters/export/print/schedule/favourites, revenue by category, owner digest | 16 |
| 21 | Accounting (Farhan, Sagar) | accounting | chart, ledgers, party statements, opening balances, manual vouchers (journal, contra, receipt, payment), loans, trial balance, day book, P&L, balance sheet, cash ratio, ageing, GST set-off + month lock, TDS, Tally export, bank reconciliation, year end (explain) | 30 |
| 22 | Compliance calendar and the assistant (Farhan, Sagar) | compliance, assistant | obligations, firm profile, evidence, Action Center, ≈₹ payable; assistant questions and anomaly alerts | 12 |
| 23 | Administration and setup (Sagar, Lata) | access, settings, approvals, backup, syshealth, legacy-import, roadmap | users (create Sunil), roles (custom "Night manager"), Modules & features switch off/on, audit; Master configuration 12 groups (Money and tax Owner-only), Modules on/off; Masters (room 203 amenities, CSV import/export); Approvals & audit; Backup now + restore drill; System health; Legacy import (explain); Internet access; Roadmap | 34 |
| 24 | Closing: shortcuts, help, quick-reference (Aarav) | — | F-keys/shortcuts (`src/app/shortcuts.ts`), search palette recap, where to get help, quick-reference card | 6 |

Total ≈ **460 steps** → slides ≈ 1 + 25 + 460 + 25 recaps + 3 finals ≈ **514**.
