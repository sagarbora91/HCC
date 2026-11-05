-- HCC migration 4812 · F12 MEAS cap-desk: customer preferences + blacklist (reason and Owner approval), keyed by the Customer 360 key.
CREATE TABLE meas.customer_preference(
  property_id INT NOT NULL CONSTRAINT FK_meas_customer_preference_property REFERENCES inv.property,
  customer_key NVARCHAR(80) NOT NULL,
  floor NVARCHAR(80) NULL, room_kind NVARCHAR(80) NULL, meal NVARCHAR(80) NULL, pillow NVARCHAR(80) NULL, bill_to_company NVARCHAR(80) NULL, note NVARCHAR(80) NULL,
  do_not_disturb BIT NOT NULL CONSTRAINT DF_meas_customer_preference_dnd DEFAULT 0,
  blacklist_reason NVARCHAR(200) NULL,
  blacklist_approved_by INT NULL,
  blacklist_utc DATETIME2(3) NULL,
  loyalty_tag NVARCHAR(40) NULL, -- written by cap-guest (MEAS_LOYALTY_SET)
  changed_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_customer_preference_utc DEFAULT SYSUTCDATETIME(),
  changed_by INT NOT NULL,
  CONSTRAINT PK_meas_customer_preference PRIMARY KEY (property_id, customer_key),
  CONSTRAINT CK_meas_customer_preference_blacklist CHECK ((blacklist_reason IS NULL AND blacklist_approved_by IS NULL) OR (blacklist_reason IS NOT NULL AND blacklist_approved_by IS NOT NULL)));
GO
-- Append-only audit of preference and blacklist changes (who, what, when; the blacklist set / clear carries the approver).
CREATE TABLE meas.customer_preference_log(
  log_id BIGINT IDENTITY CONSTRAINT PK_meas_customer_preference_log PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_customer_preference_log_property REFERENCES inv.property,
  client_id NVARCHAR(64) NOT NULL,
  customer_key NVARCHAR(80) NOT NULL,
  action VARCHAR(20) NOT NULL CONSTRAINT CK_meas_customer_preference_log_action CHECK (action IN ('SET','BLACKLIST','BLACKLIST_CLEAR')),
  detail NVARCHAR(200) NULL,
  approved_by INT NULL,
  recorded_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_customer_preference_log_utc DEFAULT SYSUTCDATETIME(),
  recorded_by INT NOT NULL,
  CONSTRAINT UQ_meas_customer_preference_log_client UNIQUE (property_id, client_id));
