-- HCC migration 4800 · ADM-2 roles as data (02 Planning/agents/ADM_Briefs.md §2.1; DEC-216…219). The ten built-in roles stay read-only templates.
-- A custom role (codes C01…C99, fits the VARCHAR(3) key so no foreign key is rewritten) is a copy of one built-in base role at creation, then
-- adjusted by the Owner: per sub-function access OFF / VIEW / FULL (absent row = FULL) and one money flag. Its plat.role_grant rows are its own,
-- materialised by the server from the frozen copy of the base (plat.role_origin_grant) + the grid + the money flag + Advanced edits
-- (plat.role_grant_override); later changes to the base never flow down. Every change is one versioned row in plat.policy_version
-- (who / when / summary / before / after) beside the usual plat.audit_event row ROLE_*.
ALTER TABLE plat.role ADD
  is_custom BIT NOT NULL CONSTRAINT DF_role_is_custom DEFAULT 0,
  base_role VARCHAR(3) NULL CONSTRAINT FK_role_base REFERENCES plat.role,
  money BIT NOT NULL CONSTRAINT DF_role_money DEFAULT 1,
  version INT NOT NULL CONSTRAINT DF_role_version DEFAULT 1,
  created_by INT NULL,
  created_utc DATETIME2(3) NULL,
  retired_utc DATETIME2(3) NULL;
GO
-- DEC-216: Owner is never a base; a custom role always has one; built-ins never do. DEC-061: the four non-money bases never hold money.
ALTER TABLE plat.role ADD CONSTRAINT CK_role_custom CHECK (
  (is_custom = 0 AND base_role IS NULL AND retired_utc IS NULL)
  OR (is_custom = 1 AND base_role IS NOT NULL AND base_role <> 'OWN' AND role_code LIKE 'C[0-9][0-9]'));
ALTER TABLE plat.role ADD CONSTRAINT CK_role_money_lock CHECK (money = 0 OR base_role IS NULL OR base_role NOT IN ('HKP', 'SUP', 'MNT', 'RST'));
UPDATE plat.role SET money = 0 WHERE role_code IN ('HKP', 'SUP', 'MNT', 'RST');
CREATE TABLE plat.role_feature(
  role_code VARCHAR(3) NOT NULL CONSTRAINT FK_role_feature_role REFERENCES plat.role,
  feature_id VARCHAR(40) NOT NULL,
  access VARCHAR(4) NOT NULL CONSTRAINT CK_role_feature_access CHECK (access IN ('OFF', 'VIEW', 'FULL')),
  CONSTRAINT PK_role_feature PRIMARY KEY (role_code, feature_id));
-- The base's grants as they were when the custom role was created (never changed afterwards).
CREATE TABLE plat.role_origin_grant(
  role_code VARCHAR(3) NOT NULL CONSTRAINT FK_role_origin_grant_role REFERENCES plat.role,
  permission_id VARCHAR(20) NOT NULL CONSTRAINT FK_role_origin_grant_permission REFERENCES plat.permission,
  mode CHAR(1) NOT NULL CONSTRAINT CK_role_origin_grant_mode CHECK (mode IN ('A', 'L', 'R', 'V', 'M')),
  scope CHAR(1) NOT NULL CONSTRAINT CK_role_origin_grant_scope CHECK (scope IN ('P', 'S', 'A', 'O')),
  limit_value BIGINT NULL,
  limit_unit VARCHAR(5) NULL CONSTRAINT CK_role_origin_grant_unit CHECK (limit_unit IN ('PAISE', 'BPS')),
  CONSTRAINT PK_role_origin_grant PRIMARY KEY (role_code, permission_id));
-- Advanced editor (SetRoleGrant): mode NULL = the permission is removed from the role; otherwise this cell replaces the materialised one.
CREATE TABLE plat.role_grant_override(
  role_code VARCHAR(3) NOT NULL CONSTRAINT FK_role_grant_override_role REFERENCES plat.role,
  permission_id VARCHAR(20) NOT NULL CONSTRAINT FK_role_grant_override_permission REFERENCES plat.permission,
  mode CHAR(1) NULL CONSTRAINT CK_role_grant_override_mode CHECK (mode IS NULL OR mode IN ('A', 'L', 'R', 'V', 'M')),
  scope CHAR(1) NOT NULL CONSTRAINT CK_role_grant_override_scope CHECK (scope IN ('P', 'S', 'A', 'O')),
  limit_value BIGINT NULL,
  limit_unit VARCHAR(5) NULL CONSTRAINT CK_role_grant_override_unit CHECK (limit_unit IN ('PAISE', 'BPS')),
  CONSTRAINT PK_role_grant_override PRIMARY KEY (role_code, permission_id),
  CONSTRAINT CK_role_grant_override_limit CHECK ((limit_value IS NULL AND limit_unit IS NULL) OR (mode = 'L' AND limit_value >= 0 AND limit_unit IS NOT NULL)));
-- One row per policy change (plan §2.5): the Administration page's audit tab. Append-only.
CREATE TABLE plat.policy_version(
  property_id INT NOT NULL,
  version INT NOT NULL,
  role_code VARCHAR(3) NULL,
  action VARCHAR(30) NOT NULL,
  changed_by INT NOT NULL,
  changed_utc DATETIME2(3) NOT NULL,
  summary NVARCHAR(200) NOT NULL,
  before_json NVARCHAR(MAX) NULL,
  after_json NVARCHAR(MAX) NULL,
  CONSTRAINT PK_policy_version PRIMARY KEY (property_id, version));
GO
CREATE TRIGGER plat.TR_policy_version_append_only ON plat.policy_version AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51480, 'plat.policy_version is append-only.', 1;
END;
GO
-- Held roles plus the base code of each held custom role, so role-code checks ("is a Manager") keep working for a Manager-based role.
CREATE VIEW plat.v_user_role_effective AS
SELECT ur.user_id, ur.role_code, ur.valid_from_utc, ur.valid_until_utc
  FROM plat.user_role ur
UNION
SELECT ur.user_id, r.base_role, ur.valid_from_utc, ur.valid_until_utc
  FROM plat.user_role ur JOIN plat.role r ON r.role_code = ur.role_code
 WHERE r.base_role IS NOT NULL;
GO
