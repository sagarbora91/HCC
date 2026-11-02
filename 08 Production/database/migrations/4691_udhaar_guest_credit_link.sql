-- HCC migration 4691 · LF B2 / A7 (LF-CONTRACT §6, §7): Udhaar receivables raised by LF-money carry their origin.
--   guest_id / folio_id / receipt_id  B2: a guest-credit check-out — the folio balance moved to the guest's account by the 'guest-credit' receipt;
--                                     the revenue was booked with the folio lines, the receipt books Journal Dr 1120/UD-<customer key> · Cr 1100.
--   counter_bill_id                   A7: a counter bill settled on Udhaar — its Sales voucher (source 'counter') already debits 1120/<customer>.
-- Neither has a Sales voucher of its own, so the ACC-2 Udhaar back-fill skips them.
ALTER TABLE ops.udhaar_receivable ADD
  guest_id BIGINT NULL CONSTRAINT FK_udhaar_receivable_guest REFERENCES gst.guest,
  folio_id BIGINT NULL CONSTRAINT FK_udhaar_receivable_folio REFERENCES bill.folio,
  receipt_id BIGINT NULL CONSTRAINT FK_udhaar_receivable_receipt REFERENCES pay.receipt,
  counter_bill_id BIGINT NULL CONSTRAINT FK_udhaar_receivable_counter REFERENCES ctr.bill;
GO
CREATE UNIQUE INDEX UX_udhaar_receivable_receipt ON ops.udhaar_receivable(receipt_id) WHERE receipt_id IS NOT NULL;
CREATE UNIQUE INDEX UX_udhaar_receivable_counter ON ops.udhaar_receivable(counter_bill_id) WHERE counter_bill_id IS NOT NULL;
CREATE INDEX IX_udhaar_receivable_guest ON ops.udhaar_receivable(guest_id) WHERE guest_id IS NOT NULL;
