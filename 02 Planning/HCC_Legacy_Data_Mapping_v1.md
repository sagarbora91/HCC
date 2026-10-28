# HCC legacy data mapping v1 — old "Lodge" software → HCC

Draft 28 Oct 2026. Source profile: `E:\Projects\HCC\Existing Software\DB-PROFILE\README.md` (row counts, fill rates, schema CSVs) and the compacted copies in `DB-PROFILE\compacted\LodgeYY-YY.mdb` (251 MB for ten financial years; same password as the live files, hard-coded in `Forms\Forms\Module1.bas` ~line 944). Target: the production SQL Server schema in `08 Production\database\migrations` and the masters import templates in `08 Production\deployment\import-templates`.

Status: **decided 28 Oct 2026** — the owner applied the recommendations of §8 as they stood (APR-021 → DEC-161…164 in `HCC_Decision_Register.md`). Scheduled as wave **W1a LG — legacy data** in `HCC_Build_Plan_v2_2026-10-27.md` (branch `claude/lg`, migrations 4600–4619). Two inputs are still owed: the rack-rate card (Q9, OP-18) and the CA's word on invoice-number continuity (Q10, OP-19).

---

## 1. What exists on each side

| Legacy (Access, one MDB per FY 2017-18 … 2026-27) | Rows (all years) | HCC target | Notes |
|---|---:|---|---|
| `CustomerInfo` — guest master, carried forward every year | 17,512 in Lodge26-27 | `gst.guest` (+ `gst.guest_id_secret`, `crm.customer_tag`) | Latest file alone is the full master |
| `RoomBooking` + `RoomBookingDetail` + `TransactionData` — check-ins | 24,622 | history store (§4) or `inv.reservation` + `gst.stay` + `gst.stay_segment` | one row per check-in; 1.5 % have several rooms |
| `TransactionEnd` + `SalesBill` + `SalesBillDetail` — check-out bill | 24,581 | history store or `bill.folio` + `bill.charge` + `ivc.invoice` | NetBill, GST, F&B, laundry, discount, advance |
| `CustomerDeposit` — every receipt | 30,921 | history store or `pay.receipt` | Advance vs Bill Payment; Cash / Online / Card / Cheque |
| `CustomerRefund` | few hundred | `pay.receipt` kind refund | |
| `CustomerOpening` — guest balances at 1 April | 756 (FY26-27) | ACC-1 party opening balances (`imp.opening_balance`, party sub-ledger) | positive = guest owes, negative = hotel holds |
| `TransactionRoomShift` — room moves | ~1,700 | `gst.stay_segment` | |
| `RateChangeDetail` — rate changed mid-stay | ~4,000 | `inv.reservation_night.rate_paise` per night, or a history note | |
| `PaidOut` + `ExpensesMaster` — cash expenses | ~8,000 | `ops.expense` (SCC-ported expenses) | only if history is wanted; masters are free text |
| `RoomMaster` / `PackageMaster` / `RoomTypeMaster` | 36 real rooms, 5 packages | `inv.room`, `inv.room_type`, `inv.room_type_tariff` (templates rooms / room-types / tariffs) | |
| `IdentityType` | 27 (6 in real use) | `mst.id_type` (template id-types) | |
| `PaymentTypeMaster` / `CustomerDeposit.CashType` | 4 in use | `mst.payment_method` (template payment-methods) | |
| `FacilityMaster` | EP / CP / MAP / AP | `bill.meal_plan` (template meal-plans) | same codes HCC already seeds |
| `Citymaster` / `Statemaster` / `CountryMaster` | 796 / 76 / 32 | folded into `gst.guest.address` text | HCC has no city master |
| `GroupMaster` + `CustomerInfo.GroupID` | 17 | `crm.customer_tag` or `mst.booking_source` | meaning unreliable, see §2.3 |
| `EmployeeMaster` | 30 | `hr.staff` via staff.csv | manual, owner decides who is still employed |
| `UserDetail`, `SoftwareSetting`, `LedgerHead`, `AccountGroup`, restaurant / laundry / function / Tally tables | – | **not migrated** | logins are re-created; 131 tables were never used |

Money in the legacy files is `Single` (float) rupees with paise as decimals; HCC stores integer paise. Dates are Access `Date` with time; HCC uses `DATE` business dates in IST plus `DATETIME2` UTC.

---

## 2. Guest master → `gst.guest`

### 2.1 Source rows to take

Read `CustomerInfo` from **Lodge26-27.mdb only** (the full master). Checked: 17,023 of the 17,033 rows in 25-26 are identical in 26-27, and 1,734 of the 1,790 rows from 17-18 are still identical, so earlier files add nothing except the few rows that were edited later (the latest file already has the edit). Skip `deleteflag = 'Y'` (315 rows) unless they have stays in the history store, in which case import them inactive.

### 2.2 Field mapping

| Legacy `CustomerInfo` | HCC `gst.guest` | Rule |
|---|---|---|
| `GuestID` | new column `gst.guest.legacy_guest_id BIGINT NULL` (unique filtered index) | Every history row refers to it. Not the customer key: Customers 360 keys by the last 10 digits of the phone (see §9), so `lgc.*` rows also carry the normalised phone. |
| `Name` (Text 115, mostly UPPER CASE) | `full_name NVARCHAR(200)` | Trim, collapse double spaces, convert to Title Case (owner to confirm: HCC prints names on GST invoices and registration cards). Keep the original in `notes` only if it changed. |
| `CustCategoryCode` (1 Mr., 2 Miss., 3 M/S., 4 Mrs., 5 Smt.) | – | HCC has no title field. Drop; `3 = M/S.` (67 rows) marks a company guest → set `company` from `Name` when `CompanyN` is a placeholder. |
| `Gender` (M 17,028 / F 483) | – | No field. Drop (or `crm.customer_tag` if the owner wants it for reports). |
| `PhoneNo` (Text 50) | `phone VARCHAR(20) NOT NULL` | Strip spaces, `-`, `+91`, leading `0`. Accept only 10 digits starting 6–9 (14,574 rows). The 2,240 placeholder rows (`0`, `00`, `-`, `123`, `NO`) and ~700 odd values **cannot** be imported through `guests.csv` (phone is required) — see Q1. |
| `Email` | `email` | Always `-`; import as NULL. |
| `Address1`, `Citycode1` → `Citymaster.CityName`, `Statemaster.StateName`, `PinNo1` | `address NVARCHAR(400)` | `Address1 + ', ' + City + ', ' + State + ' ' + Pin`, skipping placeholders (`-`, `0`, `.`). Address1 often holds only the city name; de-duplicate when City equals Address1. `Address2`/`Citycode2`/`PinNo2` are empty. |
| `Nationality` (13 real values), `CountryID` | `nationality NVARCHAR(60) DEFAULT 'Indian'` | `Indian` unless Nationality is a real non-Indian word or `IDCode = 1` (passport). Normalise spellings (SWIZERLAND → Swiss etc.). |
| `IDCode` → `IdentityType` | `id_type NVARCHAR(40)` = `mst.id_type.code` | 8 Aadhaar → `AADHAAR`; 2 → `DL`; 3 → `VOTER`; 5/6 → `PAN`; 1 → `PASSPORT`; 7 → `OFFICE-ID`; 4, 9–25 → `OTHER`; 0 (1,459 rows) → NULL. Codes must match the id-types file the owner imports (§6). |
| `IDNumber` (Text 15, plain) | `id_last4` + `gst.guest_id_secret.sealed` | DEC-068/070: keep the full number encrypted with the guest-ID key, show last 4. Normalise (digits/upper, no spaces). Accept Aadhaar only when 12 digits (7,344 rows); other types when 5–20 chars; placeholders (`-`, `0`, `00`, `.`, `123`) → NULL. **Never** put ID numbers in any CSV — the importer refuses such a column (DEC-MA-8b), so the ID part needs its own importer that writes `gst.guest_id_secret` through the existing sealing code. |
| `PassportNo`, `Passport*`, `Visa*`, `ArrivalDateInIndia` (RoomBooking) | `doc.id_capture` / Form C data | ~11 real passports. Not worth automating; the owner re-captures at the next stay. |
| `DOB` (2,593 real), `AnniversaryDate` | – | `gst.guest` has no DOB. Keep in `notes` as `DOB 1985-03-12` **or** add `dob DATE NULL` (Q4). Birthday messages in `msg.*` would want a real column. |
| `CompanyN` (~100 real), `GSTNo` (2,934 × 15 chars) | `company`, `gstin VARCHAR(15)` | Validate GSTIN pattern `NNAAAAANNNNANAN`; 47 rows share one GSTIN (a corporate account) → also create one `grp.company_account` / companies.csv row per distinct GSTIN with ≥ 3 guests (Q5). |
| `Remark`, `VehicleNo`, `DriverName` | `notes NVARCHAR(1000)` | Append non-placeholder values as `Vehicle MH24…`, `Remark …`. |
| `Discount` (Single) | – | A per-guest standing discount %; HCC uses discount reasons per folio. Drop, mention in notes when > 0. |
| `Photpath` (524), `IDProofPath` (5,053) | `doc.id_capture_image` | File names in `SupportFile\Photo` and `SupportFile\IDProof` on the live PC. Copy the files that exist, link by `legacy_guest_id`; ones that are missing on disk are skipped. Separate step; the images are ID documents (FLAG_SECURE, ID-view permission). |
| `GroupID` | `crm.customer_tag` | See 2.3. |
| `deleteflag` | `verified = 0`, inactive | §2.1. |
| – | `origin` | `'manual'`; `created_utc` = import time; `verified` = 1 only when phone and ID both valid (Q6). |

### 2.3 Duplicates and identity

- Distinct 10-digit phones: 12,411 for 14,594 phone-bearing rows, so ~2,200 rows share a phone with another row (families, repeat walk-ins typed again, drivers). Rule: group by normalised phone; if the names fold to the same string (case, spaces, initials) keep one guest, keep every `legacy_guest_id` in `crm.customer_alias` (`alias_key` = legacy id) so history still resolves; if the names differ, keep separate guests (HCC allows the same phone twice; the Customers 360 merge proposal handles the rest).
- `GroupID` is the "Guest Grouping / Occupation" master but its values are nonsense in practice (16 = AGODA on 8,703 guests, 21 = not in the master on 4,473, 1 = "Laundry" on 1,141). It cannot be mapped safely. Proposal: tag `OTA:AGODA` / `OTA:MMT` where the master row is an OTA name, drop the rest (Q7).

### 2.4 How to load

1. Guest rows with a valid phone → `guests.csv` (template columns `name,phone,email,address,nationality,company,gstin,notes`) through the existing `Import-HccMasters.ps1` step 9. "A guest with the same name and phone already in HCC is not changed."
2. ID numbers, legacy ids, DOB, photos → a new **legacy importer** (one `Hcc.Tools.LegacyImport` console or a PowerShell + stored-procedure pair) that runs after step 1 on the server PC, finds each guest by name + phone, writes `legacy_guest_id`, seals the ID number with the guest-ID key, and copies the images. Owner-only, dry-run first, result JSON like `imp.batch`.
3. Guests without a valid phone: not loadable today (Q1).

---

## 3. Rooms, types, tariffs, lookups → masters templates

The legacy `RoomMaster` has 117 rows but only **36 room names were ever booked** (all `AType = Y` = AC): 101–107, 201–216 (not every number), 301–314, and the halls `CON` / `CONAC` (conference hall, 256 bookings). Rows 1–117 named by their own number, `df`, `7df`, `2244`, `1123` … are placeholders. Take only rooms with at least one booking, plus any the owner says are new.

| Legacy | HCC template | Rule |
|---|---|---|
| `PackageMaster.RoomType` 1 Luxury Suit, 2 Premium Room, 3 Deluxe Room, 4 Conference Hall, 5 Twin Rooms (6 "209" and 0 are junk) | `room-types.csv` `code,name,maxOccupancy,rackRate,sortOrder` | `SUITE`, `PREMIUM`, `DELUXE`, `TWIN`; `CONF` is a hall → HCC hall booking (HB wave), not a room type (Q8). `maxOccupancy` 2 (3 for suites); `rackRate` = `DoublePersonAC` ÷ 1 rupee. |
| `RoomMaster.RoomName`, `PackageID` | `rooms.csv` `roomNo,roomType` | 36 rows; `206` was Deluxe from 24-25 (earlier a Twin at a different id) — use the latest. |
| `PackageMaster` rates (Single/Double/Extra AC) | `tariffs.csv` `roomType,effectiveFrom,rate` + occupancy-charges.csv `extraAdult` = `ExtraPersonAC` | effectiveFrom = cut-over date. Real billed rates differ (1,286 / 1,429 / 2,000 / 2,667 / 3,000 are typical TransactionEnd room charges), rates are inclusive-of-GST in several bills (1,786 + 12 % = 2,000) — the owner sets the real rack rates (Q9). |
| `FacilityMaster` 4 EP, 5 CP, 6 MAP, 7 AP | `meal-plans.csv` | Codes already exist in the HCC seed; map `RoomBooking.FacilityID` to them in history (62 % CP, 38 % EP). |
| `IdentityType` | `id-types.csv` `code,name,accepted,needsPhoto,foreignerOnly,sortOrder,active` | `AADHAAR, DL, VOTER, PAN, PASSPORT (foreignerOnly), OFFICE-ID, OTHER`. |
| `PaymentTypeMaster` / `CashType` Cash, Online (`ChequeNo = 'QR'` ⇒ UPI), Credit Card, Cheque | `payment-methods.csv` | `CASH, UPI, CARD, BANK` — the seed already has these; `Cheque` (6 rows in 25-26) → `BANK`. |
| `GroupMaster` OTA names (AGODA, MMT/Make My Trip, gobibo in PurposeOfVisit) | `booking-sources.csv` | `AGODA`, `MMT`, `GOIBIBO` segment OTA, commission to be set by the owner. |
| `Statemaster.GSTStateCode` 27 (Maharashtra) | `cmp.profile` / GST setting | Place of supply = the hotel (DEC-136); nothing to import. |
| Bill numbers: `TransactionEnd.BillNo` restarts each FY (3,223 in 25-26, 831 by 22 Jun 2026); `GRCNo` is 0 everywhere | `number-series.csv` | `INV` series `start_no` = last legacy BillNo + 1 **if** the owner wants continuity inside FY 2026-27 (Q10); GRC/REG series starts at 1. |

---

## 4. Stay, bill and receipt history

### 4.1 Where it goes — the design decision (Q2)

HCC's transactional tables are not a fit for 24,600 past stays: `inv.room_night` enforces exclusive inventory per night, `gst.stay` needs a folio, charges post GST from the dated master, receipts move cash through `pay.cash_movement`, and the day-close / accounting waves assume every row was produced by a command. Replaying ten years through them would also create ten years of accounting entries the CA never saw.

Proposal: a read-only **legacy history schema** `lgc` (one migration, ~4 tables) that Customers 360 and the reports read beside the live data:

| `lgc.stay` | `lgc.bill` | `lgc.receipt` | `lgc.room_segment` |
|---|---|---|---|
| legacy_fy, legacy_booking_id, legacy_guest_id, guest_id (resolved), guest_name_snapshot, phone_key (last 10 digits, the Customers 360 key), arrival_utc, departure_utc, nights, adults, children, meal_plan, room_no (first), grc_no, purpose, source_tag, status (CheckedOut/InHouseAtCutover), paid_flag | legacy_fy, legacy_bill_no, stay ref, bill_date, room_charge_paise, extra_paise, fb_paise, laundry_paise, other_paise, discount_paise, gst_bps, gst_paise, net_paise, advance_paise, balance_paise, gstin_snapshot | legacy_fy, legacy_deposit_id, stay ref / legacy_guest_id, date, kind (deposit / payment / refund), method, amount_paise, reference | stay ref, seq, room_no, from, to |

Customers 360 (`src/domain/streams/customers.ts`: stays, nights, lifetime spend, avgRate, firstStay, lastStay, cancelled) can then count legacy stays, and the GST / audit trail of the old bills stays queryable without pretending HCC issued them. The alternative, "CSV archive only, nothing in HCC", costs nothing but loses the repeat-guest view the owner asked Customers 360 for.

### 4.2 Stay mapping (`lgc.stay`)

| Legacy | Target | Rule |
|---|---|---|
| file name `LodgeYY-YY` | `legacy_fy` | e.g. `2025-26`. Bookings at the FY boundary (late March) appear in two files — key on (`legacy_fy`, `BookingID`) and drop the copy whose `TDate` falls outside that FY. |
| `RoomBooking.BookingID` | `legacy_booking_id` | |
| `RoomBooking.GuestID` | `legacy_guest_id` → `guest_id` via `legacy_guest_id` / alias | `CompanyID` always equals `GuestID`; ignore. |
| `RoomBooking.FromDate` (datetime), `ToDate` | `arrival_utc`, expected departure | IST → UTC. `DaycloseType` Hours (99.6 %) with `Hours = 24` = 24-hour checkout system; `TransactionEnd.CheckInHours`, `LateHours` carry the same. |
| `TransactionEnd.DepartureDate` | `departure_utc`, `status = CheckedOut` | Bookings without a `TransactionEnd` row (`TransactionRoom.CheckOutFlag` = N for all rows is useless; 24,622 bookings vs 24,581 ends = 41 open at the time of each backup) → `InHouseAtCutover`; only the ones in Lodge26-27 matter at go-live (§5). |
| `RoomBookingDetail` rows (`RoomNo` → `RoomMaster.RoomName`, `Adult`, `Childrens`, `AdultFemale`, `RoomCharges`, `ExtraCharges`, `RType`) | `room_no`, `adults`, `children`, `lgc.room_segment` seq 1…n, nightly rate | 52 multi-room bookings in 25-26: one `lgc.stay` with several segments (HCC's split-folio wave may later want one stay per room). |
| `TransactionRoomShift` (`FromRoom`, `ToRoom`, `SequenceNo`) | further `lgc.room_segment` rows | Dates of the shift are not stored; set from = arrival for seq 1 and leave later `from` NULL with a note. |
| `RateChangeDetail` | note on the stay (`rate changed 2000 → 1786, reason NN`) | Dates not stored. |
| `FacilityID` | `meal_plan` EP / CP / MAP / AP | |
| `PurposeOfVisit` → `PurposeOfVisitMaster` | `purpose` text | 0 for 99.8 %; free text master (Pune, official, BYJUS …). |
| `NoOFPerson`, `NextDestination`, `Remark`, `VehicleNo` | `notes` | |
| `GRCNo` | `grc_no` | 0 in every year checked; column kept for completeness. |
| `RepresentID` 1 / 2 | – | Two values, no master; probably the two login users. Drop. |
| `CPhoto`, `ITProff` | – | Always NULL. |
| Foreign-guest columns (`ArrivalDateInIndia`, `Visa*`, `CFromNo`) | `notes` | ~0 real rows. |

### 4.3 Bill mapping (`lgc.bill`)

One `TransactionEnd` row per check-out (`SalesBill` repeats the same figures with arrival/departure times; `SalesBillDetail` holds the per-day split; `BillSummary` is empty in the latest file).

| Legacy `TransactionEnd` | `lgc.bill` | Rule |
|---|---|---|
| `BillNo` | `legacy_bill_no` | Restarts every FY; unique on (`legacy_fy`, `BillNo`). |
| `DepartureDate` | `bill_date` | = check-out moment (`IncrementBillNo = CO`). |
| `RoomCharg` (room tariff for the whole stay) + `ExtraCharge` + `ACCharge` + `ElectricalExpCharges` | `room_charge_paise`, `extra_paise` | × 100, round half away from zero. |
| `LuxuryTaxPer` (12 / 5 / 18) and `LuxuryTax` | `gst_bps`, `gst_paise` | "Luxury tax" is the room GST after 2017: 12 % up to 21 Sep 2025 (1,496 bills in 25-26), 5 % after (1,726) — matches HCC's dated GST master (DEC-069). `LuxuryTaxSlabType = Y` means slab by tariff. |
| `FB`, `Laundry`, `Others`, `Telephone`, `FoodGSTPer`/`FoodGSTRs`, `LaundryGSTPer`/`LaunderyGSTRs`, `OtherGSTPer`/`OtherGSTRs` | `fb_paise`, `laundry_paise`, `other_paise`, and their GST | F&B GST 5 % on 95 % of bills (even when FB = 0 the rate is stored). |
| `DiscountType` In % / In Rs., `Discount`, `DiscountAmt`, `BillDiscount`, `HalfDayDiscount`, `DonationDiscount` | `discount_paise` | Use `DiscountAmt` (already the rupee value) + `BillDiscount`. |
| `Advance` | `advance_paise` | Advance adjusted into this bill (the `CustomerDeposit` rows are the receipts themselves). |
| `NetBill`, `BalanceDue`, `Refundamt`, `PaidFlag`, `DataFlag` | `net_paise`, `balance_paise`, `refund_paise`, `paid_flag` | `DataFlag` C = complete (paid in full at check-out), P = pending balance (315 in 25-26); `PaidFlag` the same test on a different form (228 N). Source comment: "If total amount paid the Data flag save C-Complit.... Else P-Pending". |
| `VatPer`/`VatAmount`, `ServiceCharge*` | – | Pre-GST columns; zero after 2017. Import for 17-18 only if the CA wants it. |
| `Damages`, `DamagesRemark`, `WaterDonation` | `other_paise` + note | |
| `CustomerGstNoFlag` (always blank) + `GSTR1` rows (292 in 25-26; `GSTNo`, `TaxableAmt`, `GSTAmt`) | `gstin_snapshot` | `GSTR1` is the B2B list the old software produced; join on `BillNo` within the FY to mark B2B bills. |
| `SalesBillDetail` (per day `Tariff`, `LuxuryTax`, `ExtraCharges`, `DiscountAmt`) | optional `lgc.bill_night` | Only if per-night history is wanted for yield/forecast seeding (`yld.history`) — Q11. |

Reconciliation check per FY (the extractor must print these and the importer must match them): count of bills, Σ `NetBill`, Σ `LuxuryTax`, Σ `FB`. For 25-26: 3,223 bills, Σ NetBill ₹88,05,603, Σ room GST ₹6,62,978.

### 4.4 Receipt mapping (`lgc.receipt`)

| Legacy `CustomerDeposit` | `lgc.receipt` | Rule |
|---|---|---|
| `DepositID` | `legacy_deposit_id` | |
| `PaymentType` Advance (`Advance` column, `TransactionID = 0`, `BillNo = -1`) / Bill Payment (`DepositAmt`) | `kind` deposit / payment | 3 advances vs 3,419 payments in 25-26: the hotel collects at check-out. |
| `Date` + `TTime` (text "01:58 PM") | `received_utc` | |
| `CashType` Cash / Online / Credit Card / Cheque, `ChequeNo` (`QR` for UPI), `BankName`, `DepositBankCode` | `method` CASH / UPI / CARD / BANK, `reference` | `Online` + `ChequeNo = 'QR'` ⇒ UPI; `Online` otherwise ⇒ BANK. |
| `TransactionID`, `CustomerCode`, `RoomNo`, `BillNo` | stay ref / `legacy_guest_id` | Advances have no stay yet; link by guest. |
| `TDSAmount`, `CommissionInPer`, `CommissionAmount`, `SecurityDeposit`, `DirectBillAmount` | `notes` / separate kinds | Rare; OTA commission belongs to `ota.commission_entry` if ever needed. |
| `Usercode` | – | Old login id; drop. |
| `CustomerRefund` (`RefundAmt`, `CashType`, `billno`, `SecurityDepositRefund`) | `kind` refund | |

Reconciliation per FY: count, Σ `DepositAmt` + Σ `Advance` (25-26: 3,422 rows, ₹79,53,681 + ₹5,800).

### 4.5 Expenses (`PaidOut`) — optional

`PaidOut` has ~860 rows a year, every row `Type = Other`, `PTypeID = 0` (cash), `ExpensesID` → `ExpensesMaster.Expenses` which is a free-text list of 2,058 names (vendors and descriptions mixed, 2,048 of them in group 0). It can be loaded into `ops.expense` as cash expenses with category `PETTY`/`OTHER` and the name in the description, but each row would need a category and the month would already be locked. Recommendation: do not import; keep the Excel export for the CA (Q12).

---

## 5. Cut-over day (what must be exact)

Run on the evening the old software stops, from the live `Lodge26-27.mdb` (not the June backup):

1. **In-house guests** — bookings in Lodge26-27 without a `TransactionEnd` row. Re-create each in HCC by hand (reservation → check-in on the cut-over business date, room, rate, meal plan, advance received). Expected: 10–40 rows. The extractor prints the list with room, name, arrival, rate, advances so far.
2. **Advances not yet adjusted** — `CustomerDeposit` Advance rows whose guest has no later bill: enter as HCC deposits on the re-created reservations, or as party opening credit balances (ACC-1) when no reservation exists yet.
3. **Receivables** — `TransactionEnd` rows with `DataFlag = P` and `BalanceDue > 0` in 2026-27, plus `CustomerOpening` balances carried into 2026-27 (756 rows; positive = the guest owes, negative = the hotel holds the guest's money): party opening balances in `imp.opening_balance` / the ACC-1 opening screen (DEC-139/148), one per guest with the legacy id in the narration. The sum must equal the sundry-debtors figure the CA agrees for the cut-over date.
4. **Number series** — `INV` series starts at the next legacy `BillNo` if continuity inside the FY is wanted (Q10).
5. **Till cash** — not in the legacy data (`UsersChargeDetail.CashBalance` is the shift hand-over figure; the last row of the day gives the drawer): `till-cash.csv`.

---

## 6. Order of work

| Step | What | Depends on |
|---|---|---|
| 0 | Owner decisions Q1–Q14 — **done 28 Oct 2026** (APR-021, DEC-161…164); still owed: the rate card (Q9, OP-18) and the CA's answer on bill-number continuity (Q10, OP-19) | – |
| 1 | `tools/legacy/extract.py` (reads the compacted MDBs via ODBC/ACE, writes `guests.csv` + `rooms.csv` + `room-types.csv` + `tariffs.csv` + `id-types.csv` + `booking-sources.csv` in the template layouts, and `lgc_*.csv` + `cutover_*.csv`; prints the reconciliation totals; masks nothing because the files stay on the owner's PC, but **never writes ID numbers into the masters CSVs**) | profile, DEC-162 |
| 2 | Migration `4600_legacy_history.sql` (LG block 4600–4619): `lgc.*` tables, `gst.guest.legacy_guest_id`, `gst.guest.dob`, views for Customers 360 | DEC-161, DEC-162 |
| 3 | Legacy importer (server-side, Owner-only, dry-run, result JSON): guests part 2 (ids, DOB, photos), `lgc.*` load, reconciliation report against the extractor totals | 1, 2 |
| 4 | Customers 360 + reports read `lgc` (stays, nights, lifetime, first/last stay; "legacy" badge) | 2 |
| 5 | Dry run on this PC against the June 2026 backups; owner reviews 20 known guests | 1–4 |
| 6 | Cut-over evening: live MDB → extractor → masters import → importer → manual in-house re-entry (§5) → opening balances → first day close | all |

Scheduled in the build plan as wave **W1a LG — legacy data** (`claude/lg`) after ACC-1 (needs party ledgers for §5.3) and before GL go-live: Fable for the `lgc` design and identity rules (contract file), Opus for the migration and the importer, Sonnet for the extractor, the Customers 360 read path and the tests. Step 6, the cut-over evening, is an owner session in W9 (DEC-164).

---

## 7. Validation and reconciliation

- Per FY, per entity: row count, Σ money, min/max dates printed by the extractor, re-counted by the importer, asserted equal.
- Guests: count imported = valid-phone rows − merged duplicates; every `legacy_guest_id` referenced by a `lgc.stay` resolves to a guest or to a stub guest (§Q1).
- Spot checks: 20 guests the owner knows (stays per year, last bill amount) read from Customers 360 vs the old software's Guest Register report.
- GST: Σ `lgc.bill.gst_paise` per month 2025-26 vs the GSTR-3B the CA filed (owner supplies).
- Nothing in `lgc` is editable; the migration is re-runnable (truncate + reload by `legacy_fy`) while no business day is closed, locked after.

---

## 8. Decisions (owner applied the recommendations on 28 Oct 2026 — APR-021)

Recorded in `HCC_Decision_Register.md` as DEC-161 (history schema and scope: Q2, Q3, Q8, Q11, Q12), DEC-162 (guest import rules: Q1, Q4, Q5, Q6, Q7, Q13), DEC-163 (rates and number series: Q9, Q10) and DEC-164 (LG wave and cut-over: Q14).

| # | Question | Options considered | Decision |
|---|---|---|---|
| Q1 | 2,240 guests have no usable phone number. | (a) skip them; (b) placeholder phone — not allowed by the phone rule; (c) `phone NULL` allowance for legacy rows; (d) `lgc` only with a name snapshot | **(d)** — history keeps them (`lgc.stay.guest_name_snapshot`, `phone_key` NULL → name key); no `gst.guest` row; they get a real record the next time they stay. DEC-162 |
| Q2 | Where does ten years of stay/bill/receipt history go? | (a) `lgc` read-only schema; (b) replay into live tables; (c) CSV archive only | **(a)** — §4.1. DEC-161 |
| Q3 | How far back? | all ten FYs; or 2021-22 onwards | **All ten FYs** (2017-18 … 2026-27). DEC-161 |
| Q4 | Date of birth (2,593 guests) and anniversary. | notes text; or new `gst.guest.dob` column | **New column `gst.guest.dob DATE NULL`** (migration 4600); anniversary stays in `notes` until messaging wants it. DEC-162 |
| Q5 | Corporate accounts from shared GSTINs. | create `companies.csv` rows for GSTINs with ≥ 3 guests; or none | **Create** one company account per GSTIN with ≥ 3 guests; the extractor prints the list and the owner reviews it before the masters import. DEC-162 |
| Q6 | `verified` flag for imported guests. | 0 for all; or 1 when phone + ID both valid | **0 for all** — verification is a front-desk act. DEC-162 |
| Q7 | `GroupID` tags (AGODA / MMT / unknown codes 21, -1). | drop; or tag OTA names only | **Tag OTA names only** (`OTA:AGODA`, `OTA:MMT`, `OTA:GOIBIBO`); every other code dropped. DEC-162 |
| Q8 | Conference hall `CON` / `CONAC` (256 bookings). | history as stays with room `CON`; or wait for the HB wave | **History as stays now**; the HB wave may re-read `lgc` later. DEC-161 |
| Q9 | Rack rates and whether legacy rates were GST-inclusive. | owner supplies the rate card | **Owner's rate card** (owed — OP-18 in the build plan); legacy `PackageMaster` rates are not loaded as tariffs. DEC-163 |
| Q10 | Invoice numbering continuity inside FY 2026-27. | continue from the last legacy BillNo; or a new series with its own prefix | **New prefix from 1 unless the CA asks for continuity** (OP-19); GRC / REG series start at 1. DEC-163 |
| Q11 | Per-night bill split (`SalesBillDetail`) for yield history. | `lgc.bill_night`; or skip | **Skip in v1** (`SalesBillDetail` stays in the compacted MDBs if yield seeding ever wants it). DEC-161 |
| Q12 | Old cash expenses (`PaidOut`). | skip (Excel to the CA); or load as locked expenses | **Skip**; the extractor writes `cutover_paidout.xlsx` for the CA. DEC-161 |
| Q13 | Name case: legacy is UPPER CASE. | Title Case on import; or keep | **Title Case** (with the usual small-word and initial handling, e.g. `M/S.`, `Dr.`). DEC-162 |
| Q14 | Who runs the cut-over evening and when. | — | **Owner + one HCC session, a weekday evening after the last check-out**, the live PC stopped for the extractor; an owner session in wave W9. DEC-164 |

---

## 9. Known gaps in this draft

- Customers 360 keys a customer by the **last 10 digits of the phone** (`customerPhoneKey` in `src/domain/streams/customers.ts`), falling back to `n:<normalised name>` when there is no phone; `crm.customer_key` holds that same key. So `lgc.stay` must carry the guest's normalised phone (and name) so legacy stays group under the same key as live ones, even for guests who never get a `gst.guest` row (Q1 d). The `legacy_guest_id` column is still useful for the importer and the alias table, but it is not the customer key.
- `RepresentID`, `GroupID` 21 / -1, and the dates of room shifts and rate changes are not recoverable from the data.
- The 18 dated 2025 backups and four SQL Server `.bak` files in `Forms\Forms\.git` were not profiled; they are subsets of 24-25 / 25-26 and are superseded by the yearly zips.
- Lodge22-23 compacts to 70 MB against ~20 MB for neighbouring years; the extra is not in the row counts (probably memo/report data) and does not affect the mapping.
