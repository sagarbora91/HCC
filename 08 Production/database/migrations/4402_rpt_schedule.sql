-- HCC migration 4402 · F7-R1 scheduled report runs (RPT-ARCHITECTURE §5, catalogue §1.9). A schedule runs as its owner's role (it never widens what
-- that role may see); money reports go only to Owner / Manager / Accountant recipients (checked by the application). params = JSON with relative
-- date tokens (range = yesterday | month-to-date, or from / to = today | yesterday | month-start). Time is India time (HH:mm).
CREATE TABLE rpt.schedule(
  schedule_id BIGINT IDENTITY CONSTRAINT PK_rpt_schedule PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_rpt_schedule_property REFERENCES inv.property,
  report_id VARCHAR(80) NOT NULL,
  params NVARCHAR(2000) NOT NULL CONSTRAINT CK_rpt_schedule_params CHECK (ISJSON(params) = 1),
  format VARCHAR(8) NOT NULL CONSTRAINT CK_rpt_schedule_format CHECK (format IN ('xlsx', 'pdf', 'csv')),
  cadence VARCHAR(8) NOT NULL CONSTRAINT CK_rpt_schedule_cadence CHECK (cadence IN ('daily', 'weekly', 'monthly')),
  cadence_day TINYINT NULL,
  run_time TIME(0) NOT NULL,
  owner_principal_id INT NOT NULL,
  recipients NVARCHAR(400) NOT NULL CONSTRAINT CK_rpt_schedule_recipients CHECK (ISJSON(recipients) = 1),
  enabled BIT NOT NULL CONSTRAINT DF_rpt_schedule_enabled DEFAULT 1,
  last_run_utc DATETIME2(3) NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_rpt_schedule_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_rpt_schedule_day CHECK ((cadence = 'daily' AND cadence_day IS NULL) OR (cadence = 'weekly' AND cadence_day BETWEEN 0 AND 6)
    OR (cadence = 'monthly' AND cadence_day BETWEEN 1 AND 28)));
CREATE INDEX IX_rpt_schedule_property ON rpt.schedule(property_id, enabled);
