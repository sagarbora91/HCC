-- HCC migration 2840 · MA-4 POS masters (block 2840–2849). Plan: 02 Planning/HCC_Customisation_Features_Plan_v1.md Part 2 §8 F
-- (M26 outlets, M27 tables and counters, M28 menu categories and items) on the masters framework (§9, MA-0 migration 2801).
-- Prototype rules: 03 Prototype/src/domain/streams/pos-master.ts. Row rules: code ^[A-Z0-9-]{1,20}$ unique per property (retired rows
-- keep their code), name 1–60, sort order, active / retired_on (retire, never delete a used row). History goes to mst.master_history
-- (master ids 'outlets', 'posTables', 'menuCategories', 'menuItems'). Permissions: PERM-MST-001 (lists; Owner, Tech admin) and
-- PERM-MST-002 (price, GST class, outlet GST profile; Owner) from 2802 — no new PERM ids. GST rates stay in the GST master (DEC-069);
-- the outlet profile only picks "hotel status" / standard / specified — illustrative, CA to confirm.
--
--   pos.outlet         + outlet_type (restaurant / room-service / bar / banquet; room-service ⇔ kind 'room'), opens_at / closes_at,
--                        gst_profile (hotel / standard / specified), kot_station, sort_order, active, retired_on, updated_by / updated_utc
--   pos.outlet_table   + table_id (new primary key), code, name, seats, section, is_counter, sort_order, active, retired_on, row_ver;
--                        one ACTIVE table number per outlet (a retired T5 may be replaced by a new T5)
--   pos.menu_category  new (seeded from the distinct menu_item.category texts: CAT-01, CAT-02 … in name order)
--   pos.menu_item      + category_id, short_code, gst_class (food / laundry / banquet / other), station, sort_order, active, retired_on,
--                        sold_out_on ("sold out today": available = 0 lapses after that business date), updated_by / updated_utc
--   pos.menu_price     new: dated prices (the price in force on a date = the latest effective_from ≤ date); every existing item gets its
--                        current price from 2017-07-01, so every figure stays as it was
--   pos.order_line     + gst_class snapshot (NULL = food) — one GST class per check
--   pos.kot            + station (NULL = no station configured: one KOT per send as before)
ALTER TABLE pos.outlet ADD
  outlet_type VARCHAR(12) NOT NULL CONSTRAINT DF_pos_outlet_type DEFAULT 'restaurant',
  opens_at CHAR(5) NULL,
  closes_at CHAR(5) NULL,
  gst_profile VARCHAR(9) NOT NULL CONSTRAINT DF_pos_outlet_gst DEFAULT 'hotel',
  kot_station NVARCHAR(20) NULL,
  sort_order INT NOT NULL CONSTRAINT DF_pos_outlet_sort DEFAULT 0,
  active BIT NOT NULL CONSTRAINT DF_pos_outlet_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL;
GO
UPDATE pos.outlet SET outlet_type = IIF(kind = 'room', 'room-service', 'restaurant');
UPDATE o SET sort_order = x.n * 10
  FROM pos.outlet o JOIN (SELECT outlet_id, ROW_NUMBER() OVER (PARTITION BY property_id ORDER BY outlet_id) AS n FROM pos.outlet) x ON x.outlet_id = o.outlet_id;
ALTER TABLE pos.outlet ADD
  CONSTRAINT CK_pos_outlet_type CHECK (outlet_type IN ('restaurant','room-service','bar','banquet')),
  CONSTRAINT CK_pos_outlet_type_kind CHECK ((outlet_type = 'room-service' AND kind = 'room') OR (outlet_type <> 'room-service' AND kind = 'table')),
  CONSTRAINT CK_pos_outlet_gst CHECK (gst_profile IN ('hotel','standard','specified')),
  CONSTRAINT CK_pos_outlet_times CHECK ((opens_at IS NULL OR opens_at LIKE '[0-2][0-9]:[0-5][0-9]') AND (closes_at IS NULL OR closes_at LIKE '[0-2][0-9]:[0-5][0-9]')),
  CONSTRAINT CK_pos_outlet_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_pos_outlet_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL));
GO
-- Tables and counters: a surrogate key so a table number can be retired and reused; the old (outlet, table_no) key becomes "one active".
ALTER TABLE pos.outlet_table ADD
  table_id INT IDENTITY(1,1) NOT NULL,
  code VARCHAR(20) NULL,
  name NVARCHAR(60) NULL,
  seats TINYINT NULL,
  section NVARCHAR(40) NULL,
  is_counter BIT NOT NULL CONSTRAINT DF_pos_outlet_table_counter DEFAULT 0,
  sort_order INT NOT NULL CONSTRAINT DF_pos_outlet_table_sort DEFAULT 0,
  active BIT NOT NULL CONSTRAINT DF_pos_outlet_table_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL,
  row_ver ROWVERSION;
GO
UPDATE t SET code = LEFT(UPPER(REPLACE(o.code + '-' + t.table_no, ' ', '-')), 20), name = LEFT(o.name + N' ' + t.table_no, 60),
       sort_order = x.n * 10
  FROM pos.outlet_table t
  JOIN pos.outlet o ON o.outlet_id = t.outlet_id
  JOIN (SELECT outlet_id, table_no, ROW_NUMBER() OVER (PARTITION BY outlet_id ORDER BY LEN(table_no), table_no) AS n FROM pos.outlet_table) x
    ON x.outlet_id = t.outlet_id AND x.table_no = t.table_no;
ALTER TABLE pos.outlet_table ALTER COLUMN code VARCHAR(20) NOT NULL;
ALTER TABLE pos.outlet_table ALTER COLUMN name NVARCHAR(60) NOT NULL;
ALTER TABLE pos.outlet_table DROP CONSTRAINT PK_pos_outlet_table;
GO
ALTER TABLE pos.outlet_table ADD
  CONSTRAINT PK_pos_outlet_table PRIMARY KEY (table_id),
  CONSTRAINT CK_pos_outlet_table_seats CHECK (seats IS NULL OR seats BETWEEN 1 AND 60),
  CONSTRAINT CK_pos_outlet_table_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_pos_outlet_table_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL));
CREATE UNIQUE INDEX UX_pos_outlet_table_active ON pos.outlet_table(outlet_id, table_no) WHERE active = 1;
CREATE INDEX IX_pos_outlet_table_code ON pos.outlet_table(code);
GO
CREATE TABLE pos.menu_category(
  category_id INT IDENTITY CONSTRAINT PK_pos_menu_category PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_pos_menu_category_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_pos_menu_category_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_pos_menu_category_code UNIQUE (property_id, code),
  CONSTRAINT CK_pos_menu_category_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_pos_menu_category_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_pos_menu_category_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_pos_menu_category_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE INDEX IX_pos_menu_category_order ON pos.menu_category(property_id, sort_order);
GO
ALTER TABLE pos.menu_item ADD
  category_id INT NULL CONSTRAINT FK_pos_menu_item_category REFERENCES pos.menu_category,
  short_code VARCHAR(10) NULL,
  gst_class VARCHAR(7) NOT NULL CONSTRAINT DF_pos_menu_item_gst DEFAULT 'food',
  station NVARCHAR(20) NULL,
  sort_order INT NOT NULL CONSTRAINT DF_pos_menu_item_sort DEFAULT 0,
  active BIT NOT NULL CONSTRAINT DF_pos_menu_item_active DEFAULT 1,
  retired_on DATE NULL,
  sold_out_on DATE NULL,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NULL;
GO
INSERT pos.menu_category(property_id, code, name, sort_order, active)
SELECT c.property_id, CONCAT('CAT-', FORMAT(c.n, '00')), LEFT(c.category, 60), c.n * 10, 1
  FROM (SELECT property_id, category, ROW_NUMBER() OVER (PARTITION BY property_id ORDER BY category) AS n
          FROM (SELECT DISTINCT property_id, category FROM pos.menu_item) d) c;
UPDATE m SET category_id = c.category_id
  FROM pos.menu_item m JOIN pos.menu_category c ON c.property_id = m.property_id AND c.name = LEFT(m.category, 60);
UPDATE m SET sort_order = x.n * 10
  FROM pos.menu_item m JOIN (SELECT item_id, ROW_NUMBER() OVER (PARTITION BY property_id ORDER BY category, name, item_id) AS n FROM pos.menu_item) x ON x.item_id = m.item_id;
ALTER TABLE pos.menu_item ADD
  CONSTRAINT CK_pos_menu_item_gst CHECK (gst_class IN ('food','laundry','banquet','other')),
  CONSTRAINT CK_pos_menu_item_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_pos_menu_item_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL));
CREATE INDEX IX_pos_menu_item_category ON pos.menu_item(category_id);
GO
CREATE TABLE pos.menu_price(
  price_id BIGINT IDENTITY CONSTRAINT PK_pos_menu_price PRIMARY KEY,
  item_id INT NOT NULL CONSTRAINT FK_pos_menu_price_item REFERENCES pos.menu_item,
  effective_from DATE NOT NULL,
  price_paise BIGINT NOT NULL CONSTRAINT CK_pos_menu_price_amount CHECK (price_paise BETWEEN 0 AND 10000000000),
  created_by INT NULL,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_pos_menu_price UNIQUE (item_id, effective_from));
GO
INSERT pos.menu_price(item_id, effective_from, price_paise, created_by, created_utc)
SELECT item_id, '2017-07-01', price_paise, NULL, SYSUTCDATETIME() FROM pos.menu_item;
ALTER TABLE pos.order_line ADD gst_class VARCHAR(7) NULL CONSTRAINT CK_pos_line_gst CHECK (gst_class IS NULL OR gst_class IN ('laundry','banquet','other'));
ALTER TABLE pos.kot ADD station NVARCHAR(20) NULL;
GO
