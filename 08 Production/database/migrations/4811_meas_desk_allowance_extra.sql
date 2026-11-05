-- HCC migration 4811 · F12 MEAS cap-desk: allowance reasons (extends the discount-reasons master) and per-booking extras
-- (booked room type kept when the assigned type differs; upgrade paid / free + approver). UPG / LCO1 / LCO2 folio lines use the existing add-line command.
CREATE TABLE meas.allowance_reason(
  property_id INT NOT NULL CONSTRAINT FK_meas_allowance_reason_property REFERENCES inv.property,
  code VARCHAR(24) NOT NULL,
  label NVARCHAR(60) NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_meas_allowance_reason_active DEFAULT 1,
  changed_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_allowance_reason_utc DEFAULT SYSUTCDATETIME(),
  changed_by INT NOT NULL,
  CONSTRAINT PK_meas_allowance_reason PRIMARY KEY (property_id, code));
GO
CREATE TABLE meas.booking_extra(
  property_id INT NOT NULL CONSTRAINT FK_meas_booking_extra_property REFERENCES inv.property,
  reservation_id BIGINT NOT NULL,
  booked_type NVARCHAR(40) NULL,
  upgrade VARCHAR(4) NULL CONSTRAINT CK_meas_booking_extra_upgrade CHECK (upgrade IS NULL OR upgrade IN ('paid','free')),
  upgrade_approver INT NULL,
  changed_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_booking_extra_utc DEFAULT SYSUTCDATETIME(),
  changed_by INT NOT NULL,
  CONSTRAINT PK_meas_booking_extra PRIMARY KEY (property_id, reservation_id));
GO
-- Audit trail of the extras (append-only): every set, including a waived late check-out (no folio line exists for it).
CREATE TABLE meas.booking_extra_log(
  log_id BIGINT IDENTITY CONSTRAINT PK_meas_booking_extra_log PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_booking_extra_log_property REFERENCES inv.property,
  client_id NVARCHAR(64) NOT NULL,
  reservation_id BIGINT NOT NULL,
  item VARCHAR(4) NULL CONSTRAINT CK_meas_booking_extra_log_item CHECK (item IS NULL OR item IN ('UPG','LCO1','LCO2')),
  waived BIT NOT NULL CONSTRAINT DF_meas_booking_extra_log_waived DEFAULT 0,
  waive_reason NVARCHAR(80) NULL,
  recorded_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_booking_extra_log_utc DEFAULT SYSUTCDATETIME(),
  recorded_by INT NOT NULL,
  CONSTRAINT UQ_meas_booking_extra_log_client UNIQUE (property_id, client_id));
