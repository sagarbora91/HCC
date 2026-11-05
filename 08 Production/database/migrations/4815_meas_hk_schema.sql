-- HCC migration 4815 · F12 MEAS meas-cap-hk (MEAS-CONTRACT §2 "Server mirror"): housekeeping / engineering capture tables in schema meas.
-- task_times (start / finish / inspect times per housekeeping task), room_offline (why a room is out of order / service), meter + meter_reading
-- (utilities, monotonic per meter), asset_life (expected life, grade, replacement cost in paise), discard (linen discards). property_id on every
-- table; client_id (the command key) makes every insert idempotent. Money = integer paise (asset_life.replacement_cost_paise only; Housekeeping and
-- Maintenance never read it: DEC-061). Numbered inside the meas-cap-hk block 4815-4819; out of order below the highest locked version (accepted).
IF SCHEMA_ID('meas') IS NULL EXEC('CREATE SCHEMA meas');
GO
CREATE TABLE meas.task_times(
  property_id INT NOT NULL CONSTRAINT FK_meas_task_times_property REFERENCES inv.property,
  task_no VARCHAR(60) NOT NULL,
  attendant_id INT NULL,
  started_utc DATETIME2(3) NULL,
  finished_utc DATETIME2(3) NULL,
  inspected_utc DATETIME2(3) NULL,
  inspect_result VARCHAR(4) NULL CONSTRAINT CK_meas_task_times_result CHECK (inspect_result IS NULL OR inspect_result IN ('pass', 'fail')),
  reject_reason NVARCHAR(200) NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_task_times_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_meas_task_times PRIMARY KEY (property_id, task_no),
  CONSTRAINT CK_meas_task_times_order CHECK (finished_utc IS NULL OR started_utc IS NULL OR finished_utc >= started_utc));
CREATE TABLE meas.room_offline(
  property_id INT NOT NULL CONSTRAINT FK_meas_room_offline_property REFERENCES inv.property,
  room_id INT NOT NULL CONSTRAINT FK_meas_room_offline_room REFERENCES inv.room,
  reason VARCHAR(12) NOT NULL CONSTRAINT CK_meas_room_offline_reason CHECK (reason IN ('OOO', 'OOS', 'house-use', 'renovation')),
  expected_return DATE NULL,
  work_order_no VARCHAR(30) NULL,
  note NVARCHAR(200) NULL,
  set_by INT NULL,
  set_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_room_offline_set DEFAULT SYSUTCDATETIME(),
  client_id VARCHAR(80) NULL,
  CONSTRAINT PK_meas_room_offline PRIMARY KEY (property_id, room_id));
CREATE UNIQUE INDEX UX_meas_room_offline_client ON meas.room_offline(property_id, client_id) WHERE client_id IS NOT NULL;
CREATE TABLE meas.meter(
  property_id INT NOT NULL CONSTRAINT FK_meas_meter_property REFERENCES inv.property,
  meter_id VARCHAR(30) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  kind VARCHAR(12) NOT NULL CONSTRAINT CK_meas_meter_kind CHECK (kind IN ('electricity', 'water', 'lpg', 'diesel', 'dg-hours')),
  unit VARCHAR(16) NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_meas_meter_active DEFAULT 1,
  client_id VARCHAR(80) NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_meas_meter PRIMARY KEY (property_id, meter_id));
CREATE UNIQUE INDEX UX_meas_meter_name ON meas.meter(property_id, name);
CREATE UNIQUE INDEX UX_meas_meter_client ON meas.meter(property_id, client_id) WHERE client_id IS NOT NULL;
CREATE TABLE meas.meter_reading(
  reading_id BIGINT IDENTITY CONSTRAINT PK_meas_meter_reading PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_meter_reading_property REFERENCES inv.property,
  meter_id VARCHAR(30) NOT NULL,
  reading_date DATE NOT NULL,
  value DECIMAL(14, 2) NOT NULL CONSTRAINT CK_meas_meter_reading_value CHECK (value >= 0),
  entered_by INT NOT NULL,
  photo_ref NVARCHAR(200) NULL,
  entered_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_meter_reading_entered DEFAULT SYSUTCDATETIME(),
  client_id VARCHAR(80) NULL,
  CONSTRAINT FK_meas_meter_reading_meter FOREIGN KEY (property_id, meter_id) REFERENCES meas.meter(property_id, meter_id));
CREATE INDEX IX_meas_meter_reading_meter ON meas.meter_reading(property_id, meter_id, reading_date, reading_id);
CREATE UNIQUE INDEX UX_meas_meter_reading_client ON meas.meter_reading(property_id, client_id) WHERE client_id IS NOT NULL;
CREATE TABLE meas.asset_life(
  property_id INT NOT NULL CONSTRAINT FK_meas_asset_life_property REFERENCES inv.property,
  asset_id VARCHAR(30) NOT NULL,
  expected_life_months INT NULL CONSTRAINT CK_meas_asset_life_months CHECK (expected_life_months IS NULL OR expected_life_months BETWEEN 1 AND 600),
  condition_grade CHAR(1) NULL CONSTRAINT CK_meas_asset_life_grade CHECK (condition_grade IS NULL OR condition_grade IN ('A', 'B', 'C', 'D')),
  replacement_cost_paise BIGINT NULL CONSTRAINT CK_meas_asset_life_cost CHECK (replacement_cost_paise IS NULL OR replacement_cost_paise >= 0),
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_asset_life_updated DEFAULT SYSUTCDATETIME(),
  client_id VARCHAR(80) NULL,
  CONSTRAINT PK_meas_asset_life PRIMARY KEY (property_id, asset_id));
CREATE UNIQUE INDEX UX_meas_asset_life_client ON meas.asset_life(property_id, client_id) WHERE client_id IS NOT NULL;
CREATE TABLE meas.discard(
  discard_id BIGINT IDENTITY CONSTRAINT PK_meas_discard PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meas_discard_property REFERENCES inv.property,
  item NVARCHAR(60) NOT NULL,
  qty INT NOT NULL CONSTRAINT CK_meas_discard_qty CHECK (qty BETWEEN 1 AND 1000),
  reason NVARCHAR(60) NOT NULL,
  discard_date DATE NOT NULL,
  entered_by INT NOT NULL,
  entered_utc DATETIME2(3) NOT NULL CONSTRAINT DF_meas_discard_entered DEFAULT SYSUTCDATETIME(),
  client_id VARCHAR(80) NULL);
CREATE INDEX IX_meas_discard_date ON meas.discard(property_id, discard_date);
CREATE UNIQUE INDEX UX_meas_discard_client ON meas.discard(property_id, client_id) WHERE client_id IS NOT NULL;
