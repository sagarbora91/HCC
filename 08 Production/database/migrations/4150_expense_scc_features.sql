-- HCC migration 4150 · SCC expense features (DEC-120, 27 Oct 2026; SCC port wave block 4150–4169). HCC's expense module (0800, designed in
-- HCC) gains SCC's features while keeping HCC's approval rules (an expense counts only once approved; the Manager approves up to ₹5,000 or
-- the category's limit; nobody approves their own; the Cashier never reverses or voids — DEC-063 G5, REQ-EXP-003). Rules: SCC
-- expense\index.html (expense.js) and the prototype twin 03 Prototype/src/domain/streams/expenses-scc.ts. No new permissions: PERM-EXP-001
-- (submit, post a recurring draft, petty disbursement / top-up request, Udhaar), PERM-EXP-002 (edit any pending, budgets, recurring
-- templates, petty top-up decision), PERM-EXP-003 (void, Udhaar write-off), PERM-MST-002 (Owner: month lock and its override).
--   ops.expense            + description, GST on the bill, source (petty / recurring), void (status 'Void' with one of SCC's six reasons)
--   ops.expense_edit       SCC edit log (who, when, why, old → new as JSON)
--   ops.expense_bill_photo the bill photo (JPEG / PNG ≤ 4 MB, sent at 1000 px / JPEG 0.7 by the app), its own table so lists never read it
--   ops.expense_budget     SCC monthly budget per category
--   ops.expense_recurring  SCC recurring templates (posting raises a draft for approval)
--   ops.petty_float_entry  SCC petty-cash float top-ups, each approved by a second person (float = Σ approved)
--   ops.udhaar_receivable  SCC Udhaar credit register (+ ops.udhaar_settlement); never touches cash or income
--   ops.expense_month_lock SCC "Generate & lock tax feed": the month's figures; writes into the month need the Owner + a reason (stale flag)
ALTER TABLE ops.expense DROP CONSTRAINT CK_expense_status;
ALTER TABLE ops.expense DROP CONSTRAINT CK_expense_decided;
GO
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_status CHECK (status IN ('Draft','Pending','Posted','Rejected','Reversed','Void'));
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_decided CHECK (status IN ('Draft','Pending','Void') OR (decided_by IS NOT NULL AND decided_utc IS NOT NULL));
ALTER TABLE ops.expense ADD
  description NVARCHAR(300) NULL,
  gst_rate_bps INT NULL CONSTRAINT CK_expense_gst_rate CHECK (gst_rate_bps BETWEEN 1 AND 10000),
  gst_paise BIGINT NULL CONSTRAINT CK_expense_gst CHECK (gst_paise <> 0 AND gst_paise BETWEEN -10000000000 AND 10000000000),
  source VARCHAR(10) NULL CONSTRAINT CK_expense_source CHECK (source IN ('petty','recurring')),
  source_ref VARCHAR(60) NULL,
  void_reason NVARCHAR(300) NULL,
  voided_by INT NULL,
  voided_utc DATETIME2(3) NULL;
GO
ALTER TABLE ops.expense ADD CONSTRAINT CK_expense_void CHECK ((status = 'Void' AND void_reason IS NOT NULL AND voided_by IS NOT NULL AND voided_utc IS NOT NULL)
  OR (status <> 'Void' AND void_reason IS NULL AND voided_by IS NULL AND voided_utc IS NULL));
CREATE INDEX IX_expense_source_ref ON ops.expense(property_id, source_ref) INCLUDE (status, reversal_of, amount_paise) WHERE source_ref IS NOT NULL;
GO
CREATE TABLE ops.expense_edit(
  edit_id BIGINT IDENTITY CONSTRAINT PK_expense_edit PRIMARY KEY,
  expense_id BIGINT NOT NULL CONSTRAINT FK_expense_edit_expense REFERENCES ops.expense,
  edited_by INT NOT NULL,
  edited_utc DATETIME2(3) NOT NULL,
  reason NVARCHAR(200) NOT NULL CONSTRAINT CK_expense_edit_reason CHECK (LEN(reason) > 0),
  old_json NVARCHAR(2000) NOT NULL CONSTRAINT CK_expense_edit_old CHECK (ISJSON(old_json) = 1),
  new_json NVARCHAR(2000) NOT NULL CONSTRAINT CK_expense_edit_new CHECK (ISJSON(new_json) = 1));
CREATE INDEX IX_expense_edit ON ops.expense_edit(expense_id, edit_id);

CREATE TABLE ops.expense_bill_photo(
  expense_id BIGINT NOT NULL CONSTRAINT PK_expense_bill_photo PRIMARY KEY CONSTRAINT FK_expense_bill_photo_expense REFERENCES ops.expense,
  content_type VARCHAR(10) NOT NULL CONSTRAINT CK_expense_bill_photo_type CHECK (content_type IN ('image/jpeg','image/png')),
  size_bytes INT NOT NULL CONSTRAINT CK_expense_bill_photo_size CHECK (size_bytes BETWEEN 1 AND 4194304),
  image VARBINARY(MAX) NOT NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_expense_bill_photo_bytes CHECK (DATALENGTH(image) = size_bytes));

CREATE TABLE ops.expense_budget(
  property_id INT NOT NULL CONSTRAINT FK_expense_budget_property REFERENCES inv.property,
  budget_month CHAR(7) NOT NULL CONSTRAINT CK_expense_budget_month CHECK (budget_month LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]'),
  category NVARCHAR(60) NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_expense_budget_amount CHECK (amount_paise BETWEEN 0 AND 10000000000),
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_expense_budget PRIMARY KEY (property_id, budget_month, category));

CREATE TABLE ops.expense_recurring(
  template_id INT IDENTITY CONSTRAINT PK_expense_recurring PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_expense_recurring_property REFERENCES inv.property,
  name NVARCHAR(60) NOT NULL CONSTRAINT CK_expense_recurring_name CHECK (LEN(name) > 0),
  amount_paise BIGINT NOT NULL CONSTRAINT CK_expense_recurring_amount CHECK (amount_paise BETWEEN 1 AND 10000000000),
  due_day TINYINT NOT NULL CONSTRAINT CK_expense_recurring_day CHECK (due_day BETWEEN 1 AND 31),
  category NVARCHAR(60) NOT NULL,
  method NVARCHAR(60) NOT NULL,
  vendor NVARCHAR(120) NOT NULL,
  active BIT NOT NULL,
  -- SCC deletes a template; HCC keeps the row (deleted_utc) so the audit trail and posted entries ('rec:<id>:<month>') still name it.
  deleted_by INT NULL,
  deleted_utc DATETIME2(3) NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION);
CREATE INDEX IX_expense_recurring ON ops.expense_recurring(property_id, template_id) WHERE deleted_utc IS NULL;

CREATE TABLE ops.petty_float_entry(
  entry_id BIGINT IDENTITY CONSTRAINT PK_petty_float_entry PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_petty_float_entry_property REFERENCES inv.property,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_petty_float_entry_amount CHECK (amount_paise BETWEEN 1 AND 10000000000),
  business_date DATE NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_petty_float_entry_status CHECK (status IN ('Pending','Approved','Rejected')),
  note NVARCHAR(200) NULL,
  recorded_by INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_petty_float_entry_decided CHECK ((status = 'Pending' AND decided_by IS NULL AND decided_utc IS NULL)
    OR (status <> 'Pending' AND decided_by IS NOT NULL AND decided_utc IS NOT NULL)),
  -- Maker-checker: nobody approves their own top-up.
  CONSTRAINT CK_petty_float_entry_sod CHECK (decided_by IS NULL OR decided_by <> recorded_by));
CREATE INDEX IX_petty_float_entry ON ops.petty_float_entry(property_id, entry_id);

CREATE TABLE ops.udhaar_receivable(
  receivable_id BIGINT IDENTITY CONSTRAINT PK_udhaar_receivable PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_udhaar_receivable_property REFERENCES inv.property,
  customer_name NVARCHAR(80) NOT NULL CONSTRAINT CK_udhaar_receivable_name CHECK (LEN(customer_name) > 0),
  mobile CHAR(10) NULL CONSTRAINT CK_udhaar_receivable_mobile CHECK (mobile NOT LIKE '%[^0-9]%'),
  amount_paise BIGINT NOT NULL CONSTRAINT CK_udhaar_receivable_amount CHECK (amount_paise BETWEEN 1 AND 10000000000),
  credit_date DATE NOT NULL,
  due_date DATE NULL,
  note NVARCHAR(200) NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  is_void BIT NOT NULL CONSTRAINT DF_udhaar_receivable_void DEFAULT 0,
  void_reason NVARCHAR(200) NULL,
  voided_by INT NULL,
  voided_date DATE NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_udhaar_receivable_void CHECK ((is_void = 1 AND voided_by IS NOT NULL AND voided_date IS NOT NULL) OR (is_void = 0 AND voided_by IS NULL AND voided_date IS NULL)));
CREATE INDEX IX_udhaar_receivable ON ops.udhaar_receivable(property_id, credit_date, receivable_id);

CREATE TABLE ops.udhaar_settlement(
  settlement_id BIGINT IDENTITY CONSTRAINT PK_udhaar_settlement PRIMARY KEY,
  receivable_id BIGINT NOT NULL CONSTRAINT FK_udhaar_settlement_receivable REFERENCES ops.udhaar_receivable,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_udhaar_settlement_amount CHECK (amount_paise BETWEEN 1 AND 10000000000),
  settle_date DATE NOT NULL,
  mode NVARCHAR(60) NOT NULL,
  recorded_by INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_udhaar_settlement ON ops.udhaar_settlement(receivable_id, settlement_id);

CREATE TABLE ops.expense_month_lock(
  property_id INT NOT NULL CONSTRAINT FK_expense_month_lock_property REFERENCES inv.property,
  lock_month CHAR(7) NOT NULL CONSTRAINT CK_expense_month_lock_month CHECK (lock_month LIKE '[0-9][0-9][0-9][0-9]-[01][0-9]'),
  expense_paise BIGINT NOT NULL,
  entry_count INT NOT NULL,
  gst_paid_paise BIGINT NOT NULL,
  by_category NVARCHAR(MAX) NOT NULL CONSTRAINT CK_expense_month_lock_cat CHECK (ISJSON(by_category) = 1),
  generated_by INT NOT NULL,
  generated_utc DATETIME2(3) NOT NULL,
  stale BIT NOT NULL CONSTRAINT DF_expense_month_lock_stale DEFAULT 0,
  stale_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_expense_month_lock PRIMARY KEY (property_id, lock_month));
GO
