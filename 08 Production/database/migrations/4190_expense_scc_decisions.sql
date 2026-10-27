-- HCC migration 4190 · SCC decisions wave, expenses (owner, 27 Oct 2026 — Decision Register DEC-129…131, DEC-135; block 4190–4199).
-- Builds on 4150 (DEC-120 SCC expense features). No new permission ids: DEC-129 moves the budget and recurring-template commands from
-- PERM-EXP-002 to PERM-MST-002 (the Owner's money-master permission, DEC-058) in code; DEC-131's unlock is PERM-MST-002 as the lock.
--   DEC-135 ops.expense.itc_allowed      input tax credit of the bill's GST: 1 = ITC allowed (the GST goes to the GST input-credit ledger),
--                                        0 = ITC disallowed (the GST stays in the expense cost). Every existing row is 0 (disallowed: nothing
--                                        is claimed unless someone marks it); a row without GST is always 0.
--   DEC-130 ops.udhaar_settlement.desk   1 = taken as a desk receipt (cash into the open shift's drawer — pay.cash_movement kind 'payment',
--                                        source type 'udhaar', source id = settlement — other modes to their own ledgers; booked in Accounting
--                                        as Dr tender · Cr Udhaar receivables). Payments recorded before stay 0: they never reached the drawer.
--           + reference (non-cash transaction reference), till_id (the drawer that took the cash).
--   DEC-131 ops.expense_month_lock       unlocked_by / unlocked_utc / unlock_reason: the Owner unlocked the month (writes no longer need the
--                                        override; the figures are stale until the month is locked again, which clears them) + gst_itc_paise
--                                        (DEC-135: the ITC-allowed part of the feed's GST paid);
--           ops.expense_month_unlock     every unlock with its reason (the audit trail keeps it too; a re-lock never erases it).
ALTER TABLE ops.expense ADD itc_allowed BIT NOT NULL CONSTRAINT DF_expense_itc_allowed DEFAULT 0;
GO
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_itc CHECK (itc_allowed = 0 OR gst_paise IS NOT NULL);
GO
ALTER TABLE ops.udhaar_settlement ADD
  desk BIT NOT NULL CONSTRAINT DF_udhaar_settlement_desk DEFAULT 0,
  reference NVARCHAR(120) NULL,
  till_id INT NULL CONSTRAINT FK_udhaar_settlement_till REFERENCES pay.till;
GO
ALTER TABLE ops.udhaar_settlement ADD CONSTRAINT CK_udhaar_settlement_till CHECK (till_id IS NULL OR desk = 1);
GO
ALTER TABLE ops.expense_month_lock ADD
  gst_itc_paise BIGINT NULL,
  unlocked_by INT NULL,
  unlocked_utc DATETIME2(3) NULL,
  unlock_reason NVARCHAR(200) NULL;
GO
ALTER TABLE ops.expense_month_lock ADD CONSTRAINT CK_expense_month_unlock CHECK (
     (unlocked_by IS NULL AND unlocked_utc IS NULL AND unlock_reason IS NULL)
  OR (unlocked_by IS NOT NULL AND unlocked_utc IS NOT NULL AND LEN(unlock_reason) >= 3));
GO
CREATE TABLE ops.expense_month_unlock(
  unlock_id BIGINT IDENTITY CONSTRAINT PK_expense_month_unlock PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_expense_month_unlock_property REFERENCES inv.property,
  lock_month CHAR(7) NOT NULL CONSTRAINT CK_expense_month_unlock_month CHECK (lock_month LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]'),
  unlocked_by INT NOT NULL,
  unlocked_utc DATETIME2(3) NOT NULL,
  reason NVARCHAR(200) NOT NULL CONSTRAINT CK_expense_month_unlock_reason CHECK (LEN(reason) >= 3));
CREATE INDEX IX_expense_month_unlock ON ops.expense_month_unlock(property_id, lock_month, unlock_id);
GO
