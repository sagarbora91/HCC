-- HCC migration 2870 · MA-7 housekeeping and guest-services masters (block 2870–2879). Plan: 02 Planning/HCC_Customisation_Features_Plan_v1.md
-- Part 2 §8 M23 (housekeeping task types), M24 (zones), M25 (complaint and lost-and-found categories). Prototype rules:
-- 03 Prototype/src/domain/streams/hkmasters.ts + gsvmasters.ts. Row rules as the floors master (2801): code unique per property (also for
-- retired rows) and immutable once used, name 1–60, sort order, retire (active = 0 + retired_on) instead of deleting a used row.
-- No money fields anywhere in this block (DEC-061: housekeeping and maintenance read these lists). Changed through PERM-MST-001
-- (Owner, Tech admin); change history in mst.master_history (master = hkTaskTypes | hkZones | complaintCategories | lostFoundCategories).
IF SCHEMA_ID('gsv') IS NULL EXEC('CREATE SCHEMA gsv');
GO
-- Task types: which task kinds (checkout, move, stayover) or roles (inspection, deep) a type serves, standard minutes (attendant load and
-- board timing), whether a finished clean waits for the supervisor, and the checklist (JSON array of 1–8 items; a task keeps one tick per
-- item in ops.hk_task.checklist_mask, widened to 8 bits below).
CREATE TABLE ops.hk_task_type(
  task_type_id INT IDENTITY CONSTRAINT PK_hk_task_type PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hk_task_type_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_hk_task_type_active DEFAULT 1,
  retired_on DATE NULL,
  applies_to VARCHAR(80) NOT NULL,
  standard_minutes SMALLINT NOT NULL,
  needs_inspection BIT NOT NULL,
  checklist_json NVARCHAR(1000) NOT NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_hk_task_type_code UNIQUE (property_id, code),
  CONSTRAINT CK_hk_task_type_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_hk_task_type_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_hk_task_type_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_hk_task_type_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)),
  CONSTRAINT CK_hk_task_type_minutes CHECK (standard_minutes BETWEEN 5 AND 240),
  CONSTRAINT CK_hk_task_type_applies CHECK (LEN(applies_to) > 0 AND applies_to NOT LIKE '%[^a-z,]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_hk_task_type_checklist CHECK (ISJSON(checklist_json) = 1));
CREATE INDEX IX_hk_task_type_order ON ops.hk_task_type(property_id, sort_order);

-- Zones: a set of floors (codes of mst.floor, comma separated) and/or single rooms (room numbers, comma separated) with a default attendant.
CREATE TABLE ops.hk_zone(
  zone_id INT IDENTITY CONSTRAINT PK_hk_zone PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hk_zone_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_hk_zone_active DEFAULT 1,
  retired_on DATE NULL,
  floor_codes VARCHAR(200) NOT NULL CONSTRAINT DF_hk_zone_floors DEFAULT '',
  room_nos VARCHAR(400) NOT NULL CONSTRAINT DF_hk_zone_rooms DEFAULT '',
  default_attendant_id INT NULL CONSTRAINT FK_hk_zone_attendant REFERENCES plat.app_user,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_hk_zone_code UNIQUE (property_id, code),
  CONSTRAINT CK_hk_zone_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_hk_zone_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_hk_zone_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_hk_zone_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)),
  CONSTRAINT CK_hk_zone_members CHECK (LEN(floor_codes) > 0 OR LEN(room_nos) > 0));
CREATE INDEX IX_hk_zone_order ON ops.hk_zone(property_id, sort_order);

-- Complaint categories: department that normally handles it and the SLA in hours (answer or resolve within).
CREATE TABLE gsv.complaint_category(
  category_id INT IDENTITY CONSTRAINT PK_gsv_complaint_category PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_gsv_complaint_category_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_gsv_complaint_category_active DEFAULT 1,
  retired_on DATE NULL,
  department VARCHAR(12) NOT NULL,
  sla_hours SMALLINT NOT NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_gsv_complaint_category_code UNIQUE (property_id, code),
  CONSTRAINT CK_gsv_complaint_category_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_gsv_complaint_category_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_gsv_complaint_category_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_gsv_complaint_category_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)),
  CONSTRAINT CK_gsv_complaint_category_dept CHECK (department IN ('Housekeeping','Maintenance','Front desk')),
  CONSTRAINT CK_gsv_complaint_category_sla CHECK (sla_hours BETWEEN 1 AND 720));
CREATE INDEX IX_gsv_complaint_category_order ON gsv.complaint_category(property_id, sort_order);

-- Lost-and-found categories: as above plus "valuable" (held in the front-office safe, disposal needs a manager).
CREATE TABLE gsv.lostfound_category(
  category_id INT IDENTITY CONSTRAINT PK_gsv_lostfound_category PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_gsv_lostfound_category_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_gsv_lostfound_category_active DEFAULT 1,
  retired_on DATE NULL,
  department VARCHAR(12) NOT NULL,
  sla_hours SMALLINT NOT NULL,
  valuable BIT NOT NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_gsv_lostfound_category_code UNIQUE (property_id, code),
  CONSTRAINT CK_gsv_lostfound_category_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_gsv_lostfound_category_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_gsv_lostfound_category_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_gsv_lostfound_category_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)),
  CONSTRAINT CK_gsv_lostfound_category_dept CHECK (department IN ('Housekeeping','Maintenance','Front desk')),
  CONSTRAINT CK_gsv_lostfound_category_sla CHECK (sla_hours BETWEEN 1 AND 720));
CREATE INDEX IX_gsv_lostfound_category_order ON gsv.lostfound_category(property_id, sort_order);
GO
-- A task keeps one tick per checklist item of its type (1–8 items): the 3-bit mask of 0300 is widened to the 8 bits of the column.
ALTER TABLE ops.hk_task DROP CONSTRAINT CK_hk_task_checklist;
ALTER TABLE ops.hk_task ADD CONSTRAINT CK_hk_task_checklist CHECK (checklist_mask BETWEEN 0 AND 255);

-- Complaints and found items keep the category name as logged (the snapshot people read) and, new, the master row's code; the fixed
-- vocabularies of 1500 (CHECK on the name) give way to the masters above. Older rows get their code by name in 2871.
ALTER TABLE ops.guest_complaint DROP CONSTRAINT CK_guest_complaint_category;
ALTER TABLE ops.guest_complaint ALTER COLUMN category NVARCHAR(60) NOT NULL;
ALTER TABLE ops.guest_complaint ADD category_code VARCHAR(20) NULL;
ALTER TABLE ops.lost_item DROP CONSTRAINT CK_lost_item_category;
ALTER TABLE ops.lost_item ALTER COLUMN category NVARCHAR(60) NOT NULL;
ALTER TABLE ops.lost_item ADD category_code VARCHAR(20) NULL;
