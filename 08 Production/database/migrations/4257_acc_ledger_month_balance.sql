-- HCC migration 4257 · ACC-2 parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; ACC1-DONE §7 item 8): the aggregated opening read.
-- A closed month whose every day with accounting activity is locked has fixed vouchers (the lock snapshots), so its debit / credit per ledger
-- id is stored once here and a report's opening (statement, six-column trial balance) is the sum of these month rows plus the vouchers of the
-- days after the last stored month — instead of every voucher since the first source.
--   acc.ledger_month          one row per stored month: the fingerprint of the current period locks it was built from (count and the sum of
--                             their lock ids — an unlock / re-lock creates a new lock id, so the fingerprint changes and the month is rebuilt)
--   acc.ledger_month_balance  the month's totals per ledger id (a ledger code or a party ledger id '<control>/<party>', as acc.locked_entry)
-- Rows are written by the report read (Hcc.Infrastructure.SqlServer.Accounting.SqlLedgerMonthBalanceStore) and are a cache of the lock
-- snapshots: deleting them only makes the next read rebuild them. Original HCC design; money BIGINT paise; times DATETIME2(3) UTC.
CREATE TABLE acc.ledger_month(
  property_id INT NOT NULL CONSTRAINT FK_acc_ledger_month_property REFERENCES inv.property,
  month_start DATE NOT NULL CONSTRAINT CK_acc_ledger_month_start CHECK (DAY(month_start) = 1),
  lock_count INT NOT NULL CONSTRAINT CK_acc_ledger_month_count CHECK (lock_count >= 0),
  lock_sum BIGINT NOT NULL CONSTRAINT CK_acc_ledger_month_sum CHECK (lock_sum >= 0),
  debit_paise BIGINT NOT NULL,
  credit_paise BIGINT NOT NULL,
  built_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_acc_ledger_month PRIMARY KEY (property_id, month_start),
  CONSTRAINT CK_acc_ledger_month_balanced CHECK (debit_paise = credit_paise));
CREATE TABLE acc.ledger_month_balance(
  property_id INT NOT NULL,
  month_start DATE NOT NULL,
  account VARCHAR(45) NOT NULL CONSTRAINT CK_acc_ledger_month_balance_account CHECK (LEN(account) >= 4),
  debit_paise BIGINT NOT NULL CONSTRAINT CK_acc_ledger_month_balance_debit CHECK (debit_paise >= 0),
  credit_paise BIGINT NOT NULL CONSTRAINT CK_acc_ledger_month_balance_credit CHECK (credit_paise >= 0),
  CONSTRAINT PK_acc_ledger_month_balance PRIMARY KEY (property_id, month_start, account),
  CONSTRAINT FK_acc_ledger_month_balance_month FOREIGN KEY (property_id, month_start) REFERENCES acc.ledger_month ON DELETE CASCADE);
GO
