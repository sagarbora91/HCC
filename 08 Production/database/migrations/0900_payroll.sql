-- HCC migration 0900 · payroll and leave (W2-2, Wave 2 block 0900–0999). Module 17 spec + prototype staff.ts after the W05-B review
-- fixes (W05B_Payroll_Code_Review_2026-10-25.md F-01…F-17): staff master, roster, attendance, leave + policy, advances and recoveries,
-- DATED statutory rate versions with history (F-05/F-06, CA question pack: rates are dated settings, CA to confirm), pay-runs with a
-- maker-checker lock, the bank snapshot taken at lock (F-02), payslip sends / bank files / unlocks per lock (F-09), full and final
-- settlement as maker-checker (F-12, DEC-066). Original HCC design. Money = BIGINT paise; dates DATE; event times DATETIME2(3) UTC written
-- by the application from its TimeProvider. Every write goes through the command runner (idempotency, audit, outbox).
CREATE SCHEMA hr;
GO
-- One row per property: payroll money commands (rates, runs, lock/unlock, advances, F&F) serialize on it (UPDLOCK).
CREATE TABLE hr.payroll_anchor(
  property_id INT NOT NULL CONSTRAINT PK_payroll_anchor PRIMARY KEY CONSTRAINT FK_payroll_anchor_property REFERENCES inv.property,
  row_ver ROWVERSION);
CREATE TABLE hr.department(
  property_id INT NOT NULL CONSTRAINT FK_department_property REFERENCES inv.property,
  dept_code VARCHAR(10) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  leave_cap TINYINT NULL CONSTRAINT CK_department_cap CHECK (leave_cap BETWEEN 1 AND 20),   -- max staff off per day (NULL = policy default)
  row_ver ROWVERSION,
  CONSTRAINT PK_department PRIMARY KEY (property_id, dept_code),
  CONSTRAINT CK_department_code CHECK (LEN(dept_code) >= 2 AND dept_code NOT LIKE '%[^A-Z0-9]%'));
CREATE TABLE hr.staff(
  staff_id BIGINT IDENTITY CONSTRAINT PK_staff PRIMARY KEY,
  property_id INT NOT NULL,
  name NVARCHAR(100) NOT NULL,
  dept_code VARCHAR(10) NOT NULL,
  designation NVARCHAR(80) NOT NULL,
  user_id INT NULL CONSTRAINT FK_staff_user REFERENCES plat.app_user,   -- the login whose "own" payslips these are (F-03: Owner links)
  pay_type VARCHAR(7) NOT NULL CONSTRAINT CK_staff_pay_type CHECK (pay_type IN ('monthly','daily')),
  monthly_paise BIGINT NOT NULL CONSTRAINT CK_staff_monthly CHECK (monthly_paise BETWEEN 0 AND 100000000),
  daily_paise BIGINT NOT NULL CONSTRAINT CK_staff_daily CHECK (daily_paise BETWEEN 0 AND 100000000),
  default_shift CHAR(1) NOT NULL CONSTRAINT CK_staff_shift CHECK (default_shift IN ('M','E','N','G')),
  weekly_off TINYINT NOT NULL CONSTRAINT CK_staff_weekly_off CHECK (weekly_off BETWEEN 0 AND 6),   -- 0 = Sunday
  joined DATE NOT NULL,
  exited_on DATE NULL,
  active BIT NOT NULL,
  phone VARCHAR(20) NOT NULL,          -- payslip WhatsApp number (F-11: payroll field)
  gender CHAR(1) NULL CONSTRAINT CK_staff_gender CHECK (gender IN ('M','F')),             -- PT slab (F-11: payroll field)
  structure CHAR(1) NULL CONSTRAINT CK_staff_structure CHECK (structure IN ('S','B')),
  pf_applicable BIT NULL,
  esic_applicable BIT NULL,
  bank_name NVARCHAR(80) NULL,
  account_no VARCHAR(18) NULL CONSTRAINT CK_staff_account CHECK (account_no NOT LIKE '%[^0-9]%' AND LEN(account_no) >= 6),
  ifsc CHAR(11) NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT FK_staff_department FOREIGN KEY (property_id, dept_code) REFERENCES hr.department(property_id, dept_code),
  CONSTRAINT CK_staff_exit CHECK ((active = 1 AND exited_on IS NULL) OR (active = 0 AND exited_on IS NOT NULL AND exited_on >= joined)));
CREATE UNIQUE INDEX UX_staff_user ON hr.staff(user_id) WHERE user_id IS NOT NULL;
CREATE INDEX IX_staff_property ON hr.staff(property_id, dept_code);
-- Roster overrides (effective shift = override → weekly off → default shift). Past roster is history (business date or later only).
CREATE TABLE hr.roster(
  staff_id BIGINT NOT NULL CONSTRAINT FK_roster_staff REFERENCES hr.staff,
  work_date DATE NOT NULL,
  shift_code VARCHAR(3) NOT NULL CONSTRAINT CK_roster_shift CHECK (shift_code IN ('M','E','N','G','OFF')),
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_roster PRIMARY KEY (staff_id, work_date));
-- Attendance on the business date (self check-in or a manager's mark; never your own correction, F-10).
CREATE TABLE hr.attendance(
  staff_id BIGINT NOT NULL CONSTRAINT FK_attendance_staff REFERENCES hr.staff,
  work_date DATE NOT NULL,
  status VARCHAR(3) NOT NULL CONSTRAINT CK_attendance_status CHECK (status IN ('P','L','H','A','OFF')),
  ot_minutes SMALLINT NOT NULL CONSTRAINT CK_attendance_ot CHECK (ot_minutes BETWEEN 0 AND 480),
  marked_by INT NOT NULL,
  marked_utc DATETIME2(3) NOT NULL,
  note NVARCHAR(120) NULL,
  CONSTRAINT PK_attendance PRIMARY KEY (staff_id, work_date),
  CONSTRAINT CK_attendance_absent_ot CHECK (status <> 'A' OR ot_minutes = 0));
CREATE TABLE hr.leave_request(
  leave_id BIGINT IDENTITY CONSTRAINT PK_leave_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_leave_property REFERENCES inv.property,
  staff_id BIGINT NOT NULL CONSTRAINT FK_leave_staff REFERENCES hr.staff,
  leave_type VARCHAR(3) NOT NULL CONSTRAINT CK_leave_type CHECK (leave_type IN ('CL','SL','EL','CO','ML','LOP')),
  from_date DATE NOT NULL,
  to_date DATE NOT NULL,
  half CHAR(2) NULL CONSTRAINT CK_leave_half CHECK (half IN ('AM','PM')),
  days_x2 SMALLINT NOT NULL CONSTRAINT CK_leave_days CHECK (days_x2 BETWEEN 1 AND 60),   -- half-day units (weekly offs skipped)
  reason NVARCHAR(200) NOT NULL,
  status VARCHAR(9) NOT NULL CONSTRAINT CK_leave_status CHECK (status IN ('Pending','Approved','Rejected','Cancelled')),
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  note NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_leave_dates CHECK (to_date >= from_date AND (half IS NULL OR to_date = from_date)),
  CONSTRAINT CK_leave_not_self CHECK (decided_by IS NULL OR status = 'Cancelled' OR decided_by <> requested_by));
CREATE INDEX IX_leave_staff ON hr.leave_request(staff_id, from_date) INCLUDE (to_date, status, leave_type, half, days_x2);
CREATE INDEX IX_leave_property_dates ON hr.leave_request(property_id, from_date, to_date) WHERE status IN ('Pending','Approved');
-- Leave policy (not dated: applies to new requests; SCC ENT_DEFAULTS + MAX_DAY). Department overrides on hr.department.leave_cap.
CREATE TABLE hr.leave_policy(
  property_id INT NOT NULL CONSTRAINT PK_leave_policy PRIMARY KEY CONSTRAINT FK_leave_policy_property REFERENCES inv.property,
  cl_days TINYINT NOT NULL CONSTRAINT CK_leave_policy_cl CHECK (cl_days <= 60),
  sl_days TINYINT NOT NULL CONSTRAINT CK_leave_policy_sl CHECK (sl_days <= 60),
  el_days TINYINT NOT NULL CONSTRAINT CK_leave_policy_el CHECK (el_days <= 60),
  default_cap TINYINT NOT NULL CONSTRAINT CK_leave_policy_cap CHECK (default_cap BETWEEN 1 AND 20),
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL);
-- Dated statutory rate versions (effective from the 1st of a month). The JSON is a full validated PayrollRates snapshot; a pay-run copies
-- the version in force into its own row, so locked runs never change.
CREATE TABLE hr.rate_version(
  property_id INT NOT NULL CONSTRAINT FK_rate_version_property REFERENCES inv.property,
  effective_from DATE NOT NULL,
  rates_json NVARCHAR(2000) NOT NULL CONSTRAINT CK_rate_version_json CHECK (ISJSON(rates_json) = 1),
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_rate_version PRIMARY KEY (property_id, effective_from),
  CONSTRAINT CK_rate_version_first CHECK (DAY(effective_from) = 1));
-- Append-only history of rate saves (who, when, what changed, what was carried into later versions) for review and revert.
CREATE TABLE hr.rate_change(
  change_id BIGINT IDENTITY CONSTRAINT PK_rate_change PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_rate_change_property REFERENCES inv.property,
  effective_from DATE NOT NULL,
  before_json NVARCHAR(2000) NULL CONSTRAINT CK_rate_change_before CHECK (before_json IS NULL OR ISJSON(before_json) = 1),
  after_json NVARCHAR(2000) NOT NULL CONSTRAINT CK_rate_change_after CHECK (ISJSON(after_json) = 1),
  changed_fields NVARCHAR(400) NOT NULL,
  carried NVARCHAR(1000) NOT NULL,
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL);
CREATE TABLE hr.advance(
  advance_id BIGINT IDENTITY CONSTRAINT PK_advance PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_advance_property REFERENCES inv.property,
  staff_id BIGINT NOT NULL CONSTRAINT FK_advance_staff REFERENCES hr.staff,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_advance_amount CHECK (amount_paise > 0 AND amount_paise <= 100000000),
  installment_paise BIGINT NOT NULL,   -- 0 = recover the full balance in the first month (SCC emi 0)
  recovered_paise BIGINT NOT NULL CONSTRAINT DF_advance_recovered DEFAULT 0,
  advance_date DATE NOT NULL,
  from_month DATE NOT NULL CONSTRAINT CK_advance_from CHECK (DAY(from_month) = 1),
  reason NVARCHAR(200) NOT NULL,
  given_by INT NOT NULL,
  given_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_advance_installment CHECK (installment_paise BETWEEN 0 AND amount_paise),
  CONSTRAINT CK_advance_recovered CHECK (recovered_paise BETWEEN 0 AND amount_paise));
-- One open advance per staff member is enforced by the application under the payroll anchor lock.
CREATE INDEX IX_advance_staff ON hr.advance(staff_id) INCLUDE (amount_paise, recovered_paise);
CREATE TABLE hr.pay_run(
  run_id BIGINT IDENTITY CONSTRAINT PK_pay_run PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_pay_run_property REFERENCES inv.property,
  pay_month DATE NOT NULL CONSTRAINT CK_pay_run_month CHECK (DAY(pay_month) = 1),
  status VARCHAR(6) NOT NULL CONSTRAINT CK_pay_run_status CHECK (status IN ('Draft','Locked')),
  rates_json NVARCHAR(2000) NOT NULL CONSTRAINT CK_pay_run_rates CHECK (ISJSON(rates_json) = 1),
  warnings_json NVARCHAR(MAX) NOT NULL CONSTRAINT CK_pay_run_warnings CHECK (ISJSON(warnings_json) = 1),
  lock_seq INT NOT NULL CONSTRAINT DF_pay_run_lock_seq DEFAULT 0,   -- number of the current (or last) lock; sends/files/bank snapshot belong to one lock
  prepared_by INT NOT NULL,
  prepared_utc DATETIME2(3) NOT NULL,
  locked_by INT NULL,
  locked_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_pay_run_month UNIQUE (property_id, pay_month),
  CONSTRAINT CK_pay_run_lock CHECK ((status = 'Draft' AND locked_by IS NULL) OR (status = 'Locked' AND locked_by IS NOT NULL AND locked_by <> prepared_by AND lock_seq > 0)));
CREATE TABLE hr.pay_run_line(
  run_id BIGINT NOT NULL CONSTRAINT FK_pay_run_line_run REFERENCES hr.pay_run,
  staff_id BIGINT NOT NULL CONSTRAINT FK_pay_run_line_staff REFERENCES hr.staff,
  line_json NVARCHAR(4000) NOT NULL CONSTRAINT CK_pay_run_line_json CHECK (ISJSON(line_json) = 1),
  earned_paise BIGINT NOT NULL,
  gross_paise BIGINT NOT NULL,
  pf_paise BIGINT NOT NULL,
  pf_er_paise BIGINT NOT NULL,
  esic_paise BIGINT NOT NULL,
  esic_er_paise BIGINT NOT NULL,
  pt_paise BIGINT NOT NULL,
  advance_paise BIGINT NOT NULL,
  net_paise BIGINT NOT NULL,
  esic_covered BIT NULL,
  CONSTRAINT PK_pay_run_line PRIMARY KEY (run_id, staff_id),
  CONSTRAINT CK_pay_run_line_money CHECK (earned_paise >= 0 AND gross_paise >= earned_paise AND net_paise <= gross_paise));
-- F-02: bank details frozen at each lock; the bank file is built from this copy, never from the live staff master.
CREATE TABLE hr.pay_run_bank(
  run_id BIGINT NOT NULL CONSTRAINT FK_pay_run_bank_run REFERENCES hr.pay_run,
  lock_seq INT NOT NULL,
  staff_id BIGINT NOT NULL CONSTRAINT FK_pay_run_bank_staff REFERENCES hr.staff,
  bank_name NVARCHAR(80) NOT NULL,
  account_no VARCHAR(18) NOT NULL,
  ifsc VARCHAR(11) NOT NULL,
  CONSTRAINT PK_pay_run_bank PRIMARY KEY (run_id, lock_seq, staff_id));
-- F-09: bank transfer files issued (simulated, nothing is paid by HCC) per lock; a repeat needs confirmation.
CREATE TABLE hr.bank_file(
  run_id BIGINT NOT NULL CONSTRAINT FK_bank_file_run REFERENCES hr.pay_run,
  issue_no INT NOT NULL,
  lock_seq INT NOT NULL,
  net_paise BIGINT NOT NULL,
  issued_by INT NOT NULL,
  issued_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_bank_file PRIMARY KEY (run_id, issue_no));
-- Simulated WhatsApp payslip sends (DEC-056); sends of an earlier lock are superseded after an unlock (F-09).
CREATE TABLE hr.payslip_send(
  send_id BIGINT IDENTITY CONSTRAINT PK_payslip_send PRIMARY KEY,
  run_id BIGINT NOT NULL CONSTRAINT FK_payslip_send_run REFERENCES hr.pay_run,
  lock_seq INT NOT NULL,
  staff_id BIGINT NOT NULL CONSTRAINT FK_payslip_send_staff REFERENCES hr.staff,
  to_number VARCHAR(12) NOT NULL,
  net_paise BIGINT NOT NULL,
  sent_by INT NOT NULL,
  sent_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_payslip_send_run ON hr.payslip_send(run_id, staff_id, lock_seq);
-- Owner unlocks (SCC unlockRun): reason and the net figures of the lock that was undone.
CREATE TABLE hr.pay_run_unlock(
  run_id BIGINT NOT NULL CONSTRAINT FK_pay_run_unlock_run REFERENCES hr.pay_run,
  lock_seq INT NOT NULL,
  reason NVARCHAR(200) NOT NULL,
  nets_json NVARCHAR(MAX) NOT NULL CONSTRAINT CK_pay_run_unlock_nets CHECK (ISJSON(nets_json) = 1),
  bank_files INT NOT NULL,
  unlocked_by INT NOT NULL,
  unlocked_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_pay_run_unlock PRIMARY KEY (run_id, lock_seq));
CREATE TABLE hr.fnf(
  fnf_id BIGINT IDENTITY CONSTRAINT PK_fnf PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_fnf_property REFERENCES inv.property,
  staff_id BIGINT NOT NULL CONSTRAINT FK_fnf_staff REFERENCES hr.staff,
  last_day DATE NOT NULL,
  pay_month DATE NOT NULL,
  salary_included BIT NOT NULL,
  line_json NVARCHAR(4000) NULL CONSTRAINT CK_fnf_line CHECK (line_json IS NULL OR ISJSON(line_json) = 1),
  rates_json NVARCHAR(2000) NOT NULL CONSTRAINT CK_fnf_rates CHECK (ISJSON(rates_json) = 1),
  advance_outstanding_paise BIGINT NOT NULL,
  adjustments_json NVARCHAR(2000) NOT NULL CONSTRAINT CK_fnf_adjustments CHECK (ISJSON(adjustments_json) = 1),
  additions_paise BIGINT NOT NULL,
  earnings_paise BIGINT NOT NULL,
  recoveries_paise BIGINT NOT NULL,
  net_paise BIGINT NOT NULL,            -- may be negative: recoverable from the employee
  status VARCHAR(9) NOT NULL CONSTRAINT CK_fnf_status CHECK (status IN ('Draft','Settled','Discarded')),
  prepared_by INT NOT NULL,
  prepared_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_fnf_checker CHECK (status = 'Draft' OR (decided_by IS NOT NULL AND (status = 'Discarded' OR decided_by <> prepared_by))));
CREATE UNIQUE INDEX UX_fnf_staff_active ON hr.fnf(staff_id) WHERE status IN ('Draft','Settled');
-- Advance recoveries by a locked run or a settled F&F; an unlock marks the run's recoveries reversed (history kept).
CREATE TABLE hr.advance_recovery(
  recovery_id BIGINT IDENTITY CONSTRAINT PK_advance_recovery PRIMARY KEY,
  advance_id BIGINT NOT NULL CONSTRAINT FK_advance_recovery_advance REFERENCES hr.advance,
  run_id BIGINT NULL CONSTRAINT FK_advance_recovery_run REFERENCES hr.pay_run,
  fnf_id BIGINT NULL CONSTRAINT FK_advance_recovery_fnf REFERENCES hr.fnf,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_advance_recovery_amount CHECK (amount_paise > 0),
  recorded_utc DATETIME2(3) NOT NULL,
  reversed_by INT NULL,
  reversed_utc DATETIME2(3) NULL,
  CONSTRAINT CK_advance_recovery_source CHECK ((run_id IS NULL AND fnf_id IS NOT NULL) OR (run_id IS NOT NULL AND fnf_id IS NULL)));
CREATE INDEX IX_advance_recovery_run ON hr.advance_recovery(run_id) WHERE run_id IS NOT NULL;
