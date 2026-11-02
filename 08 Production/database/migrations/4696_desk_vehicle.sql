-- HCC migration 4696 · LF A4 vehicle number and type (LF-CONTRACT §8.2). mst.vehicle_type (code + name, Owner / Tech admin) and the booking's vehicle:
-- vehicle_no = upper-case, 4–13 characters of A–Z 0–9 - space (the application normalises; the check keeps the column honest), vehicle_type = the master's name.
-- The index serves the In-house / Arrivals and Customers search by vehicle number. Legacy names: tools/legacy/extract.py --vehicle-types.
CREATE TABLE mst.vehicle_type(
  row_id INT IDENTITY CONSTRAINT PK_mst_vehicle_type PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_mst_vehicle_type_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_vehicle_type_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_mst_vehicle_type_code UNIQUE (property_id, code),
  CONSTRAINT CK_mst_vehicle_type_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_mst_vehicle_type_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_mst_vehicle_type_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_mst_vehicle_type_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_mst_vehicle_type_order ON mst.vehicle_type(property_id, sort_order);
ALTER TABLE inv.reservation ADD vehicle_no VARCHAR(13) NULL, vehicle_type NVARCHAR(40) NULL;
GO
CREATE INDEX IX_reservation_vehicle ON inv.reservation(property_id, vehicle_no) WHERE vehicle_no IS NOT NULL;
