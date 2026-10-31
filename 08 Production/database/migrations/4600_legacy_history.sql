-- HCC migration 4600 · W1a LG legacy "Lodge" history (02 Planning/agents/LG-HISTORY-CONTRACT.md §2, §3; mapping v1 §8, DEC-161…164).
-- gst.guest gains the legacy GuestID that produced the profile and the date of birth (DEC-162 Q4). The lgc schema holds the old
-- program's stays, room segments, bills and receipts per legacy financial year, loaded only by the legacy importer (PERM-LGC-001):
-- no row here is ever updated by any other command and there are no row_ver columns. Money = BIGINT paise; *_utc = DATETIME2(0)
-- converted from IST (+05:30); *_date = IST business dates. lgc.v_customer_stay feeds Customers 360 and hides de-duplicated copies.
-- Original HCC design; no donor code.
ALTER TABLE gst.guest ADD legacy_guest_id BIGINT NULL;
ALTER TABLE gst.guest ADD dob DATE NULL;
GO
CREATE UNIQUE INDEX UX_guest_legacy ON gst.guest(property_id, legacy_guest_id) WHERE legacy_guest_id IS NOT NULL;
GO
CREATE SCHEMA lgc;
GO
CREATE TABLE lgc.import_run(
  run_id INT IDENTITY CONSTRAINT PK_lgc_import_run PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_lgc_import_run_property REFERENCES inv.property,
  source_folder NVARCHAR(400) NOT NULL,
  extractor_version VARCHAR(20) NOT NULL,
  dry_run BIT NOT NULL,
  parts VARCHAR(60) NOT NULL,                                     -- 'guests,history,cutover'
  started_utc DATETIME2(0) NOT NULL,
  finished_utc DATETIME2(0) NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_lgc_import_run_status CHECK (status IN ('Running','Done','Failed','DryRun')),
  run_by INT NOT NULL,
  result_json NVARCHAR(MAX) NULL CONSTRAINT CK_lgc_import_run_json CHECK (result_json IS NULL OR ISJSON(result_json) = 1));
CREATE TABLE lgc.guest_map(
  property_id INT NOT NULL CONSTRAINT FK_lgc_guest_map_property REFERENCES inv.property,
  legacy_guest_id BIGINT NOT NULL,
  guest_id BIGINT NULL CONSTRAINT FK_lgc_guest_map_guest REFERENCES gst.guest,
  customer_key VARCHAR(80) NOT NULL,                              -- contract §1
  phone_key VARCHAR(10) NULL,
  phone_valid BIT NOT NULL,
  name_snapshot NVARCHAR(200) NOT NULL,
  name_fold NVARCHAR(200) NOT NULL,
  deleted BIT NOT NULL,
  source_fy CHAR(7) NOT NULL,                                     -- '2026-27'
  CONSTRAINT PK_lgc_guest_map PRIMARY KEY (property_id, legacy_guest_id));
CREATE INDEX IX_lgc_guest_map_key ON lgc.guest_map(property_id, customer_key);
CREATE TABLE lgc.stay(
  stay_id BIGINT IDENTITY CONSTRAINT PK_lgc_stay PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_lgc_stay_property REFERENCES inv.property,
  legacy_fy CHAR(7) NOT NULL,
  legacy_booking_id BIGINT NOT NULL,
  legacy_guest_id BIGINT NOT NULL,
  customer_key VARCHAR(80) NOT NULL,
  guest_name_snapshot NVARCHAR(200) NOT NULL,
  arrival_date DATE NOT NULL,
  arrival_utc DATETIME2(0) NOT NULL,
  departure_date DATE NULL,                                       -- NULL = no TransactionEnd row in that file
  departure_utc DATETIME2(0) NULL,
  expected_departure_date DATE NULL,
  nights INT NOT NULL,                                            -- max(1, departure_date - arrival_date); expected when open
  adults INT NOT NULL,
  children INT NOT NULL,
  room_no VARCHAR(20) NOT NULL,                                   -- first room (RoomMaster.RoomName)
  room_count INT NOT NULL,                                        -- distinct rooms on the booking
  meal_plan VARCHAR(4) NULL,                                      -- EP/CP/MAP/AP from FacilityID 4/5/6/7
  grc_no INT NULL,
  purpose NVARCHAR(100) NULL,
  source_tag VARCHAR(30) NULL,                                    -- 'OTA:AGODA' ...
  status VARCHAR(16) NOT NULL CONSTRAINT CK_lgc_stay_status CHECK (status IN ('CheckedOut','OpenAtBackup','InHouseAtCutover','Duplicate')),
  paid_flag CHAR(1) NULL CONSTRAINT CK_lgc_stay_paid CHECK (paid_flag IN ('Y','N')),
  legacy_bill_no INT NULL,
  notes NVARCHAR(1000) NULL,
  CONSTRAINT UQ_lgc_stay UNIQUE (property_id, legacy_fy, legacy_booking_id));
CREATE INDEX IX_lgc_stay_customer ON lgc.stay(property_id, customer_key, arrival_date DESC);
CREATE INDEX IX_lgc_stay_guest ON lgc.stay(property_id, legacy_guest_id);
CREATE TABLE lgc.room_segment(
  stay_id BIGINT NOT NULL CONSTRAINT FK_lgc_room_segment_stay REFERENCES lgc.stay,
  seq SMALLINT NOT NULL,
  room_no VARCHAR(20) NOT NULL,
  room_type_code VARCHAR(10) NULL,                                -- SUITE/PREMIUM/DELUXE/TWIN/CONF from PackageMaster
  from_date DATE NULL,                                            -- NULL for shifts (dates not stored in the legacy data)
  to_date DATE NULL,
  adults INT NULL,
  children INT NULL,
  rate_paise BIGINT NULL,
  extra_paise BIGINT NULL,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_lgc_room_segment_kind CHECK (kind IN ('booked','shift')),
  CONSTRAINT PK_lgc_room_segment PRIMARY KEY (stay_id, seq));
CREATE TABLE lgc.bill(
  bill_id BIGINT IDENTITY CONSTRAINT PK_lgc_bill PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_lgc_bill_property REFERENCES inv.property,
  legacy_fy CHAR(7) NOT NULL,
  legacy_bill_no INT NOT NULL,
  legacy_transaction_id BIGINT NOT NULL,
  stay_id BIGINT NULL CONSTRAINT FK_lgc_bill_stay REFERENCES lgc.stay,
  legacy_guest_id BIGINT NULL,
  customer_key VARCHAR(80) NULL,
  bill_date DATE NOT NULL,
  bill_utc DATETIME2(0) NOT NULL,
  room_no VARCHAR(20) NOT NULL,
  nights INT NULL,
  room_charge_paise BIGINT NOT NULL,
  extra_paise BIGINT NOT NULL,
  fb_paise BIGINT NOT NULL,
  laundry_paise BIGINT NOT NULL,
  other_paise BIGINT NOT NULL,
  discount_paise BIGINT NOT NULL,
  room_gst_bps INT NOT NULL,                                      -- LuxuryTaxPer x 100
  room_gst_paise BIGINT NOT NULL,                                 -- LuxuryTax
  fb_gst_paise BIGINT NOT NULL,
  laundry_gst_paise BIGINT NOT NULL,
  other_gst_paise BIGINT NOT NULL,
  advance_paise BIGINT NOT NULL,
  net_paise BIGINT NOT NULL,
  balance_paise BIGINT NOT NULL,
  refund_paise BIGINT NOT NULL,
  data_flag CHAR(1) NOT NULL CONSTRAINT CK_lgc_bill_data_flag CHECK (data_flag IN ('C','P','?')),
  paid_flag CHAR(1) NOT NULL CONSTRAINT CK_lgc_bill_paid_flag CHECK (paid_flag IN ('Y','N','?')),
  gstin_snapshot VARCHAR(15) NULL,                                -- from GSTR1 (BillNo within the FY)
  b2b BIT NOT NULL,
  out_of_fy BIT NOT NULL,                                         -- contract §7
  notes NVARCHAR(500) NULL,
  CONSTRAINT UQ_lgc_bill UNIQUE (property_id, legacy_fy, legacy_bill_no));
CREATE INDEX IX_lgc_bill_customer ON lgc.bill(property_id, customer_key, bill_date DESC);
CREATE INDEX IX_lgc_bill_stay ON lgc.bill(stay_id) WHERE stay_id IS NOT NULL;
CREATE TABLE lgc.receipt(
  receipt_id BIGINT IDENTITY CONSTRAINT PK_lgc_receipt PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_lgc_receipt_property REFERENCES inv.property,
  legacy_fy CHAR(7) NOT NULL,
  legacy_source VARCHAR(8) NOT NULL CONSTRAINT CK_lgc_receipt_source CHECK (legacy_source IN ('deposit','refund')),
  legacy_row_id BIGINT NOT NULL,                                  -- DepositID or RefundID
  stay_id BIGINT NULL CONSTRAINT FK_lgc_receipt_stay REFERENCES lgc.stay,
  bill_id BIGINT NULL CONSTRAINT FK_lgc_receipt_bill REFERENCES lgc.bill,
  legacy_guest_id BIGINT NULL,
  customer_key VARCHAR(80) NULL,
  received_date DATE NOT NULL,
  received_utc DATETIME2(0) NOT NULL,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_lgc_receipt_kind CHECK (kind IN ('advance','payment','refund','security')),
  method VARCHAR(6) NOT NULL CONSTRAINT CK_lgc_receipt_method CHECK (method IN ('CASH','UPI','CARD','BANK','OTHER')),
  amount_paise BIGINT NOT NULL,                                   -- positive; refunds are kind = 'refund'
  reference NVARCHAR(100) NULL,
  bank_name NVARCHAR(100) NULL,
  notes NVARCHAR(300) NULL,
  CONSTRAINT UQ_lgc_receipt UNIQUE (property_id, legacy_fy, legacy_source, legacy_row_id));
CREATE INDEX IX_lgc_receipt_customer ON lgc.receipt(property_id, customer_key, received_date DESC);
GO
CREATE VIEW lgc.v_customer_stay AS
SELECT s.property_id, s.customer_key, s.stay_id, s.legacy_fy, s.legacy_booking_id, s.legacy_bill_no,
       s.guest_name_snapshot, s.arrival_date, s.departure_date, s.nights, s.room_no, s.room_count, s.status,
       b.net_paise, b.room_gst_paise
FROM lgc.stay s LEFT JOIN lgc.bill b ON b.stay_id = s.stay_id
WHERE s.status <> 'Duplicate';
