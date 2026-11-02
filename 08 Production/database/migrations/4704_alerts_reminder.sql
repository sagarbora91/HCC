-- HCC migration 4704 · LF B4 desk reminders ("alert days", LF-CONTRACT §9.2). Legacy UserDefineAlert (subject, date, description) + AlertDaysDefine (days before).
-- remind_date = the date; next_date = the rolled date of a repeating reminder (monthly / yearly), NULL until the first Done. lead_days 0–60. role_code = 'all' or a role
-- code (OWN, MGR, REC, CSH, ACC …). Status open / done / dismissed; a dismissal keeps its reason in note. Nothing about money.
CREATE TABLE plat.reminder(
  reminder_id BIGINT IDENTITY CONSTRAINT PK_reminder PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_reminder_property REFERENCES inv.property,
  subject NVARCHAR(80) NOT NULL,
  remind_date DATE NOT NULL,
  remind_time CHAR(5) NULL,
  lead_days INT NOT NULL,
  role_code VARCHAR(3) NOT NULL,
  note NVARCHAR(200) NULL,
  repeat_rule VARCHAR(7) NOT NULL,
  status VARCHAR(9) NOT NULL,
  next_date DATE NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  done_by INT NULL,
  done_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_reminder_lead CHECK (lead_days BETWEEN 0 AND 60),
  CONSTRAINT CK_reminder_repeat CHECK (repeat_rule IN ('none','monthly','yearly')),
  CONSTRAINT CK_reminder_status CHECK (status IN ('open','done','dismissed')),
  CONSTRAINT CK_reminder_subject CHECK (LEN(subject) BETWEEN 2 AND 80),
  CONSTRAINT CK_reminder_time CHECK (remind_time IS NULL OR remind_time LIKE '[0-2][0-9]:[0-5][0-9]'));
CREATE INDEX IX_reminder_open ON plat.reminder(property_id, status, remind_date);
