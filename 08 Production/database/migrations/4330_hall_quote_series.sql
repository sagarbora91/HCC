-- HCC migration 4330 · F6 hall booking quotations (F6-CONTRACT §6–§7, lane H2): the QUO document joins MA-5's numbering-series master
-- (bill.number_series, 2850 / 2891) and hall.quote_series keeps the running number per property × financial year (QT/2627/0001, restarts every
-- 1 April, gap-free: allocated under an exclusive app lock inside the creating transaction; a cancelled / lost quotation keeps its number and
-- every revision keeps it too). QUO is not a GST document (no CK_bill_number_series_gst rule).
ALTER TABLE bill.number_series DROP CONSTRAINT CK_bill_number_series_document;
ALTER TABLE bill.number_series ADD CONSTRAINT CK_bill_number_series_document CHECK (document IN ('INV','BOS','CRN','RCP','KOT','REG','RST','QUO'));
GO
CREATE TABLE hall.quote_series(
  property_id INT NOT NULL CONSTRAINT FK_hall_quote_series_property REFERENCES inv.property,
  fy_code CHAR(4) NOT NULL,
  prefix VARCHAR(8) NOT NULL,             -- frozen for the FY at its first quotation
  last_seq INT NOT NULL CONSTRAINT CK_hall_quote_series_seq CHECK (last_seq >= 1),
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_hall_quote_series PRIMARY KEY (property_id, fy_code));
GO
-- A property that already stored its numbering list gets the default QUO row (QT, 4 digits, yearly reset).
INSERT bill.number_series(property_id, code, name, document, prefix, start_no, padding, reset_rule, active_from, sort_order, active, updated_by, updated_utc)
SELECT p.property_id, 'QUO', N'Hall quotation', 'QUO', 'QT', 1, 4, 'FY', '2000-04-01',
       (SELECT ISNULL(MAX(sort_order), 0) + 10 FROM bill.number_series x WHERE x.property_id = p.property_id), 1, 0, SYSUTCDATETIME()
  FROM (SELECT DISTINCT property_id FROM bill.number_series) p
 WHERE NOT EXISTS (SELECT 1 FROM bill.number_series r WHERE r.property_id = p.property_id AND (r.document = 'QUO' OR r.code = 'QUO'));
