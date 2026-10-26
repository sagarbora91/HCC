-- HCC migration 3200 · DEC-072 (audit A03a-01): walk-in and paid-at-outlet restaurant bills are GST tax invoices in their own series.
-- A settled outlet POS bill (pos.bill mode 'outlet') is issued as an ivc.invoice with source_kind 'pos' (source_id = pos.bill.bill_id) in
-- the gap-free series doc_type 'RST' (number RST/<FY>/<7 digits>, e.g. RST/2627/0000001 = 16 characters, restarting each 1 April), with
-- the same machinery as the INV series (ivc.series row per property / FY / type under an app lock in the settling transaction). A bill
-- charged to a room folio (mode 'folio') is never invoiced here: its folio line reaches the guest's room invoice (INV) at checkout.
-- A voided outlet bill whose invoice was issued gets a full credit note in the existing CN series (doc_type CRN). CA to confirm (DEC-072).
-- Only CHECK constraints are widened; no data changes. Issued-document immutability triggers (52400–52405) are unchanged.
ALTER TABLE ivc.series DROP CONSTRAINT CK_ivc_series_type;
ALTER TABLE ivc.series ADD CONSTRAINT CK_ivc_series_type CHECK (doc_type IN ('INV','BOS','CRN','RST'));
GO
ALTER TABLE ivc.invoice DROP CONSTRAINT CK_ivc_invoice_type;
ALTER TABLE ivc.invoice ADD CONSTRAINT CK_ivc_invoice_type CHECK (doc_type IN ('INV','BOS','RST'));
ALTER TABLE ivc.invoice DROP CONSTRAINT CK_ivc_invoice_source;
ALTER TABLE ivc.invoice ADD CONSTRAINT CK_ivc_invoice_source CHECK (source_kind IN ('folio','group','pos'));
GO
-- An RST number is only ever a restaurant (pos) document, and a pos document is only ever RST or, for a nil-rated bill, a bill of supply.
ALTER TABLE ivc.invoice ADD CONSTRAINT CK_ivc_invoice_rst_source CHECK ((source_kind = 'pos' AND doc_type IN ('RST','BOS')) OR (source_kind <> 'pos' AND doc_type <> 'RST'));
