-- HCC migration 1000 · accounting & Tally (W2-3, block 1000–1099). Module spec 20; prototype 03 Prototype/src/domain/streams/accounting.ts.
-- Vouchers are DERIVED: the outbox consumer 'accounting-vouchers' records one source per operational record (folio line,
-- receipt, outlet bill, expense, accepted cash difference, pay run) with classification-keyed entries; the mapping
-- (defaults in code + Owner overrides here) resolves keys to ledgers at read time for unlocked dates. Locking a closed day
-- stores the numbered vouchers as a snapshot; unlocking stamps the lock (history kept, nothing deleted).
-- The chart of accounts is fixed in code (Hcc.Domain.Accounting.ChartOfAccounts; illustrative, CA to confirm).
-- Original HCC design; no donor code. Money BIGINT paise; event times DATETIME2(3) UTC written by the application.
CREATE SCHEMA acc;
GO
CREATE TABLE acc.source(
  source_id BIGINT IDENTITY CONSTRAINT PK_acc_source PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_source_property REFERENCES inv.property,
  source_key VARCHAR(80) NOT NULL,
  source_kind VARCHAR(12) NOT NULL CONSTRAINT CK_acc_source_kind CHECK (source_kind IN ('line','receipt','pos','expense','closing','payroll')),
  sub VARCHAR(30) NOT NULL,
  voucher_type VARCHAR(8) NOT NULL CONSTRAINT CK_acc_source_type CHECK (voucher_type IN ('Sales','Receipt','Payment','Journal')),
  business_date DATE NOT NULL,
  narration NVARCHAR(400) NOT NULL,
  event_type VARCHAR(60) NOT NULL,
  outbox_id BIGINT NULL CONSTRAINT FK_acc_source_outbox REFERENCES plat.outbox,
  received_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_acc_source UNIQUE (property_id, source_key));
CREATE INDEX IX_acc_source_date ON acc.source(property_id, business_date, source_id);
CREATE TABLE acc.source_entry(
  source_id BIGINT NOT NULL CONSTRAINT FK_acc_source_entry_source REFERENCES acc.source,
  line_no TINYINT NOT NULL,
  map_key VARCHAR(80) NOT NULL,
  debit_paise BIGINT NOT NULL CONSTRAINT CK_acc_source_entry_debit CHECK (debit_paise BETWEEN 0 AND 10000000000),
  credit_paise BIGINT NOT NULL CONSTRAINT CK_acc_source_entry_credit CHECK (credit_paise BETWEEN 0 AND 10000000000),
  CONSTRAINT PK_acc_source_entry PRIMARY KEY (source_id, line_no),
  CONSTRAINT CK_acc_source_entry_side CHECK (debit_paise = 0 OR credit_paise = 0));
CREATE INDEX IX_acc_source_entry_key ON acc.source_entry(map_key);
-- Owner overrides of the default mapping (DEC-063 G11) and their history (before/after, actor, time).
CREATE TABLE acc.mapping(
  property_id INT NOT NULL CONSTRAINT FK_acc_mapping_property REFERENCES inv.property,
  map_key VARCHAR(80) NOT NULL,
  account_code VARCHAR(4) NOT NULL,
  changed_by INT NOT NULL,
  changed_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_acc_mapping PRIMARY KEY (property_id, map_key),
  CONSTRAINT CK_acc_mapping_not_suspense CHECK (account_code <> '9999'));
CREATE TABLE acc.mapping_change(
  change_id BIGINT IDENTITY CONSTRAINT PK_acc_mapping_change PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_mapping_change_property REFERENCES inv.property,
  map_key VARCHAR(80) NOT NULL,
  before_code VARCHAR(4) NULL,
  after_code VARCHAR(4) NULL,
  actor INT NOT NULL,
  at_utc DATETIME2(3) NOT NULL);
-- Period locks: one current lock per date (filtered unique index); an unlock stamps the row, a relock inserts a new one.
CREATE TABLE acc.period_lock(
  lock_id BIGINT IDENTITY CONSTRAINT PK_acc_period_lock PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_period_lock_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  voucher_count INT NOT NULL,
  debit_paise BIGINT NOT NULL,
  credit_paise BIGINT NOT NULL,
  locked_by INT NOT NULL,
  locked_utc DATETIME2(3) NOT NULL,
  unlocked_by INT NULL,
  unlocked_utc DATETIME2(3) NULL,
  unlock_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_acc_period_lock_balanced CHECK (debit_paise = credit_paise),
  CONSTRAINT CK_acc_period_lock_unlock CHECK ((unlocked_utc IS NULL AND unlocked_by IS NULL AND unlock_reason IS NULL)
                                            OR (unlocked_utc IS NOT NULL AND unlocked_by IS NOT NULL AND unlock_reason IS NOT NULL)));
CREATE UNIQUE INDEX UX_acc_period_lock_current ON acc.period_lock(property_id, business_date) WHERE unlocked_utc IS NULL;
CREATE TABLE acc.locked_voucher(
  lock_id BIGINT NOT NULL CONSTRAINT FK_acc_locked_voucher_lock REFERENCES acc.period_lock,
  seq INT NOT NULL,
  voucher_no VARCHAR(20) NOT NULL,
  voucher_type VARCHAR(8) NOT NULL,
  narration NVARCHAR(400) NOT NULL,
  source_key VARCHAR(80) NOT NULL,
  source_kind VARCHAR(12) NOT NULL,
  sub VARCHAR(30) NOT NULL,
  CONSTRAINT PK_acc_locked_voucher PRIMARY KEY (lock_id, seq));
CREATE TABLE acc.locked_entry(
  lock_id BIGINT NOT NULL,
  seq INT NOT NULL,
  line_no TINYINT NOT NULL,
  account_code VARCHAR(4) NOT NULL,
  debit_paise BIGINT NOT NULL,
  credit_paise BIGINT NOT NULL,
  CONSTRAINT PK_acc_locked_entry PRIMARY KEY (lock_id, seq, line_no),
  CONSTRAINT FK_acc_locked_entry_voucher FOREIGN KEY (lock_id, seq) REFERENCES acc.locked_voucher);
GO
-- Sources and snapshots are immutable (Financial Rulebook §5): corrections arrive as new sources (reversal lines, void bills).
CREATE TRIGGER acc.TR_source_append_only ON acc.source AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51101, 'acc.source is append-only.', 1;
END
GO
CREATE TRIGGER acc.TR_source_entry_append_only ON acc.source_entry AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51102, 'acc.source_entry is append-only.', 1;
END
GO
CREATE TRIGGER acc.TR_locked_voucher_append_only ON acc.locked_voucher AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51103, 'Locked vouchers are a snapshot and cannot change.', 1;
END
GO
CREATE TRIGGER acc.TR_locked_entry_append_only ON acc.locked_entry AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51104, 'Locked vouchers are a snapshot and cannot change.', 1;
END
