-- HCC migration 2300 · W3-5 Customers / CRM (block 2300-2399). Customer 360 (prototype src/domain/streams/customers.ts).
-- A customer is NOT a table: it is derived on read from gst.guest profiles + gst.registration + inv.reservation/gst.stay + bill.* (read side only).
-- Identity = last 10 digits of the mobile number, else the normalised name ('n:' prefix). This module only stores what the read model cannot derive:
--   * merges of duplicate customers (maker-checker) as an ALIAS mapping source key -> target key (no other module's row is touched or deleted, history stays),
--   * tags, notes and a preferences override per customer key.
-- No guest ID number (full or last 4) is read or stored here (DEC-068). Original HCC design; no donor code.
CREATE SCHEMA crm;
GO
CREATE TABLE crm.customer_merge(
  merge_id BIGINT IDENTITY CONSTRAINT PK_crm_merge PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_crm_merge_property REFERENCES inv.property,
  source_key VARCHAR(80) NOT NULL,                 -- the duplicate that disappears from the list
  target_key VARCHAR(80) NOT NULL,                 -- the customer that survives
  source_name NVARCHAR(200) NOT NULL,
  target_name NVARCHAR(200) NOT NULL,
  reason NVARCHAR(300) NOT NULL,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_crm_merge_status CHECK (status IN ('Pending','Approved','Rejected')),
  proposed_by INT NOT NULL,
  proposed_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decision_note NVARCHAR(300) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_crm_merge_distinct CHECK (source_key <> target_key),
  CONSTRAINT CK_crm_merge_maker_checker CHECK (decided_by IS NULL OR decided_by <> proposed_by),
  CONSTRAINT CK_crm_merge_decided CHECK ((status = 'Pending' AND decided_by IS NULL AND decided_utc IS NULL) OR (status <> 'Pending' AND decided_by IS NOT NULL AND decided_utc IS NOT NULL)));
-- At most one open proposal per duplicate.
CREATE UNIQUE INDEX UX_crm_merge_pending ON crm.customer_merge(property_id, source_key) WHERE status = 'Pending';
-- Approved merges: alias -> canonical key. The read model resolves keys through this table (transitively, application side).
CREATE TABLE crm.customer_alias(
  property_id INT NOT NULL CONSTRAINT FK_crm_alias_property REFERENCES inv.property,
  alias_key VARCHAR(80) NOT NULL,
  target_key VARCHAR(80) NOT NULL,
  merge_id BIGINT NOT NULL CONSTRAINT FK_crm_alias_merge REFERENCES crm.customer_merge,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_crm_alias PRIMARY KEY (property_id, alias_key),
  CONSTRAINT CK_crm_alias_distinct CHECK (alias_key <> target_key));
CREATE INDEX IX_crm_alias_target ON crm.customer_alias(property_id, target_key);
CREATE TABLE crm.customer_tag(
  property_id INT NOT NULL CONSTRAINT FK_crm_tag_property REFERENCES inv.property,
  customer_key VARCHAR(80) NOT NULL,
  tag NVARCHAR(30) NOT NULL,
  added_by INT NOT NULL,
  added_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_crm_tag PRIMARY KEY (property_id, customer_key, tag));
CREATE TABLE crm.customer_note(
  note_id BIGINT IDENTITY CONSTRAINT PK_crm_note PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_crm_note_property REFERENCES inv.property,
  customer_key VARCHAR(80) NOT NULL,
  body NVARCHAR(500) NOT NULL,
  author_id INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_crm_note_customer ON crm.customer_note(property_id, customer_key);
-- Preferences override; when no row exists the read model shows the guest profiles' preferences.
CREATE TABLE crm.customer_pref(
  property_id INT NOT NULL CONSTRAINT FK_crm_pref_property REFERENCES inv.property,
  customer_key VARCHAR(80) NOT NULL,
  preferences NVARCHAR(500) NOT NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_crm_pref PRIMARY KEY (property_id, customer_key));
