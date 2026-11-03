-- HCC migration 4280 · ACC-4 acc4-year · financial year status (02 Planning/agents/ACC4-CONTRACT.md §1.1–§1.2; rulebook G3 step 5–6, G4; DEC-077
-- extended to a year). One row per property and FY ('2026-27'); status open → closing → locked → reopened → locked. The lock / reopen / relock audit
-- is kept in acc.fiscal_year_event (append-only). provisional_exports = 1 from a reopen until the relock. Original HCC design; times UTC.
CREATE TABLE acc.fiscal_year(
  property_id INT NOT NULL CONSTRAINT FK_acc_fiscal_year_property REFERENCES inv.property,
  fy CHAR(7) NOT NULL CONSTRAINT CK_acc_fiscal_year_fy CHECK (fy LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]'),
  status VARCHAR(9) NOT NULL CONSTRAINT CK_acc_fiscal_year_status CHECK (status IN ('open','closing','locked','reopened')),
  locked_by INT NULL,
  locked_utc DATETIME2(3) NULL,
  reopened_by INT NULL,
  reopened_utc DATETIME2(3) NULL,
  reopen_reason NVARCHAR(400) NULL,
  provisional_exports BIT NOT NULL CONSTRAINT DF_acc_fiscal_year_provisional DEFAULT (0),
  carry_forward_id VARCHAR(48) NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_acc_fiscal_year PRIMARY KEY (property_id, fy),
  CONSTRAINT CK_acc_fiscal_year_locked CHECK (status <> 'locked' OR (locked_by IS NOT NULL AND locked_utc IS NOT NULL AND carry_forward_id IS NOT NULL)),
  CONSTRAINT CK_acc_fiscal_year_reopen CHECK (status <> 'reopened' OR (reopened_by IS NOT NULL AND LEN(reopen_reason) >= 10)));
CREATE TABLE acc.fiscal_year_event(
  event_id BIGINT IDENTITY CONSTRAINT PK_acc_fiscal_year_event PRIMARY KEY,
  property_id INT NOT NULL,
  fy CHAR(7) NOT NULL,
  action VARCHAR(14) NOT NULL CONSTRAINT CK_acc_fiscal_year_event_action CHECK (action IN ('carry-forward','lock','reopen','relock')),
  actor INT NOT NULL,
  at_utc DATETIME2(3) NOT NULL,
  reason NVARCHAR(400) NULL,
  CONSTRAINT FK_acc_fiscal_year_event_year FOREIGN KEY (property_id, fy) REFERENCES acc.fiscal_year(property_id, fy));
CREATE INDEX IX_acc_fiscal_year_event ON acc.fiscal_year_event(property_id, fy, at_utc);
