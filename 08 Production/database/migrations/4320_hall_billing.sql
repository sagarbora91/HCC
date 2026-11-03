-- HCC migration 4320 · F6 hall booking billing (F6-CONTRACT §5, §6; lane H1): advance plan rows (per quotation version and per event), event
-- receipts (advance / balance / refund — advances are never income and carry no GST, DEC-142), the event-day bill → tax invoice of the INV series
-- (HPE/2627/…, DEC-197; ivc.invoice source_kind 'hall'), the cancellation (slab, kept → 4300 with GST, refund awaiting approval) and the
-- accounting sources ( hall:advance / hall:invoice / hall:advance-apply / hall:balance / hall:cancel / hall:refund).
-- Ledger 4400 Banquet & events revenue is already in the built-in chart (ACC-1); hall revenue posts to it as ledger:4400.
-- The source-kind CHECK widenings ('hall' on ivc.invoice and acc.source) are in 4740: 4687 (LF) re-creates both constraints, so on a fresh
-- database a widening numbered below 4687 would be undone.
CREATE TABLE hall.quotation_advance(
  version_id BIGINT NOT NULL CONSTRAINT FK_hall_quotation_advance_version REFERENCES hall.quotation_version,
  seq INT NOT NULL,
  label NVARCHAR(40) NOT NULL,
  pct_bps INT NULL CONSTRAINT CK_hall_quotation_advance_pct CHECK (pct_bps IS NULL OR pct_bps BETWEEN 0 AND 10000),
  amount_paise BIGINT NULL,
  due VARCHAR(10) NOT NULL,               -- ON_ACCEPT | EVENT_DAY | yyyy-MM-dd
  CONSTRAINT PK_hall_quotation_advance PRIMARY KEY (version_id, seq));
CREATE TABLE hall.event_advance(
  event_id BIGINT NOT NULL CONSTRAINT FK_hall_event_advance_event REFERENCES hall.event,
  seq INT NOT NULL,
  label NVARCHAR(40) NOT NULL,
  pct_bps INT NULL,
  amount_paise BIGINT NOT NULL,
  due VARCHAR(10) NOT NULL,
  paid_paise BIGINT NOT NULL CONSTRAINT DF_hall_event_advance_paid DEFAULT 0,
  CONSTRAINT PK_hall_event_advance PRIMARY KEY (event_id, seq),
  CONSTRAINT CK_hall_event_advance_paid CHECK (paid_paise >= 0));
CREATE TABLE hall.event_receipt(
  receipt_id BIGINT IDENTITY CONSTRAINT PK_hall_event_receipt PRIMARY KEY,
  event_id BIGINT NOT NULL CONSTRAINT FK_hall_event_receipt_event REFERENCES hall.event,
  business_date DATE NOT NULL,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_hall_event_receipt_kind CHECK (kind IN ('ADVANCE','BALANCE','REFUND')),
  tender VARCHAR(16) NULL CONSTRAINT CK_hall_event_receipt_tender CHECK (tender IS NULL OR tender IN ('Cash','UPI','Card','Bank transfer')),
  company_id BIGINT NULL,
  udhaar BIT NOT NULL CONSTRAINT DF_hall_event_receipt_udhaar DEFAULT 0,
  reference NVARCHAR(60) NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_hall_event_receipt_amount CHECK (amount_paise > 0),
  by_user INT NOT NULL,
  at_utc DATETIME2(3) NOT NULL,
  idem_key VARCHAR(80) NULL,
  CONSTRAINT CK_hall_event_receipt_side CHECK ((kind = 'BALANCE' AND (tender IS NOT NULL OR company_id IS NOT NULL OR udhaar = 1)) OR (kind <> 'BALANCE' AND tender IS NOT NULL)));
CREATE INDEX IX_hall_event_receipt_event ON hall.event_receipt(event_id, business_date);
ALTER TABLE hall.event ADD
  invoice_id BIGINT NULL,
  invoice_no VARCHAR(24) NULL,
  invoice_date DATE NULL,
  billed_pax INT NULL,
  taxable_paise BIGINT NULL, cgst_paise BIGINT NULL, sgst_paise BIGINT NULL, total_paise BIGINT NULL,
  advance_applied_paise BIGINT NULL, balance_paise BIGINT NULL,
  settle_mode VARCHAR(8) NULL CONSTRAINT CK_hall_event_settle CHECK (settle_mode IS NULL OR settle_mode IN ('TENDER','COMPANY','UDHAAR','DUE')),
  cancel_date DATE NULL, cancel_days INT NULL, cancel_kept_bps INT NULL, cancel_tax_bps INT NULL,
  cancel_kept_paise BIGINT NULL, cancel_refund_paise BIGINT NULL, cancel_by INT NULL, cancel_reason NVARCHAR(200) NULL,
  refund_state VARCHAR(8) NULL CONSTRAINT CK_hall_event_refund CHECK (refund_state IS NULL OR refund_state IN ('NONE','PENDING','PAID')),
  refund_by INT NULL;
GO
ALTER TABLE hall.event ADD CONSTRAINT CK_hall_event_billed CHECK (status <> 'BILLED' OR (invoice_no IS NOT NULL AND total_paise IS NOT NULL));
ALTER TABLE hall.event ADD CONSTRAINT CK_hall_event_cancelled CHECK (status <> 'CANCELLED' OR (cancel_date IS NOT NULL AND refund_state IS NOT NULL));
ALTER TABLE hall.event ADD CONSTRAINT CK_hall_event_refund_sod CHECK (refund_by IS NULL OR refund_by <> cancel_by);
GO
