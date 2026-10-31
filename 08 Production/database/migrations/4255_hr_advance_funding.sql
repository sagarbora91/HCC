-- HCC migration 4255 · ACC-2 integration, coordinator ruling 5 (parity DONE open item 1): a salary advance is paid by a payment method
-- (funding; the client sends it, default Cash). Drawer cash leaves the till of the open shift (pay.cash_movement kind expense, source type
-- staff-advance) and goes through the contract §7 cash guard (above Rs 10,000 to one person in a day: allowed, "deduction lost").
-- Rows before ACC-2 keep funding NULL = the simulated bank transfer they were (Accounting credits the bank, as before).
ALTER TABLE hr.advance ADD
  funding NVARCHAR(60) NULL,
  till_id INT NULL CONSTRAINT FK_advance_till REFERENCES pay.till,
  cash_movement_id BIGINT NULL,
  cash_deduction_lost BIT NOT NULL CONSTRAINT DF_advance_cash_deduction_lost DEFAULT 0;
GO
CREATE INDEX IX_advance_date_till ON hr.advance(property_id, advance_date) INCLUDE (staff_id, amount_paise) WHERE till_id IS NOT NULL;
GO
