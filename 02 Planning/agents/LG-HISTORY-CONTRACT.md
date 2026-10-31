# LG history contract (wave `claude/lg`, 28 Oct 2026)

The shared shapes every LG lane builds against. **Copy, never reinterpret.** A lane that needs a change writes it in its DONE file; the coordinator amends this file and tells the other lanes. Source of truth for the rules: `02 Planning/HCC_Legacy_Data_Mapping_v1.md` (decided §8, DEC-161…164) and the profile `E:\Projects\HCC\Existing Software\DB-PROFILE\README.md`.

## 0. Sources, secrets, where files may go

| What | Where | Rule |
|---|---|---|
| Legacy databases (read-only input) | `E:\Projects\HCC\Existing Software\DB-PROFILE\compacted\LodgeYY-YY.mdb` (ten files, 17-18 … 26-27). **`Lodge26-27.mdb` there is the compacted copy of the live database as of 30 Oct 2026 23:15** (source `E:\Projects\HCC\Existing Software\Updated Version\Lodge26-27.mdb`, the owner's "Updated Version" folder, which also holds the live program, `SupportFile\Photo` 74 + `SupportFile\IDProof` 19 + `Photo` 12 images, three dated 26-27 copies and `Data\Lodge2526-Org.bak`); the 22 Jun 2026 backup is kept as `Lodge26-27-asof-20260622.mdb` and is not an input. The live PC's file replaces it again on cut-over night. | Open with `Provider=Microsoft.ACE.OLEDB.12.0;Data Source=<file>;Jet OLEDB:Database Password=<pw>`. The password is read at run time from the legacy source `E:\Projects\HCC\Existing Software\Forms\Forms\Module1.bas` (regex `LodgeDatabasePassword\s*=\s*"([^"]+)"`), **never typed into any HCC file, test, log or chat**. ACE uses ANSI-92 SQL (`[6-9]#` LIKE wildcards do not work; use `Len` / `IsNumeric`; `COUNT(DISTINCT)` does not exist; cross-database joins are not supported). |
| Extractor output | `E:\Projects\HCC\Existing Software\LG-EXTRACT\<yyyyMMdd-HHmm>\` (outside the git repository) | Masters CSVs, `lgc_*.csv`, `cutover_*`, `totals.json`, `report.md`. **No ID numbers in any of these files.** |
| ID numbers and images | `E:\Projects\HCC\Existing Software\LG-EXTRACT\<run>\secret\guest_ids.jsonl` + `secret\images\` (copies of `Forms\Forms\SupportFile\Photo\*.jpg`, `SupportFile\IDProof\*.jpg`) | Read only by the legacy importer from a **server-side folder path** (never uploaded through the browser, never a `masters` CSV — DEC-MA-8b stands). The importer overwrites and deletes `guest_ids.jsonl` after a committed run. |
| Test fixtures | `08 Production/tests/Hcc.Legacy.Tests/Fixtures/` and `03 Prototype/tests/fixtures/legacy/` | Fictional rows only, shaped like the legacy tables; **never a row copied from the real files**. |
| Repository | `E:\Projects\HCC\worktrees\lg*` | MDBs, extracts and the secret folder are never committed; the wave's setup commit adds `*.mdb`, `*.accdb`, `*.ldb`, `LG-EXTRACT/` and `tools/legacy/.runs/` to `.gitignore`; the extractor refuses an output path inside a git work tree. |

Run every Python file with `python -I`; the extractor lives in `tools/legacy/` and is run from the repository root with absolute paths as arguments.

## 1. Identity rules (the Customers 360 key)

These are the rules of `03 Prototype/src/domain/streams/customers.ts` and `08 Production/src/Hcc.Domain/Customers/CustomerRules.cs`; the legacy data must produce the same keys so legacy stays group with live ones.

```
digits(p)      = p with every non-digit removed
phone_key(p)   = last 10 of digits(p) if len(digits(p)) >= 10 else ''        -- exactly customerPhoneKey()
phone_valid(p) = len(digits(p)) in {10, 11 with leading 0, 12 with leading 91}
                 and the last 10 digits start with 6, 7, 8 or 9               -- what guests.csv accepts
name_fold(n)   = lower(trim(n)) with every run of [^a-z0-9 ] replaced by one space, runs of spaces collapsed, trimmed
customer_key   = phone_key(phone) if phone_key(phone) <> '' else 'n:' + name_fold(name)
title_case(n)  = each word capitalised; keep 'M/S.', 'Dr.', 'Mr.', 'Mrs.', 'Ms.', 'Adv.', 'Er.', 'Capt.', initials like 'S.K.' as upper; words after '(' likewise; never change a word that already mixes case
```

Guests (`CustomerInfo`, taken from the **latest** file only, 17,512 rows in Lodge26-27 — `GuestID` is carried forward unchanged every year):

| Case | Rule |
|---|---|
| `phone_valid` and no earlier guest with the same `phone_key` | one `gst.guest` row (`origin = 'manual'`, `verified = 0`); `lgc.guest_map.guest_id` set |
| `phone_valid` and the same `phone_key` already produced a guest **with the same `name_fold`** (or one name fold is a prefix of the other, e.g. `ramesh patil` / `ramesh patil deccan agro`) | **no new row** (ruling LG-R1, 1 Nov 2026 — mapping §2.3 wins over the earlier phone-only wording): the higher `GuestID` wins the profile fields (name, address, ID type, company); the other GuestIDs map to that `guest_id` in `lgc.guest_map`; each keeps its own `name_snapshot` |
| `phone_valid`, the same `phone_key` exists, but a **different** `name_fold` (family members, a company phone) | a **separate `gst.guest` row** with the same phone (the guest table allows it); both share the same `customer_key`, so Customers 360 still groups their stays under one customer; `lgc.guest_map.guest_id` points at each one's own row |
| not `phone_valid` (2,240 placeholders such as `-`, `0`, `00`, `123`, `NO`, plus ~700 odd values and landlines) | **no `gst.guest` row** (DEC-162); `lgc.guest_map.guest_id` NULL; `customer_key` = `'n:' + name_fold` unless `phone_key` is non-empty (a 10+-digit landline keeps its digits as the key, exactly as Customers 360 does today) |
| `deleteflag = True` | same as above, plus `lgc.guest_map.deleted = 1`; a `gst.guest` row is still created when the phone is valid, with `notes` starting `Legacy: marked deleted` |

`GroupID` → tag only when `GroupMaster.GroupName` for that id is an OTA name (`AGODA`, `MMT` / `MAKEMYTRIP`, `GOIBIBO`, `BOOKING.COM`, `OYO`, `YATRA`, `TRIVAGO`, case-insensitive) → `crm.customer_tag` row `OTA:<NAME>` on the `customer_key`; every other code dropped.

Names in `gst.guest.full_name` and `lgc.*.guest_name_snapshot` are `title_case(trim(Name))`; `name_fold` is computed from the **original** name.

## 2. `gst.guest` additions (migration 4600, lane lg-schema)

```sql
ALTER TABLE gst.guest ADD legacy_guest_id BIGINT NULL;   -- the latest legacy GuestID that produced this row
ALTER TABLE gst.guest ADD dob DATE NULL;                 -- DEC-162 (Q4)
CREATE UNIQUE INDEX UX_guest_legacy ON gst.guest(property_id, legacy_guest_id) WHERE legacy_guest_id IS NOT NULL;
```

`GuestView` / `GuestSummary` contracts and the prototype `guests[]` type gain `dob?: string` (ISO date) and `legacyGuestId?: number`. The guest form shows DOB (optional, date picker); nothing else in the live guest flow changes.

## 3. The `lgc` schema (migration 4600)

All tables carry `property_id INT NOT NULL REFERENCES inv.property`. Money is integer **paise**; `*_utc` are `DATETIME2(0)` converted from IST (`Asia/Kolkata`, +05:30, no DST); `*_date` are the IST business dates. No row in `lgc` is ever updated by a command other than the legacy importer; there are no `row_ver` columns.

```sql
CREATE SCHEMA lgc;
CREATE TABLE lgc.import_run(
  run_id INT IDENTITY PRIMARY KEY, property_id INT NOT NULL,
  source_folder NVARCHAR(400) NOT NULL, extractor_version VARCHAR(20) NOT NULL,
  dry_run BIT NOT NULL, parts VARCHAR(60) NOT NULL,               -- 'guests,history,cutover'
  started_utc DATETIME2(0) NOT NULL, finished_utc DATETIME2(0) NULL,
  status VARCHAR(10) NOT NULL CHECK (status IN ('Running','Done','Failed','DryRun')),
  run_by INT NOT NULL, result_json NVARCHAR(MAX) NULL CHECK (result_json IS NULL OR ISJSON(result_json)=1));
CREATE TABLE lgc.guest_map(
  property_id INT NOT NULL, legacy_guest_id BIGINT NOT NULL,
  guest_id BIGINT NULL REFERENCES gst.guest,
  customer_key VARCHAR(80) NOT NULL,                              -- §1
  phone_key VARCHAR(10) NULL, phone_valid BIT NOT NULL,
  name_snapshot NVARCHAR(200) NOT NULL, name_fold NVARCHAR(200) NOT NULL,
  deleted BIT NOT NULL, source_fy CHAR(7) NOT NULL,               -- '2026-27'
  CONSTRAINT PK_lgc_guest_map PRIMARY KEY (property_id, legacy_guest_id));
CREATE INDEX IX_lgc_guest_map_key ON lgc.guest_map(property_id, customer_key);
CREATE TABLE lgc.stay(
  stay_id BIGINT IDENTITY PRIMARY KEY, property_id INT NOT NULL,
  legacy_fy CHAR(7) NOT NULL, legacy_booking_id BIGINT NOT NULL,
  legacy_guest_id BIGINT NOT NULL, customer_key VARCHAR(80) NOT NULL,
  guest_name_snapshot NVARCHAR(200) NOT NULL,
  arrival_date DATE NOT NULL, arrival_utc DATETIME2(0) NOT NULL,
  departure_date DATE NULL, departure_utc DATETIME2(0) NULL,     -- NULL = no TransactionEnd row in that file
  expected_departure_date DATE NULL,
  nights INT NOT NULL,                                            -- max(1, departure_date - arrival_date); expected when open
  adults INT NOT NULL, children INT NOT NULL,
  room_no VARCHAR(20) NOT NULL,                                   -- first room (RoomMaster.RoomName)
  room_count INT NOT NULL,                                        -- distinct rooms on the booking
  meal_plan VARCHAR(4) NULL,                                      -- EP/CP/MAP/AP from FacilityID 4/5/6/7
  grc_no INT NULL, purpose NVARCHAR(100) NULL, source_tag VARCHAR(30) NULL,  -- 'OTA:AGODA' …
  status VARCHAR(16) NOT NULL CHECK (status IN ('CheckedOut','OpenAtBackup','InHouseAtCutover','Duplicate')),
  paid_flag CHAR(1) NULL CHECK (paid_flag IN ('Y','N')),
  legacy_bill_no INT NULL,
  notes NVARCHAR(1000) NULL,
  CONSTRAINT UQ_lgc_stay UNIQUE (property_id, legacy_fy, legacy_booking_id));
CREATE INDEX IX_lgc_stay_customer ON lgc.stay(property_id, customer_key, arrival_date DESC);
CREATE INDEX IX_lgc_stay_guest ON lgc.stay(property_id, legacy_guest_id);
CREATE TABLE lgc.room_segment(
  stay_id BIGINT NOT NULL REFERENCES lgc.stay, seq SMALLINT NOT NULL,
  room_no VARCHAR(20) NOT NULL, room_type_code VARCHAR(10) NULL,  -- SUITE/PREMIUM/DELUXE/TWIN/CONF from PackageMaster
  from_date DATE NULL, to_date DATE NULL,                         -- NULL for shifts (dates not stored in the legacy data)
  adults INT NULL, children INT NULL,
  rate_paise BIGINT NULL, extra_paise BIGINT NULL,
  kind VARCHAR(8) NOT NULL CHECK (kind IN ('booked','shift')),
  CONSTRAINT PK_lgc_room_segment PRIMARY KEY (stay_id, seq));
CREATE TABLE lgc.bill(
  bill_id BIGINT IDENTITY PRIMARY KEY, property_id INT NOT NULL,
  legacy_fy CHAR(7) NOT NULL, legacy_bill_no INT NOT NULL, legacy_transaction_id BIGINT NOT NULL,
  stay_id BIGINT NULL REFERENCES lgc.stay,
  legacy_guest_id BIGINT NULL, customer_key VARCHAR(80) NULL,
  bill_date DATE NOT NULL, bill_utc DATETIME2(0) NOT NULL,
  room_no VARCHAR(20) NOT NULL, nights INT NULL,
  room_charge_paise BIGINT NOT NULL, extra_paise BIGINT NOT NULL,
  fb_paise BIGINT NOT NULL, laundry_paise BIGINT NOT NULL, other_paise BIGINT NOT NULL,
  discount_paise BIGINT NOT NULL,
  room_gst_bps INT NOT NULL, room_gst_paise BIGINT NOT NULL,      -- LuxuryTaxPer × 100, LuxuryTax
  fb_gst_paise BIGINT NOT NULL, laundry_gst_paise BIGINT NOT NULL, other_gst_paise BIGINT NOT NULL,
  advance_paise BIGINT NOT NULL, net_paise BIGINT NOT NULL, balance_paise BIGINT NOT NULL, refund_paise BIGINT NOT NULL,
  data_flag CHAR(1) NOT NULL CHECK (data_flag IN ('C','P','?')), paid_flag CHAR(1) NOT NULL CHECK (paid_flag IN ('Y','N','?')),
  gstin_snapshot VARCHAR(15) NULL, b2b BIT NOT NULL,              -- from GSTR1 (BillNo within the FY)
  out_of_fy BIT NOT NULL,                                         -- §7
  notes NVARCHAR(500) NULL,
  CONSTRAINT UQ_lgc_bill UNIQUE (property_id, legacy_fy, legacy_bill_no));
CREATE INDEX IX_lgc_bill_customer ON lgc.bill(property_id, customer_key, bill_date DESC);
CREATE TABLE lgc.receipt(
  receipt_id BIGINT IDENTITY PRIMARY KEY, property_id INT NOT NULL,
  legacy_fy CHAR(7) NOT NULL, legacy_source VARCHAR(8) NOT NULL CHECK (legacy_source IN ('deposit','refund')),
  legacy_row_id BIGINT NOT NULL,                                  -- DepositID or RefundID
  stay_id BIGINT NULL REFERENCES lgc.stay, bill_id BIGINT NULL REFERENCES lgc.bill,
  legacy_guest_id BIGINT NULL, customer_key VARCHAR(80) NULL,
  received_date DATE NOT NULL, received_utc DATETIME2(0) NOT NULL,
  kind VARCHAR(8) NOT NULL CHECK (kind IN ('advance','payment','refund','security')),
  method VARCHAR(6) NOT NULL CHECK (method IN ('CASH','UPI','CARD','BANK','OTHER')),
  amount_paise BIGINT NOT NULL,                                   -- positive; refunds are kind='refund'
  reference NVARCHAR(100) NULL, bank_name NVARCHAR(100) NULL,
  notes NVARCHAR(300) NULL,
  CONSTRAINT UQ_lgc_receipt UNIQUE (property_id, legacy_fy, legacy_source, legacy_row_id));
CREATE INDEX IX_lgc_receipt_customer ON lgc.receipt(property_id, customer_key, received_date DESC);
```

View for Customers 360 (same migration):

```sql
CREATE VIEW lgc.v_customer_stay AS
SELECT s.property_id, s.customer_key, s.stay_id, s.legacy_fy, s.legacy_booking_id, s.legacy_bill_no,
       s.guest_name_snapshot, s.arrival_date, s.departure_date, s.nights, s.room_no, s.room_count, s.status,
       b.net_paise, b.room_gst_paise
FROM lgc.stay s LEFT JOIN lgc.bill b ON b.stay_id = s.stay_id
WHERE s.status <> 'Duplicate';
```

Permission (migration 4601): `PERM-LGC-001` "Legacy data import" granted to **Owner** only (seed as `4221_acc_opening_rbac_seed.sql` does). Reading legacy history uses the existing Customers 360 permissions (contact and money masking as today, `GuestContactMask`).

## 4. Extractor outputs (lane lg-extract → lanes lg-import, lg-tests)

`tools/legacy/extract.py --mdb-dir <dir> --out <run-folder> [--rate-card <csv>] [--latest-only] [--fy 2025-26 …]` writes, with UTF-8 and `\n`:

**Masters CSVs in the template layouts** of `08 Production/deployment/import-templates/templates/` (header row identical to the template; no extra columns; the masters importer refuses unknown columns and any ID-number column):

| File | Rows | Columns and rules |
|---|---|---|
| `guests.csv` | one per `gst.guest` of §1 | `name,phone,email,address,nationality,company,gstin,notes` — `phone` = the 10 digits; `email` empty; `address` = `Address1`, city (`Citymaster`), state (`Statemaster`), PIN joined by `, ` with placeholders (`-`, `0`, `NA`) dropped; `nationality` `Indian` unless `Nationality` is a real other value; `company` = `CompanyN` when real or `Name` when `CustCategoryCode = 3` (M/S.); `gstin` when it matches `^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$`; `notes` = `Legacy GuestID <id>` + `; Remark: …` + `; Vehicle: …` + `; Anniversary: yyyy-mm-dd` (each only when real), ≤ 1000 chars |
| `companies.csv` | one per GSTIN with ≥ 3 guests | `name,gstin,contact,phone,email,city,creditLimit,negotiatedRates` — name = the most frequent `CompanyN` for that GSTIN else the first guest's name; `creditLimit` 0; `negotiatedRates` empty. Also `companies-review.md` listing each GSTIN with its guest count and names for the owner |
| `room-types.csv` | SUITE, PREMIUM, DELUXE, TWIN, CONF | `code,name,maxOccupancy,rackRate,sortOrder` — rackRate from `--rate-card` (columns `code,single,double,extraPerson`) else **empty** and a warning in `report.md` (DEC-163: legacy `PackageMaster` rates are not loaded) |
| `rooms.csv` | the 36 rooms that have at least one booking in any FY | `roomNo,roomType` — `RoomMaster.RoomName` and the type from `PackageMaster` (`RoomType` 1 SUITE, 2 PREMIUM, 3 DELUXE, 5 TWIN, 4 CONF) |
| `tariffs.csv` | one per room type | `roomType,effectiveFrom,rate` — only when `--rate-card` is given; `effectiveFrom` = `--cutover-date` (default today) |
| `occupancy-charges.csv` | one per room type | `extraAdult` from the rate card's `extraPerson`; only with `--rate-card` |
| `id-types.csv` | AADHAAR, DL, VOTER, PAN, PASSPORT, OFFICE-ID, OTHER | `code,name,accepted,needsPhoto,foreignerOnly,sortOrder,active` (PASSPORT `foreignerOnly=no` — Indian passports occur) |
| `booking-sources.csv` | one per OTA name found in `GroupMaster` (§1) | `code,name,segment,channel,commissionBps,sortOrder,active` — `OTA-AGODA,Agoda,OTA,Agoda,0,…` |

**History files** (`lgc_*.csv`, one row per target table row; column order = the table's column order in §3 without identity / `property_id`; `stay_ref` = `legacy_fy|legacy_booking_id`, `bill_ref` = `legacy_fy|legacy_bill_no`; money in **paise**, dates `yyyy-mm-dd`, UTC `yyyy-mm-ddTHH:MM:SSZ`):

`lgc_guest_map.csv` (+ four helper columns at the end for the importer's matching: `guest_name,guest_phone,is_primary,dob`; `guest_id` empty — ruling LG-R3), `lgc_stay.csv`, `lgc_room_segment.csv` (`stay_ref,seq,…`), `lgc_bill.csv`, `lgc_receipt.csv` (security deposits are separate rows with a **negative** `legacy_row_id`), `lgc_tags.csv` (`customer_key,tag`). `totals.json` carries per FY `beforeDedup` and `afterDedup`; the importer compares against `afterDedup`; `receiptsCount` counts advance + payment rows only; the discount figure compared is `discountAmtOnlyPaise` (= §6 Σ DiscountAmt) while `lgc.bill.discount_paise` = DiscountAmt + BillDiscount (ruling LG-R4).

**Cut-over files** (from the latest file only): `cutover_inhouse.csv` (open bookings: `legacy_booking_id,guest_name,phone_key,room_no,arrival_date,expected_departure_date,adults,children,meal_plan,advance_paise,notes` — re-entered by hand on cut-over night), `cutover_advances.csv` (deposits with `TransactionID = 0` not adjusted into a bill: `legacy_deposit_id,guest_name,phone_key,received_date,amount_paise,method,reference`), `cutover_receivables.csv` (bills with `balance_paise > 0` or `data_flag = 'P'` and `paid_flag = 'N'`: `legacy_bill_no,bill_date,guest_name,phone_key,company,gstin,net_paise,balance_paise`), `cutover_customer_opening.csv` (`CustomerOpening` rows: `legacy_guest_id,guest_name,phone_key,opening_paise,as_of_date`), `cutover_paidout.csv` (every `PaidOut` row with the expense name, for the CA — DEC-161; CSV because this PC's Python has no Excel library) and `cutover_number_series.md` (last `BillNo` per FY, the GRC / REG / RCP series start at 1 — DEC-163).

**Secret folder** `secret\guest_ids.jsonl`: one JSON object per legacy guest with a real ID number **or a photo / ID-proof image or a DOB** (ruling LG-R2, 1 Nov 2026: an image is never dropped because the number is missing; `idNumber` is then `null`): `{"legacyGuestId":123,"idType":"AADHAAR","idNumber":"<normalised upper-case, no spaces>","dob":"1985-03-12"|null,"photoFile":"1234.jpg"|null,"idProofFile":"1234.jpg"|null}`; `secret\images\photo\` and `secret\images\idproof\` hold the copied jpgs. `IDCode` → type: 8 AADHAAR, 2 DL, 3 VOTER, 5 and 6 PAN, 1 PASSPORT, 7 OFFICE-ID, 4 and 9–25 OTHER, 0 none. An `IDNumber` that is a placeholder (`-`, `0`, `00`, `NA`, `NIL`, fewer than 4 characters) is no ID.

**`totals.json`**: per FY the figures of §6 as computed from the source, plus `dedup` (§7) counts, and the extractor version; **`report.md`**: the same as a table, the warnings, the companies list, the rooms list.

## 5. The legacy importer (lane lg-import)

Server command, Owner only (`PERM-LGC-001`), setup-state **not** required (history may be loaded after go-live, re-run while no business day is closed; after the first day close `history` is refused):

```
POST /commands/legacy.import
{ "folder": "E:\\Projects\\HCC\\Existing Software\\LG-EXTRACT\\20261105-2130", "parts": ["guests","history","cutover"], "dryRun": true, "expectedTotals": "<totals.json text>" }
→ LegacyImportResult {
    runId, dryRun, parts, extractorVersion,
    guests: { csvRows, created, matchedExisting, skippedInvalidPhone, merged, secretsSealed, dobSet, photosCopied, idProofsCopied, tagsAdded, errors[] },
    history: { perFy: [ { fy, stays, segments, bills, receipts, duplicatesDropped, outOfFy, reconciliation: { billsCount, netPaise, roomGstPaise, fbPaise, receiptsCount, receiptsPaise, refundsCount, refundsPaise, match: true|false, differences[] } } ], errors[] },
    cutover: { customerOpeningRows, receivablesRows, advancesRows, inhouseRows, note },
    warnings[], committed: bool }
```

Rules:
1. `guests` runs **after** `guests.csv` was committed through the masters importer (`imp.batch` master `guests`): it finds each `gst.guest` by `phone` + `full_name` from `lgc_guest_map.csv`, writes `legacy_guest_id`, `dob`, then **seals the ID number** through the existing `GuestIdService` / `GuestIdProtector` (DEC-068 / 070: `gst.guest_id_secret` + `id_last4` + `id_type`), copies the photo and ID-proof images through the documents module's own capture path (`doc.id_capture` + `doc.id_capture_image` sealed, kind `legacy`), adds `crm.customer_tag` rows from `lgc_tags.csv`. Any CSV in the folder that carries a column named like an ID number (`idNumber`, `aadhaar`, `pan`, `passport`, `IDNumber`) is refused before anything is written.
2. `history` loads `lgc.guest_map`, `lgc.stay`, `lgc.room_segment`, `lgc.bill`, `lgc.receipt` **per `legacy_fy` inside one transaction per FY**: delete that FY's rows, insert, then compute the §6 reconciliation from what was inserted and compare with `expectedTotals`; a mismatch rolls that FY back and is reported, the other FYs continue. Resolves `guest_id` through `lgc.guest_map`; `customer_key` is taken from the CSV as is (the extractor computed it by §1).
3. `cutover` writes `imp.opening_set` + `imp.opening_balance` rows for the receivables and the customer openings as **party opening balances** (ACC-1, DEC-148; party = the company account when the bill carries a GSTIN of a company, else the Udhaar / walk-in receivable control), and the advances as advance-deposit openings; the Owner approves them in the ACC-1 opening screen. Nothing else is written; the in-house list is only echoed in the result for the hand re-entry.
4. `dryRun` performs every check and the reconciliation inside a transaction that is rolled back; the result is identical in shape.
5. The importer never reads an MDB and never needs ACE; it reads only the run folder. `secret\guest_ids.jsonl` is overwritten with zeros and deleted after a committed `guests` part (an audit row `legacy.secret_shredded` records it).
6. Audit: one `audit` row per run with the result JSON; `lgc.import_run` mirrors it.

## 6. Reconciliation totals per FY (computed 28 Oct 2026 from the compacted files; the importer must reproduce these before de-dup, and `totals.json` must carry both before- and after-de-dup figures)

Counts are rows; money in rupees as stored (`Single` columns; the extractor's paise totals are these × 100 after per-row rounding, so the comparison is on the paise figures the extractor writes, with a tolerance of ±1 paise per row).

| FY | CustomerInfo | RoomBooking | TransactionEnd | BillNo range | C / P / PaidFlag N | Σ RoomCharg | Σ ExtraCharge | Σ FB | Σ Laundry | Σ Others | Σ DiscountAmt | Σ LuxuryTax (room GST) | Σ FoodGSTRs | Σ Advance (on bills) | Σ NetBill | CustomerDeposit | Σ DepositAmt | Σ Advance (deposits) | Σ SecurityDeposit | CustomerRefund | Σ RefundAmt | LuxuryTaxPer slabs (bills) |
|---|---:|---:|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 2017-18 | 1,790 | 2,021 | 2,019 | 1–2019 | 1682 / 337 / 449 | 45,99,669 | 2,48,597 | 2,68,768 | 600 | 10,300 | 1,94,383 | 2,90,195 | 95 | 12,91,195 | 52,14,023 | 2,655 | 47,75,412 | 0 | 1,592 | 160 | 24,830 | 0 %: 1144 · 12 %: 851 · 18 %: 12 · 28 %: 12 |
| 2018-19 | 4,287 | 2,866 | 2,859 | 1–2859 | 2665 / 194 / 179 | 67,90,282 | 1,83,200 | 5,48,365 | 600 | 1,800 | 2,57,605 | 6,61,511 | 0 | 15,97,282 | 79,12,133 | 3,722 | 79,84,539 | 7,125 | 1,237 | 132 | 32,130 | 0 %: 758 · 1 %: 1 · 12 %: 2060 · 18 %: 33 · 28 %: 7 |
| 2019-20 | 6,358 | 2,665 | 2,665 | 1–2665 | 2587 / 78 / 69 | 66,66,866 | 1,84,332 | 5,57,858 | 615 | 1,000 | 4,89,415 | 6,34,918 | 34 | 15,01,181 | 75,29,986 | 3,563 | 75,36,309 | 0 | 0 | 121 | 27,258 | 0 %: 699 · 10 %: 1 · 11 %: 1 · 12 %: 1897 · 18 %: 66 · 28 %: 1 |
| 2020-21 | 7,192 | 1,206 | 1,205 | 1–1205 | 1011 / 194 / 186 | 30,11,441 | 1,34,470 | 2,28,423 | 0 | 0 | 1,70,131 | 2,34,094 | 11,312 | 8,19,864 | 34,34,428 | 1,766 | 39,97,062 | 0 | 2,000 | 60 | 9,609 | 0 %: 542 · 12 %: 661 · 18 %: 2 |
| 2021-22 | 9,108 | 2,573 | 2,570 | 1–2570 | 2099 / 471 / 361 | 68,90,013 | 1,77,645 | 4,65,257 | 0 | 0 | 2,08,084 | 6,59,329 | 23,263 | 16,66,711 | 79,85,928 | 3,558 | 78,53,611 | 1,000 | 0 | 114 | 21,364 | 0 %: 774 · 12 %: 1795 · 18 %: 1 |
| 2022-23 | 11,296 | 3,247 | 3,242 | 1–3242 | 2986 / 256 / 231 | 97,14,535 | 1,56,235 | 2,88,904 | 300 | 0 | 2,38,210 | 11,20,487 | 14,445 | 17,85,073 | 1,10,26,099 | 4,280 | 1,10,16,915 | 1,000 | 2,901 | 89 | 18,292 | 0 %: 227 · 12 %: 3010 · 18 %: 5 |
| 2023-24 | 13,330 | 3,086 | 3,083 | 1–3083 | 2793 / 290 / 200 | 68,93,856 | 1,06,415 | 24,466 | 200 | 318 | 1,44,994 | 8,23,091 | 1,211 | 26,44,521 | 76,72,076 | 3,734 | 75,42,010 | 3,000 | 1,146 | 36 | 9,784 | 12 %: 3078 · 18 %: 5 |
| 2024-25 | 15,231 | 2,894 | 2,884 | 1–2884 | 2713 / 171 / 88 | 71,78,087 | 63,426 | 18,330 | 0 | 77 | 1,79,615 | 8,49,697 | 913 | 27,39,071 | 79,09,807 | 3,341 | 75,51,343 | 5,800 | 0 | 16 | 9,159 | 12 %: 2879 · 18 %: 5 |
| 2025-26 | 17,033 | 3,228 | 3,223 | 1–3223 | 2908 / 315 / 228 | 83,31,042 | 59,200 | 8,437 | 4,685 | 20 | 2,44,684 | 6,62,978 | 422 | 29,57,021 | 88,05,603 | 3,422 | 79,53,681 | 5,800 | 0 | 9 | 1,045 | 5 %: 1726 · 12 %: 1496 · 18 %: 1 |
| 2026-27 (live copy of 30 Oct 2026) | 18,022 | 1,932 | 1,908 | 1–1908 | 1723 / 185 / 157 | 48,67,903 | 39,200 | 2,846 | 0 | 0 | 69,730 | 2,40,507 | 142 | 20,65,469 | 50,70,985 | 2,029 | 47,97,345 | 5,800 | 0 | 1 | 95 | 5 %: 1908 |

The 2026-27 row supersedes the 22 Jun 2026 backup figures (17,512 guests, 836 bookings, 831 bills) quoted in the mapping document and the profile README; `GuestID` runs 0 … 18,021 in the live copy, bookings `TDate` 10 Mar – 30 Oct 2026, 349 room shifts. Everything else in this document that says "17,512" means "the latest file's `CustomerInfo`"; the extractor always reports the count it found.

Date ranges that matter: bookings (`TDate`) and receipts start a few days **before** 1 April in every file (advance bookings and advances for the new year keyed in the old file) and the 2025-26 file holds bookings as far as Aug 2026 (`FromDate`) and receipts from Jan 2025; the 2019-20 file has four junk bills dated after Jul 2020 (net ₹49) and the 2022-23 file was used until 3 Apr 2023. `CustomerDeposit.Advance` (deposits with `PaymentType = 'Advance'`, `TransactionID = 0`) totals ₹0 … ₹7,125 a year — the hotel collects at check-out. `LuxuryTaxPer` 1 / 10 / 11 are single mis-keyed bills: keep the stored per cent as `room_gst_bps`.

## 7. De-dup across the yearly files (measured 28 Oct 2026)

Keys: bill = (`DepartureDate` date, `RoomNo`, round(`NetBill`)); booking = (`FromDate` date, `RoomNo`, `GuestID`); receipt = (`Date` date, round(`DepositAmt`), `CustomerCode`). Found: bills duplicated only 22-23 → 23-24 (22 bills, ₹60,053, departures 1–3 Apr 2023); bookings 1 / 6 / 0 / 0 / 3 / 13 / 3 / 6 / 1 per boundary (advance bookings keyed in the old file); receipts 0 / 0 / 0 / 0 / 0 / 6 / 1 / 8 / 1.

Rules (the extractor applies them and lists every dropped row in `report.md`; the importer re-checks `status = 'Duplicate'` counts against `totals.json`):
1. A **bill** belongs to the FY of its `DepartureDate` (1 Apr – 31 Mar). A bill in file N whose departure falls in file N+1's FY **and** whose key exists in file N+1 is dropped (`status` not loaded; counted in `dedup.billsDropped`). A bill whose departure falls outside its file's FY with no twin is kept with `out_of_fy = 1`.
2. A **booking** present in two adjacent files by key is kept in the file that holds its `TransactionEnd` row (the check-out); if both or neither, in the later file. The dropped copy is loaded as `lgc.stay` with `status = 'Duplicate'` (so counts reconcile) and is hidden by `lgc.v_customer_stay`.
3. A **receipt** present in two adjacent files by key is kept in the file whose FY contains its `Date`; the other copy is dropped.
4. `CustomerInfo` is read from the latest file only (§1); earlier files are not used for guests.

## 8. Conversions

- Rupee `Single` / `Double` → paise: `round_half_away_from_zero(x * 100)` computed on `Decimal(str(x))`, never on the float.
- `LuxuryTaxPer` → `room_gst_bps = round(per × 100)`; GST before 1 Jul 2017 does not occur (the first file starts 2 Jul 2017).
- Legacy dates are IST wall-clock `Date`/`DateTime`; `TTime` is text `hh:mm AM/PM` (blank → 12:00). `*_utc = ist - 5:30`.
- `legacy_fy` = `YYYY-YY` from the file name (`Lodge25-26.mdb` → `2025-26`), not from the dates.
- `RoomNo` text in `RoomBooking` / `TransactionEnd` is the room **name**; `RoomBookingDetail.RoomNo` is the `RoomMaster` id → name through `RoomMaster.RoomName`. Names are trimmed, upper-cased.
- `FacilityID` 4 EP, 5 CP, 6 MAP, 7 AP; anything else NULL.
- `CashType` `Cash` → CASH; `Online` with `ChequeNo = 'QR'` → UPI; `Online` otherwise → BANK; `Credit Card` / `Card` → CARD; `Cheque` → BANK (`reference` = cheque no + bank); anything else OTHER.
- Receipt kinds: `PaymentType = 'Advance'` → `advance` (amount = `Advance`); `'Bill Payment'` → `payment` (amount = `DepositAmt`); `SecurityDeposit > 0` → a second row `security`; `CustomerRefund` → `refund` (amount = `RefundAmt` + `SecurityDepositRefund`).

## 9. Customers 360 read shape (lanes lg-schema → lg-ui)

Server (`Hcc.Contracts/Customers/CustomerContracts.cs`, lane lg-schema adds, others copy):

```csharp
public sealed record LegacyStayView(long StayId, string LegacyFy, long LegacyBookingId, int? LegacyBillNo, string Arrival, string? Departure, int Nights, string Room, int RoomCount, string Status, long? NetPaise);
// CustomerSummary gains: int LegacyStays, string? FirstStay   (FirstStay = min(legacy arrival, live arrival))
// CustomerProfile gains: IReadOnlyList<LegacyStayView> LegacyStays, long? LegacyLifetimePaise (null when money is masked)
// CustomerStayView is unchanged; Stays (live) and LegacyStays are separate lists; Nights and LifetimePaise on the summary INCLUDE legacy
```

`ICustomerReadStore` gains `Task<IReadOnlyList<CrmLegacyStay>> LegacyStaysAsync(int propertyId, string customerKey, CancellationToken ct)` and `Task<IReadOnlyDictionary<string, (int Stays, int Nights, long NetPaise, DateOnly First, DateOnly Last)>> LegacyRollupAsync(int propertyId, IEnumerable<string> keys, CancellationToken ct)` reading `lgc.v_customer_stay`.

Prototype (`03 Prototype`): type `LegacyStay = {fy:string;bookingId:number;billNo?:number;arrival:string;departure?:string;nights:number;room:string;roomCount:number;status:string;net?:number}` in `src/domain/streams/customers.ts`; `Customer` gains `legacyStays:LegacyStay[]`, and `nights`, `lifetime`, `firstStay` include them. **No new persisted State slice and no SCHEMA bump:** the mock service answers `GET /customers/profile` from `src/fixtures/ext/legacy-sample.ts` (six fictional legacy stays for two fixture guests) through the existing customers handler; in production the client reads the server's `LegacyStays`. The profile shows a "Legacy" badge on those rows, the old bill number, and never a link into a folio (there is none).

## 10. Permissions

| Code | Meaning | Default |
|---|---|---|
| `PERM-LGC-001` | Run the legacy importer (`legacy.import`), list runs (`GET /legacy/runs`) | Owner |

## 11. Migrations and test projects owned

| Lane | Migrations | Server test project | Prototype tests |
|---|---|---|---|
| lg-schema | 4600 `lgc` schema + `gst.guest` columns + view; 4601 `PERM-LGC-001` seed; 4602–4604 reserved | `tests/Hcc.Legacy.Tests/Sql/` (schema smoke, view) + the `GuestView` dob test in `Hcc.Stays.Tests` | — |
| lg-import | 4605–4609 only if the importer needs a table the contract does not list (say so in the DONE file) | `tests/Hcc.Legacy.Tests/Import/` | — |
| lg-extract | — | — | `tests/legacy-extract.mjs` runs `python -I tools/legacy/extract.py --self-test` (fictional fixture) |
| lg-ui | — | — | `tests/stream-customers-legacy.mjs` |
| lg-tests | — | `tests/Hcc.Legacy.Tests/Golden/` (fixture run folder → importer → reconciliation) | `tests/stream-legacy-golden.mjs` |
| lg-audit | — | `tests/Hcc.Legacy.Tests/Audit/` (independent reconciliation + `lgc` invariants) + `tools/legacy/audit/` | — |

`Hcc.Legacy.Tests` is created by **lg-schema** (project file, `Hcc.slnx` line, Sql folder); the other lanes add folders only.
