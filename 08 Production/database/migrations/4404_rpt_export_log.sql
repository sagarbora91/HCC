-- HCC migration 4404 · F7-R1 print / export log (catalogue §1.3 rule V.4): every print and every export writes one row. Append-only.
CREATE TABLE rpt.export_log(
  log_id BIGINT IDENTITY CONSTRAINT PK_rpt_export_log PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_rpt_export_log_property REFERENCES inv.property,
  at_utc DATETIME2(3) NOT NULL,
  principal_id INT NOT NULL,
  report_id VARCHAR(80) NOT NULL,
  params_hash CHAR(64) NOT NULL,
  params NVARCHAR(2000) NOT NULL CONSTRAINT CK_rpt_export_log_params CHECK (ISJSON(params) = 1),
  format VARCHAR(8) NOT NULL CONSTRAINT CK_rpt_export_log_format CHECK (format IN ('print', 'xlsx', 'pdf', 'csv', 'xml', 'zip')),
  row_count INT NOT NULL,
  file_name NVARCHAR(200) NULL,
  provisional BIT NOT NULL);
CREATE INDEX IX_rpt_export_log_property ON rpt.export_log(property_id, at_utc DESC);
