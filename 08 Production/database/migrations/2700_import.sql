-- HCC migration 2700 · W4-13 masters & opening balances import (block 2700-2799). Original HCC design; no donor code.
-- Batches: one uploaded CSV file per master (text kept for the audit trail and for re-validation), committed at most once.
-- Committed rows are written into the OWNING modules' tables through their ports (room types/rooms inv.*, tariffs via the
-- Yield rate publication port, seasons yld.*, users plat.app_user/user_role without any credential, tills/shifts pay.*,
-- guest profiles gst.guest); this module stores only what no module owns yet: the owner's own ledgers (mapped to the fixed HCC
-- chart of accounts) and the opening trial balance (debits = credits, refused once the first business day is closed).
-- Money BIGINT paise; event times DATETIME2(3) UTC written by the application. No secret and no full ID number is stored.
CREATE SCHEMA imp;
GO
CREATE TABLE imp.batch(
  batch_id BIGINT IDENTITY CONSTRAINT PK_imp_batch PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_imp_batch_property REFERENCES inv.property,
  master VARCHAR(20) NOT NULL CONSTRAINT CK_imp_batch_master CHECK (master IN ('room-types','rooms','tariffs','seasons','staff','ledgers','opening-balances','till-cash','guests')),
  file_name NVARCHAR(200) NOT NULL,
  file_sha256 BINARY(32) NOT NULL,
  csv_text NVARCHAR(MAX) NOT NULL,
  as_of_date DATE NULL,
  row_count INT NOT NULL,
  error_count INT NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_imp_batch_status CHECK (status IN ('Uploaded','Committed')),
  uploaded_by INT NOT NULL,
  uploaded_utc DATETIME2(3) NOT NULL,
  committed_by INT NULL,
  committed_utc DATETIME2(3) NULL,
  result_json NVARCHAR(MAX) NULL CONSTRAINT CK_imp_batch_result CHECK (result_json IS NULL OR ISJSON(result_json) = 1),
  row_ver ROWVERSION,
  CONSTRAINT CK_imp_batch_committed CHECK ((status = 'Committed' AND committed_by IS NOT NULL AND committed_utc IS NOT NULL)
                                        OR (status = 'Uploaded' AND committed_by IS NULL AND committed_utc IS NULL)));
CREATE INDEX IX_imp_batch_property ON imp.batch(property_id, batch_id DESC);
-- The owner's ledgers (e.g. Tally names). hcc_account = the fixed HCC chart code the ledger rolls up into (NULL: equity and
-- ledgers HCC never posts to). Codes of the HCC chart itself are refused by the application.
CREATE TABLE imp.ledger(
  property_id INT NOT NULL CONSTRAINT FK_imp_ledger_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(100) NOT NULL,
  grp VARCHAR(10) NOT NULL CONSTRAINT CK_imp_ledger_group CHECK (grp IN ('Asset','Liability','Equity','Income','Expense')),
  hcc_account VARCHAR(4) NULL CONSTRAINT CK_imp_ledger_account CHECK (hcc_account IS NULL OR hcc_account <> '9999'),
  batch_id BIGINT NOT NULL CONSTRAINT FK_imp_ledger_batch REFERENCES imp.batch,
  changed_by INT NOT NULL,
  changed_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_imp_ledger PRIMARY KEY (property_id, code));
-- The opening trial balance (one per property; a later setup import replaces it while no business day is closed).
CREATE TABLE imp.opening_set(
  property_id INT NOT NULL CONSTRAINT PK_imp_opening_set PRIMARY KEY CONSTRAINT FK_imp_opening_set_property REFERENCES inv.property,
  as_of_date DATE NOT NULL,
  batch_id BIGINT NOT NULL CONSTRAINT FK_imp_opening_set_batch REFERENCES imp.batch,
  debit_paise BIGINT NOT NULL,
  credit_paise BIGINT NOT NULL,
  committed_by INT NOT NULL,
  committed_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_imp_opening_set_balanced CHECK (debit_paise = credit_paise AND debit_paise >= 0));
CREATE TABLE imp.opening_balance(
  property_id INT NOT NULL CONSTRAINT FK_imp_opening_balance_set REFERENCES imp.opening_set,
  ledger_code VARCHAR(20) NOT NULL,
  debit_paise BIGINT NOT NULL CONSTRAINT CK_imp_opening_debit CHECK (debit_paise BETWEEN 0 AND 10000000000),
  credit_paise BIGINT NOT NULL CONSTRAINT CK_imp_opening_credit CHECK (credit_paise BETWEEN 0 AND 10000000000),
  CONSTRAINT PK_imp_opening_balance PRIMARY KEY (property_id, ledger_code),
  CONSTRAINT CK_imp_opening_side CHECK ((debit_paise > 0 AND credit_paise = 0) OR (credit_paise > 0 AND debit_paise = 0)));
