-- HCC migration 4110 · SCC leave rules in full (owner rule DEC-116; DEC-119 keeps HCC's safeguards: nobody approves their own leave, a removed
-- leave keeps an audit record, role permissions, staff by ID, past days through DEC-075 corrections). Source: SCC LeaveDesk (leave\index.html);
-- report 05 Delivery Evidence/SCC Port/SCC_Leave_Port_2026-10-27.md.
-- 1. hr.leave_request becomes ONE RECORD PER DATE (SCC data.leaves[date][]): from_date = to_date = the leave date, decided on its own. request_from /
--    request_to keep the range it was requested with (SCC leaveFrom / leaveTo); approver_id / approver_name = SCC approvedBy (a user, or an imported
--    SCC name); cancelled_by / cancelled_utc = SCC "remove" as a cancel with an audit record (the row stays); legacy / scc_id / decided_by_name for
--    records imported from an SCC backup (legacy = SCC record without a status = approved). The reason becomes optional (SCC: up to 500).
--    Old SPANS are split into one row per date they counted when they were stored — the staff member's rostered working days that were not paid
--    holidays (the old leave-day rule) — so balances and pay stay what they were; the first counted date keeps the row and its id; a span that
--    counted no date keeps its first date (no record is ever dropped).
-- 2. hr.leave_policy: SCC entitlements as decimals (any number ≥ 0, halves allowed, no maximum) + the hotel's leave week-off set (weekoff_mask: bit 0
--    = Sunday … bit 6 = Saturday; 0 = none, HCC's default — SCC's own default would be 65 = Saturday + Sunday). default_cap is no longer read.
-- 3. hr.department.leave_cap: SCC per-store caps, ADVISORY (whole number ≥ 0, no maximum; NULL = SCC MAX_DAY 3). The binding limit is SCC MAX_DAY:
--    3 leave records a date for the whole property (pending + approved), enforced by the RequestLeave command under the payroll anchor lock.
-- 4. hr.leave_type: decimal yearly quota (CL / SL / EL only — SCC TRACKED_CATS; the list is SCC's fixed six + LOP); carry-forward and encashment are
--    no longer rules (set to 0, columns kept for history); SCC's "Other" (OTH) is added to every stored list.
-- 5. New: hr.leave_ent_override (SCC perEmployee, by staff ID), hr.leave_blackout (blackouts and store holidays), hr.leave_agenda (agenda items).
ALTER TABLE hr.leave_request ADD request_from DATE NULL, request_to DATE NULL, approver_id INT NULL, approver_name NVARCHAR(100) NULL,
  cancelled_by INT NULL, cancelled_utc DATETIME2(3) NULL, legacy BIT NOT NULL CONSTRAINT DF_leave_legacy DEFAULT 0, scc_id NVARCHAR(30) NULL,
  decided_by_name NVARCHAR(100) NULL, decided_at_text NVARCHAR(30) NULL;
ALTER TABLE hr.leave_request ALTER COLUMN reason NVARCHAR(500) NULL;
GO
UPDATE hr.leave_request SET request_from = from_date, request_to = to_date;
-- The dates each multi-day span counted (old rule = PayrollRules.DayOff before this migration): not a rostered weekly off (a roster cell, else the staff
-- member's weekly_off unless the default shift's pattern leaves weekly offs to the roster), not an active paid holiday. Weekday 0 = Sunday whatever
-- DATEFIRST is (7 Jan 1900 was a Sunday).
;WITH n AS (SELECT TOP (4000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects a CROSS JOIN sys.all_objects b),
days AS (
  SELECT l.leave_id, l.property_id, l.staff_id, DATEADD(DAY, n.k, l.from_date) AS d
    FROM hr.leave_request l JOIN n ON n.k <= DATEDIFF(DAY, l.from_date, l.to_date)
   WHERE l.to_date > l.from_date),
counted AS (
  SELECT x.leave_id, x.d, ROW_NUMBER() OVER (PARTITION BY x.leave_id ORDER BY x.d) AS rn
    FROM days x JOIN hr.staff st ON st.staff_id = x.staff_id
    LEFT JOIN hr.roster r ON r.staff_id = x.staff_id AND r.work_date = x.d
    LEFT JOIN hr.shift_pattern sp ON sp.property_id = st.property_id AND sp.code = st.default_shift
   WHERE COALESCE(r.shift_code, CASE WHEN DATEDIFF(DAY, '19000107', x.d) % 7 = st.weekly_off AND ISNULL(sp.weekly_off_rule, 'fixed') <> 'roster' THEN 'OFF'
                                     ELSE st.default_shift END) <> 'OFF'
     AND NOT EXISTS (SELECT 1 FROM hr.holiday h WHERE h.property_id = x.property_id AND h.active = 1 AND h.kind = 'paid' AND h.code = CONVERT(VARCHAR(10), x.d, 23)))
SELECT leave_id, d, rn INTO #leave_split FROM counted;
INSERT hr.leave_request(property_id, staff_id, leave_type, from_date, to_date, half, days_x2, reason, status, requested_by, requested_utc, decided_by, decided_utc,
                        note, correction_id, request_from, request_to)
SELECT l.property_id, l.staff_id, l.leave_type, s.d, s.d, NULL, 2, l.reason, l.status, l.requested_by, l.requested_utc, l.decided_by, l.decided_utc,
       l.note, l.correction_id, l.request_from, l.request_to
  FROM #leave_split s JOIN hr.leave_request l ON l.leave_id = s.leave_id
 WHERE s.rn > 1
 ORDER BY l.leave_id, s.d;
UPDATE l SET from_date = s.d, to_date = s.d, days_x2 = 2 FROM hr.leave_request l JOIN #leave_split s ON s.leave_id = l.leave_id AND s.rn = 1;
UPDATE hr.leave_request SET to_date = from_date, days_x2 = CASE WHEN half IS NULL THEN 2 ELSE 1 END WHERE to_date > from_date;   -- a span that counted no date
DROP TABLE #leave_split;
GO
ALTER TABLE hr.leave_request ALTER COLUMN request_from DATE NOT NULL;
ALTER TABLE hr.leave_request ALTER COLUMN request_to DATE NOT NULL;
ALTER TABLE hr.leave_request DROP CONSTRAINT CK_leave_dates;
ALTER TABLE hr.leave_request DROP CONSTRAINT CK_leave_days;
GO
ALTER TABLE hr.leave_request ADD CONSTRAINT CK_leave_one_date CHECK (to_date = from_date);
ALTER TABLE hr.leave_request ADD CONSTRAINT CK_leave_days CHECK (days_x2 = CASE WHEN half IS NULL THEN 2 ELSE 1 END);   -- SCC leaveDaysOf: 1 / 0.5
ALTER TABLE hr.leave_request ADD CONSTRAINT CK_leave_request_range CHECK (request_from <= request_to);
ALTER TABLE hr.leave_request ADD CONSTRAINT CK_leave_cancelled CHECK ((status = 'Cancelled') OR (cancelled_by IS NULL AND cancelled_utc IS NULL));
ALTER TABLE hr.leave_request ADD CONSTRAINT CK_leave_approver CHECK (approver_name IS NULL OR LEN(approver_name) BETWEEN 1 AND 100);
CREATE INDEX IX_leave_property_day ON hr.leave_request(property_id, from_date) INCLUDE (staff_id, half, status, leave_type);
GO
-- 2. Leave settings: decimal entitlements (SCC: any number ≥ 0, no maximum) and the leave week-off set.
ALTER TABLE hr.leave_policy DROP CONSTRAINT CK_leave_policy_cl;
ALTER TABLE hr.leave_policy DROP CONSTRAINT CK_leave_policy_sl;
ALTER TABLE hr.leave_policy DROP CONSTRAINT CK_leave_policy_el;
GO
ALTER TABLE hr.leave_policy ALTER COLUMN cl_days DECIMAL(9,2) NOT NULL;
ALTER TABLE hr.leave_policy ALTER COLUMN sl_days DECIMAL(9,2) NOT NULL;
ALTER TABLE hr.leave_policy ALTER COLUMN el_days DECIMAL(9,2) NOT NULL;
ALTER TABLE hr.leave_policy ADD weekoff_mask TINYINT NOT NULL CONSTRAINT DF_leave_policy_weekoff DEFAULT 0;
GO
ALTER TABLE hr.leave_policy ADD CONSTRAINT CK_leave_policy_cl CHECK (cl_days >= 0);
ALTER TABLE hr.leave_policy ADD CONSTRAINT CK_leave_policy_sl CHECK (sl_days >= 0);
ALTER TABLE hr.leave_policy ADD CONSTRAINT CK_leave_policy_el CHECK (el_days >= 0);
ALTER TABLE hr.leave_policy ADD CONSTRAINT CK_leave_policy_weekoff CHECK (weekoff_mask BETWEEN 0 AND 127);
-- 3. Department caps: SCC per-store caps, advisory.
ALTER TABLE hr.department DROP CONSTRAINT CK_department_cap;
GO
ALTER TABLE hr.department ALTER COLUMN leave_cap INT NULL;
GO
ALTER TABLE hr.department ADD CONSTRAINT CK_department_cap CHECK (leave_cap >= 0);
-- 4. Leave types: SCC's fixed categories; decimal quota; no carry-forward or encashment rule.
ALTER TABLE hr.leave_type DROP CONSTRAINT CK_leave_type_quota;
ALTER TABLE hr.leave_type DROP CONSTRAINT CK_leave_type_rules;
ALTER TABLE hr.leave_type DROP CONSTRAINT CK_leave_type_unpaid;
GO
ALTER TABLE hr.leave_type ALTER COLUMN yearly_quota DECIMAL(9,2) NULL;
GO
UPDATE hr.leave_type SET carry_forward = 0, encashment = 0 WHERE carry_forward <> 0 OR encashment <> 0;
ALTER TABLE hr.leave_type ADD CONSTRAINT CK_leave_type_quota CHECK (yearly_quota >= 0);
ALTER TABLE hr.leave_type ADD CONSTRAINT CK_leave_type_unpaid CHECK (paid = 1 OR ISNULL(yearly_quota, 0) = 0);
ALTER TABLE hr.leave_type ADD CONSTRAINT CK_leave_type_no_carry CHECK (carry_forward = 0 AND encashment = 0);
INSERT hr.leave_type(property_id, code, name, paid, yearly_quota, carry_forward, encashment, sort_order, active, retired_on, updated_by, updated_utc)
SELECT t.property_id, 'OTH', N'Other', 1, NULL, 0, 0, MAX(t.sort_order) + 10, 1, NULL, 0, SYSUTCDATETIME()
  FROM hr.leave_type t
 WHERE NOT EXISTS (SELECT 1 FROM hr.leave_type o WHERE o.property_id = t.property_id AND o.code = 'OTH')
 GROUP BY t.property_id;
GO
-- 5. SCC perEmployee entitlement overrides (by staff ID): a NULL category uses the default.
CREATE TABLE hr.leave_ent_override(
  property_id INT NOT NULL CONSTRAINT FK_leave_override_property REFERENCES inv.property,
  staff_id BIGINT NOT NULL CONSTRAINT FK_leave_override_staff REFERENCES hr.staff,
  cl_days DECIMAL(9,2) NULL CONSTRAINT CK_leave_override_cl CHECK (cl_days >= 0),
  sl_days DECIMAL(9,2) NULL CONSTRAINT CK_leave_override_sl CHECK (sl_days >= 0),
  el_days DECIMAL(9,2) NULL CONSTRAINT CK_leave_override_el CHECK (el_days >= 0),
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_leave_ent_override PRIMARY KEY (property_id, staff_id),
  CONSTRAINT CK_leave_override_any CHECK (cl_days IS NOT NULL OR sl_days IS NOT NULL OR el_days IS NOT NULL));
-- SCC blackouts and store holidays: dept_code '' = every department (SCC "All stores"); store holidays never block (hard = 0).
CREATE TABLE hr.leave_blackout(
  blackout_id BIGINT IDENTITY CONSTRAINT PK_leave_blackout PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_leave_blackout_property REFERENCES inv.property,
  from_date DATE NOT NULL,
  to_date DATE NOT NULL,
  dept_code VARCHAR(10) NOT NULL CONSTRAINT DF_leave_blackout_dept DEFAULT '',
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_leave_blackout_kind CHECK (kind IN ('blackout','holiday')),
  hard BIT NOT NULL,
  label NVARCHAR(80) NOT NULL CONSTRAINT DF_leave_blackout_label DEFAULT N'',
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_leave_blackout_dates CHECK (from_date <= to_date),
  CONSTRAINT CK_leave_blackout_hard CHECK (kind = 'blackout' OR hard = 0));
CREATE INDEX IX_leave_blackout_property ON hr.leave_blackout(property_id, from_date, to_date);
-- SCC agenda items (meeting / agenda / reminder) by date; time 'HH:MM' or '' (sorted blank last).
CREATE TABLE hr.leave_agenda(
  agenda_id BIGINT IDENTITY CONSTRAINT PK_leave_agenda PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_leave_agenda_property REFERENCES inv.property,
  agenda_date DATE NOT NULL,
  title NVARCHAR(200) NOT NULL CONSTRAINT CK_leave_agenda_title CHECK (LEN(title) >= 1),
  time_text VARCHAR(5) NOT NULL CONSTRAINT DF_leave_agenda_time DEFAULT '',
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_leave_agenda_kind CHECK (kind IN ('meeting','agenda','reminder')),
  description NVARCHAR(1000) NOT NULL CONSTRAINT DF_leave_agenda_description DEFAULT N'',
  scc_id NVARCHAR(30) NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_leave_agenda_time CHECK (time_text = '' OR time_text LIKE '[0-2][0-9]:[0-5][0-9]'));
CREATE INDEX IX_leave_agenda_property ON hr.leave_agenda(property_id, agenda_date);
