-- HCC migration 2850 · MA-5 front-desk lists (block 2850–2859). Plan: 02 Planning/HCC_Customisation_Features_Plan_v1.md Part 2 §8
-- M9 booking sources and market segments, M11 guest ID types, M12 document numbering series, M14 booking policies; prototype rules:
-- 03 Prototype/src/domain/streams/frontdesk-master.ts. Masters framework rules as MA-0 (2801): code ^[A-Z0-9-]{1,20}$ unique per
-- property (retired rows included) and immutable once used, name 1–60, retire instead of deleting a used row, history in
-- mst.master_history (master = bookingSources | idTypes | numberSeries | policies).
-- Defaults: a property without rows reads the built-in defaults (application code, the prototype's fixture values); the first
-- command on a list stores them (as the prototype stores its seed on the first change). Nothing is seeded here.
-- Numbering (DEC-MA-5: ONE tax-invoice series for the hotel): the invoices module (ivc.series, 2400) still allocates gap-free per
-- (property, FY, document) and freezes the prefix per FY; bill.number_series decides the prefix, first number and digits of the
-- next FY series and is read by IssueInvoice / the credit-note issue. A series that has issued documents never changes; a new one
-- starts from a date with no documents (GST: consecutive, unique per FY, ≤ 16 characters — illustrative, CA to confirm).
-- Money: commission and policy percentages in basis points (Owner-only changes, PERM-MST-002, DEC-058).

CREATE TABLE mst.booking_source(
  row_id INT IDENTITY CONSTRAINT PK_mst_booking_source PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_booking_source_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  segment VARCHAR(12) NOT NULL CONSTRAINT CK_mst_booking_source_segment CHECK (segment IN ('DIRECT','OTA','CORPORATE','AGENT','GROUP','OTHER')),
  channel NVARCHAR(60) NULL,
  commission_bps INT NOT NULL CONSTRAINT DF_mst_booking_source_commission DEFAULT 0 CONSTRAINT CK_mst_booking_source_commission CHECK (commission_bps BETWEEN 0 AND 5000),
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_booking_source_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_mst_booking_source_code UNIQUE (property_id, code),
  CONSTRAINT CK_mst_booking_source_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_mst_booking_source_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_mst_booking_source_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_mst_booking_source_channel CHECK (channel IS NULL OR segment IN ('OTA','CORPORATE','AGENT')),
  CONSTRAINT CK_mst_booking_source_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_mst_booking_source_order ON mst.booking_source(property_id, sort_order);

CREATE TABLE mst.id_type(
  row_id INT IDENTITY CONSTRAINT PK_mst_id_type PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_id_type_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  accepted BIT NOT NULL CONSTRAINT DF_mst_id_type_accepted DEFAULT 1,
  needs_photo BIT NOT NULL CONSTRAINT DF_mst_id_type_photo DEFAULT 0,
  foreigner_only BIT NOT NULL CONSTRAINT DF_mst_id_type_foreigner DEFAULT 0,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_id_type_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_mst_id_type_code UNIQUE (property_id, code),
  CONSTRAINT CK_mst_id_type_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_mst_id_type_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_mst_id_type_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_mst_id_type_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_mst_id_type_order ON mst.id_type(property_id, sort_order);

-- document: INV tax invoice · BOS bill of supply · CRN credit note (GST documents: yearly reset, prefix ≤ 6 so PREFIX/2627/digits fits
-- 16 characters) · RCP receipt · KOT kitchen order ticket · REG registration card. reset_rule FY = restart on 1 April, NEVER = one run.
CREATE TABLE bill.number_series(
  row_id INT IDENTITY CONSTRAINT PK_bill_number_series PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_bill_number_series_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  document CHAR(3) NOT NULL CONSTRAINT CK_bill_number_series_document CHECK (document IN ('INV','BOS','CRN','RCP','KOT','REG')),
  prefix VARCHAR(8) NOT NULL CONSTRAINT CK_bill_number_series_prefix CHECK (LEN(prefix) BETWEEN 1 AND 8 AND prefix NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  start_no INT NOT NULL CONSTRAINT CK_bill_number_series_start CHECK (start_no BETWEEN 1 AND 9999999),
  padding INT NOT NULL CONSTRAINT CK_bill_number_series_padding CHECK (padding BETWEEN 1 AND 10),
  reset_rule VARCHAR(5) NOT NULL CONSTRAINT CK_bill_number_series_reset CHECK (reset_rule IN ('FY','NEVER')),
  active_from DATE NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_bill_number_series_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_bill_number_series_code UNIQUE (property_id, code),
  CONSTRAINT CK_bill_number_series_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_bill_number_series_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_bill_number_series_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_bill_number_series_gst CHECK (document NOT IN ('INV','BOS','CRN') OR (reset_rule = 'FY' AND LEN(prefix) <= 6 AND LEN(prefix) + 6 + padding <= 16)),
  CONSTRAINT CK_bill_number_series_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
-- One active series per document and start date (DEC-MA-5: one tax-invoice series at a time).
CREATE UNIQUE INDEX UX_bill_number_series_start ON bill.number_series(property_id, document, active_from) WHERE active = 1;

CREATE TABLE inv.policy(
  row_id INT IDENTITY CONSTRAINT PK_inv_policy PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_inv_policy_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  free_cancel_hours INT NOT NULL CONSTRAINT CK_inv_policy_hours CHECK (free_cancel_hours BETWEEN 0 AND 720),
  cancel_charge_bps INT NOT NULL CONSTRAINT CK_inv_policy_cancel CHECK (cancel_charge_bps BETWEEN 0 AND 10000),
  no_show_charge_bps INT NOT NULL CONSTRAINT CK_inv_policy_noshow CHECK (no_show_charge_bps BETWEEN 0 AND 10000),
  deposit_bps INT NOT NULL CONSTRAINT CK_inv_policy_deposit CHECK (deposit_bps BETWEEN 0 AND 10000),
  -- Booking-source codes this policy applies to, as a JSON array; [] = the default policy for every other source.
  sources NVARCHAR(1000) NOT NULL CONSTRAINT DF_inv_policy_sources DEFAULT N'[]' CONSTRAINT CK_inv_policy_sources CHECK (ISJSON(sources) = 1),
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_inv_policy_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_inv_policy_code UNIQUE (property_id, code),
  CONSTRAINT CK_inv_policy_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_inv_policy_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_inv_policy_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_inv_policy_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_inv_policy_order ON inv.policy(property_id, sort_order);
