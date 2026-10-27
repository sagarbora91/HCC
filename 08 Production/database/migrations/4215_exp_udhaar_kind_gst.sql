-- HCC migration 4215 · ACC-1 Udhaar credit raised (contract §4, rulebook A17, DEC-144; lane acc1-parties). The Udhaar register
-- (ops.udhaar_receivable, migration 4150) gains what was sold on credit and its GST rate; raising a credit now publishes UdhaarCreditRaised and
-- accounting books a Sales voucher the same day: Dr 1120/<customer> (gross) · Cr F&B (restaurant) / room (room) / other revenue (other) ·
-- Cr Output CGST · Cr Output SGST (the GST inside the gross amount, split half / half).
--   kind     restaurant | room | other; NULL = raised before ACC-1 (treated as other)
--   gst_bps  GST rate in basis points (0–2800); 0 = no GST lines (every existing row)
ALTER TABLE ops.udhaar_receivable ADD
  kind VARCHAR(10) NULL CONSTRAINT CK_udhaar_receivable_kind CHECK (kind IS NULL OR kind IN ('restaurant','room','other')),
  gst_bps INT NOT NULL CONSTRAINT DF_udhaar_receivable_gst_bps DEFAULT 0 CONSTRAINT CK_udhaar_receivable_gst_bps CHECK (gst_bps BETWEEN 0 AND 2800);
GO
