-- HCC migration 1300 · W2-7 Maintenance (block 1300–1399). Prototype module 15 (maintenance.ts): vendors, assets, AMC contracts,
-- spares, preventive plans, work orders (parts, history). Downtime blocks rooms through ops.room_block + inv.room_block
-- (Housekeeping store + IRoomBlockPort), never by writing those tables from here. Uses 0002/0300 tables as they are.
-- No permissions are added: maintenance rides the existing PERM-HK-001/002/004, PERM-ROOM-001/002 and PERM-RPT-002 grants
-- (RBAC matrix v1 §5 + DEC-063); a dedicated PERM-MNT-* set is a decision listed in the W2-7 report.
-- Money columns (unit_cost_paise, value_paise) are never selected for readers without finance rights (DEC-063 G12).
CREATE SCHEMA mnt;
GO
CREATE TABLE mnt.vendor(
  vendor_id INT IDENTITY CONSTRAINT PK_mnt_vendor PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mnt_vendor_property REFERENCES inv.property,
  name NVARCHAR(120) NOT NULL,
  trade NVARCHAR(60) NOT NULL,
  phone VARCHAR(30) NOT NULL,
  row_ver ROWVERSION);

CREATE TABLE mnt.asset(
  asset_id INT IDENTITY CONSTRAINT PK_mnt_asset PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mnt_asset_property REFERENCES inv.property,
  name NVARCHAR(120) NOT NULL,
  kind VARCHAR(12) NOT NULL CONSTRAINT CK_mnt_asset_kind CHECK (kind IN ('AC','Geyser','TV','Lift','Generator','Pump','RO plant','Other')),
  room_id INT NULL CONSTRAINT FK_mnt_asset_room REFERENCES inv.room,
  area NVARCHAR(60) NULL,
  make NVARCHAR(60) NOT NULL CONSTRAINT DF_mnt_asset_make DEFAULT N'',
  model NVARCHAR(60) NOT NULL CONSTRAINT DF_mnt_asset_model DEFAULT N'',
  installed_on DATE NULL,
  vendor_id INT NULL CONSTRAINT FK_mnt_asset_vendor REFERENCES mnt.vendor,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_mnt_asset_status CHECK (status IN ('Working','Faulty','Retired')),
  row_ver ROWVERSION,
  CONSTRAINT CK_mnt_asset_location CHECK (room_id IS NOT NULL OR area IS NOT NULL));

CREATE TABLE mnt.amc(
  amc_id INT IDENTITY CONSTRAINT PK_mnt_amc PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mnt_amc_property REFERENCES inv.property,
  vendor_id INT NOT NULL CONSTRAINT FK_mnt_amc_vendor REFERENCES mnt.vendor,
  scope NVARCHAR(200) NOT NULL,
  from_date DATE NOT NULL,
  to_date DATE NOT NULL,
  visits TINYINT NOT NULL CONSTRAINT CK_mnt_amc_visits CHECK (visits <= 52),
  value_paise BIGINT NOT NULL CONSTRAINT CK_mnt_amc_value CHECK (value_paise > 0),   -- money (DEC-063 G12)
  row_ver ROWVERSION,
  CONSTRAINT CK_mnt_amc_dates CHECK (to_date > from_date));

CREATE TABLE mnt.spare(
  spare_id INT IDENTITY CONSTRAINT PK_mnt_spare PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mnt_spare_property REFERENCES inv.property,
  name NVARCHAR(120) NOT NULL,
  unit VARCHAR(10) NOT NULL,
  stock INT NOT NULL CONSTRAINT CK_mnt_spare_stock CHECK (stock >= 0),
  reorder_at INT NOT NULL CONSTRAINT CK_mnt_spare_reorder CHECK (reorder_at >= 0),
  cost_paise BIGINT NOT NULL CONSTRAINT CK_mnt_spare_cost CHECK (cost_paise >= 0),   -- money (DEC-063 G12)
  row_ver ROWVERSION);

CREATE TABLE mnt.pm_plan(
  plan_id INT IDENTITY CONSTRAINT PK_mnt_pm_plan PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mnt_pm_plan_property REFERENCES inv.property,
  asset_id INT NOT NULL CONSTRAINT FK_mnt_pm_plan_asset REFERENCES mnt.asset,
  title NVARCHAR(200) NOT NULL,
  every_days SMALLINT NOT NULL CONSTRAINT CK_mnt_pm_plan_every CHECK (every_days BETWEEN 1 AND 365),
  next_due DATE NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mnt_pm_plan_active DEFAULT 1,
  last_done DATE NULL,
  row_ver ROWVERSION);

CREATE TABLE mnt.work_order(
  wo_id BIGINT IDENTITY CONSTRAINT PK_mnt_work_order PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mnt_work_order_property REFERENCES inv.property,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_mnt_wo_kind CHECK (kind IN ('Corrective','Preventive')),
  title NVARCHAR(200) NOT NULL,
  priority CHAR(2) NOT NULL CONSTRAINT CK_mnt_wo_priority CHECK (priority IN ('P1','P2','P3','P4')),
  status VARCHAR(10) NOT NULL CONSTRAINT CK_mnt_wo_status CHECK (status IN ('Open','InProgress','OnHold','Done','Verified','Cancelled')),
  room_id INT NULL CONSTRAINT FK_mnt_wo_room REFERENCES inv.room,
  asset_id INT NULL CONSTRAINT FK_mnt_wo_asset REFERENCES mnt.asset,
  area NVARCHAR(60) NULL,
  assignee_id INT NULL,                          -- principal (plat.app_user); NULL = unassigned or external vendor
  vendor_id INT NULL CONSTRAINT FK_mnt_wo_vendor REFERENCES mnt.vendor,
  created_utc DATETIME2(3) NOT NULL,
  created_by INT NOT NULL,
  started_utc DATETIME2(3) NULL,
  done_utc DATETIME2(3) NULL,
  done_by INT NULL,
  verified_utc DATETIME2(3) NULL,
  verified_by INT NULL,
  hold_reason NVARCHAR(200) NULL,
  awaiting_spare_id INT NULL CONSTRAINT FK_mnt_wo_spare REFERENCES mnt.spare,
  note NVARCHAR(500) NULL,
  return_reason NVARCHAR(500) NULL,
  cancel_reason NVARCHAR(200) NULL,
  repair_task_id BIGINT NULL CONSTRAINT FK_mnt_wo_repair_task REFERENCES ops.hk_task,
  plan_id INT NULL CONSTRAINT FK_mnt_wo_plan REFERENCES mnt.pm_plan,
  due_date DATE NULL,
  downtime_from_utc DATETIME2(3) NULL,
  downtime_to_utc DATETIME2(3) NULL,
  block_id BIGINT NULL CONSTRAINT FK_mnt_wo_block REFERENCES ops.room_block,
  row_ver ROWVERSION,
  CONSTRAINT CK_mnt_wo_location CHECK (room_id IS NOT NULL OR area IS NOT NULL),
  CONSTRAINT CK_mnt_wo_assignment CHECK (assignee_id IS NULL OR vendor_id IS NULL));
-- One work order per repair task; one per plan occurrence (PM generation is repeat-safe even under a race).
CREATE UNIQUE INDEX UX_mnt_wo_repair_task ON mnt.work_order(repair_task_id) WHERE repair_task_id IS NOT NULL;
CREATE UNIQUE INDEX UX_mnt_wo_plan_due ON mnt.work_order(plan_id, due_date) WHERE plan_id IS NOT NULL;
CREATE INDEX IX_mnt_wo_status ON mnt.work_order(property_id, status);
CREATE INDEX IX_mnt_wo_assignee ON mnt.work_order(assignee_id, status) WHERE assignee_id IS NOT NULL;
CREATE INDEX IX_mnt_wo_room ON mnt.work_order(room_id) WHERE room_id IS NOT NULL;

-- Parts booked on a work order; the unit cost is a snapshot at booking time (money, never sent to Maintenance).
CREATE TABLE mnt.work_order_part(
  wo_id BIGINT NOT NULL CONSTRAINT FK_mnt_wop_wo REFERENCES mnt.work_order,
  spare_id INT NOT NULL CONSTRAINT FK_mnt_wop_spare REFERENCES mnt.spare,
  unit_cost_paise BIGINT NOT NULL,
  qty INT NOT NULL CONSTRAINT CK_mnt_wop_qty CHECK (qty BETWEEN 1 AND 9999),
  CONSTRAINT PK_mnt_work_order_part PRIMARY KEY (wo_id, spare_id, unit_cost_paise));

CREATE TABLE mnt.work_order_history(
  history_id BIGINT IDENTITY CONSTRAINT PK_mnt_wo_history PRIMARY KEY,
  wo_id BIGINT NOT NULL CONSTRAINT FK_mnt_woh_wo REFERENCES mnt.work_order,
  at_utc DATETIME2(3) NOT NULL,
  by_user INT NOT NULL,
  action VARCHAR(40) NOT NULL,
  note NVARCHAR(500) NULL);
CREATE INDEX IX_mnt_woh_wo ON mnt.work_order_history(wo_id, history_id);
