-- HCC migration 4348 · F6 hall booking print log (spec §19, lane H3): who printed which document and when (quotation version, function sheet, tax invoice, receipt).
-- Append-only; the function-sheet row records whether prices were included (the kitchen copy has none).
CREATE TABLE hall.print_log(
  print_id BIGINT IDENTITY CONSTRAINT PK_hall_print_log PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_print_log_property REFERENCES inv.property,
  doc_kind VARCHAR(10) NOT NULL CONSTRAINT CK_hall_print_log_kind CHECK (doc_kind IN ('QUOTATION','BEO','INVOICE','RECEIPT')),
  ref_id BIGINT NOT NULL,                 -- quotation_id, event_id or receipt_id
  version INT NULL,
  with_prices BIT NOT NULL CONSTRAINT DF_hall_print_log_prices DEFAULT 1,
  printed_by INT NOT NULL,
  printed_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_hall_print_log_ref ON hall.print_log(property_id, doc_kind, ref_id);
