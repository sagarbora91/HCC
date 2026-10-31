-- HCC migration 4246 · ACC-2 acc2-gst · the accounting month lock (02 Planning/agents/ACC2-POSTING-CONTRACT.md §6; rulebook §G1; DEC-131).
-- LockAccountingMonth (Owner, PERM-ACC-012) locks a month once every business day of it is closed and locked (closed days are locked in the
-- same command), the GST set-off of the month is posted (acc.gst_setoff, migration 4247) and no manual voucher dated in it is pending; it
-- also locks the SCC expense month (ops.expense_month_lock). UnlockAccountingMonth (Owner, reason) stamps the row (DEC-131 mirror; the day
-- locks stay); a relock inserts a new row, so the history is kept. RequestAccountingMonthLock (the Accountant) records a request.
-- Original HCC design; no donor code. Times DATETIME2(3) UTC written by the application.
CREATE TABLE acc.month_lock(
  month_lock_id BIGINT IDENTITY CONSTRAINT PK_acc_month_lock PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_month_lock_property REFERENCES inv.property,
  lock_month CHAR(7) NOT NULL CONSTRAINT CK_acc_month_lock_month CHECK (lock_month LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]'),
  setoff_id BIGINT NULL,
  locked_by INT NOT NULL,
  locked_utc DATETIME2(3) NOT NULL,
  unlocked_by INT NULL,
  unlocked_utc DATETIME2(3) NULL,
  unlock_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_acc_month_lock_unlock CHECK ((unlocked_utc IS NULL AND unlocked_by IS NULL AND unlock_reason IS NULL)
                                          OR (unlocked_utc IS NOT NULL AND unlocked_by IS NOT NULL AND LEN(unlock_reason) >= 3)));
CREATE UNIQUE INDEX UX_acc_month_lock_current ON acc.month_lock(property_id, lock_month) WHERE unlocked_utc IS NULL;
CREATE TABLE acc.month_lock_request(
  request_id BIGINT IDENTITY CONSTRAINT PK_acc_month_lock_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_acc_month_lock_request_property REFERENCES inv.property,
  lock_month CHAR(7) NOT NULL CONSTRAINT CK_acc_month_lock_request_month CHECK (lock_month LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]'),
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_acc_month_lock_request ON acc.month_lock_request(property_id, lock_month, requested_utc);
