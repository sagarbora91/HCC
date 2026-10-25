-- HCC migration 1400 · W2-8 linen, laundry & stock (block 1400–1499), schema stk. Original HCC design; rules ported from the
-- prototype module 16 (03 Prototype/src/domain/streams/inventory.ts). Folder name Stock (the name Inventory is room inventory).
-- Uses inv.property and inv.room_type from 0002 only (FKs); adds nothing to other modules' tables. Money = BIGINT paise.
-- Stock is held per item per location in stk.stock (qty >= 0 is a CHECK, so no interleaving can drive a location negative);
-- every command locks the item rows it touches (UPDLOCK on stk.item, ascending item_id) before it reads or writes quantities.
CREATE SCHEMA stk;
GO
CREATE TABLE stk.item(
  item_id INT IDENTITY CONSTRAINT PK_stk_item PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stk_item_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(120) NOT NULL,
  kind VARCHAR(10) NOT NULL CONSTRAINT CK_stk_item_kind CHECK (kind IN ('linen','amenity','cleaning')),
  unit VARCHAR(20) NOT NULL,
  -- Standard (weighted-average) cost per unit; shown only to money roles (PERM-RPT-002).
  cost_paise BIGINT NOT NULL CONSTRAINT DF_stk_item_cost DEFAULT 0 CONSTRAINT CK_stk_item_cost CHECK (cost_paise BETWEEN 0 AND 1000000000),
  min_qty INT NOT NULL CONSTRAINT CK_stk_item_min CHECK (min_qty BETWEEN 0 AND 10000),
  reorder_qty INT NOT NULL CONSTRAINT CK_stk_item_reorder CHECK (reorder_qty BETWEEN 0 AND 10000),
  -- Standard use per occupied room-night x 100 (150 = 1.5).
  std_x100 INT NULL CONSTRAINT CK_stk_item_std CHECK (std_x100 IS NULL OR std_x100 BETWEEN 0 AND 10000),
  laundry_rate_paise BIGINT NULL CONSTRAINT CK_stk_item_laundry CHECK (laundry_rate_paise IS NULL OR laundry_rate_paise BETWEEN 0 AND 1000000000),
  supplier NVARCHAR(120) NULL,
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_stk_item_code UNIQUE (property_id, code),
  CONSTRAINT CK_stk_item_laundry_linen CHECK (laundry_rate_paise IS NULL OR kind = 'linen'));
-- Linen pieces per room by room type (linen only; enforced by the application).
CREATE TABLE stk.item_par(
  item_id INT NOT NULL CONSTRAINT FK_stk_item_par_item REFERENCES stk.item,
  room_type_id INT NOT NULL CONSTRAINT FK_stk_item_par_room_type REFERENCES inv.room_type,
  pieces TINYINT NOT NULL CONSTRAINT CK_stk_item_par CHECK (pieces <= 20),
  CONSTRAINT PK_stk_item_par PRIMARY KEY (item_id, room_type_id));
CREATE TABLE stk.stock(
  item_id INT NOT NULL CONSTRAINT FK_stk_stock_item REFERENCES stk.item,
  loc VARCHAR(8) NOT NULL CONSTRAINT CK_stk_stock_loc CHECK (loc IN ('store','floor1','floor2','floor3','rooms','soiled','laundry')),
  qty INT NOT NULL CONSTRAINT CK_stk_stock_qty CHECK (qty >= 0),
  CONSTRAINT PK_stk_stock PRIMARY KEY (item_id, loc));
CREATE TABLE stk.setting(
  property_id INT NOT NULL CONSTRAINT PK_stk_setting PRIMARY KEY CONSTRAINT FK_stk_setting_property REFERENCES inv.property,
  par_sets TINYINT NOT NULL CONSTRAINT DF_stk_setting_sets DEFAULT 3 CONSTRAINT CK_stk_setting_sets CHECK (par_sets BETWEEN 1 AND 10),
  vendor_name NVARCHAR(120) NULL,
  turnaround_days TINYINT NOT NULL CONSTRAINT DF_stk_setting_turn DEFAULT 1 CONSTRAINT CK_stk_setting_turn CHECK (turnaround_days BETWEEN 0 AND 30),
  row_ver ROWVERSION);
-- Occupied room-nights before go-live (used when no stay covers the date). Live nights come from inv.room_night.
CREATE TABLE stk.occupancy_history(
  property_id INT NOT NULL CONSTRAINT FK_stk_occ_property REFERENCES inv.property,
  night DATE NOT NULL,
  rooms SMALLINT NOT NULL CONSTRAINT CK_stk_occ_rooms CHECK (rooms >= 0),
  CONSTRAINT PK_stk_occupancy_history PRIMARY KEY (property_id, night));
CREATE TABLE stk.grn(
  grn_id BIGINT IDENTITY CONSTRAINT PK_stk_grn PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stk_grn_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  supplier NVARCHAR(120) NOT NULL,
  ref NVARCHAR(60) NOT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL);
CREATE TABLE stk.grn_line(
  grn_id BIGINT NOT NULL CONSTRAINT FK_stk_grn_line_grn REFERENCES stk.grn,
  item_id INT NOT NULL CONSTRAINT FK_stk_grn_line_item REFERENCES stk.item,
  qty INT NOT NULL CONSTRAINT CK_stk_grn_line_qty CHECK (qty BETWEEN 1 AND 10000),
  cost_paise BIGINT NOT NULL CONSTRAINT CK_stk_grn_line_cost CHECK (cost_paise >= 0),
  CONSTRAINT PK_stk_grn_line PRIMARY KEY (grn_id, item_id));
-- Movement ledger. qty is signed only for kind count (variance); to_loc may be 'used'.
CREATE TABLE stk.move(
  move_id BIGINT IDENTITY CONSTRAINT PK_stk_move PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stk_move_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  at_utc DATETIME2(3) NOT NULL,
  kind VARCHAR(14) NOT NULL CONSTRAINT CK_stk_move_kind CHECK (kind IN ('grn','issue','use','room-change','laundry-out','laundry-in','loss','count')),
  item_id INT NOT NULL CONSTRAINT FK_stk_move_item REFERENCES stk.item,
  qty INT NOT NULL,
  from_loc NVARCHAR(120) NULL,
  to_loc NVARCHAR(40) NULL,
  ref VARCHAR(40) NULL,
  reason NVARCHAR(200) NULL,
  actor_id INT NOT NULL);
CREATE INDEX IX_stk_move_date ON stk.move(property_id, business_date, item_id);
CREATE TABLE stk.laundry_batch(
  batch_id BIGINT IDENTITY CONSTRAINT PK_stk_laundry_batch PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stk_laundry_batch_property REFERENCES inv.property,
  vendor NVARCHAR(120) NOT NULL,
  sent_date DATE NOT NULL,
  sent_utc DATETIME2(3) NOT NULL,
  status VARCHAR(14) NOT NULL CONSTRAINT CK_stk_laundry_batch_status CHECK (status IN ('Out','Returned','Returned short')),
  returned_date DATE NULL,
  actor_id INT NOT NULL,
  received_by INT NULL,
  note NVARCHAR(400) NULL,
  charge_paise BIGINT NULL CONSTRAINT CK_stk_laundry_batch_charge CHECK (charge_paise IS NULL OR charge_paise >= 0),
  row_ver ROWVERSION);
CREATE TABLE stk.laundry_line(
  batch_id BIGINT NOT NULL CONSTRAINT FK_stk_laundry_line_batch REFERENCES stk.laundry_batch,
  item_id INT NOT NULL CONSTRAINT FK_stk_laundry_line_item REFERENCES stk.item,
  sent INT NOT NULL CONSTRAINT CK_stk_laundry_line_sent CHECK (sent BETWEEN 1 AND 10000),
  received INT NULL CONSTRAINT CK_stk_laundry_line_received CHECK (received IS NULL OR received >= 0),
  damaged INT NULL CONSTRAINT CK_stk_laundry_line_damaged CHECK (damaged IS NULL OR damaged >= 0),
  CONSTRAINT PK_stk_laundry_line PRIMARY KEY (batch_id, item_id),
  CONSTRAINT CK_stk_laundry_line_total CHECK (received IS NULL OR received + ISNULL(damaged, 0) <= sent));
CREATE TABLE stk.loss(
  loss_id BIGINT IDENTITY CONSTRAINT PK_stk_loss PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stk_loss_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  item_id INT NOT NULL CONSTRAINT FK_stk_loss_item REFERENCES stk.item,
  loc VARCHAR(8) NOT NULL CONSTRAINT CK_stk_loss_loc CHECK (loc IN ('store','floor1','floor2','floor3','rooms','soiled','laundry')),
  qty INT NOT NULL CONSTRAINT CK_stk_loss_qty CHECK (qty BETWEEN 1 AND 10000),
  reason NVARCHAR(100) NOT NULL,
  note NVARCHAR(400) NULL,
  actor_id INT NOT NULL,
  value_paise BIGINT NOT NULL CONSTRAINT CK_stk_loss_value CHECK (value_paise >= 0),
  ref VARCHAR(40) NULL,
  created_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_stk_loss_date ON stk.loss(property_id, business_date);
CREATE TABLE stk.count(
  count_id BIGINT IDENTITY CONSTRAINT PK_stk_count PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stk_count_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  at_utc DATETIME2(3) NOT NULL,
  loc VARCHAR(8) NOT NULL CONSTRAINT CK_stk_count_loc CHECK (loc IN ('store','floor1','floor2','floor3')),
  status VARCHAR(10) NOT NULL CONSTRAINT CK_stk_count_status CHECK (status IN ('Submitted','Posted','Rejected')),
  actor_id INT NOT NULL,
  reviewer_id INT NULL,
  reason NVARCHAR(400) NULL,
  note NVARCHAR(400) NULL,
  row_ver ROWVERSION);
-- One count per location awaiting review (prototype rule); the filtered unique index is the race backstop.
CREATE UNIQUE INDEX UQ_stk_count_pending ON stk.count(property_id, loc) WHERE status = 'Submitted';
CREATE TABLE stk.count_line(
  count_id BIGINT NOT NULL CONSTRAINT FK_stk_count_line_count REFERENCES stk.count,
  item_id INT NOT NULL CONSTRAINT FK_stk_count_line_item REFERENCES stk.item,
  system_qty INT NOT NULL,
  counted_qty INT NOT NULL CONSTRAINT CK_stk_count_line_counted CHECK (counted_qty BETWEEN 0 AND 10000),
  CONSTRAINT PK_stk_count_line PRIMARY KEY (count_id, item_id));
