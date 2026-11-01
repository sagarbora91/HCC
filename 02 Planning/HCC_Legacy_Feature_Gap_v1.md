# Old Lodge software vs HCC: feature gap list (v1, 1 Nov 2026)

**Purpose:** list what the old VB6 "Lodge" software (live at Hotel Parth Executive) does that HCC does not, so the missing pieces can be built.

**Sources (old software):**
- `Existing Software\HTML Demo\FUNCTION_MAP.md`: 137 forms, every button probed.
- `HTML Demo\live\THIRD_LEVEL_MAP.md`: the live menu path of each form.
- `HTML Demo\live\reports.json`: 61 reports.
- `DB-PROFILE\rowcounts_matrix.csv`: rows per table per year. This shows what the hotel **actually uses**.
- The live database `Updated Version\Lodge26-27.mdb`.

**Sources (HCC):**
- `main` with ACC-2, plus W1a LG (`claude/fit` 541bbff7).
- The page list in `03 Prototype/src/app/navmap.ts`, and a code search for each feature.

"Used" means the live database has rows for it in FY 2025-26 or 2026-27. A feature whose tables are empty or unchanged for years is listed in §D as not used.

## A. Missing in HCC, and used by the hotel: build these

| # | Feature in the old software | Evidence of use | What HCC has today | What to build |
|---|---|---|---|---|
| A1 | **Rate change during a stay**, with reason, the authoriser's name and a password (Room Booking › rate / pax change) plus the **Rate Change Detail** report | `RateChangeDetail`: 1,609 rows in 25-26, 386 in 26-27 (Jun copy), 959 on 30 Oct | Discount with a reason on the folio; no "change this stay's nightly rate" with an approval and log | Change rate / guests for the rest of a stay (Manager PIN or approval, reason, old → new), audit log, report |
| A2 | **Correct a bill after check-out** (Bill Updation: change a day's room rent, remove a KOT line, delete a direct bill), with the Bill Updation report of old vs new | `BillUpdationOLD`: 683 in 25-26, 179 in 26-27 | Invoices are locked once issued; correction is by credit note only, one step at a time | One "Correct this bill" flow: issue a credit note and a corrected invoice together, with the reason and the approval (the GST-safe form of Bill Updation), plus a correction report. Posted invoices stay unchanged (no editing) |
| A3 | **Purpose of visit** at check-in (36-entry master) | Every `RoomBooking` row has it | No field | Field at check-in, master list (imported from Lodge), shown on the registration card and in reports |
| A4 | **Vehicle number and type** at check-in | `RoomBooking.VehicleNo`, `VehicleMaster` | No field | Optional field at check-in, on the registration card, searchable |
| A5 | **Foreign guest (Form C) details and the NRI / Foreigner report**: arrival date and place in India, visa number, issue place / date, expiry, stay duration, purpose | `RoomBooking` columns; report on the live menu | Nationality and ID type only; no Form C fields or report (known gap since 24 Oct) | Form C fields when nationality is not Indian, a printable Form C sheet and the foreigner register for the police / FRRO upload |
| A6 | **Check-out time rule**: 24-hour check-out (from the check-in time) or the fixed noon check-out, per booking, plus "Check-out time adjust hours" | `RoomBooking.DaycloseType`, `Hours`; `CheckOutTimeHours` setting | Fixed check-out time; day-use rates exist only in yield | Per-booking check-out basis (24-hour / fixed time), late-check-out hours, the night count following that basis |
| A7 | **Direct bill**: a bill for a party that is not staying (services, hall, sundry) without a room | `DirectBill`: 22 in 25-26, 13 in 26-27 | Restaurant walk-in bills only | "Counter bill" in Cash & Billing: any catalogue item to a named party, GST invoice from the INV series, paid or on credit |
| A8 | **Function keys** on the front desk (the live install uses F-keys for its main screens) | Live F-key settings | Ctrl + K search only | Keyboard shortcuts on the desktop layout for the main desk actions (new booking, check-in, payment, check-out, room board), listed in the user menu |
| A9 | **Reference / agent on a booking and commission to referrers** (Reference and Booking Wise report, Commission Payment Detail, Payment Commission master) | `RoomBooking.ReferenceCode` | Booking sources and OTA commission; no local referrer (agent, driver, company contact) with commission | **Not needed:** owner, 1 Nov 2026: "the hotel doesn't use referrers or agents with commission". Not built; the legacy `ReferenceCode` stays in the read-only history |

## B. Partly in HCC: small additions

| # | Old feature | HCC today | Addition |
|---|---|---|---|
| B1 | Guest-specific tariff (Guest Tariff Define; per-booking discount `CustomerDiscountSetting`, 836 rows) | Company rates in Groups & corporate; discount reasons | Remember a **regular guest's agreed rate** on the customer profile and offer it at booking |
| B2 | Guest opening balance and guest outstanding (756 opening rows; Guest Outstanding, Bill-wise Outstanding, Guest Ledger reports) | Party sub-ledgers and opening balances (ACC-1); company credit | **Individual credit guests** (not only companies) with a bill-wise outstanding list and a ledger; LG loads the old balances |
| B3 | Wake-up call screen that pops up at the set time | "Wake-up call" is a guest request type | A timed alert on the desk screen at the call time, with a done button |
| B4 | User-defined alerts and alert days (reminders before due dates) | Compliance calendar for statutory dates | Desk reminders: free-text alerts with a date and a role (e.g. "AMC renewal", "advance due") |
| B5 | Several photos and ID proofs per booking (one per guest in the room) | ID capture per guest record | Check that every guest in a room, not only the primary one, can carry an ID and a photo; add if not |
| B6 | Notes printed on the bill, bill header / footer titles (`NoteMaster`, `MainTitle`, `SubTitle`) | Invoice print settings (hotel name, GSTIN, address) | Owner-editable footer notes / terms on invoices and receipts |
| B7 | Summary of Documents Issued (GSTR-1 Table 13), SAC summary | GSTR-1 working summary | Table 13 must also list the legacy bill series for FY 2026-27 (DEC-197); add the SAC summary |
| B8 | SMS to the guest and owner, e-mail the bill (SMS API and e-mail settings are configured) | Messaging module, simulated in production | Already planned: GL go-live stand-ins (real SMS / WhatsApp / e-mail) |

## C. Already in HCC (no work)

Room booking, advance booking, check-in, room shift / move, extend stay, check-out, lodging bill and folios, guest payment / deposit / refund, discounts, day close with Counter Cashier hand-over, room status board, housekeeping (cleaning tasks and times), room complaints → maintenance, restaurant / room service with KOT, food and other items on the room bill, expenses and petty cash (paid out), owner and bank receipts / payments, contra / journal / payment / receipt vouchers, ledger heads and groups, opening balances, trial balance, Tally export, GST slabs (tariff GST, food / other GST), payment-type commission (MDR journal), purchases and stock (store), staff, attendance, salary, salary advance, users and rights, software settings, backup, financial-year handling, masters (city, state, country, ID types, rooms, room types, packages / plans, units, items, suppliers, designations). Hindi labels on printed documents are a setting.

## D. In the old software but not used by the hotel (not planned unless the owner asks)

Rows unchanged for years or zero: laundry issue / return / payment and guest laundry receive / return (no rows); store purchase bill, material consumption, scrap note, restaurant purchase (no rows); employee attendance / salary inside Lodge (static since before 2024: HCC's payroll replaces it); donation coupons, donor receipt, "Dan Data Sampark Patra" (static); water tanker log; guest grouping (static); function prospectus / function bill (halls: **planned anyway as W5 hall booking**); bill finalize renumbering (code commented out); multi-organisation selection; software registration and call log (vendor items).

## E. Reports (for the W7 report catalogue)

Old reports that HCC has no single screen for yet. Each becomes a catalogue entry in W7 (RPT); several draw on A1–A9:
- **Guests and stays:**
  - Userwise check-in / check-out
  - Rate change detail (A1)
  - Reference / booking-wise (A9)
  - Business from company
  - NRI / foreigner (A5)
  - Advance booking detail
  - Guest information
  - Guest tariff detail (B1)
- **Rooms:**
  - Room-wise transaction summary
  - Room-wise allocation detail
  - Occupancy average
  - Room-wise housekeeping period
  - Today's room status
  - Balance room
- **Money:**
  - Collection detail
  - Counter cash hand-over
  - Day end report / summary / detail
  - Paid-out detail
  - Direct bill detail (A7)
  - Guest refund detail
  - Guest ledger, guest outstanding, bill-wise outstanding (B2)
  - Commission payment detail (A9)
  - Owner / bank ledger
  - Multiple sales bill print
- **Tax:**
  - GST R1
  - Summary of documents issued (B7)
  - SAC summary
  - Lodge GST / tax report
  - Bill updation report (A2)

## Suggested build order

1. **Desk and legal:** A3 purpose, A4 vehicle, A5 Form C, A6 check-out basis. These are small fields with clear rules, and Form C is a legal duty.
2. **Money:** A1 rate change, A2 bill correction, A7 counter bill, B2 credit guests. They touch billing and GST, so these are Opus lanes.
3. **Speed and comfort:** A8 function keys, B1 regular guest rate, B3 wake-up alert, B4 reminders, B6 invoice notes.
4. A9 referrers: dropped (owner, 1 Nov 2026).
5. B7 / E reports go into W7 RPT; B8 into the GL stand-ins.

Proposed as wave **LF (legacy feature parity)** after FIT: about 6 lanes, prototype and server, contract and briefs in the usual ETP form.

## F. Found while sizing the database (owner's question, 1 Nov 2026)

HCC keeps documents and images **inside SQL Server** (`SqlDocumentStore`). SQL Server 2025 Express allows 50 GB of data per database.
- **Legacy history (W1a LG):** about 1.85 million rows across the ten yearly files, many of them repeated master copies. The imported `lgc` history is estimated at **0.2–0.5 GB**. The legacy photos and ID proofs on disk are 25 MB.
- **HCC's own data without images:** about 0.2–0.4 GB a year (stays, folio lines, payments, journal lines, audit, events, housekeeping, attendance), so more than 100 years to 50 GB.
- **Images are the risk.** ID images and documents are accepted up to 4 MB each, and are not compressed on the device. Only the bank-slip photo is, to under 100 KB. About 3,000 guests a year × 2 images × 2–4 MB comes to 12–24 GB a year, so the limit would be reached in 2–4 years.
- **Build item G1:** compress ID, document and expense-bill images on the device to about 150 KB, reusing the slip-photo compressor (`app/slipPhoto.ts`). That brings images to about 1 GB a year, roughly 1.5 GB a year in total, or 30+ years.
- **Option G2:** move images to a second database (`HccDocuments`), which has its own 50 GB. The Express limit is per database, and the log file does not count.
