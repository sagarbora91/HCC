-- HCC migration 4400 · F7-R1 report module (F7-CONTRACT §3, RPT-ARCHITECTURE §2). The catalogue itself is code (Hcc.Reporting ReportCatalogue);
-- this table only lets the Owner switch a report off or leave a note against it. No money, no guest data.
CREATE SCHEMA rpt;
GO
CREATE TABLE rpt.catalogue_override(
  property_id INT NOT NULL CONSTRAINT FK_rpt_catalogue_override_property REFERENCES inv.property,
  report_id VARCHAR(80) NOT NULL,
  enabled BIT NOT NULL CONSTRAINT DF_rpt_catalogue_override_enabled DEFAULT 1,
  owner_note NVARCHAR(200) NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_rpt_catalogue_override_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_rpt_catalogue_override PRIMARY KEY (property_id, report_id),
  CONSTRAINT CK_rpt_catalogue_override_id CHECK (report_id LIKE 'rpt.%'));
