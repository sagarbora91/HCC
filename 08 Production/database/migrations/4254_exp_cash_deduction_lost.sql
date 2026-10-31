-- HCC migration 4254 · ACC-2 flows: cash guard B10 (contract §7; rulebook B10; old s.40A(3), VERIFY section number). An expense paid in
-- drawer cash that takes one payee's cash for the day above ₹10,000 (₹35,000 for goods transport) is posted with a warning and marked
-- "deduction lost" (the P&L notes list them). The limits are dated Master-configuration values cash.receiptLimit / cash.paymentLimit /
-- cash.transportLimit (Owner-only; defaults in Hcc.Domain.Accounting.CashGuard).
-- ACC-2 integration: acc2-purchase's 4230 already adds ops.expense.cash_deduction_lost (both lanes wrote it); the column is added here
-- only when it is missing, so the wave's migrations run in order on one database (edited before the wave reached main: never applied there).
IF COL_LENGTH('ops.expense', 'cash_deduction_lost') IS NULL
  ALTER TABLE ops.expense ADD cash_deduction_lost BIT NOT NULL CONSTRAINT DF_expense_cash_deduction_lost DEFAULT 0;
GO
