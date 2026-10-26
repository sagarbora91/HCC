-- HCC migration 2890 · MA-11 review (block 2890–2899): records take any method of the Payment methods master (mst.payment_method, 2830).
-- Before: the method / tender columns of the money records were fixed lists by CHECK constraint and narrow VARCHAR columns, so a method the
-- Owner added (e.g. "Wallet", "Cheque") was refused by the database even though the master allowed it:
--   ops.expense.method   VARCHAR(14) CK_expense_method   IN ('Cash','UPI','Bank transfer','Vendor credit')   (0800)
--   pos.bill.tender      VARCHAR(8)  CK_pos_bill_tender  IN ('Cash','UPI','Card')                          (1200)
--   pos.receipt.tender   VARCHAR(8)  CK_pos_receipt_tender IN ('Cash','UPI','Card')                        (1200)
--   pay.receipt.method   VARCHAR(16) (no list; too narrow for a 60-character method name)                 (0002)
-- After: NVARCHAR(60) (= mst.payment_method.name) with a length check; the application resolves the value against the master (usable
-- at the place, active, reference) — PaymentMethodBook in Hcc.Domain/MoneyMasters. "Cash" as a literal is no longer the drawer test:
-- a method that settles to a till is drawer cash, so CK_pos_bill_cash becomes "till, shift and drawer movement together or none".
-- Applied migrations are never edited; this one only touches objects created before it (0002, 0400, 0800, 1200), so it runs the same on
-- a fresh database and on one upgraded from before the masters wave (whose 2900–3701 are already applied).

-- ops.expense.method
ALTER TABLE ops.expense DROP CONSTRAINT CK_expense_method;
DROP INDEX IX_expense_date ON ops.expense;
ALTER TABLE ops.expense ALTER COLUMN method NVARCHAR(60) NOT NULL;
CREATE INDEX IX_expense_date ON ops.expense(property_id, business_date) INCLUDE (status, method, amount_paise, reversal_of);
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_method CHECK (LEN(method) BETWEEN 1 AND 60);

-- pos.bill.tender (the shape and cash rules read the column, so they are dropped and added again)
ALTER TABLE pos.bill DROP CONSTRAINT CK_pos_bill_tender;
ALTER TABLE pos.bill DROP CONSTRAINT CK_pos_bill_shape;
ALTER TABLE pos.bill DROP CONSTRAINT CK_pos_bill_cash;
ALTER TABLE pos.bill ALTER COLUMN tender NVARCHAR(60) NULL;
ALTER TABLE pos.bill ADD CONSTRAINT CK_pos_bill_tender CHECK (tender IS NULL OR LEN(tender) BETWEEN 1 AND 60);
ALTER TABLE pos.bill ADD CONSTRAINT CK_pos_bill_shape CHECK ((mode = 'outlet' AND tender IS NOT NULL AND folio_id IS NULL AND charge_id IS NULL)
                                   OR (mode = 'folio' AND tender IS NULL AND folio_id IS NOT NULL AND charge_id IS NOT NULL AND till_id IS NULL));
ALTER TABLE pos.bill ADD CONSTRAINT CK_pos_bill_cash CHECK ((till_id IS NULL AND shift_id IS NULL AND cash_movement_id IS NULL)
                                  OR (till_id IS NOT NULL AND shift_id IS NOT NULL AND cash_movement_id IS NOT NULL));

-- pos.receipt.tender
ALTER TABLE pos.receipt DROP CONSTRAINT CK_pos_receipt_tender;
DROP INDEX IX_pos_receipt_date ON pos.receipt;
ALTER TABLE pos.receipt ALTER COLUMN tender NVARCHAR(60) NOT NULL;
CREATE INDEX IX_pos_receipt_date ON pos.receipt(business_date, tender) INCLUDE (amount_paise);
ALTER TABLE pos.receipt ADD CONSTRAINT CK_pos_receipt_tender CHECK (LEN(tender) BETWEEN 1 AND 60);

-- pay.receipt.method
DROP INDEX IX_receipt_business_date ON pay.receipt;
DROP INDEX IX_receipt_folio ON pay.receipt;
ALTER TABLE pay.receipt ALTER COLUMN method NVARCHAR(60) NOT NULL;
CREATE INDEX IX_receipt_business_date ON pay.receipt(business_date) INCLUDE (kind, method, amount_paise);
CREATE INDEX IX_receipt_folio ON pay.receipt(folio_id) INCLUDE (kind, method, amount_paise, refund_of);
