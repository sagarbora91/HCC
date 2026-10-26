-- HCC migration 2810 · MA-1 rooms and room types masters (block 2810–2819). Plan: 02 Planning/HCC_Customisation_Features_Plan_v1.md
-- Part 2 §8 M2–M5; prototype rules: 03 Prototype/src/domain/streams/roommasters.ts. Module RoomMasters (Application/Infrastructure
-- RoomMasters/*). Extends the Inventory tables of 0002/0200 (never edits them):
--   inv.room_type  + occupancy split (max adults / children; max_occupancy stays adults + children), extra bed, bed type, size,
--                    amenity codes (JSON list), description, active / retired_on, updated_by / updated_utc, row_ver (STALE_VERSION).
--                    Rack rate = rack_rate_paise (base) + inv.room_type_tariff (dated) — the same schedule pricing reads.
--   inv.room       + floor_id → mst.floor (MA-0), smoking, connecting room, extension, notes, amenity codes, active / retired_on and the
--                    open-ended inventory block placed when the room is retired (retire_block_id).
--   inv.amenity    (M4) room features picked by room types and rooms.
--   inv.occupancy_charge + inv.occupancy_charge_rate (M5) extra adult / child / bed per room type, dated amounts, child age band.
ALTER TABLE inv.room_type ADD
  max_adults TINYINT NULL,
  max_children TINYINT NOT NULL CONSTRAINT DF_room_type_max_children DEFAULT 0,
  extra_bed_allowed BIT NOT NULL CONSTRAINT DF_room_type_extra_bed DEFAULT 0,
  bed_type NVARCHAR(20) NULL,
  size_sqft INT NULL,
  amenities_json NVARCHAR(1000) NOT NULL CONSTRAINT DF_room_type_amenities DEFAULT N'[]',
  description NVARCHAR(500) NULL,
  active BIT NOT NULL CONSTRAINT DF_room_type_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL,
  row_ver ROWVERSION;
GO
-- max_adults NULL = a type written before MA-1 or by the import: its adults are max_occupancy − max_children (readers COALESCE).
UPDATE inv.room_type SET max_adults = max_occupancy;
ALTER TABLE inv.room_type ADD
  CONSTRAINT CK_room_type_adults CHECK ((max_adults IS NULL OR max_adults BETWEEN 1 AND 10) AND max_children BETWEEN 0 AND 10),
  CONSTRAINT CK_room_type_size CHECK (size_sqft IS NULL OR size_sqft BETWEEN 1 AND 100000),
  CONSTRAINT CK_room_type_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL));
GO
ALTER TABLE inv.room ADD
  floor_id INT NULL CONSTRAINT FK_room_floor REFERENCES mst.floor,
  smoking BIT NOT NULL CONSTRAINT DF_room_smoking DEFAULT 0,
  connecting_room_no VARCHAR(10) NULL,
  extension VARCHAR(6) NULL,
  notes NVARCHAR(200) NULL,
  features_json NVARCHAR(1000) NOT NULL CONSTRAINT DF_room_features DEFAULT N'[]',
  active BIT NOT NULL CONSTRAINT DF_room_active DEFAULT 1,
  retired_on DATE NULL,
  retire_block_id BIGINT NULL CONSTRAINT FK_room_retire_block REFERENCES inv.room_block;
GO
ALTER TABLE inv.room ADD CONSTRAINT CK_room_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL));
CREATE INDEX IX_room_floor ON inv.room(floor_id) WHERE floor_id IS NOT NULL;
GO
-- Existing rooms get the floor MA-0 shows them on (hundreds digit of the number → floor code F<n>, seeded by migration 2801).
UPDATE r SET floor_id = f.floor_id
  FROM inv.room r JOIN mst.floor f ON f.property_id = r.property_id
   AND f.code = CONCAT('F', IIF(r.room_no NOT LIKE '%[^0-9]%' AND LEN(r.room_no) >= 3, IIF(CAST(r.room_no AS BIGINT) / 100 < 1, 1, CAST(r.room_no AS BIGINT) / 100), 1));
GO
CREATE TABLE inv.amenity(
  amenity_id INT IDENTITY CONSTRAINT PK_amenity PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_amenity_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  icon VARCHAR(30) NULL,
  show_on_website BIT NOT NULL CONSTRAINT DF_amenity_web DEFAULT 1,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_amenity_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_amenity_code UNIQUE (property_id, code),
  CONSTRAINT CK_amenity_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_amenity_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_amenity_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_amenity_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));

CREATE TABLE inv.occupancy_charge(
  charge_id INT IDENTITY CONSTRAINT PK_occupancy_charge PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_occupancy_charge_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  room_type_id INT NOT NULL CONSTRAINT FK_occupancy_charge_type REFERENCES inv.room_type,
  child_age_from TINYINT NULL,
  child_age_to TINYINT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_occupancy_charge_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_occupancy_charge_code UNIQUE (property_id, code),
  CONSTRAINT CK_occupancy_charge_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_occupancy_charge_ages CHECK ((child_age_from IS NULL OR child_age_from <= 17) AND (child_age_to IS NULL OR child_age_to <= 17)
    AND (child_age_from IS NULL OR child_age_to IS NULL OR child_age_from <= child_age_to)),
  CONSTRAINT CK_occupancy_charge_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
-- One active charge row per room type (retired rows keep their history).
CREATE UNIQUE INDEX UX_occupancy_charge_type ON inv.occupancy_charge(room_type_id) WHERE active = 1;

CREATE TABLE inv.occupancy_charge_rate(
  charge_id INT NOT NULL CONSTRAINT FK_occupancy_rate_charge REFERENCES inv.occupancy_charge ON DELETE CASCADE,
  kind VARCHAR(5) NOT NULL CONSTRAINT CK_occupancy_rate_kind CHECK (kind IN ('Adult','Child','Bed')),
  effective_from DATE NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_occupancy_rate_amount CHECK (amount_paise BETWEEN 0 AND 10000000000),
  CONSTRAINT PK_occupancy_charge_rate PRIMARY KEY (charge_id, kind, effective_from));
