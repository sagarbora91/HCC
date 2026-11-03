-- HCC migration 4281 · ACC-4 acc4-year · the carry-forward (ACC4-CONTRACT §1.1; rulebook G3 step 5). Generated from the books, never typed:
-- balance-sheet ledgers (party sub-ledgers kept) → the next FY's opening; P&L ledgers and 3000 closed to 3100 (close lines = the year-close
-- voucher dated 1 April of to_fy, source kind opening, sub year-close). draft → approved (Owner at the lock) → superseded (relock).
CREATE TABLE acc.carry_forward(
  carry_forward_id VARCHAR(48) NOT NULL CONSTRAINT PK_acc_carry_forward PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_carry_forward_property REFERENCES inv.property,
  from_fy CHAR(7) NOT NULL,
  to_fy CHAR(7) NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_acc_carry_forward_status CHECK (status IN ('draft','approved','superseded')),
  profit_to_capital BIGINT NOT NULL,
  drawings_to_capital BIGINT NOT NULL,
  generated_by INT NOT NULL,
  generated_utc DATETIME2(3) NOT NULL,
  approved_by INT NULL,
  approved_utc DATETIME2(3) NULL,
  superseded_by VARCHAR(48) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_acc_carry_forward_approval CHECK (status = 'draft' OR (approved_by IS NOT NULL AND approved_utc IS NOT NULL)));
CREATE UNIQUE INDEX UX_acc_carry_forward_approved ON acc.carry_forward(property_id, from_fy) WHERE status = 'approved';
CREATE TABLE acc.carry_forward_line(
  carry_forward_id VARCHAR(48) NOT NULL CONSTRAINT FK_acc_carry_forward_line_cf REFERENCES acc.carry_forward,
  line_no SMALLINT NOT NULL,
  kind CHAR(5) NOT NULL CONSTRAINT CK_acc_carry_forward_line_kind CHECK (kind IN ('carry','close')),
  ledger VARCHAR(4) NOT NULL,
  party VARCHAR(36) NULL,
  debit BIGINT NOT NULL CONSTRAINT CK_acc_carry_forward_line_dr CHECK (debit >= 0),
  credit BIGINT NOT NULL CONSTRAINT CK_acc_carry_forward_line_cr CHECK (credit >= 0),
  CONSTRAINT PK_acc_carry_forward_line PRIMARY KEY (carry_forward_id, kind, line_no),
  CONSTRAINT CK_acc_carry_forward_line_side CHECK (debit = 0 OR credit = 0));
