-- HCC migration 4220 · ACC-1 opening balances entered by hand (02 Planning/agents/ACC1-POSTING-CONTRACT.md §5; rulebook §G2; DEC-139 / DEC-148).
-- One opening entry per property, dated the start of the business financial year (1 April): the Manager or the Accountant saves it
-- (a draft may be unbalanced: the application adds the balancing line on 1400 Opening-balance difference, so the stored lines always
-- balance), the Owner approves it, and the first period lock of a date on or after as_of is refused while the entry is a draft or
-- 1400 <> 0 ("opening balances pending"). The Owner may reopen an approved entry (reason kept here and audited) only while no period
-- lock exists on or after as_of. Only an approved entry counts in the cumulative trial balance, where it supersedes the imported
-- opening trial balance (W4-13). account = a ledger code or a party ledger id '<control code>/<party id>' (contract §3).
-- Original HCC design; no donor code. Money BIGINT paise; times DATETIME2(3) UTC written by the application.
CREATE TABLE acc.opening_entry(
  property_id INT NOT NULL CONSTRAINT PK_acc_opening_entry PRIMARY KEY CONSTRAINT FK_acc_opening_entry_property REFERENCES inv.property,
  as_of DATE NOT NULL,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_acc_opening_entry_status CHECK (status IN ('draft','approved')),
  entered_by INT NOT NULL,
  entered_utc DATETIME2(3) NOT NULL,
  approved_by INT NULL,
  approved_utc DATETIME2(3) NULL,
  note NVARCHAR(200) NULL,
  reopened_by INT NULL,
  reopened_utc DATETIME2(3) NULL,
  reopen_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_acc_opening_entry_fy_start CHECK (MONTH(as_of) = 4 AND DAY(as_of) = 1),
  CONSTRAINT CK_acc_opening_entry_approval CHECK ((status = 'draft' AND approved_by IS NULL AND approved_utc IS NULL)
                                               OR (status = 'approved' AND approved_by IS NOT NULL AND approved_utc IS NOT NULL)),
  CONSTRAINT CK_acc_opening_entry_reopen CHECK ((reopened_utc IS NULL AND reopened_by IS NULL AND reopen_reason IS NULL)
                                             OR (reopened_utc IS NOT NULL AND reopened_by IS NOT NULL AND reopen_reason IS NOT NULL)));
CREATE TABLE acc.opening_line(
  property_id INT NOT NULL CONSTRAINT FK_acc_opening_line_entry REFERENCES acc.opening_entry,
  line_no SMALLINT NOT NULL,
  account VARCHAR(41) NOT NULL,
  debit_paise BIGINT NOT NULL CONSTRAINT CK_acc_opening_line_debit CHECK (debit_paise BETWEEN 0 AND 10000000000),
  credit_paise BIGINT NOT NULL CONSTRAINT CK_acc_opening_line_credit CHECK (credit_paise BETWEEN 0 AND 10000000000),
  CONSTRAINT PK_acc_opening_line PRIMARY KEY (property_id, line_no),
  CONSTRAINT UQ_acc_opening_line_account UNIQUE (property_id, account),
  CONSTRAINT CK_acc_opening_line_side CHECK ((debit_paise > 0 AND credit_paise = 0) OR (credit_paise > 0 AND debit_paise = 0)),
  CONSTRAINT CK_acc_opening_line_account CHECK (account LIKE '[0-9][0-9][0-9][0-9]' OR account LIKE '[0-9][0-9][0-9][0-9]/_%'));
