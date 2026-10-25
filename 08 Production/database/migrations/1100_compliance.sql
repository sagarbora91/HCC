-- HCC migration 1100 · W2-5 Compliance calendar (block 1100–1199). Prototype: 03 Prototype/src/domain/streams/compliance.ts (rules from the SCC tax
-- module; illustrative due dates — CA to confirm). DEC-055: Owner and Accountant only. Original HCC design; no donor code.
-- Tables: obligations with DATED due rules, one filing row per obligation x period, the kept record of reopened filings, per-property settings
-- (CA name / e-mail / "due this week" window) and the simulated CA packs (nothing is e-mailed).
-- The RBAC matrix v1 has no compliance row: two PERM ids are added here (DEC-055 placement).

CREATE SCHEMA cmp;
GO

CREATE TABLE cmp.obligation(
  obligation_id BIGINT IDENTITY CONSTRAINT PK_cmp_obligation PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cmp_obligation_property REFERENCES inv.property,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_cmp_obligation_kind CHECK (kind IN ('TDS', 'GSTR-1', 'GSTR-3B', 'GSTR-9', 'EPF', 'ESIC', 'PT')),
  name NVARCHAR(60) NOT NULL,
  form NVARCHAR(60) NOT NULL CONSTRAINT DF_cmp_obligation_form DEFAULT N'',
  frequency VARCHAR(7) NOT NULL CONSTRAINT CK_cmp_obligation_frequency CHECK (frequency IN ('Monthly', 'Annual')),
  owner_role NVARCHAR(30) NOT NULL,
  from_period VARCHAR(7) NOT NULL,                       -- 'YYYY-MM' (monthly) or 'YYYY-YY' (annual financial year)
  active BIT NOT NULL CONSTRAINT DF_cmp_obligation_active DEFAULT 1,
  note NVARCHAR(300) NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_cmp_obligation_name UNIQUE (property_id, name));

-- Dated due rules: the rule for a period is the latest one whose effective_from is on or before the period's first day.
CREATE TABLE cmp.obligation_rule(
  obligation_id BIGINT NOT NULL CONSTRAINT FK_cmp_rule_obligation REFERENCES cmp.obligation,
  effective_from DATE NOT NULL,
  rule_type VARCHAR(9) NOT NULL CONSTRAINT CK_cmp_rule_type CHECK (rule_type IN ('day', 'month-end', 'fixed')),
  due_day TINYINT NULL,
  due_month TINYINT NULL,
  special_month TINYINT NULL,
  special_day TINYINT NULL,
  CONSTRAINT PK_cmp_obligation_rule PRIMARY KEY (obligation_id, effective_from),
  CONSTRAINT CK_cmp_rule_shape CHECK (
    (rule_type = 'month-end' AND due_day IS NULL AND due_month IS NULL AND special_month IS NULL AND special_day IS NULL)
    OR (rule_type = 'day' AND due_day BETWEEN 1 AND 28 AND due_month IS NULL AND ((special_month IS NULL AND special_day IS NULL) OR (special_month BETWEEN 1 AND 12 AND special_day BETWEEN 1 AND 31)))
    OR (rule_type = 'fixed' AND due_day BETWEEN 1 AND 31 AND due_month BETWEEN 1 AND 12 AND special_month IS NULL AND special_day IS NULL)));

CREATE TABLE cmp.filing(
  obligation_id BIGINT NOT NULL CONSTRAINT FK_cmp_filing_obligation REFERENCES cmp.obligation,
  period VARCHAR(7) NOT NULL,
  status VARCHAR(14) NOT NULL CONSTRAINT CK_cmp_filing_status CHECK (status IN ('Filed', 'Not applicable')),
  filed_by INT NOT NULL,
  filed_utc DATETIME2(3) NOT NULL,
  reference NVARCHAR(40) NULL,
  reason NVARCHAR(120) NULL,
  CONSTRAINT PK_cmp_filing PRIMARY KEY (obligation_id, period),
  CONSTRAINT CK_cmp_filing_detail CHECK ((status = 'Filed' AND reference IS NOT NULL) OR (status = 'Not applicable' AND reason IS NOT NULL)));

-- A filing the Owner reopened: the original record is kept (who, when, reference) with who reopened it, when and why.
CREATE TABLE cmp.filing_reopened(
  reopen_id BIGINT IDENTITY CONSTRAINT PK_cmp_filing_reopened PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cmp_reopened_property REFERENCES inv.property,
  obligation_id BIGINT NOT NULL CONSTRAINT FK_cmp_reopened_obligation REFERENCES cmp.obligation,
  period VARCHAR(7) NOT NULL,
  status VARCHAR(14) NOT NULL,
  filed_by INT NOT NULL,
  filed_utc DATETIME2(3) NOT NULL,
  reference NVARCHAR(40) NULL,
  reason NVARCHAR(120) NULL,
  reopened_by INT NOT NULL,
  reopened_utc DATETIME2(3) NOT NULL,
  reopen_reason NVARCHAR(120) NOT NULL);
CREATE INDEX IX_cmp_reopened_property ON cmp.filing_reopened(property_id, reopen_id DESC);

CREATE TABLE cmp.setting(
  property_id INT NOT NULL CONSTRAINT PK_cmp_setting PRIMARY KEY CONSTRAINT FK_cmp_setting_property REFERENCES inv.property,
  ca_name NVARCHAR(60) NOT NULL,
  ca_email NVARCHAR(80) NOT NULL,
  due_soon_days TINYINT NOT NULL CONSTRAINT CK_cmp_setting_days CHECK (due_soon_days BETWEEN 1 AND 30),
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL,
  row_ver ROWVERSION);

-- Simulated CA pack (nothing is sent): the lines are the position as on the business date.
CREATE TABLE cmp.ca_pack(
  pack_id BIGINT IDENTITY CONSTRAINT PK_cmp_ca_pack PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cmp_pack_property REFERENCES inv.property,
  created_utc DATETIME2(3) NOT NULL,
  created_by INT NOT NULL,
  to_email NVARCHAR(80) NOT NULL,
  to_name NVARCHAR(60) NOT NULL,
  overdue_count INT NOT NULL,
  due_count INT NOT NULL,
  filed_count INT NOT NULL,
  na_count INT NOT NULL,
  lines_json NVARCHAR(MAX) NOT NULL CONSTRAINT CK_cmp_pack_lines CHECK (ISJSON(lines_json) = 1));
CREATE INDEX IX_cmp_pack_property ON cmp.ca_pack(property_id, pack_id DESC);
GO

-- ── Permissions (DEC-055: Owner and Accountant only; managing = Owner) ──
INSERT plat.permission(permission_id, resource_action, kind, is_money) VALUES
  ('PERM-CMP-001', 'compliance.use (view, mark filed / not applicable, CA pack)', 'ACTION', 0),
  ('PERM-CMP-002', 'compliance.manage (reopen, obligations, settings)', 'ACTION', 0);
INSERT plat.role_grant(role_code, permission_id, mode, scope, limit_value, limit_unit) VALUES
  ('OWN', 'PERM-CMP-001', 'A', 'P', NULL, NULL),
  ('ACC', 'PERM-CMP-001', 'A', 'P', NULL, NULL),
  ('OWN', 'PERM-CMP-002', 'A', 'P', NULL, NULL);
