-- HCC migration 4256 · ACC-2 integration, coordinator rulings 9 and 10.
-- Ruling 9 (contract §5 amended): the TDS rate column follows the PAYEE — ops.expense_vendor.payee_type 'individual' (individual / HUF: old 194C
-- 1 %) or 'other' (2 %); NULL = other. The master suggests it from the PAN's 4th letter (P / H = individual).
-- Ruling 10 (contract §7): ops.expense_category.transport — goods transport, the cash-payment limit is Rs 35,000 a day (cash.transportLimit).
ALTER TABLE ops.expense_vendor ADD payee_type VARCHAR(10) NULL
  CONSTRAINT CK_ops_expense_vendor_payee_type CHECK (payee_type IS NULL OR payee_type IN ('individual','other'));
ALTER TABLE ops.expense_category ADD transport BIT NOT NULL CONSTRAINT DF_ops_expense_category_transport DEFAULT 0;
GO
