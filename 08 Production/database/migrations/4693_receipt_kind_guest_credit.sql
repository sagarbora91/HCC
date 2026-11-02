-- HCC migration 4693 · LF B2 (LF-CONTRACT §7): receipt kind 'guest-credit' = the folio balance moved to the guest's Udhaar account at
-- check-out (Journal Dr 1120/UD-<customerKey> · Cr 1100; the mirror of company credit). It moves no cash: no till, no shift.
ALTER TABLE pay.receipt DROP CONSTRAINT CK_receipt_kind;
ALTER TABLE pay.receipt DROP CONSTRAINT CK_receipt_flows_no_cash;
GO
ALTER TABLE pay.receipt ADD CONSTRAINT CK_receipt_kind CHECK (kind IN ('payment','deposit','deposit-return','refund','company-credit','deposit-apply','ota-credit','guest-credit'));
ALTER TABLE pay.receipt ADD CONSTRAINT CK_receipt_flows_no_cash CHECK (kind NOT IN ('deposit-apply','ota-credit','guest-credit') OR (till_id IS NULL AND shift_id IS NULL));
