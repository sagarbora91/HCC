-- HCC migration 4286 · ACC-4 acc4-bank · imported bank statements (ACC4-CONTRACT §2.2). A file is imported once (file_hash = SHA-256,
-- BANK_DUP_FILE); rows keep the bank's view (credit = money in, paise). Rows overlapping an earlier import are skipped unless kept.
CREATE TABLE acc.bank_statement_import(
  import_id BIGINT IDENTITY CONSTRAINT PK_acc_bank_statement_import PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_bank_import_property REFERENCES inv.property,
  bank_ledger VARCHAR(41) NOT NULL,
  layout_id VARCHAR(40) NOT NULL CONSTRAINT FK_acc_bank_import_layout REFERENCES mst.bank_statement_layout,
  file_name NVARCHAR(200) NOT NULL,
  file_hash CHAR(64) NOT NULL,
  from_date DATE NOT NULL, to_date DATE NOT NULL,
  opening_balance BIGINT NOT NULL, closing_balance BIGINT NOT NULL,
  row_count INT NOT NULL,
  imported_by INT NOT NULL, imported_utc DATETIME2(3) NOT NULL);
CREATE UNIQUE INDEX UX_acc_bank_import_hash ON acc.bank_statement_import(property_id, file_hash);
CREATE TABLE acc.bank_statement_row(
  row_id BIGINT IDENTITY CONSTRAINT PK_acc_bank_statement_row PRIMARY KEY,
  import_id BIGINT NOT NULL CONSTRAINT FK_acc_bank_row_import REFERENCES acc.bank_statement_import,
  property_id INT NOT NULL,
  bank_ledger VARCHAR(41) NOT NULL,
  row_date DATE NOT NULL,
  narration NVARCHAR(400) NOT NULL,
  ref NVARCHAR(80) NULL,
  debit BIGINT NOT NULL CONSTRAINT CK_acc_bank_row_dr CHECK (debit >= 0),
  credit BIGINT NOT NULL CONSTRAINT CK_acc_bank_row_cr CHECK (credit >= 0),
  balance BIGINT NULL,
  status VARCHAR(9) NOT NULL CONSTRAINT CK_acc_bank_row_status CHECK (status IN ('unmatched','matched','ignored')),
  ignore_reason NVARCHAR(200) NULL,
  match_id BIGINT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_acc_bank_row_side CHECK (debit = 0 OR credit = 0));
CREATE INDEX IX_acc_bank_row_ledger ON acc.bank_statement_row(property_id, bank_ledger, row_date, status);
