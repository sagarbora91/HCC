-- HCC migration 1700 · W2-11 shift handover (block 1700–1799). Spec: modules/HCC_Module_19_Checklists_Handover_Spec.md;
-- prototype rules domain/streams/handover.ts + mock handlers/handover.ts (R1–R10). Tables live in schema ops.
-- No new PERM ids: the module reuses the seeded PERM-HK-001 / PERM-PAY-001 / PERM-APR-001 / PERM-RPT-002 as the coarse
-- permissions the prototype used (housekeeping / payments / approve / reports), so the 45-permission matrix stays untouched
-- (decision for the owner: dedicated PERM-HOV-* ids can be added with the next RBAC matrix revision).
-- Adds no column to tables owned by other modules; user ids are plain INT (plat.app_user is P1's, no cross-module FK).

-- ── Checklist templates (R2): ordered items, roles by role code (OWN MGR REC CSH SUP HKP MNT), version bumped on every edit ──
CREATE TABLE ops.hov_template(
  template_id BIGINT IDENTITY CONSTRAINT PK_hov_template PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hov_template_property REFERENCES inv.property,
  name NVARCHAR(60) NOT NULL,
  desk VARCHAR(12) NOT NULL CONSTRAINT CK_hov_template_desk CHECK (desk IN ('Front desk','Housekeeping','Maintenance')),
  shift VARCHAR(7) NOT NULL CONSTRAINT CK_hov_template_shift CHECK (shift IN ('Morning','Evening','Night')),
  active BIT NOT NULL CONSTRAINT DF_hov_template_active DEFAULT 1,
  version INT NOT NULL CONSTRAINT DF_hov_template_version DEFAULT 1,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_hov_template_name UNIQUE (property_id, name),
  CONSTRAINT CK_hov_template_name CHECK (LEN(name) >= 3));
CREATE TABLE ops.hov_template_role(
  template_id BIGINT NOT NULL CONSTRAINT FK_hov_template_role_t REFERENCES ops.hov_template ON DELETE CASCADE,
  role_code VARCHAR(3) NOT NULL,
  CONSTRAINT PK_hov_template_role PRIMARY KEY (template_id, role_code));
CREATE TABLE ops.hov_template_item(
  template_id BIGINT NOT NULL CONSTRAINT FK_hov_template_item_t REFERENCES ops.hov_template ON DELETE CASCADE,
  seq SMALLINT NOT NULL,
  item_key VARCHAR(20) NOT NULL,
  text NVARCHAR(120) NOT NULL,
  mandatory BIT NOT NULL,
  due CHAR(5) NULL CONSTRAINT CK_hov_template_item_due CHECK (due IS NULL OR due LIKE '[01][0-9]:[0-5][0-9]' OR due LIKE '2[0-3]:[0-5][0-9]'),
  photo BIT NOT NULL CONSTRAINT DF_hov_template_item_photo DEFAULT 0,
  link VARCHAR(40) NULL,
  CONSTRAINT PK_hov_template_item PRIMARY KEY (template_id, seq),
  CONSTRAINT UQ_hov_template_item_key UNIQUE (template_id, item_key));

-- ── Runs: one template on one business date; snapshot of the template's items and roles at the first tick ──
CREATE TABLE ops.hov_run(
  run_id BIGINT IDENTITY CONSTRAINT PK_hov_run PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hov_run_property REFERENCES inv.property,
  template_id BIGINT NOT NULL CONSTRAINT FK_hov_run_template REFERENCES ops.hov_template,
  template_version INT NOT NULL,
  name NVARCHAR(60) NOT NULL,
  desk VARCHAR(12) NOT NULL,
  shift VARCHAR(7) NOT NULL,
  business_date DATE NOT NULL,
  status VARCHAR(9) NOT NULL CONSTRAINT CK_hov_run_status CHECK (status IN ('Open','Submitted')),
  started_by INT NOT NULL,
  started_utc DATETIME2(3) NOT NULL,
  submitted_by INT NULL,
  submitted_utc DATETIME2(3) NULL,
  late BIT NULL,
  reopen_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_hov_run_template_date UNIQUE (template_id, business_date),
  CONSTRAINT CK_hov_run_submit CHECK ((status = 'Submitted' AND submitted_by IS NOT NULL AND submitted_utc IS NOT NULL) OR (status = 'Open' AND submitted_by IS NULL AND submitted_utc IS NULL)));
CREATE INDEX IX_hov_run_date ON ops.hov_run(property_id, business_date);
CREATE TABLE ops.hov_run_role(
  run_id BIGINT NOT NULL CONSTRAINT FK_hov_run_role_run REFERENCES ops.hov_run ON DELETE CASCADE,
  role_code VARCHAR(3) NOT NULL,
  CONSTRAINT PK_hov_run_role PRIMARY KEY (run_id, role_code));
CREATE TABLE ops.hov_run_item(
  run_id BIGINT NOT NULL CONSTRAINT FK_hov_run_item_run REFERENCES ops.hov_run ON DELETE CASCADE,
  seq SMALLINT NOT NULL,
  item_key VARCHAR(20) NOT NULL,
  text NVARCHAR(120) NOT NULL,
  mandatory BIT NOT NULL,
  due CHAR(5) NULL,
  photo BIT NOT NULL,
  link VARCHAR(40) NULL,
  done BIT NOT NULL CONSTRAINT DF_hov_run_item_done DEFAULT 0,
  done_by INT NULL,
  done_utc DATETIME2(3) NULL,
  note NVARCHAR(200) NULL,
  photo_name NVARCHAR(80) NULL,           -- file name only (simulated: no upload)
  CONSTRAINT PK_hov_run_item PRIMARY KEY (run_id, seq),
  CONSTRAINT UQ_hov_run_item_key UNIQUE (run_id, item_key),
  CONSTRAINT CK_hov_run_item_done CHECK ((done = 1 AND done_by IS NOT NULL AND done_utc IS NOT NULL) OR (done = 0 AND done_by IS NULL AND done_utc IS NULL)),
  CONSTRAINT CK_hov_run_item_photo CHECK (done = 0 OR photo = 0 OR photo_name IS NOT NULL));

-- ── Handover notes (R7–R10): one per desk + business date + outgoing shift; replaced in place until acknowledged ──
-- cash_paise is the expected float handed over (paise); only Front desk notes may carry it (R10) and it is masked on read
-- unless the reader holds PERM-PAY-001 or PERM-RPT-002.
CREATE TABLE ops.hov_note(
  note_id BIGINT IDENTITY CONSTRAINT PK_hov_note PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hov_note_property REFERENCES inv.property,
  desk VARCHAR(12) NOT NULL CONSTRAINT CK_hov_note_desk CHECK (desk IN ('Front desk','Housekeeping','Maintenance')),
  business_date DATE NOT NULL,
  shift VARCHAR(7) NOT NULL CONSTRAINT CK_hov_note_shift CHECK (shift IN ('Morning','Evening','Night')),
  to_shift VARCHAR(7) NOT NULL,
  author_id INT NOT NULL,
  written_utc DATETIME2(3) NOT NULL,
  issues NVARCHAR(400) NOT NULL,
  nothing BIT NOT NULL,
  keys NVARCHAR(400) NOT NULL,
  guests NVARCHAR(400) NOT NULL,
  cash_paise BIGINT NULL,
  cash_note NVARCHAR(200) NULL,
  status VARCHAR(12) NOT NULL CONSTRAINT CK_hov_note_status CHECK (status IN ('Submitted','Acknowledged')),
  ack_by INT NULL,
  ack_utc DATETIME2(3) NULL,
  ack_remark NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_hov_note UNIQUE (property_id, desk, business_date, shift),
  CONSTRAINT CK_hov_note_cash CHECK (cash_paise IS NULL OR (desk = 'Front desk' AND cash_paise BETWEEN 0 AND 100000000)),
  CONSTRAINT CK_hov_note_ack CHECK ((status = 'Acknowledged' AND ack_by IS NOT NULL AND ack_utc IS NOT NULL AND ack_by <> author_id) OR (status = 'Submitted' AND ack_by IS NULL)));  -- no self-acknowledgement
CREATE INDEX IX_hov_note_status ON ops.hov_note(property_id, desk, status);
CREATE TABLE ops.hov_carry(
  carry_id BIGINT IDENTITY CONSTRAINT PK_hov_carry PRIMARY KEY,
  note_id BIGINT NOT NULL CONSTRAINT FK_hov_carry_note REFERENCES ops.hov_note ON DELETE CASCADE,
  seq SMALLINT NOT NULL,
  text NVARCHAR(120) NOT NULL,
  done BIT NOT NULL CONSTRAINT DF_hov_carry_done DEFAULT 0,
  done_by INT NULL,
  done_utc DATETIME2(3) NULL,
  CONSTRAINT UQ_hov_carry_seq UNIQUE (note_id, seq),
  CONSTRAINT CK_hov_carry_done CHECK ((done = 1 AND done_by IS NOT NULL AND done_utc IS NOT NULL) OR (done = 0 AND done_by IS NULL AND done_utc IS NULL)));
