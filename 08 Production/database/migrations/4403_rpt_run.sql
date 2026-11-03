-- HCC migration 4403 · F7-R1 report runs (manual and scheduled). status ok | failed | held (delivery held by the F5 egress gate while net.* is OFF).
-- The file itself lands in the documents store by reference (file_ref), never inline in the main database.
CREATE TABLE rpt.run(
  run_id BIGINT IDENTITY CONSTRAINT PK_rpt_run PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_rpt_run_property REFERENCES inv.property,
  schedule_id BIGINT NULL CONSTRAINT FK_rpt_run_schedule REFERENCES rpt.schedule ON DELETE SET NULL,
  report_id VARCHAR(80) NOT NULL,
  params NVARCHAR(2000) NOT NULL CONSTRAINT CK_rpt_run_params CHECK (ISJSON(params) = 1),
  principal_id INT NOT NULL,
  started_utc DATETIME2(3) NOT NULL,
  finished_utc DATETIME2(3) NULL,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_rpt_run_status CHECK (status IN ('running', 'ok', 'failed', 'held')),
  row_count INT NULL,
  file_ref NVARCHAR(200) NULL,
  error NVARCHAR(400) NULL);
CREATE INDEX IX_rpt_run_property ON rpt.run(property_id, run_id DESC);
