-- HCC migration 1201 · W2-6 POS: intra-state CGST / SGST split stored on every outlet / room-posted bill (DEC-069 GST master wiring at the
-- Wave 2 merge). SGST = half the bill tax toward zero, CGST = the rest; NULL only for rows written before this migration.
ALTER TABLE pos.bill ADD cgst_paise BIGINT NULL, sgst_paise BIGINT NULL;
GO
ALTER TABLE pos.bill ADD CONSTRAINT CK_pos_bill_gst_split CHECK (cgst_paise IS NULL OR (sgst_paise IS NOT NULL AND cgst_paise + sgst_paise = tax_paise AND ABS(cgst_paise - sgst_paise) <= 1));
