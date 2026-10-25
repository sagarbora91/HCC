-- HCC migration 0002 · core inventory / folio / receipt tables (contracts package §4 rules 1, 3, 4, 5, 10).
-- Baseline = the verified A3 spike subset (07 Integration Lab/sql-spike/01-schema.sql), original HCC design. P2 (inventory),
-- P4 (billing/payments) extend these tables with their own later migrations (0003+); this file is never edited once applied.
CREATE TABLE inv.property(
  property_id INT IDENTITY CONSTRAINT PK_property PRIMARY KEY,
  code VARCHAR(20) NOT NULL CONSTRAINT UQ_property_code UNIQUE,
  name NVARCHAR(200) NOT NULL,
  time_zone VARCHAR(64) NOT NULL,          -- IANA id, validated by the application (CalendarDate.AssertTimeZone, lab H04)
  business_date DATE NOT NULL,             -- advanced only by an authorized day close (DEC-042)
  row_ver ROWVERSION);
CREATE TABLE inv.room_type(
  room_type_id INT IDENTITY CONSTRAINT PK_room_type PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_room_type_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  max_occupancy TINYINT NOT NULL CONSTRAINT CK_room_type_occupancy CHECK (max_occupancy BETWEEN 1 AND 10),
  CONSTRAINT UQ_room_type UNIQUE (property_id, code));
CREATE TABLE inv.room(
  room_id INT IDENTITY CONSTRAINT PK_room PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_room_property REFERENCES inv.property,
  room_no VARCHAR(10) NOT NULL,
  room_type_id INT NOT NULL CONSTRAINT FK_room_room_type REFERENCES inv.room_type,
  hk_status VARCHAR(12) NOT NULL CONSTRAINT DF_room_hk DEFAULT 'Ready'
    CONSTRAINT CK_room_hk CHECK (hk_status IN ('Ready','Dirty','Cleaning','Inspection')),
  row_ver ROWVERSION,
  CONSTRAINT UQ_room UNIQUE (property_id, room_no));
CREATE TABLE inv.reservation(
  reservation_id BIGINT IDENTITY CONSTRAINT PK_reservation PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_reservation_property REFERENCES inv.property,
  public_no VARCHAR(20) NOT NULL,
  guest_name NVARCHAR(200) NOT NULL,
  arrival DATE NOT NULL,
  departure DATE NOT NULL,
  status VARCHAR(12) NOT NULL CONSTRAINT CK_res_status CHECK (status IN ('Held','Expired','Confirmed','InHouse','Departed','Cancelled','NoShow')),
  hold_until_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_reservation_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_res_dates CHECK (departure > arrival),
  CONSTRAINT UQ_res_no UNIQUE (property_id, public_no));
-- Exclusive inventory: one row per room per night; the primary key makes an overlapping stay impossible in any interleaving.
CREATE TABLE inv.room_night(
  room_id INT NOT NULL CONSTRAINT FK_room_night_room REFERENCES inv.room,
  night DATE NOT NULL,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_room_night_reservation REFERENCES inv.reservation,
  CONSTRAINT PK_room_night PRIMARY KEY (room_id, night));
CREATE INDEX IX_room_night_res ON inv.room_night(reservation_id);
CREATE TABLE bill.folio(
  folio_id BIGINT IDENTITY CONSTRAINT PK_folio PRIMARY KEY,
  reservation_id BIGINT NOT NULL CONSTRAINT UQ_folio_reservation UNIQUE CONSTRAINT FK_folio_reservation REFERENCES inv.reservation,
  row_ver ROWVERSION);
CREATE TABLE bill.charge(
  charge_id BIGINT IDENTITY CONSTRAINT PK_charge PRIMARY KEY,
  folio_id BIGINT NOT NULL CONSTRAINT FK_charge_folio REFERENCES bill.folio,
  kind VARCHAR(12) NOT NULL CONSTRAINT CK_charge_kind CHECK (kind IN ('room','service','discount','correction')),
  service_date DATE NOT NULL,
  label NVARCHAR(200) NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_charge_amount CHECK (amount_paise BETWEEN -10000000000 AND 10000000000),
  tax_bps INT NOT NULL CONSTRAINT CK_charge_tax_bps CHECK (tax_bps BETWEEN 0 AND 10000),   -- snapshot at posting
  tax_paise BIGINT NOT NULL,
  reverses_charge_id BIGINT NULL CONSTRAINT FK_charge_reverses REFERENCES bill.charge,
  posted_utc DATETIME2(3) NOT NULL CONSTRAINT DF_charge_posted DEFAULT SYSUTCDATETIME());
-- One canonical room-night charge per folio and service date (repeat-safe night posting, spike T07).
CREATE UNIQUE INDEX UX_charge_room_night ON bill.charge(folio_id, service_date) WHERE kind = 'room' AND reverses_charge_id IS NULL;
CREATE TABLE pay.receipt(
  receipt_id BIGINT IDENTITY CONSTRAINT PK_receipt PRIMARY KEY,
  folio_id BIGINT NOT NULL CONSTRAINT FK_receipt_folio REFERENCES bill.folio,
  kind VARCHAR(16) NOT NULL CONSTRAINT CK_receipt_kind CHECK (kind IN ('payment','deposit','deposit-return','refund','company-credit')),
  method VARCHAR(16) NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_receipt_amount CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  refund_of BIGINT NULL CONSTRAINT FK_receipt_refund_of REFERENCES pay.receipt,
  business_date DATE NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_refund_link CHECK ((kind = 'refund' AND refund_of IS NOT NULL) OR (kind <> 'refund' AND refund_of IS NULL)));
CREATE INDEX IX_receipt_refund_of ON pay.receipt(refund_of) WHERE refund_of IS NOT NULL;
CREATE INDEX IX_receipt_business_date ON pay.receipt(business_date) INCLUDE (kind, method, amount_paise);
