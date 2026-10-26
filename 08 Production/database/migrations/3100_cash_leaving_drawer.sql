-- HCC migration 3100 · cash leaving the drawer and the shared-till handover (Wave B1, block 3100-3109). DEC-073 (bank deposit with slip
-- number + slip photo, confirmed by a DIFFERENT Manager or Owner; till transfer accepted by the receiver on their own open shift; Owner
-- drawing, Owner only; none on a locked cash-book day), DEC-078 (each person opens their own shift with a handover count); audit A03b-01,
-- A03b-06. Every hand-off is a signed pay.cash_movement on the shift it leaves or enters, so expected cash, the count, the day close and the
-- cash book see it. Expected cash drops when a deposit is RECORDED (the cash has left the drawer); a rejected deposit puts it back with a
-- 'banking' movement of the opposite sign (source banking-return). Maker-checker reuses bill.approval (actions cash-banking, till-transfer).
-- The slip photo (JPEG / PNG under 100 KB, compressed on the device) is kept 600 days and then deleted by the nightly retention purge; the
-- banking record stays with slip_purged_utc. Original HCC design; no donor code.

-- Owner drawing: its own movement kind (cash book line "Owner drawing"; inside the transfers part of expected cash).
ALTER TABLE pay.cash_movement DROP CONSTRAINT CK_cash_movement_kind;
GO
ALTER TABLE pay.cash_movement ADD CONSTRAINT CK_cash_movement_kind CHECK (kind IN ('payment','deposit','refund','deposit-return','company-credit',
  'pos-sale','pos-void','expense','expense-reversal','transfer-in','transfer-out','banking','owner-drawing'));
GO
-- Approval requests for the two hand-offs that need a second person. Like a cash difference they belong to a shift, not a folio.
ALTER TABLE bill.approval DROP CONSTRAINT CK_approval_action;
ALTER TABLE bill.approval DROP CONSTRAINT CK_approval_target_kind;
ALTER TABLE bill.approval DROP CONSTRAINT CK_approval_scope;
GO
ALTER TABLE bill.approval ADD CONSTRAINT CK_approval_action
  CHECK (action IN ('discount','refund','credit','correction','cash-difference','cash-banking','till-transfer'));
ALTER TABLE bill.approval ADD CONSTRAINT CK_approval_target_kind CHECK (target_kind IN ('folio','receipt','charge','shift','banking','transfer'));
ALTER TABLE bill.approval ADD CONSTRAINT CK_approval_scope
  CHECK ((action IN ('cash-difference','cash-banking','till-transfer') AND shift_id IS NOT NULL AND folio_id IS NULL)
      OR (action NOT IN ('cash-difference','cash-banking','till-transfer') AND folio_id IS NOT NULL AND shift_id IS NULL));
GO
-- DEC-078 handover: the opener's own count of the drawer they take over (difference = handover − opening float, shown and audited).
ALTER TABLE pay.shift ADD
  handover_counted_paise BIGINT NULL CONSTRAINT CK_shift_handover CHECK (handover_counted_paise BETWEEN 0 AND 10000000000),
  handover_reason NVARCHAR(200) NULL;
GO
-- One row per bank deposit, till transfer or Owner drawing. from_shift = the shift the cash left; to_shift = the receiving shift of an
-- accepted transfer. decided_date = business date of the decision (a rejection's return and an acceptance are dated it).
CREATE TABLE pay.cash_transfer(
  transfer_id BIGINT IDENTITY CONSTRAINT PK_cash_transfer PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cash_transfer_property REFERENCES inv.property,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_cash_transfer_kind CHECK (kind IN ('banking','till','drawing')),
  status VARCHAR(10) NOT NULL CONSTRAINT CK_cash_transfer_status CHECK (status IN ('Pending','Confirmed','Accepted','Rejected','Posted')),
  business_date DATE NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_cash_transfer_amount CHECK (amount_paise > 0 AND amount_paise <= 10000000000),
  from_till_id INT NOT NULL CONSTRAINT FK_cash_transfer_from_till REFERENCES pay.till,
  from_shift_id BIGINT NOT NULL CONSTRAINT FK_cash_transfer_from_shift REFERENCES pay.shift,
  to_till_id INT NULL CONSTRAINT FK_cash_transfer_to_till REFERENCES pay.till,
  to_shift_id BIGINT NULL CONSTRAINT FK_cash_transfer_to_shift REFERENCES pay.shift,
  bank NVARCHAR(100) NULL,
  slip_no NVARCHAR(40) NULL,
  reason NVARCHAR(200) NULL,
  approval_id BIGINT NULL CONSTRAINT FK_cash_transfer_approval REFERENCES bill.approval,
  recorded_by INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decided_date DATE NULL,
  decision_reason NVARCHAR(200) NULL,
  slip_purged_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_cash_transfer_shape CHECK (
       (kind = 'banking' AND bank IS NOT NULL AND slip_no IS NOT NULL AND to_till_id IS NULL AND status IN ('Pending','Confirmed','Rejected'))
    OR (kind = 'till' AND to_till_id IS NOT NULL AND to_till_id <> from_till_id AND bank IS NULL AND slip_no IS NULL AND status IN ('Pending','Accepted','Rejected'))
    OR (kind = 'drawing' AND reason IS NOT NULL AND to_till_id IS NULL AND bank IS NULL AND slip_no IS NULL AND status = 'Posted')),
  -- Maker-checker: the recorder never decides their own deposit or transfer.
  CONSTRAINT CK_cash_transfer_sod CHECK (decided_by IS NULL OR decided_by <> recorded_by));
CREATE INDEX IX_cash_transfer_date ON pay.cash_transfer(property_id, business_date, transfer_id);
CREATE INDEX IX_cash_transfer_pending ON pay.cash_transfer(property_id, status) WHERE status = 'Pending';
CREATE INDEX IX_cash_transfer_slip_retention ON pay.cash_transfer(recorded_utc) INCLUDE (transfer_id) WHERE kind = 'banking' AND slip_purged_utc IS NULL;
GO
-- The deposit-slip photo, in its own table so listings never read it. Deleted by the retention purge 600 days after recording.
CREATE TABLE pay.banking_slip(
  transfer_id BIGINT NOT NULL CONSTRAINT PK_banking_slip PRIMARY KEY CONSTRAINT FK_banking_slip_transfer REFERENCES pay.cash_transfer,
  content_type VARCHAR(10) NOT NULL CONSTRAINT CK_banking_slip_type CHECK (content_type IN ('image/jpeg','image/png')),
  size_bytes INT NOT NULL CONSTRAINT CK_banking_slip_size CHECK (size_bytes BETWEEN 1 AND 102399),
  image VARBINARY(MAX) NOT NULL,
  CONSTRAINT CK_banking_slip_bytes CHECK (DATALENGTH(image) = size_bytes));
