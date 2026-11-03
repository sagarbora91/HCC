-- HCC migration 4333 · F6 hall booking menu packages (lane H2 schema; the master command HALL_MENU_UPSERT is H1's, spec §12): a package has a
-- price per plate and is made of POS menu items (pos.menu_item codes, kept as text so a retired item never breaks an old package).
CREATE TABLE hall.menu_package(
  menu_id INT IDENTITY CONSTRAINT PK_hall_menu_package PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_menu_package_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(60) NOT NULL,
  per_plate_paise BIGINT NOT NULL CONSTRAINT CK_hall_menu_package_rate CHECK (per_plate_paise > 0),
  veg BIT NOT NULL CONSTRAINT DF_hall_menu_package_veg DEFAULT 1,
  active BIT NOT NULL CONSTRAINT DF_hall_menu_package_active DEFAULT 1,
  version INT NOT NULL CONSTRAINT DF_hall_menu_package_version DEFAULT 1,
  updated_by INT NULL,
  updated_utc DATETIME2(3) NOT NULL CONSTRAINT DF_hall_menu_package_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT UX_hall_menu_package_code UNIQUE (property_id, code),
  CONSTRAINT UX_hall_menu_package_name UNIQUE (property_id, name));
CREATE TABLE hall.menu_package_item(
  menu_id INT NOT NULL CONSTRAINT FK_hall_menu_package_item_menu REFERENCES hall.menu_package,
  seq INT NOT NULL,
  pos_item_code VARCHAR(40) NOT NULL,
  CONSTRAINT PK_hall_menu_package_item PRIMARY KEY (menu_id, seq));
