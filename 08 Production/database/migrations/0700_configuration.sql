-- HCC migration 0700 · Master configuration (W2-1). DEC-057 one settings hub with dated values, approvals, history and
-- modules on/off; DEC-058 Owner changes every group, Tech admin only non-money groups, never self-approve. Design:
-- 02 Planning/HCC_Master_Configuration_Design_v1.md §4-§5. The registry (definitions, groups, validation) lives in code
-- (Hcc.Application/Configuration providers); this schema keeps the values. A value in force on a business date = the newest
-- not-withdrawn change with effective_from <= that date (default from the registry otherwise), so dated changes need no job.
-- No PERM rows: the module uses PERM-ADM-003 (A = Owner, V = Tech admin non-money per DEC-058) and PERM-APR-001 (Manager
-- decides requests whose approver is Manager) from 0101.
CREATE SCHEMA cfg;
GO
-- One row per setting that has ever been changed: its rowversion is the setting's expectedVersion and its lock serializes changes.
CREATE TABLE cfg.setting(
  property_id INT NOT NULL CONSTRAINT FK_cfg_setting_property REFERENCES inv.property,
  setting_key VARCHAR(80) NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  changed_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION NOT NULL,
  CONSTRAINT PK_cfg_setting PRIMARY KEY (property_id, setting_key));
-- Every applied or scheduled value (history + revert keep the raw before/after JSON; the *_text columns never hold a secret).
CREATE TABLE cfg.setting_change(
  change_id BIGINT IDENTITY CONSTRAINT PK_cfg_setting_change PRIMARY KEY,
  property_id INT NOT NULL,
  setting_key VARCHAR(80) NOT NULL,
  effective_from DATE NOT NULL,
  value_json NVARCHAR(4000) NOT NULL,
  before_json NVARCHAR(4000) NOT NULL,
  before_text NVARCHAR(300) NOT NULL,
  after_text NVARCHAR(300) NOT NULL,
  reason NVARCHAR(200) NULL,
  source VARCHAR(8) NOT NULL CONSTRAINT CK_cfg_change_source CHECK (source IN ('change', 'revert', 'import', 'approval')),
  changed_by INT NOT NULL CONSTRAINT FK_cfg_change_by REFERENCES plat.app_user,
  approved_by INT NULL CONSTRAINT FK_cfg_change_approved REFERENCES plat.app_user,
  request_id BIGINT NULL,
  revert_of BIGINT NULL CONSTRAINT FK_cfg_change_revert REFERENCES cfg.setting_change,
  changed_utc DATETIME2(3) NOT NULL,
  withdrawn_utc DATETIME2(3) NULL,
  withdrawn_by INT NULL CONSTRAINT FK_cfg_change_withdrawn_by REFERENCES plat.app_user,
  CONSTRAINT FK_cfg_change_setting FOREIGN KEY (property_id, setting_key) REFERENCES cfg.setting(property_id, setting_key),
  CONSTRAINT CK_cfg_change_withdrawn CHECK ((withdrawn_utc IS NULL AND withdrawn_by IS NULL) OR (withdrawn_utc IS NOT NULL AND withdrawn_by IS NOT NULL)),
  CONSTRAINT CK_cfg_change_sod CHECK (approved_by IS NULL OR approved_by <> changed_by));
CREATE INDEX IX_cfg_change_value ON cfg.setting_change(property_id, setting_key, effective_from, change_id) INCLUDE (value_json) WHERE withdrawn_utc IS NULL;
CREATE INDEX IX_cfg_change_history ON cfg.setting_change(property_id, change_id);
-- A Tech admin change to a setting with an approver (DEC-058); one pending request per setting; never decided by the requester.
CREATE TABLE cfg.change_request(
  request_id BIGINT IDENTITY CONSTRAINT PK_cfg_change_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_cfg_request_property REFERENCES inv.property,
  setting_key VARCHAR(80) NOT NULL,
  value_json NVARCHAR(4000) NOT NULL,
  before_json NVARCHAR(4000) NOT NULL,
  effective_from DATE NOT NULL,
  approver VARCHAR(7) NOT NULL CONSTRAINT CK_cfg_request_approver CHECK (approver IN ('Manager', 'Owner')),
  summary NVARCHAR(400) NOT NULL,
  reason NVARCHAR(200) NULL,
  revert_of BIGINT NULL CONSTRAINT FK_cfg_request_revert REFERENCES cfg.setting_change,
  requested_by INT NOT NULL CONSTRAINT FK_cfg_request_by REFERENCES plat.app_user,
  requested_utc DATETIME2(3) NOT NULL,
  expires_utc DATETIME2(3) NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_cfg_request_status CHECK (status IN ('Pending', 'Rejected', 'Executed')),
  decided_by INT NULL CONSTRAINT FK_cfg_request_decided REFERENCES plat.app_user,
  decided_utc DATETIME2(3) NULL,
  decision_reason NVARCHAR(200) NULL,
  change_id BIGINT NULL CONSTRAINT FK_cfg_request_change REFERENCES cfg.setting_change,
  row_ver ROWVERSION NOT NULL,
  CONSTRAINT CK_cfg_request_sod CHECK (decided_by IS NULL OR decided_by <> requested_by),
  CONSTRAINT CK_cfg_request_decided CHECK ((status = 'Pending' AND decided_by IS NULL) OR (status <> 'Pending' AND decided_by IS NOT NULL AND decided_utc IS NOT NULL)));
CREATE UNIQUE INDEX UX_cfg_request_pending ON cfg.change_request(property_id, setting_key) WHERE status = 'Pending';
ALTER TABLE cfg.setting_change ADD CONSTRAINT FK_cfg_change_request FOREIGN KEY (request_id) REFERENCES cfg.change_request;
