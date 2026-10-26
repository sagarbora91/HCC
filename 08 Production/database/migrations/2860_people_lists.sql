-- HCC migration 2860 · MA-6 people lists (block 2860–2869). Plan: 02 Planning/HCC_Customisation_Features_Plan_v1.md Part 2 §8 M19–M22 and §9
-- (masters framework). Prototype rules: 03 Prototype/src/domain/streams/people-masters.ts. Tables follow mst.floor (2801): code unique per
-- property (also for retired rows), name 1–60, sort order, active / retired_on, row_ver; the change history of every list goes to
-- mst.master_history (master = designations | shiftPatterns | leaveTypes | holidays). PERM-MST-001 / PERM-MST-002 (2802) guard the commands.
--
--   hr.designation   job titles: department, grade, default pay rule (pay field, Owner). hr.staff gets designation_code (the printed name stays in designation).
--   hr.shift_pattern duty times, unpaid break, night flag, weekly-off rule (fixed | roster). hr.staff.default_shift and hr.roster.shift_code take any pattern code (1–3 characters, or OFF).
--   hr.leave_type    paid, yearly quota (null = not tracked), carry-forward days, encashment. hr.leave_request.leave_type takes any code (1–6 characters).
--   hr.holiday       one holiday per date (code = YYYY-MM-DD), kind paid | restricted.
--   hr.people_list_seed  a property is seeded once (shift patterns M E N G, the six leave types with the stored policy's CL / SL / EL days, one designation per staff title);
--                        a property with no rows keeps working with the built-in defaults.
CREATE TABLE hr.people_list_seed(
  property_id INT NOT NULL CONSTRAINT PK_people_list_seed PRIMARY KEY CONSTRAINT FK_people_list_seed_property REFERENCES inv.property,
  seeded_utc DATETIME2(3) NOT NULL);

CREATE TABLE hr.designation(
  designation_id INT IDENTITY CONSTRAINT PK_designation PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_designation_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  dept_code VARCHAR(10) NULL,
  grade NVARCHAR(20) NULL,
  default_pay_rule VARCHAR(20) NULL CONSTRAINT CK_designation_pay_rule CHECK (default_pay_rule IN ('monthly-structured', 'monthly-basic', 'daily')),
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_designation_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_designation_code UNIQUE (property_id, code),
  CONSTRAINT FK_designation_department FOREIGN KEY (property_id, dept_code) REFERENCES hr.department(property_id, dept_code),
  CONSTRAINT CK_designation_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_designation_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_designation_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_designation_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_designation_order ON hr.designation(property_id, sort_order);

CREATE TABLE hr.shift_pattern(
  shift_pattern_id INT IDENTITY CONSTRAINT PK_shift_pattern PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_shift_pattern_property REFERENCES inv.property,
  code VARCHAR(3) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  start_time TIME(0) NOT NULL,
  end_time TIME(0) NOT NULL,
  break_minutes SMALLINT NOT NULL CONSTRAINT CK_shift_pattern_break CHECK (break_minutes BETWEEN 0 AND 240),
  night BIT NOT NULL,
  weekly_off_rule VARCHAR(6) NOT NULL CONSTRAINT CK_shift_pattern_rule CHECK (weekly_off_rule IN ('fixed', 'roster')),
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_shift_pattern_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_shift_pattern_code UNIQUE (property_id, code),
  CONSTRAINT CK_shift_pattern_code CHECK (LEN(code) BETWEEN 1 AND 3 AND code NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN AND code <> 'OFF'),
  CONSTRAINT CK_shift_pattern_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_shift_pattern_times CHECK (start_time <> end_time),
  CONSTRAINT CK_shift_pattern_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_shift_pattern_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_shift_pattern_order ON hr.shift_pattern(property_id, sort_order);

CREATE TABLE hr.leave_type(
  leave_type_id INT IDENTITY CONSTRAINT PK_leave_type PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_leave_type_property REFERENCES inv.property,
  code VARCHAR(6) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  paid BIT NOT NULL,
  yearly_quota TINYINT NULL CONSTRAINT CK_leave_type_quota CHECK (yearly_quota <= 60),
  carry_forward TINYINT NOT NULL CONSTRAINT DF_leave_type_carry DEFAULT 0 CONSTRAINT CK_leave_type_carry CHECK (carry_forward <= 60),
  encashment BIT NOT NULL CONSTRAINT DF_leave_type_encash DEFAULT 0,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_leave_type_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_leave_type_code UNIQUE (property_id, code),
  CONSTRAINT CK_leave_type_code CHECK (LEN(code) BETWEEN 1 AND 6 AND code NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_leave_type_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_leave_type_rules CHECK (carry_forward = 0 OR yearly_quota IS NOT NULL),
  CONSTRAINT CK_leave_type_unpaid CHECK (paid = 1 OR (ISNULL(yearly_quota, 0) = 0 AND carry_forward = 0 AND encashment = 0)),
  CONSTRAINT CK_leave_type_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_leave_type_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_leave_type_order ON hr.leave_type(property_id, sort_order);

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
-- Staff, roster and leave take the list codes: widen the old fixed sets (M E N G / CL SL EL CO ML LOP) to any code of the lists.
ALTER TABLE hr.staff DROP CONSTRAINT CK_staff_shift;
ALTER TABLE hr.staff ALTER COLUMN default_shift VARCHAR(3) NOT NULL;
ALTER TABLE hr.staff ADD designation_code VARCHAR(20) NULL;
GO
ALTER TABLE hr.staff ADD CONSTRAINT CK_staff_shift CHECK (LEN(default_shift) BETWEEN 1 AND 3 AND default_shift NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN AND default_shift <> 'OFF');
ALTER TABLE hr.staff ADD CONSTRAINT CK_staff_designation_code CHECK (designation_code IS NULL OR (LEN(designation_code) BETWEEN 1 AND 20 AND designation_code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN));
ALTER TABLE hr.roster DROP CONSTRAINT CK_roster_shift;
ALTER TABLE hr.roster ADD CONSTRAINT CK_roster_shift CHECK (LEN(shift_code) BETWEEN 1 AND 3 AND shift_code NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN);
ALTER TABLE hr.leave_request DROP CONSTRAINT CK_leave_type;
DROP INDEX IX_leave_staff ON hr.leave_request;
ALTER TABLE hr.leave_request ALTER COLUMN leave_type VARCHAR(6) NOT NULL;
CREATE INDEX IX_leave_staff ON hr.leave_request(staff_id, from_date) INCLUDE (to_date, status, leave_type, half, days_x2);
ALTER TABLE hr.leave_request ADD CONSTRAINT CK_leave_type CHECK (LEN(leave_type) BETWEEN 1 AND 6 AND leave_type NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_BIN);
