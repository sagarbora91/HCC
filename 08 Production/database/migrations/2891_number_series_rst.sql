-- HCC migration 2891 · MA-11 review (block 2890–2899): Wave B's restaurant tax invoices (RST series, DEC-072, migration 3200) become a row of
-- MA-5's numbering-series master (bill.number_series, 2850), read by the invoices module the same way as the tax-invoice series
-- (INumberSeriesSource). Before: the master could not hold an RST row, and once the Owner had stored the list, every outlet bill's
-- invoice failed with "No numbering series is in force for the RST". RST is a GST document (yearly reset, ≤ 16 characters).
-- Receipt (RCP), KOT and registration-card (REG) series stay defined but RESERVED (no command numbers them yet).
ALTER TABLE bill.number_series DROP CONSTRAINT CK_bill_number_series_document;
ALTER TABLE bill.number_series ADD CONSTRAINT CK_bill_number_series_document CHECK (document IN ('INV','BOS','CRN','RCP','KOT','REG','RST'));
ALTER TABLE bill.number_series DROP CONSTRAINT CK_bill_number_series_gst;
ALTER TABLE bill.number_series ADD CONSTRAINT CK_bill_number_series_gst CHECK (document NOT IN ('INV','BOS','CRN','RST') OR (reset_rule = 'FY' AND LEN(prefix) <= 6 AND LEN(prefix) + 6 + padding <= 16));
GO
-- A property that already stored its list gets the default RST row (the numbers it printed before: RST/2627/0000001, full 16 characters).
INSERT bill.number_series(property_id, code, name, document, prefix, start_no, padding, reset_rule, active_from, sort_order, active, updated_by, updated_utc)
SELECT p.property_id, 'RST', N'Restaurant tax invoice', 'RST', 'RST', 1, 7, 'FY', '2000-04-01',
       (SELECT ISNULL(MAX(sort_order), 0) + 10 FROM bill.number_series x WHERE x.property_id = p.property_id), 1, 0, SYSUTCDATETIME()
  FROM (SELECT DISTINCT property_id FROM bill.number_series) p
 WHERE NOT EXISTS (SELECT 1 FROM bill.number_series r WHERE r.property_id = p.property_id AND (r.document = 'RST' OR r.code = 'RST'));
