-- HCC migration 4287 · ACC-4 acc4-bank · matches and cleared dates (ACC4-CONTRACT §2.3–§2.4). A match ties statement rows to book lines
-- (voucher source key + entry index); the book line's cleared date = the statement row date (books are never re-dated). Undo stamps undone_utc
-- (allowed until the month is locked). rule: auto-ref | auto-amount-date | manual | created.
CREATE TABLE acc.bank_match(
  match_id BIGINT IDENTITY CONSTRAINT PK_acc_bank_match PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_bank_match_property REFERENCES inv.property,
  bank_ledger VARCHAR(41) NOT NULL,
  rule_kind VARCHAR(16) NOT NULL CONSTRAINT CK_acc_bank_match_rule CHECK (rule_kind IN ('auto-ref','auto-amount-date','manual','created')),
  cleared_date DATE NOT NULL,
  matched_by INT NOT NULL, matched_utc DATETIME2(3) NOT NULL,
  undone_by INT NULL, undone_utc DATETIME2(3) NULL);
CREATE TABLE acc.bank_match_row(
  match_id BIGINT NOT NULL CONSTRAINT FK_acc_bank_match_row_match REFERENCES acc.bank_match,
  row_id BIGINT NOT NULL CONSTRAINT FK_acc_bank_match_row_row REFERENCES acc.bank_statement_row,
  CONSTRAINT PK_acc_bank_match_row PRIMARY KEY (match_id, row_id));
CREATE TABLE acc.bank_match_line(
  match_id BIGINT NOT NULL CONSTRAINT FK_acc_bank_match_line_match REFERENCES acc.bank_match,
  source_key VARCHAR(80) NOT NULL,
  entry_no SMALLINT NOT NULL,
  cleared_date DATE NOT NULL,
  CONSTRAINT PK_acc_bank_match_line PRIMARY KEY (match_id, source_key, entry_no));
CREATE INDEX IX_acc_bank_match_line_source ON acc.bank_match_line(source_key, entry_no);
