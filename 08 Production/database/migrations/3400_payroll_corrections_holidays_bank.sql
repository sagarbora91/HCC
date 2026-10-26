-- HCC migration 3400 · payroll owner decisions (Wave B item B4, block 3400–3409): DEC-075 correction of past attendance in the open pay month
-- (audit A04-02), DEC-083 dated paid-holiday calendar (A04-05), DEC-084 bank-detail change with the Owner's approval (O-16).
-- hr.change_request follows the module request pattern of ivc.credit_note_request / cfg.change_request (Pending → decided by another person,
-- PERM-SOD-001 check, one pending request per target, direct = applied without a checker). No new permissions: corrections need PERM-HR-002
-- (Owner, Manager), bank requests PERM-HR-005 (Owner, Accountant), decisions and holidays PERM-HR-009 (Owner). Fictional data only.
-- DEC-083: the property's paid holidays (date + name), maintained by the Owner; nothing is seeded (the owner supplies the list).
CREATE TABLE hr.holiday(
  property_id INT NOT NULL CONSTRAINT FK_holiday_property REFERENCES inv.property,
  holiday_date DATE NOT NULL,
  name NVARCHAR(60) NOT NULL CONSTRAINT CK_holiday_name CHECK (LEN(name) >= 2),
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_holiday PRIMARY KEY (property_id, holiday_date));
-- DEC-075 / DEC-084 requests. Attendance: work_date + before / after day entry ({"kind":"none|mark|leave","status","otMinutes","leaveType"})
-- and the gross pay change of the month (positive = raises pay = needs the Owner). Bank: before / after details ({"bankName","accountNo","ifsc"},
-- full account number — the request applies it; screens and audit mask it). direct = 1: applied at once (a correction that lowers or keeps
-- pay, or the Owner's own change), stored as Approved by the requester for the history.
CREATE TABLE hr.change_request(
  request_id BIGINT IDENTITY CONSTRAINT PK_hr_change_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hr_request_property REFERENCES inv.property,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_hr_request_kind CHECK (kind IN ('attendance','bank')),
  staff_id BIGINT NOT NULL CONSTRAINT FK_hr_request_staff REFERENCES hr.staff,
  work_date DATE NULL,
  before_json NVARCHAR(400) NULL CONSTRAINT CK_hr_request_before CHECK (before_json IS NULL OR ISJSON(before_json) = 1),
  after_json NVARCHAR(400) NOT NULL CONSTRAINT CK_hr_request_after CHECK (ISJSON(after_json) = 1),
  pay_effect_paise BIGINT NULL,
  reason NVARCHAR(200) NOT NULL,
  status VARCHAR(9) NOT NULL CONSTRAINT CK_hr_request_status CHECK (status IN ('Pending','Approved','Rejected','Withdrawn')),
  direct BIT NOT NULL,
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decision_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_hr_request_scope CHECK ((kind = 'attendance' AND work_date IS NOT NULL) OR (kind = 'bank' AND work_date IS NULL AND pay_effect_paise IS NULL)),
  CONSTRAINT CK_hr_request_direct CHECK (direct = 0 OR status = 'Approved'),
  CONSTRAINT CK_hr_request_decided CHECK ((status = 'Pending' AND decided_by IS NULL) OR (status <> 'Pending' AND decided_by IS NOT NULL AND decided_utc IS NOT NULL)),
  -- PERM-SOD-001: nobody approves or rejects their own request; only a direct entry or the requester's own withdrawal carries the requester.
  CONSTRAINT CK_hr_request_sod CHECK (decided_by IS NULL OR decided_by <> requested_by OR direct = 1 OR status = 'Withdrawn'));
-- One pending request per staff member and day (attendance) / per staff member (bank: work_date NULL — NULLs are equal in a unique index).
CREATE UNIQUE INDEX UX_hr_request_pending ON hr.change_request(staff_id, kind, work_date) WHERE status = 'Pending';
CREATE INDEX IX_hr_request_property ON hr.change_request(property_id, status) INCLUDE (kind, work_date, requested_utc);
-- A back-dated one-day leave created by a correction points to it; that request is the approval trail (a direct correction is decided by its maker).
ALTER TABLE hr.leave_request ADD correction_id BIGINT NULL CONSTRAINT FK_leave_correction REFERENCES hr.change_request;
GO
ALTER TABLE hr.leave_request DROP CONSTRAINT CK_leave_not_self;
ALTER TABLE hr.leave_request ADD CONSTRAINT CK_leave_not_self CHECK (decided_by IS NULL OR status = 'Cancelled' OR decided_by <> requested_by OR correction_id IS NOT NULL);
