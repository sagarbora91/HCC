-- HCC migration 0800 · W2-4 expenses & petty cash (Wave 2 block 0800–0899). Prototype rules: closing.ts EXPENSE / EXPENSE_SUBMIT /
-- EXPENSE_APPROVE / EXPENSE_REVERSE; RBAC matrix PERM-EXP-001/002/003 (+ DEC-063 G4/G5), PERM-SOD-001, DEC-052 (cash expenses are
-- movements in the shared drawer, written through ICashDrawerPort into pay.cash_movement, never twice).
-- Original HCC design; no donor code. Uses 0002/0400 tables as they are (inv.property, pay.till); the EXP PERM grants already exist
-- in 0101_access_rbac_seed (OWN/MGR/ACC EXP-002 + EXP-003, OWN/MGR/REC/CSH/ACC EXP-001, Manager EXP-002 limit 500000 paise), so this
-- block adds no grants. Event times are written by the application from its TimeProvider.

-- One row per expense record. A reversal is its own row (negative amount, status Posted, business date = the reversal's day) that
-- points at the original (which becomes Reversed), exactly as the prototype does, so Σ posted-or-reversed amounts by date is the
-- figure closing and reports use.
CREATE TABLE ops.expense(
  expense_id BIGINT IDENTITY CONSTRAINT PK_expense PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_expense_property REFERENCES inv.property,
  vendor NVARCHAR(120) NOT NULL,
  category NVARCHAR(60) NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_expense_amount CHECK (amount_paise <> 0 AND amount_paise BETWEEN -10000000000 AND 10000000000),
  method VARCHAR(14) NOT NULL CONSTRAINT CK_expense_method CHECK (method IN ('Cash','UPI','Bank transfer','Vendor credit')),
  business_date DATE NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_expense_status CHECK (status IN ('Draft','Pending','Posted','Rejected','Reversed')),
  submitted_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  notes NVARCHAR(300) NULL,
  receipt NVARCHAR(120) NULL,
  reason NVARCHAR(300) NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  till_id INT NULL CONSTRAINT FK_expense_till REFERENCES pay.till,
  reversal_of BIGINT NULL CONSTRAINT FK_expense_reversal_of REFERENCES ops.expense,
  reversed_by BIGINT NULL CONSTRAINT FK_expense_reversed_by REFERENCES ops.expense,
  row_ver ROWVERSION,
  CONSTRAINT CK_expense_sign CHECK ((reversal_of IS NULL AND amount_paise > 0) OR (reversal_of IS NOT NULL AND amount_paise < 0 AND status = 'Posted')),
  CONSTRAINT CK_expense_decided CHECK (status IN ('Draft','Pending') OR (decided_by IS NOT NULL AND decided_utc IS NOT NULL)),
  -- PERM-SOD-001: nobody approves or rejects their own expense (a reversal is decided by whoever reverses it).
  CONSTRAINT CK_expense_sod CHECK (reversal_of IS NOT NULL OR decided_by IS NULL OR decided_by <> submitted_by),
  CONSTRAINT CK_expense_reversed CHECK ((status = 'Reversed' AND reversed_by IS NOT NULL) OR (status <> 'Reversed' AND reversed_by IS NULL)));
-- A posted expense is reversed at most once, even under a race (the handler also locks the row).
CREATE UNIQUE INDEX UX_expense_reversal_of ON ops.expense(reversal_of) WHERE reversal_of IS NOT NULL;
CREATE INDEX IX_expense_date ON ops.expense(property_id, business_date) INCLUDE (status, method, amount_paise, reversal_of);
CREATE INDEX IX_expense_status ON ops.expense(property_id, status, expense_id);
CREATE INDEX IX_expense_submitter ON ops.expense(property_id, submitted_by, expense_id);
GO
