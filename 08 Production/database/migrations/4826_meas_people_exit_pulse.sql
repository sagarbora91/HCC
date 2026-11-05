-- HCC migration 4826 · F12 MEAS people captures: meas.staff_exit (MEAS_EXIT_SET, reason from a fixed list + date; one row per staff, the staff and payroll rows
-- are NOT changed) and meas.pulse (MEAS_PULSE_ADD, anonymous five-question form).
-- ANONYMITY: meas.pulse has NO staff, user or actor column on purpose, and no audit table; client_id is a random id made by the form for idempotency only.
-- The department is chosen on the form. Report lanes show a department only when it has at least three answers in the quarter.
CREATE TABLE meas.staff_exit(
  property_id INT NOT NULL CONSTRAINT FK_meas_staff_exit_property REFERENCES inv.property,
  staff_id BIGINT NOT NULL CONSTRAINT FK_meas_staff_exit_staff REFERENCES hr.staff,
  reason NVARCHAR(30) NOT NULL,
  exit_date DATE NOT NULL,
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_staff_exit_set DEFAULT SYSUTCDATETIME(),
  row_ver ROWVERSION,
  CONSTRAINT PK_meas_staff_exit PRIMARY KEY (property_id, staff_id),
  CONSTRAINT CK_meas_staff_exit_reason CHECK (reason IN (N'Resigned', N'Better pay', N'Relocated', N'Personal / family', N'Studies', N'Health', N'Dismissed',
    N'End of contract', N'Absconded', N'Retired', N'Other')));
CREATE TABLE meas.pulse(
  pulse_id BIGINT IDENTITY CONSTRAINT PK_meas_pulse PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_pulse_property REFERENCES inv.property,
  client_id UNIQUEIDENTIFIER NOT NULL,
  quarter_key CHAR(10) NOT NULL,
  dept_code VARCHAR(10) NOT NULL,
  workload TINYINT NOT NULL,
  respect TINYINT NOT NULL,
  tools TINYINT NOT NULL,
  pay_fairness TINYINT NOT NULL,
  recommend TINYINT NOT NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_pulse_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT UQ_meas_pulse_client UNIQUE (property_id, client_id),
  CONSTRAINT CK_meas_pulse_quarter CHECK (quarter_key LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]-Q[1-4]'),
  CONSTRAINT CK_meas_pulse_scores CHECK (workload BETWEEN 1 AND 5 AND respect BETWEEN 1 AND 5 AND tools BETWEEN 1 AND 5 AND pay_fairness BETWEEN 1 AND 5 AND recommend BETWEEN 0 AND 10));
CREATE INDEX IX_meas_pulse_quarter ON meas.pulse (property_id, quarter_key, dept_code);
