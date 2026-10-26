-- HCC migration 3800 · Holiday calendar = one hr.holiday (masters integration of MA-6 2860 and Wave B 3400). Fictional data only.
-- Masters integration (MA-6 + Wave B, 26 Oct 2026): ONE holidays table. Wave B's 3400 created hr.holiday (property, date, name; DEC-083 paid
-- holidays, nothing seeded); MA-6 (2860, M22) designed the Holiday calendar master (code = YYYY-MM-DD, kind paid | restricted, sort order,
-- retire, row version, mst.master_history). This migration rebuilds hr.holiday in the master shape and keeps every Wave B row as an active
-- paid holiday, so payroll (DEC-083: an unmarked paid holiday counts like a weekly off), leave days and compliance due dates read one list.
-- Wave B's commands keep working on it as SavePaidHoliday / RemoveHoliday; the master commands are SaveHoliday / RetireHoliday / … (PERM-MST-002).
SELECT property_id, holiday_date, name, set_by, set_utc INTO hr.holiday_wave_b FROM hr.holiday;
DROP TABLE hr.holiday;
GO
CREATE TABLE hr.holiday(
  holiday_id INT IDENTITY CONSTRAINT PK_holiday PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_holiday_property REFERENCES inv.property,
  code VARCHAR(10) NOT NULL,   -- the date, YYYY-MM-DD (one holiday per date, retired rows keep theirs)
  name NVARCHAR(60) NOT NULL,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_holiday_kind CHECK (kind IN ('paid', 'restricted')),
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_holiday_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_holiday_code UNIQUE (property_id, code),
  CONSTRAINT CK_holiday_code CHECK (code LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]-[0-3][0-9]' AND TRY_CONVERT(DATE, code, 23) IS NOT NULL),
  CONSTRAINT CK_holiday_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_holiday_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_holiday_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_holiday_order ON hr.holiday(property_id, sort_order);
GO
INSERT hr.holiday(property_id, code, name, kind, sort_order, active, retired_on, updated_by, updated_utc)
SELECT property_id, CONVERT(VARCHAR(10), holiday_date, 23), name, 'paid', 10 * ROW_NUMBER() OVER (PARTITION BY property_id ORDER BY holiday_date), 1, NULL, set_by, set_utc
FROM hr.holiday_wave_b;
DROP TABLE hr.holiday_wave_b;
GO
