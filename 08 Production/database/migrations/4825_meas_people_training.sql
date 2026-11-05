-- HCC migration 4825 · F12 MEAS people captures (MEAS-CONTRACT §2): meas.training = training / certificate records (MEAS_TRAINING_SAVE, HR-001 gate).
-- The schema `meas` is shared by every MEAS capture lane; whoever runs first creates it. evidence_ref points into the LF documents store (HccDocuments);
-- the certificate-expiry alert (30 days) is a plat.reminder row written by the command, not a column here. Nothing about money.
IF SCHEMA_ID('meas') IS NULL EXEC('CREATE SCHEMA meas');
GO
CREATE TABLE meas.training(
  training_id BIGINT IDENTITY CONSTRAINT PK_meas_training PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_training_property REFERENCES inv.property,
  client_id UNIQUEIDENTIFIER NOT NULL,
  staff_id BIGINT NOT NULL CONSTRAINT FK_meas_training_staff REFERENCES hr.staff,
  course NVARCHAR(80) NOT NULL,
  trained_on DATE NOT NULL,
  hours DECIMAL(6, 2) NOT NULL,
  certificate_no NVARCHAR(40) NULL,
  valid_to DATE NULL,
  evidence_ref NVARCHAR(120) NULL,
  mandatory BIT NOT NULL CONSTRAINT DF_meas_training_mandatory DEFAULT 0,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_training_created DEFAULT SYSUTCDATETIME(),
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_meas_training_client UNIQUE (property_id, client_id),
  CONSTRAINT CK_meas_training_course CHECK (LEN(course) BETWEEN 2 AND 80),
  CONSTRAINT CK_meas_training_hours CHECK (hours > 0 AND hours <= 200),
  CONSTRAINT CK_meas_training_valid CHECK (valid_to IS NULL OR valid_to >= trained_on));
CREATE INDEX IX_meas_training_staff ON meas.training (property_id, staff_id, trained_on);
CREATE INDEX IX_meas_training_valid ON meas.training (property_id, valid_to) WHERE valid_to IS NOT NULL;
CREATE TABLE meas.training_audit(
  audit_id BIGINT IDENTITY CONSTRAINT PK_meas_training_audit PRIMARY KEY,
  training_id BIGINT NOT NULL,
  property_id INT NOT NULL,
  action VARCHAR(8) NOT NULL CONSTRAINT CK_meas_training_audit_action CHECK (action IN ('created', 'edited')),
  actor_id INT NOT NULL,
  at_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_training_audit_at DEFAULT SYSUTCDATETIME(),
  detail NVARCHAR(300) NULL);
