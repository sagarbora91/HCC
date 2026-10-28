-- HCC migration 4240 · ACC-2 loan party ledgers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §2 "Add loan"; rulebook §5 B11).
-- The chart screen's "Add loan" (Owner, PERM-ACC-007) creates a party of the control ledger 2400 Bank loans (secured): account
-- 2400/LN-<code> (party id LN-<code>), the lender and the last 4 digits of the loan account. Manual vouchers post to it (loan received,
-- EMI principal). acc.ledger keeps 4-digit codes only, so loans have their own table.
CREATE TABLE acc.loan(
  property_id INT NOT NULL CONSTRAINT FK_acc_loan_property REFERENCES inv.property,
  party_id VARCHAR(40) NOT NULL CONSTRAINT CK_acc_loan_party CHECK (party_id LIKE 'LN-_%' AND party_id NOT LIKE '%/%'),
  name NVARCHAR(60) NOT NULL CONSTRAINT CK_acc_loan_name CHECK (LEN(name) >= 3),
  lender NVARCHAR(60) NOT NULL CONSTRAINT CK_acc_loan_lender CHECK (LEN(lender) >= 2),
  account_last4 CHAR(4) NULL CONSTRAINT CK_acc_loan_last4 CHECK (account_last4 IS NULL OR account_last4 LIKE '[0-9][0-9][0-9][0-9]'),
  note NVARCHAR(200) NULL,
  active BIT NOT NULL CONSTRAINT DF_acc_loan_active DEFAULT 1,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_acc_loan PRIMARY KEY (property_id, party_id));
GO
