-- HCC migration 2820 · MA-2 meal plans, extras catalogue and discount reasons (block 2820–2829). Plan: 02 Planning/
-- HCC_Customisation_Features_Plan_v1.md Part 2 §8 M6–M8; owner decisions DEC-MA-3 (meal plans now) and DEC-MA-4 (free-text charges stay
-- allowed for Reception, no approval; lines record catalogue vs free text). Prototype rules: 03 Prototype/src/domain/streams/catalogue.ts.
-- All three are money masters (DEC-058): every change needs PERM-MST-002 (Owner, migration 2802). Row rules as mst.floor (2801): code
-- ^[A-Z0-9-]{1,20}$ unique per property also for retired rows, immutable once used, name 1–60, retire instead of delete when used.
-- Dated amounts (add-on per guest per night, default price) live in *_price tables: one amount per effective date, amounts already in
-- effect are never changed (a new amount from the business date or later is added). History goes to mst.master_history.
-- Links (no column on tables owned by Billing / Reservations): bill.charge_catalogue (charge → extra item / meal plan / discount reason),
-- bill.approval_discount_reason (a reasoned discount waiting for approval), bill.reservation_meal_plan (booking → plan),
-- bill.meal_plan_night (one plan line per folio and night, repeat-safe night posting).
CREATE TABLE bill.meal_plan(
  meal_plan_id INT IDENTITY CONSTRAINT PK_meal_plan PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_meal_plan_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  meals VARCHAR(3) NOT NULL CONSTRAINT DF_meal_plan_meals DEFAULT '',          -- subset of B, L, D in that order ('' = room only)
  gst_treatment VARCHAR(8) NOT NULL CONSTRAINT CK_meal_plan_gst CHECK (gst_treatment IN ('bundled','separate')),   -- Illustrative — CA to confirm
  room_types NVARCHAR(400) NOT NULL CONSTRAINT DF_meal_plan_room_types DEFAULT '',  -- comma list of room type codes ('' = every type)
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_meal_plan_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_meal_plan_code UNIQUE (property_id, code),
  CONSTRAINT CK_meal_plan_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_meal_plan_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_meal_plan_meals CHECK (meals IN ('','B','L','D','BL','BD','LD','BLD')),
  CONSTRAINT CK_meal_plan_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_meal_plan_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE TABLE bill.meal_plan_price(
  meal_plan_id INT NOT NULL CONSTRAINT FK_meal_plan_price_plan REFERENCES bill.meal_plan ON DELETE CASCADE,
  effective_from DATE NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_meal_plan_price_amount CHECK (amount_paise BETWEEN 0 AND 10000000000),   -- per guest per night
  CONSTRAINT PK_meal_plan_price PRIMARY KEY (meal_plan_id, effective_from));

CREATE TABLE bill.extra_item(
  extra_item_id INT IDENTITY CONSTRAINT PK_extra_item PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_extra_item_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  category VARCHAR(12) NOT NULL CONSTRAINT CK_extra_item_category CHECK (category IN ('transport','laundry','fnb','room','misc')),
  gst_category VARCHAR(8) NOT NULL CONSTRAINT CK_extra_item_gst CHECK (gst_category IN ('food','laundry','banquet','other')),   -- GST master keys
  unit VARCHAR(8) NOT NULL CONSTRAINT CK_extra_item_unit CHECK (unit IN ('each','piece','bag','night','trip','hour','person')),
  department VARCHAR(12) NULL CONSTRAINT CK_extra_item_department CHECK (department IN ('frontdesk','housekeeping','laundry','restaurant','transport','banquet','maintenance')),
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_extra_item_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_extra_item_code UNIQUE (property_id, code),
  CONSTRAINT CK_extra_item_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_extra_item_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_extra_item_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_extra_item_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));
CREATE TABLE bill.extra_item_price(
  extra_item_id INT NOT NULL CONSTRAINT FK_extra_item_price_item REFERENCES bill.extra_item ON DELETE CASCADE,
  effective_from DATE NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_extra_item_price_amount CHECK (amount_paise BETWEEN 0 AND 10000000000),
  CONSTRAINT PK_extra_item_price PRIMARY KEY (extra_item_id, effective_from));

CREATE TABLE bill.discount_reason(
  discount_reason_id INT IDENTITY CONSTRAINT PK_discount_reason PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_discount_reason_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  max_bps INT NOT NULL CONSTRAINT CK_discount_reason_max CHECK (max_bps BETWEEN 1 AND 10000),          -- of the folio's net charges
  approval_above_bps INT NULL CONSTRAINT CK_discount_reason_approval CHECK (approval_above_bps BETWEEN 0 AND 10000),
  roles VARCHAR(20) NOT NULL CONSTRAINT DF_discount_reason_roles DEFAULT '',  -- comma list of OWN, MGR, REC ('' = every role that may discount)
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_discount_reason_active DEFAULT 1,
  retired_on DATE NULL,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT UQ_discount_reason_code UNIQUE (property_id, code),
  CONSTRAINT CK_discount_reason_code CHECK (LEN(code) BETWEEN 1 AND 20 AND code NOT LIKE '%[^A-Z0-9-]%' COLLATE Latin1_General_BIN),
  CONSTRAINT CK_discount_reason_name CHECK (LEN(name) BETWEEN 1 AND 60),
  CONSTRAINT CK_discount_reason_limits CHECK (approval_above_bps IS NULL OR approval_above_bps < max_bps),
  CONSTRAINT CK_discount_reason_sort CHECK (sort_order BETWEEN 0 AND 1000000),
  CONSTRAINT CK_discount_reason_retired CHECK ((active = 1 AND retired_on IS NULL) OR (active = 0 AND retired_on IS NOT NULL)));

-- Which master row a posted charge came from (absent = free text / plain line). Exactly one link per row.
CREATE TABLE bill.charge_catalogue(
  charge_id BIGINT NOT NULL CONSTRAINT PK_charge_catalogue PRIMARY KEY CONSTRAINT FK_charge_catalogue_charge REFERENCES bill.charge,
  extra_item_id INT NULL CONSTRAINT FK_charge_catalogue_extra REFERENCES bill.extra_item,
  meal_plan_id INT NULL CONSTRAINT FK_charge_catalogue_plan REFERENCES bill.meal_plan,
  discount_reason_id INT NULL CONSTRAINT FK_charge_catalogue_reason REFERENCES bill.discount_reason,
  quantity INT NULL CONSTRAINT CK_charge_catalogue_qty CHECK (quantity BETWEEN 1 AND 99),
  CONSTRAINT CK_charge_catalogue_one CHECK ((CASE WHEN extra_item_id IS NULL THEN 0 ELSE 1 END) + (CASE WHEN meal_plan_id IS NULL THEN 0 ELSE 1 END)
    + (CASE WHEN discount_reason_id IS NULL THEN 0 ELSE 1 END) = 1));
CREATE INDEX IX_charge_catalogue_extra ON bill.charge_catalogue(extra_item_id) WHERE extra_item_id IS NOT NULL;
CREATE INDEX IX_charge_catalogue_plan ON bill.charge_catalogue(meal_plan_id) WHERE meal_plan_id IS NOT NULL;
CREATE INDEX IX_charge_catalogue_reason ON bill.charge_catalogue(discount_reason_id) WHERE discount_reason_id IS NOT NULL;
CREATE TABLE bill.approval_discount_reason(
  approval_id BIGINT NOT NULL CONSTRAINT PK_approval_discount_reason PRIMARY KEY CONSTRAINT FK_approval_discount_reason_approval REFERENCES bill.approval,
  discount_reason_id INT NOT NULL CONSTRAINT FK_approval_discount_reason_reason REFERENCES bill.discount_reason);
CREATE INDEX IX_approval_discount_reason_reason ON bill.approval_discount_reason(discount_reason_id);
CREATE TABLE bill.reservation_meal_plan(
  reservation_id BIGINT NOT NULL CONSTRAINT PK_reservation_meal_plan PRIMARY KEY CONSTRAINT FK_reservation_meal_plan_reservation REFERENCES inv.reservation,
  meal_plan_id INT NOT NULL CONSTRAINT FK_reservation_meal_plan_plan REFERENCES bill.meal_plan,
  updated_by INT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_reservation_meal_plan_plan ON bill.reservation_meal_plan(meal_plan_id);
CREATE TABLE bill.meal_plan_night(
  folio_id BIGINT NOT NULL CONSTRAINT FK_meal_plan_night_folio REFERENCES bill.folio,
  service_date DATE NOT NULL,
  charge_id BIGINT NOT NULL CONSTRAINT FK_meal_plan_night_charge REFERENCES bill.charge,
  CONSTRAINT PK_meal_plan_night PRIMARY KEY (folio_id, service_date));
GO
-- Seed (prototype catSeedMealPlans / catSeedExtras / catSeedDiscountReasons; amounts from 1 Apr 2025) for every property that exists at
-- upgrade time; a property created later gets an empty catalogue until the Owner adds rows (or this batch is run for it).
DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
INSERT bill.meal_plan(property_id, code, name, meals, gst_treatment, sort_order, active, updated_by, updated_utc)
SELECT p.property_id, v.code, v.name, v.meals, v.gst, v.sort_order, 1, 0, @now
  FROM inv.property p CROSS JOIN (VALUES ('EP', N'Room only (EP)', '', 'separate', 10), ('CP', N'Breakfast included (CP)', 'B', 'bundled', 20),
       ('MAP', N'Breakfast and dinner (MAP)', 'BD', 'separate', 30), ('AP', N'All meals (AP)', 'BLD', 'separate', 40)) v(code, name, meals, gst, sort_order)
 WHERE NOT EXISTS (SELECT 1 FROM bill.meal_plan m WHERE m.property_id = p.property_id AND m.code = v.code);
INSERT bill.meal_plan_price(meal_plan_id, effective_from, amount_paise)
SELECT m.meal_plan_id, '2025-04-01', v.amount
  FROM bill.meal_plan m JOIN (VALUES ('EP', 0), ('CP', 30000), ('MAP', 75000), ('AP', 120000)) v(code, amount) ON v.code = m.code
 WHERE NOT EXISTS (SELECT 1 FROM bill.meal_plan_price x WHERE x.meal_plan_id = m.meal_plan_id);
INSERT bill.extra_item(property_id, code, name, category, gst_category, unit, department, sort_order, active, updated_by, updated_utc)
SELECT p.property_id, v.code, v.name, v.category, v.gst, v.unit, v.department, v.sort_order, 1, 0, @now
  FROM inv.property p CROSS JOIN (VALUES
       ('LAUNDRY', N'Laundry', 'laundry', 'laundry', 'bag', 'laundry', 10), ('DRYCLEAN', N'Dry cleaning', 'laundry', 'laundry', 'piece', 'laundry', 20),
       ('XBED', N'Extra bed', 'room', 'other', 'night', 'housekeeping', 30), ('PICKUP', N'Airport pickup', 'transport', 'other', 'trip', 'transport', 40),
       ('DROP', N'Airport drop', 'transport', 'other', 'trip', 'transport', 50), ('MINIBAR', N'Minibar', 'fnb', 'food', 'each', 'restaurant', 60),
       ('LATECO', N'Late checkout', 'misc', 'other', 'each', 'frontdesk', 70), ('HALL', N'Meeting hall', 'misc', 'banquet', 'hour', 'banquet', 80)
       ) v(code, name, category, gst, unit, department, sort_order)
 WHERE NOT EXISTS (SELECT 1 FROM bill.extra_item e WHERE e.property_id = p.property_id AND e.code = v.code);
INSERT bill.extra_item_price(extra_item_id, effective_from, amount_paise)
SELECT e.extra_item_id, '2025-04-01', v.amount
  FROM bill.extra_item e JOIN (VALUES ('LAUNDRY', 50000), ('DRYCLEAN', 25000), ('XBED', 100000), ('PICKUP', 120000), ('DROP', 120000), ('MINIBAR', 25000),
       ('LATECO', 100000), ('HALL', 250000)) v(code, amount) ON v.code = e.code
 WHERE NOT EXISTS (SELECT 1 FROM bill.extra_item_price x WHERE x.extra_item_id = e.extra_item_id);
INSERT bill.discount_reason(property_id, code, name, max_bps, approval_above_bps, roles, sort_order, active, updated_by, updated_utc)
SELECT p.property_id, v.code, v.name, v.max_bps, v.approval_bps, v.roles, v.sort_order, 1, 0, @now
  FROM inv.property p CROSS JOIN (VALUES ('SERVICE', N'Service recovery', 2500, 1000, '', 10), ('CORP', N'Corporate rate', 2000, NULL, '', 20),
       ('REPEAT', N'Repeat guest', 1000, NULL, '', 30), ('LONGSTAY', N'Long stay', 1500, NULL, 'OWN,MGR', 40),
       ('MGMT', N'Management courtesy', 10000, NULL, 'OWN,MGR', 50)) v(code, name, max_bps, approval_bps, roles, sort_order)
 WHERE NOT EXISTS (SELECT 1 FROM bill.discount_reason d WHERE d.property_id = p.property_id AND d.code = v.code);
