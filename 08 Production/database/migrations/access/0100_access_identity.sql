-- HCC migration 0100 · access (P1): users, roles, permissions, grants, explicit denies, user exceptions, credentials,
-- PIN history, registered devices and server sessions (contracts §1, RBAC matrix v1, DEC-040, DEC-062). Original HCC design.
-- Event times DATETIME2(3) UTC written from the application's injected clock. Secrets are stored only as PBKDF2/SHA-256 hashes.
CREATE TABLE plat.app_user(
  user_id INT IDENTITY CONSTRAINT PK_app_user PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_app_user_property REFERENCES inv.property,
  login_name VARCHAR(40) NOT NULL CONSTRAINT UQ_app_user_login UNIQUE,   -- lower-case, normalized by the application
  display_name NVARCHAR(100) NOT NULL,
  enabled BIT NOT NULL CONSTRAINT DF_app_user_enabled DEFAULT 1,
  valid_from_utc DATETIME2(3) NULL,
  valid_until_utc DATETIME2(3) NULL,
  security_stamp INT NOT NULL CONSTRAINT DF_app_user_stamp DEFAULT 1,  -- bumped on disable/role/credential change: sessions die next request (AS-07)
  created_utc DATETIME2(3) NOT NULL,
  created_by INT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_app_user_login CHECK (LEN(login_name) >= 3 AND login_name NOT LIKE '%[^a-z0-9._-]%'),
  CONSTRAINT CK_app_user_validity CHECK (valid_until_utc IS NULL OR valid_from_utc IS NULL OR valid_until_utc > valid_from_utc));
CREATE TABLE plat.role(
  role_code VARCHAR(3) NOT NULL CONSTRAINT PK_role PRIMARY KEY,
  name NVARCHAR(60) NOT NULL,
  credential_kind VARCHAR(8) NOT NULL CONSTRAINT CK_role_credential CHECK (credential_kind IN ('PASSWORD', 'PIN')),  -- DEC-062
  owner_only_assign BIT NOT NULL,   -- DEC-058: roles holding money or admin permissions are assigned/revoked by an Owner only
  sort_order TINYINT NOT NULL);
CREATE TABLE plat.permission(
  permission_id VARCHAR(20) NOT NULL CONSTRAINT PK_permission PRIMARY KEY,
  resource_action VARCHAR(100) NOT NULL,
  kind VARCHAR(6) NOT NULL CONSTRAINT CK_permission_kind CHECK (kind IN ('ACTION', 'VIEW')),
  is_money BIT NOT NULL);
CREATE TABLE plat.role_grant(
  role_code VARCHAR(3) NOT NULL CONSTRAINT FK_role_grant_role REFERENCES plat.role,
  permission_id VARCHAR(20) NOT NULL CONSTRAINT FK_role_grant_permission REFERENCES plat.permission,
  mode CHAR(1) NOT NULL CONSTRAINT CK_role_grant_mode CHECK (mode IN ('A', 'L', 'R', 'V', 'M')),
  scope CHAR(1) NOT NULL CONSTRAINT CK_role_grant_scope CHECK (scope IN ('P', 'S', 'A', 'O')),
  limit_value BIGINT NULL,
  limit_unit VARCHAR(5) NULL CONSTRAINT CK_role_grant_unit CHECK (limit_unit IN ('PAISE', 'BPS')),
  CONSTRAINT PK_role_grant PRIMARY KEY (role_code, permission_id),
  CONSTRAINT CK_role_grant_limit CHECK ((limit_value IS NULL AND limit_unit IS NULL) OR (mode = 'L' AND limit_value >= 0 AND limit_unit IS NOT NULL)));
-- Explicit denies on a role (DEC-040 step 3: deny wins over any allow from any role).
CREATE TABLE plat.role_deny(
  role_code VARCHAR(3) NOT NULL CONSTRAINT FK_role_deny_role REFERENCES plat.role,
  permission_id VARCHAR(20) NOT NULL CONSTRAINT FK_role_deny_permission REFERENCES plat.permission,
  reason NVARCHAR(200) NOT NULL,
  CONSTRAINT PK_role_deny PRIMARY KEY (role_code, permission_id));
CREATE TABLE plat.user_role(
  user_id INT NOT NULL CONSTRAINT FK_user_role_user REFERENCES plat.app_user,
  role_code VARCHAR(3) NOT NULL CONSTRAINT FK_user_role_role REFERENCES plat.role,
  valid_from_utc DATETIME2(3) NOT NULL,
  valid_until_utc DATETIME2(3) NULL,
  assigned_by INT NULL,              -- NULL only for the bootstrap Owner
  assigned_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_user_role PRIMARY KEY (user_id, role_code),
  CONSTRAINT CK_user_role_validity CHECK (valid_until_utc IS NULL OR valid_until_utc > valid_from_utc),
  CONSTRAINT CK_user_role_not_self CHECK (assigned_by IS NULL OR assigned_by <> user_id));   -- PERM-SOD-002 backstop
CREATE INDEX IX_user_role_role ON plat.user_role(role_code, user_id);
-- Reasoned user-level exceptions with expiry (RBAC §1).
CREATE TABLE plat.user_exception(
  exception_id INT IDENTITY CONSTRAINT PK_user_exception PRIMARY KEY,
  user_id INT NOT NULL CONSTRAINT FK_user_exception_user REFERENCES plat.app_user,
  permission_id VARCHAR(20) NOT NULL CONSTRAINT FK_user_exception_permission REFERENCES plat.permission,
  effect VARCHAR(5) NOT NULL CONSTRAINT CK_user_exception_effect CHECK (effect IN ('ALLOW', 'DENY')),
  mode CHAR(1) NOT NULL CONSTRAINT CK_user_exception_mode CHECK (mode IN ('A', 'L', 'R', 'V', 'M')),
  scope CHAR(1) NOT NULL CONSTRAINT CK_user_exception_scope CHECK (scope IN ('P', 'S', 'A', 'O')),
  limit_value BIGINT NULL,
  limit_unit VARCHAR(5) NULL CONSTRAINT CK_user_exception_unit CHECK (limit_unit IN ('PAISE', 'BPS')),
  reason NVARCHAR(400) NOT NULL CONSTRAINT CK_user_exception_reason CHECK (LEN(reason) >= 3),
  valid_until_utc DATETIME2(3) NOT NULL,
  granted_by INT NOT NULL,
  granted_utc DATETIME2(3) NOT NULL,
  revoked_utc DATETIME2(3) NULL,
  revoked_by INT NULL,
  CONSTRAINT CK_user_exception_not_self CHECK (granted_by <> user_id),
  CONSTRAINT CK_user_exception_limit CHECK ((limit_value IS NULL AND limit_unit IS NULL) OR (mode = 'L' AND limit_value >= 0 AND limit_unit IS NOT NULL)));
CREATE INDEX IX_user_exception_user ON plat.user_exception(user_id) WHERE revoked_utc IS NULL;
-- One credential per user and kind (DEC-062: PASSWORD for OWN/ACC/TAD, PIN for other staff). Lockout state lives here.
CREATE TABLE plat.credential(
  user_id INT NOT NULL CONSTRAINT FK_credential_user REFERENCES plat.app_user,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_credential_kind CHECK (kind IN ('PASSWORD', 'PIN')),
  algorithm VARCHAR(20) NOT NULL,
  iterations INT NOT NULL CONSTRAINT CK_credential_iterations CHECK (iterations >= 1000),
  salt VARBINARY(32) NOT NULL,
  secret_hash VARBINARY(64) NOT NULL,
  failed_count INT NOT NULL CONSTRAINT DF_credential_failed DEFAULT 0,
  locked_until_utc DATETIME2(3) NULL,
  changed_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_credential PRIMARY KEY (user_id, kind));
CREATE TABLE plat.pin_history(
  pin_history_id INT IDENTITY CONSTRAINT PK_pin_history PRIMARY KEY,
  user_id INT NOT NULL CONSTRAINT FK_pin_history_user REFERENCES plat.app_user,
  algorithm VARCHAR(20) NOT NULL,
  iterations INT NOT NULL,
  salt VARBINARY(32) NOT NULL,
  secret_hash VARBINARY(64) NOT NULL,
  changed_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_pin_history_user ON plat.pin_history(user_id, pin_history_id DESC);
-- Registered shared tablets/phones (DEC-062 device binding). The device keeps a random token; only its SHA-256 is stored.
CREATE TABLE plat.device(
  device_id INT IDENTITY CONSTRAINT PK_device PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_device_property REFERENCES inv.property,
  label NVARCHAR(60) NOT NULL,
  token_sha256 BINARY(32) NOT NULL CONSTRAINT UQ_device_token UNIQUE,
  registered_by INT NOT NULL,
  registered_utc DATETIME2(3) NOT NULL,
  last_seen_utc DATETIME2(3) NULL,
  revoked_utc DATETIME2(3) NULL,
  revoked_by INT NULL);
-- Opaque server sessions (contracts §1): token SHA-256 only; idle and absolute expiry checked against the injected clock.
CREATE TABLE plat.session(
  session_id BIGINT IDENTITY CONSTRAINT PK_session PRIMARY KEY,
  token_sha256 BINARY(32) NOT NULL CONSTRAINT UQ_session_token UNIQUE,
  user_id INT NOT NULL CONSTRAINT FK_session_user REFERENCES plat.app_user,
  property_id INT NOT NULL,
  credential_kind VARCHAR(8) NOT NULL CONSTRAINT CK_session_kind CHECK (credential_kind IN ('PASSWORD', 'PIN')),
  device_id INT NULL CONSTRAINT FK_session_device REFERENCES plat.device,
  security_stamp INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  last_seen_utc DATETIME2(3) NOT NULL,
  absolute_expires_utc DATETIME2(3) NOT NULL,
  revoked_utc DATETIME2(3) NULL,
  revoked_reason VARCHAR(30) NULL,
  CONSTRAINT CK_session_pin_device CHECK (credential_kind = 'PASSWORD' OR device_id IS NOT NULL));
CREATE INDEX IX_session_user_open ON plat.session(user_id) WHERE revoked_utc IS NULL;
CREATE INDEX IX_session_device_open ON plat.session(device_id) WHERE revoked_utc IS NULL;
