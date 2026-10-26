-- HCC migration 2801 · MA-0 masters framework + Floors (block 2800–2809). Plan: 02 Planning/HCC_Customisation_Features_Plan_v1.md
-- Part 2 §9 (common framework) and §8 M1 (floors). Prototype rules: 03 Prototype/src/domain/streams/masters.ts (+ handlers/masters.ts).
-- Row rules: code unique per property (also for retired rows), ^[A-Z0-9-]{1,20}$ checked by the application, immutable once used;
-- name 1–60; retire (active = 0, retired_on) instead of deleting a floor that rooms use; delete only when unused.
-- No column on inv.room (owned by Inventory; MA-1 adds floor_id in 2810): until then the floor of a room is the hundreds digit of
-- its number (101 → F1), exactly as the production sync layer (sync/rooms.ts floorOf) shows it.
CREATE SCHEMA mst;
GO
CREATE TABLE mst.floor(
  floor_id INT IDENTITY CONSTRAINT PK_mst_floor PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_floor_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_floor_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_mst_floor_code UNIQUE (property_id, code),
  CONSTRAINT CK_mst_floor_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_mst_floor_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_mst_floor_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_mst_floor_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_mst_floor_order ON mst.floor(property_id, sort_order);

-- Change history of every master (one row per changed field, "who, when, old → new"); shared by MA-1 … MA-7 (master = list id).
CREATE TABLE mst.master_history(
  history_id BIGINT IDENTITY CONSTRAINT PK_mst_master_history PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_master_history_property REFERENCES inv.property,
  master VARCHAR(30) NOT NULL,
  row_id BIGINT NOT NULL,
  code VARCHAR(20) NULL,
  field VARCHAR(40) NOT NULL,
  old_value NVARCHAR(200) NULL,
  new_value NVARCHAR(200) NULL,
  changed_by INT NOT NULL,
  changed_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_mst_master_history_row ON mst.master_history(property_id, master, row_id, history_id DESC);
CREATE INDEX IX_mst_master_history_master ON mst.master_history(property_id, master, history_id DESC);
GO
-- Seed from the rooms that already exist (prototype fixture rule: floors 1..n named "Floor n", sort order 10, 20, …): an install that
-- has rooms gets its floors; a fresh database gets none (the boards then show "Floor n" until the Owner / Tech admin adds them).
INSERT mst.floor(property_id, code, name, sort_order, active, updated_by, updated_utc)
SELECT f.property_id, CONCAT('F', f.n), CONCAT(N'Floor ', f.n), f.n * 10, 1, 0, SYSUTCDATETIME()
  FROM (SELECT DISTINCT r.property_id,
               IIF(r.room_no NOT LIKE '%[^0-9]%' AND LEN(r.room_no) >= 3, IIF(CAST(r.room_no AS INT) / 100 < 1, 1, CAST(r.room_no AS INT) / 100), 1) AS n
          FROM inv.room r) f;
