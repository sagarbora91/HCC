-- HCC migration 0400 · billing & cashier (P4, Wave 1 block 0400–0499). Contracts package §3 (PostRoomNight, RecordReceipt,
-- Request/Decide/ExecuteAdjustment, GetFolio), §4 rules 3–8; DEC-031 (refund = approved request + separate execution),
-- DEC-035 (opening float carried from the previous counted close), DEC-041 (approval bound to action + target + amount +
-- version; approver ≠ requester), DEC-052 (shared till: restaurant cash counts once in the front-desk drawer).
-- Extends the 0002 tables (bill.folio, bill.charge, pay.receipt) and adds tills, shifts, cash movements, counts, approvals.
-- Location: database/modules/ until the migration runner accepts block-numbered files in database/migrations/
-- (see "Requested changes" in the P4 report); applied by Hcc.Infrastructure.SqlServer.Billing.BillingMigrationSource.
-- Original HCC design; no donor code. Event times are written by the application from its TimeProvider.

-- Folio version: every financial change touches the folio row so its rowversion moves (approvals bind to it, DEC-041).
ALTER TABLE bill.folio ADD change_seq INT NOT NULL CONSTRAINT DF_folio_change_seq DEFAULT 0;
GO
ALTER TABLE bill.charge ADD
  principal_id INT NULL,
  approval_id BIGINT NULL;
GO
-- Tills (DEC-052 option A: one shared front-desk till per property; outlet cash is a movement in this drawer).
CREATE TABLE pay.till(
  till_id INT IDENTITY CONSTRAINT PK_till PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_till_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(100) NOT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_till UNIQUE (property_id, code));
-- Cashier shifts. One active (Open or Submitted) shift per till; several shifts per business date are allowed (DEC-035 production).
CREATE TABLE pay.shift(
  shift_id BIGINT IDENTITY CONSTRAINT PK_shift PRIMARY KEY,
  till_id INT NOT NULL CONSTRAINT FK_shift_till REFERENCES pay.till,
  business_date DATE NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_shift_status CHECK (status IN ('Open','Submitted','Closed')),
  opening_float_paise BIGINT NOT NULL CONSTRAINT CK_shift_float CHECK (opening_float_paise BETWEEN 0 AND 10000000000),
  previous_shift_id BIGINT NULL CONSTRAINT FK_shift_previous REFERENCES pay.shift,
  opened_by INT NOT NULL,
  opened_utc DATETIME2(3) NOT NULL,
  expected_paise BIGINT NULL,
  counted_paise BIGINT NULL CONSTRAINT CK_shift_counted CHECK (counted_paise BETWEEN 0 AND 10000000000),
  submitted_by INT NULL,
  submitted_utc DATETIME2(3) NULL,
  difference_reason NVARCHAR(200) NULL,
  approval_id BIGINT NULL,
  closed_by INT NULL,
  closed_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_shift_closed CHECK (status <> 'Closed' OR (expected_paise IS NOT NULL AND counted_paise IS NOT NULL AND closed_utc IS NOT NULL)));
CREATE UNIQUE INDEX UX_shift_active_per_till ON pay.shift(till_id) WHERE status IN ('Open','Submitted');
CREATE INDEX IX_shift_till_closed ON pay.shift(till_id, shift_id) WHERE status = 'Closed';
-- Approvals for folio adjustments and cash differences (DEC-041). Named bill.approval so it cannot collide with a generic
-- plat approval table from P1; see the P4 report.
CREATE TABLE bill.approval(
  approval_id BIGINT IDENTITY CONSTRAINT PK_approval PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_approval_property REFERENCES inv.property,
  action VARCHAR(16) NOT NULL CONSTRAINT CK_approval_action CHECK (action IN ('discount','refund','credit','correction','cash-difference')),
  folio_id BIGINT NULL CONSTRAINT FK_approval_folio REFERENCES bill.folio,
  shift_id BIGINT NULL CONSTRAINT FK_approval_shift REFERENCES pay.shift,
  target_kind VARCHAR(8) NOT NULL CONSTRAINT CK_approval_target_kind CHECK (target_kind IN ('folio','receipt','charge','shift')),
  target_id BIGINT NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_approval_amount CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  target_version BINARY(8) NULL,             -- folio rowversion at request time (NULL for cash differences: bound to expected/counted)
  expected_paise BIGINT NULL,                -- cash difference: expected drawer at count
  counted_paise BIGINT NULL,                 -- cash difference: counted cash
  reason NVARCHAR(200) NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_approval_status CHECK (status IN ('Pending','Approved','Rejected','Expired','Executed')),
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  expires_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decision_reason NVARCHAR(200) NULL,
  executed_by INT NULL,
  executed_utc DATETIME2(3) NULL,
  result_id BIGINT NULL,                     -- charge or receipt created on execution
  row_ver ROWVERSION,
  CONSTRAINT CK_approval_scope CHECK ((action = 'cash-difference' AND shift_id IS NOT NULL AND folio_id IS NULL)
                                   OR (action <> 'cash-difference' AND folio_id IS NOT NULL AND shift_id IS NULL)),
  -- PERM-SOD-001: nobody decides their own request. DEC-031: the requester of a refund cannot execute it.
  CONSTRAINT CK_approval_sod_decide CHECK (decided_by IS NULL OR decided_by <> requested_by),
  CONSTRAINT CK_approval_sod_refund CHECK (action <> 'refund' OR executed_by IS NULL OR executed_by <> requested_by));
CREATE INDEX IX_approval_folio ON bill.approval(folio_id, action, status) WHERE folio_id IS NOT NULL;
CREATE INDEX IX_approval_target ON bill.approval(target_kind, target_id, status);
ALTER TABLE pay.shift ADD CONSTRAINT FK_shift_approval FOREIGN KEY (approval_id) REFERENCES bill.approval;
ALTER TABLE bill.charge ADD CONSTRAINT FK_charge_approval FOREIGN KEY (approval_id) REFERENCES bill.approval;
GO
-- Receipt context: till/shift for cash (application rule: cash needs an open shift on that till), reference, actor, time.
ALTER TABLE pay.receipt ADD
  till_id INT NULL CONSTRAINT FK_receipt_till REFERENCES pay.till,
  shift_id BIGINT NULL CONSTRAINT FK_receipt_shift REFERENCES pay.shift,
  reference NVARCHAR(120) NOT NULL CONSTRAINT DF_receipt_reference DEFAULT N'',
  principal_id INT NULL,
  recorded_utc DATETIME2(3) NULL,
  approval_id BIGINT NULL CONSTRAINT FK_receipt_approval REFERENCES bill.approval;
GO
CREATE INDEX IX_receipt_folio ON pay.receipt(folio_id) INCLUDE (kind, method, amount_paise, refund_of);
-- Cash movements: one signed row per cash event in a shift (+ into the drawer, − out). Expected cash = opening float + Σ.
-- Folio cash receipts, restaurant / room-service cash (DEC-052) and drawer-funded expenses all land here exactly once.
CREATE TABLE pay.cash_movement(
  movement_id BIGINT IDENTITY CONSTRAINT PK_cash_movement PRIMARY KEY,
  shift_id BIGINT NOT NULL CONSTRAINT FK_cash_movement_shift REFERENCES pay.shift,
  till_id INT NOT NULL CONSTRAINT FK_cash_movement_till REFERENCES pay.till,
  business_date DATE NOT NULL,
  kind VARCHAR(16) NOT NULL CONSTRAINT CK_cash_movement_kind CHECK (kind IN ('payment','deposit','refund','deposit-return','company-credit',
    'pos-sale','pos-void','expense','expense-reversal','transfer-in','transfer-out','banking')),
  source_type VARCHAR(16) NOT NULL,
  source_id BIGINT NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_cash_movement_amount CHECK (amount_paise <> 0 AND amount_paise BETWEEN -10000000000 AND 10000000000),
  label NVARCHAR(200) NOT NULL,
  reference NVARCHAR(120) NOT NULL,
  principal_id INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_cash_movement_source UNIQUE (source_type, source_id));
CREATE INDEX IX_cash_movement_shift ON pay.cash_movement(shift_id) INCLUDE (kind, amount_paise);
-- Every count submitted for a shift (a rejected difference reopens the shift for a recount; history is kept).
CREATE TABLE pay.shift_count(
  count_id BIGINT IDENTITY CONSTRAINT PK_shift_count PRIMARY KEY,
  shift_id BIGINT NOT NULL CONSTRAINT FK_shift_count_shift REFERENCES pay.shift,
  expected_paise BIGINT NOT NULL,
  counted_paise BIGINT NOT NULL CONSTRAINT CK_shift_count_counted CHECK (counted_paise BETWEEN 0 AND 10000000000),
  denominations NVARCHAR(400) NULL CONSTRAINT CK_shift_count_denominations CHECK (denominations IS NULL OR ISJSON(denominations) = 1),
  reason NVARCHAR(200) NULL,
  approval_id BIGINT NULL CONSTRAINT FK_shift_count_approval REFERENCES bill.approval,
  submitted_by INT NOT NULL,
  submitted_utc DATETIME2(3) NOT NULL);
GO
-- Posted financial records are immutable (Financial Rulebook §5): corrections are reversal lines, refunds are child receipts.
CREATE TRIGGER bill.TR_charge_append_only ON bill.charge AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51001, 'bill.charge is append-only; post a reversal line instead.', 1;
END;
GO
CREATE TRIGGER pay.TR_receipt_append_only ON pay.receipt AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51002, 'pay.receipt is append-only; record a linked refund or return instead.', 1;
END;
GO
CREATE TRIGGER pay.TR_cash_movement_append_only ON pay.cash_movement AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51003, 'pay.cash_movement is append-only.', 1;
END;
