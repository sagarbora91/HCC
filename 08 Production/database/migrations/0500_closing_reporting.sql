-- HCC migration 0500 · P6 closing & reports (Wave 1 block 0500–0599). Contracts package §3 RunBusinessDayClose / GetReport, §4 schema
-- cls (closing runs, steps, snapshots); WF-DAY-001; DEC-042 (business date moves only through close → next day), DEC-065 (cash-book day
-- lock until the Owner reopens with a reason). Original HCC design; no donor code. Event times written by the application (TimeProvider).
-- Reads (never writes) the tables of P2–P4; the only write outside cls is inv.property.business_date (owned by the close, DEC-042).
-- RBAC: PERM-CLS-001…004 and PERM-RPT-001…003 grants are already seeded by 0101 exactly as the matrix + DEC-063 (no grant missing).

-- One close run per property and business date; attempts count every start/resume (a rerun after a crash continues the same run).
CREATE TABLE cls.close_run(
  run_id BIGINT IDENTITY CONSTRAINT PK_close_run PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_close_run_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  run_ref VARCHAR(40) NOT NULL,              -- client runId of the first attempt
  last_run_ref VARCHAR(40) NOT NULL,         -- client runId of the latest attempt
  status VARCHAR(10) NOT NULL CONSTRAINT CK_close_run_status CHECK (status IN ('Running','Blocked','Closed','Completed')),
  current_step VARCHAR(10) NOT NULL CONSTRAINT CK_close_run_step CHECK (current_step IN ('preflight','nights','cash','close','snapshot','next-day','done')),
  attempts INT NOT NULL CONSTRAINT CK_close_run_attempts CHECK (attempts >= 1),
  started_by INT NOT NULL,
  started_utc DATETIME2(3) NOT NULL,
  last_attempt_by INT NOT NULL,
  last_attempt_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_close_run_day UNIQUE (property_id, business_date));
-- Persisted progress per step (latest attempt wins; the audit trail keeps every attempt).
CREATE TABLE cls.close_step(
  run_id BIGINT NOT NULL CONSTRAINT FK_close_step_run REFERENCES cls.close_run,
  step VARCHAR(10) NOT NULL CONSTRAINT CK_close_step_step CHECK (step IN ('preflight','nights','cash','close','snapshot','next-day')),
  status VARCHAR(8) NOT NULL CONSTRAINT CK_close_step_status CHECK (status IN ('Done','Blocked')),
  attempt INT NOT NULL,
  detail NVARCHAR(MAX) NOT NULL CONSTRAINT CK_close_step_detail CHECK (ISJSON(detail) = 1),
  principal_id INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_close_step PRIMARY KEY (run_id, step));
-- Late-write barrier: one row per closed business date, inserted first in the close transaction. Writers read it with HOLDLOCK
-- (key-range lock) inside their own command transaction, so the insert waits for in-flight writers and every later writer sees it.
CREATE TABLE cls.business_day(
  property_id INT NOT NULL CONSTRAINT FK_business_day_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  run_id BIGINT NOT NULL CONSTRAINT FK_business_day_run REFERENCES cls.close_run,
  closed_by INT NOT NULL,
  closed_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_business_day PRIMARY KEY (property_id, business_date));
-- Immutable closing record (prototype ClosingRecord): canonical JSON of the record + its SHA-256, key figures as columns.
CREATE TABLE cls.closing_snapshot(
  snapshot_id BIGINT IDENTITY CONSTRAINT PK_closing_snapshot PRIMARY KEY,
  property_id INT NOT NULL,
  business_date DATE NOT NULL,
  run_id BIGINT NOT NULL CONSTRAINT FK_closing_snapshot_run REFERENCES cls.close_run,
  closed_by INT NOT NULL,
  closed_utc DATETIME2(3) NOT NULL,
  net_revenue_paise BIGINT NOT NULL,
  tax_paise BIGINT NOT NULL,
  net_receipts_paise BIGINT NOT NULL,
  cash_expected_paise BIGINT NOT NULL,
  cash_counted_paise BIGINT NULL,
  occupied_room_nights INT NOT NULL,
  sellable_room_nights INT NOT NULL,
  record_json NVARCHAR(MAX) NOT NULL CONSTRAINT CK_closing_snapshot_json CHECK (ISJSON(record_json) = 1),
  record_sha256 BINARY(32) NOT NULL,
  CONSTRAINT UQ_closing_snapshot_day UNIQUE (property_id, business_date),
  CONSTRAINT FK_closing_snapshot_day FOREIGN KEY (property_id, business_date) REFERENCES cls.business_day(property_id, business_date));
-- Cash-book day lock (DEC-065): only a closed day can be locked; reopen = Owner with a reason. History is append-only.
CREATE TABLE cls.cashbook_day(
  property_id INT NOT NULL,
  business_date DATE NOT NULL,
  locked BIT NOT NULL,
  locked_by INT NULL,
  locked_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_cashbook_day PRIMARY KEY (property_id, business_date),
  CONSTRAINT FK_cashbook_day_day FOREIGN KEY (property_id, business_date) REFERENCES cls.business_day(property_id, business_date),
  CONSTRAINT CK_cashbook_day_lock CHECK ((locked = 1 AND locked_by IS NOT NULL AND locked_utc IS NOT NULL) OR (locked = 0 AND locked_by IS NULL AND locked_utc IS NULL)));
CREATE TABLE cls.cashbook_event(
  event_id BIGINT IDENTITY CONSTRAINT PK_cashbook_event PRIMARY KEY,
  property_id INT NOT NULL,
  business_date DATE NOT NULL,
  action VARCHAR(6) NOT NULL CONSTRAINT CK_cashbook_event_action CHECK (action IN ('lock','reopen')),
  principal_id INT NOT NULL,
  at_utc DATETIME2(3) NOT NULL,
  reason NVARCHAR(200) NULL,
  CONSTRAINT CK_cashbook_event_reason CHECK (action <> 'reopen' OR LEN(reason) > 0),
  CONSTRAINT FK_cashbook_event_day FOREIGN KEY (property_id, business_date) REFERENCES cls.cashbook_day(property_id, business_date));
CREATE INDEX IX_cashbook_event_day ON cls.cashbook_event(property_id, business_date, event_id);
GO
CREATE TRIGGER cls.TR_business_day_append_only ON cls.business_day AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51500, 'cls.business_day is append-only: a closed business date stays closed.', 1;
END;
GO
CREATE TRIGGER cls.TR_closing_snapshot_append_only ON cls.closing_snapshot AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51501, 'cls.closing_snapshot is immutable; corrections post in the open business day.', 1;
END;
GO
CREATE TRIGGER cls.TR_cashbook_event_append_only ON cls.cashbook_event AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51502, 'cls.cashbook_event is append-only.', 1;
END;
